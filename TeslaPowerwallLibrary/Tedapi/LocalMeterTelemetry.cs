// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Reported site shutdown state; reading this model never changes that state.</summary>
public sealed record LocalShutdownStatus
	{
	/// <summary>Whether the controller reports site shutdown.</summary>
	/// <value>Whether the controller reports site shutdown.</value>
	[JsonPropertyName ("isShutDown")]
	public bool? IsShutDown { get; init; }
	/// <summary>Reported reasons for shutdown.</summary>
	/// <value>Reported reasons for shutdown.</value>
	[JsonPropertyName ("reasons")]
	public IReadOnlyList<string>? Reasons { get; init; }
	}

/// <summary>Reported protection-trip test activity. Reading this model never starts or cancels a test.</summary>
public sealed record LocalProtectionTestStatus
	{
	/// <summary>Stored test-result records, including null slots; present in the supplemental protection query.</summary>
	/// <value>Stored test-result records, including null slots; present in the supplemental protection query.</value>
	[JsonPropertyName ("results")]
	public LocalDiagnosticRecords<LocalProtectionTestResult>? Results { get; init; }

	/// <summary>Whether a protection-trip test is running; null means the field was not reported.</summary>
	/// <value>Whether a protection-trip test is running; null means the field was not reported.</value>
	[JsonPropertyName ("isRunning")]
	public bool? IsRunning { get; init; }
	}

/// <summary>Reported site-manager service state.</summary>
public sealed record LocalSiteManagerStatus
	{
	/// <summary>Whether the controller service is running.</summary>
	/// <value>Whether the controller service is running.</value>
	[JsonPropertyName ("isRunning")]
	public bool? IsRunning { get; init; }
	}

/// <summary>Reported firmware version identifiers.</summary>
public sealed record LocalFirmwareVersion
	{
	/// <summary>Firmware version string.</summary>
	/// <value>Firmware version string.</value>
	[JsonPropertyName ("version")]
	public string? Version { get; init; }
	/// <summary>Firmware build commit identifier.</summary>
	/// <value>Firmware build commit identifier.</value>
	[JsonPropertyName ("gitHash")]
	public string? GitHash { get; init; }
	}

