// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Persisted connection settings for the test console. The password is stored encrypted (DPAPI) and the
/// file lives under <c>%LocalAppData%</c>, outside the repository, so it is never committable or pushable.
/// Cloud tokens and the selected site are not stored here: the library owns and persists them internally
/// (keyed by email), so the console only remembers non-secret connection defaults plus the local password.
/// </summary>
internal sealed class ConsoleSettings
	{
	/// <summary>Gateway host name or IP address to default on the next run.</summary>
	[JsonProperty ("host")]
	public string? Host { get; set; }

	/// <summary>Encrypted (DPAPI, base64) Powerwall password; never stored in plaintext.</summary>
	[JsonProperty ("protectedPassword")]
	public string? ProtectedPassword { get; set; }

	/// <summary>Customer email to default on the next run.</summary>
	[JsonProperty ("email")]
	public string? Email { get; set; }

	/// <summary>IANA time zone to default on the next run.</summary>
	[JsonProperty ("timezone")]
	public string? Timezone { get; set; }

	/// <summary>Per-request HTTP timeout, in seconds, to default on the next run.</summary>
	[JsonProperty ("timeoutSeconds")]
	public int? TimeoutSeconds { get; set; }

	/// <summary>Cached response expiry, in seconds, to default on the next run.</summary>
	[JsonProperty ("cacheExpireSeconds")]
	public int? CacheExpireSeconds { get; set; }

	/// <summary>Tesla region (<c>us</c> or <c>cn</c>) to default for the cloud browser login.</summary>
	[JsonProperty ("region")]
	public string? Region { get; set; }

	/// <summary>Tesla FleetAPI application Client ID to default on the next run.</summary>
	[JsonProperty ("fleetApiClientId")]
	public string? FleetApiClientId { get; set; }

	/// <summary>
	/// Encrypted (DPAPI, base64) Tesla FleetAPI refresh token; never stored in plaintext. FleetAPI mode has no
	/// library-owned token cache, so the console persists this itself (mirroring <see cref="ProtectedPassword"/>)
	/// so the caller does not have to supply it on every run.
	/// </summary>
	[JsonProperty ("protectedFleetApiRefreshToken")]
	public string? ProtectedFleetApiRefreshToken { get; set; }

	/// <summary>Tesla FleetAPI region (<c>na</c>, <c>eu</c>, or <c>cn</c>) to default on the next run.</summary>
	[JsonProperty ("fleetApiRegion")]
	public string? FleetApiRegion { get; set; }

	/// <summary>
	/// Whether the last session used Tesla FleetAPI mode, so it is remembered as the default connection mode
	/// on the next run even without a host configured (which would otherwise default to Tesla Owners cloud mode).
	/// </summary>
	[JsonProperty ("preferFleetApi")]
	public bool? PreferFleetApi { get; set; }
	}

/// <summary>
/// Loads and saves <see cref="ConsoleSettings"/> from a per-user, non-repository location
/// (<c>%LocalAppData%\TeslaPowerwallLibrary\testconsole.settings.json</c>).
/// </summary>
internal static class SettingsStore
	{
	/// <summary>Gets the full path to the settings file under the user's local application data folder.</summary>
	public static string FilePath { get; } = BuildFilePath ();

	/// <summary>Loads persisted settings, or returns an empty instance when none exist or the file is unreadable.</summary>
	public static ConsoleSettings Load ()
		{
		try
			{
			if (!File.Exists (FilePath))
				return new ConsoleSettings ();

			var json = File.ReadAllText (FilePath);
			return JsonConvert.DeserializeObject<ConsoleSettings> (json) ?? new ConsoleSettings ();
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or JsonException)
			{
			ConsoleHelpers.WriteError ($"Could not read settings ({exc.Message}); using defaults.");
			return new ConsoleSettings ();
			}
		}

	/// <summary>Persists the supplied settings to the per-user settings file, creating the directory as needed.</summary>
	public static void Save (ConsoleSettings settings)
		{
		try
			{
			var directory = Path.GetDirectoryName (FilePath);
			if (!string.IsNullOrEmpty (directory))
				Directory.CreateDirectory (directory!);

			var json = JsonConvert.SerializeObject (settings, Formatting.Indented);
			File.WriteAllText (FilePath, json);
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
			{
			ConsoleHelpers.WriteError ($"Could not save settings: {exc.Message}");
			}
		}

	/// <summary>Deletes the persisted settings file, if present.</summary>
	public static void Clear ()
		{
		try
			{
			if (File.Exists (FilePath))
				File.Delete (FilePath);
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
			{
			ConsoleHelpers.WriteError ($"Could not clear settings: {exc.Message}");
			}
		}

	private static string BuildFilePath ()
		{
		var localAppData = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);
		return Path.Combine (localAppData, "TeslaPowerwallLibrary", "testconsole.settings.json");
		}
	}
