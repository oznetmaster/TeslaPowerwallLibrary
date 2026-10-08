// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Legacy energy-storage bus telemetry reported by the detailed controller query.</summary>
public sealed record LocalEnergyBusTelemetry
	{
	/// <summary>Read-only phase-detection state and results, when supplied.</summary>
	[JsonPropertyName ("phaseDetection")]
	public LocalPhaseDetection? PhaseDetection { get; init; }

	/// <summary>Read-only inverter self-test state and results, when supplied.</summary>
	[JsonPropertyName ("inverterSelfTests")]
	public LocalInverterSelfTests? InverterSelfTests { get; init; }

	/// <summary>Reported legacy bus firmware-update state and Powerwall slots.</summary>
	[JsonPropertyName ("firmwareUpdate")]
	public LocalLegacyFirmwareUpdate? FirmwareUpdate { get; init; }

	/// <summary>Device families on the legacy bus; missing families remain null.</summary>
	[JsonPropertyName ("bus")]
	public LocalEnergyBusDevices? Devices { get; init; }

	/// <summary>Reported bus enumeration state.</summary>
	[JsonPropertyName ("enumeration")]
	public LocalEnergyBusEnumeration? Enumeration { get; init; }

	}

/// <summary>Legacy bus enumeration counts and state.</summary>
public sealed record LocalEnergyBusEnumeration
	{
	/// <summary>Whether enumeration is running.</summary>
	[JsonPropertyName ("inProgress")]
	public bool? InProgress { get; init; }

	/// <summary>Reported AC Powerwall count.</summary>
	[JsonPropertyName ("numACPW")]
	public int? PowerwallCount { get; init; }

	/// <summary>Reported photovoltaic inverter count.</summary>
	[JsonPropertyName ("numPVI")]
	public int? SolarInverterCount { get; init; }

	}

/// <summary>Legacy energy bus device families in their reported order.</summary>
public sealed record LocalEnergyBusDevices
	{
	/// <summary>Photovoltaic AC inverter devices.</summary>
	[JsonPropertyName ("PVAC")]
	public IReadOnlyList<LocalSolarInverterBus>? SolarInverters { get; init; }

	/// <summary>Photovoltaic string-controller devices.</summary>
	[JsonPropertyName ("PVS")]
	public IReadOnlyList<LocalSolarStringsBus>? SolarStrings { get; init; }

	/// <summary>Powerwall thermal-controller identities used to correlate legacy battery slots.</summary>
	[JsonPropertyName ("THC")]
	public IReadOnlyList<LocalThermalControllerBus>? ThermalControllers { get; init; }

	/// <summary>Battery inverter data in bus order; entries do not carry independent identities.</summary>
	[JsonPropertyName ("PINV")]
	public IReadOnlyList<LocalBatteryInverterBus>? BatteryInverters { get; init; }

	/// <summary>Battery energy data in bus order; entries do not carry independent identities.</summary>
	[JsonPropertyName ("POD")]
	public IReadOnlyList<LocalBatteryEnergyBus>? BatteryEnergy { get; init; }

	/// <summary>Site-controller meter measurements.</summary>
	[JsonPropertyName ("SYNC")]
	public LocalSyncBus? Sync { get; init; }

	/// <summary>Meter Z and solar assembly measurements when supplied by the query.</summary>
	[JsonPropertyName ("MSA")]
	public LocalMeterAssemblyBus? MeterAssembly { get; init; }

	/// <summary>Grid connection and phase measurements.</summary>
	[JsonPropertyName ("ISLANDER")]
	public LocalIslanderBus? Islander { get; init; }

	}

/// <summary>Availability flags reported with a legacy bus message.</summary>
public record LocalBusMessage
	{
	/// <summary>Last receive timestamp exactly as reported, without assuming a time zone or epoch.</summary>
	[JsonPropertyName ("lastRxTime")]
	public LocalDiagnosticScalar? LastReceiveTime { get; init; }


	/// <summary>Whether this bus message is marked missing; retained values may be stale.</summary>
	[JsonPropertyName ("isMIA")]
	public bool? IsMissing { get; init; }

	/// <summary>Whether this bus message is complete.</summary>
	[JsonPropertyName ("isComplete")]
	public bool? IsComplete { get; init; }

	}

