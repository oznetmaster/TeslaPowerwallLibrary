// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using TeslaPowerwallLibrary.Login;

namespace TeslaPowerwallLibrary.Setup;

/// <summary>
/// Interaction logic for <see cref="MainWindow"/>. A thin wrapper around the shared
/// <c>TeslaPowerwallLibrary.Login</c> library: starts the Tesla™ OAuth login (hosted in its own native
/// window by the library) and presents the resulting refresh and access tokens — the Windows equivalent
/// of <c>python -m pypowerwall authtoken</c>.
/// </summary>
public partial class MainWindow : Window
	{
	private CancellationTokenSource? _loginCts;

	/// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
	public MainWindow ()
		{
		InitializeComponent ();
		SelectRegion (App.Region);
		LoadFleetApp ();
		}

	private void SelectRegion (string region)
		{
		foreach (var item in RegionSelector.Items)
			{
			if (item is ComboBoxItem candidate
				&& string.Equals (candidate.Tag as string, region, StringComparison.OrdinalIgnoreCase))
				{
				RegionSelector.SelectedItem = candidate;
				return;
				}
			}
		}

	/// <inheritdoc/>
	protected override void OnClosed (EventArgs e)
		{
		base.OnClosed (e);

		// Abandon an in-flight login if the wrapper window is closed before it completes.
		_loginCts?.Cancel ();
		}

	private string SelectedRegion =>
		(RegionSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "us";

	private async void OnStartLoginClick (object sender, RoutedEventArgs e)
		{
		ResultsPanel.Visibility = Visibility.Collapsed;
		PlaceholderPanel.Visibility = Visibility.Visible;
		StartButton.IsEnabled = false;
		RegionSelector.IsEnabled = false;
		SetStatus ("Opening the Tesla login window. Complete the sign-in in the window that appears...");

		_loginCts?.Dispose ();
		_loginCts = new CancellationTokenSource ();

		try
			{
			var result = await TeslaCloudLogin.SignInAsync (
				SelectedRegion, TimeSpan.FromMinutes (5), cancellationToken: _loginCts.Token).ConfigureAwait (true);

			switch (result.Status)
				{
				case TeslaCloudLoginStatus.Success:
					ShowTokens (result.Tokens!);
					return;

				case TeslaCloudLoginStatus.Cancelled:
					ShowError ("Tesla login was cancelled.");
					return;

				default:
					ShowError ($"Login failed: {result.Message}");
					return;
				}
			}
		catch (Exception exc)
			{
			ShowError ($"Login failed: {exc.Message}");
			}
		}

	private void ShowTokens (TeslaCloudLoginTokens tokens)
		{
		RefreshTokenText.Text = tokens.RefreshToken;
		AccessTokenText.Text = string.IsNullOrEmpty (tokens.AccessToken) ? "(not available)" : tokens.AccessToken;

		if (string.IsNullOrEmpty (tokens.Email))
			{
			EmailRow.Visibility = Visibility.Collapsed;
			}
		else
			{
			EmailRow.Visibility = Visibility.Visible;
			EmailText.Text = tokens.Email;
			}

		PlaceholderPanel.Visibility = Visibility.Collapsed;
		ResultsPanel.Visibility = Visibility.Visible;
		RestartButton.Visibility = Visibility.Visible;
		StartButton.IsEnabled = true;
		RegionSelector.IsEnabled = true;
		SetStatus ("Done. Copy the tokens into your Powerwall configuration.");
		}

	private void OnCopyRefreshClick (object sender, RoutedEventArgs e) =>
		CopyToClipboard (RefreshTokenText.Text, "Refresh token copied to clipboard.");

	private void OnCopyAccessClick (object sender, RoutedEventArgs e) =>
		CopyToClipboard (AccessTokenText.Text, "Access token copied to clipboard.");

	private void CopyToClipboard (string value, string confirmation)
		{
		if (string.IsNullOrEmpty (value) || value == "(not available)")
			{
			SetStatus ("Nothing to copy.");
			return;
			}

		try
			{
			Clipboard.SetText (value);
			SetStatus (confirmation);
			}
		catch (Exception exc)
			{
			SetStatus ($"Unable to copy to clipboard: {exc.Message}");
			}
		}

	private void ShowError (string message)
		{
		PlaceholderPanel.Visibility = Visibility.Visible;
		ResultsPanel.Visibility = Visibility.Collapsed;
		RestartButton.Visibility = Visibility.Visible;
		StartButton.IsEnabled = true;
		RegionSelector.IsEnabled = true;
		SetStatus (message);
		}

	private void SetStatus (string message) =>
		StatusText.Text = message;

	// --- FleetAPI setup ---

	private string? _fleetApiClientId;
	private string? _fleetApiClientSecret;
	private string? _fleetApiRedirectUri;
	private string? _fleetApiAudience;
	private string? _fleetApiState;

	private void LoadFleetApp ()
		{
		try
			{
			var saved = FleetSetupPreferences.Load (FleetSetupPreferences.FilePath);
			if (saved is null)
				return;
			FleetApiClientIdBox.Text = saved.ClientId;
			FleetApiClientSecretBox.Password = saved.ClientSecret;
			FleetApiDomainBox.Text = saved.Domain;
			FleetApiRedirectUriBox.Text = saved.RedirectUri;
			foreach (ComboBoxItem item in FleetApiRegionSelector.Items)
				if ((string?)item.Tag == saved.Region)
					FleetApiRegionSelector.SelectedItem = item;
			RememberFleetApp.IsChecked = true;
			}
		catch { SetFleetApiStatus ("Saved application settings could not be loaded. Enter them again; no authorization was attempted."); }
		}

	private void OnForgetFleetApp (object sender, RoutedEventArgs e)
		{
		try
			{
			if (File.Exists (FleetSetupPreferences.FilePath))
				File.Delete (FleetSetupPreferences.FilePath);
			}
		catch { SetFleetApiStatus ("Saved application settings could not be removed. Check local file access."); }
		}

	private void PrepareFleetAuthorization (string clientId, string secret, string redirect, string region, string domain)
		{
		if (string.IsNullOrWhiteSpace (clientId) || string.IsNullOrWhiteSpace (secret)
			|| !Uri.TryCreate (redirect, UriKind.Absolute, out var callback) || callback.Scheme != Uri.UriSchemeHttps)
			throw new InvalidDataException ("Client ID, Client Secret and the registered HTTPS Redirect URI are required.");
		if (RememberFleetApp.IsChecked == true)
			new FleetSetupPreferences { ClientId = clientId, ClientSecret = secret, Domain = domain, RedirectUri = redirect, Region = region }.Save (FleetSetupPreferences.FilePath);
		_fleetApiClientId = clientId;
		_fleetApiClientSecret = secret;
		_fleetApiRedirectUri = redirect;
		_fleetApiAudience = FleetApiRegionAudience (region);
		var authorization = TeslaFleetApiLogin.BuildAuthorizeUrl (clientId, redirect);
		_fleetApiState = authorization.State;
		FleetApiAuthorizeUrlText.Text = authorization.AuthorizeUrl;
		FleetApiCodeBox.Clear ();
		FleetApiRefreshTokenText.Clear ();
		FleetApiAccessTokenText.Clear ();
		FleetApiResultsPanel.Visibility = Visibility.Collapsed;
		FleetApiAuthorizePanel.Visibility = Visibility.Visible;
		FleetApiExchangeButton.IsEnabled = true;
		SetFleetApiStatus ("Opening Tesla sign-in. The callback and token exchange are handled automatically.");
		}

	private async void OnFleetApiExistingClick (object sender, RoutedEventArgs e)
		{
		try
			{
			PrepareFleetAuthorization (FleetApiClientIdBox.Text.Trim (), FleetApiClientSecretBox.Password, FleetApiRedirectUriBox.Text.Trim (), FleetApiSelectedRegion, FleetApiDomainBox.Text.Trim ());
			await AuthorizeFleetAsync ();
			}
		catch { SetFleetApiStatus ("Could not prepare authorization. Check the Client ID, Client Secret, registered HTTPS Redirect URI and local file access."); }
		}

	private async Task AuthorizeFleetAsync ()
		{
		FleetApiExistingButton.IsEnabled = false;
		FleetApiRegisterButton.IsEnabled = false;
		try
			{
			var login = new FleetAuthorizationWindow (FleetApiAuthorizeUrlText.Text, _fleetApiRedirectUri!, _fleetApiState!) { Owner = this };
			login.ShowDialog ();
			if (login.CallbackUrl is null)
				{
				SetFleetApiStatus (login.FailureMessage);
				return;
				}
			await ExchangeFleetCallbackAsync (login.CallbackUrl);
			}
		finally
			{
			FleetApiExistingButton.IsEnabled = true;
			FleetApiRegisterButton.IsEnabled = true;
			}
		}

	private string FleetApiSelectedRegion =>
		(FleetApiRegionSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "na";

	private static string FleetApiRegionAudience (string region) =>
		region switch
			{
				"eu" => "https://fleet-api.prd.eu.vn.cloud.tesla.com",
				"cn" => "https://fleet-api.prd.cn.vn.cloud.tesla.cn",
				_ => "https://fleet-api.prd.na.vn.cloud.tesla.com"
				};

	private async void OnFleetApiRegisterClick (object sender, RoutedEventArgs e)
		{
		var clientId = FleetApiClientIdBox.Text.Trim ();
		var clientSecret = FleetApiClientSecretBox.Password;
		var domain = FleetApiDomainBox.Text.Trim ();
		var redirectUri = FleetApiRedirectUriBox.Text.Trim ();

		if (string.IsNullOrWhiteSpace (clientId) || string.IsNullOrWhiteSpace (clientSecret) || string.IsNullOrWhiteSpace (domain))
			{
			SetFleetApiStatus ("Client ID, Client Secret, and Domain are required.");
			return;
			}

		if (string.IsNullOrWhiteSpace (redirectUri))
			{
			redirectUri = $"https://{domain}/access";
			FleetApiRedirectUriBox.Text = redirectUri;
			}

		FleetApiRegisterButton.IsEnabled = false;
		FleetApiExistingButton.IsEnabled = false;
		FleetApiAuthorizePanel.Visibility = Visibility.Collapsed;
		FleetApiResultsPanel.Visibility = Visibility.Collapsed;

		try
			{
			var region = FleetApiSelectedRegion;
			var audience = FleetApiRegionAudience (region);

			SetFleetApiStatus ("Verifying PEM key file...");
			if (!await TeslaFleetApiLogin.VerifyPemKeyAsync (domain).ConfigureAwait (true))
				{
				SetFleetApiStatus ($"Could not verify PEM key file at https://{domain}/.well-known/appspecific/com.tesla.3p.public-key.pem. Make sure the public key has been created and uploaded to your website.");
				return;
				}

			SetFleetApiStatus ("Generating partner authentication token...");
			var partnerTokenResult = await TeslaFleetApiLogin.GetPartnerTokenAsync (clientId, clientSecret, audience).ConfigureAwait (true);
			if (partnerTokenResult.Status != TeslaFleetApiLoginStatus.Success)
				{
				SetFleetApiStatus ($"Error: {partnerTokenResult.Message}");
				return;
				}

			SetFleetApiStatus ("Registering partner account...");
			var registerResult = await TeslaFleetApiLogin.RegisterPartnerAccountAsync (partnerTokenResult.PartnerToken!, audience, domain).ConfigureAwait (true);
			if (registerResult.Status != TeslaFleetApiLoginStatus.Success)
				{
				SetFleetApiStatus ($"Error: {registerResult.Message}");
				return;
				}

			PrepareFleetAuthorization (clientId, clientSecret, redirectUri, region, domain);
			await AuthorizeFleetAsync ();
			}
		catch (Exception exc)
			{
			SetFleetApiStatus ($"Error: {exc.Message}");
			}
		finally
			{
			FleetApiRegisterButton.IsEnabled = true;
			FleetApiExistingButton.IsEnabled = true;
			}
		}

	private void OnFleetApiOpenBrowserClick (object sender, RoutedEventArgs e)
		{
		var url = FleetApiAuthorizeUrlText.Text;
		if (string.IsNullOrWhiteSpace (url))
			return;

		try
			{
			Process.Start (new ProcessStartInfo (url) { UseShellExecute = true });
			}
		catch (Exception exc)
			{
			SetFleetApiStatus ($"Unable to open browser: {exc.Message}");
			}
		}

	private void OnFleetApiCopyUrlClick (object sender, RoutedEventArgs e) =>
		CopyFleetApiToClipboard (FleetApiAuthorizeUrlText.Text, "Authorize URL copied to clipboard.");

	private async void OnFleetApiExchangeClick (object sender, RoutedEventArgs e)
		{
		await ExchangeFleetCallbackAsync (FleetApiCodeBox.Text.Trim ());
		}

	private async Task ExchangeFleetCallbackAsync (string callbackUrl)
		{
		if (_fleetApiClientId is null || _fleetApiClientSecret is null || _fleetApiRedirectUri is null || _fleetApiAudience is null || _fleetApiState is null)
			{
			SetFleetApiStatus ("Start a new Fleet sign-in before exchanging a code.");
			return;
			}

		string code;
		try
			{
			code = FleetSetupPreferences.ParseCallback (callbackUrl, _fleetApiRedirectUri, _fleetApiState);
			}
		catch { SetFleetApiStatus ("Paste the complete redirected URL from this authorization attempt. The callback address and state must match."); return; }
		// Authorization codes are single-use; an uncertain exchange must not be repeated silently.
		_fleetApiState = null;

		FleetApiExchangeButton.IsEnabled = false;
		FleetApiExistingButton.IsEnabled = false;
		FleetApiRegisterButton.IsEnabled = false;
		try
			{
			SetFleetApiStatus ("Exchanging authorization code for tokens...");
			using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			var result = await TeslaFleetApiLogin.ExchangeCodeAsync (
				_fleetApiClientId, _fleetApiClientSecret, code, _fleetApiRedirectUri, _fleetApiAudience, timeout.Token).ConfigureAwait (true);

			if (result.Status != TeslaFleetApiLoginStatus.Success)
				{
				SetFleetApiStatus ($"Error: {result.Message}");
				return;
				}

			FleetApiRefreshTokenText.Text = result.Tokens!.RefreshToken;
			FleetApiAccessTokenText.Text = result.Tokens.AccessToken;
			FleetApiResultsPanel.Visibility = Visibility.Visible;
			FleetApiAuthorizePanel.Visibility = Visibility.Collapsed;
			FleetApiCodeBox.Clear ();
			SetFleetApiStatus ("Done. Use the refresh token and Client ID in your Fleet configuration.");
			}
		catch
			{
			SetFleetApiStatus ("The token exchange did not complete. Start a new sign-in; this code will not be retried.");
			}
		finally
			{
			FleetApiExchangeButton.IsEnabled = false;
			FleetApiExistingButton.IsEnabled = true;
			FleetApiRegisterButton.IsEnabled = true;
			}
		}

	private void OnFleetApiCopyRefreshClick (object sender, RoutedEventArgs e) =>
		CopyFleetApiToClipboard (FleetApiRefreshTokenText.Text, "Refresh token copied to clipboard.");

	private void OnFleetApiCopyAccessClick (object sender, RoutedEventArgs e) =>
		CopyFleetApiToClipboard (FleetApiAccessTokenText.Text, "Access token copied to clipboard.");

	private void CopyFleetApiToClipboard (string value, string confirmation)
		{
		if (string.IsNullOrEmpty (value))
			{
			SetFleetApiStatus ("Nothing to copy.");
			return;
			}

		try
			{
			Clipboard.SetText (value);
			SetFleetApiStatus (confirmation);
			}
		catch (Exception exc)
			{
			SetFleetApiStatus ($"Unable to copy to clipboard: {exc.Message}");
			}
		}

	private void SetFleetApiStatus (string message) =>
		FleetApiStatusText.Text = message;
	}