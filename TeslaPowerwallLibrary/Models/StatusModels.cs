// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// Gateway status payload returned by <c>/api/status</c>.
/// </summary>
public sealed record GatewayStatus
	{
	/// <summary>Device identification number (DIN).</summary>
	[JsonProperty ("din")]
	public string? Din { get; init; }

	/// <summary>Gateway start time.</summary>
	[JsonProperty ("start_time")]
	public string? StartTime { get; init; }

	/// <summary>System uptime, expressed as a duration string (for example <c>1541h38m20.998412744s</c>).</summary>
	[JsonProperty ("up_time_seconds")]
	public string? UpTimeSeconds { get; init; }

	/// <summary>Indicates whether the gateway is newly commissioned.</summary>
	[JsonProperty ("is_new")]
	public bool? IsNew { get; init; }

	/// <summary>Firmware version string.</summary>
	[JsonProperty ("version")]
	public string? Version { get; init; }

	/// <summary>Source control hash for the firmware build.</summary>
	[JsonProperty ("git_hash")]
	public string? GitHash { get; init; }

	/// <summary>Number of times the gateway has been commissioned.</summary>
	[JsonProperty ("commission_count")]
	public int? CommissionCount { get; init; }

	/// <summary>Device type identifier.</summary>
	[JsonProperty ("device_type")]
	public string? DeviceType { get; init; }

	/// <summary>Synchronization type identifier.</summary>
	[JsonProperty ("sync_type")]
	public string? SyncType { get; init; }

	/// <summary>Leader identifier in a multi-gateway configuration.</summary>
	[JsonProperty ("leader")]
	public string? Leader { get; init; }

	/// <summary>Follower identifiers in a multi-gateway configuration.</summary>
	[JsonProperty ("followers")]
	public IReadOnlyList<string>? Followers { get; init; }

	/// <summary>Indicates whether the cellular interface is disabled.</summary>
	[JsonProperty ("cellular_disabled")]
	public bool? CellularDisabled { get; init; }
	}

/// <summary>
/// Site name payload returned by <c>/api/site_info/site_name</c>.
/// </summary>
public sealed record SiteName
	{
	/// <summary>Configured site name.</summary>
	[JsonProperty ("site_name")]
	public string? Name { get; init; }

	/// <summary>Configured site time zone.</summary>
	[JsonProperty ("timezone")]
	public string? Timezone { get; init; }
	}

/// <summary>
/// Sitemaster status payload returned by <c>/api/sitemaster</c>.
/// </summary>
public sealed record SitemasterStatus
	{
	/// <summary>Sitemaster status string (for example <c>StatusUp</c>).</summary>
	[JsonProperty ("status")]
	public string? Status { get; init; }

	/// <summary>Indicates whether the sitemaster is running.</summary>
	[JsonProperty ("running")]
	public bool? Running { get; init; }

	/// <summary>Indicates whether the gateway is connected to Tesla™.</summary>
	[JsonProperty ("connected_to_tesla")]
	public bool? ConnectedToTesla { get; init; }

	/// <summary>Indicates whether the system is in power supply mode.</summary>
	[JsonProperty ("power_supply_mode")]
	public bool? PowerSupplyMode { get; init; }
	}

#pragma warning restore CA1507
