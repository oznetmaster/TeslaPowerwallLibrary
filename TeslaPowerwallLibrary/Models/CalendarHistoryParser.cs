// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.



using System.Text.Json;
using System.Text.Json.Serialization;


namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// Parses the raw JSON returned by <see cref="Powerwall.GetCalendarHistoryAsync"/> into strongly typed
/// records for the calendar-history kinds with a stable, verified schema (<c>energy</c>, <c>power</c>,
/// <c>soe</c>, <c>self_consumption</c>, and <c>backup</c>). Each payload is deserialized directly into its
/// corresponding record via System.Text.Json <c>[JsonPropertyName]</c> mappings; missing or malformed payloads never
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
		if (string.IsNullOrWhiteSpace (json))
			return new BackupHistory ();
		try
			{
			return JsonHelper.Deserialize<ApiResponse<BackupHistory>> (json!)?.Response
				 ?? JsonHelper.Deserialize<BackupHistory> (json!) ?? new BackupHistory ();
			}
		catch (JsonException) { return new BackupHistory (); }
		}

	private static List<T> ParseTimeSeries<T> (string? json)
		{
		var root = JsonHelper.DeserializeOrNull<HistoryEnvelope<T>> (json);
		return (root?.Response ?? root)?.TimeSeries ?? new List<T> ();
		}

	private sealed record HistoryEnvelope<T>
		{
		[JsonPropertyName ("response")]
		public HistoryEnvelope<T>? Response
			{
			get; init;
			}
		[JsonPropertyName ("time_series")]
		public List<T>? TimeSeries
			{
			get; init;
			}
		}
	}