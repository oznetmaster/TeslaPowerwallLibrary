// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Identified fan, temperature and photovoltaic measurements from one component.</summary>
public sealed record LocalComponentDiagnostics
	{
	/// <summary>Parent device DIN, when known; no hardware identity is synthesized.</summary>
	[JsonPropertyName ("deviceDin")]
	public string? DeviceDin { get; init; }

	/// <summary>Protocol component family.</summary>
	[JsonPropertyName ("family")]
	public string Family { get; init; } = string.Empty;

	/// <summary>Original component index within its family.</summary>
	[JsonPropertyName ("index")]
	public int Index { get; init; }

	/// <summary>Reported component part number.</summary>
	[JsonPropertyName ("partNumber")]
	public string? PartNumber { get; init; }

	/// <summary>Reported component serial number.</summary>
	[JsonPropertyName ("serialNumber")]
	public string? SerialNumber { get; init; }

	/// <summary>Reported fans; an empty collection means no fan signals were supplied.</summary>
	[JsonPropertyName ("fans")]
	public IReadOnlyList<LocalFanReading> Fans { get; init; } = Array.Empty<LocalFanReading> ();

	/// <summary>Reported temperature sensors and their native Celsius readings.</summary>
	[JsonPropertyName ("temperatures")]
	public IReadOnlyList<LocalTemperatureReading> Temperatures { get; init; } = Array.Empty<LocalTemperatureReading> ();

	/// <summary>Reported photovoltaic inputs, retaining unavailable values as null.</summary>
	[JsonPropertyName ("solarStrings")]
	public IReadOnlyList<LocalSolarStringReading> SolarStrings { get; init; } = Array.Empty<LocalSolarStringReading> ();

	}

/// <summary>A fan measurement; drive duty and target speed are distinct values.</summary>
public sealed record LocalFanReading
	{
	/// <summary>Fan identifier within its component.</summary>
	[JsonPropertyName ("name")]
	public string Name { get; init; } = string.Empty;

	/// <summary>Reported rotational speed in revolutions per minute; zero means stopped.</summary>
	[JsonPropertyName ("speedRpm")]
	public double? SpeedRpm { get; init; }

	/// <summary>Reported target speed in revolutions per minute, when provided.</summary>
	[JsonPropertyName ("targetSpeedRpm")]
	public double? TargetSpeedRpm { get; init; }

	/// <summary>Reported fan drive duty percentage; not a target RPM.</summary>
	[JsonPropertyName ("dutyPercent")]
	public double? DutyPercent { get; init; }

	}

/// <summary>A temperature sensor measurement reported by a component.</summary>
public sealed record LocalTemperatureReading
	{
	/// <summary>Original firmware signal identifying the sensor.</summary>
	[JsonPropertyName ("signalName")]
	public string SignalName { get; init; } = string.Empty;

	/// <summary>Temperature in degrees Celsius; null means unavailable.</summary>
	[JsonPropertyName ("celsius")]
	public double? Celsius { get; init; }

	/// <summary>Device timestamp for this signal, when supplied.</summary>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp { get; init; }

	}

/// <summary>One photovoltaic input with reported electrical measurements and connection state.</summary>
public sealed record LocalSolarStringReading
	{
	/// <summary>Input identifier, such as A through F.</summary>
	[JsonPropertyName ("name")]
	public string Name { get; init; } = string.Empty;

	/// <summary>Native state string; not inferred from voltage or power.</summary>
	[JsonPropertyName ("state")]
	public string? State { get; init; }

	/// <summary>Explicit connection flag when reported; not inferred from active or standby state.</summary>
	[JsonPropertyName ("connected")]
	public bool? Connected { get; init; }

	/// <summary>Reported DC voltage in volts.</summary>
	[JsonPropertyName ("voltageVolts")]
	public double? VoltageVolts { get; init; }

	/// <summary>Reported DC current in amperes.</summary>
	[JsonPropertyName ("currentAmps")]
	public double? CurrentAmps { get; init; }

	/// <summary>DC power in watts calculated only when both reported voltage and current are available.</summary>
	[JsonIgnore]
	public double? PowerWatts => VoltageVolts * CurrentAmps;
	}

