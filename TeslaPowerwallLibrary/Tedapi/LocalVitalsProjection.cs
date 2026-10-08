// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Preserves the existing vitals-map API using typed local models from every available device.</summary>
internal static class LocalVitalsProjection
	{
	/// <summary>Projects signals and legacy bus messages without parsing or publishing a raw JSON payload.</summary>
	/// <param name="snapshot">Collected devices and controller measurements.</param>
	/// <param name="controllerDin">Authenticated controller identity when configuration omits it.</param>
	/// <returns>Device-qualified maps so repeated component serials cannot overwrite another device.</returns>
	internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Create (LocalDeviceSnapshot snapshot, string controllerDin)
		{
		var result = new Dictionary<string, IReadOnlyDictionary<string, object?>> (StringComparer.Ordinal);
		result[controllerDin] = new Dictionary<string, object?> { ["alerts"] = snapshot.Controller.Control?.Alerts?.Active };
		void Components (string din, IReadOnlyDictionary<string, IReadOnlyList<LocalComponent>>? families)
			{
			foreach (var family in families ?? new Dictionary<string, IReadOnlyList<LocalComponent>> ())
				for (int index = 0; index < family.Value.Count; index++)
					{
					var component = family.Value[index];
					var values = new Dictionary<string, object?> (StringComparer.Ordinal)
						{
						["partNumber"] = component.PartNumber, ["serialNumber"] = component.SerialNumber,
						["componentParentDin"] = din,
						["alerts"] = component.ActiveAlerts?.Select (a => a.Name).Where (n => n is not null).ToArray ()
						};
					foreach (var signal in component.Signals ?? Array.Empty<LocalSignal> ())
						if (!string.IsNullOrEmpty (signal.Name))
							values[signal.Name!] = signal.BoolValue is bool flag ? flag : signal.TextValue is string text ? text : signal.Value;
					result[din + "/" + family.Key + "/" + index + "/" + component.SerialNumber] = values;
					}
			}
		Components (controllerDin, snapshot.Controller.Components);
		foreach (var device in snapshot.Devices)
			Components (device.Din, device.Telemetry?.Components);
		var bus = snapshot.Controller.EnergyBus?.Devices;
		void Bus<T> (string family, IReadOnlyList<T>? devices) where T : class
			{
			for (int index = 0; index < (devices?.Count ?? 0); index++)
				{
				var values = new Dictionary<string, object?> (StringComparer.Ordinal) { ["componentParentDin"] = controllerDin };
				AddMessage (values, devices![index]);
				result[controllerDin + "/bus/" + family + "/" + index] = values;
				}
			}
		Bus ("PVAC", bus?.SolarInverters);
		Bus ("PVS", bus?.SolarStrings);
		Bus ("THC", bus?.ThermalControllers);
		Bus ("PINV", bus?.BatteryInverters);
		Bus ("POD", bus?.BatteryEnergy);
		if (bus?.Sync is { } sync) Bus ("SYNC", new[] { sync });
		if (bus?.MeterAssembly is { } msa) Bus ("MSA", new[] { msa });
		if (bus?.Islander is { } islander) Bus ("ISLANDER", new[] { islander });
		return result;
		}

	private static void AddMessage (Dictionary<string, object?> values, object message)
		{
		if (message is LocalBusMessage { IsMissing: true } or LocalBusMessage { IsComplete: false }) return;
		if (message is LocalBusAlerts alerts) { values["alerts"] = alerts.Active; return; }
		foreach (var property in message.GetType ().GetProperties (BindingFlags.Public | BindingFlags.Instance))
			{
			var name = property.GetCustomAttribute<JsonPropertyNameAttribute> ()?.Name;
			if (name is null || name is "isMIA" or "isComplete" || !property.CanRead || property.GetIndexParameters ().Length != 0) continue;
			var value = property.GetValue (message);
			if (value is null || value is string || value.GetType ().IsValueType || value is IEnumerable<string> || value is IEnumerable<long>) values[name] = value;
			else AddMessage (values, value);
			}
		}
	}
