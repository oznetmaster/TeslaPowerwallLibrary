// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Google.Protobuf;
using Classic = TeslaPowerwallLibrary.Tedapi.Protocol.Classic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using System.Text.Json;
using System.Text.Json.Serialization;

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Local;

/// <summary>
/// Local-mode Powerwall™ client that communicates directly with a Tesla™ Energy Gateway over HTTPS.
/// Faithfully adapts the behavior of the Python <c>PyPowerwallLocal</c> class using an async,
/// strongly-typed, cancellable API surface.
/// </summary>
public sealed class PowerwallLocalClient : PowerwallClientBase, IDisposable
	{
	private readonly ILogger _log;
	private readonly SemaphoreSlim _requestGate = new (1, 1);
	private readonly HttpMessageHandler? _providedHandler;
	private readonly bool _persistSession = true;
	private readonly bool _allowControl = true;

	private readonly string _host;
	private readonly string _password;
	private readonly string _timezone;
	private readonly TimeSpan _timeout;
	private readonly int _cacheExpireSeconds;
	private readonly string _cacheFile;
	private readonly Stopwatch _clock = Stopwatch.StartNew ();
	private readonly Dictionary<string, double> _cacheTimes = [];

	private string _authMode;
	private HttpClient? _httpClient;
	private CookieContainer? _cookies;
	private string? _authorizationHeader;
	private bool _hasAuth;
	private double _cooldownUntil;
	private bool _vitalsApiAvailable = true;

	/// <summary>
	/// Initializes a new instance of the <see cref="PowerwallLocalClient"/> class.
	/// </summary>
	/// <param name="host">Hostname or IP address of the gateway, optionally including a <c>:port</c> suffix.</param>
	/// <param name="password">Customer password configured on the gateway.</param>
	/// <param name="email">Customer email.</param>
	/// <param name="timezone">IANA time zone reported to the gateway in client info.</param>
	/// <param name="timeout">Per-request HTTP timeout.</param>
	/// <param name="cacheExpireSeconds">Number of seconds before cached responses expire.</param>
	/// <param name="authMode">Authentication mode: <c>cookie</c> (default) or <c>token</c>.</param>
	/// <param name="cacheFile">Path to the file used to persist the authentication session.</param>
	/// <exception cref="ArgumentException">Thrown when <paramref name="host"/> is null or whitespace.</exception>
	public PowerwallLocalClient (
		string host,
		string password,
		string email,
		string timezone,
		TimeSpan timeout,
		int cacheExpireSeconds,
		string authMode,
		string cacheFile)
		: this (host, password, email, timezone, timeout, cacheExpireSeconds, authMode, cacheFile, null)
		{
		}

	/// <summary>Creates a client using a caller-owned logger. The client never disposes the logger.</summary>
	/// <param name="host">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="password">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="email">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="timezone">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="timeout">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="cacheExpireSeconds">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="authMode">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="cacheFile">See the corresponding parameter of the default-logging constructor.</param>
	/// <param name="logger">Logger carrying the caller's category and scopes; null disables logging.</param>
	public PowerwallLocalClient (
		string host,
		string password,
		string email,
		string timezone,
		TimeSpan timeout,
		int cacheExpireSeconds,
		string authMode,
		string cacheFile,
		ILogger? logger)
		: base (email)
		{
		_log = logger ?? NullLogger.Instance;
		if (string.IsNullOrWhiteSpace (host))
			throw new ArgumentException ("Host is required for local mode.", nameof (host));

		_host = LocalEndpoint.Create (host).Authority;
		_password = password ?? string.Empty;
		_timezone = string.IsNullOrWhiteSpace (timezone) ? Constants.DEFAULT_TIMEZONE : timezone;
		_timeout = timeout;
		_cacheExpireSeconds = cacheExpireSeconds;
		_authMode = authMode is "cookie" or "token" ? authMode : "cookie";
		_cacheFile = string.IsNullOrWhiteSpace (cacheFile) ? Constants.DEFAULT_CACHE_FILE : cacheFile;
		}

		/// <summary>Creates a local gateway client with explicit session-persistence and control settings.</summary>
	/// <param name="options">Local connection settings; no cloud credentials are used.</param>
	public PowerwallLocalClient (PowerwallOptions options) : this (options, null)
		{
		}

	/// <summary>Creates a local gateway client with a deterministic test transport.</summary>
	/// <param name="options">Local connection settings.</param>
	/// <param name="handler">Optional test handler, owned by the client.</param>
	internal PowerwallLocalClient (PowerwallOptions options, HttpMessageHandler? handler)
		: this (options.Host, options.Password, options.Email, options.Timezone, options.Timeout,
			options.CacheExpireSeconds, options.AuthMode, options.CacheFile, options.Logger)
		{
		_providedHandler = handler;
		_persistSession = !options.NoLocalSessionPersistence;
		_allowControl = options.AllowLocalControl;
		}

	private void EnsureAuthenticated ()
		{
		if (_httpClient is null || !_hasAuth)
			throw new PowerwallConnectionException ("Authenticate with the local gateway before requesting data or sending commands.");
		}
	private double NowSeconds => _clock.Elapsed.TotalSeconds;


	/// <inheritdoc/>
	public override async Task AuthenticateAsync (CancellationToken cancellationToken = default)
		{
		await _requestGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			LibraryLog.TeslaLocalModeEnabled (_log);
			bool firstConnection = _httpClient is null;
			if (firstConnection)
				{
				_cookies = new CookieContainer ();
				HttpMessageHandler handler = _providedHandler ?? new HttpClientHandler
					{
					CookieContainer = _cookies,
					AllowAutoRedirect = false,
					UseProxy = false,
					// This private handler only accesses the configured local gateway.
					ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
					};
				_httpClient = new HttpClient (handler) { Timeout = _timeout, MaxResponseContentBufferSize = 8 * 1024 * 1024 };
				}
			_hasAuth = false;
			_authorizationHeader = null;
			Token = null;
			Cache.Clear ();
			if (firstConnection && _persistSession)
				LoadCachedAuth ();
			if (!_hasAuth)
				await GetSessionAsync (cancellationToken).ConfigureAwait (false);
			}
		finally { _requestGate.Release (); }
		}

	/// <inheritdoc/>
	public override async Task CloseSessionAsync (CancellationToken cancellationToken = default)
		{
		await _requestGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			if (_httpClient is not null && _hasAuth)
				{
				try
					{
					using HttpRequestMessage request = CreateRequest (HttpMethod.Get, $"https://{_host}/api/logout");
					using HttpResponseMessage response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
					}
				catch (Exception exc) when (!cancellationToken.IsCancellationRequested && exc is HttpRequestException or TaskCanceledException)
					{
					LibraryLog.ErrorDuringLogout (_log, exc.Message);
					}
				}
			}
		finally
			{
			_hasAuth = false;
			_authorizationHeader = null;
			Token = null;
			Cache.Clear ();
			_requestGate.Release ();
			}
		}

	/// <inheritdoc/>
	public override async Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		await _requestGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			return await PollCoreAsync (api, force, recursive, cancellationToken).ConfigureAwait (false);
			}
		finally
			{
			_requestGate.Release ();
			}
		}

	/// <inheritdoc/>
	public override async Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		await _requestGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			return await PollRawCoreAsync (api, force, recursive, cancellationToken).ConfigureAwait (false);
			}
		finally
			{
			_requestGate.Release ();
			}
		}
	/// <inheritdoc/>
	private async Task<string?> PollCoreAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		cancellationToken.ThrowIfCancellationRequested ();
		EnsureAuthenticated ();
		if (TryGetCached (api, out var cached) && !force)
			return cached;

		if (_cooldownUntil > NowSeconds || (_cacheTimes.TryGetValue (api, out var retryAfter) && retryAfter > NowSeconds))
			{
			LibraryLog.RateLimitCooldownPeriodPausingAPICalls (_log);
			return null;
			}

		EnsureAuthenticated ();
		var url = $"https://{_host}{api}";
		HttpResponseMessage response;
		try
			{
			using HttpRequestMessage request = CreateRequest (HttpMethod.Get, url);
			response = await _httpClient!.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			LibraryLog.TimeoutWaitingForPowerwallAPICheckNetworkConnectivityTo (_log, api, _host);
			return null;
			}
		catch (HttpRequestException exc)
			{
			LibraryLog.UnableToConnectToPowerwallAtCheckThatThe (_log, _host, exc.Message);
			return null;
			}

		using (response)
			{
			(bool Handled, bool Retry) statusHandling = await HandleStatusAsync (api, url, response, recursive, raw: false, cancellationToken).ConfigureAwait (false);
			if (statusHandling.Handled)
				return statusHandling.Retry ? await PollCoreAsync (api, force, recursive: true, cancellationToken).ConfigureAwait (false) : null;

#if NETFRAMEWORK
			var body = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
#else
			var body = await response.Content.ReadAsStringAsync (cancellationToken).ConfigureAwait (false);
#endif
			if (string.IsNullOrEmpty (body))
				{
				LibraryLog.EmptyResponseFromPowerwallAt (_log, url);
				return null;
				}

			StoreCache (api, body);
			return body;
			}
		}

	/// <inheritdoc/>
	private async Task<byte[]?> PollRawCoreAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		if (api == "/api/devices/vitals" && !_vitalsApiAvailable)
			return null;

		if (_cooldownUntil > NowSeconds || (_cacheTimes.TryGetValue (api, out var retryAfter) && retryAfter > NowSeconds))
			{
			LibraryLog.RateLimitCooldownPeriodPausingAPICalls (_log);
			return null;
			}

		EnsureAuthenticated ();
		var url = $"https://{_host}{api}";
		HttpResponseMessage response;
		try
			{
			using HttpRequestMessage request = CreateRequest (HttpMethod.Get, url);
			response = await _httpClient!.SendAsync (request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			LibraryLog.TimeoutWaitingForPowerwallAPICheckNetworkConnectivityTo (_log, api, _host);
			return null;
			}
		catch (HttpRequestException exc)
			{
			LibraryLog.UnableToConnectToPowerwallAtCheckThatThe (_log, _host, exc.Message);
			return null;
			}

		using (response)
			{
			(bool Handled, bool Retry) statusHandling = await HandleStatusAsync (api, url, response, recursive, raw: true, cancellationToken).ConfigureAwait (false);
			if (statusHandling.Handled)
				return statusHandling.Retry ? await PollRawCoreAsync (api, force, recursive: true, cancellationToken).ConfigureAwait (false) : null;

			const int maximumBytes = 8 * 1024 * 1024;
			if (response.Content.Headers.ContentLength > maximumBytes)
				throw new PowerwallConnectionException ("The local binary response exceeds the permitted size.");
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
			deadline.CancelAfter (_timeout);
#if NETFRAMEWORK
			using Stream stream = await response.Content.ReadAsStreamAsync ().ConfigureAwait (false);
#else
			using Stream stream = await response.Content.ReadAsStreamAsync (deadline.Token).ConfigureAwait (false);
#endif
			using var output = new MemoryStream ();
			var buffer = new byte[8192];
			int read;
#if NETFRAMEWORK
			while ((read = await stream.ReadAsync (buffer, 0, buffer.Length, deadline.Token).ConfigureAwait (false)) > 0)
#else
			while ((read = await stream.ReadAsync (buffer.AsMemory (), deadline.Token).ConfigureAwait (false)) > 0)
#endif
				{
				if (output.Length + read > maximumBytes)
					throw new PowerwallConnectionException ("The local binary response exceeds the permitted size.");
				output.Write (buffer, 0, read);
				}
			return output.ToArray ();
			}
		}


	/// <inheritdoc/>
	public override async Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default)
		{
		await _requestGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			return await PostCoreAsync (api, payload, din, recursive, cancellationToken).ConfigureAwait (false);
			}
		finally
			{
			// The gateway may have applied a command even when its response was lost.
			Cache.Clear ();
			_requestGate.Release ();
			}
		}

	private async Task<string?> PostCoreAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (!_allowControl)
			{
			throw new PowerwallNotSupportedException ("Local control is disabled. Enable AllowLocalControl only when commands are intended.");
			}

		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		EnsureAuthenticated ();
		var url = $"https://{_host}{api}";
		HttpResponseMessage response;
		try
			{
			using HttpRequestMessage request = CreateRequest (HttpMethod.Post, url);
			if (payload is not null)
				{
				var json = JsonHelper.Serialize (payload);
				request.Content = new StringContent (json, Encoding.UTF8, "application/json");
				}

			response = await _httpClient!.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			LibraryLog.ERRORTimeoutWaitingForPowerwallAPI (_log, url);
			return null;
			}
		catch (HttpRequestException exc)
			{
			LibraryLog.ERRORUnableToConnectToPowerwallAt (_log, url, exc.Message);
			return null;
			}

		using (response)
			{
			(bool Handled, bool Retry) statusHandling = await HandleStatusAsync (api, url, response, recursive, raw: false, cancellationToken).ConfigureAwait (false);
			if (statusHandling.Handled)
				{
				if (statusHandling.Retry)
					return await PostCoreAsync (api, payload, din, recursive: true, cancellationToken).ConfigureAwait (false);
				return null;
				}

#if NETFRAMEWORK
			var body = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
#else
			var body = await response.Content.ReadAsStringAsync (cancellationToken).ConfigureAwait (false);
#endif
			InvalidateCache (api);
			return string.IsNullOrEmpty (body) ? null : body;
			}
		}

	/// <inheritdoc/>
	public override async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default)
		{
		byte[]? bytes = await PollRawAsync ("/api/devices/vitals", cancellationToken: cancellationToken).ConfigureAwait (false);
		if (bytes is null)
			return null;
		Classic.DevicesWithVitals devices;
		try { devices = Classic.DevicesWithVitals.Parser.ParseFrom (bytes); }
		catch (InvalidProtocolBufferException exc)
			{
			throw new PowerwallConnectionException ("The local gateway returned malformed device vitals.", exc);
			}
		var result = new Dictionary<string, IReadOnlyDictionary<string, object?>> (StringComparer.Ordinal);
		foreach (Classic.SiteControllerConnectedDeviceWithVitals item in devices.Devices)
			{
			Classic.Device? device = item.Device?.Device;
			if (string.IsNullOrWhiteSpace (device?.Din?.Value))
				continue;
			var values = new Dictionary<string, object?> (StringComparer.Ordinal)
				{
				["partNumber"] = device!.PartNumber?.Value,
				["serialNumber"] = device.SerialNumber?.Value,
				["manufacturer"] = device.Manufacturer?.Value,
				["siteLabel"] = device.SiteLabel?.Value,
				["componentParentDin"] = device.ComponentParentDin?.Value,
				["firmwareVersion"] = device.FirmwareVersion?.Value,
				["alerts"] = item.Alerts.ToArray ()
				};
			foreach (Classic.DeviceVital vital in item.Vitals)
				{
				if (string.IsNullOrWhiteSpace (vital.Name))
					continue;
				values[vital.Name] = vital.ValueCase switch
					{
					Classic.DeviceVital.ValueOneofCase.IntValue => vital.IntValue,
					Classic.DeviceVital.ValueOneofCase.FloatValue => vital.FloatValue,
					Classic.DeviceVital.ValueOneofCase.StringValue => vital.StringValue,
					Classic.DeviceVital.ValueOneofCase.BoolValue => vital.BoolValue,
					_ => null
					};
				}
			result[device.Din.Value] = values;
			}
		return result;
		}

	/// <inheritdoc/>
	public override async Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default)
		{
		var payload = await PollAsync ("/api/system_status", cancellationToken: cancellationToken).ConfigureAwait (false);
		SystemStatus? status = JsonHelper.DeserializeOrNull<SystemStatus> (payload);
		if (status?.NominalEnergyRemaining is double remaining)
			{
			var load = await FetchPowerAsync ("load", cancellationToken: cancellationToken).ConfigureAwait (false) ?? 0.0;
			if (load > 0)
				return remaining / load;
			}

		return null;
		}

	/// <summary>
	/// Returns the gateway firmware version string from <c>/api/status</c>.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The version string, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> GetVersionAsync (CancellationToken cancellationToken = default)
		{
		GatewayStatus? status = await GetStatusAsync (cancellationToken).ConfigureAwait (false);
		return status?.Version;
		}

	/// <summary>
	/// Returns the gateway firmware version as a comparable integer from <c>/api/status</c>.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The comparable integer version, or <see langword="null"/> when unavailable.</returns>
	public async Task<long?> GetVersionIntAsync (CancellationToken cancellationToken = default) =>
		VersionHelper.ParseVersion (await GetVersionAsync (cancellationToken).ConfigureAwait (false));

	/// <summary>
	/// Returns the deserialized gateway status from <c>/api/status</c>.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The <see cref="GatewayStatus"/>, or <see langword="null"/> when unavailable.</returns>
	public async Task<GatewayStatus?> GetStatusAsync (CancellationToken cancellationToken = default)
		{
		var payload = await PollAsync ("/api/status", cancellationToken: cancellationToken).ConfigureAwait (false);
		return JsonHelper.DeserializeOrNull<GatewayStatus> (payload);
		}

	private async Task GetSessionAsync (CancellationToken cancellationToken)
		{
		_hasAuth = false;
		_authorizationHeader = null;
		Token = null;
		Cache.Clear ();
		var url = $"https://{_host}/api/login/Basic";
		var loginPayload = new LocalLoginRequest
			{
			Username = "customer",
			Password = _password,
			Email = Email,
			ClientInfo = new LocalClientInfo { Timezone = _timezone }
			};

		HttpResponseMessage response;
		try
			{
			using var request = new HttpRequestMessage (HttpMethod.Post, url)
				{
				Content = new StringContent (JsonHelper.Serialize (loginPayload), Encoding.UTF8, "application/json")
				};
			response = await _httpClient!.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (Exception exc) when (!cancellationToken.IsCancellationRequested && exc is HttpRequestException or TaskCanceledException)
			{
			var err = $"Unable to connect to Powerwall at https://{_host}: {exc.Message}";
			LibraryLog.CheckThatTheGatewayIsReachableOnTheNetwork (_log, err);
			throw new PowerwallConnectionException (err, exc);
			}

		using (response)
			{
#if NETFRAMEWORK
			var body = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
#else
			var body = await response.Content.ReadAsStringAsync (cancellationToken).ConfigureAwait (false);
#endif
			if (!response.IsSuccessStatusCode)
				{
				LibraryLog.LoginFailedHTTP (_log, (int)response.StatusCode);
				throw (int)response.StatusCode is 401 or 403
					? new LoginException ($"Invalid Powerwall Login - check password for {_host}")
					: new LoginException ($"Login failed for {_host} (HTTP {(int)response.StatusCode}) - check that the gateway is reachable and responding correctly");
				}

			try
				{
				LocalLoginResponse? json = JsonHelper.Deserialize<LocalLoginResponse> (body);
				if (_authMode == "token")
					{
					Token = !string.IsNullOrWhiteSpace (json?.Token) ? json!.Token
						: throw new LoginException ("The local gateway did not return a bearer token.");
					_authorizationHeader = $"Bearer {Token}";
					}

				_hasAuth = true;
				if (_persistSession)
					PersistAuth ();
				}
			catch (JsonException exc)
				{
				LibraryLog.LoginFailed (_log, exc.Message);
				throw new LoginException ($"Invalid Powerwall Login response from {_host}", exc);
				}
			}
		}

	private void LoadCachedAuth ()
		{
		try
			{
			if (!File.Exists (_cacheFile))
				return;

			LocalAuthCacheEntry? entry = JsonHelper.Deserialize<LocalAuthCacheEntry> (File.ReadAllText (_cacheFile));
			if (entry is null)
				return;

			if (_authMode == "token")
				{
				var authorization = entry.Authorization;
				if (!string.IsNullOrWhiteSpace (authorization))
					{
					_authorizationHeader = authorization;
					Token = authorization!.Split (' ').Last ();
					_hasAuth = true;
					}
				}
			else if (entry.AuthCookie is not null && entry.UserRecord is not null)
				{
				_cookies!.Add (new Cookie ("AuthCookie", entry.AuthCookie, "/", HostWithoutPort));
				_cookies.Add (new Cookie ("UserRecord", entry.UserRecord, "/", HostWithoutPort));
				_hasAuth = true;
				}

			LibraryLog.LoadedAuthFromCacheFileAuthmode (_log, _cacheFile, _authMode);
			}
		catch (Exception exc) when (exc is IOException or JsonException or UnauthorizedAccessException)
			{
			LibraryLog.NoAuthCacheFile (_log, exc.Message);
			}
		}

	private void PersistAuth ()
		{
		try
			{
			LocalAuthCacheEntry auth = _authMode == "token"
				? new LocalAuthCacheEntry { Authorization = _authorizationHeader }
				: BuildCookieAuthEntry ();

			File.WriteAllText (_cacheFile, JsonHelper.Serialize (auth));
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
			{
			LibraryLog.UnableToCacheAuthSessionContinuing (_log, exc.Message);
			}
		}

	private LocalAuthCacheEntry BuildCookieAuthEntry ()
		{
		CookieCollection cookies = _cookies!.GetCookies (new Uri ($"https://{HostWithoutPort}"));
		return new LocalAuthCacheEntry
			{
			AuthCookie = cookies["AuthCookie"]?.Value,
			UserRecord = cookies["UserRecord"]?.Value
			};
		}

	private HttpRequestMessage CreateRequest (HttpMethod method, string url)
		{
		var target = new Uri (url, UriKind.Absolute);
		Uri gateway = LocalEndpoint.Create (_host);
		if (target.Scheme != gateway.Scheme || target.Host != gateway.Host || target.Port != gateway.Port || target.UserInfo.Length != 0)
			{
			throw new ArgumentException ("Local requests must target the configured gateway.", nameof (url));
			}
		var request = new HttpRequestMessage (method, target);
		if (_authMode == "token" && !string.IsNullOrWhiteSpace (_authorizationHeader))
			request.Headers.TryAddWithoutValidation ("Authorization", _authorizationHeader);

		return request;
		}

	private async Task<(bool Handled, bool Retry)> HandleStatusAsync (string api, string url, HttpResponseMessage response, bool recursive, bool raw, CancellationToken cancellationToken)
		{
		var status = (int)response.StatusCode;
		switch (status)
			{
			case 404:
				LibraryLog.PowerwallAPINotFoundAt (_log, url);
				if (api == "/api/devices/vitals")
					{
					var versionPayload = await PollCoreAsync ("/api/status", cancellationToken: cancellationToken).ConfigureAwait (false);
					var version = VersionHelper.ParseVersion (JsonHelper.DeserializeOrNull<GatewayStatus> (versionPayload)?.Version);
					if (version >= 23440)
						{
						_vitalsApiAvailable = false;
						LibraryLog.FirmwareDetectedDoesNotSupportVitalsAPIDisabling (_log, version);
						}
					}

				SetCacheCooldown (api, 600);
				return (true, false);

			case 429:
				_cooldownUntil = NowSeconds + 300;
				LibraryLog.RateLimitedByPowerwallAPIAtActivatingMinuteCooldown (_log, url);
				return (true, false);

			case 401 or 403:
				LibraryLog.SessionExpiredTryingToGetANewOne (_log);
				if (!recursive)
					{
					await GetSessionAsync (cancellationToken).ConfigureAwait (false);
					return (true, true);
					}

				if (status == 401)
					LibraryLog.UnableToEstablishSessionWithPowerwallAtCheckPassword (_log, url);
				else
					LibraryLog.UnauthorizedByPowerwallAPIAtEndpointDisabledInThis (_log, url);

				SetCacheCooldown (api, 600);
				return (true, false);

			case 503:
				LibraryLog.ServiceUnavailableAtActivatingMinuteAPICooldown (_log, url);
				SetCacheCooldown (api, 300);
				return (true, false);

			case >= 400 and < 500:
				LibraryLog.UnhandledHTTPResponseCodeAt (_log, status, url);
				return (true, false);

			case >= 500:
				LibraryLog.ServerSideProblemAtPowerwallAPIStatusCodeAt (_log, status, url);
				return (true, false);

			default:
				return (false, false);
			}
		}

	private bool TryGetCached (string api, out string? payload)
		{
		if (Cache.TryGetValue (api, out payload) && payload is not null && _cacheTimes.TryGetValue (api, out var cachedAt))
			{
			if (NowSeconds - cachedAt < _cacheExpireSeconds)
				{
				LibraryLog.LocalReturningCached (_log, api);
				return true;
				}
			}

		payload = null;
		return false;
		}

	private void StoreCache (string api, string payload)
		{
		Cache[api] = payload;
		_cacheTimes[api] = NowSeconds;
		}

	private void SetCacheCooldown (string api, double seconds)
		{
		Cache[api] = null;
		_cacheTimes[api] = NowSeconds + seconds;
		}

	private string HostWithoutPort => LocalEndpoint.Create (_host).DnsSafeHost;

	/// <summary>
	/// Releases the underlying <see cref="HttpClient"/> and associated resources.
	/// </summary>
	public void Dispose ()
		{
		_httpClient?.Dispose ();
		_requestGate.Dispose ();
		}
	}