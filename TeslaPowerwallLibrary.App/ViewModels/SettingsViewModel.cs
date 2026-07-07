// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
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
/// Drives the Settings / controls screen: backup reserve, operation mode, and (cloud-only) grid charging,
/// grid export, and site selection. Reads current values on load and writes changes through the library facade.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
	{
	private readonly PowerwallConnectionService _connection;
	private bool _isLoading;

	/// <summary>Raised when the user requests to switch accounts (sign out and return to the connect screen).</summary>
	public event EventHandler? SwitchAccountRequested;

	/// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public SettingsViewModel (PowerwallConnectionService connection)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		Modes = new ObservableCollection<string> (new[] { "self_consumption", "autonomous", "backup" });
		ExportRules = new ObservableCollection<string> (new[] { "battery_ok", "pv_only", "never" });
		Sites = new ObservableCollection<CloudSite> ();
		}

	/// <summary>Gets the selectable operation modes.</summary>
	public ObservableCollection<string> Modes { get; }

	/// <summary>Gets the selectable grid export rules.</summary>
	public ObservableCollection<string> ExportRules { get; }

	/// <summary>Gets the available Tesla™ energy sites (cloud mode only).</summary>
	public ObservableCollection<CloudSite> Sites { get; }

	/// <summary>Gets a value indicating whether cloud-only controls are available.</summary>
	public bool IsCloudMode => _connection.Mode == PowerwallMode.Cloud;

	/// <summary>Gets the customer email of the active connection, for display next to "Switch account".</summary>
	public string? Email => _connection.Email;

	/// <summary>Gets or sets the backup reserve percentage.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (ReserveText))]
	private double _reservePercent;

	/// <summary>Gets or sets the selected operation mode.</summary>
	[ObservableProperty]
	private string? _selectedMode;

	/// <summary>Gets or sets a value indicating whether grid charging is allowed.</summary>
	[ObservableProperty]
	private bool _gridChargingEnabled;

	/// <summary>Gets or sets the selected grid export rule.</summary>
	[ObservableProperty]
	private string? _selectedExportRule;

	/// <summary>Gets or sets a value indicating whether Storm Watch is enabled.</summary>
	[ObservableProperty]
	private bool _stormWatchEnabled;

	/// <summary>Gets or sets the selected site.</summary>
	[ObservableProperty]
	private CloudSite? _selectedSite;

	/// <summary>Gets the formatted reserve percentage.</summary>
	public string ReserveText => $"{ReservePercent:0}%";

	/// <summary>Loads the current settings from the connected system.</summary>
	/// <returns>A task that completes when the current settings have been read.</returns>
	[RelayCommand]
	private async Task LoadAsync ()
		{
		StatusMessage = null;
		IsBusy = true;
		_isLoading = true;
		// The connection's account may have changed (switch account / sign in again) since this view-model
		// was last shown, so refresh the bound email every time the screen loads.
		OnPropertyChanged (nameof (Email));
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			var powerwall = _connection.Powerwall;

			var reserve = await powerwall.GetReserveAsync (cancellationToken: cts.Token).ConfigureAwait (true);
			if (reserve is double r)
				ReservePercent = r;

			SelectedMode = await powerwall.GetModeAsync (cancellationToken: cts.Token).ConfigureAwait (true);

			if (IsCloudMode)
				await LoadCloudSettingsAsync (powerwall, cts.Token).ConfigureAwait (true);
			}
		catch (OperationCanceledException)
			{
			StatusMessage = "Loading settings timed out.";
			}
		catch (PowerwallException exc)
			{
			StatusMessage = $"Could not load settings: {exc.Message}";
			}
		finally
			{
			_isLoading = false;
			IsBusy = false;
			}
		}

	/// <summary>Applies the backup reserve and operation mode to the connected system.</summary>
	/// <returns>A task that completes when the operation settings have been written.</returns>
	[RelayCommand]
	private async Task ApplyOperationAsync ()
		{
		await RunWriteAsync (async (powerwall, token) =>
			{
			await powerwall.SetOperationAsync (Math.Round (ReservePercent), SelectedMode, token).ConfigureAwait (true);
			StatusMessage = "Operation settings applied.";
			}).ConfigureAwait (true);
		}

	/// <summary>Applies the grid charging preference (cloud mode only).</summary>
	/// <returns>A task that completes when grid charging has been written.</returns>
	[RelayCommand]
	private async Task ApplyGridChargingAsync ()
		{
		if (!IsCloudMode)
			return;

		await RunWriteAsync (async (powerwall, token) =>
			{
			await powerwall.SetGridChargingAsync (GridChargingEnabled, token).ConfigureAwait (true);
			StatusMessage = "Grid charging updated.";
			}).ConfigureAwait (true);
		}

	/// <summary>Applies the grid export rule (cloud mode only).</summary>
	/// <returns>A task that completes when the export rule has been written.</returns>
	[RelayCommand]
	private async Task ApplyGridExportAsync ()
		{
		if (!IsCloudMode || string.IsNullOrWhiteSpace (SelectedExportRule))
			return;

		await RunWriteAsync (async (powerwall, token) =>
			{
			await powerwall.SetGridExportAsync (SelectedExportRule!, token).ConfigureAwait (true);
			StatusMessage = "Grid export rule updated.";
			}).ConfigureAwait (true);
		}

	/// <summary>Applies the Storm Watch preference (cloud mode only).</summary>
	/// <returns>A task that completes when Storm Watch has been written.</returns>
	[RelayCommand]
	private async Task ApplyStormWatchAsync ()
		{
		if (!IsCloudMode)
			return;

		await RunWriteAsync (async (powerwall, token) =>
			{
			await powerwall.SetStormWatchAsync (StormWatchEnabled, token).ConfigureAwait (true);
			StatusMessage = "Storm Watch updated.";
			}).ConfigureAwait (true);
		}

	/// <summary>Requests that the app sign out of the current account and return to the connect screen.</summary>
	[RelayCommand]
	private void SwitchAccount () => SwitchAccountRequested?.Invoke (this, EventArgs.Empty);

	partial void OnSelectedSiteChanged (CloudSite? value)
		{
		if (_isLoading || value is null || !IsCloudMode)
			return;

		_ = SwitchSiteAsync (value.SiteId);
		}

	private async Task SwitchSiteAsync (string siteId)
		{
		await RunWriteAsync (async (powerwall, token) =>
			{
			if (await powerwall.ChangeSiteAsync (siteId, token).ConfigureAwait (true))
				{
				StatusMessage = "Active site changed.";
				var site = Sites.FirstOrDefault (s => s.SiteId == siteId);
				_connection.SetSiteLabel (site?.SiteName ?? site?.SiteId ?? siteId);
				}
			else
				{
				StatusMessage = "That site could not be selected.";
				}
			}).ConfigureAwait (true);
		}

	private async Task LoadCloudSettingsAsync (Powerwall powerwall, CancellationToken token)
		{
		GridChargingEnabled = await powerwall.GetGridChargingAsync (cancellationToken: token).ConfigureAwait (true) ?? false;
		SelectedExportRule = await powerwall.GetGridExportAsync (cancellationToken: token).ConfigureAwait (true);
		StormWatchEnabled = await powerwall.GetStormWatchAsync (cancellationToken: token).ConfigureAwait (true) ?? false;

		var sites = await powerwall.GetSitesAsync (token).ConfigureAwait (true);
		Sites.Clear ();
		foreach (var site in sites)
			Sites.Add (site);

		// Preselect the site the library resolved on connect (its remembered or default site), without
		// triggering a redundant switch since _isLoading is set.
		var rememberedSiteId = powerwall.CloudSiteId;
		SelectedSite = Sites.FirstOrDefault (s => s.SiteId == rememberedSiteId) ?? Sites.FirstOrDefault ();

		// Keep the shared connection label in sync in case this screen resolves the site before Connect did.
		if (SelectedSite is not null)
			_connection.SetSiteLabel (SelectedSite.SiteName ?? SelectedSite.SiteId);
		}

	private async Task RunWriteAsync (Func<Powerwall, CancellationToken, Task> write)
		{
		StatusMessage = null;
		IsBusy = true;
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			await write (_connection.Powerwall, cts.Token).ConfigureAwait (true);
			}
		catch (OperationCanceledException)
			{
			StatusMessage = "The operation timed out.";
			}
		catch (ArgumentException exc)
			{
			StatusMessage = exc.Message;
			}
		catch (PowerwallException exc)
			{
			StatusMessage = $"Operation failed: {exc.Message}";
			}
		finally
			{
			IsBusy = false;
			}
		}
	}
