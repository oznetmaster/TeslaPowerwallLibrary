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

	/// <summary>Direct local network access to the Tesla Energy Gateway.</summary>
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

	/// <summary>Customer email.</summary>
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
	/// Tesla Owners API OAuth access token used for cloud mode. Obtain this token by completing the
	/// Tesla OAuth flow separately (for example, with the upstream <c>pypowerwall setup</c> tool); the
	/// library refreshes it using <see cref="RefreshToken"/> but does not perform interactive login.
	/// </summary>
	public string? AccessToken { get; init; }

	/// <summary>
	/// Tesla Owners API OAuth refresh token used to renew an expired <see cref="AccessToken"/> in cloud mode.
	/// </summary>
	public string? RefreshToken { get; init; }

	/// <summary>Path to cloud authentication and site cache files.</summary>
	public string AuthPath { get; init; } = string.Empty;

	/// <summary>Authentication mode for local access: <c>cookie</c> (default) or <c>token</c>.</summary>
	public string AuthMode { get; init; } = "cookie";

	/// <summary>Path to the file used to persist the local-mode authentication session.</summary>
	public string CacheFile { get; init; } = Constants.DEFAULT_CACHE_FILE;

	/// <summary>When <see langword="true"/>, use Tesla FleetAPI for data.</summary>
	public bool FleetApi { get; init; }
	}
