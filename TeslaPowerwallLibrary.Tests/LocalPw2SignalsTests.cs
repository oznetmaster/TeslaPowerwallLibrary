// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;

using TeslaPowerwallLibrary.Models;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks PW2 module identity, nullable signals and conservative battery projection.</summary>
[TestFixture]
public sealed class LocalPw2SignalsTests
	{
	/// <summary>Each package gets only its own module counters, preserving zero, signed reactive power and existing readings.</summary>
	[Test]
	public void CompleteLayout_ProjectsOnlyThePairedModule ()
		{
		var blocks = Blocks ();
		blocks[1] = blocks[1] with { EnergyDischarged = 99, PowerOut = -500 };
		var result = LocalBatteryProjection.ApplyMsa (blocks, Controller (Components ()))!;
		Assert.That (result[0].EnergyCharged, Is.Zero);
		Assert.That (result[0].EnergyDischarged, Is.EqualTo (11));
		Assert.That (result[0].ReactivePowerOut, Is.EqualTo (-2));
		Assert.That (result[1].EnergyCharged, Is.EqualTo (22));
		Assert.That (result[1].EnergyDischarged, Is.EqualTo (99));
		Assert.That (result[1].PowerOut, Is.EqualTo (-500));
		Assert.That (blocks[0].EnergyCharged, Is.Null, "Projection must not mutate a cached model.");
		}

	/// <summary>An irregular complete layout must never give one package another module's counters.</summary>
	/// <param name="fault">Ambiguity introduced into the synthetic controller response.</param>
	[TestCase ("orphan-inverter"), TestCase ("missing-module"), TestCase ("repeated-module"), TestCase ("repeated-package")]
	[TestCase ("unknown-package"), TestCase ("interrupted-pair"), TestCase ("mixed-family"), TestCase ("empty-family")]
	[TestCase ("duplicate-bus-identity"), TestCase ("no-bus-identities"), TestCase ("trailing-package")]
	public void AmbiguousLayout_DoesNotAssignModuleMeasurements (string fault)
		{
		var components = Components ().ToList ();
		var controller = Controller (components);
		switch (fault)
			{
			case "orphan-inverter": components.RemoveAt (0); break;
			case "missing-module": components.RemoveAt (4); break;
			case "repeated-module": components[4] = components[4] with { SerialNumber = "module-a" }; break;
			case "repeated-package": components[3] = components[3] with { SerialNumber = "package-a" }; break;
			case "unknown-package": components[0] = components[0] with { SerialNumber = "unconfigured" }; break;
			case "interrupted-pair": components.Insert (1, Component ("other", "SYNC_SiteSwitchEnabled")); break;
			case "mixed-family": components[1] = components[1] with { Signals = [new LocalSignal { Name = "PINV_EnergyCharged", Value = 5 }, new LocalSignal { Name = "THC_State" }] }; break;
			case "empty-family": components[1] = components[1] with { Signals = null }; break;
			case "duplicate-bus-identity": controller = Controller (components, "package-a", "package-a"); break;
			case "no-bus-identities": controller = controller with { EnergyBus = null }; break;
			case "trailing-package": components.Add (Component ("package-a", "THC_State")); break;
			default: throw new AssertionException ("Unknown test scenario.");
			}
		var result = LocalBatteryProjection.ApplyMsa (Blocks (), controller)!;
		Assert.That (result.SelectMany (block => new[] { block.EnergyCharged, block.EnergyDischarged, block.ReactivePowerOut }), Is.All.Null);
		}

	/// <summary>Module signals preserve their native typed values and serial-less slots cannot create battery associations.</summary>
	[Test]
	public void Signals_PreserveFalseZeroTextAndNullWithoutChangingVitalsKeys ()
		{
		var controller = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"components":{"msa":[{"serialNumber":"module","signals":[
			{"name":"PINV_PllLocked","boolValue":false,"value":null,"textValue":null},
			{"name":"PINV_EnergyCharged","value":0},
			{"name":"PINV_LifetimeEnergyCharged","value":null},
			{"name":"PINV_PowerLimiter","textValue":"LIMITED"}]}]}}
			""")!;
		var copy = JsonSerializer.Deserialize<LocalTelemetry> (JsonSerializer.Serialize (controller))!;
		var values = LocalVitalsProjection.Create (new LocalDeviceSnapshot { Controller = copy }, "gateway")["gateway/msa/0/module"];
		Assert.That (values["PINV_PllLocked"], Is.False);
		Assert.That (values["PINV_EnergyCharged"], Is.EqualTo (0));
		Assert.That (values["PINV_LifetimeEnergyCharged"], Is.Null);
		Assert.That (values["PINV_PowerLimiter"], Is.EqualTo ("LIMITED"));
		var components = Components ().ToList ();
		components.Insert (1, null!);
		components.Insert (2, Component (null, "PINV_EnergyCharged", 999));
		var result = LocalBatteryProjection.ApplyMsa (Blocks (), Controller (components))!;
		Assert.That (result[0].EnergyCharged, Is.Zero);
		Assert.That (result[1].EnergyCharged, Is.EqualTo (22));
		}

	/// <summary>Duplicate named signals and unavailable families remain absent without inventing measurements.</summary>
	[Test]
	public void MissingAndDuplicateSignals_DoNotFabricateCounters ()
		{
		var components = Components ().ToList ();
		components[1] = components[1] with { Signals = [new LocalSignal { Name = "PINV_EnergyCharged", Value = 1 }, new LocalSignal { Name = "PINV_EnergyCharged", Value = 2 }] };
		var result = LocalBatteryProjection.ApplyMsa (Blocks (), Controller (components))!;
		Assert.That (result[0].EnergyCharged, Is.Null);
		Assert.That (result[0].EnergyDischarged, Is.Null);
		Assert.That (result[1].EnergyCharged, Is.EqualTo (22));
		Assert.That (LocalBatteryProjection.ApplyMsa (null, Controller (components)), Is.Null);
		Assert.That (LocalBatteryProjection.ApplyMsa (Blocks (), new LocalTelemetry ())![0].EnergyCharged, Is.Null);
		var missing = JsonSerializer.Deserialize<LocalTelemetry> ("{\"components\":{\"msa\":null}}")!;
		Assert.That (LocalBatteryProjection.ApplyMsa (Blocks (), missing)![0].EnergyCharged, Is.Null);
		}

	/// <summary>Expansion packs and PW3 identities cannot acquire PW2 module counters.</summary>
	[TestCase ("BatteryExpansion"), TestCase ("Powerwall3")]
	public void OtherBatteryTypes_DoNotAcquirePw2Counters (string type)
		{
		var blocks = Blocks ();
		blocks[0] = blocks[0] with { Type = type };
		Assert.That (LocalBatteryProjection.ApplyMsa (blocks, Controller (Components ()))![0].EnergyCharged, Is.Null);
		}

	private static BatteryBlock[] Blocks () => [new () { PackageSerialNumber = "package-a", Type = "Powerwall2" }, new () { PackageSerialNumber = "package-b", Type = "Powerwall2" }];
	private static LocalComponent Component (string? serial, string signal, double? value = null) => new () { SerialNumber = serial, Signals = [new LocalSignal { Name = signal, Value = value }] };
	private static LocalComponent[] Components () =>
		[
		Component ("package-a", "THC_State"),
		new () { SerialNumber = "module-a", Signals = [new () { Name = "PINV_EnergyCharged", Value = 0 }, new () { Name = "PINV_EnergyDischarged", Value = 11 }, new () { Name = "PINV_Qout", Value = -2 }] },
		Component (null, "PINV_EnergyCharged", 999),
		Component ("package-b", "THC_State"),
		Component ("module-b", "PINV_EnergyCharged", 22)
		];
	private static LocalTelemetry Controller (IReadOnlyList<LocalComponent> components, params string[] serials) => new ()
		{
		Components = new Dictionary<string, IReadOnlyList<LocalComponent>> { ["msa"] = components },
		EnergyBus = new LocalEnergyBusTelemetry { Devices = new LocalEnergyBusDevices { ThermalControllers = (serials.Length == 0 ? new[] { "package-a", "package-b" } : serials).Select (serial => new LocalThermalControllerBus { SerialNumber = serial }).ToArray () } }
		};
	}
