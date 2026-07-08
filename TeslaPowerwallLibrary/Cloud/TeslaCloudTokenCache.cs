// Copyright © 2026 Neil Colvin.
// Adapted from the Python teslapy/pypowerwall projects Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;

using log4net;

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// File-backed cache of Tesla™ Owners API tokens and the selected site, keyed by customer email. This is the
/// library's internal equivalent of the upstream <c>teslapy</c> cache file plus the <c>pypowerwall</c> site
/// persistence: tokens are loaded when a cloud connection is created, rewritten whenever Tesla rotates them,
/// and the selected site is remembered across runs. Owning this inside the library frees every consumer from
/// managing token storage themselves.
/// </summary>
/// <remarks>
/// The on-disk format is a JSON object keyed by email. Each entry holds the access token, refresh token, the
/// selected site id, and a <c>protected</c> flag indicating whether the token values were encrypted at rest
/// (DPAPI on modern .NET running on Windows; plaintext otherwise). Reads and writes are serialized with a
/// lock because a token refresh may occur on a background polling thread.
/// </remarks>
internal sealed class TeslaCloudTokenCache
	{
	private static readonly ILog _log = LogManager.GetLogger (typeof (TeslaCloudTokenCache));

	private readonly object _gate = new ();
	private readonly string _filePath;
	private readonly string _email;
	private readonly bool _isExplicitPath;

	/// <summary>Initializes a new instance of the <see cref="TeslaCloudTokenCache"/> class.</summary>
	/// <param name="authPath">
	/// Directory or file path for the cache. When empty, a per-user default under the local application data
	/// folder is used and storage failures are logged but otherwise ignored. When non-empty, the location is
	/// authoritative: no fallback is attempted, and a failure to read or write it raises
	/// <see cref="PowerwallCloudTokenCacheStorageException"/>. When a directory is supplied, the default
	/// cache file name is appended.
	/// </param>
	/// <param name="email">The customer email the cached entry is keyed by.</param>
	public TeslaCloudTokenCache (string? authPath, string email)
		{
		_email = string.IsNullOrWhiteSpace (email) ? Constants.DEFAULT_EMAIL : email;
		_isExplicitPath = !string.IsNullOrWhiteSpace (authPath);
		_filePath = ResolveFilePath (authPath);
		}

	/// <summary>Gets the resolved full path to the cache file.</summary>
	public string FilePath => _filePath;

	/// <summary>Loads the cached entry for the configured email, or an empty entry when none exists.</summary>
	/// <returns>The cached tokens and site for this email.</returns>
	public CloudTokenCacheEntry Load ()
		{
		lock (_gate)
			{
			Dictionary<string, CloudTokenCacheFileEntry> root = ReadRoot ();
			if (!root.TryGetValue (_email, out CloudTokenCacheFileEntry? entry) || entry is null)
				return CloudTokenCacheEntry.Empty;

			return new CloudTokenCacheEntry (
				CloudTokenProtector.Unprotect (entry.AccessToken, entry.Protected),
				CloudTokenProtector.Unprotect (entry.RefreshToken, entry.Protected),
				entry.SiteId);
			}
		}

	/// <summary>Persists the supplied tokens for the configured email, preserving any remembered site.</summary>
	/// <param name="accessToken">The current Tesla Owners API access token.</param>
	/// <param name="refreshToken">The current Tesla Owners API refresh token (may have been rotated).</param>
	public void SaveTokens (string? accessToken, string? refreshToken)
		{
		lock (_gate)
			{
			Dictionary<string, CloudTokenCacheFileEntry> root = ReadRoot ();
			root.TryGetValue (_email, out CloudTokenCacheFileEntry? existing);

			root[_email] = new CloudTokenCacheFileEntry
				{
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
			Dictionary<string, CloudTokenCacheFileEntry> root = ReadRoot ();
			root.TryGetValue (_email, out CloudTokenCacheFileEntry? existing);

			root[_email] = (existing ?? new CloudTokenCacheFileEntry ()) with
				{
				SiteId = string.IsNullOrWhiteSpace (siteId) ? null : siteId
				};

			WriteRoot (root);
			}
		}

	private Dictionary<string, CloudTokenCacheFileEntry> ReadRoot ()
		{
		try
			{
			if (!File.Exists (_filePath))
				return [];

			var json = File.ReadAllText (_filePath);
			if (string.IsNullOrWhiteSpace (json))
				return [];

			return JsonConvert.DeserializeObject<Dictionary<string, CloudTokenCacheFileEntry>> (json) ?? [];
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or JsonException)
			{
			if (_isExplicitPath)
				{
				throw new PowerwallCloudTokenCacheStorageException (
					$"Unable to read the Tesla cloud token cache at the explicitly configured location '{_filePath}': {exc.Message}",
					exc);
				}

			_log.Warn ($"Unable to read Tesla cloud token cache '{_filePath}': {exc.Message}");
			return [];
			}
		}

	private void WriteRoot (Dictionary<string, CloudTokenCacheFileEntry> root)
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
				throw new PowerwallCloudTokenCacheStorageException (
					$"Unable to write the Tesla cloud token cache at the explicitly configured location '{_filePath}': {exc.Message}",
					exc);
				}

			// Persisting tokens at the default per-user location is best-effort; a failure here must not
			// disrupt an active connection.
			_log.Warn ($"Unable to write Tesla cloud token cache '{_filePath}': {exc.Message}");
			}
		}

	private static string ResolveFilePath (string? authPath)
		{
		if (string.IsNullOrWhiteSpace (authPath))
			{
			var localAppData = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);
			return Path.Combine (localAppData, Constants.DEFAULT_CLOUD_AUTH_FOLDER, Constants.DEFAULT_CLOUD_AUTH_FILE);
			}

		// A path that exists as a directory, or that has no file extension, is treated as a folder and the
		// default cache file name is appended; otherwise the path is used verbatim as the cache file.
		if (Directory.Exists (authPath) || string.IsNullOrEmpty (Path.GetExtension (authPath)))
			return Path.Combine (authPath!, Constants.DEFAULT_CLOUD_AUTH_FILE);

		return authPath!;
		}
	}

