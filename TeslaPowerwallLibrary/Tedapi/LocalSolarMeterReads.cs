// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

public sealed partial class PowerwallTedapiClient
	{
	private Task<MeterAggregates> GetSynthesizedMeterAggregatesAsync (bool force, CancellationToken cancellationToken) =>
		ReadWithFailoverAsync (recovering => GetPrimaryMeterAggregatesAsync (force || recovering, cancellationToken),
			fallback => fallback.GetSynthesizedMeterAggregatesAsync (force, cancellationToken), cancellationToken);

	private async Task<MeterAggregates> GetPrimaryMeterAggregatesAsync (bool force, CancellationToken cancellationToken)
		{
		// Keep the whole read on one route. Nested public reads would reacquire the failover gate.
		var status = await ReadPrimaryQueryAsync<LocalTelemetry> ("device_controller_basic", force, null, cancellationToken).ConfigureAwait (false);
		var basis = new MeterAggregates
			{
			Site = Reading (status, "SITE"), Solar = Reading (status, "SOLAR"),
			Battery = Reading (status, "BATTERY"), Load = Reading (status, "LOAD")
			};
		if (!LocalSolarMeterHold.IsCandidate (status)) return basis;
		try
			{
			var configuration = await GetPrimaryConfigurationAsync (force, cancellationToken).ConfigureAwait (false);
			var detailed = await ReadPrimaryQueryAsync<LocalTelemetry> ("device_controller_full", force, null, cancellationToken).ConfigureAwait (false);
			return LocalSolarMeterHold.Apply (basis, configuration, status, detailed);
			}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested
			&& error is PowerwallException or HttpRequestException or OperationCanceledException)
			{
			// Optional enrichment cannot discard a successful controller read or silently switch its source.
			if (_options.Logger is { } logger) LibraryLog.SolarMeterHoldUnavailable (logger, error);
			return basis;
			}
		}
	}