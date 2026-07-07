// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Globalization;

using log4net;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Tesla™ Owners (cloud) mode client. Communicates with the Tesla Owners API to retrieve Powerwall™ data
/// and maps the cloud responses onto the same local-gateway API shapes used by the rest of the library.
/// Faithfully adapts the behavior of the Python <c>PyPowerwallCloud</c> class using an async,
/// strongly-typed, cancellable API surface.
/// </summary>
/// <remarks>
/// A <see cref="PowerwallOptions.RefreshToken"/> must be supplied programmatically (or already cached from a
/// prior connect); <see cref="PowerwallOptions.AccessToken"/> is optional and, when absent or rejected, is
/// silently (re)derived from the refresh token. This client refreshes an expired access token but does not
/// perform interactive browser login; obtain the initial refresh token with an external setup tool.
/// </remarks>
public sealed class PowerwallCloudClient : PowerwallClientBase, IDisposable
	{
	private const int COUNTER_MAX = 64;
	private const int SITE_CONFIG_TTL_SECONDS = 59;
	private const double RESERVE_SCALE_BUFFER = 5.0 / 0.95;

	private static readonly ILog _log = LogManager.GetLogger (typeof (PowerwallCloudClient));

	private readonly Stopwatch _clock = Stopwatch.StartNew ();
	private readonly Dictionary<string, JToken> _cloudCache = [];
	private readonly Dictionary<string, double> _cloudCacheTimes = [];
	private readonly string? _accessToken;
	private readonly string? _refreshToken;
	private readonly TeslaCloudTokenCache? _tokenCache;

	private TeslaCloudConnection? _connection;
	private string? _resolvedSiteId;
	private int _counter;

	/// <summary>
	/// Initializes a new instance of the <see cref="PowerwallCloudClient"/> class.
	/// </summary>
	/// <param name="email">
	/// Customer email. Tesla Owners API authentication is entirely token-based, so this value is not sent to
	/// Tesla; it is used only as the token cache key and for diagnostic display. Ignored (beyond display) when
	/// <paramref name="noCloudTokenPersistence"/> is <see langword="true"/>.
	/// </param>
	/// <param name="cacheExpireSeconds">Number of seconds before cached responses expire.</param>
	/// <param name="timeout">Per-request HTTP timeout.</param>
	/// <param name="accessToken">Tesla Owners API OAuth access token.</param>
	/// <param name="refreshToken">Tesla Owners API OAuth refresh token used to renew the access token.</param>
	/// <param name="siteId">Optional site identifier to select when an account has multiple sites.</param>
	/// <param name="authPath">Path to cloud authentication and site cache files.</param>
	/// <param name="noCloudTokenPersistence">
	/// When <see langword="true"/>, disables the token cache entirely: <paramref name="authPath"/> is ignored,
	/// <paramref name="accessToken"/>/<paramref name="refreshToken"/> must be supplied on every call, and
	/// rotated tokens are only ever surfaced through <see cref="TokensRefreshed"/>.
	/// </param>
	public PowerwallCloudClient (
		string email,
		int cacheExpireSeconds,
		TimeSpan timeout,
		string? accessToken = null,
		string? refreshToken = null,
		string? siteId = null,
		string authPath = "",
		bool noCloudTokenPersistence = false)
		: base (email)
		{
		CacheExpireSeconds = cacheExpireSeconds;
		Timeout = timeout;
		SiteId = siteId;
		AuthPath = authPath ?? string.Empty;
		NoCloudTokenPersistence = noCloudTokenPersistence;
		_accessToken = accessToken;
		_refreshToken = refreshToken;
		_tokenCache = noCloudTokenPersistence ? null : new TeslaCloudTokenCache (AuthPath, email);
		}

	/// <summary>Gets the configured site identifier, when one was supplied.</summary>
	public string? SiteId { get; private set; }

	/// <summary>Gets the number of seconds before cached responses expire.</summary>
	public int CacheExpireSeconds { get; }

	/// <summary>Gets the per-request HTTP timeout.</summary>
	public TimeSpan Timeout { get; }

	/// <summary>Gets the path to cloud authentication and site cache files.</summary>
	public string AuthPath { get; }

	/// <summary>Gets a value indicating whether the library-owned token cache is disabled for this instance.</summary>
	public bool NoCloudTokenPersistence { get; }

	/// <summary>
	/// Raised after the underlying Tesla connection refreshes its OAuth tokens. Firing depends on whether an
	/// access token was supplied when this client was constructed (either explicitly via the
	/// <c>accessToken</c> constructor parameter, or resolved from the library's own token cache): when one
	/// was, every refresh is reported in full; when none was (a pure refresh-token bootstrap), a refresh is
	/// only reported when Tesla also rotated the refresh token, and <see cref="CloudTokensRefreshedEventArgs.AccessToken"/>
	/// is <see langword="null"/> in that case. Raised on the calling thread.
	/// </summary>
	public event EventHandler<CloudTokensRefreshedEventArgs>? TokensRefreshed;

	/// <summary>Gets the current Tesla Owners API access token, updated after any refresh.</summary>
	public string? CurrentAccessToken => _connection?.AccessToken ?? _accessToken;

	/// <summary>Gets the current Tesla Owners API refresh token, updated after any rotation.</summary>
	public string? CurrentRefreshToken => _connection?.RefreshToken ?? _refreshToken;

	private double NowSeconds => _clock.Elapsed.TotalSeconds;

	/// <inheritdoc/>
	public override async Task AuthenticateAsync (CancellationToken cancellationToken = default)
		{
		_log.Debug ("Tesla cloud mode enabled");

		// Load any library-persisted tokens/site for this email. Explicitly supplied values (a first-time
		// login or a caller override) take precedence over the cache; otherwise reuse what we persisted on a
		// previous run, so callers never have to manage token storage themselves. When the cache is disabled
		// the caller must supply at least a refresh token on every call (the access token is optional and is
		// silently re-derived below when absent) and is solely responsible for persisting a rotated refresh
		// token via TokensRefreshed.
		CloudTokenCacheEntry cached = _tokenCache?.Load () ?? CloudTokenCacheEntry.Empty;
		var accessToken = string.IsNullOrWhiteSpace (_accessToken) ? cached.AccessToken : _accessToken;
		var refreshToken = string.IsNullOrWhiteSpace (_refreshToken) ? cached.RefreshToken : _refreshToken;
		SiteId ??= cached.SiteId;

		_connection = new TeslaCloudConnection (accessToken, refreshToken, Timeout);
		_connection.TokensRefreshed += OnConnectionTokensRefreshed;
		if (!_connection.HasToken)
			{
			_connection.Dispose ();
			_connection = null;
			throw new PowerwallCloudNoTeslaAuthFileException (
				"No Tesla cloud authentication tokens were provided. Run setup to create a Tesla auth file and supply "
				+ "the access and refresh tokens via PowerwallOptions before connecting in cloud mode.");
			}

		// When only a refresh token is available, obtain an initial access token up front.
		if (_connection.AccessToken is null && !await _connection.RefreshAccessTokenAsync (cancellationToken).ConfigureAwait (false))
			{
			_connection.Dispose ();
			_connection = null;
			throw new PowerwallCloudNoTeslaAuthFileException (
				"Unable to obtain a Tesla cloud access token from the supplied refresh token. Run setup to renew the Tesla auth file.");
			}

		List<JObject>? sites = await FetchEnergySitesAsync (cancellationToken).ConfigureAwait (false);
		if (sites is null)
			{
			_connection.Dispose ();
			_connection = null;
			throw new PowerwallCloudNoTeslaAuthFileException (
				"Unable to retrieve Tesla cloud product list. The access token may be expired or rejected - run setup to renew the Tesla auth file.");
			}

		if (sites.Count == 0)
			{
			_connection.Dispose ();
			_connection = null;
			throw new PowerwallCloudTeslaNotConnectedException ($"No Tesla energy sites were found for {Email}.");
			}

		_resolvedSiteId = SelectSite (sites);
		SiteId ??= _resolvedSiteId;

		// Persist the tokens now in use (an initial refresh above may have produced new ones) and the resolved
		// site, so a later run reconnects without any caller involvement. Skipped entirely in no-cache mode;
		// the caller already receives the current tokens via TokensRefreshed on any subsequent rotation.
		_tokenCache?.SaveTokens (_connection.AccessToken, _connection.RefreshToken);
		_tokenCache?.SaveSite (_resolvedSiteId);
		_log.Debug ($"Connected to Tesla cloud - using site {_resolvedSiteId} for {Email}");
		}

	/// <inheritdoc/>
	public override Task CloseSessionAsync (CancellationToken cancellationToken = default)
		{
		_connection?.Dispose ();
		_connection = null;
		_resolvedSiteId = null;
		return Task.CompletedTask;
		}

	/// <summary>
	/// Returns the list of Tesla energy sites (Powerwall and solar) available to the authenticated account.
	/// Faithfully adapts the upstream pypowerwall <c>getsites()</c> method.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The available sites, or an empty list when none are found.</returns>
	public async Task<IReadOnlyList<CloudSite>> GetSitesAsync (CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		List<JObject>? sites = await FetchEnergySitesAsync (cancellationToken).ConfigureAwait (false);
		if (sites is null)
			return [];

		return sites
			.Select (static site => new CloudSite
				{
				SiteId = GetSiteId (site),
				SiteName = site.Value<string> ("site_name"),
				ResourceType = site.Value<string> ("resource_type")
				})
			.ToList ();
		}

	/// <summary>
	/// Switches the active site to the one matching <paramref name="siteId"/> without reconnecting.
	/// Faithfully adapts the upstream pypowerwall <c>change_site()</c> method, additionally clearing the
	/// cloud cache so subsequent reads reflect the newly selected site.
	/// </summary>
	/// <param name="siteId">The Tesla energy site identifier to switch to.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the site was found and selected; otherwise <see langword="false"/>.</returns>
	public async Task<bool> ChangeSiteAsync (string siteId, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		if (string.IsNullOrWhiteSpace (siteId))
			{
			_log.Error ("Invalid siteid - value is null or empty.");
			return false;
			}

		List<JObject>? sites = await FetchEnergySitesAsync (cancellationToken).ConfigureAwait (false);
		if (sites is null || sites.Count == 0)
			{
			_log.Error ($"No sites found for {Email}.");
			return false;
			}

		foreach (JObject site in sites)
			{
			if (GetSiteId (site) != siteId)
				continue;

			_resolvedSiteId = siteId;
			SiteId = siteId;
			ClearCloudCache ();
			_tokenCache?.SaveSite (siteId);
			var siteName = site.Value<string> ("site_name") ?? "Unknown";
			_log.Debug ($"Changed site to {siteId} ({siteName}) for {Email}");
			return true;
			}

		_log.Error ($"Site {siteId} not found for {Email}.");
		return false;
		}

	/// <summary>
	/// Enables or disables charging the battery from the grid. Faithfully adapts the upstream
	/// pypowerwall <c>set_grid_charging()</c> method.
	/// </summary>
	/// <param name="enabled"><see langword="true"/> to allow grid charging; <see langword="false"/> to disallow it.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when the call fails.</returns>
	public async Task<string?> SetGridChargingAsync (bool enabled, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();

		// Upstream inverts the flag: enabling grid charging clears "disallow_charge_from_grid_with_solar_installed".
		var settings = new JObject { ["disallow_charge_from_grid_with_solar_installed"] = !enabled };
		JObject? response = await _connection!.SetGridImportExportAsync (_resolvedSiteId!, settings, cancellationToken).ConfigureAwait (false);
		InvalidateCloudCache ("SITE_CONFIG");
		return Serialize (response);
		}

	/// <summary>
	/// Sets the grid export rule. Faithfully adapts the upstream pypowerwall <c>set_grid_export()</c> method.
	/// </summary>
	/// <param name="mode">The export rule: <c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when the call fails.</returns>
	public async Task<string?> SetGridExportAsync (string mode, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();

		var settings = new JObject { ["customer_preferred_export_rule"] = mode };
		JObject? response = await _connection!.SetGridImportExportAsync (_resolvedSiteId!, settings, cancellationToken).ConfigureAwait (false);
		InvalidateCloudCache ("SITE_CONFIG");
		return Serialize (response);
		}

	/// <summary>
	/// Returns whether charging the battery from the grid is currently allowed. Faithfully adapts the
	/// upstream pypowerwall <c>get_grid_charging()</c> method.
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when grid charging is allowed, <see langword="false"/> when disallowed, or <see langword="null"/> when unavailable.</returns>
	public async Task<bool?> GetGridChargingAsync (bool force = false, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();

		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? components = config?["response"]?["components"];
		if (components is null)
			return null;

		var disallow = components.Value<bool?> ("disallow_charge_from_grid_with_solar_installed") ?? false;
		return !disallow;
		}

	/// <summary>
	/// Returns the current grid export rule. Faithfully adapts the upstream pypowerwall
	/// <c>get_grid_export()</c> method.
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The export rule (<c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>), or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> GetGridExportAsync (bool force = false, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();

		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? components = config?["response"]?["components"];
		if (components is null)
			return null;

		// A pre-PTO "non_export_configured" flag overrides the preferred export rule.
		if (components.Value<bool?> ("non_export_configured") == true)
			return "never";

		return components.Value<string> ("customer_preferred_export_rule") ?? "battery_ok";
		}

	/// <summary>
	/// Enables or disables Storm Watch (predictive pre-charging ahead of severe weather) for the active site.
	/// This is the same setting exposed by the Tesla™ app; Tesla's upstream <c>teslapy</c>/<c>teslajsonpy</c>
	/// endpoint registries list the underlying <c>STORM_MODE_SETTINGS</c> endpoint, but pypowerwall does not
	/// implement a convenience wrapper for it.
	/// </summary>
	/// <param name="enabled"><see langword="true"/> to enable Storm Watch; <see langword="false"/> to disable it.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when the call fails.</returns>
	public async Task<string?> SetStormWatchAsync (bool enabled, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();

		JObject? response = await _connection!.SetStormModeAsync (_resolvedSiteId!, enabled, cancellationToken).ConfigureAwait (false);
		InvalidateCloudCache ("SITE_CONFIG");
		return Serialize (response);
		}

	/// <summary>
	/// Returns whether Storm Watch is currently enabled for the active site. This is the same setting exposed
	/// by the Tesla™ app.
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when Storm Watch is enabled, <see langword="false"/> when disabled, or <see langword="null"/> when unavailable.</returns>
	public async Task<bool?> GetStormWatchAsync (bool force = false, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();

		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? userSettings = config?["response"]?["user_settings"];
		if (userSettings is null)
			return null;

		return userSettings.Value<bool?> ("storm_mode_enabled") ?? false;
		}

	/// <summary>
	/// Returns raw energy history for the active site (cloud mode only). Faithfully adapts the upstream
	/// pypowerwall/FleetAPI <c>get_history()</c> method.
	/// </summary>
	/// <param name="kind">The history kind (for example <c>power</c>, <c>energy</c>, <c>backup</c>, or <c>self_consumption</c>).</param>
	/// <param name="period">The aggregation period (for example <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>).</param>
	/// <param name="timeZone">IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> GetHistoryAsync (
		string? kind = null,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		try
			{
			JObject? response = await _connection!.GetHistoryAsync (_resolvedSiteId!, kind, period, timeZone, startDate, endDate, cancellationToken).ConfigureAwait (false);
			return Serialize (response?["response"] ?? response);
			}
		catch (PowerwallCloudEndpointRemovedException exc)
			{
			// Tesla retired the '/history' endpoint (HTTP 410). '/calendar_history' is the current
			// replacement and supports a superset of kinds, so point callers there explicitly.
			throw new PowerwallCloudEndpointRemovedException (
				"Tesla has permanently removed the '/history' energy endpoint (HTTP 410 Gone). "
				+ "Use GetCalendarHistoryAsync (the 'calendarhistory' command) instead.", exc);
			}
		}

	/// <summary>
	/// Returns raw calendar-aligned energy history for the active site (cloud mode only). Faithfully adapts
	/// the upstream pypowerwall/FleetAPI <c>get_calendar_history()</c> method.
	/// </summary>
	/// <param name="kind">The history kind (for example <c>power</c>, <c>soe</c>, <c>energy</c>, <c>backup</c>, <c>self_consumption</c>, <c>time_of_use_energy</c>, or <c>savings</c>).</param>
	/// <param name="period">The aggregation period (for example <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>).</param>
	/// <param name="timeZone">IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> GetCalendarHistoryAsync (
		string? kind = null,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		JObject? response = await _connection!.GetCalendarHistoryAsync (_resolvedSiteId!, kind, period, timeZone, startDate, endDate, cancellationToken).ConfigureAwait (false);
		return Serialize (response?["response"] ?? response);
		}

	/// <inheritdoc/>
	public override Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		EnsureConnected ();
		return MapPollAsync (api, force, cancellationToken);
		}

	/// <inheritdoc/>
	public override Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default)
		{
		// Cloud mode has no protobuf vitals path; mirror upstream by returning no binary payload.
		_log.Debug ($"Raw poll is not supported in cloud mode for {api}");
		return Task.FromResult<byte[]?> (null);
		}

	/// <inheritdoc/>
	public override Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default)
		{
		if (string.IsNullOrWhiteSpace (api))
			throw new ArgumentException ("API endpoint is required.", nameof (api));

		EnsureConnected ();
		return MapPostAsync (api, payload, din, cancellationToken);
		}

	/// <inheritdoc/>
	public override async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		JToken? vitals = await GetVitalsAsync (force: false, cancellationToken).ConfigureAwait (false);
		if (vitals is not JObject devices)
			return null;

		var result = new Dictionary<string, IReadOnlyDictionary<string, object?>> (StringComparer.Ordinal);
		foreach (JProperty device in devices.Properties ())
			{
			if (device.Value is not JObject telemetry)
				continue;

			var values = new Dictionary<string, object?> (StringComparer.Ordinal);
			foreach (JProperty attribute in telemetry.Properties ())
				values[attribute.Name] = attribute.Value.Type == JTokenType.Null ? null : attribute.Value.ToObject<object?> ();

			result[device.Name] = values;
			}

		return result;
		}

	/// <inheritdoc/>
	public override async Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		JObject? response = await GetCachedSiteDataAsync (
			"ENERGY_SITE_BACKUP_TIME_REMAINING",
			CacheExpireSeconds,
			(c, ct) => c.GetBackupTimeRemainingAsync (_resolvedSiteId!, ct),
			force: false,
			cancellationToken).ConfigureAwait (false);

		JToken? hours = response?["response"]?["time_remaining_hours"];
		if (hours is null)
			return 0.0;

		return hours.Type is JTokenType.Float or JTokenType.Integer ? hours.Value<double> () : 0.0;
		}

	private async Task<string?> MapPollAsync (string api, bool force, CancellationToken cancellationToken)
		{
		_log.Debug ($" -- cloud: Request for {api}");

		JToken? data = api switch
			{
			"/api/devices/vitals" => GetApiDevicesVitals (),
			"/api/meters/aggregates" => await GetApiMetersAggregatesAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/operation" => await GetApiOperationAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/site_info" => await GetApiSiteInfoAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/site_info/site_name" => await GetApiSiteInfoSiteNameAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/status" => await GetApiStatusAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/system_status" => await GetApiSystemStatusAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/system_status/grid_status" => await GetApiSystemStatusGridStatusAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/system_status/soe" => await GetApiSystemStatusSoeAsync (force, cancellationToken).ConfigureAwait (false),
			"/vitals" => await GetVitalsAsync (force, cancellationToken).ConfigureAwait (false),
			"/api/login/Basic" => MockObject ("api_login_basic", static () => new JObject { ["status"] = "ok" }),
			"/api/logout" => MockObject ("api_logout", static () => new JObject { ["status"] = "ok" }),
			"/api/auth/toggle/supported" => MockObject ("get_api_auth_toggle_supported", static () => new JObject { ["toggle_auth_supported"] = true }),
			"/api/customer" => MockObject ("get_api_customer", static () => new JObject { ["registered"] = true }),
			"/api/customer/registration" => MockParse ("get_api_customer_registration", """{"privacy_notice":null,"limited_warranty":null,"grid_services":null,"marketing":null,"registered":true,"timed_out_registration":false}"""),
			"/api/installer" => MockParse ("get_api_installer", CloudMockData.INSTALLER),
			"/api/meters" => MockParse ("get_api_meters", CloudMockData.METERS),
			"/api/meters/readings" => MockValue ("get_api_unimplemented_timeout", "TIMEOUT!"),
			"/api/meters/site" => MockParse ("get_api_meters_site", CloudMockData.METERS_SITE),
			"/api/meters/solar" => null,
			"/api/networks" => MockValue ("get_api_unimplemented_timeout", "TIMEOUT!"),
			"/api/powerwalls" => MockParse ("get_api_powerwalls", CloudMockData.POWERWALLS),
			"/api/site_info/grid_codes" => MockValue ("get_api_unimplemented_timeout", "TIMEOUT!"),
			"/api/sitemaster" => MockObject ("get_api_sitemaster", static () => new JObject
				{
				["status"] = "StatusUp",
				["running"] = true,
				["connected_to_tesla"] = true,
				["power_supply_mode"] = false,
				["can_reboot"] = "Yes"
				}),
			"/api/solar_powerwall" => MockObject ("get_api_solar_powerwall", static () => new JObject ()),
			"/api/solars" => MockParse ("get_api_solars", """[{"brand":"Tesla","model":"Solar Inverter 7.6","power_rating_watts":7600}]"""),
			"/api/solars/brands" => MockParse ("get_api_solars_brands", CloudMockData.SOLARS_BRANDS),
			"/api/synchrometer/ct_voltage_references" => MockParse ("get_api_synchrometer_ct_voltage_references", """{"ct1":"Phase1","ct2":"Phase2","ct3":"Phase1"}"""),
			"/api/system/update/status" => MockParse ("get_api_system_update_status", """{"state":"/update_succeeded","info":{"status":["nonactionable"]},"current_time":1702756114429,"last_status_time":1702753309227,"version":"23.28.2 27626f98","offline_updating":false,"offline_update_error":"","estimated_bytes_per_second":null}"""),
			"/api/system_status/grid_faults" => MockParse ("get_api_system_status_grid_faults", "[]"),
			"/api/troubleshooting/problems" => MockParse ("get_api_troubleshooting_problems", """{"problems":[]}"""),
			_ => new JObject { ["ERROR"] = $"Unknown API: {api}" }
			};

		return Serialize (data);
		}

	private async Task<string?> MapPostAsync (string api, object? payload, string? din, CancellationToken cancellationToken)
		{
		_log.Debug ($" -- cloud: Request for {api}");

		if (api != "/api/operation")
			return Serialize (new JObject { ["ERROR"] = $"Unknown API: {api}" });

		JObject? result = await PostApiOperationAsync (payload, din, cancellationToken).ConfigureAwait (false);
		if (result is not null)
			InvalidateCache (api);

		return Serialize (result);
		}

	private static JToken? GetApiDevicesVitals ()
		{
		_log.Warn ("Protobuf payload - not implemented for /api/devices/vitals - use /vitals instead");
		return null;
		}

	private async Task<JToken?> GetApiSystemStatusSoeAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? battery = await GetBatteryAsync (force, cancellationToken).ConfigureAwait (false);
		if (battery is null)
			return null;

		var percentageCharged = battery["response"]?.Value<double?> ("percentage_charged") ?? 0;
		var soe = (percentageCharged + RESERVE_SCALE_BUFFER) * 0.95;
		return new JObject { ["percentage"] = soe };
		}

	private async Task<JToken?> GetApiStatusAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? response = config?["response"];
		if (response is null)
			return null;

		return new JObject
			{
			["din"] = response["id"],
			["start_time"] = response["installation_date"],
			["up_time_seconds"] = null,
			["is_new"] = false,
			["version"] = response["version"],
			["git_hash"] = "27626f98a66cad5c665bbe1d4d788cdb3e94fd34",
			["commission_count"] = 0,
			["device_type"] = response["components"]?["gateway"],
			["teg_type"] = "unknown",
			["sync_type"] = "v2.1",
			["cellular_disabled"] = false,
			["can_reboot"] = true
			};
		}

	private async Task<JToken?> GetApiSystemStatusGridStatusAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? power = await GetSitePowerAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? response = power?["response"];
		if (response is null)
			return null;

		var gridStatusValue = response.Value<string> ("grid_status");
		var gridStatus = gridStatusValue is "Active" or "Unknown" or null or ""
			? "SystemGridConnected"
			: "SystemIslandedActive";

		return new JObject
			{
			["grid_status"] = gridStatus,
			["grid_services_active"] = response["grid_services_active"]
			};
		}

	private async Task<JToken?> GetApiSiteInfoSiteNameAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? response = config?["response"];
		if (response is null)
			return null;

		return new JObject
			{
			["site_name"] = response["site_name"],
			["timezone"] = response["installation_time_zone"]
			};
		}

	private async Task<JToken?> GetApiSiteInfoAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? response = config?["response"];
		if (response is null)
			return null;

		var nameplatePower = ParseLong (response["nameplate_power"]) / 1000.0;
		var nameplateEnergy = ParseLong (response["nameplate_energy"]) / 1000.0;

		return new JObject
			{
			["max_system_energy_kWh"] = nameplateEnergy,
			["max_system_power_kW"] = nameplatePower,
			["site_name"] = response["site_name"],
			["timezone"] = response["installation_time_zone"],
			["max_site_meter_power_kW"] = response["max_site_meter_power_ac"],
			["min_site_meter_power_kW"] = response["min_site_meter_power_ac"],
			["nominal_system_energy_kWh"] = nameplateEnergy,
			["nominal_system_power_kW"] = nameplatePower,
			["panel_max_current"] = null,
			["grid_code"] = new JObject
				{
				["grid_code"] = null,
				["grid_voltage_setting"] = null,
				["grid_freq_setting"] = null,
				["grid_phase_setting"] = null,
				["country"] = null,
				["state"] = null,
				["utility"] = response["tariff_content"]?["utility"]
				}
			};
		}

	private async Task<JToken?> GetVitalsAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JObject? power = await GetSitePowerAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? configResponse = config?["response"];
		JToken? powerResponse = power?["response"];
		if (configResponse is null || powerResponse is null)
			return null;

		var din = configResponse.Value<string> ("id");
		string? partNumber = null;
		string? serialNumber = null;
		if (din is not null)
			{
			var parts = din.Split (["--"], StringSplitOptions.None);
			if (parts.Length == 2)
				{
				partNumber = parts[0];
				serialNumber = parts[1];
				}
			}

		var islandStatus = powerResponse.Value<string> ("island_status");
		var alert = islandStatus switch
			{
			"on_grid" => "SystemConnectedToGrid",
			"off_grid_intentional" => "ScheduledIslandContactorOpen",
			"off_grid" => "UnscheduledIslandContactorOpen",
			_ => powerResponse.Value<string> ("grid_status") is "Active" or "Unknown" ? "SystemConnectedToGrid" : ""
			};

		var deviceKey = $"STSTSM--{partNumber}--{serialNumber}";
		return new JObject
			{
			[deviceKey] = new JObject
				{
				["partNumber"] = partNumber,
				["serialNumber"] = serialNumber,
				["manufacturer"] = "Simulated",
				["firmwareVersion"] = configResponse["version"],
				["lastCommunicationTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds (),
				["teslaEnergyEcuAttributes"] = new JObject { ["ecuType"] = 207 },
				["STSTSM-Location"] = "Simulated",
				["alerts"] = new JArray { alert }
				}
			};
		}

	private async Task<JToken?> GetApiMetersAggregatesAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JObject? power = await GetSitePowerAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? configResponse = config?["response"];
		JToken? powerResponse = power?["response"];
		if (configResponse is null || powerResponse is null)
			return null;

		JToken? timestamp = powerResponse["timestamp"];
		JToken? batteryCount = configResponse["battery_count"];

		int solarInverters;
		if (configResponse["components"]?["inverters"] is JArray inverters)
			solarInverters = inverters.Count;
		else if (configResponse["components"]?["solar"] is { } solar && solar.Type != JTokenType.Null)
			solarInverters = 1;
		else
			solarInverters = 0;

		var data = JObject.Parse (CloudMockData.METERS_AGGREGATES_TEMPLATE);
		MergeInto ((JObject) data["site"]!, new JObject
			{
			["last_communication_time"] = timestamp,
			["instant_power"] = powerResponse["grid_power"]
			});
		MergeInto ((JObject) data["battery"]!, new JObject
			{
			["last_communication_time"] = timestamp,
			["instant_power"] = powerResponse["battery_power"],
			["num_meters_aggregated"] = batteryCount
			});
		MergeInto ((JObject) data["load"]!, new JObject
			{
			["last_communication_time"] = timestamp,
			["instant_power"] = powerResponse["load_power"]
			});
		MergeInto ((JObject) data["solar"]!, new JObject
			{
			["last_communication_time"] = timestamp,
			["instant_power"] = powerResponse["solar_power"],
			["num_meters_aggregated"] = solarInverters
			});

		return data;
		}

	private async Task<JToken?> GetApiOperationAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? response = config?["response"];
		if (response is null)
			return null;

		var backupReservePercent = response.Value<double?> ("backup_reserve_percent") ?? 0;
		var backup = (backupReservePercent + RESERVE_SCALE_BUFFER) * 0.95;
		return new JObject
			{
			["real_mode"] = response["default_real_mode"],
			["backup_reserve_percent"] = backup
			};
		}

	private async Task<JToken?> GetApiSystemStatusAsync (bool force, CancellationToken cancellationToken)
		{
		JObject? power = await GetSitePowerAsync (force, cancellationToken).ConfigureAwait (false);
		JObject? config = await GetSiteConfigAsync (force, cancellationToken).ConfigureAwait (false);
		JObject? battery = await GetBatteryAsync (force, cancellationToken).ConfigureAwait (false);
		JToken? powerResponse = power?["response"];
		JToken? configResponse = config?["response"];
		JToken? batteryResponse = battery?["response"];
		if (powerResponse is null || configResponse is null || batteryResponse is null)
			return null;

		JToken? batteryCount = configResponse["battery_count"];
		JToken? nameplatePower = configResponse["nameplate_power"];

		string gridStatus;
		if (powerResponse.Value<string> ("island_status") == "on_grid")
			{
			gridStatus = "SystemGridConnected";
			}
		else
			{
			gridStatus = powerResponse.Value<string> ("grid_status") is "Active" or "Unknown"
				? "SystemGridConnected"
				: "SystemIslandedActive";
			}

		var data = JObject.Parse (CloudMockData.SYSTEM_STATUS_TEMPLATE);
		MergeInto (data, new JObject
			{
			["nominal_full_pack_energy"] = batteryResponse["total_pack_energy"],
			["nominal_energy_remaining"] = batteryResponse["energy_left"],
			["max_charge_power"] = nameplatePower,
			["max_discharge_power"] = nameplatePower,
			["max_apparent_power"] = nameplatePower,
			["grid_services_power"] = powerResponse["grid_services_power"],
			["system_island_state"] = gridStatus,
			["available_blocks"] = batteryCount,
			["solar_real_power_limit"] = powerResponse["solar_power"],
			["blocks_controlled"] = batteryCount
			});

		return data;
		}

	private async Task<JObject?> PostApiOperationAsync (object? payload, string? din, CancellationToken cancellationToken)
		{
		JObject payloadObject = payload is null ? new JObject () : JObject.FromObject (payload);
		JToken? reserveToken = payloadObject["backup_reserve_percent"];
		var hasReserve = reserveToken is not null && reserveToken.Type != JTokenType.Null && reserveToken.Type != JTokenType.Boolean;
		var realMode = payloadObject.Value<string> ("real_mode");

		if (!hasReserve && string.IsNullOrEmpty (realMode))
			{
			throw new PowerwallCloudInvalidPayloadException (
				"/api/operation payload missing required parameters. Either 'backup_reserve_percent' or 'real_mode', or both, must be present.");
			}

		if (string.IsNullOrWhiteSpace (din))
			_log.Warn ("No valid DIN provided, will adjust the configured site battery.");

		var reservePercent = (int) Math.Round (payloadObject.Value<double?> ("backup_reserve_percent") ?? 0);

		try
			{
			JObject? levelResult = await _connection!.SetBackupReserveAsync (_resolvedSiteId!, reservePercent, cancellationToken).ConfigureAwait (false);
			JObject? modeResult = realMode is null
				? null
				: await _connection.SetOperationModeAsync (_resolvedSiteId!, realMode, cancellationToken).ConfigureAwait (false);

			return new JObject
				{
				["set_backup_reserve_percent"] = new JObject
					{
					["backup_reserve_percent"] = reservePercent,
					["din"] = din,
					["result"] = ExtractCommandResult (levelResult)
					},
				["set_operation"] = new JObject
					{
					["real_mode"] = realMode,
					["din"] = din,
					["result"] = ExtractCommandResult (modeResult)
					}
				};
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
			return new JObject { ["error"] = exc.Message };
			}
		}

	private Task<JObject?> GetSiteConfigAsync (bool force, CancellationToken cancellationToken) =>
		GetCachedSiteDataAsync (
			"SITE_CONFIG",
			SITE_CONFIG_TTL_SECONDS,
			(c, ct) => c.GetSiteConfigAsync (_resolvedSiteId!, ct),
			force,
			cancellationToken);

	private async Task<JObject?> GetSitePowerAsync (bool force, CancellationToken cancellationToken)
		{
		var cachedBefore = IsCloudCacheValid ("SITE_DATA", CacheExpireSeconds);
		var counter = _counter + 1;
		JObject? response = await GetCachedSiteDataAsync (
			"SITE_DATA",
			CacheExpireSeconds,
			(c, ct) => c.GetSitePowerAsync (_resolvedSiteId!, counter, ct),
			force,
			cancellationToken).ConfigureAwait (false);

		if (!cachedBefore && response is not null)
			_counter = (_counter + 1) % COUNTER_MAX;

		return response;
		}

	private Task<JObject?> GetBatteryAsync (bool force, CancellationToken cancellationToken) =>
		GetCachedSiteDataAsync (
			"SITE_SUMMARY",
			CacheExpireSeconds,
			(c, ct) => c.GetSiteSummaryAsync (_resolvedSiteId!, ct),
			force,
			cancellationToken);

	private async Task<JObject?> GetCachedSiteDataAsync (
		string name,
		int ttlSeconds,
		Func<TeslaCloudConnection, CancellationToken, Task<JObject?>> fetch,
		bool force,
		CancellationToken cancellationToken)
		{
		if (!force && IsCloudCacheValid (name, ttlSeconds) && _cloudCache.TryGetValue (name, out JToken? cached))
			{
			_log.Debug ($" -- cloud: Returning cached {name} data");
			return cached as JObject;
			}

		JObject? response = await fetch (_connection!, cancellationToken).ConfigureAwait (false);
		if (response is not null)
			{
			_cloudCache[name] = response;
			_cloudCacheTimes[name] = NowSeconds;
			_log.Debug ($" -- cloud: Retrieved {name} data");
			}

		return response;
		}

	private bool IsCloudCacheValid (string name, int ttlSeconds) =>
		_cloudCache.ContainsKey (name)
		&& _cloudCacheTimes.TryGetValue (name, out var cachedAt)
		&& cachedAt > NowSeconds - ttlSeconds;

	private async Task<List<JObject>?> FetchEnergySitesAsync (CancellationToken cancellationToken)
		{
		JArray? products = await _connection!.GetProductsAsync (cancellationToken).ConfigureAwait (false);
		return products?
			.OfType<JObject> ()
			.Where (static p => p.Value<string> ("resource_type") is "battery" or "solar")
			.ToList ();
		}

	private void InvalidateCloudCache (string name)
		{
		_cloudCache.Remove (name);
		_cloudCacheTimes.Remove (name);
		}

	private void ClearCloudCache ()
		{
		_cloudCache.Clear ();
		_cloudCacheTimes.Clear ();
		}

	private string SelectSite (List<JObject> sites)
		{
		if (SiteId is null)
			return GetSiteId (sites[0]);

		foreach (JObject site in sites)
			{
			if (GetSiteId (site) == SiteId)
				return SiteId;
			}

		_log.Warn ($"Site {SiteId} not found for {Email} - defaulting to first site.");
		return GetSiteId (sites[0]);
		}

	private static string GetSiteId (JObject site) =>
		site.Value<string> ("energy_site_id")
		?? site.Value<long?> ("energy_site_id")?.ToString (CultureInfo.InvariantCulture)
		?? site.Value<string> ("id")
		?? string.Empty;

	private void EnsureConnected ()
		{
		if (_connection is null || _resolvedSiteId is null)
			throw new PowerwallCloudTeslaNotConnectedException ("Not connected to the Tesla cloud. Call AuthenticateAsync before invoking data methods.");
		}

	private static JObject MockObject (string name, Func<JObject> factory)
		{
		LogMockUsage (name);
		return factory ();
		}

	private static JToken MockParse (string name, string json)
		{
		LogMockUsage (name);
		return JToken.Parse (json);
		}

	private static JValue MockValue (string name, string value)
		{
		LogMockUsage (name);
		return new JValue (value);
		}

	private static void LogMockUsage (string name) =>
		_log.Debug ($"This API [{name}] is using mock data in cloud mode.");

	private static JToken ExtractCommandResult (JObject? response)
		{
		if (response is null)
			return "BatteryNotFound";

		// Tesla command responses wrap the outcome in a response envelope.
		return response["response"] ?? response;
		}

	private static void MergeInto (JObject target, JObject updates)
		{
		foreach (JProperty property in updates.Properties ())
			target[property.Name] = property.Value;
		}

	private static long ParseLong (JToken? token)
		{
		if (token is null || token.Type == JTokenType.Null)
			return 0;

		return token.Type switch
			{
			JTokenType.Integer or JTokenType.Float => token.Value<long> (),
			JTokenType.String => long.TryParse (token.Value<string> (), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
			_ => 0
			};
		}

	private static string? Serialize (JToken? token) =>
		token is null ? null : token.ToString (Formatting.None);

	private void OnConnectionTokensRefreshed (object? sender, ConnectionTokensRefreshedEventArgs e)
		{
		// Tesla refreshed the tokens; always persist internally so the next run reconnects without a fresh
		// login (unless the caller has opted out of library-owned persistence). This happens regardless of
		// whether the public event below fires.
		_tokenCache?.SaveTokens (e.AccessToken, e.RefreshToken);

		// Surface on the public event according to whether an access token was supplied when the raising
		// connection was constructed (either explicitly by the caller, or resolved from the library's own
		// cache):
		//  - Access token was provided: every refresh is significant to the caller (it owns/tracks the access
		//    token too), so report it in full on every call.
		//  - No access token was provided (pure refresh-token bootstrap): the caller never had an access
		//    token to begin with and only cares about the durable credential, so only report when the
		//    refresh token itself changed, and omit the access token from the notification.
		var accessTokenProvided = (sender as TeslaCloudConnection)?.AccessTokenProvidedAtConstruction ?? false;
		if (accessTokenProvided)
			TokensRefreshed?.Invoke (this, new CloudTokensRefreshedEventArgs (e.AccessToken, e.RefreshToken));
		else if (e.RefreshTokenChanged)
			TokensRefreshed?.Invoke (this, new CloudTokensRefreshedEventArgs (null, e.RefreshToken));
		}

	/// <summary>Releases the underlying Tesla cloud connection.</summary>
	public void Dispose ()
		{
		if (_connection is not null)
			_connection.TokensRefreshed -= OnConnectionTokensRefreshed;

		_connection?.Dispose ();
		_connection = null;
		}
	}
