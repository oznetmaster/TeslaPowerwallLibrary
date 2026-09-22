// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// A single raw, timestamped point of energy-history data (calendar-history <c>energy</c> kind), with all
/// computed totals expressed in kilowatt-hours. Deserialized directly from Tesla's raw watt-hour fields;
/// <see cref="SolarKwh"/>, <see cref="HomeKwh"/>, <see cref="FromGridKwh"/>, <see cref="ToGridKwh"/>,
/// <see cref="BatteryChargeKwh"/>, and <see cref="BatteryDischargeKwh"/> are computed properties that sum
/// and convert the underlying raw fields.
/// </summary>
public sealed record EnergyHistoryPoint
	{
	/// <summary>The point's timestamp, used to resample the data into period-appropriate buckets.</summary>
	[JsonPropertyName ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	[JsonPropertyName ("solar_energy_exported"), JsonInclude]
	private double SolarEnergyExported { get; init; }

	[JsonPropertyName ("grid_energy_imported"), JsonInclude]
	private double GridEnergyImported { get; init; }

	[JsonPropertyName ("grid_energy_exported_from_solar"), JsonInclude]
	private double GridEnergyExportedFromSolar { get; init; }

	[JsonPropertyName ("grid_energy_exported_from_battery"), JsonInclude]
	private double GridEnergyExportedFromBattery { get; init; }

	[JsonPropertyName ("grid_energy_exported_from_generator"), JsonInclude]
	private double GridEnergyExportedFromGenerator { get; init; }

	[JsonPropertyName ("battery_energy_exported"), JsonInclude]
	private double BatteryEnergyExported { get; init; }

	[JsonPropertyName ("battery_energy_imported_from_grid"), JsonInclude]
	private double BatteryEnergyImportedFromGrid { get; init; }

	[JsonPropertyName ("battery_energy_imported_from_solar"), JsonInclude]
	private double BatteryEnergyImportedFromSolar { get; init; }

	[JsonPropertyName ("battery_energy_imported_from_generator"), JsonInclude]
	private double BatteryEnergyImportedFromGenerator { get; init; }

	[JsonPropertyName ("consumer_energy_imported_from_grid"), JsonInclude]
	private double ConsumerEnergyImportedFromGrid { get; init; }

	[JsonPropertyName ("consumer_energy_imported_from_solar"), JsonInclude]
	private double ConsumerEnergyImportedFromSolar { get; init; }

	[JsonPropertyName ("consumer_energy_imported_from_battery"), JsonInclude]
	private double ConsumerEnergyImportedFromBattery { get; init; }

	[JsonPropertyName ("consumer_energy_imported_from_generator"), JsonInclude]
	private double ConsumerEnergyImportedFromGenerator { get; init; }

	/// <summary>Solar energy produced, in kilowatt-hours.</summary>
	public double SolarKwh => ToKwh (SolarEnergyExported);

	/// <summary>Home (consumer) energy used, in kilowatt-hours.</summary>
	public double HomeKwh => ToKwh (
		ConsumerEnergyImportedFromGrid
		+ ConsumerEnergyImportedFromSolar
		+ ConsumerEnergyImportedFromBattery
		+ ConsumerEnergyImportedFromGenerator);

	/// <summary>Energy imported from the grid, in kilowatt-hours.</summary>
	public double FromGridKwh => ToKwh (GridEnergyImported);

	/// <summary>Energy exported to the grid, in kilowatt-hours.</summary>
	public double ToGridKwh => ToKwh (
		GridEnergyExportedFromSolar
		+ GridEnergyExportedFromBattery
		+ GridEnergyExportedFromGenerator);

	/// <summary>Gross energy charged into the Powerwall™ battery (from solar, grid, or generator), in kilowatt-hours.</summary>
	public double BatteryChargeKwh => ToKwh (
		BatteryEnergyImportedFromGrid
		+ BatteryEnergyImportedFromSolar
		+ BatteryEnergyImportedFromGenerator);

	/// <summary>Gross energy discharged from the Powerwall battery, in kilowatt-hours.</summary>
	public double BatteryDischargeKwh => ToKwh (BatteryEnergyExported);

	private static double ToKwh (double watthours) =>
		Math.Round (watthours / 1000.0, 3);
	}

/// <summary>
/// A single timestamped point of instantaneous power-flow data (calendar-history <c>power</c> kind), with all
/// values expressed in watts.
/// </summary>
public sealed record PowerHistoryPoint
	{
	/// <summary>The point's timestamp.</summary>
	[JsonPropertyName ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	/// <summary>Solar generation power.</summary>
	[JsonPropertyName ("solar_power")]
	public double SolarPower { get; init; }

	/// <summary>Powerwall™ battery power. Positive values indicate discharge.</summary>
	[JsonPropertyName ("battery_power")]
	public double BatteryPower { get; init; }

	/// <summary>Grid (site) power. Positive values indicate import from the grid.</summary>
	[JsonPropertyName ("grid_power")]
	public double GridPower { get; init; }

	/// <summary>Power committed to grid services (for example demand response).</summary>
	[JsonPropertyName ("grid_services_power")]
	public double GridServicesPower { get; init; }

	/// <summary>Backup generator power.</summary>
	[JsonPropertyName ("generator_power")]
	public double GeneratorPower { get; init; }
	}

/// <summary>
/// A single timestamped point of battery state-of-energy data (calendar-history <c>soe</c> kind).
/// </summary>
public sealed record StateOfEnergyHistoryPoint
	{
	/// <summary>The point's timestamp.</summary>
	[JsonPropertyName ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	/// <summary>Battery state of energy as a percentage (raw gateway scale).</summary>
	[JsonPropertyName ("soe")]
	public double Soe { get; init; }
	}

/// <summary>
/// A single timestamped point of self-consumption data (calendar-history <c>self_consumption</c> kind).
/// </summary>
public sealed record SelfConsumptionHistoryPoint
	{
	/// <summary>The point's timestamp.</summary>
	[JsonPropertyName ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	/// <summary>Percentage of consumption self-supplied from solar.</summary>
	[JsonPropertyName ("solar")]
	public double SolarPercentage { get; init; }

	/// <summary>Percentage of consumption self-supplied from the Powerwall™ battery.</summary>
	[JsonPropertyName ("battery")]
	public double BatteryPercentage { get; init; }
	}

/// <summary>
/// Backup (outage) event history (calendar-history <c>backup</c> kind). Tesla returns this as a paged envelope;
/// <see cref="Events"/> entries are exposed as loosely typed maps (mirroring <see cref="Powerwall.VitalsAsync"/>)
/// because no backup event has yet been observed on a real site to confirm a per-event field schema.
/// </summary>
public sealed record BackupHistory
	{
	/// <summary>The backup events on the requested page, field names preserved as reported by Tesla.</summary>
	[JsonPropertyName ("events")]
	public IReadOnlyList<IReadOnlyDictionary<string, object?>> Events { get; init; } = Array.Empty<IReadOnlyDictionary<string, object?>> ();

	/// <summary>The number of events included in <see cref="Events"/>.</summary>
	[JsonPropertyName ("events_count")]
	public int EventsCount { get; init; }

	/// <summary>The total number of events available across all pages.</summary>
	[JsonPropertyName ("total_events")]
	public int TotalEvents { get; init; }

	/// <summary>The start of the next page's date range, when more events are available.</summary>
	[JsonPropertyName ("next_start_date")]
	public DateTimeOffset? NextStartDate { get; init; }

	/// <summary>The end of the next page's date range, when more events are available.</summary>
	[JsonPropertyName ("next_end_date")]
	public DateTimeOffset? NextEndDate { get; init; }
	}

#pragma warning restore CA1507