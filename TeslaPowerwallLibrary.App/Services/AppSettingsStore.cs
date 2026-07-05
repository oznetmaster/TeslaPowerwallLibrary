// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// Persisted connection settings for the desktop app. The local password is stored encrypted with DPAPI and
/// the file lives under <c>%LocalAppData%</c>, outside the repository, so it is never committable. Cloud
/// tokens and the selected site are not stored here: the library owns and persists them internally (keyed by
/// email), so the app only remembers non-secret connection defaults plus the local password.
/// </summary>
public sealed class AppSettings
	{
	/// <summary>Gets or sets the last connection mode used (<c>Cloud</c> or <c>Local</c>).</summary>
	[JsonProperty ("mode")]
	public string? Mode { get; set; }

	/// <summary>Gets or sets the gateway host name or IP address for local mode.</summary>
	[JsonProperty ("host")]
	public string? Host { get; set; }

	/// <summary>Gets or sets the encrypted (DPAPI, base64) Powerwall™ password; never stored in plaintext.</summary>
	[JsonProperty ("protectedPassword")]
	public string? ProtectedPassword { get; set; }

	/// <summary>Gets or sets the customer email for cloud mode.</summary>
	[JsonProperty ("email")]
	public string? Email { get; set; }

	/// <summary>Gets or sets the Tesla region (<c>us</c> or <c>cn</c>) used by the browser sign-in flow.</summary>
	[JsonProperty ("region")]
	public string? Region { get; set; }
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
			return JsonConvert.DeserializeObject<AppSettings> (json) ?? new AppSettings ();
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

			var json = JsonConvert.SerializeObject (settings, Formatting.Indented);
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
