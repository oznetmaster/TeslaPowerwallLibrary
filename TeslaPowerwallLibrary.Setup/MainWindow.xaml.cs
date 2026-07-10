// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
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

			_fleetApiClientId = clientId;
			_fleetApiClientSecret = clientSecret;
			_fleetApiRedirectUri = redirectUri;
			_fleetApiAudience = audience;

			var (authorizeUrl, _) = TeslaFleetApiLogin.BuildAuthorizeUrl (clientId, redirectUri);
			FleetApiAuthorizeUrlText.Text = authorizeUrl;
			FleetApiAuthorizePanel.Visibility = Visibility.Visible;
			SetFleetApiStatus ("Partner account registered. Visit the authorize URL, sign in, then paste the returned code below.");
			}
		catch (Exception exc)
			{
			SetFleetApiStatus ($"Error: {exc.Message}");
			}
		finally
			{
			FleetApiRegisterButton.IsEnabled = true;
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
		if (_fleetApiClientId is null || _fleetApiClientSecret is null || _fleetApiRedirectUri is null || _fleetApiAudience is null)
			{
			SetFleetApiStatus ("Complete step 1 before exchanging a code.");
			return;
			}

		var code = FleetApiCodeBox.Text.Trim ();
		if (string.IsNullOrWhiteSpace (code))
			{
			SetFleetApiStatus ("Enter the authorization code returned after signing in.");
			return;
			}

		if (code.StartsWith ("http", StringComparison.OrdinalIgnoreCase))
			{
			var codeIndex = code.IndexOf ("code=", StringComparison.OrdinalIgnoreCase);
			if (codeIndex >= 0)
				{
				code = code.Substring (codeIndex + "code=".Length);
				var ampersandIndex = code.IndexOf ('&');
				if (ampersandIndex >= 0)
					code = code.Substring (0, ampersandIndex);
				}
			}

		FleetApiExchangeButton.IsEnabled = false;
		try
			{
			SetFleetApiStatus ("Exchanging authorization code for tokens...");
			var result = await TeslaFleetApiLogin.ExchangeCodeAsync (
				_fleetApiClientId, _fleetApiClientSecret, code, _fleetApiRedirectUri, _fleetApiAudience).ConfigureAwait (true);

			if (result.Status != TeslaFleetApiLoginStatus.Success)
				{
				SetFleetApiStatus ($"Error: {result.Message}");
				return;
				}

			FleetApiRefreshTokenText.Text = result.Tokens!.RefreshToken;
			FleetApiAccessTokenText.Text = result.Tokens.AccessToken;
			FleetApiResultsPanel.Visibility = Visibility.Visible;
			SetFleetApiStatus ("Done. Copy the tokens into your Powerwall configuration.");
			}
		catch (Exception exc)
			{
			SetFleetApiStatus ($"Error: {exc.Message}");
			}
		finally
			{
			FleetApiExchangeButton.IsEnabled = true;
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
