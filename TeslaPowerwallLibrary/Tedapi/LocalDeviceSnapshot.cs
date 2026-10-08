// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Component measurements belonging to one configured Powerwall 3, with explicit availability.</summary>
public sealed record LocalDeviceComponents
	{
	/// <summary>Configured device identification number.</summary>
	[JsonPropertyName ("din")]
	public string Din { get; init; } = string.Empty;
	/// <summary>Measurements from this device only; null if this transport cannot obtain them.</summary>
	[JsonPropertyName ("telemetry")]
	public LocalComponentTelemetry? Telemetry { get; init; }
	/// <summary>Reason measurements are unavailable; null on success.</summary>
	[JsonPropertyName ("unavailableReason")]
	public string? UnavailableReason { get; init; }
	}

/// <summary>A requested system read containing identified device measurements and normalized battery summaries.</summary>
/// <remarks>Individual queries are sequential and are not a simultaneous hardware snapshot.</remarks>
public sealed record LocalDeviceSnapshot
	{
	/// <summary>Non-secret configured device and meter identities.</summary>
	[JsonPropertyName ("configuration")]
	public LocalConfiguration Configuration { get; init; } = new ();
	/// <summary>Detailed controller telemetry, including the legacy energy bus.</summary>
	[JsonPropertyName ("controller")]
	public LocalTelemetry Controller { get; init; } = new ();
	/// <summary>One result for each configured Powerwall 3, retaining unavailable followers explicitly.</summary>
	[JsonPropertyName ("devices")]
	public IReadOnlyList<LocalDeviceComponents> Devices { get; init; } = Array.Empty<LocalDeviceComponents> ();
	/// <summary>Configured batteries with measurements attached only to matching device identities.</summary>
	[JsonPropertyName ("batteries")]
	public IReadOnlyList<BatteryBlock>? Batteries { get; init; }
	}

public sealed partial class PowerwallTedapiClient
	{
	/// <summary>Reads configured Powerwall components and legacy bus data, retaining per-device availability.</summary>
	/// <remarks>Setup-network TEDAPI can route configured followers. Signed LAN uses an explicitly supplied caller-owned follower connection when available; otherwise those followers remain unavailable.</remarks>
	/// <param name="force">Bypasses measurement caches but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation, including between device queries.</param>
	/// <returns>Typed device results and battery summaries; unavailable values remain null.</returns>
	public async Task<LocalDeviceSnapshot> GetDeviceSnapshotAsync (bool force = false, CancellationToken cancellationToken = default)
		{
		var config = await GetConfigurationAsync (force, cancellationToken).ConfigureAwait (false);
		var controller = await GetDetailedTelemetryAsync (force, cancellationToken).ConfigureAwait (false);
		var devices = new List<LocalDeviceComponents> ();
		var blocks = LocalBatteryProjection.Create (config, _din!, new LocalComponentTelemetry ());
		foreach (var battery in (config.Batteries ?? Array.Empty<LocalBatteryConfiguration> ())
			.Where (b => !string.IsNullOrWhiteSpace (b.Din) && b.Type?.IndexOf ("Powerwall3", StringComparison.OrdinalIgnoreCase) >= 0)
			.GroupBy (b => b.Din, StringComparer.Ordinal).Select (g => g.First ()))
			{
			cancellationToken.ThrowIfCancellationRequested ();
			string din = battery.Din!;
			if (IsSigned && din != _din && _options.LocalFollowerConnection is null)
				{
				devices.Add (new LocalDeviceComponents { Din = din, UnavailableReason = "This signed LAN connection cannot route to another Powerwall." });
				continue;
				}
			try
				{
				var telemetry = await GetComponentsAsync (din, force, cancellationToken).ConfigureAwait (false);
				devices.Add (new LocalDeviceComponents { Din = din, Telemetry = telemetry });
				var perDevice = LocalBatteryProjection.Create (new LocalConfiguration { Batteries = new[] { battery } }, din, telemetry);
				if (blocks is not null && perDevice is not null)
					blocks = blocks.Select (block => perDevice.FirstOrDefault (candidate => candidate.PackagePartNumber == block.PackagePartNumber
						&& candidate.PackageSerialNumber == block.PackageSerialNumber) ?? block).ToArray ();
				}
			catch (Exception exc) when (!cancellationToken.IsCancellationRequested
				&& exc is PowerwallException or System.Net.Http.HttpRequestException or OperationCanceledException
					or System.Text.Json.JsonException or Google.Protobuf.InvalidProtocolBufferException)
				{
				devices.Add (new LocalDeviceComponents { Din = din, UnavailableReason = exc.Message });
				}
			}
		blocks = LocalBatteryProjection.ApplyLegacy (blocks, controller.EnergyBus?.Devices);
		return new LocalDeviceSnapshot { Configuration = config, Controller = controller, Devices = devices, Batteries = blocks };
		}
	}
