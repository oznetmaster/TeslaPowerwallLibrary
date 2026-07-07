// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// Parses the raw JSON returned by <see cref="Powerwall.GetCalendarHistoryAsync"/> into strongly typed
/// records for the calendar-history kinds with a stable, verified schema (<c>energy</c>, <c>power</c>,
/// <c>soe</c>, <c>self_consumption</c>, and <c>backup</c>). Each payload is deserialized directly into its
/// corresponding record via Json.NET's <c>[JsonProperty]</c> mappings; missing or malformed payloads never
/// throw, they simply yield an empty result. <c>time_of_use_energy</c> and <c>savings</c> have no parser
/// yet because Tesla returns an empty payload for both unless a time-of-use tariff is configured.
/// </summary>
public static class CalendarHistoryParser
	{
	/// <summary>Parses the calendar-history <c>energy</c> payload into raw, timestamped points (kilowatt-hours).</summary>
	/// <param name="json">The raw JSON body returned for the <c>energy</c> kind.</param>
	/// <returns>The parsed points; empty when the payload is missing, malformed, or contains no series.</returns>
	public static IReadOnlyList<EnergyHistoryPoint> ParseEnergy (string? json) =>
		ParseTimeSeries<EnergyHistoryPoint> (json);

	/// <summary>Parses the calendar-history <c>power</c> payload into raw, timestamped points (watts).</summary>
	/// <param name="json">The raw JSON body returned for the <c>power</c> kind.</param>
	/// <returns>The parsed points; empty when the payload is missing, malformed, or contains no series.</returns>
	public static IReadOnlyList<PowerHistoryPoint> ParsePower (string? json) =>
		ParseTimeSeries<PowerHistoryPoint> (json);

	/// <summary>Parses the calendar-history <c>soe</c> payload into raw, timestamped points.</summary>
	/// <param name="json">The raw JSON body returned for the <c>soe</c> kind.</param>
	/// <returns>The parsed points; empty when the payload is missing, malformed, or contains no series.</returns>
	public static IReadOnlyList<StateOfEnergyHistoryPoint> ParseStateOfEnergy (string? json) =>
		ParseTimeSeries<StateOfEnergyHistoryPoint> (json);

	/// <summary>Parses the calendar-history <c>self_consumption</c> payload into raw, timestamped points.</summary>
	/// <param name="json">The raw JSON body returned for the <c>self_consumption</c> kind.</param>
	/// <returns>The parsed points; empty when the payload is missing, malformed, or contains no series.</returns>
	public static IReadOnlyList<SelfConsumptionHistoryPoint> ParseSelfConsumption (string? json) =>
		ParseTimeSeries<SelfConsumptionHistoryPoint> (json);

	/// <summary>Parses the calendar-history <c>backup</c> payload into a typed envelope.</summary>
	/// <param name="json">The raw JSON body returned for the <c>backup</c> kind.</param>
	/// <returns>The parsed envelope; an empty envelope when the payload is missing or malformed.</returns>
	public static BackupHistory ParseBackup (string? json)
		{
		JObject? body = GetBody (json);
		if (body is null)
			return new BackupHistory ();

		try
			{
			return body.ToObject<BackupHistory> () ?? new BackupHistory ();
			}
		catch (JsonException)
			{
			return new BackupHistory ();
			}
		}

	/// <summary>Parses a calendar-history payload's <c>time_series</c> array directly into <typeparamref name="T"/> instances.</summary>
	private static List<T> ParseTimeSeries<T> (string? json)
		{
		JObject? body = GetBody (json);
		if (body?["time_series"] is not JArray series || series.Count == 0)
			return [];

		try
			{
			return series.ToObject<List<T>> () ?? [];
			}
		catch (JsonException)
			{
			return [];
			}
		}

	/// <summary>Parses the payload root and unwraps the optional Fleet-API-style <c>response</c> envelope.</summary>
	private static JObject? GetBody (string? json)
		{
		if (string.IsNullOrWhiteSpace (json))
			return null;

		try
			{
			// DateParseHandling.None keeps timestamp-like strings (e.g. "...+01:00" or "...Z") as raw JSON
			// strings instead of Newtonsoft eagerly converting them to JTokenType.Date - which discards the
			// original UTC offset. The records below declare their timestamp members as DateTimeOffset, so
			// deserializing straight from the preserved string parses the real, offset-preserving value.
			using var reader = new JsonTextReader (new StringReader (json!)) { DateParseHandling = DateParseHandling.None };
			JObject root = JObject.Load (reader);
			return (root["response"] as JObject) ?? root;
			}
		catch (JsonException)
			{
			return null;
			}
		}
	}