/// <summary>Availability and active alert names reported by a bus device.</summary>
public sealed record LocalBusAlerts : LocalBusMessage
	{
	/// <summary>Reported active alert names.</summary>
	[JsonPropertyName ("active")]
	public IReadOnlyList<string>? Active { get; init; }

	}

/// <summary>Physical package identity of a bus device.</summary>
public record LocalBusIdentity
	{
	/// <summary>Reported subassembly part number, when queried.</summary>
	[JsonPropertyName ("subPackagePartNumber")]
	public string? SubPackagePartNumber { get; init; }

	/// <summary>Reported subassembly serial number, when queried.</summary>
	[JsonPropertyName ("subPackageSerialNumber")]
	public string? SubPackageSerialNumber { get; init; }


	/// <summary>Reported package part number.</summary>
	[JsonPropertyName ("packagePartNumber")]
	public string? PartNumber { get; init; }

	/// <summary>Reported package serial number.</summary>
	[JsonPropertyName ("packageSerialNumber")]
	public string? SerialNumber { get; init; }

	/// <summary>Reported alerts and their availability flags.</summary>
	[JsonPropertyName ("alerts")]
	public LocalBusAlerts? Alerts { get; init; }

	}

/// <summary>Legacy photovoltaic inverter identity and measurements.</summary>
public sealed record LocalSolarInverterBus : LocalBusIdentity
	{
	/// <summary>Reported fan diagnostic state; reading this does not start a test.</summary>
	[JsonPropertyName ("PVAC_ControlMeasurements")]
	public LocalSolarInverterControlMeasurements? ControlMeasurements { get; init; }


	/// <summary>Reported firmware information and availability.</summary>
	[JsonPropertyName ("PVAC_InfoMsg")]
	public LocalPvacFirmware? Firmware { get; init; }


	/// <summary>AC output measurements and state.</summary>
	[JsonPropertyName ("PVAC_Status")]
	public LocalSolarInverterStatus? Status { get; init; }

	/// <summary>PV input and ground-referenced voltage measurements.</summary>
	[JsonPropertyName ("PVAC_Logging")]
	public LocalSolarInverterMeasurements? Measurements { get; init; }

	}

/// <summary>Legacy photovoltaic inverter output.</summary>
public sealed record LocalSolarInverterStatus : LocalBusMessage
	{
	/// <summary>Output real power in watts.</summary>
	[JsonPropertyName ("PVAC_Pout")]
	public double? PowerWatts { get; init; }

	/// <summary>Firmware-provided state name.</summary>
	[JsonPropertyName ("PVAC_State")]
	public string? State { get; init; }

	/// <summary>Output voltage in volts.</summary>
	[JsonPropertyName ("PVAC_Vout")]
	public double? VoltageVolts { get; init; }

	/// <summary>Output frequency in hertz.</summary>
	[JsonPropertyName ("PVAC_Fout")]
	public double? FrequencyHertz { get; init; }

	}

