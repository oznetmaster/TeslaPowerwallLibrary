// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;

using log4net;

using Newtonsoft.Json;

using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.FleetApi;

/// <summary>
/// File-backed cache of Tesla™ FleetAPI tokens and the selected site, keyed by customer email. This is the
/// library's equivalent of the upstream <c>pypowerwall</c> <c>.pypowerwall.fleetapi</c> config file: tokens
/// are loaded when a FleetAPI connection is created, rewritten whenever Tesla rotates them, and the selected
/// site is remembered across runs. Owning this inside the library frees every consumer from managing FleetAPI
/// token storage themselves, mirroring <see cref="TeslaCloudTokenCache"/> for cloud mode.
/// </summary>
/// <remarks>
/// The on-disk format is a JSON object keyed by email. Each entry holds the client id, access token, refresh
/// token, the selected site id, and a <c>protected</c> flag indicating whether the token values were
/// encrypted at rest (DPAPI on modern .NET running on Windows; plaintext otherwise). Reads and writes are
/// serialized with a lock because a token refresh may occur on a background polling thread.
/// </remarks>
internal sealed class TeslaFleetApiTokenCache
	{
	private static readonly ILog _log = LogManager.GetLogger (typeof (TeslaFleetApiTokenCache));

	private readonly object _gate = new ();
	private readonly string _filePath;
	private readonly string _email;
	private readonly bool _isExplicitPath;

	/// <summary>Initializes a new instance of the <see cref="TeslaFleetApiTokenCache"/> class.</summary>
	/// <param name="authPath">
	/// Directory or file path for the cache. When empty, a per-user default under the local application data
	/// folder is used and storage failures are logged but otherwise ignored. When non-empty, the location is
	/// authoritative: no fallback is attempted, and a failure to read or write it raises
	/// <see cref="PowerwallFleetApiTokenCacheStorageException"/>. When a directory is supplied, the default
	/// cache file name is appended.
	/// </param>
	/// <param name="email">The customer email the cached entry is keyed by.</param>
	public TeslaFleetApiTokenCache (string? authPath, string email)
		{
		_email = string.IsNullOrWhiteSpace (email) ? Constants.DEFAULT_EMAIL : email;
		_isExplicitPath = !string.IsNullOrWhiteSpace (authPath);
		_filePath = ResolveFilePath (authPath);
		}

	/// <summary>Gets the resolved full path to the cache file.</summary>
	public string FilePath => _filePath;

	/// <summary>Loads the cached entry for the configured email, or an empty entry when none exists.</summary>
	/// <returns>The cached tokens and site for this email.</returns>
	public FleetApiTokenCacheEntry Load ()
		{
		lock (_gate)
			{
			Dictionary<string, FleetApiTokenCacheFileEntry> root = ReadRoot ();
			if (!root.TryGetValue (_email, out FleetApiTokenCacheFileEntry? entry) || entry is null)
				return FleetApiTokenCacheEntry.Empty;

			return new FleetApiTokenCacheEntry (
				entry.ClientId,
				CloudTokenProtector.Unprotect (entry.AccessToken, entry.Protected),
				CloudTokenProtector.Unprotect (entry.RefreshToken, entry.Protected),
				entry.SiteId);
			}
		}

	/// <summary>Persists the supplied client id and tokens for the configured email, preserving any remembered site.</summary>
	/// <param name="clientId">The Tesla FleetAPI application Client ID.</param>
	/// <param name="accessToken">The current Tesla FleetAPI access token.</param>
	/// <param name="refreshToken">The current Tesla FleetAPI refresh token (may have been rotated).</param>
	public void SaveTokens (string? clientId, string? accessToken, string? refreshToken)
		{
		lock (_gate)
			{
			Dictionary<string, FleetApiTokenCacheFileEntry> root = ReadRoot ();
			root.TryGetValue (_email, out FleetApiTokenCacheFileEntry? existing);

			root[_email] = new FleetApiTokenCacheFileEntry
				{
				ClientId = string.IsNullOrWhiteSpace (clientId) ? existing?.ClientId : clientId,
				AccessToken = CloudTokenProtector.Protect (accessToken),
				RefreshToken = CloudTokenProtector.Protect (refreshToken),
				Protected = CloudTokenProtector.IsActive,
				SiteId = existing?.SiteId
				};

			WriteRoot (root);
			}
		}

	/// <summary>Persists the selected site for the configured email, preserving any stored tokens.</summary>
	/// <param name="siteId">The Tesla energy site identifier to remember, or <see langword="null"/> to clear it.</param>
	public void SaveSite (string? siteId)
		{
		lock (_gate)
			{
			Dictionary<string, FleetApiTokenCacheFileEntry> root = ReadRoot ();
			root.TryGetValue (_email, out FleetApiTokenCacheFileEntry? existing);

			root[_email] = (existing ?? new FleetApiTokenCacheFileEntry ()) with
				{
				SiteId = string.IsNullOrWhiteSpace (siteId) ? null : siteId
				};

			WriteRoot (root);
			}
		}

	private Dictionary<string, FleetApiTokenCacheFileEntry> ReadRoot ()
		{
		try
			{
			if (!File.Exists (_filePath))
				return [];

			var json = File.ReadAllText (_filePath);
			if (string.IsNullOrWhiteSpace (json))
				return [];

			return JsonConvert.DeserializeObject<Dictionary<string, FleetApiTokenCacheFileEntry>> (json) ?? [];
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or JsonException)
			{
			if (_isExplicitPath)
				{
				throw new PowerwallFleetApiTokenCacheStorageException (
					$"Unable to read the Tesla FleetAPI token cache at the explicitly configured location '{_filePath}': {exc.Message}",
					exc);
				}

			_log.Warn ($"Unable to read Tesla FleetAPI token cache '{_filePath}': {exc.Message}");
			return [];
			}
		}

	private void WriteRoot (Dictionary<string, FleetApiTokenCacheFileEntry> root)
		{
		try
			{
			var directory = Path.GetDirectoryName (_filePath);
			if (!string.IsNullOrEmpty (directory))
				Directory.CreateDirectory (directory!);

			File.WriteAllText (_filePath, JsonConvert.SerializeObject (root, Formatting.Indented));
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
			{
			if (_isExplicitPath)
				{
				throw new PowerwallFleetApiTokenCacheStorageException (
					$"Unable to write the Tesla FleetAPI token cache at the explicitly configured location '{_filePath}': {exc.Message}",
					exc);
				}

			// Persisting tokens at the default per-user location is best-effort; a failure here must not
			// disrupt an active connection.
			_log.Warn ($"Unable to write Tesla FleetAPI token cache '{_filePath}': {exc.Message}");
			}
		}

	private static string ResolveFilePath (string? authPath)
		{
		if (string.IsNullOrWhiteSpace (authPath))
			{
			var localAppData = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);
			return Path.Combine (localAppData, Constants.DEFAULT_CLOUD_AUTH_FOLDER, Constants.DEFAULT_FLEETAPI_AUTH_FILE);
			}

		// A path that exists as a directory, or that has no file extension, is treated as a folder and the
		// default cache file name is appended; otherwise the path is used verbatim as the cache file.
		if (Directory.Exists (authPath) || string.IsNullOrEmpty (Path.GetExtension (authPath)))
			return Path.Combine (authPath!, Constants.DEFAULT_FLEETAPI_AUTH_FILE);

		return authPath!;
		}
	}

