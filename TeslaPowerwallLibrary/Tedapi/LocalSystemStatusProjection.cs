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
			InstantaneousMaxChargePower = AvailablePower (snapshot.Controller, "POD_available_charge_power"),
			InstantaneousMaxDischargePower = AvailablePower (snapshot.Controller, "POD_available_dischg_power"),
			MaxApparentPower = limit,
			InverterNominalUsablePower = reportedUsable,
			AvailableBlocks = telemetry.Control?.BatteryBlocks?.Count,
			BatteryBlocks = snapshot.Batteries,
			SystemIslandState = telemetry.Control?.Islanding?.ContactorClosed is bool closed ? closed ? "SystemGridConnected" : "SystemIslandedActive" : null
			};
		}

	private static double? AvailablePower (LocalTelemetry controller, string name)
		{
		if (controller.Components is null || !controller.Components.TryGetValue ("msa", out var components) || components is null)
			return null;
		// pypowerwall #429: deduplicate by module serial, retain the last usable
		// measurement and reject partial totals independently for each direction.
		var modules = new Dictionary<string, double?> (StringComparer.Ordinal);
		foreach (var component in components)
			{
			string? serial = component?.SerialNumber;
			if (string.IsNullOrWhiteSpace (serial))
				continue;
			foreach (var signal in component!.Signals ?? Array.Empty<LocalSignal> ())
				{
				if (signal?.Name is not ("POD_available_charge_power" or "POD_available_dischg_power"))
					continue;
				if (!modules.ContainsKey (serial!))
					modules.Add (serial!, null);
				if (signal.Name == name && signal.BoolValue is null && signal.TextValue is null
					&& signal.Value is double value && value >= 0 && !double.IsInfinity (value))
					modules[serial!] = value;
				}
			}
		if (modules.Count == 0 || modules.Values.Any (value => !value.HasValue))
			return null;
		double total = modules.Values.Sum (value => value!.Value);
		return double.IsInfinity (total) ? null : total;
		}

	private static double? PositiveFinite (double? value) =>
		value > 0 && !double.IsInfinity (value.Value) ? value : null;
	}