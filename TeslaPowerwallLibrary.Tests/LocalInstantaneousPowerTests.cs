// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;

using TeslaPowerwallLibrary.Models;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Protects PW2 available-power totals, identities and missing-measurement semantics.</summary>
[TestFixture]
public sealed class LocalInstantaneousPowerTests
	{
	/// <summary>Each direction requires a complete set; zero remains a usable measurement.</summary>
	[TestCase (7000d, 8570d, 14000d, 15570d)]
	[TestCase (0d, 0d, 7000d, 7000d)]
	[TestCase (null, 8570d, null, 15570d)]
	[TestCase (7000d, null, 14000d, null)]
	[TestCase (-1d, 8570d, null, 15570d)]
	[TestCase (7000d, -1d, 14000d, null)]
	[TestCase (double.NaN, 8570d, null, 15570d)]
	[TestCase (double.PositiveInfinity, 8570d, null, 15570d)]
	[TestCase (7000d, double.NegativeInfinity, 14000d, null)]
	public void Totals_RequireCompleteFiniteNonnegativeMeasurements (double? charge, double? discharge, double? expectedCharge, double? expectedDischarge)
		{
		var status = Project (Pod ("module1", 7000, 7000), Pod ("module2", charge, discharge));
		Assert.That (status.InstantaneousMaxChargePower, Is.EqualTo (expectedCharge));
		Assert.That (status.InstantaneousMaxDischargePower, Is.EqualTo (expectedDischarge));
		Assert.That (() => JsonSerializer.Serialize (status), Throws.Nothing);
		}

	/// <summary>A serial with only one echoed direction still makes the other total incomplete.</summary>
	[TestCase ("POD_available_charge_power")]
	[TestCase ("POD_available_dischg_power")]
	public void Totals_RejectMissingSignalIndependently (string reported)
		{
		var status = Project (Pod ("module1", 7000, 8570), new LocalComponent
			{ SerialNumber = "module2", Signals = [new LocalSignal { Name = reported, Value = 1000 }] });
		Assert.That (status.InstantaneousMaxChargePower, Is.EqualTo (reported == "POD_available_charge_power" ? 8000d : (double?)null));
		Assert.That (status.InstantaneousMaxDischargePower, Is.EqualTo (reported == "POD_available_dischg_power" ? 9570d : (double?)null));
		}

	/// <summary>Repeated serials use the last usable value without counting one module twice.</summary>
	[Test]
	public void Totals_DeduplicateSerialsAndRetainLastUsableValues ()
		{
		var status = Project (Pod ("module", 7000, 8570), Pod ("module", 2000, 3000), Pod ("module", null, -1));
		Assert.That (status.InstantaneousMaxChargePower, Is.EqualTo (2000));
		Assert.That (status.InstantaneousMaxDischargePower, Is.EqualTo (3000));
		}

	/// <summary>Empty slots and unrelated components cannot fabricate available battery power.</summary>
	[Test]
	public void Totals_IgnoreSeriallessSlotsAndUnrelatedComponents ()
		{
		var status = Project (Pod ("module", 7000, 8570), Pod (null, 9000, 9000), Pod ("", null, null), Pod (" ", 1, 1),
			new LocalComponent { SerialNumber = "inverter", Signals = [new LocalSignal { Name = "PINV_Pnom", Value = 10000 }] });
		Assert.That (status.InstantaneousMaxChargePower, Is.EqualTo (7000));
		Assert.That (status.InstantaneousMaxDischargePower, Is.EqualTo (8570));
		}

	/// <summary>Text and boolean channels cannot stand in for a numeric measurement.</summary>
	[TestCase (true)]
	[TestCase (false)]
	public void Totals_RejectNonNumericSignalChannels (bool text)
		{
		var signal = new LocalSignal { Name = "POD_available_charge_power", Value = 1000, TextValue = text ? "1000" : null, BoolValue = text ? null : false };
		var status = Project (new LocalComponent { SerialNumber = "module", Signals = [signal, new LocalSignal { Name = "POD_available_dischg_power", Value = 0 }] });
		Assert.That (status.InstantaneousMaxChargePower, Is.Null);
		Assert.That (status.InstantaneousMaxDischargePower, Is.Zero);
		}

	/// <summary>Overflow cannot escape as an infinite JSON value or a partial total.</summary>
	[Test]
	public void Totals_RejectOverflowIndependently ()
		{
		var status = Project (Pod ("module1", double.MaxValue, 0), Pod ("module2", double.MaxValue, 0));
		Assert.That (status.InstantaneousMaxChargePower, Is.Null);
		Assert.That (status.InstantaneousMaxDischargePower, Is.Zero);
		Assert.That (() => JsonSerializer.Serialize (status), Throws.Nothing);
		}

	/// <summary>Missing data, including PW3 and June 2026 responses without POD signals, stays unavailable.</summary>
	[Test]
	public void Totals_DoNotSubstituteStaticRatingsForMissingMeasurements ()
		{
		foreach (var telemetry in new[] { new LocalTelemetry (), new LocalTelemetry
			{ Components = new Dictionary<string, IReadOnlyList<LocalComponent>> { ["msa"] = [] } } })
			{
			var status = LocalSystemStatusProjection.Create (new LocalTelemetry (), new LocalDeviceSnapshot
				{ Controller = telemetry, Configuration = new LocalConfiguration { Site = new LocalSiteConfiguration { NominalPowerKilowatts = 10 } } });
			Assert.That (status.MaxChargePower, Is.EqualTo (10000));
			Assert.That (status.InstantaneousMaxChargePower, Is.Null);
			Assert.That (status.InstantaneousMaxDischargePower, Is.Null);
			}
		}

	/// <summary>Wire fields preserve zero, fractional watts and absence through the public status model.</summary>
	[Test]
	public void Status_RoundTripsWireNamesAndUnavailableValues ()
		{
		var status = Project (Pod ("module", 0, 8570.5));
		using var json = JsonDocument.Parse (JsonSerializer.Serialize (status));
		Assert.That (json.RootElement.GetProperty ("instantaneous_max_charge_power").GetDouble (), Is.Zero);
		Assert.That (json.RootElement.GetProperty ("instantaneous_max_discharge_power").GetDouble (), Is.EqualTo (8570.5));
		var copy = JsonSerializer.Deserialize<SystemStatus> (json.RootElement.GetRawText ())!;
		Assert.That (copy.InstantaneousMaxChargePower, Is.Zero);
		Assert.That (copy.InstantaneousMaxDischargePower, Is.EqualTo (8570.5));
		var missing = JsonSerializer.Deserialize<SystemStatus> ("{}")!;
		Assert.That (missing.InstantaneousMaxChargePower, Is.Null);
		Assert.That (missing.InstantaneousMaxDischargePower, Is.Null);
		}

	private static LocalComponent Pod (string? serial, double? charge, double? discharge) => new ()
		{
		SerialNumber = serial,
		Signals = [new LocalSignal { Name = "POD_available_charge_power", Value = charge },
			new LocalSignal { Name = "POD_available_dischg_power", Value = discharge }]
		};

	private static SystemStatus Project (params LocalComponent[] components) => LocalSystemStatusProjection.Create (new LocalTelemetry (),
		new LocalDeviceSnapshot { Controller = new LocalTelemetry { Components = new Dictionary<string, IReadOnlyList<LocalComponent>> { ["msa"] = components } } });
	}
