// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

internal static partial class LocalBatteryProjection
	{
	/// <summary>Attaches PW2 module counters only when the complete package/module layout is unambiguous.</summary>
	/// <param name="blocks">Configured batteries with existing measurements.</param>
	/// <param name="controller">Detailed controller telemetry containing thermal identities and MSA components.</param>
	/// <returns>Summaries with reported charged/discharged energy and reactive power; ambiguous module readings stay absent.</returns>
	internal static IReadOnlyList<BatteryBlock>? ApplyMsa (IReadOnlyList<BatteryBlock>? blocks, LocalTelemetry controller)
		{
		if (blocks is null || controller.Components is null || !controller.Components.TryGetValue ("msa", out var components) || components is null) return blocks;
		var serials = (controller.EnergyBus?.Devices?.ThermalControllers ?? Array.Empty<LocalThermalControllerBus> ())
			.Where (device => !string.IsNullOrWhiteSpace (device?.SerialNumber)).Select (device => device.SerialNumber!).ToArray ();
		var known = new HashSet<string> (serials, StringComparer.Ordinal);
		if (known.Count == 0 || known.Count != serials.Length) return blocks;
		var pairs = new Dictionary<string, LocalComponent> (StringComparer.Ordinal);
		var seen = new HashSet<string> (StringComparer.Ordinal);
		string? pending = null;
		// pypowerwall v0.21.0 _pw2_msa_module_pairing: a known TETHC must be
		// followed by its TEPINV. Serial-less slots do not establish an identity.
		foreach (var component in components)
			{
			string? serial = component?.SerialNumber;
			if (string.IsNullOrWhiteSpace (serial)) continue;
			string[] families = (component!.Signals ?? Array.Empty<LocalSignal> ())
				.Where (signal => !string.IsNullOrEmpty (signal?.Name)).Select (signal => signal.Name!.Split ('_')[0])
				.Distinct (StringComparer.Ordinal).ToArray ();
			string family = families.Length == 1 ? families[0] : string.Empty;
			if (family == "THC")
				{
				if (pending is not null || !known.Contains (serial!) || !seen.Add (serial!)) return blocks;
				pending = serial;
				}
			else if (family == "PINV")
				{
				if (pending is null || !seen.Add (serial!)) return blocks;
				pairs.Add (pending, component);
				pending = null;
				}
			else if (pending is not null) return blocks;
			}
		if (pending is not null || pairs.Count != known.Count) return blocks;
		return blocks.Select (block => block.PackageSerialNumber is not null
			&& block.Type != "BatteryExpansion" && block.Type?.IndexOf ("Powerwall3", StringComparison.OrdinalIgnoreCase) is not >= 0
			&& pairs.TryGetValue (block.PackageSerialNumber, out var module)
			? block with
				{
				EnergyCharged = block.EnergyCharged ?? MsaNumber (module, "PINV_EnergyCharged"),
				EnergyDischarged = block.EnergyDischarged ?? MsaNumber (module, "PINV_EnergyDischarged"),
				ReactivePowerOut = block.ReactivePowerOut ?? MsaNumber (module, "PINV_Qout")
				} : block).ToArray ();
		}

	private static double? MsaNumber (LocalComponent module, string name)
		{
		var matches = (module.Signals ?? Array.Empty<LocalSignal> ()).Where (signal => signal?.Name == name).ToArray ();
		return matches.Length == 1 ? matches[0].Value : null;
		}
	}