/// <summary>Reported PV inputs; power can be derived only when both voltage and current are available.</summary>
public sealed record LocalSolarInverterMeasurements : LocalBusMessage
	{
	/// <summary>Reported fan speed in revolutions per minute, when included in the selected query.</summary>
	[JsonPropertyName ("PVAC_Fan_Speed_Actual_RPM")]
	public double? FanSpeedRpm { get; init; }

	/// <summary>Reported fan target in revolutions per minute, when included in the selected query.</summary>
	[JsonPropertyName ("PVAC_Fan_Speed_Target_RPM")]
	public double? FanTargetSpeedRpm { get; init; }


	/// <summary>PV input A current in amperes.</summary>
	[JsonPropertyName ("PVAC_PVCurrent_A")]
	public double? CurrentAAmps { get; init; }

	/// <summary>PV input A voltage in volts.</summary>
	[JsonPropertyName ("PVAC_PVMeasuredVoltage_A")]
	public double? VoltageAVolts { get; init; }

	/// <summary>PV input B current in amperes.</summary>
	[JsonPropertyName ("PVAC_PVCurrent_B")]
	public double? CurrentBAmps { get; init; }

	/// <summary>PV input B voltage in volts.</summary>
	[JsonPropertyName ("PVAC_PVMeasuredVoltage_B")]
	public double? VoltageBVolts { get; init; }

	/// <summary>PV input C current in amperes.</summary>
	[JsonPropertyName ("PVAC_PVCurrent_C")]
	public double? CurrentCAmps { get; init; }

	/// <summary>PV input C voltage in volts.</summary>
	[JsonPropertyName ("PVAC_PVMeasuredVoltage_C")]
	public double? VoltageCVolts { get; init; }

	/// <summary>PV input D current in amperes.</summary>
	[JsonPropertyName ("PVAC_PVCurrent_D")]
	public double? CurrentDAmps { get; init; }

	/// <summary>PV input D voltage in volts.</summary>
	[JsonPropertyName ("PVAC_PVMeasuredVoltage_D")]
	public double? VoltageDVolts { get; init; }

	/// <summary>Line 1 to ground voltage in volts.</summary>
	[JsonPropertyName ("PVAC_VL1Ground")]
	public double? Line1GroundVolts { get; init; }

	/// <summary>Line 2 to ground voltage in volts.</summary>
	[JsonPropertyName ("PVAC_VL2Ground")]
	public double? Line2GroundVolts { get; init; }

	}

/// <summary>Legacy photovoltaic string controller.</summary>
public sealed record LocalSolarStringsBus
	{
	/// <summary>Reported PV string lockout and completion state.</summary>
	[JsonPropertyName ("PVS_Logging")]
	public LocalSolarStringsLogging? Logging { get; init; }


	/// <summary>PV string connection and self-test state.</summary>
	[JsonPropertyName ("PVS_Status")]
	public LocalSolarStringsStatus? Status { get; init; }

	/// <summary>Reported alerts and availability flags.</summary>
	[JsonPropertyName ("alerts")]
	public LocalBusAlerts? Alerts { get; init; }

	}

/// <summary>PV string-controller connection and self-test state.</summary>
public sealed record LocalSolarStringsStatus : LocalBusMessage
	{
	/// <summary>Firmware-provided controller state.</summary>
	[JsonPropertyName ("PVS_State")]
	public string? State { get; init; }

	/// <summary>Line-to-line voltage in volts.</summary>
	[JsonPropertyName ("PVS_vLL")]
	public double? LineVoltageVolts { get; init; }

	/// <summary>Firmware-provided self-test state.</summary>
	[JsonPropertyName ("PVS_SelfTestState")]
	public string? SelfTestState { get; init; }

	/// <summary>Whether PV string A is connected; null means unreported.</summary>
	[JsonPropertyName ("PVS_StringA_Connected")]
	public bool? StringAConnected { get; init; }

	/// <summary>Whether PV string B is connected; null means unreported.</summary>
	[JsonPropertyName ("PVS_StringB_Connected")]
	public bool? StringBConnected { get; init; }

	/// <summary>Whether PV string C is connected; null means unreported.</summary>
	[JsonPropertyName ("PVS_StringC_Connected")]
	public bool? StringCConnected { get; init; }

	/// <summary>Whether PV string D is connected; null means unreported.</summary>
	[JsonPropertyName ("PVS_StringD_Connected")]
	public bool? StringDConnected { get; init; }

	}

/// <summary>Legacy Powerwall thermal-controller identity.</summary>
public sealed record LocalThermalControllerBus : LocalBusIdentity
	{
	/// <summary>Reported Powerwall enable-line state.</summary>
	[JsonPropertyName ("THC_Logging")]
	public LocalThermalLogging? Logging { get; init; }


	/// <summary>Reported firmware information and availability.</summary>
	[JsonPropertyName ("THC_InfoMsg")]
	public LocalThcFirmware? Firmware { get; init; }


	}

