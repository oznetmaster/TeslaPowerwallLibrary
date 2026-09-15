// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TeslaPowerwallLibrary.Setup;

/// <summary>Captures the registered Fleet callback before navigating to the external callback website.</summary>
internal sealed class FleetAuthorizationWindow : Window
	{
	private readonly WebView2 _browser = new ();
	private readonly DispatcherTimer _timeout = new () { Interval = TimeSpan.FromMinutes (5) };
	private readonly string _authorizeUrl;
	private readonly string _redirectUri;
	private readonly string _state;
	private bool _closed;

	internal string? CallbackUrl
		{
		get; private set;
		}
	internal string FailureMessage { get; private set; } = "Tesla sign-in was cancelled. No code was exchanged.";

	internal FleetAuthorizationWindow (string authorizeUrl, string redirectUri, string state)
		{
		_authorizeUrl = authorizeUrl;
		_redirectUri = redirectUri;
		_state = state;
		Title = "Tesla Fleet sign-in";
		Width = 620;
		Height = 820;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Content = _browser;
		Loaded += InitializeBrowser;
		_timeout.Tick += OnTimeout;
		}

	private async void InitializeBrowser (object sender, RoutedEventArgs e)
		{
		_timeout.Start ();
		try
			{
			string folder = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "TeslaPowerwallLibrary", "Setup", "FleetWebView2");
			var environment = await CoreWebView2Environment.CreateAsync (userDataFolder: folder);
			if (_closed)
				return;
			await _browser.EnsureCoreWebView2Async (environment);
			if (_closed)
				return;
			var browser = _browser.CoreWebView2;
			browser.Settings.AreDefaultContextMenusEnabled = false;
			browser.Settings.IsStatusBarEnabled = false;
			browser.CookieManager.DeleteAllCookies ();
			browser.NavigationStarting += OnNavigationStarting;
			browser.NewWindowRequested += (_, args) => args.Handled = true;
			browser.Navigate (_authorizeUrl);
			}
		catch
			{
			if (_closed)
				return;
			FailureMessage = "The sign-in window could not start. Check the WebView2 runtime, or use the manual browser option.";
			Close ();
			}
		}

	private void OnNavigationStarting (object? sender, CoreWebView2NavigationStartingEventArgs e)
		{
		if (!FleetSetupPreferences.IsCallbackAddress (e.Uri, _redirectUri))
			return;
		// Do not send the authorization code to the callback website, even when validation fails.
		e.Cancel = true;
		try
			{
			FleetSetupPreferences.ParseCallback (e.Uri, _redirectUri, _state);
			CallbackUrl = e.Uri;
			}
		catch
			{
			FailureMessage = "Tesla authorization was declined or the callback failed validation. Start a new sign-in.";
			}
		// Let the browser finish dispatching this event before disposing its control.
		Dispatcher.BeginInvoke (new Action (Close));
		}

	private void OnTimeout (object? sender, EventArgs e)
		{
		FailureMessage = "Tesla sign-in timed out. No code was exchanged.";
		Close ();
		}

	protected override void OnClosed (EventArgs e)
		{
		_closed = true;
		_timeout.Stop ();
		_browser.Dispose ();
		base.OnClosed (e);
		}
	}