/// <summary>Projects actual component signals without network access or fabricated default measurements.</summary>
public static class LocalDiagnosticsProjection
	{
	private static readonly string[] TemperatureSignals =
		{ "THC_AmbientTemp", "PCH_AmbientTemp", "PCH_heatsinkTemp", "HVP_PackTempMax", "HVP_PackTempMin", "HVP_ShuntTemperature" };

	/// <summary>Extracts typed diagnostics from all available controller and configured-device components.</summary>
	/// <param name="snapshot">A collected device snapshot.</param>
	/// <returns>Separate component records, preserving hardware identity and original family indexes.</returns>
	public static IReadOnlyList<LocalComponentDiagnostics> Create (LocalDeviceSnapshot snapshot)
		{
		#if NETFRAMEWORK
		if (snapshot is null) throw new ArgumentNullException (nameof (snapshot));
#else
		ArgumentNullException.ThrowIfNull (snapshot);
#endif
		var result = new List<LocalComponentDiagnostics> ();
		AddComponents (result, snapshot.Configuration.Din, snapshot.Controller.Components);
		foreach (var device in snapshot.Devices)
			AddComponents (result, device.Din, device.Telemetry?.Components);
		AddLegacy (result, snapshot.Configuration.Din, snapshot.Controller.EnergyBus?.Devices);
		return result;
		}

	private static void AddComponents (List<LocalComponentDiagnostics> result, string? din,
		IReadOnlyDictionary<string, IReadOnlyList<LocalComponent>>? families)
		{
		foreach (var family in families ?? new Dictionary<string, IReadOnlyList<LocalComponent>> ())
			for (int index = 0; index < family.Value.Count; index++)
				{
				var component = family.Value[index];
				var signals = component.Signals ?? Array.Empty<LocalSignal> ();
				LocalSignal? Signal (string name) => signals.FirstOrDefault (s => s.Name == name);
				var fans = new List<LocalFanReading> ();
				if (Signal ("PVAC_Fan_Speed_Actual_RPM") is not null || Signal ("PVAC_Fan_Speed_Target_RPM") is not null)
					fans.Add (new LocalFanReading { Name = "PVAC", SpeedRpm = Signal ("PVAC_Fan_Speed_Actual_RPM")?.Value,
						TargetSpeedRpm = Signal ("PVAC_Fan_Speed_Target_RPM")?.Value });
				foreach (var name in new[] { "A", "B" })
					if (Signal ("PCH_FanSpeed_" + name) is not null || Signal ("PCH_FanDuty_" + name) is not null)
						fans.Add (new LocalFanReading { Name = name, SpeedRpm = Signal ("PCH_FanSpeed_" + name)?.Value,
							DutyPercent = Signal ("PCH_FanDuty_" + name)?.Value });
				var temperatures = signals.Where (s => TemperatureSignals.Contains (s.Name)).Select (s =>
					new LocalTemperatureReading { SignalName = s.Name!, Celsius = s.Value, Timestamp = s.Timestamp }).ToArray ();
				var strings = new List<LocalSolarStringReading> ();
				foreach (var name in new[] { "A", "B", "C", "D", "E", "F" })
					{
					var state = Signal ("PCH_PvState_" + name);
					var voltage = Signal ("PCH_PvVoltage" + name);
					var current = Signal ("PCH_PvCurrent" + name);
					if (state is not null || voltage is not null || current is not null)
						strings.Add (new LocalSolarStringReading { Name = name, State = state?.TextValue,
							VoltageVolts = voltage?.Value, CurrentAmps = current?.Value });
					}
				if (fans.Count + temperatures.Length + strings.Count > 0)
					result.Add (new LocalComponentDiagnostics { DeviceDin = din, Family = family.Key, Index = index,
						PartNumber = component.PartNumber, SerialNumber = component.SerialNumber,
						Fans = fans, Temperatures = temperatures, SolarStrings = strings });
				}
		}

	private static bool Available (LocalBusMessage? message) => message is not null && message.IsMissing != true && message.IsComplete != false;

	private static void AddLegacy (List<LocalComponentDiagnostics> result, string? din, LocalEnergyBusDevices? bus)
		{
		for (int index = 0; index < (bus?.SolarInverters?.Count ?? 0); index++)
			{
			var inverter = bus!.SolarInverters![index];
			var measurements = inverter.Measurements;
			if (!Available (measurements)) continue;
			var voltages = new[] { measurements!.VoltageAVolts, measurements.VoltageBVolts, measurements.VoltageCVolts, measurements.VoltageDVolts };
			var currents = new[] { measurements.CurrentAAmps, measurements.CurrentBAmps, measurements.CurrentCAmps, measurements.CurrentDAmps };
			var strings = Enumerable.Range (0, 4).Select (slot => new LocalSolarStringReading
				{ Name = ((char)('A' + slot)).ToString (), VoltageVolts = voltages[slot], CurrentAmps = currents[slot] }).ToArray ();
			result.Add (new LocalComponentDiagnostics { DeviceDin = din, Family = "PVAC", Index = index,
				PartNumber = inverter.PartNumber, SerialNumber = inverter.SerialNumber, SolarStrings = strings,
				Fans = measurements.FanSpeedRpm.HasValue || measurements.FanTargetSpeedRpm.HasValue
					? new[] { new LocalFanReading { Name = "PVAC", SpeedRpm = measurements.FanSpeedRpm, TargetSpeedRpm = measurements.FanTargetSpeedRpm } }
					: Array.Empty<LocalFanReading> () });
			}
		for (int index = 0; index < (bus?.SolarStrings?.Count ?? 0); index++)
			{
			var status = bus!.SolarStrings![index].Status;
			if (!Available (status)) continue;
			var connected = new[] { status!.StringAConnected, status.StringBConnected, status.StringCConnected, status.StringDConnected };
			result.Add (new LocalComponentDiagnostics { DeviceDin = din, Family = "PVS", Index = index,
				SolarStrings = Enumerable.Range (0, 4).Select (slot => new LocalSolarStringReading
					{ Name = ((char)('A' + slot)).ToString (), Connected = connected[slot] }).ToArray () });
			}
		}
	}

public sealed partial class PowerwallTedapiClient
	{
	/// <summary>Reads typed fan, temperature and photovoltaic-input diagnostics across configured devices.</summary>
	/// <param name="force">Bypasses measurement caches without bypassing device backoff.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>Identified component diagnostics, preserving unavailable values.</returns>
	public async Task<IReadOnlyList<LocalComponentDiagnostics>> GetComponentDiagnosticsAsync (bool force = false, CancellationToken cancellationToken = default) =>
		LocalDiagnosticsProjection.Create (await GetDeviceSnapshotAsync (force, cancellationToken).ConfigureAwait (false));
	}
