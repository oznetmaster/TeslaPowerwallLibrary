// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;

using TeslaPowerwallLibrary.Models;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks site ratings, derated inverter capabilities and nullable system status.</summary>
[TestFixture]
public sealed class LocalSystemStatusTests
	{
	/// <summary>Limits use the lesser usable rating, with explicit watt conversion and missing-source fallbacks.</summary>
	/// <param name="kilowatts">Configured site nameplate.</param>
	/// <param name="first">First inverter capability in watts.</param>
	/// <param name="second">Second inverter capability in watts.</param>
	/// <param name="expected">Expected maximum power.</param>
	/// <param name="usable">Expected summed inverter capability.</param>
	[TestCase (10, 4600, 4600, 9200, 9200)]
	[TestCase (10.8, 5800, 5800, 10800, 11600)]
	[TestCase (10, null, null, 10000, 0)]
	[TestCase (null, 4600, 4600, 9200, 9200)]
	[TestCase (null, null, null, null, 0)]
	[TestCase (0, 0, -1, null, 0)]
	[TestCase (-1, 4600, null, 4600, 4600)]
	[TestCase (1.2345, null, null, 1234, 0)]
	[TestCase (double.NaN, 100, null, 100, 100)]
	[TestCase (double.PositiveInfinity, null, null, null, 0)]
	[TestCase (1, double.NaN, double.PositiveInfinity, 1000, 0)]
	public void PowerLimits_RespectConfiguredAndInverterRatings (double? kilowatts, double? first, double? second, double? expected, double usable)
		{
		var snapshot = Snapshot (kilowatts, new LocalBatteryPowerCapability { NominalPowerWatts = first }, new LocalBatteryPowerCapability { NominalPowerWatts = second });
		var result = LocalSystemStatusProjection.Create (new LocalTelemetry (), snapshot);
		Assert.That (new[] { result.MaxChargePower, result.MaxDischargePower, result.MaxApparentPower }, Is.All.EqualTo (expected));
		Assert.That (result.InverterNominalUsablePower, Is.EqualTo (usable));
		}

	/// <summary>Capabilities marked missing or incomplete cannot derate the site; unreported flags are accepted.</summary>
	/// <param name="missing">Reported missing-device flag.</param>
	/// <param name="complete">Reported message-completeness flag.</param>
	/// <param name="usable">Expected eligible capability.</param>
	[TestCase (true, true, 0)]
	[TestCase (true, null, 0)]
	[TestCase (false, false, 0)]
	[TestCase (null, false, 0)]
	[TestCase (false, true, 4600)]
	[TestCase (null, null, 4600)]
	public void PowerLimits_ExcludeUnavailableCapabilities (bool? missing, bool? complete, double usable)
		{
		var result = LocalSystemStatusProjection.Create (new LocalTelemetry (), Snapshot (10,
			new LocalBatteryPowerCapability { IsMissing = missing, IsComplete = complete, NominalPowerWatts = 4600 }));
		Assert.That (result.InverterNominalUsablePower, Is.EqualTo (usable));
		Assert.That (result.MaxChargePower, Is.EqualTo (usable > 0 ? usable : 10000));
		}

	/// <summary>Overflow does not produce invalid JSON or a fabricated finite inverter total.</summary>
	[Test]
	public void PowerLimits_RejectOverflowedRatings ()
		{
		var result = LocalSystemStatusProjection.Create (new LocalTelemetry (), Snapshot (double.MaxValue,
			new LocalBatteryPowerCapability { NominalPowerWatts = double.MaxValue }, new LocalBatteryPowerCapability { NominalPowerWatts = double.MaxValue }));
		Assert.That (result.InverterNominalUsablePower, Is.Null);
		Assert.That (result.MaxChargePower, Is.Null);
		Assert.That (() => JsonSerializer.Serialize (result), Throws.Nothing);
		}

	/// <summary>New wire fields round-trip without changing existing energy, grid, count or battery values.</summary>
	[Test]
	public void SystemStatus_PreservesExistingMeasurementsAndWireNames ()
		{
		var snapshot = Snapshot (10, new LocalBatteryPowerCapability { NominalPowerWatts = 4600 });
		snapshot = snapshot with { Batteries = [new BatteryBlock { PackageSerialNumber = "battery", EnergyCharged = 0 }] };
		var telemetry = new LocalTelemetry
			{
			Control = new LocalControlTelemetry
				{
				SystemStatus = new LocalBatteryEnergy { FullCapacityWattHours = 13500, RemainingWattHours = 0 },
				BatteryBlocks = [new LocalBatteryDevice { Din = "battery" }],
				Islanding = new LocalIslandingStatus { ContactorClosed = false }
				}
			};
		var copy = JsonSerializer.Deserialize<SystemStatus> (JsonSerializer.Serialize (LocalSystemStatusProjection.Create (telemetry, snapshot)))!;
		Assert.That (copy.NominalFullPackEnergy, Is.EqualTo (13500));
		Assert.That (copy.NominalEnergyRemaining, Is.Zero);
		Assert.That (copy.AvailableBlocks, Is.EqualTo (1));
		Assert.That (copy.SystemIslandState, Is.EqualTo ("SystemIslandedActive"));
		Assert.That (copy.BatteryBlocks![0].EnergyCharged, Is.Zero);
		Assert.That (copy.InverterNominalUsablePower, Is.EqualTo (4600));
		Assert.That (JsonSerializer.Deserialize<SystemStatus> ("{\"inverter_nominal_usable_power\":0}")!.InverterNominalUsablePower, Is.Zero);
		Assert.That (JsonSerializer.Deserialize<SystemStatus> ("{}")!.InverterNominalUsablePower, Is.Null);
		}

	private static LocalDeviceSnapshot Snapshot (double? kilowatts, params LocalBatteryPowerCapability[] capabilities) => new ()
		{
		Configuration = new LocalConfiguration { Site = new LocalSiteConfiguration { NominalPowerKilowatts = kilowatts } },
		Controller = new LocalTelemetry
			{
			EnergyBus = new LocalEnergyBusTelemetry
				{
				Devices = new LocalEnergyBusDevices { BatteryInverters = capabilities.Select (value => new LocalBatteryInverterBus { PowerCapability = value }).ToArray () }
				}
			}
		};
	}
