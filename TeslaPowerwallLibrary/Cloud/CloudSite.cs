// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Describes a single Tesla energy site (Powerwall or solar) available to the authenticated account.
/// Returned by <see cref="Powerwall.GetSitesAsync"/> and mirrors the upstream pypowerwall
/// <c>getsites()</c> site inventory.
/// </summary>
public sealed record CloudSite
	{
	/// <summary>Gets the Tesla energy site identifier (<c>energy_site_id</c>) used to select the site.</summary>
	public required string SiteId { get; init; }

	/// <summary>Gets the human-readable site name, when the account provides one.</summary>
	public string? SiteName { get; init; }

	/// <summary>Gets the Tesla resource type for the site (for example <c>battery</c> or <c>solar</c>).</summary>
	public string? ResourceType { get; init; }
	}
