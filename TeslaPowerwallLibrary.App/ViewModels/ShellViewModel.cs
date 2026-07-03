// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeslaPowerwallLibrary.App.Services;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Identifies the primary navigable screens of the desktop app.
/// </summary>
public enum AppScreen
	{
	/// <summary>The connect / login screen.</summary>
	Connect,

	/// <summary>The Home power-flow screen.</summary>
	Home,

	/// <summary>The Energy history screen.</summary>
	Energy,

	/// <summary>The Settings / controls screen.</summary>
	Settings,

	/// <summary>The System information screen.</summary>
	System
	}

/// <summary>
/// Root view-model that hosts navigation between the connect screen and the four main screens, owning the
/// shared <see cref="PowerwallConnectionService"/> and each screen's view-model.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
	{
	private readonly PowerwallConnectionService _connection;

	/// <summary>Initializes a new instance of the <see cref="ShellViewModel"/> class.</summary>
	public ShellViewModel ()
		{
		_connection = new PowerwallConnectionService ();

		Connect = new ConnectViewModel (_connection);
		Home = new HomeViewModel (_connection);
		Energy = new EnergyViewModel (_connection);
		Settings = new SettingsViewModel (_connection);
		System = new SystemViewModel (_connection);

		Connect.Connected += OnConnected;

		_current = Connect;
		_currentScreen = AppScreen.Connect;
		}

	/// <summary>Gets the connect screen view-model.</summary>
	public ConnectViewModel Connect { get; }

	/// <summary>Gets the Home screen view-model.</summary>
	public HomeViewModel Home { get; }

	/// <summary>Gets the Energy screen view-model.</summary>
	public EnergyViewModel Energy { get; }

	/// <summary>Gets the Settings screen view-model.</summary>
	public SettingsViewModel Settings { get; }

	/// <summary>Gets the System screen view-model.</summary>
	public SystemViewModel System { get; }

	/// <summary>Gets or sets the currently displayed view-model.</summary>
	[ObservableProperty]
	private ObservableObject _current;

	/// <summary>Gets or sets the currently active screen.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (IsConnected))]
	private AppScreen _currentScreen;

	/// <summary>Gets a value indicating whether a live connection exists (any screen other than Connect).</summary>
	public bool IsConnected => _connection.IsConnected;

	/// <summary>
	/// Runs a one-time startup auto-connect that reuses saved credentials (Tesla tokens or a local gateway
	/// login) so a returning user is signed in without a fresh login. Falls back to the connect screen when
	/// no saved credentials exist or the reconnect fails.
	/// </summary>
	/// <returns>A task that completes when the startup connect attempt finishes.</returns>
	public Task InitializeAsync () => Connect.TryAutoConnectAsync ();

	/// <summary>Navigates to the specified screen. Navigation to a data screen is ignored when not connected.</summary>
	/// <param name="screen">The screen to display.</param>
	[RelayCommand]
	private void Navigate (AppScreen screen)
		{
		if (screen != AppScreen.Connect && !_connection.IsConnected)
			return;

		Current = screen switch
			{
			AppScreen.Connect => Connect,
			AppScreen.Home => Home,
			AppScreen.Energy => Energy,
			AppScreen.Settings => Settings,
			AppScreen.System => System,
			_ => Current
			};
		CurrentScreen = screen;

		// Kick off a one-shot load for screens that read on demand (Home updates via the polling loop).
		switch (screen)
			{
			case AppScreen.Energy when Energy.LoadCommand.CanExecute (null):
				Energy.LoadCommand.Execute (null);
				break;
			case AppScreen.Settings when Settings.LoadCommand.CanExecute (null):
				Settings.LoadCommand.Execute (null);
				break;
			case AppScreen.System when System.LoadCommand.CanExecute (null):
				System.LoadCommand.Execute (null);
				break;
			}
		}

	/// <summary>Signs out of the current account, tears down the connection, and returns to the connect screen.</summary>
	/// <returns>A task that completes once the connection has been released and the connect screen is ready.</returns>
	[RelayCommand]
	private async Task DisconnectAsync ()
		{
		await _connection.DisconnectAsync ().ConfigureAwait (true);
		Connect.PrepareForSignIn ();
		Navigate (AppScreen.Connect);
		OnPropertyChanged (nameof (IsConnected));
		}

	private void OnConnected (object? sender, EventArgs e)
		{
		_connection.StartPolling ();
		OnPropertyChanged (nameof (IsConnected));
		Navigate (AppScreen.Home);
		}

	/// <summary>Releases the connection and associated resources.</summary>
	public void Dispose ()
		{
		Connect.Connected -= OnConnected;
		_connection.Dispose ();
		}
	}
