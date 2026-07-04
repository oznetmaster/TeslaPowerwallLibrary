// Copyright ┬⌐ 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright ┬⌐ 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Web.WebView2.Core;

namespace TeslaPowerwallLibrary.Setup;

/// <summary>
/// Interaction logic for <see cref="MainWindow"/>. Hosts the embedded Tesla OAuth login, intercepts the
/// <c>tesla://auth/callback</c> redirect, exchanges the authorization code for tokens, and presents the
/// resulting refresh and access tokens ΓÇö the Windows equivalent of <c>python -m pypowerwall authtoken</c>.
/// </summary>
public partial class MainWindow : Window
	{
	/// <summary>
	/// Sentinel prefix written to standard output in emit mode, followed by the base64-encoded JSON
	/// token payload. A calling process scans stdout for this line to receive the captured tokens.
	/// </summary>
	internal const string TokenSentinel = "__PWTOKENS__=";

	private TeslaAuthRequest? _authRequest;
	private bool _webViewInitialized;
	private bool _exchangeStarted;
	private bool _emitHandled;

	/// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
	public MainWindow ()
		{
		InitializeComponent ();

		// In emit mode the window auto-starts the login as soon as it is ready and returns the
		// captured tokens to the calling process via standard output.
		if (App.EmitMode)
			Loaded += OnWindowLoadedForEmit;
		}

	private async void OnWindowLoadedForEmit (object sender, RoutedEventArgs e)
		{
		Loaded -= OnWindowLoadedForEmit;

		try
			{
			SelectRegion (App.Region);
			await StartLoginAsync ().ConfigureAwait (true);
			}
		catch (Exception exc)
			{
			ShowError ($"Login failed: {exc.Message}");
			}
		}

	/// <inheritdoc/>
	protected override void OnClosed (EventArgs e)
		{
		base.OnClosed (e);

		// If the user closes the login window before completing authentication, signal the caller
		// with a non-zero exit code so it can fall back to manual token entry.
		if (App.EmitMode && !_emitHandled)
			Environment.Exit (2);
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

	private void EmitTokensAndExit (TeslaTokens tokens)
		{
		_emitHandled = true;

		try
			{
			var payload = new Dictionary<string, string>
				{
				["refresh_token"] = tokens.RefreshToken,
				["access_token"] = tokens.AccessToken ?? string.Empty,
				["email"] = tokens.Email ?? string.Empty
				};
			var encoded = Convert.ToBase64String (Encoding.UTF8.GetBytes (JsonSerializer.Serialize (payload)));
			Console.Out.WriteLine (TokenSentinel + encoded);
			Console.Out.Flush ();
			}
		catch (Exception exc)
			{
			TryWriteError ($"Unable to return tokens to the calling process: {exc.Message}");
			Environment.Exit (3);
			}

		Environment.Exit (0);
		}

	private void FailEmit (string message)
		{
		_emitHandled = true;
		TryWriteError (message);
		Environment.Exit (1);
		}

	private static void TryWriteError (string message)
		{
		try
			{
			Console.Error.WriteLine (message);
			Console.Error.Flush ();
			}
		catch (System.IO.IOException)
			{
			// No redirected error stream available ΓÇö nothing more we can do.
			}
		}

	private string SelectedRegion =>
		(RegionSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "us";

	private async void OnStartLoginClick (object sender, RoutedEventArgs e)
		{
		try
			{
			await StartLoginAsync ().ConfigureAwait (true);
			}
		catch (Exception exc)
			{
			ShowError ($"Login failed: {exc.Message}");
			}
		}

	private async Task StartLoginAsync ()
		{
		_exchangeStarted = false;
		_authRequest = TeslaAuth.BuildAuthUrl (SelectedRegion);

		ResultsPanel.Visibility = Visibility.Collapsed;
		PlaceholderPanel.Visibility = Visibility.Collapsed;
		LoginWebView.Visibility = Visibility.Visible;
		StartButton.IsEnabled = false;
		RegionSelector.IsEnabled = false;
		SetStatus ("Loading Tesla sign-in page...");

		await EnsureWebViewInitializedAsync ().ConfigureAwait (true);

		// Clear any cached Tesla session so each run starts from a fresh login.
		LoginWebView.CoreWebView2.CookieManager.DeleteAllCookies ();
		LoginWebView.CoreWebView2.Navigate (_authRequest.AuthUrl);
		}

	private async Task EnsureWebViewInitializedAsync ()
		{
		if (_webViewInitialized)
			return;

		// Use an isolated, app-specific user-data folder so login state never leaks into other apps.
		var userDataFolder = System.IO.Path.Combine (
			Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData),
			"TeslaPowerwallSetup",
			"WebView2");
		var environment = await CoreWebView2Environment.CreateAsync (userDataFolder: userDataFolder).ConfigureAwait (true);
		await LoginWebView.EnsureCoreWebView2Async (environment).ConfigureAwait (true);

		LoginWebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
		LoginWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
		LoginWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
		_webViewInitialized = true;
		}

	// Mirrors upstream _patch_pywebview_win32: intercept tesla:// before the browser tries to navigate.
	private async void OnNavigationStarting (object? sender, CoreWebView2NavigationStartingEventArgs e)
		{
		if (!TeslaAuth.TryParseCallback (e.Uri, out var callback))
			return;

		// tesla:// is not a real scheme ΓÇö cancel the navigation and complete the exchange ourselves.
		e.Cancel = true;

		if (_exchangeStarted)
			return;
		_exchangeStarted = true;

		try
			{
			await CompleteLoginAsync (callback).ConfigureAwait (true);
			}
		catch (Exception exc)
			{
			ShowError ($"Login failed: {exc.Message}");
			}
		}

	private async Task CompleteLoginAsync (TeslaCallback callback)
		{
		if (_authRequest is null)
			{
			ShowError ("Login state was lost. Click Start Login to try again.");
			return;
			}

		if (!string.IsNullOrEmpty (callback.Error))
			{
			ShowError ($"Tesla returned an error: {callback.Error}");
			return;
			}

		if (string.IsNullOrEmpty (callback.Code))
			{
			ShowError ("No authorization code was returned by Tesla.");
			return;
			}

		if (!string.Equals (callback.State, _authRequest.State, StringComparison.Ordinal))
			{
			ShowError ("Security check failed (CSRF state mismatch). Click Start Login to try again.");
			return;
			}

		LoginWebView.Visibility = Visibility.Collapsed;
		PlaceholderPanel.Visibility = Visibility.Visible;
		SetStatus ("Exchanging authorization code for tokens...");

		using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		var tokens = await TeslaAuth.ExchangeCodeAsync (
			callback.Code!,
			_authRequest.CodeVerifier,
			SelectedRegion,
			timeout.Token).ConfigureAwait (true);

		ShowTokens (tokens);
		}

	private void ShowTokens (TeslaTokens tokens)
		{
		if (App.EmitMode)
			{
			EmitTokensAndExit (tokens);
			return;
			}

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
		if (App.EmitMode && !_emitHandled)
			{
			FailEmit (message);
			return;
			}

		LoginWebView.Visibility = Visibility.Collapsed;
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
