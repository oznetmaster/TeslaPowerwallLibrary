// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Projects local status and configured ratings using the gateway's derated power-limit semantics.</summary>
internal static class LocalSystemStatusProjection
	{
	/// <summary>Combines controller status with the configured nameplate and usable inverter capabilities.</summary>
	/// <param name="telemetry">Controller energy and grid status.</param>
	/// <param name="snapshot">Configuration, detailed inverter capabilities and identified battery measurements.</param>
	/// <returns>Status with watt-based limits; unavailable limits remain null.</returns>
	internal static SystemStatus Create (LocalTelemetry telemetry, LocalDeviceSnapshot snapshot)
		{
		// pypowerwall v0.21.0 get_api_system_status: site kW converted to W, capped
		// by the active PINV capabilities. PW3 has no usable legacy PINV slots.
		double? kilowatts = PositiveFinite (snapshot.Configuration.Site?.NominalPowerKilowatts);
		double? nameplate = kilowatts.HasValue ? PositiveFinite (Math.Round (kilowatts.Value * 1000, MidpointRounding.ToEven)) : null;
		double usable = 0;
		foreach (var inverter in snapshot.Controller.EnergyBus?.Devices?.BatteryInverters ?? Array.Empty<LocalBatteryInverterBus> ())
			{
			var capability = inverter?.PowerCapability;
			if (capability is null || capability.IsMissing == true || capability.IsComplete == false)
				continue;
			usable += PositiveFinite (capability.NominalPowerWatts) ?? 0;
			}
		double? reportedUsable = double.IsInfinity (usable) ? null : usable;
		double? cap = PositiveFinite (reportedUsable);
		double? limit = nameplate.HasValue && cap.HasValue ? Math.Min (nameplate.Value, cap.Value) : nameplate ?? cap;
		return new SystemStatus
			{
			NominalFullPackEnergy = telemetry.Control?.SystemStatus?.FullCapacityWattHours,
			NominalEnergyRemaining = telemetry.Control?.SystemStatus?.RemainingWattHours,
			MaxChargePower = limit,
			MaxDischargePower = limit,
			MaxApparentPower = limit,
			InverterNominalUsablePower = reportedUsable,
			AvailableBlocks = telemetry.Control?.BatteryBlocks?.Count,
			BatteryBlocks = snapshot.Batteries,
			SystemIslandState = telemetry.Control?.Islanding?.ContactorClosed is bool closed ? closed ? "SystemGridConnected" : "SystemIslandedActive" : null
			};
		}

	private static double? PositiveFinite (double? value) =>
		value > 0 && !double.IsInfinity (value.Value) ? value : null;
	}
