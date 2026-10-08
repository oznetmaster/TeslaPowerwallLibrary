// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Typed live telemetry supplied by a TEDAPI device-controller query.</summary>
public sealed record LocalTelemetry
	{
	/// <summary>Reported IEEE 2030.5 metadata; availability depends on the selected query and firmware.</summary>
	[JsonPropertyName ("ieee20305")]
	public LocalIeee20305Telemetry? Ieee20305 { get; init; }

	/// <summary>Legacy Powerwall and photovoltaic inverter bus measurements, when reported by the detailed query.</summary>
	[JsonPropertyName ("esCan")]
	public LocalEnergyBusTelemetry? EnergyBus { get; init; }

	/// <summary>Powerwall 3 bus enumeration and firmware-update progress.</summary>
	[JsonPropertyName ("pw3Can")]
	public LocalPowerwall3Bus? Powerwall3Bus { get; init; }

	/// <summary>Neurio meter channels, timestamps and pairing information.</summary>
	[JsonPropertyName ("neurio")]
	public LocalNeurioTelemetry? Neurio { get; init; }

	/// <summary>Tesla remote-meter readings from the detailed controller query.</summary>
	[JsonPropertyName ("teslaRemoteMeter")]
	public LocalRemoteMeterTelemetry? RemoteMeters { get; init; }

	/// <summary>Component families included by the detailed controller query.</summary>
	[JsonPropertyName ("components")]
	public IReadOnlyDictionary<string, IReadOnlyList<LocalComponent>>? Components { get; init; }

	/// <summary>Energy, power flow, active alerts and grid contactor information.</summary>
	[JsonPropertyName ("control")]
	public LocalControlTelemetry? Control { get; init; }

	/// <summary>Gateway clock and service status.</summary>
	[JsonPropertyName ("system")]
	public LocalSystemTelemetry? System { get; init; }
	}

/// <summary>Powerwall controller measurements. Missing firmware fields remain null.</summary>
public sealed record LocalControlTelemetry
	{
	/// <summary>Read-only protection-trip test state, when included by the query and firmware.</summary>
	[JsonPropertyName ("protectionTripTests")]
	public LocalProtectionTestStatus? ProtectionTripTests { get; init; }

	/// <summary>Site shutdown state and reported reasons.</summary>
	[JsonPropertyName ("siteShutdown")]
	public LocalShutdownStatus? Shutdown { get; init; }

	/// <summary>Solar inverter identifiers and disable reasons.</summary>
	[JsonPropertyName ("pvInverters")]
	public IReadOnlyList<LocalBatteryDevice>? SolarInverters { get; init; }

	/// <summary>Battery capacity and remaining stored energy, in watt-hours.</summary>
	[JsonPropertyName ("systemStatus")]
	public LocalBatteryEnergy? SystemStatus { get; init; }

	/// <summary>Instantaneous power at each measurement location.</summary>
	[JsonPropertyName ("meterAggregates")]
	public IReadOnlyList<LocalPowerReading>? MeterAggregates { get; init; }

	/// <summary>Grid contactor and microgrid state reported by the controller.</summary>
	[JsonPropertyName ("islanding")]
	public LocalIslandingStatus? Islanding { get; init; }

	/// <summary>Currently active controller alerts.</summary>
	[JsonPropertyName ("alerts")]
	public LocalActiveAlerts? Alerts { get; init; }

	/// <summary>Battery device identifiers and any reported disable reasons.</summary>
	[JsonPropertyName ("batteryBlocks")]
	public IReadOnlyList<LocalBatteryDevice>? BatteryBlocks { get; init; }
	}

/// <summary>Stored battery energy reported directly by the controller.</summary>
public sealed record LocalBatteryEnergy
	{
	/// <summary>Current full-charge capacity in watt-hours.</summary>
	[JsonPropertyName ("nominalFullPackEnergyWh")]
	public double? FullCapacityWattHours { get; init; }

	/// <summary>Remaining energy in watt-hours; zero is a valid reading.</summary>
	[JsonPropertyName ("nominalEnergyRemainingWh")]
	public double? RemainingWattHours { get; init; }
	}

/// <summary>A controller power measurement; positive battery power means discharge.</summary>
public sealed record LocalPowerReading
	{
	/// <summary>Measurement location, such as SITE, SOLAR, BATTERY or LOAD.</summary>
	[JsonPropertyName ("location")]
	public string? Location { get; init; }

	/// <summary>Real power in watts.</summary>
	[JsonPropertyName ("realPowerW")]
	public double? Watts { get; init; }
	}

