// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>Owns a separate, on-demand cloud connection for history while live readings remain local.</summary>
internal sealed class CloudHistorySource : IDisposable
	{
	private Powerwall? _client;
	private string? _scope;

	/// <summary>Gets a credential-free provider/account scope for cache and site associations.</summary>
	/// <param name="provider">Owner or Fleet.</param>
	/// <param name="settings">Saved desktop settings.</param>
	/// <returns>The account scope.</returns>
	internal static string AccountScope (string provider, AppSettings settings) =>
		string.Join ("|", provider, (provider == "Owner" ? ResolveOwnerEmail (settings) : settings.Email)?.Trim ().ToLowerInvariant (),
			provider == "Fleet" ? settings.FleetApiClientId : null,
			provider == "Fleet" ? settings.FleetApiRegion : settings.Region);

	/// <summary>Lists cloud sites only after the user requests history setup.</summary>
	/// <param name="provider">Owner or Fleet.</param>
	/// <param name="cancellationToken">Cancels cloud access.</param>
	/// <returns>Sites for explicit association with the local system.</returns>
	internal async Task<IReadOnlyList<CloudSite>> GetSitesAsync (string provider, CancellationToken cancellationToken)
		{
		var client = await ConnectAsync (provider, cancellationToken).ConfigureAwait (false);
		return await client.GetSitesAsync (cancellationToken).ConfigureAwait (false);
		}

	/// <summary>Fetches a selected history window, without polling or changing the live connection.</summary>
	/// <param name="provider">Owner or Fleet.</param>
	/// <param name="request">The selected site and history range.</param>
	/// <param name="period">Typed cloud aggregation period.</param>
	/// <param name="cancellationToken">Cancels cloud access.</param>
	/// <returns>Typed cloud energy samples.</returns>
	internal async Task<IReadOnlyList<EnergyHistoryPoint>> FetchAsync (string provider, EnergyHistoryRequest request,
		HistoryPeriod period, CancellationToken cancellationToken)
		{
		if (AccountScope (provider, AppSettingsStore.Load ()) != request.Account)
			throw new InvalidOperationException ("History credentials changed. Select the history account and site again.");
		var client = await ConnectAsync (provider, cancellationToken).ConfigureAwait (false);
		if (client.CloudSiteId != request.Site)
			await client.ChangeSiteAsync (request.Site, cancellationToken).ConfigureAwait (false);
		return await client.GetEnergyCalendarHistoryAsync (period, request.Timezone,
			request.Start?.ToString ("O"), request.End?.ToString ("O"), cancellationToken).ConfigureAwait (false);
		}

	private async Task<Powerwall> ConnectAsync (string provider, CancellationToken cancellationToken)
		{
		var settings = AppSettingsStore.Load ();
		string scope = AccountScope (provider, settings);
		if (_client is not null && _scope == scope)
			return _client;
		var options = BuildOptions (provider, settings);
		var candidate = new Powerwall (options);
		candidate.FleetApiTokensRefreshed += OnFleetTokensRefreshed;
		try
			{
			if (!await candidate.ConnectAsync (cancellationToken).ConfigureAwait (false))
				throw new InvalidOperationException ("Cloud history sign-in failed. Renew the selected account's sign-in through the connection screen.");
			}
		catch
			{
			candidate.Dispose ();
			throw;
			}
		_client?.Dispose ();
		_client = candidate;
		_scope = scope;
		return candidate;
		}

	/// <summary>Builds history-only options from either saved account type without opening a connection.</summary>
	/// <param name="provider">Owner or Fleet.</param>
	/// <param name="settings">Saved credentials and regional defaults.</param>
	/// <param name="ownerCachePath">Optional isolated Owner cache path for offline tests.</param>
	/// <returns>Cloud options that never authorize local control.</returns>
	internal static PowerwallOptions BuildOptions (string provider, AppSettings settings, string? ownerCachePath = null)
		{
		if (provider == "Fleet")
			{
			if (string.IsNullOrWhiteSpace (settings.FleetApiClientId))
				throw new InvalidOperationException ("No saved Fleet account. Sign in through the connection screen first.");
			return new PowerwallOptions
				{
				CloudMode = true, FleetApi = true, FleetApiClientId = settings.FleetApiClientId.Trim (),
				FleetApiRefreshToken = CredentialProtector.Unprotect (settings.ProtectedFleetApiRefreshToken),
				FleetApiRegion = "auto"
				};
			}
		else
			{
			string? email = ResolveOwnerEmail (settings, ownerCachePath);
			if (string.IsNullOrWhiteSpace (email))
				throw new InvalidOperationException ("No saved Owner account. Sign in through the connection screen first.");
			return new PowerwallOptions { CloudMode = true, Email = email.Trim () };
			}
		}

	/// <summary>Finds an existing Owner account in the library cache without exporting or copying its tokens.</summary>
	/// <param name="settings">Explicit account choice, when available.</param>
	/// <param name="cachePath">Optional isolated cache file for tests.</param>
	/// <returns>The explicit email or the single unambiguous cached account; null otherwise.</returns>
	internal static string? ResolveOwnerEmail (AppSettings settings, string? cachePath = null)
		{
		if (!string.IsNullOrWhiteSpace (settings.Email))
			return settings.Email;
		cachePath ??= Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData),
			"TeslaPowerwallLibrary", ".powerwall.auth.json");
		try
			{
			if (!File.Exists (cachePath))
				return null;
			var entries = JsonSerializer.Deserialize<Dictionary<string, SavedOwnerAccount>> (File.ReadAllText (cachePath));
			var accounts = entries?.Where (entry => entry.Value is not null && (!string.IsNullOrWhiteSpace (entry.Value.AccessToken)
				|| !string.IsNullOrWhiteSpace (entry.Value.RefreshToken))).Select (entry => entry.Key).ToArray ();
			return accounts?.Length == 1 ? accounts[0] : null;
			}
		catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or JsonException)
			{
			return null;
			}
		}

	private sealed class SavedOwnerAccount
		{
		/// <summary>Gets or sets a stored token solely to determine whether the account has saved authentication.</summary>
		[JsonPropertyName ("access_token")]
		public string? AccessToken { get; set; }
		/// <summary>Gets or sets the stored refresh-token marker; it is never copied or decrypted here.</summary>
		[JsonPropertyName ("refresh_token")]
		public string? RefreshToken { get; set; }
		}

	private static void OnFleetTokensRefreshed (object? sender, FleetApiTokensRefreshedEventArgs e)
		{
		if (string.IsNullOrWhiteSpace (e.RefreshToken))
			return;
		var settings = AppSettingsStore.Load ();
		settings.ProtectedFleetApiRefreshToken = CredentialProtector.Protect (e.RefreshToken);
		AppSettingsStore.Save (settings);
		}

	/// <summary>Releases only the history connection.</summary>
	public void Dispose ()
		{
		_client?.Dispose ();
		_client = null;
		}
	}
