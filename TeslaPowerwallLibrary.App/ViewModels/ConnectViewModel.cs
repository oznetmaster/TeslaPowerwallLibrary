// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the connect screen. Supports a Cloud/Local mode picker: cloud mode signs in with a Tesla™ account
/// (browser login or pasted tokens), then presents a site picker populated from the cloud; local mode
/// connects with a gateway host and password. The last account, region, and site are loaded from and saved
/// to per-user, DPAPI-protected settings.
/// </summary>
public sealed partial class ConnectViewModel : ViewModelBase
	{
	private readonly PowerwallConnectionService _connection;

	/// <summary>Raised when a connection has been successfully established.</summary>
	public event EventHandler? Connected;

	/// <summary>Initializes a new instance of the <see cref="ConnectViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public ConnectViewModel (PowerwallConnectionService connection)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		Sites = new ObservableCollection<CloudSite> ();
		LoadFromSettings ();
		}

	/// <summary>Gets the Tesla energy sites available to the account, populated after a successful cloud connect.</summary>
	public ObservableCollection<CloudSite> Sites { get; }

	/// <summary>Gets the selectable Tesla regions shown in the sign-in region picker.</summary>
	public IReadOnlyList<RegionOption> Regions { get; } =
		[
			new RegionOption ("United States / International", "us"),
			new RegionOption ("China", "cn")
		];

	/// <summary>Gets or sets a value indicating whether cloud mode is selected.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (IsLocalMode))]
	private bool _isCloudMode = true;

	/// <summary>Gets or sets a value indicating whether Tesla FleetAPI mode is selected.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (IsLocalMode))]
	private bool _isFleetApiMode;

	/// <summary>Gets a value indicating whether local mode is selected (neither cloud nor FleetAPI).</summary>
	public bool IsLocalMode => !IsCloudMode && !IsFleetApiMode;

	partial void OnIsCloudModeChanged (bool value)
		{
		if (value)
			IsFleetApiMode = false;
		}

	partial void OnIsFleetApiModeChanged (bool value)
		{
		if (value)
			IsCloudMode = false;
		}

	/// <summary>Gets or sets the Tesla account email used for cloud mode.</summary>
	[ObservableProperty]
	private string _email = string.Empty;

	/// <summary>Gets or sets the Tesla Owners API access token used for cloud mode.</summary>
	[ObservableProperty]
	private string _accessToken = string.Empty;

	/// <summary>Gets or sets the Tesla Owners API refresh token used for cloud mode.</summary>
	[ObservableProperty]
	private string _refreshToken = string.Empty;

	/// <summary>Gets or sets the Tesla FleetAPI application Client ID used for FleetAPI mode.</summary>
	[ObservableProperty]
	private string _fleetApiClientId = string.Empty;

	/// <summary>Gets or sets the Tesla FleetAPI OAuth refresh token used for FleetAPI mode.</summary>
	[ObservableProperty]
	private string _fleetApiRefreshToken = string.Empty;

	/// <summary>Gets or sets the Tesla FleetAPI region (<c>na</c>, <c>eu</c>, or <c>cn</c>) used for FleetAPI mode.</summary>
	[ObservableProperty]
	private string _fleetApiRegion = "na";

	/// <summary>Gets or sets the Tesla FleetAPI application Client Secret, used only for the guided setup wizard.</summary>
	[ObservableProperty]
	private string _fleetApiClientSecret = string.Empty;

	/// <summary>Gets or sets the domain registered with Tesla FleetAPI, used only for the guided setup wizard.</summary>
	[ObservableProperty]
	private string _fleetApiDomain = string.Empty;

	/// <summary>Gets or sets the redirect URI registered with Tesla FleetAPI, used only for the guided setup wizard.</summary>
	[ObservableProperty]
	private string _fleetApiRedirectUri = string.Empty;

	/// <summary>Gets or sets a value indicating whether the FleetAPI setup wizard's authorize step is shown.</summary>
	[ObservableProperty]
	private bool _showFleetApiAuthorize;

	/// <summary>Gets or sets the FleetAPI authorize URL to visit, populated after partner registration succeeds.</summary>
	[ObservableProperty]
	private string _fleetApiAuthorizeUrl = string.Empty;

	/// <summary>Gets or sets the authorization code (or redirected URL) pasted back by the user.</summary>
	[ObservableProperty]
	private string _fleetApiAuthorizationCode = string.Empty;

	// Captured from the register step for use by the exchange step.
	private string? _fleetApiSetupAudience;

	/// <summary>
	/// Gets or sets the remembered Tesla energy site identifier. Used to pre-connect to the last site and to
	/// preselect the matching entry once <see cref="Sites"/> is populated.
	/// </summary>
	[ObservableProperty]
	private string _siteId = string.Empty;

	/// <summary>Gets or sets the site chosen from the cloud-populated <see cref="Sites"/> list.</summary>
	[ObservableProperty]
	private CloudSite? _selectedSite;

	/// <summary>
	/// Gets or sets a value indicating whether the site picker phase is shown (after a successful cloud
	/// connect that returned more than one site).
	/// </summary>
	[ObservableProperty]
	private bool _showSiteSelection;

	/// <summary>Gets or sets the Tesla region (<c>us</c> or <c>cn</c>) used by the browser sign-in flow.</summary>
	[ObservableProperty]
	private string _region = "us";

	/// <summary>Gets or sets the gateway host or IP address used for local mode.</summary>
	[ObservableProperty]
	private string _host = string.Empty;

	/// <summary>Gets or sets the gateway customer password used for local mode.</summary>
	[ObservableProperty]
	private string _password = string.Empty;

	/// <summary>Gets or sets a value indicating whether credentials should be saved on a successful connect.</summary>
	[ObservableProperty]
	private bool _rememberCredentials = true;

	/// <summary>Attempts to connect using the currently selected mode and credentials.</summary>
	/// <returns>A task that completes when the connect attempt finishes.</returns>
	[RelayCommand]
	private async Task ConnectAsync ()
		{
		IsBusy = true;
		try
			{
			await TryConnectAsync ().ConfigureAwait (true);
			}
		finally
			{
			IsBusy = false;
			}
		}

	/// <summary>
	/// Attempts a one-time silent reconnect at startup using the credentials loaded from saved settings,
	/// reusing previously obtained Tesla tokens (or a saved local gateway login) without a fresh browser
	/// sign-in. This mirrors the test console, which reuses cached tokens on launch. When no saved
	/// credentials exist, or the attempt fails, the connect screen remains so the user can sign in manually.
	/// </summary>
	/// <returns>A task that completes when the startup auto-connect attempt finishes.</returns>
	public async Task TryAutoConnectAsync ()
		{
		if (!HasSavedCredentials ())
			return;

		IsBusy = true;
		try
			{
			await TryConnectAsync (preferSavedSite: true).ConfigureAwait (true);
			}
		finally
			{
			IsBusy = false;
			}
		}

	// True when there is enough to attempt a connection without prompting: cloud tokens the library cached
	// for this account, a saved FleetAPI Client ID and refresh token, or a saved host and password for local mode.
	private bool HasSavedCredentials ()
		{
		if (IsFleetApiMode)
			return !string.IsNullOrWhiteSpace (FleetApiClientId) && !string.IsNullOrWhiteSpace (FleetApiRefreshToken);

		return IsCloudMode
			? !string.IsNullOrWhiteSpace (Email) && Powerwall.HasStoredCloudTokens (Email.Trim ())
			: !string.IsNullOrWhiteSpace (Host) && !string.IsNullOrWhiteSpace (Password);
		}

	/// <summary>
	/// Signs in through Tesla's real browser login (handled by the out-of-process WebView2 setup app,
	/// which manages captcha and multi-factor sign-in), captures the returned tokens, and connects.
	/// This is the desktop equivalent of the test console's <c>login cloud</c> flow.
	/// </summary>
	/// <returns>A task that completes when the login and connect attempt finishes.</returns>
	[RelayCommand]
	private async Task SignInWithTeslaAsync ()
		{
		IsBusy = true;
		try
			{
			IsCloudMode = true;
			StatusMessage = "Opening the Tesla login window. Complete the sign-in in the browser that appears...";

			var emailHint = string.IsNullOrWhiteSpace (Email) ? null : Email.Trim ();
			var result = await TeslaLoginService.AcquireTokensAsync (Region, TimeSpan.FromMinutes (5), emailHint).ConfigureAwait (true);
			switch (result.Status)
				{
				case TeslaLoginStatus.Success:
					var tokens = result.Tokens!;
					RefreshToken = tokens.RefreshToken;
					AccessToken = tokens.AccessToken;
					if (!string.IsNullOrWhiteSpace (tokens.Email))
						Email = tokens.Email;

					await TryConnectAsync ().ConfigureAwait (true);
					return;

				case TeslaLoginStatus.Cancelled:
					StatusMessage = "Tesla login was cancelled.";
					return;

				default:
					StatusMessage = $"Tesla login failed: {result.Message}";
					return;
				}
			}
		finally
			{
			IsBusy = false;
			}
		}

	/// <summary>
	/// Runs the first step of the Tesla FleetAPI setup wizard: verifies the hosted PEM key, generates a
	/// partner token, and registers the partner account, then presents the authorize URL to visit. Adapted
	/// from the upstream <c>pypowerwall</c> <c>fleetapi.setup()</c> wizard; the caller supplies its own
	/// registered Client ID/Secret, domain, and redirect URI obtained from https://developer.tesla.com/.
	/// </summary>
	/// <returns>A task that completes when the registration step finishes.</returns>
	[RelayCommand]
	private async Task FleetApiRegisterAsync ()
		{
		var clientId = FleetApiClientId.Trim ();
		var clientSecret = FleetApiClientSecret;
		var domain = FleetApiDomain.Trim ();
		var redirectUri = FleetApiRedirectUri.Trim ();

		if (string.IsNullOrWhiteSpace (clientId) || string.IsNullOrWhiteSpace (clientSecret) || string.IsNullOrWhiteSpace (domain))
			{
			StatusMessage = "Client ID, Client Secret, and Domain are required.";
			return;
			}

		if (string.IsNullOrWhiteSpace (redirectUri))
			{
			redirectUri = $"https://{domain}/access";
			FleetApiRedirectUri = redirectUri;
			}

		IsBusy = true;
		ShowFleetApiAuthorize = false;
		try
			{
			var audience = FleetApiRegionAudience (NormalizeFleetApiRegion (FleetApiRegion));

			StatusMessage = "Verifying PEM key file...";
			if (!await TeslaLoginService.VerifyFleetApiPemKeyAsync (domain).ConfigureAwait (true))
				{
				StatusMessage = $"Could not verify PEM key file at https://{domain}/.well-known/appspecific/com.tesla.3p.public-key.pem. Make sure the public key has been uploaded to your website.";
				return;
				}

			StatusMessage = "Generating partner authentication token...";
			var (partnerToken, tokenMessage) = await TeslaLoginService.GetFleetApiPartnerTokenAsync (clientId, clientSecret, audience).ConfigureAwait (true);
			if (partnerToken is null)
				{
				StatusMessage = $"Error: {tokenMessage}";
				return;
				}

			StatusMessage = "Registering partner account...";
			var (registered, registerMessage) = await TeslaLoginService.RegisterFleetApiPartnerAccountAsync (partnerToken, audience, domain).ConfigureAwait (true);
			if (!registered)
				{
				StatusMessage = $"Error: {registerMessage}";
				return;
				}

			_fleetApiSetupAudience = audience;
			var (authorizeUrl, _) = TeslaLoginService.BuildFleetApiAuthorizeUrl (clientId, redirectUri);
			FleetApiAuthorizeUrl = authorizeUrl;
			ShowFleetApiAuthorize = true;
			StatusMessage = "Partner account registered. Visit the authorize URL, sign in, then paste the returned code below.";
			}
		finally
			{
			IsBusy = false;
			}
		}

	/// <summary>
	/// Runs the final step of the Tesla FleetAPI setup wizard: exchanges the authorization code captured from
	/// the redirect for FleetAPI tokens, then connects using them.
	/// </summary>
	/// <returns>A task that completes when the exchange and connect attempt finishes.</returns>
	[RelayCommand]
	private async Task FleetApiExchangeAsync ()
		{
		var clientId = FleetApiClientId.Trim ();
		var clientSecret = FleetApiClientSecret;
		var redirectUri = FleetApiRedirectUri.Trim ();
		var code = FleetApiAuthorizationCode.Trim ();

		if (_fleetApiSetupAudience is null)
			{
			StatusMessage = "Complete partner registration before exchanging a code.";
			return;
			}

		if (string.IsNullOrWhiteSpace (code))
			{
			StatusMessage = "Enter the authorization code returned after signing in.";
			return;
			}

		if (code.StartsWith ("http", StringComparison.OrdinalIgnoreCase))
			{
			var codeIndex = code.IndexOf ("code=", StringComparison.OrdinalIgnoreCase);
			if (codeIndex >= 0)
				{
				code = code[(codeIndex + "code=".Length)..];
				var ampersandIndex = code.IndexOf ('&');
				if (ampersandIndex >= 0)
					code = code[..ampersandIndex];
				}
			}

		IsBusy = true;
		try
			{
			StatusMessage = "Exchanging authorization code for tokens...";
			var (tokens, message) = await TeslaLoginService.ExchangeFleetApiCodeAsync (
				clientId, clientSecret, code, redirectUri, _fleetApiSetupAudience).ConfigureAwait (true);

			if (tokens is null)
				{
				StatusMessage = $"Error: {message}";
				return;
				}

			FleetApiRefreshToken = tokens.RefreshToken;
			ShowFleetApiAuthorize = false;
			FleetApiAuthorizationCode = string.Empty;

			await TryConnectAsync ().ConfigureAwait (true);
			}
		finally
			{
			IsBusy = false;
			}
		}

	// Resolves the FleetAPI audience (regional base URL) matching the main library's FleetApiRegions mapping.
	private static string FleetApiRegionAudience (string region) =>
		region switch
			{
			"eu" => "https://fleet-api.prd.eu.vn.cloud.tesla.com",
			"cn" => "https://fleet-api.prd.cn.vn.cloud.tesla.cn",
			_ => "https://fleet-api.prd.na.vn.cloud.tesla.com"
			};

	/// <summary>
	/// Confirms the chosen site from the picker, switches the active site if needed, and enters the app.
	/// </summary>
	/// <returns>A task that completes when the site has been applied and the app entered.</returns>
	[RelayCommand (CanExecute = nameof (CanContinue))]
	private async Task ContinueAsync ()
		{
		IsBusy = true;
		try
			{
			var site = SelectedSite!;
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			try
				{
				await _connection.Powerwall.ChangeSiteAsync (site.SiteId, cts.Token).ConfigureAwait (true);
				}
			catch (OperationCanceledException)
				{
				StatusMessage = "Selecting the site timed out.";
				return;
				}
			catch (PowerwallException exc)
				{
				StatusMessage = $"Could not select that site: {exc.Message}";
				return;
				}

			SiteId = site.SiteId;
			EnterApp ();
			}
		finally
			{
			IsBusy = false;
			}
		}

	private bool CanContinue () => SelectedSite is not null;

	partial void OnSelectedSiteChanged (CloudSite? value) => ContinueCommand.NotifyCanExecuteChanged ();

	/// <summary>
	/// Connects with the current options and, on success, either presents the cloud site picker or enters
	/// the app. Shared by the manual connect button and the Tesla sign-in flow.
	/// </summary>
	/// <param name="preferSavedSite">
	/// When <see langword="true"/>, a remembered site that matches the account is applied without showing
	/// the picker. Used by the startup auto-connect so a returning user lands straight in the app.
	/// </param>
	/// <returns>A task producing <see langword="true"/> when the connection succeeds.</returns>
	private async Task<bool> TryConnectAsync (bool preferSavedSite = false)
		{
		StatusMessage = null;
		ShowSiteSelection = false;

		var options = BuildOptions ();
		using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (60));

		bool ok;
		try
			{
			ok = await _connection.ConnectAsync (options, cts.Token).ConfigureAwait (true);
			}
		catch (OperationCanceledException)
			{
			StatusMessage = "The connection attempt timed out.";
			return false;
			}
		catch (PowerwallException exc)
			{
			StatusMessage = $"Connection error: {exc.Message}";
			return false;
			}

		if (!ok)
			{
			StatusMessage = "Failed to connect. Check your credentials and try again.";
			return false;
			}

		await AfterConnectAsync (preferSavedSite).ConfigureAwait (true);
		return true;
		}

	/// <summary>
	/// After a successful connect, loads the account's sites (cloud mode). When more than one site is
	/// available the picker phase is shown; otherwise the app is entered immediately.
	/// </summary>
	/// <param name="preferSavedSite">
	/// When <see langword="true"/> and the remembered <see cref="SiteId"/> matches an available site, that
	/// site is selected and the app is entered directly without showing the picker (used by auto-connect).
	/// </param>
	/// <returns>A task that completes once the sites are resolved and the next phase is chosen.</returns>
	private async Task AfterConnectAsync (bool preferSavedSite)
		{
		if (!IsCloudMode && !IsFleetApiMode)
			{
			EnterApp ();
			return;
			}

		// Seed the remembered site from the live connection: the library resolves and persists the active
		// site itself (keyed by email), so on a reconnect the client is already on the right site.
		if (string.IsNullOrWhiteSpace (SiteId) && !string.IsNullOrWhiteSpace (_connection.Powerwall.CloudSiteId))
			SiteId = _connection.Powerwall.CloudSiteId!;

		IReadOnlyList<CloudSite> sites;
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			sites = await _connection.Powerwall.GetSitesAsync (cts.Token).ConfigureAwait (true);
			}
		catch (Exception exc) when (exc is OperationCanceledException or PowerwallException)
			{
			// The connection itself succeeded; if listing sites fails, proceed with the account default,
			// falling back to the raw site id for the label since the friendly site name is unavailable.
			if (!string.IsNullOrWhiteSpace (SiteId))
				_connection.SetSiteLabel (SiteId);

			EnterApp ();
			return;
			}

		Sites.Clear ();
		foreach (var site in sites)
			Sites.Add (site);

		// When reconnecting with a remembered site, honor it and enter directly, as the console does.
		if (preferSavedSite && !string.IsNullOrWhiteSpace (SiteId))
			{
			var remembered = Sites.FirstOrDefault (s => s.SiteId == SiteId);
			if (remembered is not null)
				{
				SelectedSite = remembered;
				EnterApp ();
				return;
				}
			}

		if (Sites.Count <= 1)
			{
			if (Sites.Count == 1)
				{
				SiteId = Sites[0].SiteId;
				SelectedSite = Sites[0];
				}

			EnterApp ();
			return;
			}

		SelectedSite = Sites.FirstOrDefault (s => s.SiteId == SiteId) ?? Sites[0];
		StatusMessage = "Select the site you want to manage, then choose Continue.";
		ShowSiteSelection = true;
		}

	/// <summary>Persists credentials (when enabled) and signals that the app may proceed to its main screens.</summary>
	private void EnterApp ()
		{
		ShowSiteSelection = false;

		// Local mode's label (the gateway host) is set by PowerwallConnectionService.ConnectAsync; cloud
		// mode's label is the resolved site name, once known.
		if ((IsCloudMode || IsFleetApiMode) && SelectedSite is not null)
			_connection.SetSiteLabel (SelectedSite.SiteName ?? SelectedSite.SiteId);

		if (RememberCredentials)
			SaveToSettings ();

		Connected?.Invoke (this, EventArgs.Empty);
		}

	private PowerwallOptions BuildOptions ()
		{
		if (IsFleetApiMode)
			{
			return new PowerwallOptions
				{
				CloudMode = true,
				FleetApi = true,
				FleetApiClientId = string.IsNullOrWhiteSpace (FleetApiClientId) ? null : FleetApiClientId.Trim (),
				FleetApiRefreshToken = string.IsNullOrWhiteSpace (FleetApiRefreshToken) ? null : FleetApiRefreshToken.Trim (),
				FleetApiRegion = NormalizeFleetApiRegion (FleetApiRegion),
				SiteId = string.IsNullOrWhiteSpace (SiteId) ? null : SiteId.Trim ()
				};
			}

		if (IsCloudMode)
			{
			return new PowerwallOptions
				{
				CloudMode = true,
				Email = Email.Trim (),
				AccessToken = string.IsNullOrWhiteSpace (AccessToken) ? null : AccessToken.Trim (),
				RefreshToken = string.IsNullOrWhiteSpace (RefreshToken) ? null : RefreshToken.Trim (),
				SiteId = string.IsNullOrWhiteSpace (SiteId) ? null : SiteId.Trim ()
				};
			}

		return new PowerwallOptions
			{
			Host = Host.Trim (),
			Password = Password
			};
		}

	/// <summary>Normalizes a Tesla FleetAPI region to one of <c>na</c>, <c>eu</c>, or <c>cn</c>, defaulting to <c>na</c>.</summary>
	private static string NormalizeFleetApiRegion (string? region) =>
		region?.Trim ().ToLowerInvariant () switch
			{
			"eu" => "eu",
			"cn" => "cn",
			_ => "na"
			};

	private void LoadFromSettings ()
		{
		var settings = AppSettingsStore.Load ();

		IsFleetApiMode = string.Equals (settings.Mode, "FleetApi", StringComparison.OrdinalIgnoreCase);
		IsCloudMode = !IsFleetApiMode && !string.Equals (settings.Mode, "Local", StringComparison.OrdinalIgnoreCase);
		Email = settings.Email ?? string.Empty;
		Host = settings.Host ?? string.Empty;
		Region = string.IsNullOrWhiteSpace (settings.Region) ? "us" : settings.Region!.Trim ().ToLowerInvariant ();
		Password = CredentialProtector.Unprotect (settings.ProtectedPassword) ?? string.Empty;
		FleetApiClientId = settings.FleetApiClientId ?? string.Empty;
		FleetApiRefreshToken = CredentialProtector.Unprotect (settings.ProtectedFleetApiRefreshToken) ?? string.Empty;
		FleetApiRegion = string.IsNullOrWhiteSpace (settings.FleetApiRegion) ? "na" : settings.FleetApiRegion!.Trim ().ToLowerInvariant ();
		}

	/// <summary>
	/// Resets the transient sign-in state so the connect screen is ready for a fresh login, typically when
	/// switching accounts. Clears the captured tokens, the site picker, and any status message while keeping
	/// remembered non-secret defaults (mode, email, region, host) so the user does not have to re-enter them.
	/// </summary>
	public void PrepareForSignIn ()
		{
		AccessToken = string.Empty;
		RefreshToken = string.Empty;
		FleetApiRefreshToken = string.Empty;
		FleetApiClientSecret = string.Empty;
		FleetApiAuthorizeUrl = string.Empty;
		FleetApiAuthorizationCode = string.Empty;
		ShowFleetApiAuthorize = false;
		_fleetApiSetupAudience = null;
		SiteId = string.Empty;
		SelectedSite = null;
		Sites.Clear ();
		ShowSiteSelection = false;
		StatusMessage = null;
		}

	private void SaveToSettings ()
		{
		// Cloud and FleetAPI tokens plus the selected site are persisted by the library itself (keyed by
		// email); the app additionally remembers the FleetAPI Client ID and (encrypted) initial refresh token
		// here so it can populate the sign-in fields before the library's own cache has been created,
		// mirroring the local gateway password below.
		var settings = new AppSettings
			{
			Mode = IsFleetApiMode ? "FleetApi" : IsCloudMode ? "Cloud" : "Local",
			Email = IsCloudMode ? Email.Trim () : null,
			Host = IsLocalMode ? Host.Trim () : null,
			Region = IsCloudMode ? Region : null,
			ProtectedPassword = IsLocalMode ? CredentialProtector.Protect (Password) : null,
			FleetApiClientId = IsFleetApiMode ? FleetApiClientId.Trim () : null,
			ProtectedFleetApiRefreshToken = IsFleetApiMode ? CredentialProtector.Protect (FleetApiRefreshToken) : null,
			FleetApiRegion = IsFleetApiMode ? FleetApiRegion : null
			};

		AppSettingsStore.Save (settings);
		}
	}

/// <summary>A selectable Tesla region shown in the sign-in region picker.</summary>
/// <param name="Display">The human-readable region label.</param>
/// <param name="Value">The region code passed to the login flow (<c>us</c> or <c>cn</c>).</param>
public sealed record RegionOption (string Display, string Value);