/// <summary>Legacy battery inverter measurements.</summary>
public sealed record LocalBatteryInverterBus
	{
	/// <summary>Inverter output and state.</summary>
	[JsonPropertyName ("PINV_Status")]
	public LocalBatteryInverterStatus? Status { get; init; }

	/// <summary>Split-phase AC voltage readings.</summary>
	[JsonPropertyName ("PINV_AcMeasurements")]
	public LocalBatteryAcMeasurements? AcMeasurements { get; init; }

	/// <summary>Rated output capability.</summary>
	[JsonPropertyName ("PINV_PowerCapability")]
	public LocalBatteryPowerCapability? PowerCapability { get; init; }

	/// <summary>Reported alerts and availability flags.</summary>
	[JsonPropertyName ("alerts")]
	public LocalBusAlerts? Alerts { get; init; }

	}

/// <summary>Legacy battery inverter output and grid state.</summary>
public sealed record LocalBatteryInverterStatus : LocalBusMessage
	{
	/// <summary>Real output power in watts.</summary>
	[JsonPropertyName ("PINV_Pout")]
	public double? PowerWatts { get; init; }

	/// <summary>Output voltage in volts.</summary>
	[JsonPropertyName ("PINV_Vout")]
	public double? VoltageVolts { get; init; }

	/// <summary>Output frequency in hertz.</summary>
	[JsonPropertyName ("PINV_Fout")]
	public double? FrequencyHertz { get; init; }

	/// <summary>Firmware-provided inverter state.</summary>
	[JsonPropertyName ("PINV_State")]
	public string? State { get; init; }

	/// <summary>Firmware-provided grid state.</summary>
	[JsonPropertyName ("PINV_GridState")]
	public string? GridState { get; init; }

	}

/// <summary>Legacy battery inverter split-phase voltages.</summary>
public sealed record LocalBatteryAcMeasurements : LocalBusMessage
	{
	/// <summary>First split-phase voltage in volts.</summary>
	[JsonPropertyName ("PINV_VSplit1")]
	public double? Split1Volts { get; init; }

	/// <summary>Second split-phase voltage in volts.</summary>
	[JsonPropertyName ("PINV_VSplit2")]
	public double? Split2Volts { get; init; }

	}

/// <summary>Reported battery inverter power capability.</summary>
public sealed record LocalBatteryPowerCapability : LocalBusMessage
	{
	/// <summary>Nominal real power in watts.</summary>
	[JsonPropertyName ("PINV_Pnom")]
	public double? NominalPowerWatts { get; init; }

	}

/// <summary>Legacy battery energy telemetry.</summary>
public sealed record LocalBatteryEnergyBus
	{
	/// <summary>Reported battery alerts and availability flags.</summary>
	[JsonPropertyName ("alerts")]
	public LocalBusAlerts? Alerts { get; init; }


	/// <summary>Reported firmware information and availability.</summary>
	[JsonPropertyName ("POD_InfoMsg")]
	public LocalPodFirmware? Firmware { get; init; }


	/// <summary>Stored and full-pack energy measurements.</summary>
	[JsonPropertyName ("POD_EnergyStatus")]
	public LocalLegacyBatteryEnergy? Energy { get; init; }

	}

/// <summary>Legacy battery energy measurements in watt-hours.</summary>
public sealed record LocalLegacyBatteryEnergy : LocalBusMessage
	{
	/// <summary>Remaining nominal energy in watt-hours; zero is valid.</summary>
	[JsonPropertyName ("POD_nom_energy_remaining")]
	public double? RemainingWattHours { get; init; }

	/// <summary>Nominal full-pack energy in watt-hours.</summary>
	[JsonPropertyName ("POD_nom_full_pack_energy")]
	public double? FullCapacityWattHours { get; init; }

	}

