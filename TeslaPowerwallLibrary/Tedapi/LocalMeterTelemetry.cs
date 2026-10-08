// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Reported site shutdown state; reading this model never changes that state.</summary>
public sealed record LocalShutdownStatus
	{
	/// <summary>Whether the controller reports site shutdown.</summary>
	[JsonPropertyName ("isShutDown")]
	public bool? IsShutDown { get; init; }
	/// <summary>Reported reasons for shutdown.</summary>
	[JsonPropertyName ("reasons")]
	public IReadOnlyList<string>? Reasons { get; init; }
	}

/// <summary>Reported protection-trip test activity. Reading this model never starts or cancels a test.</summary>
public sealed record LocalProtectionTestStatus
	{
	/// <summary>Stored test-result records, including null slots; present in the supplemental protection query.</summary>
	[JsonPropertyName ("results")]
	public LocalDiagnosticRecords<LocalProtectionTestResult>? Results { get; init; }

	/// <summary>Whether a protection-trip test is running; null means the field was not reported.</summary>
	[JsonPropertyName ("isRunning")]
	public bool? IsRunning { get; init; }
	}

/// <summary>Reported site-manager service state.</summary>
public sealed record LocalSiteManagerStatus
	{
	/// <summary>Whether the controller service is running.</summary>
	[JsonPropertyName ("isRunning")]
	public bool? IsRunning { get; init; }
	}

/// <summary>Reported firmware version identifiers.</summary>
public sealed record LocalFirmwareVersion
	{
	/// <summary>Firmware version string.</summary>
	[JsonPropertyName ("version")]
	public string? Version { get; init; }
	/// <summary>Firmware build commit identifier.</summary>
	[JsonPropertyName ("gitHash")]
	public string? GitHash { get; init; }
	}

