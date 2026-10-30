// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text.Json;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Offline regressions for bounded use of gateway-retained remote solar measurements.</summary>
[TestFixture]
public sealed class LocalSolarMeterHoldTests
	{
	internal static LocalConfiguration Configuration (string type = "trm_wifi") => JsonSerializer.Deserialize<LocalConfiguration> (
		"""{"meters":[{"type":"TYPE","location":"solar","cts":[true,false],"real_power_scale_factor":2,"connection":{"device_serial":"solar-meter"}}]}""".Replace ("TYPE", type))!;

	internal static LocalTelemetry Telemetry (double? solar = 0, double age = 4, string? alert = "SolarMeterComms") => new ()
		{
		Control = new LocalControlTelemetry
			{
			Alerts = new LocalActiveAlerts { Active = alert is null ? Array.Empty<string> () : new[] { alert } },
			MeterAggregates = new[] { new LocalPowerReading { Location = "SOLAR", Watts = solar },
				new LocalPowerReading { Location = "LOAD", Watts = -500 + (solar ?? 0) },
				new LocalPowerReading { Location = "SITE", Watts = -500 }, new LocalPowerReading { Location = "BATTERY", Watts = 0 } }
			},
		System = new LocalSystemTelemetry { Time = "2026-10-30T12:00:00Z" },
		RemoteMeters = new LocalRemoteMeterTelemetry { Meters = new[] { new LocalRemoteMeter
			{ Din = "solar-meter", Reading = new LocalRemoteMeterReading
				{ Timestamp = new DateTimeOffset (2026, 10, 30, 12, 0, 0, TimeSpan.Zero).AddSeconds (-age).ToString ("O", CultureInfo.InvariantCulture),
				Channels = new[] { new LocalMeterChannel { RealPowerWatts = 1500, VoltageVolts = 240, CurrentAmps = 7, EnergyExportedWattSeconds = 3600 },
					new LocalMeterChannel { RealPowerWatts = 99999 } } } } } }
		};

	/// <summary>Both meter families apply scale once, balance load, expose age and retain raw measurements.</summary>
	[TestCase ("trm_mb", 0d), TestCase ("trm_wifi", 0d), TestCase ("trm_wifi", 1d), TestCase ("trm_mb", 10d), TestCase ("trm_wifi", null)]
	public void FreshSample_CorrectsSolarAndLoad (string type, double? solar)
		{
		var telemetry = Telemetry (solar);
		var result = LocalMeterProjection.Aggregate (Configuration (type), telemetry);
		Assert.That (result.Solar!.InstantPower, Is.EqualTo (3000));
		Assert.That (result.Load!.InstantPower, Is.EqualTo (2500));
		Assert.That (result.Site!.InstantPower, Is.EqualTo (-500));
		Assert.That (result.Battery!.InstantPower, Is.Zero);
		Assert.That (result.Solar.PowerHoldAgeSeconds, Is.EqualTo (4));
		Assert.That (result.Load.PowerHoldAgeSeconds, Is.EqualTo (4));
		Assert.That (result.Solar.InstantTotalCurrent, Is.EqualTo (7), "Measured RMS current is not power divided by voltage.");
		Assert.That (result.Solar.EnergyExported, Is.EqualTo (1));
		Assert.That (telemetry.Control!.MeterAggregates![0].Watts, Is.EqualTo (solar));
		Assert.That (LocalMeterProjection.Create (Configuration (type), telemetry)[0].Channels[0].RealPowerWatts, Is.EqualTo (3000));
		Assert.That (JsonSerializer.Deserialize<TeslaPowerwallLibrary.Models.MeterAggregates> (JsonSerializer.Serialize (result))!.Solar!.PowerHoldAgeSeconds, Is.EqualTo (4));
		Assert.That (LocalMeterProjection.Aggregate (new LocalDeviceSnapshot { Configuration = Configuration (type), Controller = telemetry }).Load!.InstantPower, Is.EqualTo (2500));
		}

	/// <summary>The gateway clock bounds each retained sample in both directions, inclusively.</summary>
	[TestCase (60d, true), TestCase (-60d, true), TestCase (60.001d, false), TestCase (-60.001d, false)]
	public void SampleAge_EnforcesBoundary (double age, bool held)
		{
		var result = LocalMeterProjection.Aggregate (Configuration (), Telemetry (age: age));
		Assert.That (result.Solar!.InstantPower, Is.EqualTo (held ? 3000 : 0));
		Assert.That (result.Load!.InstantPower, Is.EqualTo (held ? 2500 : -500));
		Assert.That (result.Solar.PowerHoldAgeSeconds, Is.EqualTo (held ? Math.Max (0, age) : (double?)null));
		}

	/// <summary>True zero, healthy output and unrelated alerts retain controller authority.</summary>
	[TestCase (0d, null), TestCase (0d, "OtherAlert"), TestCase (0d, "solarmetercomms"), TestCase (11d, "SolarMeterComms"), TestCase (-1d, "SolarMeterComms"), TestCase (3000d, "SolarMeterComms")]
	public void NonCandidates_AreUnchanged (double solar, string? alert)
		{
		var result = LocalMeterProjection.Aggregate (Configuration (), Telemetry (solar, alert: alert));
		Assert.That (result.Solar!.InstantPower, Is.EqualTo (solar));
		Assert.That (result.Load!.InstantPower, Is.EqualTo (-500 + solar));
		Assert.That (result.Solar.PowerHoldAgeSeconds, Is.Null);
		Assert.That (JsonSerializer.Serialize (result), Does.Not.Contain ("power_hold_age_seconds"));
		}

	/// <summary>Invalid or timezone-less times cannot turn retained data into a fresh sample.</summary>
	[TestCase (null), TestCase ("invalid"), TestCase ("2026-10-30T11:59:56")]
	public void InvalidTimes_RejectHold (string? time)
		{
		var telemetry = Telemetry ();
		var meter = telemetry.RemoteMeters!.Meters![0];
		var badMeter = telemetry with { RemoteMeters = new LocalRemoteMeterTelemetry { Meters = new[] { meter with { Reading = meter.Reading! with { Timestamp = time } } } } };
		Assert.That (LocalMeterProjection.Aggregate (Configuration (), badMeter).Solar!.PowerHoldAgeSeconds, Is.Null);
		Assert.That (LocalMeterProjection.Aggregate (Configuration (), telemetry with { System = new LocalSystemTelemetry { Time = time } }).Solar!.PowerHoldAgeSeconds, Is.Null);
		}

	/// <summary>Missing controller solar cannot use an expired CT fallback during the alert.</summary>
	[Test]
	public void MissingSolar_ExpiredSampleRemainsUnknown ()
		{
		var result = LocalMeterProjection.Aggregate (Configuration (), Telemetry (solar: null, age: 61));
		Assert.That (result.Solar!.InstantPower, Is.Null);
		Assert.That (result.Load!.InstantPower, Is.EqualTo (-500));
		}

	/// <summary>Every configured solar CT and meter must have a complete finite reading.</summary>
	[TestCase ("missing-ct"), TestCase ("missing-meter"), TestCase ("duplicate-meter"), TestCase ("missing-power"), TestCase ("nan"), TestCase ("infinity"), TestCase ("zero"), TestCase ("negative"), TestCase ("wrong-location"), TestCase ("mixed-family")]
	public void IncompleteOrInvalidReadings_RejectHold (string failure)
		{
		var config = Configuration ();
		var telemetry = Telemetry ();
		var assignment = config.Meters![0];
		var meter = telemetry.RemoteMeters!.Meters![0];
		if (failure == "missing-ct") config = config with { Meters = new[] { assignment with { EnabledChannels = new[] { true, false, true } } } };
		if (failure == "missing-meter") config = config with { Meters = new[] { assignment, assignment with { Connection = new LocalMeterConnection { DeviceSerial = "absent" } } } };
		if (failure == "mixed-family") config = config with { Meters = new[] { assignment, assignment with { Type = "neurio_w2_tcp", Connection = new LocalMeterConnection { DeviceSerial = "other" } } } };
		if (failure == "duplicate-meter") telemetry = telemetry with { RemoteMeters = new LocalRemoteMeterTelemetry { Meters = new[] { meter, meter } } };
		if (failure == "wrong-location") config = config with { Meters = new[] { assignment with { Location = "site" } } };
		if (failure is "missing-power" or "nan" or "infinity" or "zero" or "negative")
			{
			double? watts = failure switch { "nan" => double.NaN, "infinity" => double.PositiveInfinity, "zero" => 0, "negative" => -1, _ => null };
			telemetry = telemetry with { RemoteMeters = new LocalRemoteMeterTelemetry { Meters = new[] { meter with
				{ Reading = meter.Reading! with { Channels = new[] { new LocalMeterChannel { RealPowerWatts = watts } } } } } } };
			}
		var result = LocalMeterProjection.Aggregate (config, telemetry);
		Assert.That (result.Solar!.InstantPower, Is.Zero);
		Assert.That (result.Load!.InstantPower, Is.EqualTo (-500));
		Assert.That (result.Solar.PowerHoldAgeSeconds, Is.Null);
		}

	/// <summary>No absent load is fabricated, and multiple meters use the oldest sample without duplicate CTs.</summary>
	[Test]
	public void MultipleMeters_UseCompleteTotalAndOldestAge ()
		{
		var config = Configuration ();
		var assignment = config.Meters![0];
		config = config with { Meters = new[] { assignment, assignment, assignment with { Type = "trm_mb", Connection = new LocalMeterConnection { DeviceSerial = "second" } } } };
		var telemetry = Telemetry ();
		var meter = telemetry.RemoteMeters!.Meters![0];
		telemetry = telemetry with { RemoteMeters = new LocalRemoteMeterTelemetry { Meters = new[] { meter, meter with
			{ Din = "second", Reading = meter.Reading! with { Timestamp = "2026-10-30T12:59:50+01:00" } } } },
			Control = telemetry.Control! with { MeterAggregates = telemetry.Control!.MeterAggregates!.Where (p => p.Location != "LOAD").ToArray () } };
		var result = LocalMeterProjection.Aggregate (config, telemetry);
		Assert.That (result.Solar!.InstantPower, Is.EqualTo (6000));
		Assert.That (result.Solar.PowerHoldAgeSeconds, Is.EqualTo (10));
		Assert.That (result.Solar.LastCommunicationTime, Is.EqualTo ("2026-10-30T12:59:50+01:00"));
		Assert.That (result.Load, Is.Null);
		}

	/// <summary>A stale member prevents the use of a fresh but incomplete multi-meter subtotal.</summary>
	[Test]
	public void MultipleMeters_RejectStaleMember ()
		{
		var config = Configuration ();
		config = config with { Meters = new[] { config.Meters![0], config.Meters[0] with { Connection = new LocalMeterConnection { DeviceSerial = "second" } } } };
		var telemetry = Telemetry ();
		var meter = telemetry.RemoteMeters!.Meters![0];
		telemetry = telemetry with { RemoteMeters = new LocalRemoteMeterTelemetry { Meters = new[] { meter, meter with
			{ Din = "second", Reading = meter.Reading! with { Timestamp = "2026-10-30T11:58:59Z" } } } } };
		var result = LocalMeterProjection.Aggregate (config, telemetry);
		Assert.That (result.Solar!.InstantPower, Is.Zero);
		Assert.That (result.Load!.InstantPower, Is.EqualTo (-500));
		Assert.That (result.Solar.PowerHoldAgeSeconds, Is.Null);
		}

	/// <summary>Mixed solar sources cannot bypass validation; Neurio-only projection keeps its existing behavior.</summary>
	[Test]
	public void OtherMeterFamilies_DoNotBecomeRemoteSolarHolds ()
		{
		var config = Configuration ();
		var neurio = config.Meters![0] with { Type = "neurio_w2_tcp", Connection = new LocalMeterConnection { DeviceSerial = "neurio" } };
		var telemetry = Telemetry (solar: null) with { Neurio = new LocalNeurioTelemetry { Readings = new[] { new LocalNeurioReading
			{ Serial = "neurio", Channels = new[] { new LocalMeterChannel { RealPowerWatts = 1500 } } } } } };
		var mixed = LocalMeterProjection.Aggregate (config with { Meters = new[] { config.Meters[0], neurio } }, telemetry);
		Assert.That (mixed.Solar!.InstantPower, Is.Null, "An incomplete remote-meter subtotal cannot stand in for mixed solar sources.");
		Assert.That (mixed.Load!.InstantPower, Is.EqualTo (-500));
		var neurioOnly = LocalMeterProjection.Aggregate (config with { Meters = new[] { neurio } }, telemetry);
		Assert.That (neurioOnly.Solar!.InstantPower, Is.EqualTo (3000));
		Assert.That (neurioOnly.Solar.PowerHoldAgeSeconds, Is.Null);
		}
	}