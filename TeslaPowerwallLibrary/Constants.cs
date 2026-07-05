// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary;

/// <summary>
/// Library-wide constant values shared across the Tesla™ Powerwall™ client.
/// </summary>
public static class Constants
	{
	/// <summary>Placeholder text used when a value is missing or cannot be determined.</summary>
	public const string TEXT_UNKNOWN = "unknown";

	/// <summary>Default customer email used when none is supplied.</summary>
	public const string DEFAULT_EMAIL = "nobody@nowhere.com";

	/// <summary>Default IANA time zone reported to the gateway in client info.</summary>
	public const string DEFAULT_TIMEZONE = "America/Los_Angeles";

	/// <summary>Default cache file used to persist local-mode authentication sessions.</summary>
	public const string DEFAULT_CACHE_FILE = ".powerwall";

	/// <summary>
	/// Default file name used by the library to persist cloud-mode Tesla Owners API tokens and the selected
	/// site, keyed by customer email. Mirrors the upstream behavior of caching credentials internally so
	/// callers never have to manage token storage themselves.
	/// </summary>
	public const string DEFAULT_CLOUD_AUTH_FILE = ".powerwall.auth.json";

	/// <summary>Default local application data subfolder used for library-managed cloud token persistence.</summary>
	public const string DEFAULT_CLOUD_AUTH_FOLDER = "TeslaPowerwallLibrary";

	/// <summary>Default number of seconds before cached API responses expire.</summary>
	public const int DEFAULT_CACHE_EXPIRE_SECONDS = 5;

	/// <summary>Default number of seconds before an HTTP request times out.</summary>
	public const int DEFAULT_TIMEOUT_SECONDS = 5;

	/// <summary>Default maximum size of the HTTP connection pool (0 disables connection re-use).</summary>
	public const int DEFAULT_POOL_MAX_SIZE = 10;

	/// <summary>Link-local IP address of the Tesla Energy Gateway used for TEDAPI access.</summary>
	public const string GW_IP = "192.168.91.1";
	}