/// <summary>Contactor and grid state. A missing value must not be interpreted as false.</summary>
public sealed record LocalIslandingStatus
	{
	/// <summary>Controller-reported reasons that prevent islanding.</summary>
	[JsonPropertyName ("disableReasons")]
	public IReadOnlyList<string>? DisableReasons { get; init; }

	/// <summary>Whether the grid contactor is closed.</summary>
	[JsonPropertyName ("contactorClosed")]
	public bool? ContactorClosed { get; init; }

	/// <summary>Whether the controller considers the grid healthy.</summary>
	[JsonPropertyName ("gridOK")]
	public bool? GridOk { get; init; }

	/// <summary>Whether the controller considers the microgrid healthy.</summary>
	[JsonPropertyName ("microGridOK")]
	public bool? MicrogridOk { get; init; }

	/// <summary>Customer islanding mode reported by the device.</summary>
	[JsonPropertyName ("customerIslandMode")]
	public string? CustomerIslandMode { get; init; }
	}

/// <summary>Alerts reported as active by the controller.</summary>
public sealed record LocalActiveAlerts
	{
	/// <summary>Device-provided alert names.</summary>
	[JsonPropertyName ("active")]
	public IReadOnlyList<string>? Active { get; init; }
	}

/// <summary>Identification and availability of a battery device.</summary>
public sealed record LocalBatteryDevice
	{
	/// <summary>Device identification number.</summary>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }

	/// <summary>Device-provided reasons why the battery is disabled.</summary>
	[JsonPropertyName ("disableReasons")]
	public IReadOnlyList<string>? DisableReasons { get; init; }
	}

/// <summary>Gateway system telemetry.</summary>
public sealed record LocalSystemTelemetry
	{
	/// <summary>Read-only remote-service availability, excluding the service session identifier.</summary>
	[JsonPropertyName ("supportMode")]
	public LocalSupportMode? SupportMode { get; init; }


	/// <summary>Site-manager service state.</summary>
	[JsonPropertyName ("sitemanagerStatus")]
	public LocalSiteManagerStatus? SiteManager { get; init; }

	/// <summary>Reported available firmware update.</summary>
	[JsonPropertyName ("updateUrgencyCheck")]
	public LocalFirmwareUpdate? FirmwareUpdate { get; init; }

	/// <summary>Gateway time as reported on the wire.</summary>
	[JsonPropertyName ("time")]
	public string? Time { get; init; }
	}

/// <summary>Telemetry for Powerwall 3 components, including battery and inverter signals.</summary>
public sealed record LocalComponentTelemetry
	{
	/// <summary>Powerwall 3 firmware-update progress included by the component query.</summary>
	[JsonPropertyName ("pw3Can")]
	public LocalPowerwall3Bus? Powerwall3Bus { get; init; }

	/// <summary>Component families keyed by their protocol name, such as pch, bms or hvp.</summary>
	[JsonPropertyName ("components")]
	public IReadOnlyDictionary<string, IReadOnlyList<LocalComponent>>? Components { get; init; }
	}

/// <summary>A single component's identity, measurements and alerts.</summary>
public sealed record LocalComponent
	{
	/// <summary>Reported subassembly part number.</summary>
	[JsonPropertyName ("subPackagePartNumber")]
	public string? SubPackagePartNumber { get; init; }

	/// <summary>Reported subassembly serial number.</summary>
	[JsonPropertyName ("subPackageSerialNumber")]
	public string? SubPackageSerialNumber { get; init; }


	/// <summary>Manufacturer part number, when supplied.</summary>
	[JsonPropertyName ("partNumber")]
	public string? PartNumber { get; init; }

	/// <summary>Manufacturer serial number, when supplied.</summary>
	[JsonPropertyName ("serialNumber")]
	public string? SerialNumber { get; init; }

	/// <summary>Named typed measurements.</summary>
	[JsonPropertyName ("signals")]
	public IReadOnlyList<LocalSignal>? Signals { get; init; }

	/// <summary>Currently active component alerts.</summary>
	[JsonPropertyName ("activeAlerts")]
	public IReadOnlyList<LocalComponentAlert>? ActiveAlerts { get; init; }
	}

/// <summary>A typed local measurement. Numeric, textual and boolean values are kept separate.</summary>
public sealed record LocalSignal
	{
	/// <summary>Firmware signal name.</summary>
	[JsonPropertyName ("name")]
	public string? Name { get; init; }

	/// <summary>Numeric value in the units defined by the signal.</summary>
	[JsonPropertyName ("value")]
	public double? Value { get; init; }

	/// <summary>Textual value, if supplied.</summary>
	[JsonPropertyName ("textValue")]
	public string? TextValue { get; init; }

	/// <summary>Boolean value, if supplied; false is distinct from missing.</summary>
	[JsonPropertyName ("boolValue")]
	public bool? BoolValue { get; init; }

	/// <summary>Device timestamp associated with this measurement.</summary>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	}

/// <summary>An active component alert.</summary>
public sealed record LocalComponentAlert
	{
	/// <summary>Firmware alert name.</summary>
	[JsonPropertyName ("name")]
	public string? Name { get; init; }
	}
