// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
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
				SelectedRegion, TimeSpan.FromMinutes (5), _loginCts.Token).ConfigureAwait (true);

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
	}
