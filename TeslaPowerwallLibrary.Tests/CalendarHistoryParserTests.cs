// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests that verify <see cref="CalendarHistoryParser"/> against representative payload shapes
/// captured from live Tesla™ calendar-history responses (as returned by <c>Powerwall.GetCalendarHistoryAsync</c>).
/// </summary>
[TestFixture]
public sealed class CalendarHistoryParserTests
	{
	[Test]
	public void WhenEnergyPayloadIsParsedThenPointsAreMappedAndConvertedToKwh ()
		{
		const string json = """
			{
			  "serial_number": "6605b0bd-76e7-44e4-b1f8-17f8245c59da",
			  "installation_time_zone": "Europe/London",
			  "time_series": [
				 {
					"solar_energy_exported": 1000,
					"grid_energy_imported": 2000,
					"grid_energy_exported_from_solar": 100,
					"grid_energy_exported_from_generator": 0,
					"grid_energy_exported_from_battery": 0,
					"battery_energy_exported": 7000,
					"battery_energy_imported_from_grid": 0,
					"battery_energy_imported_from_solar": 500,
					"battery_energy_imported_from_generator": 0,
					"consumer_energy_imported_from_grid": 14000,
					"consumer_energy_imported_from_solar": 78000,
					"consumer_energy_imported_from_battery": 7000,
					"consumer_energy_imported_from_generator": 0,
					"timestamp": "2026-07-07T19:15:00+01:00"
				 }
			  ]
			}
			""";

		var points = CalendarHistoryParser.ParseEnergy (json);

		Assert.That (points.Count, Is.EqualTo (1));
		var point = points[0];
		Assert.That (point.Timestamp, Is.EqualTo (new DateTimeOffset (2026, 7, 7, 19, 15, 0, TimeSpan.FromHours (1))));
		Assert.That (point.SolarKwh, Is.EqualTo (1.0));
		Assert.That (point.HomeKwh, Is.EqualTo (99.0));
		Assert.That (point.FromGridKwh, Is.EqualTo (2.0));
		Assert.That (point.ToGridKwh, Is.EqualTo (0.1));
		Assert.That (point.BatteryChargeKwh, Is.EqualTo (0.5));
		Assert.That (point.BatteryDischargeKwh, Is.EqualTo (7.0));
		}

	[Test]
	public void WhenEnergyPayloadIsMissingOrMalformedThenParseEnergyReturnsEmpty ()
		{
		Assert.That (CalendarHistoryParser.ParseEnergy (null).Count, Is.EqualTo (0));
		Assert.That (CalendarHistoryParser.ParseEnergy ("").Count, Is.EqualTo (0));
		Assert.That (CalendarHistoryParser.ParseEnergy ("not json").Count, Is.EqualTo (0));
		Assert.That (CalendarHistoryParser.ParseEnergy ("""{ "time_series": [] }""").Count, Is.EqualTo (0));
		}

	[Test]
	public void WhenPowerPayloadIsParsedThenPointsAreMappedInWatts ()
		{
		const string json = """
			{
			  "serial_number": "6605b0bd-76e7-44e4-b1f8-17f8245c59da",
			  "installation_time_zone": "Europe/London",
			  "time_series": [
				 {
					"timestamp": "2026-07-07T00:00:00+01:00",
					"solar_power": 0,
					"battery_power": 0,
					"grid_power": 1168.5,
					"grid_services_power": 0,
					"generator_power": 0
				 }
			  ]
			}
			""";

		var points = CalendarHistoryParser.ParsePower (json);

		Assert.That (points.Count, Is.EqualTo (1));
		var point = points[0];
		Assert.That (point.Timestamp, Is.EqualTo (new DateTimeOffset (2026, 7, 7, 0, 0, 0, TimeSpan.FromHours (1))));
		Assert.That (point.SolarPower, Is.EqualTo (0.0));
		Assert.That (point.BatteryPower, Is.EqualTo (0.0));
		Assert.That (point.GridPower, Is.EqualTo (1168.5));
		Assert.That (point.GridServicesPower, Is.EqualTo (0.0));
		Assert.That (point.GeneratorPower, Is.EqualTo (0.0));
		}

	[Test]
	public void WhenStateOfEnergyPayloadIsParsedThenPointsAreMapped ()
		{
		const string json = """
			{
			  "serial_number": "1707000-30-L--TG12606400284T",
			  "installation_time_zone": "Europe/London",
			  "time_series": [
				 { "timestamp": "2026-07-07T00:00:00+01:00", "soe": 10 }
			  ]
			}
			""";

		var points = CalendarHistoryParser.ParseStateOfEnergy (json);

		Assert.That (points.Count, Is.EqualTo (1));
		Assert.That (points[0].Timestamp, Is.EqualTo (new DateTimeOffset (2026, 7, 7, 0, 0, 0, TimeSpan.FromHours (1))));
		Assert.That (points[0].Soe, Is.EqualTo (10.0));
		}

	[Test]
	public void WhenSelfConsumptionPayloadIsParsedThenPointsAreMapped ()
		{
		const string json = """
			{
			  "period": "day",
			  "timezone": "Europe/London",
			  "time_series": [
				 { "timestamp": "2026-07-07T00:00:00+01:00", "solar": 19, "battery": 3 }
			  ]
			}
			""";

		var points = CalendarHistoryParser.ParseSelfConsumption (json);

		Assert.That (points.Count, Is.EqualTo (1));
		Assert.That (points[0].Timestamp, Is.EqualTo (new DateTimeOffset (2026, 7, 7, 0, 0, 0, TimeSpan.FromHours (1))));
		Assert.That (points[0].SolarPercentage, Is.EqualTo (19.0));
		Assert.That (points[0].BatteryPercentage, Is.EqualTo (3.0));
		}

	[Test]
	public void WhenBackupPayloadHasNoEventsThenParseBackupReturnsEmptyEnvelope ()
		{
		const string json = """
			{
			  "events": [],
			  "events_count": 0,
			  "total_events": 0,
			  "next_start_date": "2026-06-24T15:23:00Z",
			  "next_end_date": "2026-07-06T18:20:18Z"
			}
			""";

		var backup = CalendarHistoryParser.ParseBackup (json);

		Assert.That (backup.Events.Count, Is.EqualTo (0));
		Assert.That (backup.EventsCount, Is.EqualTo (0));
		Assert.That (backup.TotalEvents, Is.EqualTo (0));
		Assert.That (backup.NextStartDate, Is.EqualTo (DateTimeOffset.Parse ("2026-06-24T15:23:00Z")));
		Assert.That (backup.NextEndDate, Is.EqualTo (DateTimeOffset.Parse ("2026-07-06T18:20:18Z")));
		}

	[Test]
	public void WhenBackupPayloadHasEventsThenEventsAreExposedAsLooselyTypedMaps ()
		{
		const string json = """
			{
			  "events": [
				 { "start_time": "2026-06-01T00:00:00Z", "duration_seconds": 120 }
			  ],
			  "events_count": 1,
			  "total_events": 1
			}
			""";

		var backup = CalendarHistoryParser.ParseBackup (json);

		Assert.That (backup.Events.Count, Is.EqualTo (1));
		Assert.That (backup.EventsCount, Is.EqualTo (1));
		Assert.That (backup.TotalEvents, Is.EqualTo (1));
		Assert.That (backup.NextStartDate, Is.Null);
		Assert.That (backup.NextEndDate, Is.Null);
		Assert.That (backup.Events[0]["start_time"]?.ToString (), Is.EqualTo ("2026-06-01T00:00:00Z"));
		}

	[Test]
	public void WhenBackupPayloadIsMissingOrMalformedThenParseBackupReturnsEmptyEnvelope ()
		{
		var backup = CalendarHistoryParser.ParseBackup (null);

		Assert.That (backup.Events.Count, Is.EqualTo (0));
		Assert.That (backup.EventsCount, Is.EqualTo (0));
		Assert.That (backup.TotalEvents, Is.EqualTo (0));
		Assert.That (backup.NextStartDate, Is.Null);
		Assert.That (backup.NextEndDate, Is.Null);
		}
	}