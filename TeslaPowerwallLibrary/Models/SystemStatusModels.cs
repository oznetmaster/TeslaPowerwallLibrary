// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary.Models;

/// <summary>
/// System status payload returned by <c>/api/system_status</c>.
/// </summary>
public sealed record SystemStatus
	{
	/// <summary>Nominal full pack energy in watt-hours.</summary>
	[JsonProperty ("nominal_full_pack_energy")]
	public double? NominalFullPackEnergy { get; init; }

	/// <summary>Nominal energy remaining in watt-hours.</summary>
	[JsonProperty ("nominal_energy_remaining")]
	public double? NominalEnergyRemaining { get; init; }

	/// <summary>Maximum charge power in watts.</summary>
	[JsonProperty ("max_charge_power")]
	public double? MaxChargePower { get; init; }

	/// <summary>Maximum discharge power in watts.</summary>
	[JsonProperty ("max_discharge_power")]
	public double? MaxDischargePower { get; init; }

	/// <summary>Maximum apparent power in volt-amperes.</summary>
	[JsonProperty ("max_apparent_power")]
	public double? MaxApparentPower { get; init; }

	/// <summary>Island state of the system; mirrors the grid status value.</summary>
	[JsonProperty ("system_island_state")]
	public string? SystemIslandState { get; init; }

	/// <summary>Number of available battery blocks.</summary>
	[JsonProperty ("available_blocks")]
	public int? AvailableBlocks { get; init; }

	/// <summary>Per-battery detail blocks.</summary>
	[JsonProperty ("battery_blocks")]
	public IReadOnlyList<BatteryBlock>? BatteryBlocks { get; init; }

	/// <summary>Grid fault entries reported by the system.</summary>
	[JsonProperty ("grid_faults")]
	public IReadOnlyList<object>? GridFaults { get; init; }
	}

/// <summary>
/// Detailed status for a single battery block within <see cref="SystemStatus.BatteryBlocks"/>.
/// </summary>
public sealed record BatteryBlock
	{
	/// <summary>Battery block type (for example <c>ACPW</c>).</summary>
	[JsonProperty ("Type")]
	public string? Type { get; init; }

	/// <summary>Manufacturer package part number.</summary>
	[JsonProperty ("PackagePartNumber")]
	public string? PackagePartNumber { get; init; }

	/// <summary>Manufacturer package serial number; used as the battery identifier.</summary>
	[JsonProperty ("PackageSerialNumber")]
	public string? PackageSerialNumber { get; init; }

	/// <summary>Nominal energy remaining for this block in watt-hours.</summary>
	[JsonProperty ("nominal_energy_remaining")]
	public double? NominalEnergyRemaining { get; init; }

	/// <summary>Nominal full pack energy for this block in watt-hours.</summary>
	[JsonProperty ("nominal_full_pack_energy")]
	public double? NominalFullPackEnergy { get; init; }

	/// <summary>Inverter state (for example <c>PINV_Active</c>).</summary>
	[JsonProperty ("pinv_state")]
	public string? PinvState { get; init; }

	/// <summary>Inverter grid compliance state.</summary>
	[JsonProperty ("pinv_grid_state")]
	public string? PinvGridState { get; init; }

	/// <summary>Real power output in watts.</summary>
	[JsonProperty ("p_out")]
	public double? PowerOut { get; init; }

	/// <summary>Reactive power output in volt-amperes reactive.</summary>
	[JsonProperty ("q_out")]
	public double? ReactivePowerOut { get; init; }

	/// <summary>Output voltage in volts.</summary>
	[JsonProperty ("v_out")]
	public double? VoltageOut { get; init; }

	/// <summary>Output frequency in hertz.</summary>
	[JsonProperty ("f_out")]
	public double? FrequencyOut { get; init; }

	/// <summary>Output current in amperes.</summary>
	[JsonProperty ("i_out")]
	public double? CurrentOut { get; init; }

	/// <summary>Cumulative energy charged in watt-hours.</summary>
	[JsonProperty ("energy_charged")]
	public double? EnergyCharged { get; init; }

	/// <summary>Cumulative energy discharged in watt-hours.</summary>
	[JsonProperty ("energy_discharged")]
	public double? EnergyDischarged { get; init; }

	/// <summary>Indicates whether the block is currently off grid.</summary>
	[JsonProperty ("off_grid")]
	public bool? OffGrid { get; init; }

	/// <summary>Indicates whether the block is ready to provide backup power.</summary>
	[JsonProperty ("backup_ready")]
	public bool? BackupReady { get; init; }

	/// <summary>Firmware version reported by the block.</summary>
	[JsonProperty ("version")]
	public string? Version { get; init; }
	}

/// <summary>
/// State-of-energy payload returned by <c>/api/system_status/soe</c>.
/// </summary>
public sealed record StateOfEnergy
	{
	/// <summary>Battery charge level as a percentage (raw gateway scale).</summary>
	[JsonProperty ("percentage")]
	public double Percentage { get; init; }
	}

/// <summary>
/// Grid status payload returned by <c>/api/system_status/grid_status</c>.
/// </summary>
public sealed record GridStatusResponse
	{
	/// <summary>Raw grid status string (for example <c>SystemGridConnected</c>).</summary>
	[JsonProperty ("grid_status")]
	public string? GridStatus { get; init; }

	/// <summary>Indicates whether grid services are currently active.</summary>
	[JsonProperty ("grid_services_active")]
	public bool? GridServicesActive { get; init; }
	}

/// <summary>
/// Battery operation payload returned by <c>/api/operation</c>.
/// </summary>
public sealed record OperationResponse
	{
	/// <summary>Configured backup reserve percentage (raw gateway scale).</summary>
	[JsonProperty ("backup_reserve_percent")]
	public double? BackupReservePercent { get; init; }

	/// <summary>Active battery operation mode (for example <c>self_consumption</c>).</summary>
	[JsonProperty ("real_mode")]
	public string? RealMode { get; init; }
	}

#pragma warning restore CA1507
