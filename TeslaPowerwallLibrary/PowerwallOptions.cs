// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary;

/// <summary>
/// Identifies which backend a <see cref="Powerwall"/> instance uses to communicate with the system.
/// </summary>
public enum PowerwallMode
	{
	/// <summary>The connection mode could not be determined.</summary>
	Unknown,

	/// <summary>Direct local network access to the Tesla™ Energy Gateway.</summary>
	Local,

	/// <summary>Tesla Owners (cloud) API access.</summary>
	Cloud,

	/// <summary>Tesla FleetAPI access.</summary>
	FleetApi
	}

/// <summary>
/// Configuration options used to construct a <see cref="Powerwall"/> instance. Mirrors the constructor
/// parameters of the Python <c>Powerwall</c> class while using idiomatic .NET naming and types.
/// </summary>
public sealed record PowerwallOptions
	{
	/// <summary>
	/// Hostname or IP address of the Powerwall gateway (for example <c>10.0.1.99</c>), optionally
	/// including a non-standard HTTPS port (for example <c>10.0.1.99:8443</c>). When empty, cloud mode is assumed.
	/// </summary>
	public string Host { get; init; } = string.Empty;

	/// <summary>Customer password configured on the Powerwall gateway.</summary>
	public string Password { get; init; } = string.Empty;

	/// <summary>
	/// Customer email. In local mode this is sent to the gateway during login. In cloud/FleetAPI mode Tesla
	/// authentication is entirely token-based (<see cref="AccessToken"/>/<see cref="RefreshToken"/>) and this
	/// value is used only as the cloud token cache key and for diagnostic display; a valid email is required
	/// unless <see cref="NoCloudTokenPersistence"/> is <see langword="true"/>, in which case there is no cache
	/// to key and the value is not validated.
	/// </summary>
	public string Email { get; init; } = Constants.DEFAULT_EMAIL;

	/// <summary>IANA time zone for the location of the Powerwall.</summary>
	public string Timezone { get; init; } = Constants.DEFAULT_TIMEZONE;

	/// <summary>Number of seconds before cached API responses expire.</summary>
	public int CacheExpireSeconds { get; init; } = Constants.DEFAULT_CACHE_EXPIRE_SECONDS;

	/// <summary>Per-request HTTP timeout.</summary>
	public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds (Constants.DEFAULT_TIMEOUT_SECONDS);

	/// <summary>When <see langword="true"/>, use the Tesla cloud for data instead of local access.</summary>
	public bool CloudMode { get; init; }

	/// <summary>Site identifier used in cloud mode.</summary>
	public string? SiteId { get; init; }

	/// <summary>
	/// Tesla Owners API OAuth access token used for cloud mode. Optional: when omitted (or when the supplied
	/// value is stale or rejected), the library silently derives a new one from <see cref="RefreshToken"/> -
	/// there is no need to supply this at all for a normal connect. Supply it only as an optimization to skip
	/// that one derivation round-trip on the very first connect after a fresh login. After the initial connect
	/// the library persists tokens internally (keyed by <see cref="Email"/>) and reuses them automatically on
	/// later runs, so callers do not need to store tokens themselves. Whether this was supplied also affects how
	/// often <see cref="Powerwall.CloudTokensRefreshed"/> fires - see that event's documentation.
	/// </summary>
	public string? AccessToken { get; init; }

	/// <summary>
	/// Tesla Owners API OAuth refresh token used to renew an expired or absent <see cref="AccessToken"/> in
	/// cloud mode. This is the one credential a first-time login must supply - obtain it by completing the
	/// Tesla OAuth flow separately (for example with the upstream <c>pypowerwall setup</c> tool). The library
	/// persists the (possibly rotated) refresh token internally and reuses it on later runs, so it too can be
	/// omitted once a prior connect has succeeded.
	/// </summary>
	public string? RefreshToken { get; init; }

	/// <summary>
	/// Directory or file used by the library to persist cloud-mode Tesla tokens and the selected site, keyed
	/// by <see cref="Email"/>. When empty, a per-user default under the local application data folder is used
	/// and storage failures are logged but otherwise ignored. When non-empty, the location is authoritative:
	/// no fallback is attempted, and a failure to read or write it raises
	/// <see cref="Cloud.PowerwallCloudTokenCacheStorageException"/>. When a directory is supplied, the default
	/// cache file name is appended. Ignored entirely when <see cref="NoCloudTokenPersistence"/> is
	/// <see langword="true"/>.
	/// </summary>
	public string AuthPath { get; init; } = string.Empty;

	/// <summary>
	/// When <see langword="true"/>, disables the library's cloud token cache entirely: <see cref="AuthPath"/>
	/// is ignored and no file is ever read or written. <see cref="Email"/> is not required to pass format
	/// validation in this mode, since it is otherwise used only as the cache key and for diagnostic display,
	/// not for Tesla authentication. Callers must supply <see cref="RefreshToken"/> on every run (
	/// <see cref="AccessToken"/> remains optional and is silently re-derived when absent or stale) and should
	/// subscribe to <see cref="Powerwall.CloudTokensRefreshed"/> to persist the new value using their own
	/// storage; when <see cref="AccessToken"/> was omitted, that event only fires when the refresh token itself
	/// changes. Use this on hosts where the library's default per-user file cache is not appropriate (for
	/// example Mono-hosted embedded environments without a writable per-user profile folder).
	/// </summary>
	public bool NoCloudTokenPersistence { get; init; }

	/// <summary>Authentication mode for local access: <c>cookie</c> (default) or <c>token</c>.</summary>
	public string AuthMode { get; init; } = "cookie";

	/// <summary>Path to the file used to persist the local-mode authentication session.</summary>
	public string CacheFile { get; init; } = Constants.DEFAULT_CACHE_FILE;

	/// <summary>When <see langword="true"/>, use Tesla FleetAPI for data.</summary>
	public bool FleetApi { get; init; }

	/// <summary>
	/// Tesla FleetAPI application Client ID registered at <c>https://developer.tesla.com/</c>. Required for
	/// FleetAPI mode; used only to identify the application on the OAuth refresh-token grant.
	/// </summary>
	public string? FleetApiClientId { get; init; }

	/// <summary>
	/// Tesla FleetAPI application Client Secret registered at <c>https://developer.tesla.com/</c>. Reserved
	/// for future use (for example partner-token flows); it is not required for and not sent on the
	/// refresh-token grant used to keep <see cref="FleetApiAccessToken"/> current.
	/// </summary>
	public string? FleetApiClientSecret { get; init; }

	/// <summary>
	/// Tesla FleetAPI OAuth access token. Optional: when omitted (or when the supplied value is stale or
	/// rejected), the library silently derives a new one from <see cref="FleetApiRefreshToken"/>.
	/// </summary>
	public string? FleetApiAccessToken { get; init; }

	/// <summary>
	/// Tesla FleetAPI OAuth refresh token used to renew an expired or absent <see cref="FleetApiAccessToken"/>.
	/// This is the one credential a first-time FleetAPI connection must supply - obtain it by completing the
	/// Tesla FleetAPI OAuth flow separately. After the initial connect the library persists tokens internally
	/// (keyed by <see cref="Email"/>) and reuses them automatically on later runs, so callers do not need to
	/// store tokens themselves, unless <see cref="NoFleetApiTokenPersistence"/> is <see langword="true"/>.
	/// </summary>
	public string? FleetApiRefreshToken { get; init; }

	/// <summary>
	/// Tesla FleetAPI region used to select the regional Fleet API base URL: <c>na</c> (North America /
	/// Asia-Pacific, default), <c>eu</c> (Europe / Middle East / Africa), or <c>cn</c> (China). Unrecognized
	/// values fall back to <c>na</c>.
	/// </summary>
	public string FleetApiRegion { get; init; } = "na";

	/// <summary>
	/// Directory or file used by the library to persist FleetAPI tokens and the selected site, keyed by
	/// <see cref="Email"/>. When empty, a per-user default under the local application data folder is used and
	/// storage failures are logged but otherwise ignored. When non-empty, the location is authoritative: no
	/// fallback is attempted, and a failure to read or write it raises
	/// <see cref="FleetApi.PowerwallFleetApiTokenCacheStorageException"/>. When a directory is supplied, the
	/// default cache file name is appended. Ignored entirely when <see cref="NoFleetApiTokenPersistence"/> is
	/// <see langword="true"/>.
	/// </summary>
	public string FleetApiAuthPath { get; init; } = string.Empty;

	/// <summary>
	/// When <see langword="true"/>, disables the library's FleetAPI token cache entirely: <see cref="FleetApiAuthPath"/>
	/// is ignored and no file is ever read or written. Callers must supply <see cref="FleetApiRefreshToken"/> on
	/// every run (<see cref="FleetApiAccessToken"/> remains optional and is silently re-derived when absent or
	/// stale) and should subscribe to <see cref="Powerwall.FleetApiTokensRefreshed"/> to persist the new value
	/// using their own storage; when <see cref="FleetApiAccessToken"/> was omitted, that event only fires when
	/// the refresh token itself changes. Use this on hosts where the library's default per-user file cache is
	/// not appropriate (for example Mono-hosted embedded environments without a writable per-user profile
	/// folder).
	/// </summary>
	public bool NoFleetApiTokenPersistence { get; init; }
	}
