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
/// <c>/api/meters/aggregates</c> and surfaced through the high-level power helpers.
/// </summary>
public sealed record PowerSnapshot
	{
	/// <summary>Grid (site) power in watts. Positive values indicate import from the grid.</summary>
	[JsonPropertyName ("site")]
	public double Site { get; init; }

	/// <summary>Solar generation power in watts.</summary>
	[JsonPropertyName ("solar")]
	public double Solar { get; init; }

	/// <summary>Battery power in watts. Positive values indicate discharge.</summary>
	[JsonPropertyName ("battery")]
	public double Battery { get; init; }

	/// <summary>Home (load) consumption power in watts.</summary>
	[JsonPropertyName ("load")]
	public double Load { get; init; }
	}

/// <summary>
/// Readings for a single meter within the <c>/api/meters/aggregates</c> response.
/// </summary>
public sealed record MeterReading
	{
	/// <summary>Timestamp of the most recent communication with the meter.</summary>
	[JsonPropertyName ("last_communication_time")]
	public string? LastCommunicationTime { get; init; }

	/// <summary>Instantaneous real power in watts.</summary>
	[JsonPropertyName ("instant_power")]
	public double? InstantPower { get; init; }

	/// <summary>Instantaneous reactive power in volt-amperes reactive.</summary>
	[JsonPropertyName ("instant_reactive_power")]
	public double? InstantReactivePower { get; init; }

	/// <summary>Instantaneous apparent power in volt-amperes.</summary>
	[JsonPropertyName ("instant_apparent_power")]
	public double? InstantApparentPower { get; init; }

	/// <summary>Line frequency in hertz.</summary>
	[JsonPropertyName ("frequency")]
	public double? Frequency { get; init; }

	/// <summary>Cumulative energy exported in watt-hours.</summary>
	[JsonPropertyName ("energy_exported")]
	public double? EnergyExported { get; init; }

	/// <summary>Cumulative energy imported in watt-hours.</summary>
	[JsonPropertyName ("energy_imported")]
	public double? EnergyImported { get; init; }

	/// <summary>Average voltage across measured phases in volts.</summary>
	[JsonPropertyName ("instant_average_voltage")]
	public double? InstantAverageVoltage { get; init; }

	/// <summary>Average current across measured phases in amperes.</summary>
	[JsonPropertyName ("instant_average_current")]
	public double? InstantAverageCurrent { get; init; }

	/// <summary>Total instantaneous current in amperes.</summary>
	[JsonPropertyName ("instant_total_current")]
	public double? InstantTotalCurrent { get; init; }

	/// <summary>Number of physical meters aggregated into this reading.</summary>
	[JsonPropertyName ("num_meters_aggregated")]
	public int? NumMetersAggregated { get; init; }
	}

/// <summary>
/// Aggregated meter readings returned by <c>/api/meters/aggregates</c>, one entry per energy flow.
/// </summary>
public sealed record MeterAggregates
	{
	/// <summary>Grid (site) meter readings.</summary>
	[JsonPropertyName ("site")]
	public MeterReading? Site { get; init; }

	/// <summary>Battery meter readings.</summary>
	[JsonPropertyName ("battery")]
	public MeterReading? Battery { get; init; }

	/// <summary>Home (load) meter readings.</summary>
	[JsonPropertyName ("load")]
	public MeterReading? Load { get; init; }

	/// <summary>Solar meter readings.</summary>
	[JsonPropertyName ("solar")]
	public MeterReading? Solar { get; init; }
	}

#pragma warning restore CA1507