/// <summary>Firmware-update availability reported by the gateway.</summary>
public sealed record LocalFirmwareUpdate
	{
	/// <summary>Device-provided update urgency.</summary>
	/// <value>Device-provided update urgency.</value>
	[JsonPropertyName ("urgency")]
	public string? Urgency { get; init; }
	/// <summary>Available firmware identifiers.</summary>
	/// <value>Available firmware identifiers.</value>
	[JsonPropertyName ("version")]
	public LocalFirmwareVersion? Version { get; init; }
	/// <summary>Time of the reported update check.</summary>
	/// <value>Time of the reported update check.</value>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	}

/// <summary>Voltage, current, power and cumulative energy reported by a meter channel.</summary>
public sealed record LocalMeterChannel
	{
	/// <summary>Voltage in volts.</summary>
	/// <value>Voltage in volts.</value>
	[JsonPropertyName ("voltageV")]
	public double? VoltageVolts { get; init; }
	/// <summary>Real power in watts; sign follows the device's channel configuration.</summary>
	/// <value>Real power in watts; sign follows the device's channel configuration.</value>
	[JsonPropertyName ("realPowerW")]
	public double? RealPowerWatts { get; init; }
	/// <summary>Reactive power in volt-amperes reactive.</summary>
	/// <value>Reactive power in volt-amperes reactive.</value>
	[JsonPropertyName ("reactivePowerVAR")]
	public double? ReactivePowerVars { get; init; }
	/// <summary>Current in amperes.</summary>
	/// <value>Current in amperes.</value>
	[JsonPropertyName ("currentA")]
	public double? CurrentAmps { get; init; }
	/// <summary>Cumulative exported energy in watt-seconds, when reported.</summary>
	/// <value>Cumulative exported energy in watt-seconds, when reported.</value>
	[JsonPropertyName ("energyExportedWs")]
	public double? EnergyExportedWattSeconds { get; init; }
	/// <summary>Cumulative imported energy in watt-seconds, when reported.</summary>
	/// <value>Cumulative imported energy in watt-seconds, when reported.</value>
	[JsonPropertyName ("energyImportedWs")]
	public double? EnergyImportedWattSeconds { get; init; }
	}

/// <summary>Neurio meter telemetry reported by the gateway.</summary>
public sealed record LocalNeurioTelemetry
	{
	/// <summary>Whether wired meter detection is running.</summary>
	/// <value>Whether wired meter detection is running.</value>
	[JsonPropertyName ("isDetectingWiredMeters")]
	public bool? IsDetectingWiredMeters { get; init; }
	/// <summary>Timestamped meter-channel readings.</summary>
	/// <value>Timestamped meter-channel readings.</value>
	[JsonPropertyName ("readings")]
	public IReadOnlyList<LocalNeurioReading>? Readings { get; init; }
	/// <summary>Known meter pairings.</summary>
	/// <value>Known meter pairings.</value>
	[JsonPropertyName ("pairings")]
	public IReadOnlyList<LocalMeterPairing>? Pairings { get; init; }
	}

/// <summary>A timestamped Neurio reading.</summary>
public sealed record LocalNeurioReading
	{
	/// <summary>Meter serial number.</summary>
	/// <value>Meter serial number.</value>
	[JsonPropertyName ("serial")]
	public string? Serial { get; init; }
	/// <summary>Meter firmware version, when reported.</summary>
	/// <value>Meter firmware version, when reported.</value>
	[JsonPropertyName ("firmwareVersion")]
	public string? FirmwareVersion { get; init; }
	/// <summary>Per-channel measurements.</summary>
	/// <value>Per-channel measurements.</value>
	[JsonPropertyName ("dataRead")]
	public IReadOnlyList<LocalMeterChannel>? Channels { get; init; }
	/// <summary>Device timestamp.</summary>
	/// <value>Device timestamp.</value>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	}

/// <summary>Identification and connection state of a paired meter.</summary>
public sealed record LocalMeterPairing
	{
	/// <summary>Reported short identifier, retaining its scalar representation.</summary>
	/// <value>Reported short identifier, retaining its scalar representation.</value>
	[JsonPropertyName ("shortId")]
	public LocalDiagnosticScalar? ShortId { get; init; }

	/// <summary>Reported MAC address, retaining its scalar representation.</summary>
	/// <value>Reported MAC address, retaining its scalar representation.</value>
	[JsonPropertyName ("macAddress")]
	public LocalDiagnosticScalar? MacAddress { get; init; }


	/// <summary>Meter serial number.</summary>
	/// <value>Meter serial number.</value>
	[JsonPropertyName ("serial")]
	public string? Serial { get; init; }
	/// <summary>Reported pairing status.</summary>
	/// <value>Reported pairing status.</value>
	[JsonPropertyName ("status")]
	public string? Status { get; init; }
	/// <summary>Reported pairing errors.</summary>
	/// <value>Reported pairing errors.</value>
	[JsonPropertyName ("errors")]
	public IReadOnlyList<string>? Errors { get; init; }
	/// <summary>Whether the meter uses a wired connection.</summary>
	/// <value>Whether the meter uses a wired connection.</value>
	[JsonPropertyName ("isWired")]
	public bool? IsWired { get; init; }
	/// <summary>Reported meter hostname.</summary>
	/// <value>Reported meter hostname.</value>
	[JsonPropertyName ("hostname")]
	public string? Hostname { get; init; }
	/// <summary>Modbus port identifier.</summary>
	/// <value>Modbus port identifier.</value>
	[JsonPropertyName ("modbusPort")]
	public string? ModbusPort { get; init; }
	/// <summary>Modbus device address.</summary>
	/// <value>Modbus device address.</value>
	[JsonPropertyName ("modbusId")]
	public int? ModbusId { get; init; }
	/// <summary>Last pairing-state update timestamp.</summary>
	/// <value>Last pairing-state update timestamp.</value>
	[JsonPropertyName ("lastUpdateTimestamp")]
	public string? LastUpdateTimestamp { get; init; }
	}

/// <summary>Tesla remote-meter telemetry.</summary>
public sealed record LocalRemoteMeterTelemetry
	{
	/// <summary>Known remote meters and their latest readings.</summary>
	/// <value>Known remote meters and their latest readings.</value>
	[JsonPropertyName ("meters")]
	public IReadOnlyList<LocalRemoteMeter>? Meters { get; init; }
	/// <summary>Remote meters detected on wired serial ports.</summary>
	/// <value>Remote meters detected on wired serial ports.</value>
	[JsonPropertyName ("detectedWired")]
	public IReadOnlyList<LocalDetectedMeter>? DetectedWired { get; init; }
	}

/// <summary>A detected remote meter.</summary>
public sealed record LocalDetectedMeter
	{
	/// <summary>Meter device identifier.</summary>
	/// <value>Meter device identifier.</value>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }
	/// <summary>Connected serial-port identifier.</summary>
	/// <value>Connected serial-port identifier.</value>
	[JsonPropertyName ("serialPort")]
	public string? SerialPort { get; init; }
	}

/// <summary>A remote meter and its latest measurements.</summary>
public sealed record LocalRemoteMeter
	{
	/// <summary>Firmware-update progress reported for this meter.</summary>
	/// <value>Firmware-update progress reported for this meter.</value>
	[JsonPropertyName ("firmwareUpdate")]
	public LocalUpdateProgress? FirmwareUpdate { get; init; }

	/// <summary>Meter device identifier.</summary>
	/// <value>Meter device identifier.</value>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }
	/// <summary>Latest reported meter sample.</summary>
	/// <value>Latest reported meter sample.</value>
	[JsonPropertyName ("reading")]
	public LocalRemoteMeterReading? Reading { get; init; }
	}

/// <summary>A timestamped Tesla remote-meter sample.</summary>
public sealed record LocalRemoteMeterReading
	{
	/// <summary>Device timestamp.</summary>
	/// <value>Device timestamp.</value>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	/// <summary>Meter firmware version.</summary>
	/// <value>Meter firmware version.</value>
	[JsonPropertyName ("firmwareVersion")]
	public string? FirmwareVersion { get; init; }
	/// <summary>Reported received signal strength in the gateway's rssiDb units; null means unreported.</summary>
	/// <value>Reported received signal strength in the gateway's rssiDb units; null means unreported.</value>
	[JsonPropertyName ("rssiDb")]
	public double? SignalStrengthDb { get; init; }
	/// <summary>Current-transformer channel readings.</summary>
	/// <value>Current-transformer channel readings.</value>
	[JsonPropertyName ("ctReadings")]
	public IReadOnlyList<LocalMeterChannel>? Channels { get; init; }
	}

/// <summary>Read-only Powerwall 3 bus state.</summary>
public sealed record LocalPowerwall3Bus
	{
	/// <summary>Reported firmware-update status; reading it never starts an update.</summary>
	/// <value>Reported firmware-update status; reading it never starts an update.</value>
	[JsonPropertyName ("firmwareUpdate")]
	public LocalBusFirmwareUpdate? FirmwareUpdate { get; init; }
	/// <summary>Reported device enumeration state.</summary>
	/// <value>Reported device enumeration state.</value>
	[JsonPropertyName ("enumeration")]
	public LocalBusEnumeration? Enumeration { get; init; }
	}

/// <summary>Reported device enumeration state.</summary>
public sealed record LocalBusEnumeration
	{
	/// <summary>Whether enumeration is in progress; null means unreported.</summary>
	/// <value>Whether enumeration is in progress; null means unreported.</value>
	[JsonPropertyName ("inProgress")]
	public bool? InProgress { get; init; }
	}

/// <summary>Firmware-update state and its device-provided progress measurements.</summary>
public sealed record LocalBusFirmwareUpdate
	{
	/// <summary>Whether firmware is currently being updated.</summary>
	/// <value>Whether firmware is currently being updated.</value>
	[JsonPropertyName ("isUpdating")]
	public bool? IsUpdating { get; init; }
	/// <summary>Device-provided progress details.</summary>
	/// <value>Device-provided progress details.</value>
	[JsonPropertyName ("progress")]
	public LocalUpdateProgress? Progress { get; init; }
	}

/// <summary>Reported firmware-update progress; numeric values retain the device's native scale.</summary>
public sealed record LocalUpdateProgress
	{
	/// <summary>Whether this update task is running.</summary>
	/// <value>Whether this update task is running.</value>
	[JsonPropertyName ("updating")]
	public bool? Updating { get; init; }
	/// <summary>Reported total number of update steps.</summary>
	/// <value>Reported total number of update steps.</value>
	[JsonPropertyName ("numSteps")]
	public int? NumberOfSteps { get; init; }
	/// <summary>Reported current step index.</summary>
	/// <value>Reported current step index.</value>
	[JsonPropertyName ("currentStep")]
	public int? CurrentStep { get; init; }
	/// <summary>Progress within the current step on the device's native scale.</summary>
	/// <value>Progress within the current step on the device's native scale.</value>
	[JsonPropertyName ("currentStepProgress")]
	public double? CurrentStepProgress { get; init; }
	/// <summary>Overall progress on the device's native scale.</summary>
	/// <value>Overall progress on the device's native scale.</value>
	[JsonPropertyName ("progress")]
	public double? Progress { get; init; }
	}
