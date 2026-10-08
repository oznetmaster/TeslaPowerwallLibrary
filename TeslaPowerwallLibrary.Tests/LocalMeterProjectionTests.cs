// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks meter identity, per-slot configuration, wire names, scaling and missing measurements.</summary>
[TestFixture]
public sealed class LocalMeterProjectionTests
	{
	/// <summary>Remote-meter signal strength uses the upstream fixture's numeric wire field and preserves absence.</summary>
	[TestCase ("{\"rssiDb\":-50}", -50d)]
	[TestCase ("{\"rssiDb\":0}", 0d)]
	[TestCase ("{\"rssiDb\":null}", null)]
	[TestCase ("{}", null)]
	public void RemoteMeterSignalStrength_PreservesReportedValue (string json, double? expected)
		{
		// Numeric rssiDb is documented in upstream test_tedapi_remote_meter.py; this is an offline fixture.
		var reading = JsonSerializer.Deserialize<LocalRemoteMeterReading> (json)!;
		Assert.That (reading.SignalStrengthDb, Is.EqualTo (expected));
		string serialized = JsonSerializer.Serialize (reading);
		Assert.That (serialized, Does.Contain ("\"rssiDb\":").And.Not.Contain ("SignalStrengthDb"));
		Assert.That (JsonSerializer.Deserialize<LocalRemoteMeterReading> (serialized)!.SignalStrengthDb, Is.EqualTo (expected));
		}

	/// <summary>Multiple meters keep separate CT indexes; disabled slots never shift phase assignments.</summary>
	[Test]
	public void RemoteMeters_PreserveIdentityAndSparseSlots ()
		{
		var config = JsonSerializer.Deserialize<LocalConfiguration> ("""
			{"meters":[
			{"type":"trm_mb","location":"site","cts":[true,false,true],"inverted":[false,false,true],"connection":{"device_serial":"meter-a"},"real_power_scale_factor":2},
			{"type":"trm_mb","location":"solar","cts":[true],"connection":{"device_serial":"meter-b"}}]}
			""")!;
		var telemetry = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"teslaRemoteMeter":{"meters":[
			{"din":"meter-a","reading":{"timestamp":"device-time","firmwareVersion":"1","ctReadings":[{"realPowerW":0},{"realPowerW":900},{"realPowerW":-50,"energyImportedWs":3600}]}},
			{"din":"meter-b","reading":{"ctReadings":[{"realPowerW":100}]}}]}}
			""")!;
		var result = LocalMeterProjection.Create (config, telemetry);
		Assert.That (result, Has.Count.EqualTo (2));
		Assert.That (result[0].Channels.Select (c => c.Index), Is.EqualTo (new[] { 0, 2 }));
		Assert.That (result[0].Channels[0].RealPowerWatts, Is.Zero);
		Assert.That (result[0].Channels[1].RealPowerWatts, Is.EqualTo (-100));
		Assert.That (result[0].Channels[1].Inverted, Is.True, "Inversion metadata must not cause a second sign reversal.");
		Assert.That (result[0].Channels[1].Reported.EnergyImportedWattSeconds, Is.EqualTo (3600));
		Assert.That (result[0].Channels[1].Reported.VoltageVolts, Is.Null);
		Assert.That (result[1].DeviceId, Is.EqualTo ("meter-b"));
		Assert.That (result[1].Channels[0].Location, Is.EqualTo ("solar"));
		Assert.That (result[1].Channels[0].RealPowerWatts, Is.EqualTo (100));
		Assert.That (JsonSerializer.Deserialize<LocalConfiguration> (JsonSerializer.Serialize (config))!.Meters![0].InvertedChannels![2], Is.True);
		}

	/// <summary>Split assignments use each enabled slot's own location and factor, retaining explicit zero.</summary>
	[Test]
	public void SplitAssignments_DoNotBorrowAnotherSlotsScale ()
		{
		var config = JsonSerializer.Deserialize<LocalConfiguration> ("""
			{"meters":[
			{"type":"neurio_w2_tcp","location":"site","cts":[true,false,false],"connection":{"device_serial":"n1"},"real_power_scale_factor":2},
			{"type":"neurio_w2_tcp","location":"solar","cts":[false,true,true],"connection":{"device_serial":"n1"},"real_power_scale_factor":0}]}
			""")!;
		var data = JsonSerializer.Deserialize<LocalTelemetry> ("""{"neurio":{"readings":[{"serial":"n1","dataRead":[{"realPowerW":10},{"realPowerW":25},{}]}]}}""")!;
		var channels = LocalMeterProjection.Create (config, data).Single ().Channels;
		Assert.That (channels.Select (c => c.RealPowerWatts), Is.EqualTo (new double?[] { 20, 0, null }));
		Assert.That (channels.Select (c => c.Location), Is.EqualTo (new[] { "site", "solar", "solar" }));
		}

	/// <summary>Unconfigured measurements remain accessible and unidentified meters are not given fabricated identifiers.</summary>
	[Test]
	public void UnconfiguredAndMissingMeters_RemainUninvented ()
		{
		var data = JsonSerializer.Deserialize<LocalTelemetry> ("""{"neurio":{"readings":[{"dataRead":[{}, {"realPowerW":0}]}]}}""")!;
		var result = LocalMeterProjection.Create (new LocalConfiguration (), data).Single ();
		Assert.That (result.DeviceId, Is.Null);
		Assert.That (result.Channels[0].IsConfigured, Is.False);
		Assert.That (result.Channels[0].Location, Is.Null);
		Assert.That (result.Channels[0].RealPowerWatts, Is.Null);
		Assert.That (result.Channels[1].RealPowerWatts, Is.Zero);
		Assert.That (LocalMeterProjection.Create (new LocalConfiguration (), new LocalTelemetry ()), Is.Empty);
		}

	/// <summary>Ambiguous enabled assignments are rejected rather than silently choosing an incorrect location.</summary>
	[Test]
	public void ConflictingAssignments_AreRejected ()
		{
		var config = JsonSerializer.Deserialize<LocalConfiguration> ("""
			{"meters":[{"type":"trm_mb","location":"site","cts":[true],"connection":{"device_serial":"m1"}},
			{"type":"trm_mb","location":"solar","cts":[true],"connection":{"device_serial":"m1"}}]}
			""")!;
		var data = JsonSerializer.Deserialize<LocalTelemetry> ("""{"teslaRemoteMeter":{"meters":[{"din":"m1","reading":{"ctReadings":[{"realPowerW":10}]}}]}}""")!;
		Assert.Throws<PowerwallInvalidConfigurationException> (() => LocalMeterProjection.Create (config, data));
		}
	/// <summary>Aggregate energy is converted from watt-seconds, and partial channels cannot become a false complete total.</summary>
	[Test]
	public void Aggregates_ConvertUnitsAndPreserveControllerPowerAndUnknowns ()
		{
		var config = JsonSerializer.Deserialize<LocalConfiguration> ("""
			{"meters":[{"type":"trm_mb","location":"site","cts":[true,true],"connection":{"device_serial":"m1"},"real_power_scale_factor":2}]}
			""")!;
		var data = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"control":{"meterAggregates":[{"location":"SITE","realPowerW":0}]},"teslaRemoteMeter":{"meters":[{"din":"m1","reading":{"timestamp":"reported-time","ctReadings":[
			{"realPowerW":100,"currentA":2,"voltageV":230,"energyImportedWs":3600,"energyExportedWs":0},
			{"realPowerW":200,"currentA":3,"voltageV":232,"energyImportedWs":7200}]}}]}}
			""")!;
		var site = LocalMeterProjection.Aggregate (config, data).Site!;
		Assert.That (site.InstantPower, Is.Zero, "The authoritative controller reading must not be replaced or double-counted.");
		Assert.That (site.EnergyImported, Is.EqualTo (3));
		Assert.That (site.EnergyExported, Is.Null, "One missing CT energy prevents a complete total.");
		Assert.That (site.InstantAverageVoltage, Is.EqualTo (231));
		Assert.That (site.InstantTotalCurrent, Is.EqualTo (5));
		Assert.That (site.InstantAverageCurrent, Is.EqualTo (2.5));
		Assert.That (site.NumMetersAggregated, Is.EqualTo (1));
		Assert.That (site.LastCommunicationTime, Is.EqualTo ("reported-time"));
		Assert.That (LocalMeterProjection.Aggregate (config, data).Solar, Is.Null);
		}

	/// <summary>Bus voltage and phase currents augment controller power without fabricating totals or replacing zeros.</summary>
	[Test]
	public void BusAggregates_FillMeasurementsWithoutDoubleCountingPower ()
		{
		var snapshot = JsonSerializer.Deserialize<LocalDeviceSnapshot> ("""
			{"controller":{"control":{"meterAggregates":[{"location":"SITE","realPowerW":0},{"location":"SOLAR","realPowerW":5000}]},
			 "esCan":{"bus":{"SYNC":{"METER_X_AcMeasurements":{"isMIA":false,"METER_X_CTA_I":0,"METER_X_CTC_I":3,"METER_X_VL1N":230},
			 "METER_Y_AcMeasurements":{"isMIA":true,"METER_Y_CTA_I":999}},
			 "PVAC":[{"PVAC_Status":{"isMIA":false,"PVAC_Vout":240,"PVAC_Fout":50}},{"PVAC_Status":{"isMIA":true,"PVAC_Vout":1000}}],
			 "PINV":[{"PINV_Status":{"PINV_Vout":0,"PINV_Fout":0}}],
			 "ISLANDER":{"ISLAND_AcMeasurements":{"ISLAND_VL1N_Load":231,"ISLAND_FreqL1_Load":50}}}}}}
			""")!;
		var result = LocalMeterProjection.Aggregate (snapshot);
		Assert.That (result.Site!.InstantPower, Is.Zero);
		Assert.That (result.Site.PhaseACurrent, Is.Zero);
		Assert.That (result.Site.PhaseBCurrent, Is.Null);
		Assert.That (result.Site.PhaseCCurrent, Is.EqualTo (3));
		Assert.That (result.Site.InstantTotalCurrent, Is.Null);
		Assert.That (result.Solar!.InstantPower, Is.EqualTo (5000));
		Assert.That (result.Solar.InstantAverageVoltage, Is.EqualTo (240));
		Assert.That (result.Solar.PhaseACurrent, Is.Null);
		Assert.That (result.Solar.InstantAverageCurrent, Is.Null, "Power divided by voltage is not measured RMS current.");
		Assert.That (result.Battery!.InstantAverageVoltage, Is.Zero);
		Assert.That (result.Load!.InstantAverageVoltage, Is.EqualTo (231));
		Assert.That (result.Load.InstantPower, Is.Null);
		Assert.That (LocalMeterProjection.Aggregate (new LocalDeviceSnapshot ()).Solar, Is.Null);
		}

	/// <summary>Original CT slots remain phase-aligned and configured values take precedence over alternate bus data.</summary>
	[Test]
	public void SparsePhaseCurrents_PreserveSlotsAndPreferConfiguredMeters ()
		{
		var snapshot = JsonSerializer.Deserialize<LocalDeviceSnapshot> ("""
			{"configuration":{"meters":[{"type":"trm_mb","location":"site","cts":[true,false,true],"connection":{"device_serial":"meter"}}]},
			 "controller":{"teslaRemoteMeter":{"meters":[{"din":"meter","reading":{"ctReadings":[{"currentA":0,"voltageV":0},{"currentA":999},{"currentA":-2,"voltageV":230}]}}]},
			 "esCan":{"bus":{"SYNC":{"METER_X_AcMeasurements":{"METER_X_CTA_I":40,"METER_X_CTB_I":5,"METER_X_CTC_I":60,"METER_X_VL1N":900}}}}}}
			""")!;
		var site = LocalMeterProjection.Aggregate (snapshot).Site!;
		Assert.That (site.PhaseACurrent, Is.Zero);
		Assert.That (site.PhaseBCurrent, Is.EqualTo (5));
		Assert.That (site.PhaseCCurrent, Is.EqualTo (-2));
		Assert.That (site.InstantAverageVoltage, Is.EqualTo (115));
		var copied = JsonSerializer.Deserialize<TeslaPowerwallLibrary.Models.MeterReading> (JsonSerializer.Serialize (site))!;
		Assert.That (copied.PhaseCCurrent, Is.EqualTo (-2));
		}

	/// <summary>The newer query's SYNC component signals supply the same meter fields without a legacy SYNC bus object.</summary>
	[Test]
	public void SyncComponentMeters_SupportNewQueryAndRejectAmbiguousDevices ()
		{
		var controller = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"components":{"sync":[{"serialNumber":"sync-a","signals":[
			{"name":"METER_X_CTA_I","value":0},{"name":"METER_X_VL1N","value":230},
			{"name":"METER_Y_CTC_I","value":-2},{"name":"METER_Y_VL3N","value":231}]}]}}
			""")!;
		var result = LocalMeterProjection.Aggregate (new LocalDeviceSnapshot { Controller = controller });
		Assert.That (result.Site!.PhaseACurrent, Is.Zero);
		Assert.That (result.Site.InstantAverageVoltage, Is.EqualTo (230));
		Assert.That (result.Solar!.PhaseCCurrent, Is.EqualTo (-2));
		Assert.That (result.Solar.InstantAverageVoltage, Is.EqualTo (231));
		Assert.That (result.Solar.InstantPower, Is.Null);
		var duplicate = controller with { Components = new Dictionary<string, IReadOnlyList<LocalComponent>>
			{ ["sync"] = new[] { controller.Components!["sync"][0], controller.Components["sync"][0] with { SerialNumber = "sync-b" } } } };
		var ambiguous = LocalMeterProjection.Aggregate (new LocalDeviceSnapshot { Controller = duplicate });
		Assert.That (ambiguous.Site, Is.Null);
		Assert.That (ambiguous.Solar, Is.Null);
		}

	/// <summary>Meter Z and Powerwall 3 electrical readings contribute only values actually present.</summary>
	[Test]
	public void MeterZAndPowerwall3_AugmentNativeReadings ()
		{
		var snapshot = JsonSerializer.Deserialize<LocalDeviceSnapshot> ("""
			{"controller":{"components":{"msa":[{"signals":[{"name":"METER_Z_CTA_I","value":0},{"name":"METER_Z_VL1G","value":230}]}]}},
			 "devices":[{"din":"one","telemetry":{"components":{"pch":[{"signals":[{"name":"PCH_AcVoltageAB","value":240},{"name":"PCH_AcFrequency","value":50}]}]}}},
			 {"din":"two","unavailableReason":"timeout"}]}
			""")!;
		var result = LocalMeterProjection.Aggregate (snapshot);
		Assert.That (result.Site!.PhaseACurrent, Is.Zero);
		Assert.That (result.Site.PhaseBCurrent, Is.Null);
		Assert.That (result.Battery!.InstantAverageVoltage, Is.EqualTo (240));
		Assert.That (result.Battery.Frequency, Is.EqualTo (50));
		Assert.That (result.Battery.InstantPower, Is.Null);
		Assert.That (result.Solar!.InstantAverageVoltage, Is.EqualTo (240));
		}

	}
