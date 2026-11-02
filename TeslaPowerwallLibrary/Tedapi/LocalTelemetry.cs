// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Typed live telemetry supplied by a TEDAPI device-controller query.</summary>
public sealed record LocalTelemetry
	{
	/// <summary>Reported IEEE 2030.5 metadata; availability depends on the selected query and firmware.</summary>
	/// <value>Reported IEEE 2030.5 metadata; availability depends on the selected query and firmware.</value>
	[JsonPropertyName ("ieee20305")]
	public LocalIeee20305Telemetry? Ieee20305 { get; init; }

	/// <summary>Legacy Powerwall and photovoltaic inverter bus measurements, when reported by the detailed query.</summary>
	/// <value>Legacy Powerwall and photovoltaic inverter bus measurements, when reported by the detailed query.</value>
	[JsonPropertyName ("esCan")]
	public LocalEnergyBusTelemetry? EnergyBus { get; init; }

	/// <summary>Powerwall 3 bus enumeration and firmware-update progress.</summary>
	/// <value>Powerwall 3 bus enumeration and firmware-update progress.</value>
	[JsonPropertyName ("pw3Can")]
	public LocalPowerwall3Bus? Powerwall3Bus { get; init; }

	/// <summary>Neurio meter channels, timestamps and pairing information.</summary>
	/// <value>Neurio meter channels, timestamps and pairing information.</value>
	[JsonPropertyName ("neurio")]
	public LocalNeurioTelemetry? Neurio { get; init; }

	/// <summary>Tesla remote-meter readings from the detailed controller query.</summary>
	/// <value>Tesla remote-meter readings from the detailed controller query.</value>
	[JsonPropertyName ("teslaRemoteMeter")]
	public LocalRemoteMeterTelemetry? RemoteMeters { get; init; }

	/// <summary>Component families included by the detailed controller query.</summary>
	/// <value>Component families included by the detailed controller query.</value>
	[JsonPropertyName ("components")]
	public IReadOnlyDictionary<string, IReadOnlyList<LocalComponent>>? Components { get; init; }

	/// <summary>Energy, power flow, active alerts and grid contactor information.</summary>
	/// <value>Energy, power flow, active alerts and grid contactor information.</value>
	[JsonPropertyName ("control")]
	public LocalControlTelemetry? Control { get; init; }

	/// <summary>Gateway clock and service status.</summary>
	/// <value>Gateway clock and service status.</value>
	[JsonPropertyName ("system")]
	public LocalSystemTelemetry? System { get; init; }
	}

/// <summary>Powerwall controller measurements. Missing firmware fields remain null.</summary>
public sealed record LocalControlTelemetry
	{
	/// <summary>Read-only protection-trip test state, when included by the query and firmware.</summary>
	/// <value>Read-only protection-trip test state, when included by the query and firmware.</value>
	[JsonPropertyName ("protectionTripTests")]
	public LocalProtectionTestStatus? ProtectionTripTests { get; init; }

	/// <summary>Site shutdown state and reported reasons.</summary>
	/// <value>Site shutdown state and reported reasons.</value>
	[JsonPropertyName ("siteShutdown")]
	public LocalShutdownStatus? Shutdown { get; init; }

	/// <summary>Solar inverter identifiers and disable reasons.</summary>
	/// <value>Solar inverter identifiers and disable reasons.</value>
	[JsonPropertyName ("pvInverters")]
	public IReadOnlyList<LocalBatteryDevice>? SolarInverters { get; init; }

	/// <summary>Battery capacity and remaining stored energy, in watt-hours.</summary>
	/// <value>Battery capacity and remaining stored energy, in watt-hours.</value>
	[JsonPropertyName ("systemStatus")]
	public LocalBatteryEnergy? SystemStatus { get; init; }

	/// <summary>Instantaneous power at each measurement location.</summary>
	/// <value>Instantaneous power at each measurement location.</value>
	[JsonPropertyName ("meterAggregates")]
	public IReadOnlyList<LocalPowerReading>? MeterAggregates { get; init; }

	/// <summary>Grid contactor and microgrid state reported by the controller.</summary>
	/// <value>Grid contactor and microgrid state reported by the controller.</value>
	[JsonPropertyName ("islanding")]
	public LocalIslandingStatus? Islanding { get; init; }

	/// <summary>Currently active controller alerts.</summary>
	/// <value>Currently active controller alerts.</value>
	[JsonPropertyName ("alerts")]
	public LocalActiveAlerts? Alerts { get; init; }

	/// <summary>Battery device identifiers and any reported disable reasons.</summary>
	/// <value>Battery device identifiers and any reported disable reasons.</value>
	[JsonPropertyName ("batteryBlocks")]
	public IReadOnlyList<LocalBatteryDevice>? BatteryBlocks { get; init; }
	}

