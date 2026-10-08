// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// Immutable snapshot of the instantaneous system state shown on the Home screen. All power values are in watts.
/// </summary>
/// <param name="SolarWatts">Solar generation power in watts.</param>
/// <param name="BatteryWatts">Battery power in watts (positive indicates discharge).</param>
/// <param name="HomeWatts">Home (load) consumption power in watts.</param>
/// <param name="GridWatts">Grid (site) power in watts (positive indicates import).</param>
/// <param name="BatteryPercent">Battery charge level as an app-scaled percentage, when available.</param>
/// <param name="GridStatus">Normalized grid connection status, when available.</param>
/// <param name="TimeRemainingHours">Estimated backup time remaining in hours, when available.</param>
public sealed record PowerFlowSnapshot (
	double? SolarWatts,
	double? BatteryWatts,
	double? HomeWatts,
	double? GridWatts,
	double? BatteryPercent,
	GridStatus? GridStatus,
	double? TimeRemainingHours)
	{
	/// <summary>Builds the Home snapshot from one controller response without issuing extra requests.</summary>
	/// <param name="telemetry">Reported local controller telemetry.</param>
	/// <returns>A snapshot preserving unreported fields as null.</returns>
	public static PowerFlowSnapshot FromLocal (LocalTelemetry telemetry)
		{
		if (telemetry is null)
			throw new ArgumentNullException (nameof (telemetry));
		double? Watts (string location) => telemetry.Control?.MeterAggregates?
			.FirstOrDefault (reading => string.Equals (reading.Location, location, StringComparison.OrdinalIgnoreCase))?.Watts;
		var energy = telemetry.Control?.SystemStatus;
		double? rawLevel = energy?.FullCapacityWattHours > 0 && energy.RemainingWattHours.HasValue
			? energy.RemainingWattHours / energy.FullCapacityWattHours * 100 : null;
		double? scaledLevel = (rawLevel - 5) / 0.95;
		double? load = Watts ("LOAD");
		double? estimatedBackup = load > 0 && energy?.RemainingWattHours >= 0 ? energy.RemainingWattHours / load : null;
		GridStatus? grid = telemetry.Control?.Islanding?.ContactorClosed is bool connected
			? connected ? TeslaPowerwallLibrary.GridStatus.Up : TeslaPowerwallLibrary.GridStatus.Down : null;
		return new PowerFlowSnapshot (Watts ("SOLAR"), Watts ("BATTERY"), load, Watts ("SITE"), scaledLevel, grid, estimatedBackup);
		}
	}
