// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Non-secret meter configuration used to interpret reported channels.</summary>
public sealed record LocalMeterConfiguration
	{
	/// <summary>Meter type, such as neurio_w2_tcp, trm_mb or trm_wifi.</summary>
	/// <value>Meter type, such as neurio_w2_tcp, trm_mb or trm_wifi.</value>
	[JsonPropertyName ("type")]
	public string? Type
		{
		get; init;
		}
	/// <summary>Configured measurement location, such as site or solar.</summary>
	/// <value>Configured measurement location, such as site or solar.</value>
	[JsonPropertyName ("location")]
	public string? Location
		{
		get; init;
		}
	/// <summary>Enabled current-transformer slots in their original zero-based order; absent slots are unspecified.</summary>
	/// <value>Enabled current-transformer slots in their original zero-based order; absent slots are unspecified.</value>
	[JsonPropertyName ("cts")]
	public IReadOnlyList<bool>? EnabledChannels
		{
		get; init;
		}
	/// <summary>Per-slot inversion settings; retained as metadata, not applied a second time to reported power.</summary>
	/// <value>Per-slot inversion settings; retained as metadata, not applied a second time to reported power.</value>
	[JsonPropertyName ("inverted")]
	public IReadOnlyList<bool>? InvertedChannels
		{
		get; init;
		}
	/// <summary>Real-power multiplier; an omitted multiplier defaults to one, while explicit zero is preserved.</summary>
	/// <value>Real-power multiplier; an omitted multiplier defaults to one, while explicit zero is preserved.</value>
	[JsonPropertyName ("real_power_scale_factor")]
	public double? RealPowerScaleFactor
		{
		get; init;
		}
	/// <summary>Non-secret meter identity.</summary>
	/// <value>Non-secret meter identity.</value>
	[JsonPropertyName ("connection")]
	public LocalMeterConnection? Connection
		{
		get; init;
		}
	}

/// <summary>Identifies the physical meter without exposing network credentials.</summary>
public sealed record LocalMeterConnection
	{
	/// <summary>Neurio serial number or Tesla remote-meter DIN.</summary>
	/// <value>Neurio serial number or Tesla remote-meter DIN.</value>
	[JsonPropertyName ("device_serial")]
	public string? DeviceSerial
		{
		get; init;
		}
	}

/// <summary>A reported physical meter with configured channel interpretation.</summary>
public sealed record LocalConfiguredMeter
	{
	/// <summary>Reported serial number or DIN; absent identities remain null.</summary>
	/// <value>Reported serial number or DIN; absent identities remain null.</value>
	[JsonPropertyName ("deviceId")]
	public string? DeviceId
		{
		get; init;
		}
	/// <summary>Telemetry family: Neurio or TeslaRemoteMeter.</summary>
	/// <value>Telemetry family: Neurio or TeslaRemoteMeter.</value>
	[JsonPropertyName ("family")]
	public string Family { get; init; } = string.Empty;
	/// <summary>Reported firmware version.</summary>
	/// <value>Reported firmware version.</value>
	[JsonPropertyName ("firmwareVersion")]
	public string? FirmwareVersion
		{
		get; init;
		}
	/// <summary>Device-reported sample time.</summary>
	/// <value>Device-reported sample time.</value>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp
		{
		get; init;
		}
	/// <summary>Enabled or unconfigured channels, retaining original slot numbers.</summary>
	/// <value>Enabled or unconfigured channels, retaining original slot numbers.</value>
	[JsonPropertyName ("channels")]
	public IReadOnlyList<LocalConfiguredMeterChannel> Channels { get; init; } = Array.Empty<LocalConfiguredMeterChannel> ();
	}

