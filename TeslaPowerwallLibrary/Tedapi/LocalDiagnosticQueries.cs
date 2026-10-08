// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tedapi;

public sealed partial class PowerwallTedapiClient
	{
	/// <summary>Reads IEEE 2030.5 service metadata without starting or changing a procedure.</summary>
	/// <remarks>Uses the captured June 2026 supplemental vendor query independently of the regular telemetry query version.
	/// Firmware must accept that query. No cloud fallback, mutation or automatic procedure is performed.</remarks>
	/// <param name="force">Bypasses cached results but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>Reported metadata, or null when the device returns no section.</returns>
	public async Task<LocalIeee20305Telemetry?> GetIeee20305Async (bool force = false, CancellationToken cancellationToken = default) =>
		(await ReadQueryAsync<LocalTelemetry> ("IEEE20305Query", force, cancellationToken).ConfigureAwait (false)).Ieee20305;

	/// <summary>Reads stored inverter self-test status and results without starting or changing a procedure.</summary>
	/// <remarks>Uses the captured June 2026 supplemental vendor query independently of the regular telemetry query version.
	/// Firmware must accept that query. No cloud fallback, mutation or automatic procedure is performed.</remarks>
	/// <param name="force">Bypasses cached results but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>Reported metadata, or null when the device returns no section.</returns>
	public async Task<LocalInverterSelfTests?> GetInverterSelfTestsAsync (bool force = false, CancellationToken cancellationToken = default) =>
		(await ReadQueryAsync<LocalTelemetry> ("PinvSelfTestQuery", force, cancellationToken).ConfigureAwait (false)).EnergyBus?.InverterSelfTests;

	/// <summary>Reads stored protection-test status and results without starting or changing a procedure.</summary>
	/// <remarks>Uses the captured June 2026 supplemental vendor query independently of the regular telemetry query version.
	/// Firmware must accept that query. No cloud fallback, mutation or automatic procedure is performed.</remarks>
	/// <param name="force">Bypasses cached results but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>Reported metadata, or null when the device returns no section.</returns>
	public async Task<LocalProtectionTestStatus?> GetProtectionTestStatusAsync (bool force = false, CancellationToken cancellationToken = default) =>
		(await ReadQueryAsync<LocalTelemetry> ("ProtectionTripTestQuery", force, cancellationToken).ConfigureAwait (false)).Control?.ProtectionTripTests;

	}
