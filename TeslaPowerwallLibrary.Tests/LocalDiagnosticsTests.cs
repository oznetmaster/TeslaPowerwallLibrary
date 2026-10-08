// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks identified diagnostics, native units and availability through attributed models.</summary>
[TestFixture]
public sealed class LocalDiagnosticsTests
	{
	/// <summary>The captured June 2026 false state stays distinct from missing or empty protection-test data.</summary>
	[TestCase ("{\"control\":{\"protectionTripTests\":{\"isRunning\":false}}}", false)]
	[TestCase ("{\"control\":{\"protectionTripTests\":{\"isRunning\":true}}}", true)]
	[TestCase ("{\"control\":{\"protectionTripTests\":{}}}", null)]
	[TestCase ("{\"control\":{}}", null)]
	public void ProtectionTestStatus_PreservesReportedAndMissingStates (string json, bool? expected)
		{
		var data = JsonSerializer.Deserialize<LocalTelemetry> (json)!;
		Assert.That (data.Control!.ProtectionTripTests?.IsRunning, Is.EqualTo (expected));
		string encoded = JsonSerializer.Serialize (data);
		Assert.That (encoded, Does.Contain ("\"protectionTripTests\":").And.Not.Contain ("ProtectionTripTests"));
		Assert.That (JsonSerializer.Deserialize<LocalTelemetry> (encoded)!.Control!.ProtectionTripTests?.IsRunning, Is.EqualTo (expected));
		}

	/// <summary>Real zeros, negative temperatures and missing measurements survive projection and serialization.</summary>
	[Test]
	public void Components_PreserveIdentityUnitsAndMissingValues ()
		{
		var snapshot = JsonSerializer.Deserialize<LocalDeviceSnapshot> ("""
			{"configuration":{"vin":"leader"},"controller":{"components":{"msa":[{"partNumber":"legacy","serialNumber":"fan","signals":[
			 {"name":"PVAC_Fan_Speed_Actual_RPM","value":0},{"name":"PVAC_Fan_Speed_Target_RPM","value":500},{"name":"THC_AmbientTemp","value":-2}]}]}},
			 "devices":[{"din":"one","telemetry":{"components":{"pch":[{"serialNumber":"inverter","signals":[
			 {"name":"PCH_FanSpeed_A","value":0},{"name":"PCH_FanDuty_A","value":0},{"name":"PCH_FanDuty_B","value":25},
			 {"name":"PCH_heatsinkTemp","value":42,"timestamp":"device-time"},{"name":"PCH_AmbientTemp","value":null},
			 {"name":"PCH_PvState_A","textValue":"Pv_Standby"},{"name":"PCH_PvVoltageA","value":0},{"name":"PCH_PvCurrentA","value":0},
			 {"name":"PCH_PvVoltageB","value":230},{"name":"PCH_PvVoltageF","value":250},{"name":"PCH_PvCurrentF","value":2}]}]}}},
			 {"din":"two","telemetry":{"components":{"pch":[{"serialNumber":"inverter","signals":[{"name":"PCH_FanSpeed_A","value":400}]}]}}},
			 {"din":"unavailable","unavailableReason":"timeout"}]}
			""")!;
		var items = LocalDiagnosticsProjection.Create (snapshot);
		Assert.That (items, Has.Count.EqualTo (3));
		var legacy = items.Single (i => i.DeviceDin == "leader");
		Assert.That (legacy.Fans.Single ().SpeedRpm, Is.Zero);
		Assert.That (legacy.Fans.Single ().TargetSpeedRpm, Is.EqualTo (500));
		Assert.That (legacy.Fans.Single ().DutyPercent, Is.Null);
		Assert.That (legacy.Temperatures.Single ().Celsius, Is.EqualTo (-2));
		var first = items.Single (i => i.DeviceDin == "one");
		Assert.That (first.Fans[0].SpeedRpm, Is.Zero);
		Assert.That (first.Fans[1].SpeedRpm, Is.Null);
		Assert.That (first.Fans[1].DutyPercent, Is.EqualTo (25));
		Assert.That (first.Fans.All (f => f.TargetSpeedRpm is null), Is.True);
		Assert.That (first.Temperatures[0].Timestamp, Is.EqualTo ("device-time"));
		Assert.That (first.Temperatures[1].Celsius, Is.Null);
		Assert.That (first.SolarStrings.Select (p => p.Name), Is.EqualTo (new[] { "A", "B", "F" }));
		Assert.That (first.SolarStrings[0].PowerWatts, Is.Zero);
		Assert.That (first.SolarStrings[0].Connected, Is.Null, "Standby is not proof of a disconnected string.");
		Assert.That (first.SolarStrings[1].PowerWatts, Is.Null);
		Assert.That (first.SolarStrings[2].PowerWatts, Is.EqualTo (500));
		Assert.That (items.Single (i => i.DeviceDin == "two").Fans.Single ().SpeedRpm, Is.EqualTo (400));
		string json = JsonSerializer.Serialize (first);
		Assert.That (json, Does.Contain ("\"speedRpm\":0").And.Not.Contain ("PowerWatts"));
		Assert.That (JsonSerializer.Deserialize<LocalComponentDiagnostics> (json)!.SolarStrings[2].PowerWatts, Is.EqualTo (500));
		}

	/// <summary>Missing or incomplete legacy bus messages cannot become fresh solar readings.</summary>
	[TestCase (true, null), TestCase (false, false)]
	public void Legacy_UnavailableMessagesAreOmitted (bool missing, bool? complete)
		{
		var bus = new LocalEnergyBusDevices { SolarInverters = new[] { new LocalSolarInverterBus
			{ Measurements = new LocalSolarInverterMeasurements { IsMissing = missing, IsComplete = complete, VoltageAVolts = 250, CurrentAAmps = 2 } } } };
		var items = LocalDiagnosticsProjection.Create (new LocalDeviceSnapshot { Controller = new LocalTelemetry { EnergyBus = new LocalEnergyBusTelemetry { Devices = bus } } });
		Assert.That (items, Is.Empty);
		}

	/// <summary>Legacy string measurements and explicit connection flags retain their separate device families.</summary>
	[Test]
	public void Legacy_StringsRetainAvailableMeasurements ()
		{
		var data = JsonSerializer.Deserialize<LocalDeviceSnapshot> ("""
			{"configuration":{"vin":"gateway"},"controller":{"esCan":{"bus":{
			"PVAC":[{"packageSerialNumber":"solar","PVAC_Logging":{"isMIA":false,"PVAC_PVMeasuredVoltage_A":200,"PVAC_PVCurrent_A":3,"PVAC_PVCurrent_B":0}}],
			"PVS":[{"PVS_Status":{"isMIA":false,"PVS_StringA_Connected":true,"PVS_StringB_Connected":false}}]}}}}
			""")!;
		var items = LocalDiagnosticsProjection.Create (data);
		Assert.That (items[0].SerialNumber, Is.EqualTo ("solar"));
		Assert.That (items[0].SolarStrings[0].PowerWatts, Is.EqualTo (600));
		Assert.That (items[0].SolarStrings[1].PowerWatts, Is.Null, "Zero current cannot fabricate a missing voltage.");
		Assert.That (items[1].SolarStrings[0].Connected, Is.True);
		Assert.That (items[1].SolarStrings[1].Connected, Is.False);
		Assert.That (items[1].SolarStrings[2].Connected, Is.Null);
		}
	/// <summary>Vitals include legacy and follower alerts without serial collisions or stale bus messages.</summary>
	[Test]
	public void Vitals_IncludeAllAvailableFamiliesWithoutCollisions ()
		{
		var snapshot = JsonSerializer.Deserialize<LocalDeviceSnapshot> ("""
			{"controller":{"control":{"alerts":{"active":["controller-alert"]}},"esCan":{"bus":{
			"PVAC":[{"alerts":{"isMIA":false,"isComplete":true,"active":["solar-alert"]},"PVAC_Status":{"PVAC_Pout":0}}],
			"PINV":[{"alerts":{"isMIA":true,"active":["stale-alert"]},"PINV_Status":{"isMIA":true,"PINV_Pout":999}}]}}},
			 "devices":[{"din":"one","telemetry":{"components":{"pch":[{"serialNumber":"same","signals":[{"name":"zero","value":0}],"activeAlerts":[{"name":"first"}]}]}}},
			 {"din":"two","telemetry":{"components":{"pch":[{"serialNumber":"same","signals":[{"name":"flag","boolValue":false}],"activeAlerts":[{"name":"second"}]}]}}}]}
			""")!;
		var values = LocalVitalsProjection.Create (snapshot, "controller");
		Assert.That (values["one/pch/0/same"]["zero"], Is.EqualTo (0));
		Assert.That (values["two/pch/0/same"]["flag"], Is.False);
		Assert.That (values["controller/bus/PVAC/0"]["PVAC_Pout"], Is.EqualTo (0));
		Assert.That (values["controller/bus/PINV/0"].ContainsKey ("PINV_Pout"), Is.False);
		var alerts = values.Values.Where (v => v.TryGetValue ("alerts", out var list) && list is IEnumerable<string>)
			.SelectMany (v => (IEnumerable<string>)v["alerts"]!).ToArray ();
		Assert.That (alerts, Is.EquivalentTo (new[] { "controller-alert", "solar-alert", "first", "second" }));
		}

	}
