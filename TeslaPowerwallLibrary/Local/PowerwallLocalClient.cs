// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

using log4net;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Local;

/// <summary>
/// Local-mode Powerwall client that communicates directly with a Tesla Energy Gateway over HTTPS.
/// Faithfully adapts the behavior of the Python <c>PyPowerwallLocal</c> class using an async,
/// strongly-typed, cancellable API surface.
/// </summary>
public sealed class PowerwallLocalClient : PowerwallClientBase, IDisposable
	{
	private static readonly ILog _log = LogManager.GetLogger (typeof (PowerwallLocalClient));

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
		: base (email)
		{
		if (string.IsNullOrWhiteSpace (host))
			throw new ArgumentException ("Host is required for local mode.", nameof (host));

		_host = host;
		_password = password ?? string.Empty;
		_timezone = string.IsNullOrWhiteSpace (timezone) ? Constants.DEFAULT_TIMEZONE : timezone;
		_timeout = timeout;
		_cacheExpireSeconds = cacheExpireSeconds;
		_authMode = authMode is "cookie" or "token" ? authMode : "cookie";
		_cacheFile = string.IsNullOrWhiteSpace (cacheFile) ? Constants.DEFAULT_CACHE_FILE : cacheFile;
		}

	private double NowSeconds => _clock.Elapsed.TotalSeconds;

	/// <inheritdoc/>
	public override async Task AuthenticateAsync (CancellationToken cancellationToken = default)
		{
		_log.Debug ("Tesla local mode enabled");

		_cookies = new CookieContainer ();
		var handler = new HttpClientHandler
			{
			CookieContainer = _cookies,
			// The Energy Gateway presents a self-signed certificate, so certificate validation
			// is intentionally bypassed for the gateway connection, mirroring upstream behavior.
			ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
			};

		_httpClient = new HttpClient (handler)
			{
			Timeout = _timeout
			};

		LoadCachedAuth ();

		if (!_hasAuth)
			await GetSessionAsync (cancellationToken).ConfigureAwait (false);
		}

	/// <inheritdoc/>
	public override async Task CloseSessionAsync (CancellationToken cancellationToken = default)
		{
		if (_httpClient is not null)
			{
			var url = $"https://{_host}/api/logout";
			try
				{
				using HttpRequestMessage request = CreateRequest (HttpMethod.Get, url);
				using HttpResponseMessage response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
				}
			catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException)
				{
				_log.Debug ($"Error during logout: {exc.Message}");
				}
			}

		_hasAuth = false;
		_authorizationHeader = null;
		}

	/// <inheritdoc/>
	public override async Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		if (TryGetCached (api, out var cached) && !force)
			return cached;

		if (_cooldownUntil > NowSeconds)
			{
			_log.Debug ("Rate limit cooldown period - Pausing API calls");
			return null;
			}

		var url = $"https://{_host}{api}";
		HttpResponseMessage response;
		try
			{
			using HttpRequestMessage request = CreateRequest (HttpMethod.Get, url);
			response = await _httpClient!.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			_log.Error ($"Timeout waiting for Powerwall API {api} - check network connectivity to {_host}");
			return null;
			}
		catch (HttpRequestException exc)
			{
			_log.Error ($"Unable to connect to Powerwall at {_host} - {exc.Message} - check that the gateway is reachable and powered on");
			return null;
			}

		using (response)
			{
			(bool Handled, bool Retry) statusHandling = await HandleStatusAsync (api, url, response, recursive, raw: false, cancellationToken).ConfigureAwait (false);
			if (statusHandling.Handled)
				return statusHandling.Retry ? await PollAsync (api, force, recursive: true, cancellationToken).ConfigureAwait (false) : null;

#if NETFRAMEWORK
			var body = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
#else
			var body = await response.Content.ReadAsStringAsync (cancellationToken).ConfigureAwait (false);
#endif
			if (string.IsNullOrEmpty (body))
				{
				_log.Debug ($"Empty response from Powerwall at {url}");
				return null;
				}

			StoreCache (api, body);
			return body;
			}
		}

	/// <inheritdoc/>
	public override async Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		if (api == "/api/devices/vitals" && !_vitalsApiAvailable)
			return null;

		if (_cooldownUntil > NowSeconds)
			{
			_log.Debug ("Rate limit cooldown period - Pausing API calls");
			return null;
			}

		var url = $"https://{_host}{api}";
		HttpResponseMessage response;
		try
			{
			using HttpRequestMessage request = CreateRequest (HttpMethod.Get, url);
			response = await _httpClient!.SendAsync (request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			_log.Error ($"Timeout waiting for Powerwall API {api} - check network connectivity to {_host}");
			return null;
			}
		catch (HttpRequestException exc)
			{
			_log.Error ($"Unable to connect to Powerwall at {_host} - {exc.Message} - check that the gateway is reachable and powered on");
			return null;
			}

		using (response)
			{
			(bool Handled, bool Retry) statusHandling = await HandleStatusAsync (api, url, response, recursive, raw: true, cancellationToken).ConfigureAwait (false);
			if (statusHandling.Handled)
				return statusHandling.Retry ? await PollRawAsync (api, force, recursive: true, cancellationToken).ConfigureAwait (false) : null;

#if NETFRAMEWORK
			return await response.Content.ReadAsByteArrayAsync ().ConfigureAwait (false);
#else
			return await response.Content.ReadAsByteArrayAsync (cancellationToken).ConfigureAwait (false);
#endif
			}
		}

	/// <inheritdoc/>
	public override async Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		var url = $"https://{_host}{api}";
		HttpResponseMessage response;
		try
			{
			using HttpRequestMessage request = CreateRequest (HttpMethod.Post, url);
			if (payload is not null)
				{
				var json = JsonConvert.SerializeObject (payload);
				request.Content = new StringContent (json, Encoding.UTF8, "application/json");
				}

			response = await _httpClient!.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			_log.Debug ($"ERROR Timeout waiting for Powerwall API {url}");
			return null;
			}
		catch (HttpRequestException exc)
			{
			_log.Debug ($"ERROR Unable to connect to Powerwall at {url}: {exc.Message}");
			return null;
			}

		using (response)
			{
			(bool Handled, bool Retry) statusHandling = await HandleStatusAsync (api, url, response, recursive, raw: false, cancellationToken).ConfigureAwait (false);
			if (statusHandling.Handled)
				{
				if (statusHandling.Retry)
					return await PostAsync (api, payload, din, recursive: true, cancellationToken).ConfigureAwait (false);
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
	public override Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default) =>
		// Vitals decoding requires the protobuf milestone; the binary stream is available via
		// PollRawAsync("/api/devices/vitals") and will be decoded once protobuf support is added.
		Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> (null);

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
		var url = $"https://{_host}/api/login/Basic";
		var loginPayload = new
			{
			username = "customer",
			password = _password,
			email = Email,
			clientInfo = new { timezone = _timezone }
			};

		HttpResponseMessage response;
		try
			{
			using var request = new HttpRequestMessage (HttpMethod.Post, url)
				{
				Content = new StringContent (JsonConvert.SerializeObject (loginPayload), Encoding.UTF8, "application/json")
				};
			response = await _httpClient!.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException)
			{
			var err = $"Unable to connect to Powerwall at https://{_host}: {exc.Message}";
			_log.Error ($"{err} - check that the gateway is reachable on the network");
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
				_log.Warn ($"Login failed: HTTP {(int) response.StatusCode}");
				throw (int) response.StatusCode is 401 or 403
					? new LoginException ($"Invalid Powerwall Login - check password for {_host}")
					: new LoginException ($"Login failed for {_host} (HTTP {(int) response.StatusCode}) - check that the gateway is reachable and responding correctly");
				}

			try
				{
				var json = JObject.Parse (body);
				if (_authMode == "token")
					{
					Token = json.Value<string> ("token");
					_authorizationHeader = $"Bearer {Token}";
					}

				_hasAuth = true;
				PersistAuth (json);
				}
			catch (JsonException exc)
				{
				_log.Warn ($"Login failed: {exc.Message}");
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

			var json = JObject.Parse (File.ReadAllText (_cacheFile));
			if (_authMode == "token")
				{
				var authorization = json.Value<string> ("Authorization");
				if (!string.IsNullOrWhiteSpace (authorization))
					{
					_authorizationHeader = authorization;
					Token = authorization!.Split (' ').Last ();
					_hasAuth = true;
					}
				}
			else if (json["AuthCookie"] is not null && json["UserRecord"] is not null)
				{
				_cookies!.Add (new Cookie ("AuthCookie", json.Value<string> ("AuthCookie"), "/", HostWithoutPort));
				_cookies.Add (new Cookie ("UserRecord", json.Value<string> ("UserRecord"), "/", HostWithoutPort));
				_hasAuth = true;
				}

			_log.Debug ($"loaded auth from cache file {_cacheFile} ({_authMode} authmode)");
			}
		catch (Exception exc) when (exc is IOException or JsonException or UnauthorizedAccessException)
			{
			_log.Debug ($"no auth cache file: {exc.Message}");
			}
		}

	private void PersistAuth (JObject loginResponse)
		{
		try
			{
			JObject auth;
			if (_authMode == "token")
				{
				auth = new JObject { ["Authorization"] = _authorizationHeader };
				}
			else
				{
				var stored = new JObject ();
				foreach (Cookie cookie in _cookies!.GetCookies (new Uri ($"https://{HostWithoutPort}")))
					stored[cookie.Name] = cookie.Value;
				auth = stored;
				}

			File.WriteAllText (_cacheFile, auth.ToString (Formatting.None));
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
			{
			_log.Debug ($"unable to cache auth session - continuing: {exc.Message}");
			}
		}

	private HttpRequestMessage CreateRequest (HttpMethod method, string url)
		{
		var request = new HttpRequestMessage (method, url);
		if (_authMode == "token" && !string.IsNullOrWhiteSpace (_authorizationHeader))
			request.Headers.TryAddWithoutValidation ("Authorization", _authorizationHeader);

		return request;
		}

	private async Task<(bool Handled, bool Retry)> HandleStatusAsync (string api, string url, HttpResponseMessage response, bool recursive, bool raw, CancellationToken cancellationToken)
		{
		var status = (int) response.StatusCode;
		switch (status)
			{
			case 404:
				_log.Error ($"404 Powerwall API not found at {url}");
				if (api == "/api/devices/vitals")
					{
					var version = await GetVersionIntAsync (cancellationToken).ConfigureAwait (false);
					if (version >= 23440)
						{
						_vitalsApiAvailable = false;
						_log.Error ($"Firmware {version} detected - Does not support vitals API - disabling.");
						}
					}

				SetCacheCooldown (api, 600);
				return (true, false);

			case 429:
				_cooldownUntil = NowSeconds + 300;
				_log.Error ($"429 Rate limited by Powerwall API at {url} - Activating 5 minute cooldown");
				return (true, false);

			case 401 or 403:
				_log.Debug ("Session Expired - Trying to get a new one");
				if (!recursive)
					{
					await GetSessionAsync (cancellationToken).ConfigureAwait (false);
					return (true, true);
					}

				if (status == 401)
					_log.Error ($"Unable to establish session with Powerwall at {url} - check password");
				else
					_log.Error ($"403 Unauthorized by Powerwall API at {url} - Endpoint disabled in this firmware or user lacks permission");

				SetCacheCooldown (api, 600);
				return (true, false);

			case 503:
				_log.Error ($"503 Service Unavailable at {url} - Activating 5 minute API cooldown");
				SetCacheCooldown (api, 300);
				return (true, false);

			case >= 400 and < 500:
				_log.Error ($"Unhandled HTTP response code {status} at {url}");
				return (true, false);

			case >= 500:
				_log.Error ($"Server-side problem at Powerwall API (status code {status}) at {url}");
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
				_log.Debug ($" -- local: Returning cached {api}");
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

	private string HostWithoutPort
		{
		get
			{
			var colon = _host.LastIndexOf (':');
			return colon > 0 && int.TryParse (_host[(colon + 1)..], out _) ? _host[..colon] : _host;
			}
		}

	/// <summary>
	/// Releases the underlying <see cref="HttpClient"/> and associated resources.
	/// </summary>
	public void Dispose () => _httpClient?.Dispose ();
	}
