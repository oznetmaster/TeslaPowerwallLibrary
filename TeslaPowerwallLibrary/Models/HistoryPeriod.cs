// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// The aggregation period accepted by the strongly typed calendar-history convenience methods on
/// <see cref="Powerwall"/> (for example <see cref="Powerwall.GetEnergyCalendarHistoryAsync"/>). The raw,
/// string-based <see cref="Powerwall.GetHistoryAsync"/> and <see cref="Powerwall.GetCalendarHistoryAsync"/>
/// methods are unaffected and continue to accept a <see cref="string"/> period (see
/// <see cref="Powerwall.HistoryPeriods"/>).
/// </summary>
public enum HistoryPeriod
	{
	/// <summary>A single day.</summary>
	Day,

	/// <summary>A calendar week.</summary>
	Week,

	/// <summary>A calendar month.</summary>
	Month,

	/// <summary>A calendar year.</summary>
	Year,

	/// <summary>The full lifetime of the site.</summary>
	Lifetime
	}

/// <summary>
/// Conversion helpers bridging <see cref="HistoryPeriod"/> to the lowercase wire-format strings used by
/// Tesla's calendar-history API.
/// </summary>
internal static class HistoryPeriodExtensions
	{
	/// <summary>Converts <paramref name="period"/> to the lowercase string Tesla's calendar-history API expects.</summary>
	/// <param name="period">The period to convert.</param>
	/// <returns>The lowercase wire-format value (for example <c>"day"</c>).</returns>
	public static string ToApiString (this HistoryPeriod period) =>
		period switch
			{
			HistoryPeriod.Day => "day",
			HistoryPeriod.Week => "week",
			HistoryPeriod.Month => "month",
			HistoryPeriod.Year => "year",
			HistoryPeriod.Lifetime => "lifetime",
			_ => throw new ArgumentOutOfRangeException (nameof (period), period, "Unknown history period.")
			};
	}
