// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>The cloud history site belonging to one authenticated local device.</summary>
public sealed record LocalHistorySite
	{
	/// <summary>Gets the permanent Tesla energy-site identifier.</summary>
	[JsonPropertyName ("siteId")]
	public string SiteId { get; init; } = string.Empty;
	/// <summary>Gets the site's display name.</summary>
	[JsonPropertyName ("siteName")]
	public string? SiteName { get; init; }
	}

/// <summary>Maintains hardware identity independently of cloud provider and account selection.</summary>
internal static class LocalHistoryBinding
	{
	/// <summary>Finds a known physical site's association using an authenticated device identifier.</summary>
	/// <param name="settings">Saved application settings.</param>
	/// <param name="deviceId">The identifier returned by the local connection.</param>
	/// <returns>The existing site, or null when this device has not been associated.</returns>
	internal static LocalHistorySite? Find (AppSettings settings, string? deviceId) =>
		deviceId is not null && settings.LocalHistorySites.TryGetValue (deviceId, out var site) ? site : null;

	/// <summary>Records the first association and refuses to silently attach this device to another site.</summary>
	/// <param name="settings">Application settings to update.</param>
	/// <param name="deviceId">Authenticated hardware identity; an address alone is insufficient.</param>
	/// <param name="site">Explicitly confirmed physical site.</param>
	/// <exception cref="InvalidOperationException">Identity is missing, or this device is already associated with another site.</exception>
	internal static void Bind (AppSettings settings, string? deviceId, LocalHistorySite site)
		{
		if (string.IsNullOrWhiteSpace (deviceId) || string.IsNullOrWhiteSpace (site.SiteId))
			throw new InvalidOperationException ("A local device identity and cloud site are required before linking history.");
		var existing = Find (settings, deviceId);
		if (existing is not null && existing.SiteId != site.SiteId)
			throw new InvalidOperationException ("This local Powerwall already belongs to a different history site.");
		settings.LocalHistorySites[deviceId] = site;
		}
	}