/// <summary>One physical current-transformer slot with its reported values and configured interpretation.</summary>
public sealed record LocalConfiguredMeterChannel
	{
	/// <summary>Original zero-based CT slot, independent of other enabled channels.</summary>
	/// <value>Original zero-based CT slot, independent of other enabled channels.</value>
	[JsonPropertyName ("index")]
	public int Index
		{
		get; init;
		}
	/// <summary>Configured location; null when the slot has no matching assignment.</summary>
	/// <value>Configured location; null when the slot has no matching assignment.</value>
	[JsonPropertyName ("location")]
	public string? Location
		{
		get; init;
		}
	/// <summary>Whether an explicit enabled-channel assignment was found.</summary>
	/// <value>Whether an explicit enabled-channel assignment was found.</value>
	[JsonPropertyName ("isConfigured")]
	public bool IsConfigured
		{
		get; init;
		}
	/// <summary>Configured real-power scale, defaulting to one when omitted.</summary>
	/// <value>Configured real-power scale, defaulting to one when omitted.</value>
	[JsonPropertyName ("realPowerScaleFactor")]
	public double RealPowerScaleFactor { get; init; } = 1;
	/// <summary>Configured inversion metadata; the firmware's reported sign is retained.</summary>
	/// <value>Configured inversion metadata; the firmware's reported sign is retained.</value>
	[JsonPropertyName ("inverted")]
	public bool? Inverted
		{
		get; init;
		}
	/// <summary>Unmodified reported measurements, preserving missing values and device units.</summary>
	/// <value>Unmodified reported measurements, preserving missing values and device units.</value>
	[JsonPropertyName ("reported")]
	public LocalMeterChannel Reported { get; init; } = new ();
	/// <summary>Reported watts multiplied by the configured factor; null when watts are absent.</summary>
	/// <value>Reported watts multiplied by the configured factor; null when watts are absent.</value>
	[JsonIgnore]
	public double? RealPowerWatts => Reported.RealPowerWatts * RealPowerScaleFactor;
	}