/// <summary>Site-controller meter banks.</summary>
public sealed record LocalSyncBus : LocalBusIdentity
	{
	/// <summary>Reported controller message availability and receive time.</summary>
	[JsonPropertyName ("SYNC_Status")]
	public LocalBusMessage? Status { get; init; }


	/// <summary>Reported firmware information and availability.</summary>
	[JsonPropertyName ("SYNC_InfoMsg")]
	public LocalSyncFirmware? Firmware { get; init; }


	/// <summary>Meter X phase measurements.</summary>
	[JsonPropertyName ("METER_X_AcMeasurements")]
	public LocalMeterXBus? MeterX { get; init; }

	/// <summary>Meter Y phase measurements.</summary>
	[JsonPropertyName ("METER_Y_AcMeasurements")]
	public LocalMeterYBus? MeterY { get; init; }

	}

/// <summary>Legacy meter X readings, retaining phase positions and missing values.</summary>
public sealed record LocalMeterXBus : LocalBusMessage
	{
	/// <summary>Real power in watts for CT A.</summary>
	[JsonPropertyName ("METER_X_CTA_InstRealPower")]
	public double? RealPowerAWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT A.</summary>
	[JsonPropertyName ("METER_X_CTA_InstReactivePower")]
	public double? ReactivePowerAVars { get; init; }

	/// <summary>Current in amperes for CT A.</summary>
	[JsonPropertyName ("METER_X_CTA_I")]
	public double? CurrentAAmps { get; init; }

	/// <summary>Line 1 to neutral voltage in volts.</summary>
	[JsonPropertyName ("METER_X_VL1N")]
	public double? Line1NeutralVolts { get; init; }

	/// <summary>Real power in watts for CT B.</summary>
	[JsonPropertyName ("METER_X_CTB_InstRealPower")]
	public double? RealPowerBWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT B.</summary>
	[JsonPropertyName ("METER_X_CTB_InstReactivePower")]
	public double? ReactivePowerBVars { get; init; }

	/// <summary>Current in amperes for CT B.</summary>
	[JsonPropertyName ("METER_X_CTB_I")]
	public double? CurrentBAmps { get; init; }

	/// <summary>Line 2 to neutral voltage in volts.</summary>
	[JsonPropertyName ("METER_X_VL2N")]
	public double? Line2NeutralVolts { get; init; }

	/// <summary>Real power in watts for CT C.</summary>
	[JsonPropertyName ("METER_X_CTC_InstRealPower")]
	public double? RealPowerCWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT C.</summary>
	[JsonPropertyName ("METER_X_CTC_InstReactivePower")]
	public double? ReactivePowerCVars { get; init; }

	/// <summary>Current in amperes for CT C.</summary>
	[JsonPropertyName ("METER_X_CTC_I")]
	public double? CurrentCAmps { get; init; }

	/// <summary>Line 3 to neutral voltage in volts.</summary>
	[JsonPropertyName ("METER_X_VL3N")]
	public double? Line3NeutralVolts { get; init; }

	}

/// <summary>Legacy meter Y readings, retaining phase positions and missing values.</summary>
public sealed record LocalMeterYBus : LocalBusMessage
	{
	/// <summary>Real power in watts for CT A.</summary>
	[JsonPropertyName ("METER_Y_CTA_InstRealPower")]
	public double? RealPowerAWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT A.</summary>
	[JsonPropertyName ("METER_Y_CTA_InstReactivePower")]
	public double? ReactivePowerAVars { get; init; }

	/// <summary>Current in amperes for CT A.</summary>
	[JsonPropertyName ("METER_Y_CTA_I")]
	public double? CurrentAAmps { get; init; }

	/// <summary>Line 1 to neutral voltage in volts.</summary>
	[JsonPropertyName ("METER_Y_VL1N")]
	public double? Line1NeutralVolts { get; init; }

	/// <summary>Real power in watts for CT B.</summary>
	[JsonPropertyName ("METER_Y_CTB_InstRealPower")]
	public double? RealPowerBWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT B.</summary>
	[JsonPropertyName ("METER_Y_CTB_InstReactivePower")]
	public double? ReactivePowerBVars { get; init; }

	/// <summary>Current in amperes for CT B.</summary>
	[JsonPropertyName ("METER_Y_CTB_I")]
	public double? CurrentBAmps { get; init; }

	/// <summary>Line 2 to neutral voltage in volts.</summary>
	[JsonPropertyName ("METER_Y_VL2N")]
	public double? Line2NeutralVolts { get; init; }

	/// <summary>Real power in watts for CT C.</summary>
	[JsonPropertyName ("METER_Y_CTC_InstRealPower")]
	public double? RealPowerCWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT C.</summary>
	[JsonPropertyName ("METER_Y_CTC_InstReactivePower")]
	public double? ReactivePowerCVars { get; init; }

	/// <summary>Current in amperes for CT C.</summary>
	[JsonPropertyName ("METER_Y_CTC_I")]
	public double? CurrentCAmps { get; init; }

	/// <summary>Line 3 to neutral voltage in volts.</summary>
	[JsonPropertyName ("METER_Y_VL3N")]
	public double? Line3NeutralVolts { get; init; }

	}

