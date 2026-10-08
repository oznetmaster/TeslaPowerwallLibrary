// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Collections.Generic;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// Persisted connection settings for the desktop app. The local password is stored encrypted with DPAPI and
/// the file lives under <c>%LocalAppData%</c>, outside the repository, so it is never committable. Cloud
/// Owner tokens are persisted by the library. These settings also retain protected Fleet credentials,
/// non-secret connection defaults, and confirmed hardware-to-cloud-site associations for history.
/// </summary>
public sealed class AppSettings
	{
	/// <summary>Gets or sets permanent hardware-to-site associations, independent of history credentials.</summary>
	[JsonPropertyName ("localHistorySites")]
	public Dictionary<string, LocalHistorySite> LocalHistorySites { get; set; } = new ();

	/// <summary>Gets or sets the cloud provider used only for local-mode history (Cloud or FleetApi).</summary>
	[JsonPropertyName ("historyMode")]
	public string? HistoryMode { get; set; }

	/// <summary>Gets or sets the explicitly associated local host for the history site.</summary>
	[JsonPropertyName ("historyHost")]
	public string? HistoryHost { get; set; }

	/// <summary>Gets or sets the account scope associated with the history site.</summary>
	[JsonPropertyName ("historyAccount")]
	public string? HistoryAccount { get; set; }

	/// <summary>Gets or sets the explicitly selected cloud history site identifier.</summary>
	[JsonPropertyName ("historySiteId")]
	public string? HistorySiteId { get; set; }

	/// <summary>Gets or sets the display name of the selected cloud history site.</summary>
	[JsonPropertyName ("historySiteName")]
	public string? HistorySiteName { get; set; }

	/// <summary>Gets or sets the last connection mode used (<c>Cloud</c> or <c>Local</c>).</summary>
	[JsonPropertyName ("mode")]
	public string? Mode { get; set; }

	/// <summary>Gets or sets the gateway host name or IP address for local mode.</summary>
	[JsonPropertyName ("host")]
	public string? Host { get; set; }

	/// <summary>Gets or sets the encrypted (DPAPI, base64) Powerwall™ password; never stored in plaintext.</summary>
	[JsonPropertyName ("protectedPassword")]
	public string? ProtectedPassword { get; set; }

	/// <summary>Gets or sets the desktop's local refresh interval; zero means manual refresh only.</summary>
	[JsonPropertyName ("localPollSeconds")]
	public int LocalPollSeconds { get; set; } = 5;

	/// <summary>Gets or sets the selected local transport.</summary>
	[JsonPropertyName ("localProtocol")]
	public PowerwallLocalProtocol LocalProtocol { get; set; }

	/// <summary>Gets or sets the explicit local vendor-signed query version.</summary>
	[JsonPropertyName ("localQueryVersion")]
	public Tedapi.TedapiQueryVersion LocalQueryVersion { get; set; }

	/// <summary>Gets or sets the existing Windows signing-key name; private key material is never stored here.</summary>
	[JsonPropertyName ("localSigningKeyName")]
	public string? LocalSigningKeyName { get; set; }

	/// <summary>Gets or sets the customer email for cloud mode.</summary>
	[JsonPropertyName ("email")]
	public string? Email { get; set; }

	/// <summary>Gets or sets the Tesla region (<c>us</c> or <c>cn</c>) used by the browser sign-in flow.</summary>
	[JsonPropertyName ("region")]
	public string? Region { get; set; }

	/// <summary>Gets or sets the Tesla FleetAPI application Client ID.</summary>
	[JsonPropertyName ("fleetApiClientId")]
	public string? FleetApiClientId { get; set; }

	/// <summary>
	/// Gets or sets the encrypted (DPAPI, base64) Tesla FleetAPI refresh token; never stored in plaintext.
	/// The library now also persists FleetAPI tokens internally, but the app keeps its own copy here
	/// (mirroring <see cref="ProtectedPassword"/>) so the initial sign-in value is remembered even before a
	/// successful connect populates the library's own cache.
	/// </summary>
	[JsonPropertyName ("protectedFleetApiRefreshToken")]
	public string? ProtectedFleetApiRefreshToken { get; set; }

	/// <summary>Gets or sets the Tesla FleetAPI region (<c>na</c>, <c>eu</c>, or <c>cn</c>).</summary>
	[JsonPropertyName ("fleetApiRegion")]
	public string? FleetApiRegion { get; set; }
	}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> from a per-user, non-repository location
/// (<c>%LocalAppData%\TeslaPowerwallLibrary\app.settings.json</c>).
/// </summary>
public static class AppSettingsStore
	{
	/// <summary>Gets the full path to the settings file under the user's local application data folder.</summary>
	public static string FilePath { get; } = BuildFilePath ();

	/// <summary>Loads persisted settings, or returns an empty instance when none exist or the file is unreadable.</summary>
	/// <returns>The loaded settings.</returns>
	public static AppSettings Load ()
		{
		try
			{
			if (!File.Exists (FilePath))
				return new AppSettings ();

			var json = File.ReadAllText (FilePath);
			return JsonSerializer.Deserialize<AppSettings> (json) ?? new AppSettings ();
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or JsonException)
			{
			return new AppSettings ();
			}
		}

	/// <summary>Persists the supplied settings to the per-user settings file, creating the directory as needed.</summary>
	/// <param name="settings">The settings to persist.</param>
	public static void Save (AppSettings settings)
		{
		if (settings is null)
			throw new ArgumentNullException (nameof (settings));

		try
			{
			var directory = Path.GetDirectoryName (FilePath);
			if (!string.IsNullOrEmpty (directory))
				Directory.CreateDirectory (directory!);

			var json = JsonSerializer.Serialize (settings, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText (FilePath, json);
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
			{
			// Persisting settings is best-effort; a failure here must not crash the app.
			}
		}

	private static string BuildFilePath ()
		{
		var localAppData = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);
		return Path.Combine (localAppData, "TeslaPowerwallLibrary", "app.settings.json");
		}
	}