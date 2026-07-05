// Copyright © 2026 Neil Colvin.
// Adapted from the Python teslapy/pypowerwall projects Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;

using log4net;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// File-backed cache of Tesla Owners API tokens and the selected site, keyed by customer email. This is the
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

	/// <summary>Initializes a new instance of the <see cref="TeslaCloudTokenCache"/> class.</summary>
	/// <param name="authPath">
	/// Directory or file path for the cache. When empty, a per-user default under the local application data
	/// folder is used. When a directory is supplied, the default cache file name is appended.
	/// </param>
	/// <param name="email">The customer email the cached entry is keyed by.</param>
	public TeslaCloudTokenCache (string? authPath, string email)
		{
		_email = string.IsNullOrWhiteSpace (email) ? Constants.DEFAULT_EMAIL : email;
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
			JObject root = ReadRoot ();
			if (root[_email] is not JObject entry)
				return CloudTokenCacheEntry.Empty;

			var wasProtected = entry.Value<bool?> ("protected") ?? false;
			return new CloudTokenCacheEntry (
				CloudTokenProtector.Unprotect (entry.Value<string> ("access_token"), wasProtected),
				CloudTokenProtector.Unprotect (entry.Value<string> ("refresh_token"), wasProtected),
				entry.Value<string> ("site_id"));
			}
		}

	/// <summary>Persists the supplied tokens for the configured email, preserving any remembered site.</summary>
	/// <param name="accessToken">The current Tesla Owners API access token.</param>
	/// <param name="refreshToken">The current Tesla Owners API refresh token (may have been rotated).</param>
	public void SaveTokens (string? accessToken, string? refreshToken)
		{
		lock (_gate)
			{
			JObject root = ReadRoot ();
			JObject entry = root[_email] as JObject ?? new JObject ();

			entry["access_token"] = CloudTokenProtector.Protect (accessToken);
			entry["refresh_token"] = CloudTokenProtector.Protect (refreshToken);
			entry["protected"] = CloudTokenProtector.IsActive;

			root[_email] = entry;
			WriteRoot (root);
			}
		}

	/// <summary>Persists the selected site for the configured email, preserving any stored tokens.</summary>
	/// <param name="siteId">The Tesla energy site identifier to remember, or <see langword="null"/> to clear it.</param>
	public void SaveSite (string? siteId)
		{
		lock (_gate)
			{
			JObject root = ReadRoot ();
			JObject entry = root[_email] as JObject ?? new JObject ();

			if (string.IsNullOrWhiteSpace (siteId))
				entry.Remove ("site_id");
			else
				entry["site_id"] = siteId;

			root[_email] = entry;
			WriteRoot (root);
			}
		}

	private JObject ReadRoot ()
		{
		try
			{
			if (!File.Exists (_filePath))
				return new JObject ();

			var json = File.ReadAllText (_filePath);
			return string.IsNullOrWhiteSpace (json) ? new JObject () : JObject.Parse (json);
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or JsonException)
			{
			_log.Warn ($"Unable to read Tesla cloud token cache '{_filePath}': {exc.Message}");
			return new JObject ();
			}
		}

	private void WriteRoot (JObject root)
		{
		try
			{
			var directory = Path.GetDirectoryName (_filePath);
			if (!string.IsNullOrEmpty (directory))
				Directory.CreateDirectory (directory!);

			File.WriteAllText (_filePath, root.ToString (Formatting.Indented));
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
			{
			// Persisting tokens is best-effort; a failure here must not disrupt an active connection.
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
