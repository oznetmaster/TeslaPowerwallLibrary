// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;

using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// A single aggregated point of energy-history data, with all values expressed in kilowatt-hours.
/// </summary>
/// <param name="Label">The formatted time-bucket label for the X axis.</param>
/// <param name="SolarKwh">Solar energy produced.</param>
/// <param name="HomeKwh">Home (consumer) energy used.</param>
/// <param name="FromGridKwh">Energy imported from the grid.</param>
/// <param name="ToGridKwh">Energy exported to the grid.</param>
/// <param name="FromBatteryKwh">Energy discharged from the Powerwall battery.</param>
public sealed record EnergyHistoryPoint (
	string Label,
	double SolarKwh,
	double HomeKwh,
	double FromGridKwh,
	double ToGridKwh,
	double FromBatteryKwh);

/// <summary>
/// Parses the raw JSON returned by <see cref="Powerwall.GetCalendarHistoryAsync"/> for the <c>energy</c> kind
/// into a list of <see cref="EnergyHistoryPoint"/> values suitable for charting. Missing fields are treated as
/// zero so a partial payload never throws.
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

			var fromBattery =
				ReadWh (item, "battery_energy_exported");

			points.Add (new EnergyHistoryPoint (
				FormatLabel (item["timestamp"]?.Value<string> ()),
				ToKwh (solar),
				ToKwh (home),
				ToKwh (fromGrid),
				ToKwh (toGrid),
				ToKwh (fromBattery)));
			}

		return points;
		}

	private static double ReadWh (JObject item, string property) =>
		item[property]?.Type is JTokenType.Float or JTokenType.Integer
			? item[property]!.Value<double> ()
			: 0.0;

	private static double ToKwh (double watthours) =>
		Math.Round (watthours / 1000.0, 3);

	private static string FormatLabel (string? timestamp)
		{
		if (string.IsNullOrWhiteSpace (timestamp))
			return string.Empty;

		return DateTimeOffset.TryParse (timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
			? when.ToLocalTime ().ToString ("MMM d HH:mm", CultureInfo.CurrentCulture)
			: timestamp!;
		}
	}
