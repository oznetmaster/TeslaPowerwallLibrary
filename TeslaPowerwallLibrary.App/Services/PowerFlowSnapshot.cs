// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
	double SolarWatts,
	double BatteryWatts,
	double HomeWatts,
	double GridWatts,
	double? BatteryPercent,
	GridStatus? GridStatus,
	double? TimeRemainingHours);
