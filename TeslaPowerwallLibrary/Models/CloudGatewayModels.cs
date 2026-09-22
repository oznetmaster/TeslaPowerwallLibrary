using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Models;

internal sealed record MappedMeterReading
	{
	[JsonPropertyName ("last_communication_time")]
	public string? LastCommunicationTime
		{
		get; init;
		}
	[JsonPropertyName ("instant_power")]
	public double? InstantPower
		{
		get; init;
		}
	[JsonPropertyName ("instant_reactive_power")]
	public double? InstantReactivePower
		{
		get; init;
		}
	[JsonPropertyName ("instant_apparent_power")]
	public double? InstantApparentPower
		{
		get; init;
		}
	[JsonPropertyName ("frequency")]
	public double? Frequency
		{
		get; init;
		}
	[JsonPropertyName ("energy_exported")]
	public double? EnergyExported
		{
		get; init;
		}
	[JsonPropertyName ("energy_imported")]
	public double? EnergyImported
		{
		get; init;
		}
	[JsonPropertyName ("instant_average_voltage")]
	public double? InstantAverageVoltage
		{
		get; init;
		}
	[JsonPropertyName ("instant_average_current")]
	public double? InstantAverageCurrent
		{
		get; init;
		}
	[JsonPropertyName ("i_a_current")]
	public double? IACurrent
		{
		get; init;
		}
	[JsonPropertyName ("i_b_current")]
	public double? IBCurrent
		{
		get; init;
		}
	[JsonPropertyName ("i_c_current")]
	public double? ICCurrent
		{
		get; init;
		}
	[JsonPropertyName ("last_phase_voltage_communication_time")]
	public string? LastPhaseVoltageCommunicationTime
		{
		get; init;
		}
	[JsonPropertyName ("last_phase_power_communication_time")]
	public string? LastPhasePowerCommunicationTime
		{
		get; init;
		}
	[JsonPropertyName ("last_phase_energy_communication_time")]
	public string? LastPhaseEnergyCommunicationTime
		{
		get; init;
		}
	[JsonPropertyName ("timeout")]
	public double? Timeout
		{
		get; init;
		}
	[JsonPropertyName ("num_meters_aggregated")]
	public int? NumMetersAggregated
		{
		get; init;
		}
	[JsonPropertyName ("instant_total_current")]
	public object? InstantTotalCurrent
		{
		get; init;
		}
	}

internal sealed record MappedMeterAggregates
	{
	[JsonPropertyName ("site")]
	public MappedMeterReading? Site
		{
		get; init;
		}
	[JsonPropertyName ("battery")]
	public MappedMeterReading? Battery
		{
		get; init;
		}
	[JsonPropertyName ("load")]
	public MappedMeterReading? Load
		{
		get; init;
		}
	[JsonPropertyName ("solar")]
	public MappedMeterReading? Solar
		{
		get; init;
		}
	}

internal sealed record MappedSystemStatus
	{
	[JsonPropertyName ("command_source")]
	public string? CommandSource { get; init; } = "Configuration";
	[JsonPropertyName ("battery_target_power")]
	public double? BatteryTargetPower { get; init; } = 0;
	[JsonPropertyName ("battery_target_reactive_power")]
	public double? BatteryTargetReactivePower { get; init; } = 0;
	[JsonPropertyName ("nominal_full_pack_energy")]
	public double? NominalFullPackEnergy
		{
		get; init;
		}
	[JsonPropertyName ("nominal_energy_remaining")]
	public double? NominalEnergyRemaining
		{
		get; init;
		}
	[JsonPropertyName ("max_power_energy_remaining")]
	public double? MaxPowerEnergyRemaining { get; init; } = 0;
	[JsonPropertyName ("max_power_energy_to_be_charged")]
	public double? MaxPowerEnergyToBeCharged { get; init; } = 0;
	[JsonPropertyName ("max_charge_power")]
	public double? MaxChargePower
		{
		get; init;
		}
	[JsonPropertyName ("max_discharge_power")]
	public double? MaxDischargePower
		{
		get; init;
		}
	[JsonPropertyName ("max_apparent_power")]
	public double? MaxApparentPower
		{
		get; init;
		}
	[JsonPropertyName ("instantaneous_max_discharge_power")]
	public double? InstantaneousMaxDischargePower { get; init; } = 0;
	[JsonPropertyName ("instantaneous_max_charge_power")]
	public double? InstantaneousMaxChargePower { get; init; } = 0;
	[JsonPropertyName ("instantaneous_max_apparent_power")]
	public double? InstantaneousMaxApparentPower { get; init; } = 0;
	[JsonPropertyName ("hardware_capability_charge_power")]
	public double? HardwareCapabilityChargePower { get; init; } = 0;
	[JsonPropertyName ("hardware_capability_discharge_power")]
	public double? HardwareCapabilityDischargePower { get; init; } = 0;
	[JsonPropertyName ("grid_services_power")]
	public double? GridServicesPower
		{
		get; init;
		}
	[JsonPropertyName ("system_island_state")]
	public string? SystemIslandState
		{
		get; init;
		}
	[JsonPropertyName ("available_blocks")]
	public int? AvailableBlocks
		{
		get; init;
		}
	[JsonPropertyName ("available_charger_blocks")]
	public double? AvailableChargerBlocks { get; init; } = 0;
	[JsonPropertyName ("battery_blocks")]
	public IReadOnlyList<object?>? BatteryBlocks { get; init; } = Array.Empty<object> ();
	[JsonPropertyName ("ffr_power_availability_high")]
	public double? FfrPowerAvailabilityHigh { get; init; } = 0;
	[JsonPropertyName ("ffr_power_availability_low")]
	public double? FfrPowerAvailabilityLow { get; init; } = 0;
	[JsonPropertyName ("load_charge_constraint")]
	public double? LoadChargeConstraint { get; init; } = 0;
	[JsonPropertyName ("max_sustained_ramp_rate")]
	public double? MaxSustainedRampRate { get; init; } = 0;
	[JsonPropertyName ("grid_faults")]
	public IReadOnlyList<object?>? GridFaults { get; init; } = Array.Empty<object> ();
	[JsonPropertyName ("can_reboot")]
	public string? CanReboot { get; init; } = "Yes";
	[JsonPropertyName ("smart_inv_delta_p")]
	public double? SmartInvDeltaP { get; init; } = 0;
	[JsonPropertyName ("smart_inv_delta_q")]
	public double? SmartInvDeltaQ { get; init; } = 0;
	[JsonPropertyName ("last_toggle_timestamp")]
	public string? LastToggleTimestamp { get; init; } = "2023-10-13T04:08:05.957195-07:00";
	[JsonPropertyName ("solar_real_power_limit")]
	public double? SolarRealPowerLimit
		{
		get; init;
		}
	[JsonPropertyName ("score")]
	public double? Score { get; init; } = 10000;
	[JsonPropertyName ("blocks_controlled")]
	public int? BlocksControlled
		{
		get; init;
		}
	[JsonPropertyName ("primary")]
	public bool Primary { get; init; } = true;
	[JsonPropertyName ("auxiliary_load")]
	public double? AuxiliaryLoad { get; init; } = 0;
	[JsonPropertyName ("all_enable_lines_high")]
	public bool AllEnableLinesHigh { get; init; } = true;
	[JsonPropertyName ("inverter_nominal_usable_power")]
	public double? InverterNominalUsablePower { get; init; } = 0;
	[JsonPropertyName ("expected_energy_remaining")]
	public double? ExpectedEnergyRemaining { get; init; } = 0;
	}

