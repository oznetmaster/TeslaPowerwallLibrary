// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

public static partial class LocalMeterProjection
	{
	/// <summary>Combines configured meters with available bus and inverter measurements without double-counting power.</summary>
	/// <remarks>Configured meter values take precedence. Bus and inverter readings fill missing fields only.
	/// Voltages are averages of reported measurements; no phase topology, power factor or current is invented.</remarks>
	/// <param name="snapshot">Controller data and independently identified device components.</param>
	/// <returns>Typed aggregates retaining authoritative power and unavailable measurements.</returns>
	public static MeterAggregates Aggregate (LocalDeviceSnapshot snapshot)
		{
#if NETFRAMEWORK
		if (snapshot is null) throw new ArgumentNullException (nameof (snapshot));
#else
		ArgumentNullException.ThrowIfNull (snapshot);
#endif
		var basis = Aggregate (snapshot.Configuration, snapshot.Controller);
		var bus = snapshot.Controller.EnergyBus?.Devices;
		var x = bus?.Sync?.MeterX;
		var y = bus?.Sync?.MeterY;
		var z = bus?.MeterAssembly?.MeterZ;
		var island = bus?.Islander?.AcMeasurements;
		MeterReading? site = Available (x) ? new MeterReading
			{
			InstantAverageVoltage = Mean (x!.Line1NeutralVolts, x.Line2NeutralVolts, x.Line3NeutralVolts),
			PhaseACurrent = x.CurrentAAmps, PhaseBCurrent = x.CurrentBAmps, PhaseCCurrent = x.CurrentCAmps
			} : MeterComponent (snapshot.Controller.Components, "METER_X_", "N");
		site = Fill (site, Available (z) ? new MeterReading
			{
			InstantAverageVoltage = Mean (z!.Line1GroundVolts, z.Line2GroundVolts, z.Line3GroundVolts),
			PhaseACurrent = z.CurrentAAmps, PhaseBCurrent = z.CurrentBAmps, PhaseCCurrent = z.CurrentCAmps
			} : MeterComponent (snapshot.Controller.Components, "METER_Z_", "G"));
		if (Available (island))
			site = Fill (site, new MeterReading { InstantAverageVoltage = Mean (island!.MainLine1NeutralVolts, island.MainLine2NeutralVolts, island.MainLine3NeutralVolts),
				Frequency = Mean (island.MainLine1FrequencyHertz, island.MainLine2FrequencyHertz, island.MainLine3FrequencyHertz) });
		MeterReading? solar = Available (y) ? new MeterReading
			{
			InstantAverageVoltage = Mean (y!.Line1NeutralVolts, y.Line2NeutralVolts, y.Line3NeutralVolts),
			PhaseACurrent = y.CurrentAAmps, PhaseBCurrent = y.CurrentBAmps, PhaseCCurrent = y.CurrentCAmps
			} : MeterComponent (snapshot.Controller.Components, "METER_Y_", "N");
		var pv = (bus?.SolarInverters ?? Array.Empty<LocalSolarInverterBus> ()).Where (i => Available (i.Status)).Select (i => i.Status!).ToArray ();
		var batteries = (bus?.BatteryInverters ?? Array.Empty<LocalBatteryInverterBus> ()).Where (i => Available (i.Status)).Select (i => i.Status!).ToArray ();
		var components = snapshot.Devices.Where (d => d.Telemetry?.Components is not null)
			.SelectMany (d => d.Telemetry!.Components!.Where (f => f.Key == "pch").SelectMany (f => f.Value)).ToArray ();
		double? Signal (LocalComponent component, string name) => component.Signals?.FirstOrDefault (s => s.Name == name)?.Value;
		var voltages = components.Select (c => Signal (c, "PCH_AcVoltageAB")).ToArray ();
		var frequencies = components.Select (c => Signal (c, "PCH_AcFrequency")).ToArray ();
		solar = Fill (solar, new MeterReading { InstantAverageVoltage = Mean (pv.Select (p => p.VoltageVolts).Concat (voltages).ToArray ()),
			Frequency = Mean (pv.Select (p => p.FrequencyHertz).Concat (frequencies).ToArray ()) });
		var battery = new MeterReading { InstantAverageVoltage = Mean (batteries.Select (p => p.VoltageVolts).Concat (voltages).ToArray ()),
			Frequency = Mean (batteries.Select (p => p.FrequencyHertz).Concat (frequencies).ToArray ()) };
		MeterReading? load = Available (island) ? new MeterReading
			{ InstantAverageVoltage = Mean (island!.LoadLine1NeutralVolts, island.LoadLine2NeutralVolts, island.LoadLine3NeutralVolts),
			Frequency = Mean (island.LoadLine1FrequencyHertz, island.LoadLine2FrequencyHertz, island.LoadLine3FrequencyHertz) } : null;
		return new MeterAggregates { Site = Fill (basis.Site, site), Solar = Fill (basis.Solar, solar), Battery = Fill (basis.Battery, battery), Load = Fill (basis.Load, load) };
		}

	private static MeterReading? MeterComponent (IReadOnlyDictionary<string, IReadOnlyList<LocalComponent>>? families, string prefix, string voltageReference)
		{
		if (families is null) return null;
		var meters = families.Values.SelectMany (components => components).Where (c => c.Signals?.Any (s => s.Name?.StartsWith (prefix, StringComparison.Ordinal) == true) == true).ToArray ();
		// Do not combine indistinguishable meter instances or overwrite one with another.
		if (meters.Length != 1) return null;
		double? Signal (string name) => meters[0].Signals?.FirstOrDefault (s => s.Name == name)?.Value;
		return new MeterReading
			{
			InstantAverageVoltage = Mean (Signal (prefix + "VL1" + voltageReference), Signal (prefix + "VL2" + voltageReference), Signal (prefix + "VL3" + voltageReference)),
			PhaseACurrent = Signal (prefix + "CTA_I"), PhaseBCurrent = Signal (prefix + "CTB_I"), PhaseCCurrent = Signal (prefix + "CTC_I")
			};
		}

	private static bool Available (LocalBusMessage? message) => message is not null && message.IsMissing != true && message.IsComplete != false;

	private static double? Mean (params double?[] measurements)
		{
		var values = measurements.Where (v => v.HasValue).Select (v => v!.Value).ToArray ();
		return values.Length == 0 ? null : values.Average ();
		}

	private static MeterReading? Fill (MeterReading? primary, MeterReading? secondary)
		{
		if (secondary is null) return primary;
		if (primary is null && secondary.InstantAverageVoltage is null && secondary.Frequency is null
			&& secondary.PhaseACurrent is null && secondary.PhaseBCurrent is null && secondary.PhaseCCurrent is null) return null;
		primary ??= new MeterReading ();
		return primary with
			{
			InstantAverageVoltage = primary.InstantAverageVoltage ?? secondary.InstantAverageVoltage,
			Frequency = primary.Frequency ?? secondary.Frequency,
			PhaseACurrent = primary.PhaseACurrent ?? secondary.PhaseACurrent,
			PhaseBCurrent = primary.PhaseBCurrent ?? secondary.PhaseBCurrent,
			PhaseCCurrent = primary.PhaseCCurrent ?? secondary.PhaseCCurrent
			};
		}
	}
