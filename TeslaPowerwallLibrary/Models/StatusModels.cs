// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// Gateway status payload returned by <c>/api/status</c>.
/// </summary>
public sealed record GatewayStatus
	{
	/// <summary>Device identification number (DIN).</summary>
	/// <value>Device identification number (DIN).</value>
	[JsonPropertyName ("din")]
	public string? Din
		{
		get; init;
		}

	/// <summary>Gateway start time.</summary>
	/// <value>Gateway start time.</value>
	[JsonPropertyName ("start_time")]
	public string? StartTime
		{
		get; init;
		}

	/// <summary>Cloud-reported installation date, not the gateway's boot time; null when unavailable.</summary>
	/// <value>Cloud-reported installation date, not the gateway's boot time; null when unavailable.</value>
	[JsonPropertyName ("installation_date")]
	public string? InstallationDate
		{
		get; init;
		}

	/// <summary>System uptime, expressed as a duration string (for example <c>1541h38m20.998412744s</c>).</summary>
	/// <value>System uptime, expressed as a duration string (for example <c>1541h38m20.998412744s</c>).</value>
	[JsonPropertyName ("up_time_seconds")]
	public string? UpTimeSeconds
		{
		get; init;
		}

	/// <summary>Indicates whether the gateway is newly commissioned.</summary>
	/// <value>Indicates whether the gateway is newly commissioned.</value>
	[JsonPropertyName ("is_new")]
	public bool? IsNew
		{
		get; init;
		}

	/// <summary>Firmware version string.</summary>
	/// <value>Firmware version string.</value>
	[JsonPropertyName ("version")]
	public string? Version
		{
		get; init;
		}

	/// <summary>Source control hash for the firmware build.</summary>
	/// <value>Source control hash for the firmware build.</value>
	[JsonPropertyName ("git_hash")]
	public string? GitHash
		{
		get; init;
		}

	/// <summary>Number of times the gateway has been commissioned.</summary>
	/// <value>Number of times the gateway has been commissioned.</value>
	[JsonPropertyName ("commission_count")]
	public int? CommissionCount
		{
		get; init;
		}

	/// <summary>Device type identifier.</summary>
	/// <value>Device type identifier.</value>
	[JsonPropertyName ("device_type")]
	public string? DeviceType
		{
		get; init;
		}

	/// <summary>Synchronization type identifier.</summary>
	/// <value>Synchronization type identifier.</value>
	[JsonPropertyName ("sync_type")]
	public string? SyncType
		{
		get; init;
		}

	/// <summary>Leader identifier in a multi-gateway configuration.</summary>
	/// <value>Leader identifier in a multi-gateway configuration.</value>
	[JsonPropertyName ("leader")]
	public string? Leader
		{
		get; init;
		}

	/// <summary>Follower identifiers in a multi-gateway configuration.</summary>
	/// <value>Follower identifiers in a multi-gateway configuration.</value>
	[JsonPropertyName ("followers")]
	public IReadOnlyList<string>? Followers
		{
		get; init;
		}

	/// <summary>Indicates whether the cellular interface is disabled.</summary>
	/// <value>Indicates whether the cellular interface is disabled.</value>
	[JsonPropertyName ("cellular_disabled")]
	public bool? CellularDisabled
		{
		get; init;
		}
	}

/// <summary>
/// Site name payload returned by <c>/api/site_info/site_name</c>.
/// </summary>
public sealed record SiteName
	{
	/// <summary>Configured site name.</summary>
	/// <value>Configured site name.</value>
	[JsonPropertyName ("site_name")]
	public string? Name
		{
		get; init;
		}

	/// <summary>Configured site time zone.</summary>
	/// <value>Configured site time zone.</value>
	[JsonPropertyName ("timezone")]
	public string? Timezone
		{
		get; init;
		}
	}

/// <summary>
/// Sitemaster status payload returned by <c>/api/sitemaster</c>.
/// </summary>
public sealed record SitemasterStatus
	{
	/// <summary>Sitemaster status string (for example <c>StatusUp</c>).</summary>
	/// <value>Sitemaster status string (for example <c>StatusUp</c>).</value>
	[JsonPropertyName ("status")]
	public string? Status
		{
		get; init;
		}

	/// <summary>Indicates whether the sitemaster is running.</summary>
	/// <value>Indicates whether the sitemaster is running.</value>
	[JsonPropertyName ("running")]
	public bool? Running
		{
		get; init;
		}

	/// <summary>Indicates whether the gateway is connected to Tesla™.</summary>
	/// <value>Indicates whether the gateway is connected to Tesla™.</value>
	[JsonPropertyName ("connected_to_tesla")]
	public bool? ConnectedToTesla
		{
		get; init;
		}

	/// <summary>Indicates whether the system is in power supply mode.</summary>
	/// <value>Indicates whether the system is in power supply mode.</value>
	[JsonPropertyName ("power_supply_mode")]
	public bool? PowerSupplyMode
		{
		get; init;
		}
	}

#pragma warning restore CA1507