/// <summary>Grid-connection controller telemetry.</summary>
public sealed record LocalIslanderBus
	{
	/// <summary>Reported grid-connection state.</summary>
	[JsonPropertyName ("ISLAND_GridConnection")]
	public LocalGridConnectionBus? GridConnection { get; init; }

	/// <summary>Grid and load phase measurements.</summary>
	[JsonPropertyName ("ISLAND_AcMeasurements")]
	public LocalIslanderAcMeasurements? AcMeasurements { get; init; }

	}

/// <summary>Legacy grid contactor state.</summary>
public sealed record LocalGridConnectionBus : LocalBusMessage
	{
	/// <summary>Firmware-reported utility grid connection state name; null means unreported.</summary>
	[JsonPropertyName ("ISLAND_GridConnected")]
	public string? GridConnected { get; init; }

	}

/// <summary>Grid and load voltages and frequencies from the islanding controller.</summary>
public sealed record LocalIslanderAcMeasurements : LocalBusMessage
	{
	/// <summary>Firmware-provided grid state.</summary>
	[JsonPropertyName ("ISLAND_GridState")]
	public string? GridState { get; init; }

	/// <summary>Main line 1 to neutral voltage in volts.</summary>
	[JsonPropertyName ("ISLAND_VL1N_Main")]
	public double? MainLine1NeutralVolts { get; init; }

	/// <summary>Main line 1 frequency in hertz.</summary>
	[JsonPropertyName ("ISLAND_FreqL1_Main")]
	public double? MainLine1FrequencyHertz { get; init; }

	/// <summary>Main line 2 to neutral voltage in volts.</summary>
	[JsonPropertyName ("ISLAND_VL2N_Main")]
	public double? MainLine2NeutralVolts { get; init; }

	/// <summary>Main line 2 frequency in hertz.</summary>
	[JsonPropertyName ("ISLAND_FreqL2_Main")]
	public double? MainLine2FrequencyHertz { get; init; }

	/// <summary>Main line 3 to neutral voltage in volts.</summary>
	[JsonPropertyName ("ISLAND_VL3N_Main")]
	public double? MainLine3NeutralVolts { get; init; }

	/// <summary>Main line 3 frequency in hertz.</summary>
	[JsonPropertyName ("ISLAND_FreqL3_Main")]
	public double? MainLine3FrequencyHertz { get; init; }

	/// <summary>Load line 1 to neutral voltage in volts.</summary>
	[JsonPropertyName ("ISLAND_VL1N_Load")]
	public double? LoadLine1NeutralVolts { get; init; }

	/// <summary>Load line 1 frequency in hertz.</summary>
	[JsonPropertyName ("ISLAND_FreqL1_Load")]
	public double? LoadLine1FrequencyHertz { get; init; }

	/// <summary>Load line 2 to neutral voltage in volts.</summary>
	[JsonPropertyName ("ISLAND_VL2N_Load")]
	public double? LoadLine2NeutralVolts { get; init; }

	/// <summary>Load line 2 frequency in hertz.</summary>
	[JsonPropertyName ("ISLAND_FreqL2_Load")]
	public double? LoadLine2FrequencyHertz { get; init; }

	/// <summary>Load line 3 to neutral voltage in volts.</summary>
	[JsonPropertyName ("ISLAND_VL3N_Load")]
	public double? LoadLine3NeutralVolts { get; init; }

	/// <summary>Load line 3 frequency in hertz.</summary>
	[JsonPropertyName ("ISLAND_FreqL3_Load")]
	public double? LoadLine3FrequencyHertz { get; init; }

	}

