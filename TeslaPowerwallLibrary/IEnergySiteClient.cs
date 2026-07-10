// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary;

/// <summary>
/// Shared contract for backend clients that operate against a Tesla™ energy site inventory (site
/// selection, grid charging/export configuration, and energy history). Implemented by both
/// <see cref="Cloud.PowerwallCloudClient"/> and the Tesla FleetAPI backend client, allowing
/// <see cref="Powerwall"/> to expose these operations identically regardless of which backend is active.
/// </summary>
internal interface IEnergySiteClient
	{
	/// <summary>
	/// Returns the list of Tesla energy sites (Powerwall and solar) available to the authenticated account.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The available sites, or an empty list when none are found.</returns>
	Task<IReadOnlyList<CloudSite>> GetSitesAsync (CancellationToken cancellationToken = default);

	/// <summary>
	/// Switches the active site to the one matching <paramref name="siteId"/> without reconnecting.
	/// </summary>
	/// <param name="siteId">The Tesla energy site identifier to switch to.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the site was found and selected; otherwise <see langword="false"/>.</returns>
	Task<bool> ChangeSiteAsync (string siteId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Enables or disables charging the battery from the grid.
	/// </summary>
	/// <param name="enabled"><see langword="true"/> to allow grid charging; <see langword="false"/> to disallow it.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when the call fails.</returns>
	Task<string?> SetGridChargingAsync (bool enabled, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns whether charging the battery from the grid is currently allowed.
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when grid charging is allowed, <see langword="false"/> when disallowed, or <see langword="null"/> when unavailable.</returns>
	Task<bool?> GetGridChargingAsync (bool force = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the grid export rule.
	/// </summary>
	/// <param name="mode">The export rule: <c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw response body, or <see langword="null"/> when the call fails.</returns>
	Task<string?> SetGridExportAsync (string mode, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the current grid export rule.
	/// </summary>
	/// <param name="force">When <see langword="true"/>, bypasses the cache.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The export rule (<c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>), or <see langword="null"/> when unavailable.</returns>
	Task<string?> GetGridExportAsync (bool force = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns raw energy history for the active site.
	/// </summary>
	/// <param name="kind">The history kind (for example <c>power</c>, <c>energy</c>, <c>backup</c>, or <c>self_consumption</c>).</param>
	/// <param name="period">The aggregation period (for example <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>).</param>
	/// <param name="timeZone">IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	Task<string?> GetHistoryAsync (
		string? kind = null,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns raw calendar-aligned energy history for the active site.
	/// </summary>
	/// <param name="kind">The history kind (for example <c>power</c>, <c>soe</c>, <c>energy</c>, <c>backup</c>, <c>self_consumption</c>, <c>time_of_use_energy</c>, or <c>savings</c>).</param>
	/// <param name="period">The aggregation period (for example <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>).</param>
	/// <param name="timeZone">IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	Task<string?> GetCalendarHistoryAsync (
		string? kind = null,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default);
	}
