// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>A gateway clock and sync-board uptime obtained from one controller response.</summary>
/// <remarks>Available only when a matched sync board reports SYNC_Uptime through the June 2024 query.
/// This is not the uptime of each battery or inverter. Missing measurements remain unavailable.</remarks>
public sealed record LocalGatewayUptime
	{
	/// <summary>Reported sync-board uptime, converted from milliseconds.</summary>
	[JsonPropertyName ("uptime")]
	public TimeSpan Uptime
		{
		get; init;
		}
	/// <summary>Offset-aware gateway clock captured with the uptime counter.</summary>
	[JsonPropertyName ("gatewayTime")]
	public DateTimeOffset GatewayTime
		{
		get; init;
		}
	/// <summary>Gateway boot-time estimate, stabilized against normal counter publication jitter.</summary>
	[JsonPropertyName ("startedAt")]
	public DateTimeOffset StartedAt
		{
		get; init;
		}
	}

/// <summary>Tracks one connection's boot estimate without treating invalid or missing samples as zero.</summary>
internal sealed class LocalGatewayUptimeTracker
	{
	private readonly object _sync = new ();
	private LocalGatewayUptime? _latest;

	/// <summary>Clears boot tracking when authentication or session ownership changes.</summary>
	internal void Reset ()
		{
		lock (_sync)
			_latest = null;
		}

	/// <summary>Validates a matched board and clock, then serializes boot-estimate updates.</summary>
	/// <param name="telemetry">One detailed controller response.</param>
	/// <returns>A validated sample, or null when no supported uptime is reported.</returns>
	internal LocalGatewayUptime? Update (LocalTelemetry telemetry)
		{
		string? serial = telemetry.EnergyBus?.Devices?.Sync?.SerialNumber;
		string? time = telemetry.System?.Time;
		if (string.IsNullOrWhiteSpace (serial) || string.IsNullOrWhiteSpace (time)
			|| !(time!.EndsWith ("Z", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch (time, @"[+-]\d{2}:\d{2}$"))
			|| !DateTimeOffset.TryParse (time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var now))
			return null;
		var components = telemetry.Components;
		if (components is null || !components.TryGetValue ("msa", out var boards) || boards is null)
			return null;
		double? milliseconds = boards.Where (board => board is not null && board.SerialNumber == serial)
			.SelectMany (board => board.Signals ?? Array.Empty<LocalSignal> ())
			.Where (signal => signal is not null && signal.Name == "SYNC_Uptime").Select (signal => signal.Value)
			.FirstOrDefault (value => value.HasValue && value >= 0 && !double.IsNaN (value.Value) && !double.IsInfinity (value.Value));
		if (milliseconds is null)
			return null;
		DateTimeOffset start;
		TimeSpan uptime;
		try
			{
			// Framework 4.7.2 rounds FromMilliseconds to whole milliseconds. Use ticks on both targets.
			long ticks = checked((long)Math.Round (milliseconds.Value * TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero));
			uptime = TimeSpan.FromTicks (ticks);
			start = now.Subtract (uptime);
			}
		catch (ArgumentOutOfRangeException) { return null; }
		catch (OverflowException) { return null; }
		lock (_sync)
			{
			if (_latest is { } last)
				{
				if (now < last.GatewayTime)
					return last;
				// Match upstream's ten-second reset threshold and ten-minute clock-drift tolerance.
				if (uptime > last.Uptime - TimeSpan.FromSeconds (10)
					&& Math.Abs ((start - last.StartedAt).TotalSeconds) <= 600)
					start = last.StartedAt;
				}
			return _latest = new LocalGatewayUptime { Uptime = uptime, GatewayTime = now, StartedAt = start };
			}
		}
	}

public sealed partial class PowerwallTedapiClient
	{
	private readonly LocalGatewayUptimeTracker _gatewayUptime = new ();

	/// <summary>Reads reported gateway sync-board uptime without changing device state.</summary>
	/// <param name="force">Bypasses cached telemetry but never gateway backoff.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A matched uptime and clock sample, or null without supported hardware, clock or query fields.</returns>
	public async Task<LocalGatewayUptime?> GetGatewayUptimeAsync (bool force = false, CancellationToken cancellationToken = default)
		{
		EnsureConnected ();
		cancellationToken.ThrowIfCancellationRequested ();
		if (_options.LocalQueryVersion == TedapiQueryVersion.June2026)
			return null;
		var telemetry = await GetDetailedTelemetryAsync (force, cancellationToken).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		return _gatewayUptime.Update (telemetry);
		}

	/// <summary>Formats the native gateway's date convention with an offset lacking a colon.</summary>
	private static string FormatGatewayStart (DateTimeOffset value) =>
		value.ToString ("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture)
		+ value.ToString ("zzz", CultureInfo.InvariantCulture).Replace (":", string.Empty);

	/// <summary>Retains the public gateway duration-string contract, including subsecond precision.</summary>
	private static string FormatUptime (TimeSpan value) =>
		((long)value.TotalHours).ToString (CultureInfo.InvariantCulture) + "h"
		+ value.Minutes.ToString (CultureInfo.InvariantCulture) + "m"
		+ (value.Seconds + value.Ticks % TimeSpan.TicksPerSecond / (double)TimeSpan.TicksPerSecond).ToString ("0.#######", CultureInfo.InvariantCulture) + "s";
	}
