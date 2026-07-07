// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests that verify <see cref="CalendarHistoryParser"/> against representative payload shapes
/// captured from live Tesla™ calendar-history responses (as returned by <c>Powerwall.GetCalendarHistoryAsync</c>).
/// </summary>
[TestClass]
public sealed class CalendarHistoryParserTests
	{
	[TestMethod]
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

		Assert.AreEqual (1, points.Count);
		var point = points[0];
		Assert.AreEqual (new DateTimeOffset (2026, 7, 7, 19, 15, 0, TimeSpan.FromHours (1)), point.Timestamp);
		Assert.AreEqual (1.0, point.SolarKwh);
		Assert.AreEqual (99.0, point.HomeKwh);
		Assert.AreEqual (2.0, point.FromGridKwh);
		Assert.AreEqual (0.1, point.ToGridKwh);
		Assert.AreEqual (0.5, point.BatteryChargeKwh);
		Assert.AreEqual (7.0, point.BatteryDischargeKwh);
		}

	[TestMethod]
	public void WhenEnergyPayloadIsMissingOrMalformedThenParseEnergyReturnsEmpty ()
		{
		Assert.AreEqual (0, CalendarHistoryParser.ParseEnergy (null).Count);
		Assert.AreEqual (0, CalendarHistoryParser.ParseEnergy ("").Count);
		Assert.AreEqual (0, CalendarHistoryParser.ParseEnergy ("not json").Count);
		Assert.AreEqual (0, CalendarHistoryParser.ParseEnergy ("""{ "time_series": [] }""").Count);
		}

	[TestMethod]
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

		Assert.AreEqual (1, points.Count);
		var point = points[0];
		Assert.AreEqual (new DateTimeOffset (2026, 7, 7, 0, 0, 0, TimeSpan.FromHours (1)), point.Timestamp);
		Assert.AreEqual (0.0, point.SolarPower);
		Assert.AreEqual (0.0, point.BatteryPower);
		Assert.AreEqual (1168.5, point.GridPower);
		Assert.AreEqual (0.0, point.GridServicesPower);
		Assert.AreEqual (0.0, point.GeneratorPower);
		}

	[TestMethod]
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

		Assert.AreEqual (1, points.Count);
		Assert.AreEqual (new DateTimeOffset (2026, 7, 7, 0, 0, 0, TimeSpan.FromHours (1)), points[0].Timestamp);
		Assert.AreEqual (10.0, points[0].Soe);
		}

	[TestMethod]
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

		Assert.AreEqual (1, points.Count);
		Assert.AreEqual (new DateTimeOffset (2026, 7, 7, 0, 0, 0, TimeSpan.FromHours (1)), points[0].Timestamp);
		Assert.AreEqual (19.0, points[0].SolarPercentage);
		Assert.AreEqual (3.0, points[0].BatteryPercentage);
		}

	[TestMethod]
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

		Assert.AreEqual (0, backup.Events.Count);
		Assert.AreEqual (0, backup.EventsCount);
		Assert.AreEqual (0, backup.TotalEvents);
		Assert.AreEqual (DateTimeOffset.Parse ("2026-06-24T15:23:00Z"), backup.NextStartDate);
		Assert.AreEqual (DateTimeOffset.Parse ("2026-07-06T18:20:18Z"), backup.NextEndDate);
		}

	[TestMethod]
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

		Assert.AreEqual (1, backup.Events.Count);
		Assert.AreEqual (1, backup.EventsCount);
		Assert.AreEqual (1, backup.TotalEvents);
		Assert.IsNull (backup.NextStartDate);
		Assert.IsNull (backup.NextEndDate);
		Assert.AreEqual ("2026-06-01T00:00:00Z", backup.Events[0]["start_time"]?.ToString ());
		}

	[TestMethod]
	public void WhenBackupPayloadIsMissingOrMalformedThenParseBackupReturnsEmptyEnvelope ()
		{
		var backup = CalendarHistoryParser.ParseBackup (null);

		Assert.AreEqual (0, backup.Events.Count);
		Assert.AreEqual (0, backup.EventsCount);
		Assert.AreEqual (0, backup.TotalEvents);
		Assert.IsNull (backup.NextStartDate);
		Assert.IsNull (backup.NextEndDate);
		}
	}
