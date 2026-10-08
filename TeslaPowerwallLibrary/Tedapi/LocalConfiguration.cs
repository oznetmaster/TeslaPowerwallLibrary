// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Non-secret operating configuration read from the local gateway.</summary>
/// <remarks>Credential, installer and network-password fields are deliberately not exposed.</remarks>
public sealed record LocalConfiguration
	{
	/// <summary>Configured meter identities, channel assignments and real-power scaling.</summary>
	[JsonPropertyName ("meters")]
	public IReadOnlyList<LocalMeterConfiguration>? Meters { get; init; }

	/// <summary>Gateway device identification number.</summary>
	[JsonPropertyName ("vin")]
	public string? Din { get; init; }

	/// <summary>Configured Powerwall units and their battery-only expansion packs.</summary>
	[JsonPropertyName ("battery_blocks")]
	public IReadOnlyList<LocalBatteryConfiguration>? Batteries { get; init; }

	/// <summary>Configured operating mode, such as self_consumption or autonomous.</summary>
	[JsonPropertyName ("default_real_mode")]
	public string? OperationMode { get; init; }

	/// <summary>Site identity, time zone and battery reserve configuration.</summary>
	[JsonPropertyName ("site_info")]
	public LocalSiteConfiguration? Site { get; init; }
	}

/// <summary>Non-secret local site configuration.</summary>
public sealed record LocalSiteConfiguration
	{
	/// <summary>User-assigned site name.</summary>
	[JsonPropertyName ("site_name")]
	public string? Name { get; init; }

	/// <summary>IANA time-zone identifier reported by the gateway.</summary>
	[JsonPropertyName ("timezone")]
	public string? Timezone { get; init; }

	/// <summary>Whether the gateway disallows charging from the grid; null means unreported.</summary>
	[JsonPropertyName ("disallow_charge_from_grid_with_solar_installed")]
	public bool? GridChargingDisallowed { get; init; }

	/// <summary>Configured export rule: battery_ok, pv_only, or never; null means unreported.</summary>
	[JsonPropertyName ("customer_preferred_export_rule")]
	public string? GridExport { get; init; }
	/// <summary>Configured battery reserve on the raw gateway percentage scale.</summary>
	[JsonPropertyName ("backup_reserve_percent")]
	public double? BackupReservePercent { get; init; }
	}

/// <summary>Device identity recorded by the local gateway configuration.</summary>
public sealed record LocalBatteryConfiguration
	{
	/// <summary>Powerwall device identification number, containing its part and serial numbers.</summary>
	[JsonPropertyName ("vin")]
	public string? Din { get; init; }
	/// <summary>Device type reported by the gateway.</summary>
	[JsonPropertyName ("type")]
	public string? Type { get; init; }
	/// <summary>Configured battery-only expansion packs attached to this unit.</summary>
	[JsonPropertyName ("battery_expansions")]
	public IReadOnlyList<LocalBatteryExpansion>? Expansions { get; init; }
	}

/// <summary>Identity of a configured battery expansion; it has no independent inverter.</summary>
public sealed record LocalBatteryExpansion
	{
	/// <summary>Expansion device identification number.</summary>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }
	}