/// <summary>Stored battery energy reported directly by the controller.</summary>
public sealed record LocalBatteryEnergy
	{
	/// <summary>Current full-charge capacity in watt-hours.</summary>
	/// <value>Current full-charge capacity in watt-hours.</value>
	[JsonPropertyName ("nominalFullPackEnergyWh")]
	public double? FullCapacityWattHours { get; init; }

	/// <summary>Remaining energy in watt-hours; zero is a valid reading.</summary>
	/// <value>Remaining energy in watt-hours; zero is a valid reading.</value>
	[JsonPropertyName ("nominalEnergyRemainingWh")]
	public double? RemainingWattHours { get; init; }
	}

/// <summary>A controller power measurement; positive battery power means discharge.</summary>
public sealed record LocalPowerReading
	{
	/// <summary>Measurement location, such as SITE, SOLAR, BATTERY or LOAD.</summary>
	/// <value>Measurement location, such as SITE, SOLAR, BATTERY or LOAD.</value>
	[JsonPropertyName ("location")]
	public string? Location { get; init; }

	/// <summary>Real power in watts.</summary>
	/// <value>Real power in watts.</value>
	[JsonPropertyName ("realPowerW")]
	public double? Watts { get; init; }
	}

/// <summary>Contactor and grid state. A missing value must not be interpreted as false.</summary>
public sealed record LocalIslandingStatus
	{
	/// <summary>Controller-reported reasons that prevent islanding.</summary>
	/// <value>Controller-reported reasons that prevent islanding.</value>
	[JsonPropertyName ("disableReasons")]
	public IReadOnlyList<string>? DisableReasons { get; init; }

	/// <summary>Whether the grid contactor is closed.</summary>
	/// <value>Whether the grid contactor is closed.</value>
	[JsonPropertyName ("contactorClosed")]
	public bool? ContactorClosed { get; init; }

	/// <summary>Whether the controller considers the grid healthy.</summary>
	/// <value>Whether the controller considers the grid healthy.</value>
	[JsonPropertyName ("gridOK")]
	public bool? GridOk { get; init; }

	/// <summary>Whether the controller considers the microgrid healthy.</summary>
	/// <value>Whether the controller considers the microgrid healthy.</value>
	[JsonPropertyName ("microGridOK")]
	public bool? MicrogridOk { get; init; }

	/// <summary>Customer islanding mode reported by the device.</summary>
	/// <value>Customer islanding mode reported by the device.</value>
	[JsonPropertyName ("customerIslandMode")]
	public string? CustomerIslandMode { get; init; }
	}

/// <summary>Alerts reported as active by the controller.</summary>
public sealed record LocalActiveAlerts
	{
	/// <summary>Device-provided alert names.</summary>
	/// <value>Device-provided alert names.</value>
	[JsonPropertyName ("active")]
	public IReadOnlyList<string>? Active { get; init; }
	}

/// <summary>Identification and availability of a battery device.</summary>
public sealed record LocalBatteryDevice
	{
	/// <summary>Device identification number.</summary>
	/// <value>Device identification number.</value>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }

	/// <summary>Device-provided reasons why the battery is disabled.</summary>
	/// <value>Device-provided reasons why the battery is disabled.</value>
	[JsonPropertyName ("disableReasons")]
	public IReadOnlyList<string>? DisableReasons { get; init; }
	}

