// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// The payload returned by <c>/api/solar_powerwall</c>, used as the <see cref="Powerwall.AlertsAsync"/>
/// fallback source on local firmware where device vitals are unavailable. Each alert group maps a flag
/// name to whether that alert is currently active.
/// </summary>
public sealed record SolarPowerwallAlertsResponse
	{
	/// <summary>Active alert flags reported by the PV-AC (solar inverter) controller.</summary>
	[JsonProperty ("pvac_alerts")]
	public IReadOnlyDictionary<string, bool>? PvacAlerts { get; init; }

	/// <summary>Active alert flags reported by the PVS (solar string) controller.</summary>
	[JsonProperty ("pvs_alerts")]
	public IReadOnlyDictionary<string, bool>? PvsAlerts { get; init; }
	}

#pragma warning restore CA1507
