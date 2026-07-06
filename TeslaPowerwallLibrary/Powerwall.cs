// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using log4net;

using Newtonsoft.Json.Linq;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary;

/// <summary>
/// High-level, async-first client representing a Tesla™ Energy Gateway Powerwall™ device.
/// This is the primary entry point of the library and delegates to a mode-specific
/// <see cref="PowerwallClientBase"/> implementation (currently local mode).
/// </summary>
/// <remarks>
/// This type is an idiomatic .NET adaptation of the Python <c>pypowerwall</c> <c>Powerwall</c> class.
/// Construct an instance with <see cref="PowerwallOptions"/> and call <see cref="ConnectAsync"/> before
/// invoking any data methods.
/// </remarks>
public sealed class Powerwall : IDisposable
	{
	private static readonly ILog _log = LogManager.GetLogger (typeof (Powerwall));

	/// <summary>Gets the valid <c>kind</c> values accepted by <see cref="GetHistoryAsync"/>.</summary>
	public static IReadOnlyList<string> HistoryKinds { get; } =
		["power", "energy", "backup", "self_consumption"];

	/// <summary>Gets the valid <c>kind</c> values accepted by <see cref="GetCalendarHistoryAsync"/>.</summary>
	public static IReadOnlyList<string> CalendarHistoryKinds { get; } =
		["power", "soe", "energy", "backup", "self_consumption", "time_of_use_energy", "savings"];

	/// <summary>Gets the valid <c>period</c> values accepted by the energy-history methods.</summary>
	public static IReadOnlyList<string> HistoryPeriods { get; } =
		["day", "week", "month", "year", "lifetime"];

	/// <summary>Gets the default <c>period</c> applied by the energy-history methods when the caller does not specify one.</summary>
	public const string DEFAULT_HISTORY_PERIOD = "day";

	private static readonly HashSet<string> _historyKinds = new (HistoryKinds, StringComparer.Ordinal);

	private static readonly HashSet<string> _calendarHistoryKinds = new (CalendarHistoryKinds, StringComparer.Ordinal);

	private static readonly HashSet<string> _historyPeriods = new (HistoryPeriods, StringComparer.Ordinal);

	private readonly PowerwallOptions _options;
	private PowerwallClientBase? _client;

	/// <summary>
	/// Initializes a new instance of the <see cref="Powerwall"/> class.
	/// </summary>
	/// <param name="options">Connection and behavior options.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="PowerwallInvalidConfigurationException">Thrown when the supplied options fail validation.</exception>
	public Powerwall (PowerwallOptions options)
		{
		_options = options ?? throw new ArgumentNullException (nameof (options));

		Mode = ResolveMode (options);
		ValidateConfiguration ();
		}

	/// <summary>Gets the resolved connection mode for this instance.</summary>
	public PowerwallMode Mode { get; private set; }

	/// <summary>
	/// Gets the customer email associated with this connection: the account used for cloud/FleetAPI
	/// authentication and cloud token cache lookups, or the account label configured for local mode. Mirrors
	/// the upstream <c>pypowerwall</c> <c>self.email</c> attribute, letting callers display which account is
	/// currently active even though the token cache can hold entries for many different accounts.
	/// </summary>
	public string Email => _client?.Email ?? _options.Email;

	/// <summary>
	/// Determines whether the library has cloud tokens persisted for the specified account, so callers can
	/// decide whether a first-time interactive login is required. Tokens are cached internally after the first
	/// successful cloud connect and reused automatically thereafter.
	/// </summary>
	/// <param name="email">The customer email the cached tokens are keyed by.</param>
	/// <param name="authPath">
	/// The token cache directory or file. When empty, the per-user default location is used, matching the
	/// default used when connecting.
	/// </param>
	/// <returns><see langword="true"/> when a usable cached token exists; otherwise <see langword="false"/>.</returns>
	public static bool HasStoredCloudTokens (string email, string authPath = "") =>
		new TeslaCloudTokenCache (authPath, email).Load ().HasToken;

	/// <summary>
	/// Gets the full path of the library-managed cloud token cache file for the specified account, for
	/// diagnostics or display.
	/// </summary>
	/// <param name="email">The customer email the cache entry is keyed by.</param>
	/// <param name="authPath">The token cache directory or file; when empty, the per-user default is used.</param>
	/// <returns>The resolved cache file path.</returns>
	public static string GetCloudTokenCachePath (string email, string authPath = "") =>
		new TeslaCloudTokenCache (authPath, email).FilePath;

	/// <summary>
	/// Reads the cloud tokens the library persisted for the specified account, so callers can display them
	/// even when not connected. This is the offline counterpart to <see cref="CloudAccessToken"/> and
	/// <see cref="CloudRefreshToken"/>, which reflect the live tokens of an active connection.
	/// </summary>
	/// <param name="email">The customer email the cached tokens are keyed by.</param>
	/// <param name="accessToken">When this method returns, the cached access token, or <see langword="null"/>.</param>
	/// <param name="refreshToken">When this method returns, the cached refresh token, or <see langword="null"/>.</param>
	/// <param name="authPath">The token cache directory or file; when empty, the per-user default is used.</param>
	/// <returns><see langword="true"/> when at least one token was found; otherwise <see langword="false"/>.</returns>
	public static bool TryGetStoredCloudTokens (string email, out string? accessToken, out string? refreshToken, string authPath = "")
		{
		CloudTokenCacheEntry entry = new TeslaCloudTokenCache (authPath, email).Load ();
		accessToken = entry.AccessToken;
		refreshToken = entry.RefreshToken;
		return entry.HasToken;
		}

	/// <summary>
	/// Removes any cloud tokens and remembered site the library persisted for the specified account. Use this
	/// to sign a user out so the next cloud connect requires a fresh login.
	/// </summary>
	/// <param name="email">The customer email whose cached entry should be cleared.</param>
	/// <param name="authPath">The token cache directory or file; when empty, the per-user default is used.</param>
	public static void ClearStoredCloudTokens (string email, string authPath = "")
		{
		var cache = new TeslaCloudTokenCache (authPath, email);
		cache.SaveTokens (null, null);
		cache.SaveSite (null);
		}

	/// <summary>Gets a value indicating whether a backend client has been connected.</summary>
	public bool IsClientConnected => _client is not null;

	/// <summary>
	/// Raised in cloud mode after the Tesla Owners API access token is refreshed, carrying the current
	/// tokens. Long-running callers should persist the (possibly rotated) refresh token so it can be
	/// reused on a later run. Raised on the thread that triggered the refresh, which may be a background
	/// polling thread; handlers must be thread-safe and non-blocking.
	/// </summary>
	public event EventHandler<CloudTokensRefreshedEventArgs>? CloudTokensRefreshed;

	/// <summary>
	/// Gets the current Tesla Owners API access token in cloud mode (updated after any refresh), or
	/// <see langword="null"/> when not connected in cloud mode.
	/// </summary>
	public string? CloudAccessToken => (_client as PowerwallCloudClient)?.CurrentAccessToken;

	/// <summary>
	/// Gets the current Tesla Owners API refresh token in cloud mode (updated after any rotation), or
	/// <see langword="null"/> when not connected in cloud mode.
	/// </summary>
	public string? CloudRefreshToken => (_client as PowerwallCloudClient)?.CurrentRefreshToken;

	/// <summary>
	/// Gets the Tesla energy site identifier the active cloud connection resolved to (either the caller's
	/// requested site or the library-remembered/default one), or <see langword="null"/> when not connected
	/// in cloud mode.
	/// </summary>
	public string? CloudSiteId => (_client as PowerwallCloudClient)?.SiteId;

	/// <summary>
	/// Establishes a connection to the Tesla Energy Gateway using the configured mode.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the connection succeeds; otherwise <see langword="false"/>.</returns>
	/// <exception cref="PowerwallInvalidConfigurationException">Thrown when the mode cannot be determined.</exception>
	public async Task<bool> ConnectAsync (CancellationToken cancellationToken = default)
		{
		switch (Mode)
			{
			case PowerwallMode.Local:
				var localClient = new PowerwallLocalClient (
					_options.Host,
					_options.Password,
					_options.Email,
					_options.Timezone,
					_options.Timeout,
					_options.CacheExpireSeconds,
					_options.AuthMode,
					_options.CacheFile);
				try
					{
					await localClient.AuthenticateAsync (cancellationToken).ConfigureAwait (false);
					}
				catch (Exception exc) when (exc is PowerwallException)
					{
					_log.Warn ($"Failed to connect using Local mode: {exc.Message}");
					localClient.Dispose ();
					return false;
					}

				_client = localClient;
				return true;

			case PowerwallMode.Cloud:
				var cloudClient = new PowerwallCloudClient (
					_options.Email,
					_options.CacheExpireSeconds,
					_options.Timeout,
					_options.AccessToken,
					_options.RefreshToken,
					_options.SiteId,
					_options.AuthPath,
					_options.NoCloudTokenPersistence);
				try
					{
					await cloudClient.AuthenticateAsync (cancellationToken).ConfigureAwait (false);
					}
				catch (PowerwallCloudNoTeslaAuthFileException)
					{
					// Missing or unusable tokens are a configuration problem the caller must fix (run setup),
					// so surface it rather than reporting a transient connection failure.
					cloudClient.Dispose ();
					throw;
					}
				catch (Exception exc) when (exc is PowerwallException)
					{
					_log.Warn ($"Failed to connect using Cloud mode: {exc.Message}");
					cloudClient.Dispose ();
					return false;
					}

				_client = cloudClient;
				cloudClient.TokensRefreshed += OnCloudTokensRefreshed;
				return true;

			case PowerwallMode.FleetApi:
				throw new PowerwallInvalidConfigurationException (
					$"Connection mode '{Mode}' is not yet implemented in this version of the library.");

			default:
				_log.Error ("Unable to determine mode to connect.");
				throw new PowerwallInvalidConfigurationException ("Unable to determine mode to connect.");
			}
		}

	/// <summary>
	/// Determines whether the gateway is reachable by attempting to read its status.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the gateway responds with status; otherwise <see langword="false"/>.</returns>
	public async Task<bool> IsConnectedAsync (CancellationToken cancellationToken = default)
		{
		try
			{
			return await StatusAsync (cancellationToken).ConfigureAwait (false) is not null;
			}
		catch (PowerwallException)
			{
			return false;
			}
		}

	/// <summary>
	/// Queries the gateway for the raw response body of an arbitrary API endpoint.
	/// </summary>
	/// <param name="api">The API endpoint to query.</param>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when no payload is available.</returns>
	public Task<string?> PollAsync (string api, bool force = false, CancellationToken cancellationToken = default) =>
		RequireClient ().PollAsync (api, force, cancellationToken: cancellationToken);

	/// <summary>
	/// Sends a command to an arbitrary API endpoint.
	/// </summary>
	/// <param name="api">The API endpoint to post to.</param>
	/// <param name="payload">The payload to send; serialized as JSON.</param>
	/// <param name="din">System DIN, when required by the endpoint.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when no payload is available.</returns>
	public Task<string?> PostAsync (string api, object? payload, string? din = null, CancellationToken cancellationToken = default) =>
		RequireClient ().PostAsync (api, payload, din, cancellationToken: cancellationToken);

	/// <summary>
	/// Returns the battery charge level percentage.
	/// </summary>
	/// <param name="scale">When <see langword="true"/>, converts the raw level to the Tesla app scale.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The battery level percentage, or <see langword="null"/> when unavailable.</returns>
	public async Task<double?> LevelAsync (bool scale = false, CancellationToken cancellationToken = default)
		{
		var payload = await RequireClient ().PollAsync ("/api/system_status/soe", cancellationToken: cancellationToken).ConfigureAwait (false);
		StateOfEnergy? soe = JsonHelper.DeserializeOrNull<StateOfEnergy> (payload);
		if (soe is null)
			return null;

		var level = soe.Percentage;
		return scale ? (level / 0.95) - (5 / 0.95) : level;
		}

	/// <summary>
	/// Returns the instantaneous power flows for site, solar, battery, and load.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>A <see cref="PowerSnapshot"/> with the four flows in watts.</returns>
	public Task<PowerSnapshot> PowerAsync (CancellationToken cancellationToken = default) =>
		RequireClient ().PowerAsync (cancellationToken);

	/// <summary>Returns the grid (site) power in watts.</summary>
	/// <param name="verbose">When <see langword="true"/>, reads directly from the meter aggregates endpoint.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The site power in watts, or <see langword="null"/> when unavailable.</returns>
	public Task<double?> SiteAsync (bool verbose = false, CancellationToken cancellationToken = default) =>
		RequireClient ().FetchPowerAsync ("site", verbose, cancellationToken);

	/// <summary>Returns the solar generation power in watts.</summary>
	/// <param name="verbose">When <see langword="true"/>, reads directly from the meter aggregates endpoint.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The solar power in watts, or <see langword="null"/> when unavailable.</returns>
	public Task<double?> SolarAsync (bool verbose = false, CancellationToken cancellationToken = default) =>
		RequireClient ().FetchPowerAsync ("solar", verbose, cancellationToken);

	/// <summary>Returns the battery power flow in watts.</summary>
	/// <param name="verbose">When <see langword="true"/>, reads directly from the meter aggregates endpoint.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The battery power in watts, or <see langword="null"/> when unavailable.</returns>
	public Task<double?> BatteryAsync (bool verbose = false, CancellationToken cancellationToken = default) =>
		RequireClient ().FetchPowerAsync ("battery", verbose, cancellationToken);

	/// <summary>Returns the home (load) power usage in watts.</summary>
	/// <param name="verbose">When <see langword="true"/>, reads directly from the meter aggregates endpoint.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The load power in watts, or <see langword="null"/> when unavailable.</returns>
	public Task<double?> LoadAsync (bool verbose = false, CancellationToken cancellationToken = default) =>
		RequireClient ().FetchPowerAsync ("load", verbose, cancellationToken);

	/// <summary>Alias for <see cref="SiteAsync"/>; returns the grid power usage in watts.</summary>
	/// <param name="verbose">When <see langword="true"/>, reads directly from the meter aggregates endpoint.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The grid power in watts, or <see langword="null"/> when unavailable.</returns>
	public Task<double?> GridAsync (bool verbose = false, CancellationToken cancellationToken = default) =>
		SiteAsync (verbose, cancellationToken);

	/// <summary>Alias for <see cref="LoadAsync"/>; returns the home power usage in watts.</summary>
	/// <param name="verbose">When <see langword="true"/>, reads directly from the meter aggregates endpoint.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The home power in watts, or <see langword="null"/> when unavailable.</returns>
	public Task<double?> HomeAsync (bool verbose = false, CancellationToken cancellationToken = default) =>
		LoadAsync (verbose, cancellationToken);

	/// <summary>
	/// Returns the configured site name from <c>/api/site_info/site_name</c>.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The site name, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> SiteNameAsync (CancellationToken cancellationToken = default)
		{
		var payload = await RequireClient ().PollAsync ("/api/site_info/site_name", cancellationToken: cancellationToken).ConfigureAwait (false);
		return JsonHelper.DeserializeOrNull<SiteName> (payload)?.Name;
		}

	/// <summary>
	/// Returns the deserialized gateway status from <c>/api/status</c>.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The <see cref="GatewayStatus"/>, or <see langword="null"/> when unavailable.</returns>
	public async Task<GatewayStatus?> StatusAsync (CancellationToken cancellationToken = default)
		{
		var payload = await RequireClient ().PollAsync ("/api/status", cancellationToken: cancellationToken).ConfigureAwait (false);
		return JsonHelper.DeserializeOrNull<GatewayStatus> (payload);
		}

	/// <summary>Returns the gateway firmware version string.</summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The version string, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> VersionAsync (CancellationToken cancellationToken = default) =>
		(await StatusAsync (cancellationToken).ConfigureAwait (false))?.Version;

	/// <summary>Returns the gateway firmware version as a comparable integer.</summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The comparable integer version, or <see langword="null"/> when unavailable.</returns>
	public async Task<long?> VersionIntAsync (CancellationToken cancellationToken = default) =>
		VersionHelper.ParseVersion (await VersionAsync (cancellationToken).ConfigureAwait (false));

	/// <summary>Returns the system uptime string.</summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The uptime string, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> UptimeAsync (CancellationToken cancellationToken = default) =>
		(await StatusAsync (cancellationToken).ConfigureAwait (false))?.UpTimeSeconds;

	/// <summary>Returns the system device identification number (DIN).</summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The DIN, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> DinAsync (CancellationToken cancellationToken = default) =>
		(await StatusAsync (cancellationToken).ConfigureAwait (false))?.Din;

	/// <summary>
	/// Returns the full system status from <c>/api/system_status</c>.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The <see cref="SystemStatus"/>, or <see langword="null"/> when unavailable.</returns>
	public async Task<SystemStatus?> SystemStatusAsync (CancellationToken cancellationToken = default)
		{
		var payload = await RequireClient ().PollAsync ("/api/system_status", cancellationToken: cancellationToken).ConfigureAwait (false);
		return JsonHelper.DeserializeOrNull<SystemStatus> (payload);
		}

	/// <summary>
	/// Returns detailed per-battery information keyed by package serial number.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>A map of serial number to <see cref="BatteryBlock"/>, or <see langword="null"/> when unavailable.</returns>
	public async Task<IReadOnlyDictionary<string, BatteryBlock>?> BatteryBlocksAsync (CancellationToken cancellationToken = default)
		{
		SystemStatus? status = await SystemStatusAsync (cancellationToken).ConfigureAwait (false);
		if (status?.BatteryBlocks is null)
			return null;

		var result = new Dictionary<string, BatteryBlock> ();
		foreach (BatteryBlock block in status.BatteryBlocks)
			{
			if (!string.IsNullOrWhiteSpace (block.PackageSerialNumber))
				result[block.PackageSerialNumber!] = block;
			}

		return result;
		}

	/// <summary>
	/// Returns the grid status as a normalized value (Up, Down, or Syncing).
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The <see cref="GridStatus"/>, or <see langword="null"/> when it cannot be determined.</returns>
	public async Task<GridStatus?> GridStatusAsync (CancellationToken cancellationToken = default)
		{
		var payload = await RequireClient ().PollAsync ("/api/system_status/grid_status", cancellationToken: cancellationToken).ConfigureAwait (false);
		GridStatusResponse? response = JsonHelper.DeserializeOrNull<GridStatusResponse> (payload);
		return response?.GridStatus switch
			{
			"SystemGridConnected" => GridStatus.Up,
			"SystemIslandedActive" => GridStatus.Down,
			"SystemMicroGridFaulted" => GridStatus.Down,
			"SystemWaitForUser" => GridStatus.Down,
			"SystemTransitionToGrid" => GridStatus.Syncing,
			"SystemTransitionToIsland" => GridStatus.Syncing,
			"SystemIslandedReady" => GridStatus.Syncing,
			_ => null
			};
		}

	/// <summary>
	/// Returns the battery backup reserve percentage.
	/// </summary>
	/// <param name="scale">When <see langword="true"/> (default), applies the Tesla app 5% reserve calculation.</param>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The reserve percentage, or <see langword="null"/> when unavailable.</returns>
	public async Task<double?> GetReserveAsync (bool scale = true, bool force = false, CancellationToken cancellationToken = default)
		{
		OperationResponse? operation = await GetOperationAsync (force, cancellationToken).ConfigureAwait (false);
		return operation?.BackupReservePercent is not double percent ? null : scale ? Math.Max (0, (percent / 0.95) - (5 / 0.95)) : percent;
		}

	/// <summary>
	/// Returns the active battery operation mode.
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The operation mode, or <see langword="null"/> when unavailable.</returns>
	public async Task<string?> GetModeAsync (bool force = false, CancellationToken cancellationToken = default) =>
		(await GetOperationAsync (force, cancellationToken).ConfigureAwait (false))?.RealMode;

	/// <summary>
	/// Sets the battery backup reserve level.
	/// </summary>
	/// <param name="level">Reserve level in percent (0 - 100).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw operation result body, or <see langword="null"/> when the call fails.</returns>
	/// <exception cref="InvalidBatteryReserveLevelException">Thrown when <paramref name="level"/> is outside 0 - 100.</exception>
	public Task<string?> SetReserveAsync (double level, CancellationToken cancellationToken = default) =>
		SetOperationAsync (level, null, cancellationToken);

	/// <summary>
	/// Sets the battery operation mode.
	/// </summary>
	/// <param name="mode">Operation mode (for example <c>self_consumption</c>, <c>backup</c>, or <c>autonomous</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw operation result body, or <see langword="null"/> when the call fails.</returns>
	public Task<string?> SetModeAsync (string mode, CancellationToken cancellationToken = default) =>
		SetOperationAsync (null, mode, cancellationToken);

	/// <summary>
	/// Sets the battery operation mode and/or reserve level.
	/// </summary>
	/// <param name="level">Reserve level in percent (0 - 100); when <see langword="null"/>, the current level is retained.</param>
	/// <param name="mode">Operation mode; when <see langword="null"/>, the current mode is retained.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw operation result body, or <see langword="null"/> when the call fails.</returns>
	/// <exception cref="InvalidBatteryReserveLevelException">Thrown when <paramref name="level"/> is outside 0 - 100.</exception>
	public async Task<string?> SetOperationAsync (double? level = null, string? mode = null, CancellationToken cancellationToken = default)
		{
		if (level is < 0 or > 100)
			throw new InvalidBatteryReserveLevelException ("Level can be in range of 0 to 100 only.");

		var effectiveLevel = level ?? await GetReserveAsync (cancellationToken: cancellationToken).ConfigureAwait (false) ?? 0;
		var effectiveMode = mode ?? await GetModeAsync (cancellationToken: cancellationToken).ConfigureAwait (false);

		var payload = new Dictionary<string, object?>
			{
			["backup_reserve_percent"] = effectiveLevel > 0 ? effectiveLevel : (object) false,
			["real_mode"] = effectiveMode
			};

		var din = await DinAsync (cancellationToken).ConfigureAwait (false);
		return await RequireClient ().PostAsync ("/api/operation", payload, din, cancellationToken: cancellationToken).ConfigureAwait (false);
		}

	/// <summary>
	/// Returns the estimated backup time remaining on the battery, in hours.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The time remaining in hours, or <see langword="null"/> when it cannot be determined.</returns>
	public Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default) =>
		RequireClient ().GetTimeRemainingAsync (cancellationToken);

	/// <summary>
	/// Returns the list of Tesla energy sites available to the authenticated account (cloud mode only).
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The available sites, or an empty list when none are found.</returns>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	public Task<IReadOnlyList<CloudSite>> GetSitesAsync (CancellationToken cancellationToken = default) =>
		RequireCloudClient ().GetSitesAsync (cancellationToken);

	/// <summary>
	/// Switches the active Tesla energy site without reconnecting (cloud mode only).
	/// </summary>
	/// <param name="siteId">The Tesla energy site identifier to switch to.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the site was found and selected; otherwise <see langword="false"/>.</returns>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	public Task<bool> ChangeSiteAsync (string siteId, CancellationToken cancellationToken = default) =>
		RequireCloudClient ().ChangeSiteAsync (siteId, cancellationToken);

	/// <summary>
	/// Enables or disables charging the battery from the grid (cloud mode only).
	/// </summary>
	/// <param name="enabled"><see langword="true"/> to allow grid charging; <see langword="false"/> to disallow it.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when the call fails.</returns>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	public Task<string?> SetGridChargingAsync (bool enabled, CancellationToken cancellationToken = default) =>
		RequireCloudClient ().SetGridChargingAsync (enabled, cancellationToken);

	/// <summary>
	/// Returns whether charging the battery from the grid is currently allowed (cloud mode only).
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when grid charging is allowed, <see langword="false"/> when disallowed, or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	public Task<bool?> GetGridChargingAsync (bool force = false, CancellationToken cancellationToken = default) =>
		RequireCloudClient ().GetGridChargingAsync (force, cancellationToken);

	/// <summary>
	/// Sets the grid export rule (cloud mode only).
	/// </summary>
	/// <param name="mode">The export rule: <c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when the call fails.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="mode"/> is not a valid export rule.</exception>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	public Task<string?> SetGridExportAsync (string mode, CancellationToken cancellationToken = default) =>
		mode is not ("battery_ok" or "pv_only" or "never")
			? throw new ArgumentException ($"Invalid grid export mode '{mode}'. Must be 'battery_ok', 'pv_only', or 'never'.", nameof (mode))
			: RequireCloudClient ().SetGridExportAsync (mode, cancellationToken);

	/// <summary>
	/// Returns the current grid export rule (cloud mode only).
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The export rule (<c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>), or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	public Task<string?> GetGridExportAsync (bool force = false, CancellationToken cancellationToken = default) =>
		RequireCloudClient ().GetGridExportAsync (force, cancellationToken);

	/// <summary>
	/// Returns raw energy history for the active site (cloud mode only).
	/// </summary>
	/// <remarks>
	/// Tesla has permanently removed the underlying <c>/history</c> endpoint (it now responds with HTTP 410 Gone).
	/// This method faithfully mirrors the upstream pypowerwall <c>get_history()</c> shim but will therefore throw
	/// <see cref="PowerwallCloudEndpointRemovedException"/> at call time. Use <see cref="GetCalendarHistoryAsync"/>
	/// (which targets the current <c>/calendar_history</c> endpoint and accepts a superset of kinds) instead.
	/// </remarks>
	/// <param name="kind">The history kind: <c>power</c>, <c>energy</c>, <c>backup</c>, or <c>self_consumption</c>.</param>
	/// <param name="period">The aggregation period: <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>. Defaults to <c>day</c> when not specified.</param>
	/// <param name="timeZone">Optional IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Optional inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Optional inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw history JSON, or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="kind"/> or <paramref name="period"/> is not valid.</exception>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	/// <exception cref="PowerwallCloudEndpointRemovedException">Thrown because Tesla has removed the <c>/history</c> endpoint; use <see cref="GetCalendarHistoryAsync"/>.</exception>
	public Task<string?> GetHistoryAsync (
		string kind,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default)
		{
		period ??= DEFAULT_HISTORY_PERIOD;
		ValidateHistoryArguments (kind, period, _historyKinds);
		return RequireCloudClient ().GetHistoryAsync (kind, period, timeZone, startDate, endDate, cancellationToken);
		}

	/// <summary>
	/// Returns raw calendar-aligned energy history for the active site (cloud mode only).
	/// </summary>
	/// <param name="kind">The history kind: <c>power</c>, <c>soe</c>, <c>energy</c>, <c>backup</c>, <c>self_consumption</c>, <c>time_of_use_energy</c>, or <c>savings</c>.</param>
	/// <param name="period">The aggregation period: <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>. Defaults to <c>day</c> when not specified.</param>
	/// <param name="timeZone">Optional IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Optional inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Optional inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw calendar history JSON, or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="kind"/> or <paramref name="period"/> is not valid.</exception>
	/// <exception cref="PowerwallCloudNotImplementedException">Thrown when the active connection is not in cloud mode.</exception>
	public Task<string?> GetCalendarHistoryAsync (
		string kind,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default)
		{
		period ??= DEFAULT_HISTORY_PERIOD;
		ValidateHistoryArguments (kind, period, _calendarHistoryKinds);
		return RequireCloudClient ().GetCalendarHistoryAsync (kind, period, timeZone, startDate, endDate, cancellationToken);
		}

	/// <summary>
	/// Returns device vitals as a nested map of device name to that device's telemetry values.
	/// Vitals are available in cloud mode and on local firmware that still exposes the vitals payload.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The vitals map, or <see langword="null"/> when vitals are unavailable.</returns>
	public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default) =>
		RequireClient ().VitalsAsync (cancellationToken);

	/// <summary>
	/// Returns the distinct set of active alerts reported across all devices. Faithfully adapts the upstream
	/// pypowerwall <c>alerts()</c> method: alerts are collected from device vitals, and when vitals are
	/// unavailable (for example on newer local firmware) alerts fall back to the <c>/api/solar_powerwall</c> endpoint.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The distinct alert names, ordered as first encountered; empty when none are active.</returns>
	public async Task<IReadOnlyList<string>> AlertsAsync (CancellationToken cancellationToken = default)
		{
		var alerts = new List<string> ();
		var seen = new HashSet<string> (StringComparer.Ordinal);

		IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? devices = await VitalsAsync (cancellationToken).ConfigureAwait (false);
		if (devices is { Count: > 0 })
			{
			foreach (IReadOnlyDictionary<string, object?> device in devices.Values)
				{
				if (!device.TryGetValue ("alerts", out var deviceAlerts) || deviceAlerts is null)
					continue;

				foreach (var alert in EnumerateAlertValues (deviceAlerts))
					{
					if (!string.IsNullOrEmpty (alert) && seen.Add (alert))
						alerts.Add (alert);
					}
				}

			return alerts;
			}

		// Vitals are not present on local firmware after 23.44; fall back to /api/solar_powerwall.
		var payload = await RequireClient ().PollAsync ("/api/solar_powerwall", cancellationToken: cancellationToken).ConfigureAwait (false);
		JObject? solar = JsonHelper.DeserializeOrNull<JObject> (payload);
		if (solar is null)
			return alerts;

		foreach (var group in new[] { "pvac_alerts", "pvs_alerts" })
			{
			if (solar[group] is not JObject flags)
				continue;

			foreach (JProperty flag in flags.Properties ())
				{
				if (flag.Value.Type == JTokenType.Boolean && flag.Value.Value<bool> () && seen.Add (flag.Name))
					alerts.Add (flag.Name);
				}
			}

		return alerts;
		}

	private static IEnumerable<string> EnumerateAlertValues (object deviceAlerts)
		{
		// Vitals attribute values are loosely typed; alerts is normally a JArray of strings.
		if (deviceAlerts is JArray array)
			{
			foreach (JToken item in array)
				{
				var value = item.Type == JTokenType.String ? item.Value<string> () : item.ToString ();
				if (value is not null)
					yield return value;
				}

			yield break;
			}

		if (deviceAlerts is IEnumerable<string> strings)
			{
			foreach (var value in strings)
				yield return value;

			yield break;
			}

		if (deviceAlerts is string single)
			yield return single;
		}

	private static void ValidateHistoryArguments (string kind, string? period, HashSet<string> allowedKinds)
		{
		if (string.IsNullOrWhiteSpace (kind))
			throw new ArgumentException ("A history kind is required.", nameof (kind));

		if (!allowedKinds.Contains (kind))
			throw new ArgumentException ($"Invalid history kind '{kind}'. Allowed values: {string.Join (", ", allowedKinds)}.", nameof (kind));

		if (period is not null && !_historyPeriods.Contains (period))
			throw new ArgumentException ($"Invalid history period '{period}'. Allowed values: {string.Join (", ", _historyPeriods)}.", nameof (period));
		}

	private async Task<OperationResponse?> GetOperationAsync (bool force, CancellationToken cancellationToken)
		{
		var payload = await RequireClient ().PollAsync ("/api/operation", force, cancellationToken: cancellationToken).ConfigureAwait (false);
		return JsonHelper.DeserializeOrNull<OperationResponse> (payload);
		}

	private PowerwallClientBase RequireClient () =>
		_client ?? throw new InvalidOperationException ("Not connected. Call ConnectAsync before invoking data methods.");

	private PowerwallCloudClient RequireCloudClient () =>
		RequireClient () as PowerwallCloudClient
		?? throw new PowerwallCloudNotImplementedException (
			$"This operation is only available in cloud mode. The active connection mode is '{Mode}'.");

	private static PowerwallMode ResolveMode (PowerwallOptions options) =>
		string.IsNullOrWhiteSpace (options.Host)
			? options.FleetApi ? PowerwallMode.FleetApi : PowerwallMode.Cloud
			: options.CloudMode ? options.FleetApi ? PowerwallMode.FleetApi : PowerwallMode.Cloud : PowerwallMode.Local;

	private void ValidateConfiguration ()
		{
		if (!string.IsNullOrWhiteSpace (_options.Host))
			{
			var host = StripPort (_options.Host);
			if (!Validation.IsValidHost (host) && !Validation.IsValidIpAddress (host))
				{
				throw new PowerwallInvalidConfigurationException (
					$"Invalid powerwall host: '{_options.Host}'. Must be in the form of IP address or a valid hostname or FQDN.");
				}
			}

		if (Mode == PowerwallMode.Cloud && !_options.NoCloudTokenPersistence && !Validation.IsValidEmail (_options.Email))
			{
			throw new PowerwallInvalidConfigurationException (
				$"A valid email address is required to run in cloud mode: '{_options.Email}' did not pass validation.");
			}
		}

	private static string StripPort (string host)
		{
		// Only strip a single trailing :port suffix; multi-colon values are IPv6 and must be left intact.
		if (host.Count (static c => c == ':') != 1)
			return host;

		var colon = host.LastIndexOf (':');
		return int.TryParse (host[(colon + 1)..], out _) ? host[..colon] : host;
		}

	/// <summary>
	/// Releases the underlying backend client and associated resources.
	/// </summary>
	public void Dispose ()
		{
		if (_client is PowerwallCloudClient cloudClient)
			cloudClient.TokensRefreshed -= OnCloudTokensRefreshed;

		if (_client is IDisposable disposable)
			disposable.Dispose ();
		}

	private void OnCloudTokensRefreshed (object? sender, CloudTokensRefreshedEventArgs e) =>
		CloudTokensRefreshed?.Invoke (this, e);
	}

/// <summary>
/// Normalized grid connection status. The integer values match the upstream numeric output
/// (<c>1</c> = Up, <c>0</c> = Down, <c>-1</c> = Syncing).
/// </summary>
public enum GridStatus
	{
	/// <summary>The system is transitioning between grid and island states.</summary>
	Syncing = -1,

	/// <summary>The system is disconnected from the grid (islanded).</summary>
	Down = 0,

	/// <summary>The system is connected to the grid.</summary>
	Up = 1
	}
