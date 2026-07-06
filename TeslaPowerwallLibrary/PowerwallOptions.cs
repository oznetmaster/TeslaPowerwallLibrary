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
	/// Tesla Owners API OAuth access token used for cloud mode. Supply this only for a first-time login or to
	/// override the cached token; after the initial connect the library persists tokens internally (keyed by
	/// <see cref="Email"/>) and reuses them automatically on later runs, so callers do not need to store tokens
	/// themselves. Obtain the initial token by completing the Tesla OAuth flow separately (for example, with
	/// the upstream <c>pypowerwall setup</c> tool); the library refreshes it using <see cref="RefreshToken"/>
	/// but does not perform interactive login.
	/// </summary>
	public string? AccessToken { get; init; }

	/// <summary>
	/// Tesla Owners API OAuth refresh token used to renew an expired <see cref="AccessToken"/> in cloud mode.
	/// Supply this only for a first-time login or to override the cached token; the library persists the
	/// (possibly rotated) refresh token internally and reuses it on later runs.
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
	/// not for Tesla authentication. Callers must supply <see cref="AccessToken"/> and <see cref="RefreshToken"/>
	/// on every run and should subscribe to <see cref="Powerwall.CloudTokensRefreshed"/> to receive rotated
	/// tokens and persist them using their own storage. Use this on hosts where the library's default per-user
	/// file cache is not appropriate (for example Mono-hosted embedded environments without a writable per-user
	/// profile folder).
	/// </summary>
	public bool NoCloudTokenPersistence { get; init; }

	/// <summary>Authentication mode for local access: <c>cookie</c> (default) or <c>token</c>.</summary>
	public string AuthMode { get; init; } = "cookie";

	/// <summary>Path to the file used to persist the local-mode authentication session.</summary>
	public string CacheFile { get; init; } = Constants.DEFAULT_CACHE_FILE;

	/// <summary>When <see langword="true"/>, use Tesla FleetAPI for data.</summary>
	public bool FleetApi { get; init; }
	}
