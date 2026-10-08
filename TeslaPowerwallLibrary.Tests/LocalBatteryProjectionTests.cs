// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks typed device diagnostics and the identity and units used for battery summaries.</summary>
[TestFixture]
public sealed class LocalBatteryProjectionTests
	{
	/// <summary>Measurements attach only to the queried unit and identified expansion; followers remain unmeasured.</summary>
	[Test]
	public void BatterySummary_PreservesIdentityMissingValuesAndPhysicalUnits ()
		{
		var config = JsonSerializer.Deserialize<LocalConfiguration> ("""
			{"battery_blocks":[
			 {"vin":"part--primary","type":"Powerwall3","battery_expansions":[{"din":"exp--matched"},{"din":"exp--unmatched"}]},
			 {"vin":"part--follower","type":"Powerwall3"}]}
			""")!;
		var components = JsonSerializer.Deserialize<LocalComponentTelemetry> ("""
			{"components":{
			 "bms":[{"signals":[{"name":"BMS_nominalEnergyRemaining","value":0},{"name":"BMS_nominalFullPackEnergy","value":13.5}]},
			        {"signals":[{"name":"BMS_nominalEnergyRemaining","value":6.25}]}],
			 "hvp":[{"serialNumber":"internal"},{"serialNumber":"matched"}],
			 "pch":[{"signals":[{"name":"PCH_BatteryPower","value":-1750},{"name":"PCH_AcMode","textValue":"Active"}]}]}}
			""")!;
		var blocks = LocalBatteryProjection.Create (config, "part--primary", components)!;
		Assert.That (blocks.Count, Is.EqualTo (4));
		Assert.That (blocks[0].PackageSerialNumber, Is.EqualTo ("primary"));
		Assert.That (blocks[0].NominalEnergyRemaining, Is.Zero);
		Assert.That (blocks[0].NominalFullPackEnergy, Is.EqualTo (13500));
		Assert.That (blocks[0].PowerOut, Is.EqualTo (-1750));
		Assert.That (blocks[0].FrequencyOut, Is.Null);
		Assert.That (blocks[1].NominalEnergyRemaining, Is.EqualTo (6250));
		Assert.That (blocks[1].PowerOut, Is.Null);
		Assert.That (blocks[1].NominalFullPackEnergy, Is.Null);
		Assert.That (blocks[2].PackageSerialNumber, Is.EqualTo ("unmatched"));
		Assert.That (blocks[2].NominalEnergyRemaining, Is.Null);
		Assert.That (blocks[3].PackageSerialNumber, Is.EqualTo ("follower"));
		Assert.That (blocks[3].NominalEnergyRemaining, Is.Null);
		Assert.That (blocks[3].PowerOut, Is.Null);
		}

	/// <summary>Absent configuration does not invent a battery count or synthetic identifiers.</summary>
	[Test]
	public void MissingConfiguration_DoesNotInventBatteries () =>
		Assert.That (LocalBatteryProjection.Create (new LocalConfiguration (), "part--primary", new LocalComponentTelemetry ()), Is.Null);

	/// <summary>Firmware progress and enumeration preserve false, zero and missing independently.</summary>
	[Test]
	public void DeviceDiagnostics_UseWireAttributesWithDefaultSerializer ()
		{
		const string json = """{"pw3Can":{"enumeration":{"inProgress":false},"firmwareUpdate":{"isUpdating":false,"progress":{"numSteps":0,"progress":0}}},"control":{"islanding":{"disableReasons":["example"]}}}""";
		var data = JsonSerializer.Deserialize<LocalTelemetry> (json)!;
		Assert.That (data.Powerwall3Bus!.Enumeration!.InProgress, Is.False);
		Assert.That (data.Powerwall3Bus.FirmwareUpdate!.IsUpdating, Is.False);
		Assert.That (data.Powerwall3Bus.FirmwareUpdate.Progress!.NumberOfSteps, Is.Zero);
		Assert.That (data.Powerwall3Bus.FirmwareUpdate.Progress.Progress, Is.Zero);
		Assert.That (data.Powerwall3Bus.FirmwareUpdate.Progress.CurrentStep, Is.Null);
		Assert.That (data.Control!.Islanding!.DisableReasons, Is.EqualTo (new[] { "example" }));
		var roundTrip = JsonSerializer.Deserialize<LocalTelemetry> (JsonSerializer.Serialize (data))!;
		Assert.That (roundTrip.Powerwall3Bus!.Enumeration!.InProgress, Is.False);
		}
	}
