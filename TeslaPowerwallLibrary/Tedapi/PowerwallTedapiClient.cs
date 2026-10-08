// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;

using Google.Protobuf;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;

using Graphql = TeslaPowerwallLibrary.Tedapi.Protocol.Graphql;
using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Local TEDAPI telemetry over gateway Wi-Fi or the Powerwall 3 signed LAN transport.</summary>
/// <remarks>The signed LAN transport requires an already registered key. Connecting never registers a key,
/// changes configuration, opens the grid contactor or restarts equipment.</remarks>
public sealed partial class PowerwallTedapiClient : PowerwallClientBase, IDisposable
	{
	private readonly PowerwallOptions _options;
	private readonly HttpClient _httpClient;
	private readonly Uri _endpoint;
	private readonly SemaphoreSlim _gate = new (1, 1);
	private readonly Stopwatch _clock = Stopwatch.StartNew ();
	private readonly Dictionary<string, (object Value, double Time)> _cache = [];
	private double _cooldownUntil;
	private string? _din;
	private string? _token;
	private static readonly Dictionary<string, QueryDefinition> _queries = LoadQueries ("Queries.data");
	private static readonly Dictionary<string, QueryDefinition> _queries2026 = LoadQueries ("Queries2026.data");

	/// <summary>Creates a TEDAPI client using HTTP Basic authentication on a gateway-accessible network.</summary>
	/// <param name="gatewayPassword">Full gateway password printed on the equipment label.</param>
	/// <param name="cacheExpireSeconds">Telemetry cache lifetime in seconds.</param>
	/// <param name="timeout">Per-request timeout.</param>
	/// <param name="host">Hostname or IP address with an optional HTTPS port.</param>
	public PowerwallTedapiClient (string gatewayPassword, int cacheExpireSeconds, TimeSpan timeout, string host = Constants.GW_IP)
		: this (new PowerwallOptions { Host = host, GatewayPassword = gatewayPassword,
			LocalProtocol = PowerwallLocalProtocol.Tedapi, CacheExpireSeconds = cacheExpireSeconds, Timeout = timeout })
		{
		}

	/// <summary>Creates a local telemetry client with explicit transport and authentication settings.</summary>
	/// <param name="options">TEDAPI connection options. The signing key remains caller-owned.</param>
	public PowerwallTedapiClient (PowerwallOptions options)
		: this (options, null)
		{
		}

	/// <summary>Creates a client with an injectable transport for deterministic offline tests.</summary>
	/// <param name="options">Connection settings.</param>
	/// <param name="handler">Optional test transport owned by this client.</param>
	/// <param name="readRouteClock">Optional elapsed-seconds source for deterministic failover tests.</param>
	internal PowerwallTedapiClient (PowerwallOptions options, HttpMessageHandler? handler, Func<double>? readRouteClock = null)
		: base (options?.Email ?? throw new ArgumentNullException (nameof (options)))
		{
		_options = options;
		_readRouteClock = readRouteClock ?? (() => _clock.Elapsed.TotalSeconds);
		if (options.LocalQueryVersion is not (TedapiQueryVersion.June2024 or TedapiQueryVersion.June2026))
			throw new PowerwallInvalidConfigurationException ("Unknown TEDAPI query version.");
		if (options.LocalProtocol is not (PowerwallLocalProtocol.Tedapi or PowerwallLocalProtocol.TedapiSigned or PowerwallLocalProtocol.TedapiBearer))
			{
			throw new PowerwallInvalidConfigurationException ("Select a TEDAPI local protocol.");
			}
		if (options.CacheExpireSeconds < 0 || options.Timeout <= TimeSpan.Zero)
			{
			throw new PowerwallInvalidConfigurationException ("Cache lifetime must be nonnegative and timeout must be positive.");
			}
		if (IsSigned ? options.LocalSigningKey is null || string.IsNullOrWhiteSpace (options.Password)
			: string.IsNullOrWhiteSpace (options.GatewayPassword))
			{
			throw new PowerwallInvalidConfigurationException ("TEDAPI requires a gateway password, or a customer password and registered RSA key for signed LAN access.");
			}
		if (IsSigned && options.LocalSigningKey!.KeySize != 4096)
			throw new PowerwallInvalidConfigurationException ("Signed local access requires a 4096-bit RSA key.");
		if (options.LocalFollowerConnection is { } follower && (!IsSigned || follower._options.LocalProtocol != PowerwallLocalProtocol.Tedapi))
			throw new PowerwallInvalidConfigurationException ("A follower connection must use setup-network TEDAPI and may only be attached to a signed LAN client.");
		if (options.EnableLocalReadFailover && (!IsSigned || options.LocalFollowerConnection is null || options.LocalReadFailoverRetryInterval <= TimeSpan.Zero))
			throw new PowerwallInvalidConfigurationException ("Read failover requires signed LAN, an explicit setup-network connection and a positive retry interval.");
		_endpoint = LocalEndpoint.Create (Host);
		_httpClient = new HttpClient (handler ?? new HttpClientHandler
			{
			AllowAutoRedirect = false,
			UseProxy = false,
			// Gateway certificates are self-signed. This handler is private to this local endpoint.
			ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
			}) { Timeout = options.Timeout, MaxResponseContentBufferSize = 8 * 1024 * 1024 };
		}

	/// <summary>Gets the configured gateway password. Treat this value as a secret.</summary>
	public string GatewayPassword => _options.GatewayPassword;

	/// <summary>Gets the hostname or IP address used for requests; hostnames are retained for DNS resolution.</summary>
	public string Host => string.IsNullOrWhiteSpace (_options.Host) ? Constants.GW_IP : _options.Host;

	/// <summary>Gets the telemetry cache lifetime in seconds.</summary>
	public int CacheExpireSeconds => _options.CacheExpireSeconds;

	/// <summary>Gets the per-request HTTP timeout.</summary>
	public TimeSpan Timeout => _options.Timeout;

	/// <summary>Gets the device identifier returned by the local endpoint during authentication, or null before authentication.</summary>
	public string? DeviceIdentificationNumber => _din;

	private bool IsBearer => _options.LocalProtocol == PowerwallLocalProtocol.TedapiBearer;
	private bool BareEnvelope => IsSigned || IsBearer;

	private bool IsSigned => _options.LocalProtocol == PowerwallLocalProtocol.TedapiSigned;

	/// <inheritdoc/>
	public override Task AuthenticateAsync (CancellationToken cancellationToken = default) => AuthenticateWithReadFailoverAsync (cancellationToken);

	private async Task AuthenticatePrimaryAsync (CancellationToken cancellationToken)
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			_din = null;
			_token = null;
			_nativeToken = null;
			_cache.Clear ();
			if (BareEnvelope)
				{
				await LoginAsync (cancellationToken).ConfigureAwait (false);
				}
			byte[] payload = await SendAsync (HttpMethod.Get, "/tedapi/din", null, cancellationToken).ConfigureAwait (false);
			var din = Encoding.UTF8.GetString (payload).Trim ();
			if (din.Length > 255 || din.Length < 8 || din.Any (static ch => !char.IsLetterOrDigit (ch) && ch != '-'))
				{
				throw new PowerwallConnectionException ("The gateway did not return a valid device identification number.");
				}
			_din = din;
			_cache.Clear ();
			}
		finally
			{
			_gate.Release ();
			}
		}

	/// <summary>Reads the device-controller telemetry without changing any setting.</summary>
	/// <param name="force">Bypasses cached measurements but never bypasses device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Typed measurements; omitted firmware fields remain null.</returns>
	public Task<LocalTelemetry> GetTelemetryAsync (bool force = false, CancellationToken cancellationToken = default) =>
		ReadQueryAsync<LocalTelemetry> ("device_controller_basic", force, cancellationToken);

	/// <summary>Reads Powerwall 3 component signals and active alerts.</summary>
	/// <param name="force">Bypasses cached measurements but never bypasses device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Typed component measurements.</returns>
	public Task<LocalComponentTelemetry> GetComponentsAsync (bool force = false, CancellationToken cancellationToken = default) =>
		ReadQueryAsync<LocalComponentTelemetry> ("components", force, cancellationToken);


	/// <summary>Reads one identified Powerwall's components through the configured local gateway.</summary>
	/// <remarks>Follower routing requires the setup-network transport or an explicit caller-owned LocalFollowerConnection. Controller reads switch only with explicit EnableLocalReadFailover.</remarks>
	/// <param name="deviceDin">Configured device identification number, obtained from local configuration.</param>
	/// <param name="force">Bypasses cached measurements but not device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Component measurements from the requested device only.</returns>
	public Task<LocalComponentTelemetry> GetComponentsAsync (string deviceDin, bool force = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (deviceDin) || deviceDin.Length > 128 || deviceDin.Any (static c =>
			!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')))
			throw new ArgumentException ("A device identification number containing letters, digits and hyphens is required.", nameof (deviceDin));
		EnsureConnected ();
		if (IsSigned && deviceDin != _din && _options.LocalFollowerConnection is { } follower)
			return follower.GetComponentsAsync (deviceDin, force, cancellationToken);
		return ReadQueryAsync<LocalComponentTelemetry> ("components", force, cancellationToken, deviceDin);
		}

	/// <summary>Reads the detailed signed controller query, including remote-meter measurements.</summary>
	/// <param name="force">Bypasses cached measurements but not gateway backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Typed reported controller, system and meter measurements; unsupported fields remain null.</returns>
	public Task<LocalTelemetry> GetDetailedTelemetryAsync (bool force = false, CancellationToken cancellationToken = default) =>
		ReadQueryAsync<LocalTelemetry> ("device_controller_full", force, cancellationToken);
	/// <summary>Reads configured meter channels, preserving meter identity, CT slot, units and missing values.</summary>
	/// <param name="force">Bypasses cached configuration and telemetry, but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Reported Neurio and Tesla remote-meter channels with their configured location and scale.</returns>
	public async Task<IReadOnlyList<LocalConfiguredMeter>> GetMeterReadingsAsync (bool force = false, CancellationToken cancellationToken = default)
		{
		var configuration = await GetConfigurationAsync (force, cancellationToken).ConfigureAwait (false);
		var telemetry = await GetDetailedTelemetryAsync (force, cancellationToken).ConfigureAwait (false);
		return LocalMeterProjection.Create (configuration, telemetry);
		}

	/// <summary>Reads detailed configured meter aggregates without replacing missing readings with zero.</summary>
	/// <param name="force">Bypasses cached configuration and telemetry, but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Location aggregates with explicitly converted energy units.</returns>
	public async Task<MeterAggregates> GetDetailedMeterAggregatesAsync (bool force = false, CancellationToken cancellationToken = default)
		{
		var snapshot = await GetDeviceSnapshotAsync (force, cancellationToken).ConfigureAwait (false);
		return LocalMeterProjection.Aggregate (snapshot);
		}

	private Task<T> ReadQueryAsync<T> (string name, bool force, CancellationToken cancellationToken, string? deviceDin = null) where T : class =>
		ReadWithFailoverAsync (recovering => ReadPrimaryQueryAsync<T> (name, force || recovering, deviceDin, cancellationToken),
			fallback => fallback.ReadQueryAsync<T> (name, force, cancellationToken, deviceDin), cancellationToken);

	private async Task<T> ReadPrimaryQueryAsync<T> (string name, bool force, string? deviceDin, CancellationToken cancellationToken) where T : class
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			cancellationToken.ThrowIfCancellationRequested ();
			EnsureConnected ();
			string? routedDin = deviceDin is not null && !string.Equals (deviceDin, _din, StringComparison.Ordinal) ? deviceDin : null;
			if (IsSigned && routedDin is not null)
				throw new PowerwallNotSupportedException ("Signed LAN cannot route follower queries. Use an explicitly configured setup Wi-Fi connection for that device.");
			string cacheKey = routedDin is null ? name : name + ":" + routedDin;
			if (!force && _cache.TryGetValue (cacheKey, out var cached) && _clock.Elapsed.TotalSeconds - cached.Time < CacheExpireSeconds)
				{
				return (T)cached.Value;
				}
			EnsureConnected ();
			string? json;
			if (_options.LocalQueryVersion == TedapiQueryVersion.June2026 || _queries2026.ContainsKey (name))
				json = await ReadSignedQueryAsync (name, routedDin, cancellationToken).ConfigureAwait (false);
			else
				{
				QueryDefinition query = _queries[name];
				var envelope = Envelope ();
				if (routedDin is not null)
					{
					envelope.Sender = new Legacy.Participant { Din = _din! };
					envelope.Recipient = new Legacy.Participant { Din = routedDin };
					}
				envelope.Payload = new Legacy.QueryType
					{
					Send = new Legacy.PayloadQuerySend
						{
						Num = 2,
						Payload = new Legacy.PayloadString { Value = 1, Text = query.Text },
						Code = ByteString.CopyFrom (HexBytes (query.Code)),
						B = new Legacy.StringValue { Value = query.Variables }
						}
					};
				byte[] response = await ExchangeAsync (envelope, cancellationToken, routedDin).ConfigureAwait (false);
				Legacy.MessageEnvelope answer = BareEnvelope ? Legacy.MessageEnvelope.Parser.ParseFrom (response)
					: Legacy.Message.Parser.ParseFrom (response).Message_;
				json = answer?.Payload?.Recv?.Text;
				}
			if (string.IsNullOrWhiteSpace (json))
				{
				throw new PowerwallConnectionException ("The gateway returned no TEDAPI query data.");
				}
			T value = JsonHelper.Deserialize<T> (json!) ?? throw new PowerwallConnectionException ("The gateway returned null TEDAPI query data.");
			_cache[cacheKey] = (value, _clock.Elapsed.TotalSeconds);
			return value;
			}
		catch (Exception exc) when (exc is System.Text.Json.JsonException or InvalidProtocolBufferException)
			{
			throw new PowerwallConnectionException ("The gateway returned malformed TEDAPI query data.", exc);
			}
		finally
			{
			_gate.Release ();
			}
		}


	/// <summary>Reads non-secret operating configuration directly from the gateway.</summary>
	/// <param name="force">Bypasses the cache but never bypasses gateway backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Typed site configuration without passwords or installer credentials.</returns>
	public Task<LocalConfiguration> GetConfigurationAsync (bool force = false, CancellationToken cancellationToken = default) =>
		ReadWithFailoverAsync (recovering => GetPrimaryConfigurationAsync (force || recovering, cancellationToken),
			fallback => fallback.GetConfigurationAsync (force, cancellationToken), cancellationToken);

	private async Task<LocalConfiguration> GetPrimaryConfigurationAsync (bool force, CancellationToken cancellationToken)
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			if (!force && _cache.TryGetValue ("configuration", out var cached) && _clock.Elapsed.TotalSeconds - cached.Time < CacheExpireSeconds)
				{
				return (LocalConfiguration)cached.Value;
				}
			var envelope = Envelope ();
			if (IsSigned)
				{
				envelope.DeliveryChannel = 2;
				envelope.Sender = new Legacy.Participant { AuthorizedClient = 1 };
				}
			envelope.Config = new Legacy.ConfigType { Send = new Legacy.PayloadConfigSend { Num = 1, File = "config.json" } };
			byte[] response = await ExchangeAsync (envelope, cancellationToken).ConfigureAwait (false);
			var json = BareEnvelope
				? Signed.MessageEnvelope.Parser.ParseFrom (response).Filestore?.ReadFileResponse?.File?.Blob.ToStringUtf8 ()
				: Legacy.Message.Parser.ParseFrom (response).Message_?.Config?.Recv?.File?.Text;
			if (string.IsNullOrWhiteSpace (json))
				{
				throw new PowerwallConnectionException ("The local gateway returned no configuration data.");
				}
			LocalConfiguration configuration = JsonHelper.Deserialize<LocalConfiguration> (json!)
				?? throw new PowerwallConnectionException ("The local gateway returned null configuration data.");
			_cache["configuration"] = (configuration, _clock.Elapsed.TotalSeconds);
			return configuration;
			}
		catch (Exception exc) when (exc is System.Text.Json.JsonException or InvalidProtocolBufferException)
			{
			throw new PowerwallConnectionException ("The local gateway returned malformed configuration data.", exc);
			}
		finally
			{
			_gate.Release ();
			}
		}

	private async Task<GatewayStatus> ReadStatusAsync (bool force, CancellationToken cancellationToken)
		{
		var information = await GetSystemInformationAsync (force, cancellationToken).ConfigureAwait (false);
		return new GatewayStatus { Din = information.Din, Version = information.Version?.Version };
		}

	private Legacy.MessageEnvelope Envelope () => new ()
		{
		DeliveryChannel = 1,
		Sender = new Legacy.Participant { Local = 1 },
		Recipient = new Legacy.Participant { Din = _din! }
		};

	private Task<byte[]> ExchangeAsync (Legacy.MessageEnvelope envelope, CancellationToken cancellationToken, string? routedDin = null) =>
		ExchangeEnvelopeAsync (envelope.ToByteArray (), cancellationToken, routedDin: routedDin);

	private async Task<byte[]> ExchangeEnvelopeAsync (byte[] envelope, CancellationToken cancellationToken, bool allowAuthenticationRetry = true, string? routedDin = null)
		{
		if (_primaryNeedsAuthentication)
			throw new PowerwallConnectionException ("Signed LAN is unavailable. A supported telemetry read must recover LAN before signed-only operations can run.");
		for (var attempt = 0; attempt < 2; attempt++)
			{
			byte[] request = IsSigned
				? TedapiSigning.Sign (_options.LocalSigningKey!, _din!, envelope,
					checked ((uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds () + 13))).ToByteArray ()
				: IsBearer ? new Signed.AuthEnvelope { Payload = ByteString.CopyFrom (envelope), ExternalAuth = new Signed.ExternalAuth { Type = (Signed.ExternalAuthType)1 } }.ToByteArray ()
				: new Graphql.Frame { Message = ByteString.CopyFrom (envelope), Tail = new Graphql.Tail { Value = routedDin is null ? 1 : 2 } }.ToByteArray ();
			try
				{
				byte[] response = await SendAsync (HttpMethod.Post, IsSigned ? "/tedapi/v1r" : routedDin is null ? "/tedapi/v1" : "/tedapi/device/" + routedDin + "/v1", request, cancellationToken).ConfigureAwait (false);
				if (IsBearer)
					{
					var payload = Signed.AuthEnvelope.Parser.ParseFrom (response).Payload;
					if (payload.IsEmpty) throw new PowerwallConnectionException ("The gateway returned an empty authenticated envelope.");
					return ValidateEnvelope (payload.ToByteArray ());
					}
				if (!IsSigned)
					{
					return response;
					}
				Signed.RoutableMessage routable = Signed.RoutableMessage.Parser.ParseFrom (response);
				if ((int)(routable.SignedMessageStatus?.MessageFault ?? 0) != 0)
					{
					throw new PowerwallConnectionException ("The gateway rejected the local signing key or request: " + routable.SignedMessageStatus!.MessageFault);
					}
				if (routable.ProtobufMessageAsBytes.IsEmpty)
					{
					throw new PowerwallConnectionException ("The gateway returned no signed data. The key may require registration or verification.");
					}
				return ValidateEnvelope (routable.ProtobufMessageAsBytes.ToByteArray ());
				}
			catch (LocalAuthenticationException) when (BareEnvelope && attempt == 0 && allowAuthenticationRetry)
				{
				// Login and re-sign: the previous request signature may have expired.
				await LoginAsync (cancellationToken).ConfigureAwait (false);
				}
			}
		throw new PowerwallConnectionException ("Local authentication did not recover.");
		}

	private static byte[] ValidateEnvelope (byte[] response)
		{
		Signed.ErrorResponse? error = Signed.MessageEnvelope.Parser.ParseFrom (response).Common?.ErrorResponse;
		if (error is not null)
			throw new PowerwallConnectionException ("The gateway rejected the TEDAPI request (RPC status "
				+ (error.Status?.Code.ToString (System.Globalization.CultureInfo.InvariantCulture) ?? "unreported")
				+ "). No automatic retry was attempted.");
		return response;
		}

	private async Task LoginAsync (CancellationToken cancellationToken)
		{
		var login = new LocalLoginRequest
			{
			Username = IsBearer ? "installer" : "customer", Password = IsBearer ? GatewayPassword : _options.Password, Email = IsBearer ? "installer@tesla.com" : Email,
			ClientInfo = new LocalClientInfo { Timezone = _options.Timezone }
			};
		byte[] payload = await SendAsync (HttpMethod.Post, "/api/login/Basic",
			Encoding.UTF8.GetBytes (JsonHelper.Serialize (login)), cancellationToken, login: true).ConfigureAwait (false);
		LocalLoginResponse? response = JsonHelper.Deserialize<LocalLoginResponse> (Encoding.UTF8.GetString (payload));
		_token = !string.IsNullOrWhiteSpace (response?.Token) ? response!.Token
			: throw new PowerwallConnectionException ("Customer login did not return a bearer token.");
		}

	private async Task<byte[]> SendAsync (HttpMethod method, string path, byte[]? data, CancellationToken cancellationToken, bool login = false, string? bearerOverride = null)
		{
		cancellationToken.ThrowIfCancellationRequested ();
		if (_clock.Elapsed.TotalSeconds < _cooldownUntil)
			{
			throw new PowerwallConnectionException ("The gateway requested a cooldown; local requests are temporarily suspended.");
			}
		using var request = new HttpRequestMessage (method, new Uri (_endpoint, path));
		if (!login)
			{
			request.Headers.Authorization = bearerOverride is not null ? new AuthenticationHeaderValue ("Bearer", bearerOverride) : BareEnvelope
				? new AuthenticationHeaderValue ("Bearer", _token)
				: new AuthenticationHeaderValue ("Basic", Convert.ToBase64String (Encoding.UTF8.GetBytes ("Tesla_Energy_Device:" + GatewayPassword)));
			}
		if (data is not null)
			{
			request.Content = new ByteArrayContent (data);
			request.Content.Headers.ContentType = new MediaTypeHeaderValue (login ? "application/json" : "application/octet-stream");
			}
		using HttpResponseMessage response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
		if ((int)response.StatusCode is 429 or 503)
			{
			_cooldownUntil = _clock.Elapsed.TotalSeconds + 300;
			}
		if ((int)response.StatusCode is 401 or 403)
			{
			throw new LocalAuthenticationException ();
			}
		if (!response.IsSuccessStatusCode)
			{
			if ((int)response.StatusCode is 500 or 502 or 504)
				throw new LocalTransportUnavailableException ((int)response.StatusCode);
			throw new PowerwallConnectionException ("Local gateway request failed with HTTP " + (int)response.StatusCode + ".");
			}
		byte[] bytes = await response.Content.ReadAsByteArrayAsync ().ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
			{
			using var compressed = new MemoryStream (bytes);
			using var gzip = new GZipStream (compressed, CompressionMode.Decompress);
			using var expanded = new MemoryStream ();
			var buffer = new byte[8192];
			int read;
			#if NETFRAMEWORK
			while ((read = await gzip.ReadAsync (buffer, 0, buffer.Length, cancellationToken).ConfigureAwait (false)) != 0)
#else
			while ((read = await gzip.ReadAsync (buffer.AsMemory (), cancellationToken).ConfigureAwait (false)) != 0)
#endif
				{
				if (expanded.Length + read > 8 * 1024 * 1024)
					{
					throw new PowerwallConnectionException ("Local response exceeds the permitted size.");
					}
				expanded.Write (buffer, 0, read);
				}
			return expanded.ToArray ();
			}
		return bytes;
		}

	/// <inheritdoc/>
	public override async Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		if (api == "/api/status")
			{
			return JsonHelper.Serialize (await ReadStatusAsync (force, cancellationToken).ConfigureAwait (false));
			}
		if (api is "/api/operation" or "/api/site_info/site_name")
			{
			LocalConfiguration configuration = await GetConfigurationAsync (force, cancellationToken).ConfigureAwait (false);
			return api == "/api/operation"
				? JsonHelper.Serialize (new OperationResponse { RealMode = configuration.OperationMode, BackupReservePercent = configuration.Site?.BackupReservePercent })
				: JsonHelper.Serialize (new SiteName { Name = configuration.Site?.Name, Timezone = configuration.Site?.Timezone });
			}
		if (api is not ("/api/meters/aggregates" or "/api/system_status/soe" or "/api/system_status" or "/api/system_status/grid_status"))
			{
			throw new PowerwallNotSupportedException ("This TEDAPI endpoint is not implemented: " + api);
			}
		LocalTelemetry telemetry = await GetTelemetryAsync (force, cancellationToken).ConfigureAwait (false);
		LocalBatteryEnergy? energy = telemetry.Control?.SystemStatus;
		IReadOnlyList<BatteryBlock>? batteries = null;
		if (api == "/api/system_status")
			{
			batteries = (await GetDeviceSnapshotAsync (force, cancellationToken).ConfigureAwait (false)).Batteries;
			}
		object? result = api switch
			{
			"/api/meters/aggregates" => new MeterAggregates
				{
				Site = Reading (telemetry, "SITE"), Solar = Reading (telemetry, "SOLAR"),
				Battery = Reading (telemetry, "BATTERY"), Load = Reading (telemetry, "LOAD")
				},
			"/api/system_status/soe" => energy?.FullCapacityWattHours > 0 && energy.RemainingWattHours.HasValue
				? new StateOfEnergy { Percentage = energy.RemainingWattHours.Value / energy.FullCapacityWattHours.Value * 100 } : null,
			"/api/system_status" => new SystemStatus
				{
				NominalFullPackEnergy = energy?.FullCapacityWattHours, NominalEnergyRemaining = energy?.RemainingWattHours,
				AvailableBlocks = telemetry.Control?.BatteryBlocks?.Count,
				BatteryBlocks = batteries,
				SystemIslandState = telemetry.Control?.Islanding?.ContactorClosed is bool closed ? closed ? "SystemGridConnected" : "SystemIslandedActive" : null
				},
			"/api/system_status/grid_status" => telemetry.Control?.Islanding?.ContactorClosed is bool connected
				? new GridStatusResponse { GridStatus = connected ? "SystemGridConnected" : "SystemIslandedActive" } : null,
			_ => throw new PowerwallNotSupportedException ("This TEDAPI endpoint is not implemented: " + api)
			};
		return result is null ? null : JsonHelper.Serialize (result);
		}

	private static MeterReading? Reading (LocalTelemetry telemetry, string location)
		{
		LocalPowerReading? reading = telemetry.Control?.MeterAggregates?.FirstOrDefault (
			value => string.Equals (value.Location, location, StringComparison.OrdinalIgnoreCase));
		return reading is null ? null : new MeterReading { InstantPower = reading.Watts, LastCommunicationTime = telemetry.System?.Time };
		}

	/// <inheritdoc/>
	public override Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) =>
		throw new PowerwallNotSupportedException ("Use typed TEDAPI telemetry and component measurements.");

	/// <inheritdoc/>
	public override Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default) =>
		throw new PowerwallNotSupportedException ("Use the typed local settings or control methods for TEDAPI writes.");

	/// <inheritdoc/>
	public override async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default)
		{
		var snapshot = await GetDeviceSnapshotAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		return LocalVitalsProjection.Create (snapshot, _din!);
		}

	/// <inheritdoc/>
	public override async Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default)
		{
		LocalTelemetry telemetry = await GetTelemetryAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		double? load = Reading (telemetry, "LOAD")?.InstantPower;
		double? remaining = telemetry.Control?.SystemStatus?.RemainingWattHours;
		return load > 0 && remaining >= 0 ? remaining / load : null;
		}

	/// <inheritdoc/>
	public override Task CloseSessionAsync (CancellationToken cancellationToken = default) => CloseReadRouteAsync (cancellationToken);

	private async Task ClosePrimarySessionAsync (CancellationToken cancellationToken)
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			_din = null;
			_token = null;
			_nativeToken = null;
			_cache.Clear ();
			}
		finally
			{
			_gate.Release ();
			}
		}

	private void EnsureConnected ()
		{
		if (_din is null)
			{
			throw new PowerwallConnectionException ("Connect to the local gateway before requesting telemetry.");
			}
		}

	private static Dictionary<string, QueryDefinition> LoadQueries (string resource)
		{
		using Stream stream = typeof (PowerwallTedapiClient).Assembly.GetManifestResourceStream (
			"TeslaPowerwallLibrary.Tedapi.Protocol." + resource)!;
		using var reader = new StreamReader (stream);
		return JsonHelper.Deserialize<Dictionary<string, QueryDefinition>> (reader.ReadToEnd ())!;
		}

	/// <summary>Issues one June 2026 vendor-signed query through the selected local transport.</summary>
	/// <param name="name">Known read-only query role.</param>
	/// <param name="routedDin">Follower device identifier for an explicit setup Wi-Fi route, or null for the connected unit.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The JSON response body for immediate typed deserialization.</returns>
	private async Task<string?> ReadSignedQueryAsync (string name, string? routedDin, CancellationToken cancellationToken)
		{
		QueryDefinition query = _queries2026.TryGetValue (name, out QueryDefinition? supplemental) ? supplemental
			: _queries2026[name == "components" ? "PW3Query" : "DeviceControllerQuery"];
		var envelope = new Graphql.Envelope
			{
			DeliveryChannel = 1,
			Sender = routedDin is null ? new Graphql.Participant { Local = 1 } : new Graphql.Participant { Din = _din },
			Recipient = new Graphql.Participant { Din = routedDin ?? _din },
			Graphql = new Graphql.GraphQLMessages
				{
				QueryRequest = new Graphql.QueryRequest
					{
					Format = 2,
					Query = ByteString.CopyFrom (HexBytes (query.SignedBytes)),
					Signature = ByteString.CopyFrom (HexBytes (query.Code)),
					VariablesJson = new Graphql.StringValue { Value = query.Variables }
					}
				}
			};
		byte[] response = await ExchangeEnvelopeAsync (envelope.ToByteArray (), cancellationToken, routedDin: routedDin).ConfigureAwait (false);
		Graphql.Envelope answer = Graphql.Envelope.Parser.ParseFrom (BareEnvelope ? response : Graphql.Frame.Parser.ParseFrom (response).Message.ToByteArray ());
		Graphql.QueryResponse? data = answer.Graphql?.QueryResponse;
		if (data is null || data.Status != 1 || data.Errors.Count != 0)
			throw new PowerwallConnectionException ("The gateway rejected or only partly answered the June 2026 query. This firmware may not accept the requested vendor signature.");
		return data.Data;
		}

	private static byte[] HexBytes (string value)
		{
		var bytes = new byte[value.Length / 2];
		for (var i = 0; i < bytes.Length; i++)
			{
			bytes[i] = Convert.ToByte (value.Substring (i * 2, 2), 16);
			}
		return bytes;
		}

	/// <summary>Disposes the HTTP transport. The caller-owned RSA key and optional follower connection are not disposed.</summary>
	public void Dispose ()
		{
		_httpClient.Dispose ();
		_gate.Dispose ();
		_readRouteGate.Dispose ();
		}

	/// <summary>A signed read-only query captured in the upstream reference.</summary>
	private sealed record QueryDefinition
		{
		/// <summary>Exact query text covered by the vendor signature.</summary>
		[JsonPropertyName ("text")]
		public string Text { get; init; } = string.Empty;
		/// <summary>Hexadecimal signature bytes.</summary>
		[JsonPropertyName ("code")]
		public string Code { get; init; } = string.Empty;
		/// <summary>Query variables.</summary>
		[JsonPropertyName ("b_value")]
		public string Variables { get; init; } = "{}";
		/// <summary>Exact vendor-signed GraphQL bytes for the June 2026 query format.</summary>
		[JsonPropertyName ("signed_bytes")]
		public string SignedBytes { get; init; } = string.Empty;
		}

	/// <summary>Authentication rejection used to bound read-only session recovery to one attempt.</summary>
	private sealed class LocalAuthenticationException : PowerwallConnectionException
		{
		/// <summary>Creates an authentication rejection without including response content or credentials.</summary>
		internal LocalAuthenticationException () : base ("The gateway rejected local authentication.")
			{
			}
		}
	}
