// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using Newtonsoft.Json;

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
	[JsonProperty ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	[JsonProperty ("solar_energy_exported")]
	private double SolarEnergyExported { get; init; }

	[JsonProperty ("grid_energy_imported")]
	private double GridEnergyImported { get; init; }

	[JsonProperty ("grid_energy_exported_from_solar")]
	private double GridEnergyExportedFromSolar { get; init; }

	[JsonProperty ("grid_energy_exported_from_battery")]
	private double GridEnergyExportedFromBattery { get; init; }

	[JsonProperty ("grid_energy_exported_from_generator")]
	private double GridEnergyExportedFromGenerator { get; init; }

	[JsonProperty ("battery_energy_exported")]
	private double BatteryEnergyExported { get; init; }

	[JsonProperty ("battery_energy_imported_from_grid")]
	private double BatteryEnergyImportedFromGrid { get; init; }

	[JsonProperty ("battery_energy_imported_from_solar")]
	private double BatteryEnergyImportedFromSolar { get; init; }

	[JsonProperty ("battery_energy_imported_from_generator")]
	private double BatteryEnergyImportedFromGenerator { get; init; }

	[JsonProperty ("consumer_energy_imported_from_grid")]
	private double ConsumerEnergyImportedFromGrid { get; init; }

	[JsonProperty ("consumer_energy_imported_from_solar")]
	private double ConsumerEnergyImportedFromSolar { get; init; }

	[JsonProperty ("consumer_energy_imported_from_battery")]
	private double ConsumerEnergyImportedFromBattery { get; init; }

	[JsonProperty ("consumer_energy_imported_from_generator")]
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
	[JsonProperty ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	/// <summary>Solar generation power.</summary>
	[JsonProperty ("solar_power")]
	public double SolarPower { get; init; }

	/// <summary>Powerwall™ battery power. Positive values indicate discharge.</summary>
	[JsonProperty ("battery_power")]
	public double BatteryPower { get; init; }

	/// <summary>Grid (site) power. Positive values indicate import from the grid.</summary>
	[JsonProperty ("grid_power")]
	public double GridPower { get; init; }

	/// <summary>Power committed to grid services (for example demand response).</summary>
	[JsonProperty ("grid_services_power")]
	public double GridServicesPower { get; init; }

	/// <summary>Backup generator power.</summary>
	[JsonProperty ("generator_power")]
	public double GeneratorPower { get; init; }
	}

/// <summary>
/// A single timestamped point of battery state-of-energy data (calendar-history <c>soe</c> kind).
/// </summary>
public sealed record StateOfEnergyHistoryPoint
	{
	/// <summary>The point's timestamp.</summary>
	[JsonProperty ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	/// <summary>Battery state of energy as a percentage (raw gateway scale).</summary>
	[JsonProperty ("soe")]
	public double Soe { get; init; }
	}

/// <summary>
/// A single timestamped point of self-consumption data (calendar-history <c>self_consumption</c> kind).
/// </summary>
public sealed record SelfConsumptionHistoryPoint
	{
	/// <summary>The point's timestamp.</summary>
	[JsonProperty ("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	/// <summary>Percentage of consumption self-supplied from solar.</summary>
	[JsonProperty ("solar")]
	public double SolarPercentage { get; init; }

	/// <summary>Percentage of consumption self-supplied from the Powerwall™ battery.</summary>
	[JsonProperty ("battery")]
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
	[JsonProperty ("events")]
	public IReadOnlyList<IReadOnlyDictionary<string, object?>> Events { get; init; } = Array.Empty<IReadOnlyDictionary<string, object?>> ();

	/// <summary>The number of events included in <see cref="Events"/>.</summary>
	[JsonProperty ("events_count")]
	public int EventsCount { get; init; }

	/// <summary>The total number of events available across all pages.</summary>
	[JsonProperty ("total_events")]
	public int TotalEvents { get; init; }

	/// <summary>The start of the next page's date range, when more events are available.</summary>
	[JsonProperty ("next_start_date")]
	public DateTimeOffset? NextStartDate { get; init; }

	/// <summary>The end of the next page's date range, when more events are available.</summary>
	[JsonProperty ("next_end_date")]
	public DateTimeOffset? NextEndDate { get; init; }
	}

#pragma warning restore CA1507