/// <summary>Immutable snapshot of the tokens and site cached for a single email.</summary>
internal sealed class CloudTokenCacheEntry
	{
	/// <summary>A shared empty entry used when no cached data exists.</summary>
	public static readonly CloudTokenCacheEntry Empty = new (null, null, null);

	/// <summary>Initializes a new instance of the <see cref="CloudTokenCacheEntry"/> class.</summary>
	/// <param name="accessToken">The cached access token, if any.</param>
	/// <param name="refreshToken">The cached refresh token, if any.</param>
	/// <param name="siteId">The cached site identifier, if any.</param>
	public CloudTokenCacheEntry (string? accessToken, string? refreshToken, string? siteId)
		{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		SiteId = siteId;
		}

	/// <summary>Gets the cached Tesla Owners API access token, or <see langword="null"/> when absent.</summary>
	public string? AccessToken { get; }

	/// <summary>Gets the cached Tesla Owners API refresh token, or <see langword="null"/> when absent.</summary>
	public string? RefreshToken { get; }

	/// <summary>Gets the cached Tesla energy site identifier, or <see langword="null"/> when absent.</summary>
	public string? SiteId { get; }

	/// <summary>Gets a value indicating whether any token is present in this entry.</summary>
	public bool HasToken => !string.IsNullOrWhiteSpace (AccessToken) || !string.IsNullOrWhiteSpace (RefreshToken);
	}

/// <summary>The on-disk shape of a single cached email's entry within the Tesla cloud token cache file.</summary>
internal sealed record CloudTokenCacheFileEntry
	{
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