/// <summary>Firmware-update availability reported by the gateway.</summary>
public sealed record LocalFirmwareUpdate
	{
	/// <summary>Device-provided update urgency.</summary>
	[JsonPropertyName ("urgency")]
	public string? Urgency { get; init; }
	/// <summary>Available firmware identifiers.</summary>
	[JsonPropertyName ("version")]
	public LocalFirmwareVersion? Version { get; init; }
	/// <summary>Time of the reported update check.</summary>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	}

/// <summary>Voltage, current, power and cumulative energy reported by a meter channel.</summary>
public sealed record LocalMeterChannel
	{
	/// <summary>Voltage in volts.</summary>
	[JsonPropertyName ("voltageV")]
	public double? VoltageVolts { get; init; }
	/// <summary>Real power in watts; sign follows the device's channel configuration.</summary>
	[JsonPropertyName ("realPowerW")]
	public double? RealPowerWatts { get; init; }
	/// <summary>Reactive power in volt-amperes reactive.</summary>
	[JsonPropertyName ("reactivePowerVAR")]
	public double? ReactivePowerVars { get; init; }
	/// <summary>Current in amperes.</summary>
	[JsonPropertyName ("currentA")]
	public double? CurrentAmps { get; init; }
	/// <summary>Cumulative exported energy in watt-seconds, when reported.</summary>
	[JsonPropertyName ("energyExportedWs")]
	public double? EnergyExportedWattSeconds { get; init; }
	/// <summary>Cumulative imported energy in watt-seconds, when reported.</summary>
	[JsonPropertyName ("energyImportedWs")]
	public double? EnergyImportedWattSeconds { get; init; }
	}

/// <summary>Neurio meter telemetry reported by the gateway.</summary>
public sealed record LocalNeurioTelemetry
	{
	/// <summary>Whether wired meter detection is running.</summary>
	[JsonPropertyName ("isDetectingWiredMeters")]
	public bool? IsDetectingWiredMeters { get; init; }
	/// <summary>Timestamped meter-channel readings.</summary>
	[JsonPropertyName ("readings")]
	public IReadOnlyList<LocalNeurioReading>? Readings { get; init; }
	/// <summary>Known meter pairings.</summary>
	[JsonPropertyName ("pairings")]
	public IReadOnlyList<LocalMeterPairing>? Pairings { get; init; }
	}

/// <summary>A timestamped Neurio reading.</summary>
public sealed record LocalNeurioReading
	{
	/// <summary>Meter serial number.</summary>
	[JsonPropertyName ("serial")]
	public string? Serial { get; init; }
	/// <summary>Meter firmware version, when reported.</summary>
	[JsonPropertyName ("firmwareVersion")]
	public string? FirmwareVersion { get; init; }
	/// <summary>Per-channel measurements.</summary>
	[JsonPropertyName ("dataRead")]
	public IReadOnlyList<LocalMeterChannel>? Channels { get; init; }
	/// <summary>Device timestamp.</summary>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	}

/// <summary>Identification and connection state of a paired meter.</summary>
public sealed record LocalMeterPairing
	{
	/// <summary>Reported short identifier, retaining its scalar representation.</summary>
	[JsonPropertyName ("shortId")]
	public LocalDiagnosticScalar? ShortId { get; init; }

	/// <summary>Reported MAC address, retaining its scalar representation.</summary>
	[JsonPropertyName ("macAddress")]
	public LocalDiagnosticScalar? MacAddress { get; init; }


	/// <summary>Meter serial number.</summary>
	[JsonPropertyName ("serial")]
	public string? Serial { get; init; }
	/// <summary>Reported pairing status.</summary>
	[JsonPropertyName ("status")]
	public string? Status { get; init; }
	/// <summary>Reported pairing errors.</summary>
	[JsonPropertyName ("errors")]
	public IReadOnlyList<string>? Errors { get; init; }
	/// <summary>Whether the meter uses a wired connection.</summary>
	[JsonPropertyName ("isWired")]
	public bool? IsWired { get; init; }
	/// <summary>Reported meter hostname.</summary>
	[JsonPropertyName ("hostname")]
	public string? Hostname { get; init; }
	/// <summary>Modbus port identifier.</summary>
	[JsonPropertyName ("modbusPort")]
	public string? ModbusPort { get; init; }
	/// <summary>Modbus device address.</summary>
	[JsonPropertyName ("modbusId")]
	public int? ModbusId { get; init; }
	/// <summary>Last pairing-state update timestamp.</summary>
	[JsonPropertyName ("lastUpdateTimestamp")]
	public string? LastUpdateTimestamp { get; init; }
	}

/// <summary>Tesla remote-meter telemetry.</summary>
public sealed record LocalRemoteMeterTelemetry
	{
	/// <summary>Known remote meters and their latest readings.</summary>
	[JsonPropertyName ("meters")]
	public IReadOnlyList<LocalRemoteMeter>? Meters { get; init; }
	/// <summary>Remote meters detected on wired serial ports.</summary>
	[JsonPropertyName ("detectedWired")]
	public IReadOnlyList<LocalDetectedMeter>? DetectedWired { get; init; }
	}

/// <summary>A detected remote meter.</summary>
public sealed record LocalDetectedMeter
	{
	/// <summary>Meter device identifier.</summary>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }
	/// <summary>Connected serial-port identifier.</summary>
	[JsonPropertyName ("serialPort")]
	public string? SerialPort { get; init; }
	}

/// <summary>A remote meter and its latest measurements.</summary>
public sealed record LocalRemoteMeter
	{
	/// <summary>Firmware-update progress reported for this meter.</summary>
	[JsonPropertyName ("firmwareUpdate")]
	public LocalUpdateProgress? FirmwareUpdate { get; init; }

	/// <summary>Meter device identifier.</summary>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }
	/// <summary>Latest reported meter sample.</summary>
	[JsonPropertyName ("reading")]
	public LocalRemoteMeterReading? Reading { get; init; }
	}

/// <summary>A timestamped Tesla remote-meter sample.</summary>
public sealed record LocalRemoteMeterReading
	{
	/// <summary>Device timestamp.</summary>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	/// <summary>Meter firmware version.</summary>
	[JsonPropertyName ("firmwareVersion")]
	public string? FirmwareVersion { get; init; }
	/// <summary>Reported received signal strength in the gateway's rssiDb units; null means unreported.</summary>
	[JsonPropertyName ("rssiDb")]
	public double? SignalStrengthDb { get; init; }
	/// <summary>Current-transformer channel readings.</summary>
	[JsonPropertyName ("ctReadings")]
	public IReadOnlyList<LocalMeterChannel>? Channels { get; init; }
	}

/// <summary>Read-only Powerwall 3 bus state.</summary>
public sealed record LocalPowerwall3Bus
	{
	/// <summary>Reported firmware-update status; reading it never starts an update.</summary>
	[JsonPropertyName ("firmwareUpdate")]
	public LocalBusFirmwareUpdate? FirmwareUpdate { get; init; }
	/// <summary>Reported device enumeration state.</summary>
	[JsonPropertyName ("enumeration")]
	public LocalBusEnumeration? Enumeration { get; init; }
	}

/// <summary>Reported device enumeration state.</summary>
public sealed record LocalBusEnumeration
	{
	/// <summary>Whether enumeration is in progress; null means unreported.</summary>
	[JsonPropertyName ("inProgress")]
	public bool? InProgress { get; init; }
	}

/// <summary>Firmware-update state and its device-provided progress measurements.</summary>
public sealed record LocalBusFirmwareUpdate
	{
	/// <summary>Whether firmware is currently being updated.</summary>
	[JsonPropertyName ("isUpdating")]
	public bool? IsUpdating { get; init; }
	/// <summary>Device-provided progress details.</summary>
	[JsonPropertyName ("progress")]
	public LocalUpdateProgress? Progress { get; init; }
	}

/// <summary>Reported firmware-update progress; numeric values retain the device's native scale.</summary>
public sealed record LocalUpdateProgress
	{
	/// <summary>Whether this update task is running.</summary>
	[JsonPropertyName ("updating")]
	public bool? Updating { get; init; }
	/// <summary>Reported total number of update steps.</summary>
	[JsonPropertyName ("numSteps")]
	public int? NumberOfSteps { get; init; }
	/// <summary>Reported current step index.</summary>
	[JsonPropertyName ("currentStep")]
	public int? CurrentStep { get; init; }
	/// <summary>Progress within the current step on the device's native scale.</summary>
	[JsonPropertyName ("currentStepProgress")]
	public double? CurrentStepProgress { get; init; }
	/// <summary>Overall progress on the device's native scale.</summary>
	[JsonPropertyName ("progress")]
	public double? Progress { get; init; }
	}