/// <summary>Gateway system telemetry.</summary>
public sealed record LocalSystemTelemetry
	{
	/// <summary>Read-only remote-service availability, excluding the service session identifier.</summary>
	/// <value>Read-only remote-service availability, excluding the service session identifier.</value>
	[JsonPropertyName ("supportMode")]
	public LocalSupportMode? SupportMode { get; init; }


	/// <summary>Site-manager service state.</summary>
	/// <value>Site-manager service state.</value>
	[JsonPropertyName ("sitemanagerStatus")]
	public LocalSiteManagerStatus? SiteManager { get; init; }

	/// <summary>Reported available firmware update.</summary>
	/// <value>Reported available firmware update.</value>
	[JsonPropertyName ("updateUrgencyCheck")]
	public LocalFirmwareUpdate? FirmwareUpdate { get; init; }

	/// <summary>Gateway time as reported on the wire.</summary>
	/// <value>Gateway time as reported on the wire.</value>
	[JsonPropertyName ("time")]
	public string? Time { get; init; }
	}

/// <summary>Telemetry for Powerwall 3 components, including battery and inverter signals.</summary>
public sealed record LocalComponentTelemetry
	{
	/// <summary>Powerwall 3 firmware-update progress included by the component query.</summary>
	/// <value>Powerwall 3 firmware-update progress included by the component query.</value>
	[JsonPropertyName ("pw3Can")]
	public LocalPowerwall3Bus? Powerwall3Bus { get; init; }

	/// <summary>Component families keyed by their protocol name, such as pch, bms or hvp.</summary>
	/// <value>Component families keyed by their protocol name, such as pch, bms or hvp.</value>
	[JsonPropertyName ("components")]
	public IReadOnlyDictionary<string, IReadOnlyList<LocalComponent>>? Components { get; init; }
	}

/// <summary>A single component's identity, measurements and alerts.</summary>
public sealed record LocalComponent
	{
	/// <summary>Reported subassembly part number.</summary>
	/// <value>Reported subassembly part number.</value>
	[JsonPropertyName ("subPackagePartNumber")]
	public string? SubPackagePartNumber { get; init; }

	/// <summary>Reported subassembly serial number.</summary>
	/// <value>Reported subassembly serial number.</value>
	[JsonPropertyName ("subPackageSerialNumber")]
	public string? SubPackageSerialNumber { get; init; }


	/// <summary>Manufacturer part number, when supplied.</summary>
	/// <value>Manufacturer part number, when supplied.</value>
	[JsonPropertyName ("partNumber")]
	public string? PartNumber { get; init; }

	/// <summary>Manufacturer serial number, when supplied.</summary>
	/// <value>Manufacturer serial number, when supplied.</value>
	[JsonPropertyName ("serialNumber")]
	public string? SerialNumber { get; init; }

	/// <summary>Named typed measurements.</summary>
	/// <value>Named typed measurements.</value>
	[JsonPropertyName ("signals")]
	public IReadOnlyList<LocalSignal>? Signals { get; init; }

	/// <summary>Currently active component alerts.</summary>
	/// <value>Currently active component alerts.</value>
	[JsonPropertyName ("activeAlerts")]
	public IReadOnlyList<LocalComponentAlert>? ActiveAlerts { get; init; }
	}

/// <summary>A typed local measurement. Numeric, textual and boolean values are kept separate.</summary>
public sealed record LocalSignal
	{
	/// <summary>Firmware signal name.</summary>
	/// <value>Firmware signal name.</value>
	[JsonPropertyName ("name")]
	public string? Name { get; init; }

	/// <summary>Numeric value in the units defined by the signal.</summary>
	/// <value>Numeric value in the units defined by the signal.</value>
	[JsonPropertyName ("value")]
	public double? Value { get; init; }

	/// <summary>Textual value, if supplied.</summary>
	/// <value>Textual value, if supplied.</value>
	[JsonPropertyName ("textValue")]
	public string? TextValue { get; init; }

	/// <summary>Boolean value, if supplied; false is distinct from missing.</summary>
	/// <value>Boolean value, if supplied; false is distinct from missing.</value>
	[JsonPropertyName ("boolValue")]
	public bool? BoolValue { get; init; }

	/// <summary>Device timestamp associated with this measurement.</summary>
	/// <value>Device timestamp associated with this measurement.</value>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }
	}

/// <summary>An active component alert.</summary>
public sealed record LocalComponentAlert
	{
	/// <summary>Firmware alert name.</summary>
	/// <value>Firmware alert name.</value>
	[JsonPropertyName ("name")]
	public string? Name { get; init; }
	}
