// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using TeslaPowerwallLibrary.Models;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks legacy bus wire fields, identities, availability and native units.</summary>
[TestFixture]
public sealed class LocalEnergyBusTests
	{
	/// <summary>Captured empty bus slots remain distinct from absent groups and explicit zero progress.</summary>
	[Test]
	public void LegacyFirmware_PreservesNullSlotsAndNativeProgress ()
		{
		var captured = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"esCan":{"firmwareUpdate":{"isUpdating":false,"msa":null,"powerwalls":[null,null,null,null,null,null,null,null],"pvInverters":null,"sync":null}}}
			""")!;
		Assert.That (captured.EnergyBus!.FirmwareUpdate!.IsUpdating, Is.False);
		Assert.That (captured.EnergyBus.FirmwareUpdate.Powerwalls, Has.Count.EqualTo (8));
		Assert.That (captured.EnergyBus.FirmwareUpdate.Powerwalls, Is.All.Null);
		// Progress member names follow the bundled signed query; this populated entry is a synthetic fixture.
		var value = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"esCan":{"firmwareUpdate":{"isUpdating":true,"powerwalls":[null,{"updating":true,"numSteps":4,"currentStep":0,"currentStepProgress":0,"progress":0},null]}}}
			""")!;
		var copy = JsonSerializer.Deserialize<LocalTelemetry> (JsonSerializer.Serialize (value))!;
		var slots = copy.EnergyBus!.FirmwareUpdate!.Powerwalls!;
		Assert.That (slots, Has.Count.EqualTo (3));
		Assert.That (slots[0], Is.Null);
		Assert.That (slots[2], Is.Null);
		Assert.That (slots[1]!.NumberOfSteps, Is.EqualTo (4));
		Assert.That (slots[1]!.Progress, Is.Zero);
		Assert.That (JsonSerializer.Deserialize<LocalTelemetry> ("{\"esCan\":{}}")!.EnergyBus!.FirmwareUpdate, Is.Null);
		}

	/// <summary>Observed legacy hash arrays and diagnostic flags round-trip without string coercion or lost zeros.</summary>
	[Test]
	public void BusDiagnostics_PreserveNativeWireTypes ()
		{
		var value = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"system":{"supportMode":{"remoteService":{"isEnabled":false,"expiryTime":"2026-10-08T01:00:00Z","sessionId":"must-not-be-exposed"}}},
			"esCan":{"bus":{
			"PVAC":[{"subPackagePartNumber":"subpart","subPackageSerialNumber":"subserial","PVAC_InfoMsg":{"PVAC_appGitHash":[0,255,4294967295]},"PVAC_Logging":{"isMIA":false,"PVAC_Fan_Speed_Actual_RPM":0,"PVAC_Fan_Speed_Target_RPM":800}}],
			"POD":[{"POD_InfoMsg":{"POD_appGitHash":[1,2]}}],
			"THC":[{"THC_InfoMsg":{"isMIA":true,"isComplete":false,"THC_appGitHash":[3]},"THC_Logging":{"THC_LOG_PW_2_0_EnableLineState":"Disabled"}}],
			"PVS":[{"PVS_Logging":{"PVS_numStringsLockoutBits":0,"PVS_sbsComplete":false}}],
			"SYNC":{"SYNC_InfoMsg":{"SYNC_appGitHash":[4],"SYNC_assemblyId":0}}}}}
			""")!;
		var bus = value.EnergyBus!.Devices!;
		Assert.That (bus.SolarInverters![0].Firmware!.ApplicationGitHash, Is.EqualTo (new long[] { 0, 255, 4294967295 }));
		Assert.That (bus.SolarInverters[0].SubPackageSerialNumber, Is.EqualTo ("subserial"));
		Assert.That (bus.BatteryEnergy![0].Firmware!.ApplicationGitHash, Is.EqualTo (new long[] { 1, 2 }));
		Assert.That (bus.ThermalControllers![0].Firmware!.IsMissing, Is.True);
		Assert.That (bus.ThermalControllers[0].Logging!.EnableLineState, Is.EqualTo ("Disabled"));
		Assert.That (bus.SolarStrings![0].Logging!.StringLockoutBits, Is.Zero);
		Assert.That (bus.SolarStrings[0].Logging!.SbsComplete, Is.False);
		Assert.That (bus.Sync!.Firmware!.AssemblyId, Is.Zero);
		var diagnostics = LocalDiagnosticsProjection.Create (new LocalDeviceSnapshot { Controller = value });
		Assert.That (diagnostics.Single ().Fans.Single ().SpeedRpm, Is.Zero);
		Assert.That (diagnostics.Single ().Fans.Single ().TargetSpeedRpm, Is.EqualTo (800));
		string serialized = JsonSerializer.Serialize (value);
		Assert.That (serialized, Does.Not.Contain ("must-not-be-exposed").And.Not.Contain ("sessionId"));
		var copy = JsonSerializer.Deserialize<LocalTelemetry> (serialized)!;
		Assert.That (copy.System!.SupportMode!.RemoteService!.IsEnabled, Is.False);
		Assert.That (copy.EnergyBus!.Devices!.SolarInverters![0].Firmware!.ApplicationGitHash, Is.EqualTo (new long[] { 0, 255, 4294967295 }));
		}

	/// <summary>Direct deserialization retains state strings, negative power, zero energy and omitted fields.</summary>
	[Test]
	public void EnergyBus_UsesAttributesAndRetainsMissingValues ()
		{
		var data = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"esCan":{"enumeration":{"inProgress":false,"numACPW":1},"bus":{
			"THC":[{"packagePartNumber":"part","packageSerialNumber":"serial"}],
			"POD":[{"POD_EnergyStatus":{"isMIA":false,"POD_nom_energy_remaining":0,"POD_nom_full_pack_energy":13500}}],
			"PINV":[{"PINV_Status":{"PINV_Pout":-1250,"PINV_Fout":50,"PINV_State":"PINV_GridFollowing","PINV_GridState":"Grid_Compliant"}}],
			"SYNC":{"METER_X_AcMeasurements":{"METER_X_CTC_InstRealPower":-75,"METER_X_VL3N":230}},
			"ISLANDER":{"ISLAND_GridConnection":{"ISLAND_GridConnected":"ISLAND_GridConnected_Connected"},"ISLAND_AcMeasurements":{"ISLAND_VL1N_Load":230}}
			}}}
			""")!;
		var bus = data.EnergyBus!.Devices!;
		Assert.That (data.EnergyBus.Enumeration!.InProgress, Is.False);
		Assert.That (bus.BatteryEnergy![0].Energy!.RemainingWattHours, Is.Zero);
		Assert.That (bus.BatteryInverters![0].Status!.PowerWatts, Is.EqualTo (-1250));
		Assert.That (bus.BatteryInverters[0].Status!.VoltageVolts, Is.Null);
		Assert.That (bus.Sync!.MeterX!.RealPowerCWatts, Is.EqualTo (-75));
		Assert.That (bus.Islander!.GridConnection!.GridConnected, Is.EqualTo ("ISLAND_GridConnected_Connected"));
		var blocks = LocalBatteryProjection.ApplyLegacy (new[] { new BatteryBlock { PackageSerialNumber = "serial", Type = "ACPW" } }, bus)!;
		Assert.That (blocks[0].NominalEnergyRemaining, Is.Zero);
		Assert.That (blocks[0].NominalFullPackEnergy, Is.EqualTo (13500));
		Assert.That (blocks[0].PowerOut, Is.EqualTo (-1250));
		Assert.That (blocks[0].PinvGridState, Is.EqualTo ("Grid_Compliant"));
		Assert.That (JsonSerializer.Deserialize<LocalTelemetry> (JsonSerializer.Serialize (data))!.EnergyBus!.Devices!.Islander!.AcMeasurements!.LoadLine1NeutralVolts, Is.EqualTo (230));
		}

	/// <summary>Unaligned bus arrays and messages marked missing cannot be treated as current measurements.</summary>
	[Test]
	public void AmbiguousAndMissingBusMeasurements_AreNotAttached ()
		{
		var blocks = new[] { new BatteryBlock { PackageSerialNumber = "one" }, new BatteryBlock { PackageSerialNumber = "two" } };
		var bus = new LocalEnergyBusDevices
			{
			ThermalControllers = new[] { new LocalThermalControllerBus { SerialNumber = "one" }, new LocalThermalControllerBus { SerialNumber = "two" } },
			BatteryEnergy = new[] { new LocalBatteryEnergyBus { Energy = new LocalLegacyBatteryEnergy { RemainingWattHours = 4000 } } },
			BatteryInverters = new[]
				{
				new LocalBatteryInverterBus { Status = new LocalBatteryInverterStatus { IsMissing = true, PowerWatts = 1500 } },
				new LocalBatteryInverterBus { Status = new LocalBatteryInverterStatus { PowerWatts = 0 } }
				}
			};
		var result = LocalBatteryProjection.ApplyLegacy (blocks, bus)!;
		Assert.That (result.All (b => b.NominalEnergyRemaining is null), Is.True);
		Assert.That (result[0].PowerOut, Is.Null);
		Assert.That (result[1].PowerOut, Is.Zero);
		}
	}