/// <summary>Immutable snapshot of the client id, tokens, and site cached for a single email.</summary>
internal sealed class FleetApiTokenCacheEntry
	{
	/// <summary>A shared empty entry used when no cached data exists.</summary>
	public static readonly FleetApiTokenCacheEntry Empty = new (null, null, null, null);

	/// <summary>Initializes a new instance of the <see cref="FleetApiTokenCacheEntry"/> class.</summary>
	/// <param name="clientId">The cached Tesla FleetAPI application Client ID, if any.</param>
	/// <param name="accessToken">The cached access token, if any.</param>
	/// <param name="refreshToken">The cached refresh token, if any.</param>
	/// <param name="siteId">The cached site identifier, if any.</param>
	public FleetApiTokenCacheEntry (string? clientId, string? accessToken, string? refreshToken, string? siteId)
		{
		ClientId = clientId;
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		SiteId = siteId;
		}

	/// <summary>Gets the cached Tesla FleetAPI application Client ID, or <see langword="null"/> when absent.</summary>
	public string? ClientId { get; }

	/// <summary>Gets the cached Tesla FleetAPI access token, or <see langword="null"/> when absent.</summary>
	public string? AccessToken { get; }

	/// <summary>Gets the cached Tesla FleetAPI refresh token, or <see langword="null"/> when absent.</summary>
	public string? RefreshToken { get; }

	/// <summary>Gets the cached Tesla energy site identifier, or <see langword="null"/> when absent.</summary>
	public string? SiteId { get; }

	/// <summary>Gets a value indicating whether any token is present in this entry.</summary>
	public bool HasToken => !string.IsNullOrWhiteSpace (AccessToken) || !string.IsNullOrWhiteSpace (RefreshToken);
	}

/// <summary>The on-disk shape of a single cached email's entry within the Tesla FleetAPI token cache file.</summary>
internal sealed record FleetApiTokenCacheFileEntry
	{
	/// <summary>The Tesla FleetAPI application Client ID.</summary>
	[JsonProperty ("client_id")]
	public string? ClientId { get; init; }

	/// <summary>The access token, protected at rest when <see cref="Protected"/> is <see langword="true"/>.</summary>
	[JsonProperty ("access_token")]
	public string? AccessToken { get; init; }

	/// <summary>The refresh token, protected at rest when <see cref="Protected"/> is <see langword="true"/>.</summary>
	[JsonProperty ("refresh_token")]
	public string? RefreshToken { get; init; }

	/// <summary>Indicates whether <see cref="AccessToken"/> and <see cref="RefreshToken"/> were DPAPI-protected when written.</summary>
	[JsonProperty ("protected")]
	public bool Protected { get; init; }

	/// <summary>The remembered Tesla energy site identifier, when one has been selected.</summary>
	[JsonProperty ("site_id")]
	public string? SiteId { get; init; }
	}