/// <summary>Solar assembly carrying the local Meter Z channels.</summary>
public sealed record LocalMeterAssemblyBus : LocalBusIdentity
	{
	/// <summary>Reported assembly firmware information.</summary>
	[JsonPropertyName ("MSA_InfoMsg")]
	public LocalMsaFirmware? Firmware { get; init; }

	/// <summary>Reported assembly message availability and receive time.</summary>
	[JsonPropertyName ("MSA_Status")]
	public LocalBusMessage? Status { get; init; }


	/// <summary>Reported Meter Z channels with availability flags.</summary>
	[JsonPropertyName ("METER_Z_AcMeasurements")]
	public LocalMeterZBus? MeterZ { get; init; }
	}

/// <summary>Meter Z phase measurements, preserving each CT's native slot.</summary>
public sealed record LocalMeterZBus : LocalBusMessage
	{
	/// <summary>Real power in watts for CT A.</summary>
	[JsonPropertyName ("METER_Z_CTA_InstRealPower")]
	public double? RealPowerAWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT A.</summary>
	[JsonPropertyName ("METER_Z_CTA_InstReactivePower")]
	public double? ReactivePowerAVars { get; init; }

	/// <summary>Reported current in amperes for CT A.</summary>
	[JsonPropertyName ("METER_Z_CTA_I")]
	public double? CurrentAAmps { get; init; }

	/// <summary>Line 1 to ground voltage in volts.</summary>
	[JsonPropertyName ("METER_Z_VL1G")]
	public double? Line1GroundVolts { get; init; }

	/// <summary>Real power in watts for CT B.</summary>
	[JsonPropertyName ("METER_Z_CTB_InstRealPower")]
	public double? RealPowerBWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT B.</summary>
	[JsonPropertyName ("METER_Z_CTB_InstReactivePower")]
	public double? ReactivePowerBVars { get; init; }

	/// <summary>Reported current in amperes for CT B.</summary>
	[JsonPropertyName ("METER_Z_CTB_I")]
	public double? CurrentBAmps { get; init; }

	/// <summary>Line 2 to ground voltage in volts.</summary>
	[JsonPropertyName ("METER_Z_VL2G")]
	public double? Line2GroundVolts { get; init; }

	/// <summary>Real power in watts for CT C.</summary>
	[JsonPropertyName ("METER_Z_CTC_InstRealPower")]
	public double? RealPowerCWatts { get; init; }

	/// <summary>Reactive power in volt-amperes reactive for CT C.</summary>
	[JsonPropertyName ("METER_Z_CTC_InstReactivePower")]
	public double? ReactivePowerCVars { get; init; }

	/// <summary>Reported current in amperes for CT C.</summary>
	[JsonPropertyName ("METER_Z_CTC_I")]
	public double? CurrentCAmps { get; init; }

	/// <summary>Line 3 to ground voltage in volts.</summary>
	[JsonPropertyName ("METER_Z_VL3G")]
	public double? Line3GroundVolts { get; init; }

	}

/// <summary>Meter assembly firmware identification.</summary>
public sealed record LocalMsaFirmware : LocalBusMessage
	{
	/// <summary>Reported firmware hash bytes in native order.</summary>
	[JsonPropertyName ("MSA_appGitHash")]
	public IReadOnlyList<long>? ApplicationGitHash { get; init; }

	/// <summary>Reported assembly identifier.</summary>
	[JsonPropertyName ("MSA_assemblyId")]
	public long? AssemblyId { get; init; }

	}

/// <summary>Read-only solar inverter control diagnostics.</summary>
public sealed record LocalSolarInverterControlMeasurements : LocalBusMessage
	{
	/// <summary>Reported fan self-test state, retaining the firmware scalar type.</summary>
	[JsonPropertyName ("PVAC_FanSelfTestState")]
	public LocalDiagnosticScalar? FanSelfTestState { get; init; }

	}
