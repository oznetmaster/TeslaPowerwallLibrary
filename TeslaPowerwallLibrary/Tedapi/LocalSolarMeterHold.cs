// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Bounded use of gateway-retained remote-meter samples during SolarMeterComms.</summary>
internal static class LocalSolarMeterHold
	{
	private const double ZERO_WATTS = 10;
	private const double MAX_AGE_SECONDS = 60;

	internal static bool IsCandidate (LocalTelemetry telemetry)
		{
		double? solar = SolarPower (telemetry);
		return telemetry.Control?.Alerts?.Active?.Contains ("SolarMeterComms", StringComparer.Ordinal) == true
			&& (solar is null || solar >= 0 && solar <= ZERO_WATTS);
		}

	internal static MeterAggregates Apply (MeterAggregates basis, LocalConfiguration configuration,
		LocalTelemetry status, LocalTelemetry detailed, IReadOnlyList<LocalConfiguredMeter>? projected = null)
		{
		if (!IsCandidate (status)) return basis;
		var assignments = configuration.Meters?.Where (m => string.Equals (m.Location, "solar", StringComparison.OrdinalIgnoreCase)
			&& m.EnabledChannels?.Any (enabled => enabled) == true).ToArray ();
		if (assignments is null || !assignments.Any (m => m.Type is "trm_mb" or "trm_wifi")) return basis;
		double? gatewaySolar = SolarPower (status);
		// During this alert, an unvalidated null-power CT fallback must not bypass the age guard.
		basis = basis with { Solar = basis.Solar is null ? null : basis.Solar with { InstantPower = gatewaySolar } };
		// A remote-meter subtotal cannot replace a total that also includes another meter family.
		if (assignments.Any (m => m.Type is not ("trm_mb" or "trm_wifi"))) return basis;
		if (!TryTime (status.System?.Time, out var now)) return basis;
		var meters = (projected ?? LocalMeterProjection.Create (configuration, detailed))
			.Where (m => m.Family == "TeslaRemoteMeter").ToArray ();
		var samples = new Dictionary<(string Device, int Slot), (double Watts, DateTimeOffset Time, string Timestamp)> ();
		foreach (var assignment in assignments)
			{
			if (string.IsNullOrWhiteSpace (assignment.Connection?.DeviceSerial)) return basis;
			string id = assignment.Connection!.DeviceSerial!;
			var matching = meters.Where (m => m.DeviceId == id).ToArray ();
			if (matching.Length != 1 || !TryTime (matching[0].Timestamp, out var time)
				|| Math.Abs ((now - time).TotalSeconds) > MAX_AGE_SECONDS) return basis;
			for (int slot = 0; slot < assignment.EnabledChannels!.Count; slot++)
				{
				if (!assignment.EnabledChannels[slot]) continue;
				var channels = matching[0].Channels.Where (c => c.Index == slot && c.IsConfigured
					&& string.Equals (c.Location, "solar", StringComparison.OrdinalIgnoreCase)).ToArray ();
				if (channels.Length != 1 || channels[0].RealPowerWatts is not double watts || !Finite (watts)) return basis;
				samples[(id, slot)] = (watts, time, matching[0].Timestamp!);
				}
			}
		double retained = samples.Values.Sum (s => s.Watts);
		if (!Finite (retained) || retained <= 0) return basis;
		double? load = basis.Load?.InstantPower;
		double? correctedLoad = load + retained - (gatewaySolar ?? 0);
		if (load.HasValue && (!Finite (load.Value) || !Finite (correctedLoad!.Value))) return basis;
		var oldest = samples.Values.OrderBy (s => s.Time).First ();
		double age = Math.Max (0, (now - oldest.Time).TotalSeconds);
		return basis with
			{
			Solar = (basis.Solar ?? new MeterReading ()) with
				{ InstantPower = retained, LastCommunicationTime = oldest.Timestamp, PowerHoldAgeSeconds = age },
			Load = load.HasValue ? basis.Load! with { InstantPower = correctedLoad, PowerHoldAgeSeconds = age } : basis.Load
			};
		}

	private static double? SolarPower (LocalTelemetry telemetry) => telemetry.Control?.MeterAggregates?
		.FirstOrDefault (p => string.Equals (p.Location, "solar", StringComparison.OrdinalIgnoreCase))?.Watts;

	private static bool Finite (double value) => !double.IsNaN (value) && !double.IsInfinity (value);

	private static bool TryTime (string? text, out DateTimeOffset time)
		{
		time = default;
		if (string.IsNullOrWhiteSpace (text)) return false;
		text = text!.Trim ();
		// Require an explicit ISO timezone; never interpret a gateway timestamp in the computer's local zone.
		int length = text.Length;
		bool offset = length >= 6 && text[length - 3] == ':' && (text[length - 6] is '+' or '-');
		return text.IndexOf ('T') > 0 && (text[length - 1] == 'Z' || offset)
			&& DateTimeOffset.TryParse (text, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
		}
	}