internal sealed record MappedGatewayStatus
	{
	[JsonPropertyName ("din")]
	public string? Din
		{
		get; init;
		}
	[JsonPropertyName ("start_time")]
	public string? StartTime
		{
		get; init;
		}
	[JsonPropertyName ("up_time_seconds")]
	public double? UpTimeSeconds
		{
		get; init;
		}
	[JsonPropertyName ("is_new")]
	public bool IsNew { get; init; } = false;
	[JsonPropertyName ("version")]
	public string? Version
		{
		get; init;
		}
	[JsonPropertyName ("git_hash")]
	public string? GitHash { get; init; } = "27626f98a66cad5c665bbe1d4d788cdb3e94fd34";
	[JsonPropertyName ("commission_count")]
	public int CommissionCount { get; init; } = 0;
	[JsonPropertyName ("device_type")]
	public object? DeviceType
		{
		get; init;
		}
	[JsonPropertyName ("teg_type")]
	public string? TegType { get; init; } = "unknown";
	[JsonPropertyName ("sync_type")]
	public string? SyncType { get; init; } = "v2.1";
	[JsonPropertyName ("cellular_disabled")]
	public bool CellularDisabled { get; init; } = false;
	[JsonPropertyName ("can_reboot")]
	public bool CanReboot { get; init; } = true;
	}

internal sealed record MappedGridCode
	{
	[JsonPropertyName ("grid_code")]
	public string? GridCode
		{
		get; init;
		}
	[JsonPropertyName ("grid_voltage_setting")]
	public string? GridVoltageSetting
		{
		get; init;
		}
	[JsonPropertyName ("grid_freq_setting")]
	public string? GridFreqSetting
		{
		get; init;
		}
	[JsonPropertyName ("grid_phase_setting")]
	public string? GridPhaseSetting
		{
		get; init;
		}
	[JsonPropertyName ("country")]
	public string? Country
		{
		get; init;
		}
	[JsonPropertyName ("state")]
	public string? State
		{
		get; init;
		}
	[JsonPropertyName ("utility")]
	public string? Utility
		{
		get; init;
		}
	}

internal sealed record MappedSiteInfo
	{
	[JsonPropertyName ("max_system_energy_kWh")]
	public double? MaxSystemEnergyKWh
		{
		get; init;
		}
	[JsonPropertyName ("max_system_power_kW")]
	public double? MaxSystemPowerKW
		{
		get; init;
		}
	[JsonPropertyName ("site_name")]
	public string? SiteName
		{
		get; init;
		}
	[JsonPropertyName ("timezone")]
	public string? Timezone
		{
		get; init;
		}
	[JsonPropertyName ("max_site_meter_power_kW")]
	public double? MaxSiteMeterPowerKW
		{
		get; init;
		}
	[JsonPropertyName ("min_site_meter_power_kW")]
	public double? MinSiteMeterPowerKW
		{
		get; init;
		}
	[JsonPropertyName ("nominal_system_energy_kWh")]
	public double? NominalSystemEnergyKWh
		{
		get; init;
		}
	[JsonPropertyName ("nominal_system_power_kW")]
	public double? NominalSystemPowerKW
		{
		get; init;
		}
	[JsonPropertyName ("panel_max_current")]
	public double? PanelMaxCurrent
		{
		get; init;
		}
	[JsonPropertyName ("grid_code")]
	public MappedGridCode? GridCode
		{
		get; init;
		}
	}

internal sealed record UnknownApiResponse
	{
	[JsonPropertyName ("ERROR")]
	public string? ERROR
		{
		get; init;
		}
	}