// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// Instantaneous power readings (in watts) for the four primary energy flows, as returned by
/// <c>/api/meters/aggregates</c> and surfaced through the high-level power helpers. Missing readings default to zero for compatibility.
/// Use <see cref="PowerReadings"/> to distinguish missing readings from reported zeroes.
/// </summary>
public sealed record PowerSnapshot
	{
	/// <summary>Grid (site) power in watts. Positive values indicate import from the grid.</summary>
	/// <value>Grid (site) power in watts. Positive values indicate import from the grid.</value>
	[JsonPropertyName ("site")]
	public double Site { get; init; }

	/// <summary>Solar generation power in watts.</summary>
	/// <value>Solar generation power in watts.</value>
	[JsonPropertyName ("solar")]
	public double Solar { get; init; }

	/// <summary>Battery power in watts. Positive values indicate discharge.</summary>
	/// <value>Battery power in watts. Positive values indicate discharge.</value>
	[JsonPropertyName ("battery")]
	public double Battery { get; init; }

	/// <summary>Home (load) consumption power in watts.</summary>
	/// <value>Home (load) consumption power in watts.</value>
	[JsonPropertyName ("load")]
	public double Load { get; init; }
	}

/// <summary>
/// Instantaneous power readings (in watts) for the four primary energy flows, as returned by
/// <c>/api/meters/aggregates</c> and surfaced through the high-level power helpers. Missing readings remain null.
/// </summary>
public sealed record PowerReadings
	{
	/// <summary>Grid (site) power in watts. Positive values indicate import from the grid.</summary>
	/// <value>Grid (site) power in watts. Positive values indicate import from the grid.</value>
	[JsonPropertyName ("site")]
	public double? Site { get; init; }

	/// <summary>Solar generation power in watts.</summary>
	/// <value>Solar generation power in watts.</value>
	[JsonPropertyName ("solar")]
	public double? Solar { get; init; }

	/// <summary>Battery power in watts. Positive values indicate discharge.</summary>
	/// <value>Battery power in watts. Positive values indicate discharge.</value>
	[JsonPropertyName ("battery")]
	public double? Battery { get; init; }

	/// <summary>Home (load) consumption power in watts.</summary>
	/// <value>Home (load) consumption power in watts.</value>
	[JsonPropertyName ("load")]
	public double? Load { get; init; }
	}

/// <summary>
/// Readings for a single meter within the <c>/api/meters/aggregates</c> response.
/// </summary>
public sealed record MeterReading
	{
	/// <summary>Age in seconds, relative to the gateway clock, of a retained solar sample used to correct this power reading.
	/// Null means no SolarMeterComms hold was applied. Applies only to real power; other measurements retain their reported values.</summary>
	/// <value>
	/// Age in seconds, relative to the gateway clock, of a retained solar sample used to correct this power reading.
	/// Null means no SolarMeterComms hold was applied. Applies only to real power; other measurements retain their reported values.
	/// </value>
	[JsonPropertyName ("power_hold_age_seconds")]
	[JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? PowerHoldAgeSeconds { get; init; }

	/// <summary>Timestamp of the most recent communication with the meter.</summary>
	/// <value>Timestamp of the most recent communication with the meter.</value>
	[JsonPropertyName ("last_communication_time")]
	public string? LastCommunicationTime { get; init; }

	/// <summary>Instantaneous real power in watts.</summary>
	/// <value>Instantaneous real power in watts.</value>
	[JsonPropertyName ("instant_power")]
	public double? InstantPower { get; init; }

	/// <summary>Instantaneous reactive power in volt-amperes reactive.</summary>
	/// <value>Instantaneous reactive power in volt-amperes reactive.</value>
	[JsonPropertyName ("instant_reactive_power")]
	public double? InstantReactivePower { get; init; }

	/// <summary>Instantaneous apparent power in volt-amperes.</summary>
	/// <value>Instantaneous apparent power in volt-amperes.</value>
	[JsonPropertyName ("instant_apparent_power")]
	public double? InstantApparentPower { get; init; }

	/// <summary>Line frequency in hertz.</summary>
	/// <value>Line frequency in hertz.</value>
	[JsonPropertyName ("frequency")]
	public double? Frequency { get; init; }

	/// <summary>Cumulative energy exported in watt-hours.</summary>
	/// <value>Cumulative energy exported in watt-hours.</value>
	[JsonPropertyName ("energy_exported")]
	public double? EnergyExported { get; init; }

	/// <summary>Cumulative energy imported in watt-hours.</summary>
	/// <value>Cumulative energy imported in watt-hours.</value>
	[JsonPropertyName ("energy_imported")]
	public double? EnergyImported { get; init; }

	/// <summary>Average voltage across measured phases in volts.</summary>
	/// <value>Average voltage across measured phases in volts.</value>
	[JsonPropertyName ("instant_average_voltage")]
	public double? InstantAverageVoltage { get; init; }

	/// <summary>Average current across measured phases in amperes.</summary>
	/// <value>Average current across measured phases in amperes.</value>
	[JsonPropertyName ("instant_average_current")]
	public double? InstantAverageCurrent { get; init; }

	/// <summary>Total instantaneous current in amperes.</summary>
	/// <value>Total instantaneous current in amperes.</value>
	[JsonPropertyName ("instant_total_current")]
	public double? InstantTotalCurrent { get; init; }

	/// <summary>Reported current for original CT phase A, in amperes; null when unavailable.</summary>
	/// <value>Reported current for original CT phase A, in amperes; null when unavailable.</value>
	[JsonPropertyName ("i_a_current")]
	public double? PhaseACurrent { get; init; }

	/// <summary>Reported current for original CT phase B, in amperes; null when unavailable.</summary>
	/// <value>Reported current for original CT phase B, in amperes; null when unavailable.</value>
	[JsonPropertyName ("i_b_current")]
	public double? PhaseBCurrent { get; init; }

	/// <summary>Reported current for original CT phase C, in amperes; null when unavailable.</summary>
	/// <value>Reported current for original CT phase C, in amperes; null when unavailable.</value>
	[JsonPropertyName ("i_c_current")]
	public double? PhaseCCurrent { get; init; }

	/// <summary>Number of physical meters aggregated into this reading.</summary>
	/// <value>Number of physical meters aggregated into this reading.</value>
	[JsonPropertyName ("num_meters_aggregated")]
	public int? NumMetersAggregated { get; init; }
	}

/// <summary>
/// Aggregated meter readings returned by <c>/api/meters/aggregates</c>, one entry per energy flow.
/// </summary>
public sealed record MeterAggregates
	{
	/// <summary>Grid (site) meter readings.</summary>
	/// <value>Grid (site) meter readings.</value>
	[JsonPropertyName ("site")]
	public MeterReading? Site { get; init; }

	/// <summary>Battery meter readings.</summary>
	/// <value>Battery meter readings.</value>
	[JsonPropertyName ("battery")]
	public MeterReading? Battery { get; init; }

	/// <summary>Home (load) meter readings.</summary>
	/// <value>Home (load) meter readings.</value>
	[JsonPropertyName ("load")]
	public MeterReading? Load { get; init; }

	/// <summary>Solar meter readings.</summary>
	/// <value>Solar meter readings.</value>
	[JsonPropertyName ("solar")]
	public MeterReading? Solar { get; init; }
	}

#pragma warning restore CA1507