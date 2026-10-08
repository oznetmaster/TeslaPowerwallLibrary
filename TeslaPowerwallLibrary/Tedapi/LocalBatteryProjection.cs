// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Projects identified local Powerwall components into the shared battery-summary contract.</summary>
internal static class LocalBatteryProjection
	{
	/// <summary>Builds configured battery identities and attaches measurements only from the queried unit.</summary>
	/// <param name="configuration">Non-secret configured device identities.</param>
	/// <param name="queriedDin">Device whose component measurements were requested.</param>
	/// <param name="telemetry">Component measurements from that device.</param>
	/// <returns>Configured batteries; absent or unroutable measurements remain null.</returns>
	internal static IReadOnlyList<BatteryBlock>? Create (LocalConfiguration configuration, string queriedDin, LocalComponentTelemetry telemetry)
		{
		if (configuration.Batteries is null)
			return null;
		var result = new List<BatteryBlock> ();
		IReadOnlyList<LocalComponent>? Family (string name) => telemetry.Components is { } families && families.TryGetValue (name, out var values) ? values : null;
		var bms = Family ("bms");
		var hvp = Family ("hvp");
		var pch = Family ("pch");
		foreach (LocalBatteryConfiguration battery in configuration.Batteries)
			{
			BatteryBlock? block = Identity (battery.Din, battery.Type);
			if (block is null)
				continue;
			bool primary = string.Equals (battery.Din, queriedDin, StringComparison.Ordinal);
			if (primary && battery.Type?.IndexOf ("Powerwall3", StringComparison.OrdinalIgnoreCase) >= 0)
				{
				// The upstream protocol maps the first BMS slot to the queried Powerwall;
				// later BMS slots correspond to HVP expansion identities at the same index.
				LocalComponent? energy = bms?.Count > 0 ? bms[0] : null;
				LocalComponent? inverter = pch?.Count == 1 ? pch[0] : null;
				block = block with
					{
					NominalEnergyRemaining = Number (energy, "BMS_nominalEnergyRemaining") * 1000,
					NominalFullPackEnergy = Number (energy, "BMS_nominalFullPackEnergy") * 1000,
					PowerOut = Number (inverter, "PCH_BatteryPower"),
					VoltageOut = Number (inverter, "PCH_AcVoltageAB"),
					FrequencyOut = Number (inverter, "PCH_AcFrequency"),
					PinvState = Signal (inverter, "PCH_AcMode")?.TextValue
					};
				}
			result.Add (block);
			foreach (LocalBatteryExpansion expansion in battery.Expansions ?? Array.Empty<LocalBatteryExpansion> ())
				{
				BatteryBlock? expanded = Identity (expansion.Din, "BatteryExpansion");
				if (expanded is null)
					continue;
				if (primary && hvp is not null && bms is not null)
					{
					for (int index = 1; index < hvp.Count && index < bms.Count; index++)
						{
						if (!string.Equals (hvp[index].SerialNumber, expanded.PackageSerialNumber, StringComparison.Ordinal))
							continue;
						expanded = expanded with
							{
							NominalEnergyRemaining = Number (bms[index], "BMS_nominalEnergyRemaining") * 1000,
							NominalFullPackEnergy = Number (bms[index], "BMS_nominalFullPackEnergy") * 1000
							};
						break;
						}
					}
				result.Add (expanded);
				}
			}
		return result;
		}

	/// <summary>Attaches legacy bus measurements only when the identity and parallel message arrays can be correlated.</summary>
	/// <param name="blocks">Configured battery identities and any existing measurements.</param>
	/// <param name="bus">Reported legacy bus families.</param>
	/// <returns>Updated summaries; missing, stale or ambiguous measurements remain absent.</returns>
	internal static IReadOnlyList<BatteryBlock>? ApplyLegacy (IReadOnlyList<BatteryBlock>? blocks, LocalEnergyBusDevices? bus)
		{
		if (blocks is null || bus?.ThermalControllers is null) return blocks;
		var result = new List<BatteryBlock> (blocks.Count);
		foreach (var block in blocks)
			{
			var matches = bus.ThermalControllers.Select ((controller, index) => (controller, index))
				.Where (p => !string.IsNullOrWhiteSpace (p.controller.SerialNumber)
					&& p.controller.SerialNumber == block.PackageSerialNumber).ToArray ();
			if (matches.Length != 1 || block.Type == "BatteryExpansion")
				{ result.Add (block); continue; }
			int slot = matches[0].index;
			// These legacy responses contain no identity on POD/PINV. Only equal-length arrays establish their slots.
			var energy = bus.BatteryEnergy?.Count == bus.ThermalControllers.Count ? bus.BatteryEnergy[slot].Energy : null;
			var inverter = bus.BatteryInverters?.Count == bus.ThermalControllers.Count ? bus.BatteryInverters[slot].Status : null;
			if (energy?.IsMissing == true || energy?.IsComplete == false) energy = null;
			if (inverter?.IsMissing == true || inverter?.IsComplete == false) inverter = null;
			result.Add (block with
				{
				NominalEnergyRemaining = block.NominalEnergyRemaining ?? energy?.RemainingWattHours,
				NominalFullPackEnergy = block.NominalFullPackEnergy ?? energy?.FullCapacityWattHours,
				PowerOut = block.PowerOut ?? inverter?.PowerWatts,
				VoltageOut = block.VoltageOut ?? inverter?.VoltageVolts,
				FrequencyOut = block.FrequencyOut ?? inverter?.FrequencyHertz,
				PinvState = block.PinvState ?? inverter?.State,
				PinvGridState = block.PinvGridState ?? inverter?.GridState
				});
			}
		return result;
		}

	private static BatteryBlock? Identity (string? din, string? type)
		{
		if (string.IsNullOrWhiteSpace (din))
			return null;
		int separator = din!.IndexOf ("--", StringComparison.Ordinal);
		if (separator <= 0 || separator + 2 >= din.Length)
			return null;
		return new BatteryBlock { Type = type, PackagePartNumber = din.Substring (0, separator), PackageSerialNumber = din.Substring (separator + 2) };
		}

	private static LocalSignal? Signal (LocalComponent? component, string name) =>
		component?.Signals?.FirstOrDefault (signal => string.Equals (signal.Name, name, StringComparison.Ordinal));
	private static double? Number (LocalComponent? component, string name) => Signal (component, name)?.Value;
	}
