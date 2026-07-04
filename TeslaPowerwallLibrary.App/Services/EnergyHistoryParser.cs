// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;

using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// A single raw, timestamped point of energy-history data, with all values expressed in kilowatt-hours.
/// </summary>
/// <param name="Timestamp">The point's timestamp, used to resample the data into period-appropriate buckets.</param>
/// <param name="SolarKwh">Solar energy produced.</param>
/// <param name="HomeKwh">Home (consumer) energy used.</param>
/// <param name="FromGridKwh">Energy imported from the grid.</param>
/// <param name="ToGridKwh">Energy exported to the grid.</param>
/// <param name="BatteryChargeKwh">Gross energy charged into the Powerwall battery (from solar, grid, or generator).</param>
/// <param name="BatteryDischargeKwh">Gross energy discharged from the Powerwall battery.</param>
public sealed record EnergyHistoryPoint (
	DateTimeOffset Timestamp,
	double SolarKwh,
	double HomeKwh,
	double FromGridKwh,
	double ToGridKwh,
	double BatteryChargeKwh,
	double BatteryDischargeKwh);

/// <summary>
/// Parses the raw JSON returned by <see cref="Powerwall.GetCalendarHistoryAsync"/> for the <c>energy</c> kind
/// into a list of raw, timestamped <see cref="EnergyHistoryPoint"/> values. Missing fields are treated as zero
/// so a partial payload never throws. Callers resample these raw points into period-appropriate chart buckets.
/// </summary>
public static class EnergyHistoryParser
	{
	/// <summary>Parses the calendar-history <c>energy</c> payload into aggregated, chart-ready points.</summary>
	/// <param name="json">The raw JSON body from the calendar-history call.</param>
	/// <returns>The parsed points; empty when the payload is missing, malformed, or contains no series.</returns>
	public static IReadOnlyList<EnergyHistoryPoint> ParseEnergy (string? json)
		{
		if (string.IsNullOrWhiteSpace (json))
			return Array.Empty<EnergyHistoryPoint> ();

		JObject root;
		try
			{
			root = JObject.Parse (json!);
			}
		catch (Newtonsoft.Json.JsonException)
			{
			return Array.Empty<EnergyHistoryPoint> ();
			}

		var series = (root["response"]?["time_series"] ?? root["time_series"]) as JArray;
		if (series is null || series.Count == 0)
			return Array.Empty<EnergyHistoryPoint> ();

		var points = new List<EnergyHistoryPoint> (series.Count);
		foreach (var entry in series)
			{
			if (entry is not JObject item)
				continue;

			var timestampText = item["timestamp"]?.Value<string> ();
			if (!DateTimeOffset.TryParse (timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
				continue;

			var solar = ReadWh (item, "solar_energy_exported");

			var home =
				ReadWh (item, "consumer_energy_imported_from_grid")
				+ ReadWh (item, "consumer_energy_imported_from_solar")
				+ ReadWh (item, "consumer_energy_imported_from_battery")
				+ ReadWh (item, "consumer_energy_imported_from_generator");

			var fromGrid =
				ReadWh (item, "grid_energy_imported");

			var toGrid =
				ReadWh (item, "grid_energy_exported_from_solar")
				+ ReadWh (item, "grid_energy_exported_from_battery")
				+ ReadWh (item, "grid_energy_exported_from_generator");

			var batteryDischarge =
				ReadWh (item, "battery_energy_exported");

			var batteryCharge =
				ReadWh (item, "battery_energy_imported_from_grid")
				+ ReadWh (item, "battery_energy_imported_from_solar")
				+ ReadWh (item, "battery_energy_imported_from_generator");

			points.Add (new EnergyHistoryPoint (
				timestamp,
				ToKwh (solar),
				ToKwh (home),
				ToKwh (fromGrid),
				ToKwh (toGrid),
				ToKwh (batteryCharge),
				ToKwh (batteryDischarge)));
			}

		return points;
		}

	private static double ReadWh (JObject item, string property) =>
		item[property]?.Type is JTokenType.Float or JTokenType.Integer
			? item[property]!.Value<double> ()
			: 0.0;

	private static double ToKwh (double watthours) =>
		Math.Round (watthours / 1000.0, 3);
	}