/// <summary>Interprets meter channels without conflating devices, compacting disabled slots or fabricating readings.</summary>
public static partial class LocalMeterProjection
	{
	/// <summary>Combines a controller response with non-secret configuration, without any network calls.</summary>
	/// <param name="configuration">Meter assignments and scaling.</param>
	/// <param name="telemetry">Reported meter readings from the detailed query.</param>
	/// <returns>One entry per reported meter; different devices retain separate channels.</returns>
	/// <exception cref="ArgumentNullException">Either input is null.</exception>
	/// <exception cref="PowerwallInvalidConfigurationException">A CT slot has conflicting enabled assignments or non-finite scaling.</exception>
	public static IReadOnlyList<LocalConfiguredMeter> Create (LocalConfiguration configuration, LocalTelemetry telemetry)
		{
#if NETFRAMEWORK
		if (configuration is null)
			throw new ArgumentNullException (nameof (configuration));
		if (telemetry is null)
			throw new ArgumentNullException (nameof (telemetry));
#else
		ArgumentNullException.ThrowIfNull (configuration);
		ArgumentNullException.ThrowIfNull (telemetry);
#endif
		var result = new List<LocalConfiguredMeter> ();
		foreach (var meter in telemetry.Neurio?.Readings ?? Array.Empty<LocalNeurioReading> ())
			result.Add (Project (configuration, "neurio_w2_tcp", "Neurio", meter.Serial, meter.FirmwareVersion, meter.Timestamp, meter.Channels));
		foreach (var meter in telemetry.RemoteMeters?.Meters ?? Array.Empty<LocalRemoteMeter> ())
			result.Add (Project (configuration, "trm_mb", "TeslaRemoteMeter", meter.Din, meter.Reading?.FirmwareVersion, meter.Reading?.Timestamp, meter.Reading?.Channels));
		return result;
		}

	/// <summary>Builds location summaries, retaining controller power except for a bounded SolarMeterComms hold from configured remote meters.</summary>
	/// <param name="configuration">Configured channel locations and scale factors.</param>
	/// <param name="telemetry">Controller measurements and detailed meter readings.</param>
	/// <returns>Site, solar, battery and load summaries; absent data is never replaced with zero.</returns>
	public static MeterAggregates Aggregate (LocalConfiguration configuration, LocalTelemetry telemetry)
		{
		var meters = Create (configuration, telemetry);
		MeterReading? At (string location)
			{
			var power = telemetry.Control?.MeterAggregates?.FirstOrDefault (p => string.Equals (p.Location, location, StringComparison.OrdinalIgnoreCase));
			var matching = meters.Select (m => new
				{
				Meter = m,
				Channels = m.Channels.Where (c => c.IsConfigured
				&& string.Equals (c.Location, location, StringComparison.OrdinalIgnoreCase)).ToArray ()
				}).Where (m => m.Channels.Length > 0).ToArray ();
			var channels = matching.SelectMany (m => m.Channels).ToArray ();
			if (channels.Length == 0)
				return power is null ? null : new MeterReading { InstantPower = power.Watts, LastCommunicationTime = telemetry.System?.Time };
			double? Total (Func<LocalConfiguredMeterChannel, double?> select)
				{
				var values = channels.Select (select).ToArray ();
				return values.All (v => v.HasValue) ? values.Sum (v => v!.Value) : null;
				}
			double? Phase (int index)
				{
				var values = channels.Where (c => c.Index == index).Select (c => c.Reported.CurrentAmps).ToArray ();
				return values.Length > 0 && values.All (v => v.HasValue) ? values.Sum (v => v!.Value) : null;
				}
			var times = matching.Select (m => m.Meter.Timestamp).Distinct ().ToArray ();
			return new MeterReading
				{
				InstantPower = power?.Watts ?? Total (c => c.RealPowerWatts),
				InstantReactivePower = Total (c => c.Reported.ReactivePowerVars),
				InstantAverageVoltage = Total (c => c.Reported.VoltageVolts) / channels.Length,
				InstantTotalCurrent = Total (c => c.Reported.CurrentAmps),
				InstantAverageCurrent = Total (c => c.Reported.CurrentAmps) / channels.Length,
				EnergyExported = Total (c => c.Reported.EnergyExportedWattSeconds) / 3600,
				EnergyImported = Total (c => c.Reported.EnergyImportedWattSeconds) / 3600,
				PhaseACurrent = Phase (0),
				PhaseBCurrent = Phase (1),
				PhaseCCurrent = Phase (2),
				NumMetersAggregated = matching.Length,
				LastCommunicationTime = times.Length == 1 ? times[0] : null
				};
			}
		var aggregates = new MeterAggregates { Site = At ("site"), Solar = At ("solar"), Battery = At ("battery"), Load = At ("load") };
		return LocalSolarMeterHold.Apply (aggregates, configuration, telemetry, telemetry, meters);
		}

	private static LocalConfiguredMeter Project (LocalConfiguration configuration, string type, string family, string? id,
		string? firmware, string? timestamp, IReadOnlyList<LocalMeterChannel>? reported)
		{
		var definitions = configuration.Meters?.Where (m => !string.IsNullOrWhiteSpace (id)
			&& (m.Type == type || type == "trm_mb" && m.Type == "trm_wifi")
			&& string.Equals (m.Connection?.DeviceSerial, id, StringComparison.Ordinal)).ToArray () ?? Array.Empty<LocalMeterConfiguration> ();
		var channels = new List<LocalConfiguredMeterChannel> ();
		for (int index = 0; index < (reported?.Count ?? 0); index++)
			{
			var enabled = definitions.Where (m => m.EnabledChannels is { } slots && index < slots.Count && slots[index]).ToArray ();
			bool explicitlyDisabled = definitions.Any (m => m.EnabledChannels is { } slots && index < slots.Count && !slots[index]);
			if (enabled.Length == 0 && explicitlyDisabled)
				continue;
			LocalMeterConfiguration? assignment = enabled.FirstOrDefault ();
			double factor = assignment?.RealPowerScaleFactor ?? 1;
			bool? Inverted (LocalMeterConfiguration? m) => m?.InvertedChannels is { } flags && index < flags.Count ? flags[index] : null;
			if (double.IsNaN (factor) || double.IsInfinity (factor)
				|| enabled.Any (m => m.Location != assignment!.Location || (m.RealPowerScaleFactor ?? 1) != factor || Inverted (m) != Inverted (assignment)))
				throw new PowerwallInvalidConfigurationException ("Conflicting or invalid meter channel configuration.");
			channels.Add (new LocalConfiguredMeterChannel
				{
				Index = index,
				Location = assignment?.Location,
				IsConfigured = assignment is not null,
				RealPowerScaleFactor = factor,
				Inverted = Inverted (assignment),
				Reported = reported![index]
				});
			}
		return new LocalConfiguredMeter { DeviceId = id, Family = family, FirmwareVersion = firmware, Timestamp = timestamp, Channels = channels };
		}
	}