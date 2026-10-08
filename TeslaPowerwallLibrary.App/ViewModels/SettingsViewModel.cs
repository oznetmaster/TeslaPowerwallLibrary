// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using TeslaPowerwallLibrary.Local;
using System.Security.Cryptography;
using System.Windows;
using TeslaPowerwallLibrary.Tools;
using TeslaPowerwallLibrary.Tedapi;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the Settings / controls screen: backup reserve, operation mode, grid charging, grid export, local backup and grid connection,
/// and cloud site selection. Reads current values on load and writes changes through the library facade.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
	{
	private readonly PowerwallConnectionService _connection;
	private bool _isLoading;
	private readonly Action<int> _savePollInterval;

	/// <summary>Gets or sets the local signing-key name shared with the connection screen.</summary>
	[ObservableProperty]
	private string _localSigningKeyName = AppSettingsStore.Load ().LocalSigningKeyName ?? LocalSigningKeyStore.DefaultKeyName;

	/// <summary>Gets or sets the last explicit enrollment result; never inferred from a successful request.</summary>
	[ObservableProperty]
	private string _localKeyStatus = "Key verification has not been checked.";

	/// <summary>Opens or creates the selected Windows key without contacting Tesla or the Powerwall.</summary>
	[RelayCommand]
	private void CreateLocalKey ()
		{
		try
			{
			using var key = LocalSigningKeyStore.CreateOrOpen (LocalSigningKeyName.Trim ());
			RememberLocalKey ();
			LocalKeyStatus = "Windows signing key is ready. It has not been registered or verified by this action.";
			}
		catch (Exception exc) when (exc is ArgumentException or InvalidOperationException or CryptographicException)
			{
			LocalKeyStatus = exc.Message;
			}
		}

	/// <summary>Checks the selected key through the already connected Owner or Fleet account.</summary>
	/// <returns>A task that completes after one status request.</returns>
	[RelayCommand]
	private Task CheckLocalKeyAsync () => RequestLocalKeyAsync (register: false);

	/// <summary>Starts enrollment only after explicit confirmation of the physical verification window.</summary>
	/// <returns>A task that completes after the enrollment request.</returns>
	[RelayCommand]
	private async Task RegisterLocalKeyAsync ()
		{
		if (!IsCloudMode || IsBusy)
			return;
		if (MessageBox.Show (
			$"Register the selected key for {_connection.SiteLabel ?? "the selected energy site"}?\n\n" +
			"This starts a limited physical-verification window (approximately ten minutes). " +
			"Proceed only when ready to complete the verification procedure for your system. " +
			"This application will not operate a Powerwall switch or retry enrollment automatically.",
			"Register local signing key", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
			return;
		await RequestLocalKeyAsync (register: true).ConfigureAwait (true);
		}

	/// <summary>Performs one enrollment operation and presents only the reported authorization state.</summary>
	/// <param name="register">Whether the user explicitly requested registration rather than a status read.</param>
	/// <returns>A task that completes when the one cloud operation finishes.</returns>
	private async Task RequestLocalKeyAsync (bool register)
		{
		if (!IsCloudMode || IsBusy)
			return;
		IsBusy = true;
		try
			{
			using var key = LocalSigningKeyStore.Open (LocalSigningKeyName.Trim ());
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (60));
			var result = register
				? await _connection.Powerwall.RegisterLocalKeyAsync (key, "TeslaPowerwall desktop local client", cts.Token).ConfigureAwait (true)
				: await _connection.Powerwall.GetLocalKeyStatusAsync (key, cts.Token).ConfigureAwait (true);
			RememberLocalKey ();
			LocalKeyStatus = result.State switch
				{
				LocalKeyState.PendingVerification => "Awaiting physical verification. Complete the coordinated procedure, then check status. No automatic retry.",
				LocalKeyState.VerificationTimedOut => "Physical verification timed out. The same key can be registered again when you are ready.",
				LocalKeyState.Verified => "Key verified. You can now select signed LAN access on the connection screen.",
				LocalKeyState.Removed => "This key has been removed from the device.",
				_ => "Authorization is unknown or the key was not found. Signed access is not confirmed."
				};
			LocalKeyStatus += $"\nPublic-key fingerprint: {result.Fingerprint}";
			}
		catch (Exception exc) when (exc is PowerwallException or ArgumentException or InvalidOperationException or CryptographicException or OperationCanceledException)
			{
			LocalKeyStatus = register
				? "Enrollment outcome is uncertain. Check this key's status before retrying. " + exc.Message
				: "Unable to check key verification. " + exc.Message;
			}
		finally
			{
			IsBusy = false;
			}
		}

	/// <summary>Preserves all connection settings while remembering the non-secret signing-key name.</summary>
	private void RememberLocalKey ()
		{
		var settings = AppSettingsStore.Load ();
		settings.LocalSigningKeyName = LocalSigningKeyName.Trim ();
		AppSettingsStore.Save (settings);
		}


	/// <summary>Raised when the user requests to switch accounts (sign out and return to the connect screen).</summary>
	public event EventHandler? SwitchAccountRequested;

	/// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public SettingsViewModel (PowerwallConnectionService connection) : this (connection, SavePollInterval) { }

	/// <summary>Creates Settings with an injectable preference store for offline tests.</summary>
	/// <param name="connection">Shared desktop connection.</param>
	/// <param name="savePollInterval">Persists only the local refresh preference.</param>
	internal SettingsViewModel (PowerwallConnectionService connection, Action<int> savePollInterval)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		_savePollInterval = savePollInterval ?? throw new ArgumentNullException (nameof (savePollInterval));
		LocalPollSecondsText = connection.LocalPollInterval.TotalSeconds.ToString (System.Globalization.CultureInfo.InvariantCulture);
		Modes = new ObservableCollection<string> (new[] { "self_consumption", "autonomous", "backup" });
		ExportRules = new ObservableCollection<string> (new[] { "battery_ok", "pv_only", "never" });
		Sites = new ObservableCollection<CloudSite> ();
		}

	/// <summary>Gets whether the local network identity card is relevant.</summary>
	public bool IsLocalMode => _connection.Mode == PowerwallMode.Local;

	/// <summary>Gets or sets the configured hostname, retained independently of DHCP addresses.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (ShowResolvedAddress))]
	private string? _localHostname;

	/// <summary>Gets whether resolving adds information beyond an explicitly configured IPv4 or IPv6 literal.</summary>
	public bool ShowResolvedAddress => !string.IsNullOrWhiteSpace (LocalHostname) && !IsAddressLiteral (LocalHostname);

	private static bool IsAddressLiteral (string host) => System.Net.IPAddress.TryParse (host, out _)
		|| (Uri.TryCreate ("https://" + host + "/", UriKind.Absolute, out var endpoint)
			&& System.Net.IPAddress.TryParse (endpoint.DnsSafeHost.Trim ('[', ']'), out _));

	/// <summary>Gets or sets the editable refresh interval text; zero requests manual refresh.</summary>
	[ObservableProperty]
	private string _localPollSecondsText = "5";

	/// <summary>Gets the refresh cadence currently in effect, independently of pending input.</summary>
	public string LocalPollingStatus => _connection.LocalPollInterval == TimeSpan.Zero ? "Currently manual refresh only."
		: $"Currently refreshing every {_connection.LocalPollInterval.TotalSeconds:0} seconds after the previous read completes.";

	/// <summary>Applies the desktop refresh preference without changing any hardware setting or enabling controls.</summary>
	/// <returns>A task completing when the connection and cache use the requested interval.</returns>
	[RelayCommand]
	private async Task ApplyLocalPollIntervalAsync ()
		{
		if (IsBusy || !IsLocalMode) return;
		if (!int.TryParse (LocalPollSecondsText, out int seconds) || seconds < 0 || seconds > 3600)
			{
			StatusMessage = "Enter a whole number from 1 to 3600 seconds, or 0 for manual refresh.";
			return;
			}
		IsBusy = true;
		StatusMessage = "Updating the refresh interval…";
		try
			{
			using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			if (await _connection.SetLocalPollIntervalAsync (seconds, deadline.Token).ConfigureAwait (true))
				{
				_savePollInterval (seconds);
				StatusMessage = "Refresh interval updated. No Powerwall settings have been changed.";
				}
			else StatusMessage = "Could not update the refresh interval. The previous interval is still in use.";
			}
		catch (Exception exc) when (exc is PowerwallException or OperationCanceledException or CryptographicException
			or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
			{ StatusMessage = "Could not update the refresh interval: " + exc.Message; }
		finally
			{
			OnPropertyChanged (nameof (LocalPollingStatus));
			IsBusy = false;
			}
		}

	private static void SavePollInterval (int seconds)
		{
		var settings = AppSettingsStore.Load ();
		settings.LocalPollSeconds = seconds;
		AppSettingsStore.Save (settings);
		}

	/// <summary>Gets or sets freshly resolved address candidates, including IPv6 interface scope where required.</summary>
	[ObservableProperty]
	private string _localIpAddresses = "Not resolved";

	/// <summary>Refreshes local name resolution without logging in, polling telemetry or changing configuration.</summary>
	/// <param name="host">Configured host, or null to clear a previous connection.</param>
	/// <param name="resolve">Optional offline resolver for presentation tests.</param>
	/// <returns>A task completing after bounded name resolution and presentation updates.</returns>
	internal async Task RefreshLocalAddressAsync (string? host, Func<string, CancellationToken, Task<PowerwallHost>>? resolve = null)
		{
		LocalHostname = host;
		LocalIpAddresses = host is null ? "Unavailable" : "Resolving…";
		if (host is null) return;
		if (!ShowResolvedAddress) { LocalIpAddresses = string.Empty; return; }
		try
			{
			using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (5));
			PowerwallHost result = await (resolve ?? PowerwallDiscovery.ResolveLanAsync) (host, deadline.Token).ConfigureAwait (true);
			var ipv4 = result.Addresses.Where (a => a.AddressFamily == AddressFamily.InterNetwork).ToArray ();
			LocalIpAddresses = result.Addresses.Count == 0 ? "No addresses found"
				: string.Join (Environment.NewLine, (ipv4.Length > 0 ? ipv4 : result.Addresses).Select (address => address.ToString ()).Distinct ());
			}
		catch (OperationCanceledException) { LocalIpAddresses = "Name lookup timed out"; }
		catch (SocketException) { LocalIpAddresses = "Name lookup unavailable"; }
		catch (ArgumentException) { LocalIpAddresses = "Invalid configured hostname"; }
		}

	/// <summary>Gets the selectable operation modes.</summary>
	public ObservableCollection<string> Modes { get; }

	/// <summary>Gets the selectable grid export rules.</summary>
	public ObservableCollection<string> ExportRules { get; }

	/// <summary>Gets the available Tesla™ energy sites (cloud mode only).</summary>
	public ObservableCollection<CloudSite> Sites { get; }

	/// <summary>Gets a value indicating whether cloud-only controls are available.</summary>
	public bool IsCloudMode => _connection.Mode is PowerwallMode.Cloud or PowerwallMode.FleetApi;

	/// <summary>Gets whether signed local controls and backup reads are supported by the current connection.</summary>
	public bool IsSignedLocal => _connection.Mode == PowerwallMode.Local && _connection.LocalProtocol == PowerwallLocalProtocol.TedapiSigned;

	/// <summary>Gets whether settings writes were explicitly enabled for the connection.</summary>
	public bool CanChangeSettings => IsCloudMode || (_connection.Mode == PowerwallMode.Local && _connection.AllowsLocalControl);

	/// <summary>Gets whether Settings should offer the explicit session control opt-in.</summary>
	public bool CanEnableLocalControls => _connection.CanEnableLocalControls;

	/// <summary>Gets the action offered by the session permission button.</summary>
	public string LocalControlButtonText => CanChangeSettings ? "Return to read-only" : "Enable controls for this session";

	/// <summary>Switches local control permission without applying any pending setting values.</summary>
	/// <returns>A task completing after the new session permission has been established.</returns>
	[RelayCommand]
	private Task ToggleLocalControlsAsync () => ChangeLocalControlsAsync (!CanChangeSettings);

	/// <summary>Gets the current local permission state and when changes take effect.</summary>
	public string LocalControlStatus => CanChangeSettings
		? "Controls enabled for this session. Edit a value, then press Apply to change the Powerwall."
		: "Read-only connection. Enable controls for this session to change settings. The app starts read-only each time.";

	/// <summary>Enables explicit local writes without applying any field values or persisting control permission.</summary>
	/// <returns>A task completing after the same local connection has been authenticated.</returns>
	[RelayCommand]
	private Task EnableLocalControlsAsync () => ChangeLocalControlsAsync (true);

	private async Task ChangeLocalControlsAsync (bool enable)
		{
		if (IsBusy || !IsLocalMode || CanChangeSettings == enable) return;
		IsBusy = true;
		StatusMessage = enable ? "Enabling controls for this session…" : "Returning to read-only…";
		try
			{
			using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			bool changed = enable
				? await _connection.EnableLocalControlsAsync (deadline.Token).ConfigureAwait (true)
				: await _connection.DisableLocalControlsAsync (deadline.Token).ConfigureAwait (true);
			StatusMessage = changed
				? (enable ? "Controls enabled. No Powerwall settings have been changed." : "Read-only connection restored. No Powerwall settings have been changed.")
				: PermissionChangeFailure ();
			}
		catch (Exception exc) when (exc is PowerwallException or OperationCanceledException or CryptographicException
			or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
			{
			StatusMessage = PermissionChangeFailure () + " " + exc.Message;
			}
		finally
			{
			OnPropertyChanged (nameof (CanChangeSettings));
			OnPropertyChanged (nameof (CanEnableLocalControls));
			OnPropertyChanged (nameof (LocalControlStatus));
			OnPropertyChanged (nameof (LocalControlButtonText));
			IsBusy = false;
			}
		}

	private string PermissionChangeFailure () => CanChangeSettings
		? "Could not return to read-only. Controls remain enabled; retry or disconnect."
		: "Controls could not be enabled. The existing connection remains read-only.";

	/// <summary>Gets whether grid preferences can be read for this connection.</summary>
	public bool HasGridSettings => IsCloudMode || IsSignedLocal;

	/// <summary>Gets or sets the explicitly requested manual backup duration in minutes.</summary>
	[ObservableProperty]
	private int _backupMinutes = 60;

	/// <summary>Gets or sets the last reported local backup event information.</summary>
	[ObservableProperty]
	private string _backupStatus = "Backup events have not been read.";

	/// <summary>Reads current backup events without changing them.</summary>
	/// <returns>A task completing when the event response has been displayed.</returns>
	[RelayCommand]
	private async Task ReadLocalBackupAsync ()
		{
		if (!IsSignedLocal || IsBusy)
			return;
		await RunWriteAsync (async (powerwall, token) =>
			{
			var events = await powerwall.GetLocalBackupEventsAsync (token).ConfigureAwait (true);
			BackupStatus = events.ManualBackup is { } manual
				? $"Reported manual backup start: {manual.StartTime?.ToString ("g") ?? "unreported"}; duration: {manual.DurationSeconds} seconds."
				: "No manual backup event reported.";
			BackupStatus += $" Other reported events: {events.Events.Count}.";
			}).ConfigureAwait (true);
		}

	/// <summary>Replaces manual backup only after explicit confirmation of the requested duration.</summary>
	/// <returns>A task completing when the gateway acknowledges or rejects the request.</returns>
	[RelayCommand]
	private async Task StartLocalBackupAsync ()
		{
		if (!IsSignedLocal || !CanChangeSettings || IsBusy)
			return;
		if (BackupMinutes < 1 || BackupMinutes > 1440)
			{
			StatusMessage = "Choose a backup duration between 1 and 1440 minutes.";
			return;
			}
		if (!ConfirmLocalControl ($"Replace any existing manual backup event with maximum backup for {BackupMinutes} minutes?"))
			return;
		await RunWriteAsync (async (powerwall, token) =>
			{
			var result = await powerwall.ScheduleLocalMaxBackupAsync (TimeSpan.FromMinutes (BackupMinutes), token).ConfigureAwait (true);
			StatusMessage = result.Acknowledged ? "Backup request acknowledged. Read backup events to confirm the schedule." : "Backup request was not acknowledged.";
			}).ConfigureAwait (true);
		}

	/// <summary>Cancels the local manual backup event after explicit confirmation.</summary>
	/// <returns>A task completing when the gateway responds.</returns>
	[RelayCommand]
	private async Task CancelLocalBackupAsync ()
		{
		if (!IsSignedLocal || !CanChangeSettings || IsBusy || !ConfirmLocalControl ("Cancel the current manual backup event?"))
			return;
		await RunWriteAsync (async (powerwall, token) =>
			{
			var result = await powerwall.CancelLocalMaxBackupAsync (token).ConfigureAwait (true);
			StatusMessage = result.Acknowledged ? "Backup cancellation acknowledged." : "Backup cancellation was not acknowledged.";
			}).ConfigureAwait (true);
		}

	/// <summary>Requests intentional islanding only after explicit confirmation.</summary>
	/// <returns>A task completing after the command response.</returns>
	[RelayCommand]
	private Task GoOffGridAsync () => ChangeGridAsync (connect: false);

	/// <summary>Requests reconnection to the utility grid after explicit confirmation.</summary>
	/// <returns>A task completing after the command response.</returns>
	[RelayCommand]
	private Task ReconnectGridAsync () => ChangeGridAsync (connect: true);

	/// <summary>Issues a grid command without treating its acknowledgement as proof of physical state.</summary>
	/// <param name="connect">True to reconnect; false to request islanding.</param>
	/// <returns>A task completing when the request has been answered.</returns>
	private async Task ChangeGridAsync (bool connect)
		{
		if (!IsSignedLocal || !CanChangeSettings || IsBusy)
			return;
		string prompt = connect ? "Request reconnection to the utility grid?"
			: "Disconnect the house from the utility grid? The house will depend on available battery and solar power and may lose power if those cannot supply it.";
		if (!ConfirmLocalControl (prompt))
			return;
		await RunWriteAsync (async (powerwall, token) =>
			{
			var result = connect ? await powerwall.ReconnectGridAsync (token).ConfigureAwait (true)
				: await powerwall.GoOffGridAsync (token).ConfigureAwait (true);
			StatusMessage = result.Acknowledged ? "Grid command acknowledged. Refresh Home to check the reported grid state." : "Grid command was not acknowledged.";
			}).ConfigureAwait (true);
		}

	/// <summary>Confirms an explicit power-affecting request, with cancellation selected by default.</summary>
	/// <param name="prompt">Description of the proposed action.</param>
	/// <returns>Whether the user confirmed this action.</returns>
	private static bool ConfirmLocalControl (string prompt) =>
		MessageBox.Show (prompt, "Confirm Powerwall control", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
			MessageBoxResult.Cancel) == MessageBoxResult.OK;

	/// <summary>Gets a value indicating whether Storm Watch is available (Tesla Owners cloud mode only; not exposed by FleetAPI).</summary>
	public bool IsStormWatchAvailable => _connection.Mode == PowerwallMode.Cloud;

	/// <summary>Gets the customer email of the active connection, for display next to "Switch account".</summary>
	public string? Email => _connection.Email;

	/// <summary>Gets or sets the backup reserve percentage.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (ReserveText))]
	[NotifyPropertyChangedFor (nameof (HasReservePercent))]
	[NotifyPropertyChangedFor (nameof (ReserveSliderPercent))]
	private double? _reservePercent;

	/// <summary>Gets or sets the selected operation mode.</summary>
	[ObservableProperty]
	private string? _selectedMode;

	/// <summary>Gets or sets a value indicating whether grid charging is allowed.</summary>
	[ObservableProperty]
	private bool? _gridChargingEnabled;

	/// <summary>Gets or sets the selected grid export rule.</summary>
	[ObservableProperty]
	private string? _selectedExportRule;

	/// <summary>Gets or sets a value indicating whether Storm Watch is enabled.</summary>
	[ObservableProperty]
	private bool? _stormWatchEnabled;

	/// <summary>Gets or sets the selected site.</summary>
	[ObservableProperty]
	private CloudSite? _selectedSite;

	/// <summary>Gets or sets the slider position. The slider is hidden when the nullable reserve reading is unavailable.</summary>
	public double ReserveSliderPercent
		{
		get => ReservePercent ?? 0;
		set => ReservePercent = value;
		}

	/// <summary>Gets whether a reserve reading is available to position the slider without inventing a value.</summary>
	public bool HasReservePercent => ReservePercent.HasValue;

	/// <summary>Gets the formatted reserve percentage.</summary>
	public string ReserveText => ReservePercent is double reserve ? $"{reserve:0}%" : "Unavailable";

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
		OnPropertyChanged (nameof (IsCloudMode));
		OnPropertyChanged (nameof (IsSignedLocal));
		OnPropertyChanged (nameof (IsLocalMode));
		LocalPollSecondsText = _connection.LocalPollInterval.TotalSeconds.ToString (System.Globalization.CultureInfo.InvariantCulture);
		OnPropertyChanged (nameof (LocalPollingStatus));
		OnPropertyChanged (nameof (CanChangeSettings));
		OnPropertyChanged (nameof (CanEnableLocalControls));
		OnPropertyChanged (nameof (LocalControlStatus));
		OnPropertyChanged (nameof (LocalControlButtonText));
		OnPropertyChanged (nameof (HasGridSettings));
		OnPropertyChanged (nameof (IsStormWatchAvailable));
		ReservePercent = null;
		SelectedMode = null;
		GridChargingEnabled = null;
		StormWatchEnabled = null;
		SelectedExportRule = null;
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			var powerwall = _connection.Powerwall;
			await RefreshLocalAddressAsync (powerwall.LocalHost).ConfigureAwait (true);

			var reserve = await powerwall.GetReserveAsync (cancellationToken: cts.Token).ConfigureAwait (true);
			ReservePercent = reserve;

			SelectedMode = await powerwall.GetModeAsync (cancellationToken: cts.Token).ConfigureAwait (true);

			if (HasGridSettings)
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
		if (IsBusy || !CanChangeSettings)
			return;
		await RunWriteAsync (async (powerwall, token) =>
			{
			await powerwall.SetOperationAsync (ReservePercent is double reserve ? Math.Round (reserve) : null, SelectedMode, token).ConfigureAwait (true);
			StatusMessage = "Operation settings applied.";
			}).ConfigureAwait (true);
		}

	/// <summary>Applies the grid charging preference where supported.</summary>
	/// <returns>A task that completes when grid charging has been written.</returns>
	[RelayCommand]
	private async Task ApplyGridChargingAsync ()
		{
		if (IsBusy || !HasGridSettings || !CanChangeSettings || GridChargingEnabled is null)
			return;

		await RunWriteAsync (async (powerwall, token) =>
			{
			await powerwall.SetGridChargingAsync (GridChargingEnabled!.Value, token).ConfigureAwait (true);
			StatusMessage = "Grid charging updated.";
			}).ConfigureAwait (true);
		}

	/// <summary>Applies the grid export rule where supported.</summary>
	/// <returns>A task that completes when the export rule has been written.</returns>
	[RelayCommand]
	private async Task ApplyGridExportAsync ()
		{
		if (IsBusy || !HasGridSettings || !CanChangeSettings || string.IsNullOrWhiteSpace (SelectedExportRule))
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
		if (!IsStormWatchAvailable || StormWatchEnabled is null)
			return;

		await RunWriteAsync (async (powerwall, token) =>
			{
			await powerwall.SetStormWatchAsync (StormWatchEnabled!.Value, token).ConfigureAwait (true);
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
		GridChargingEnabled = await powerwall.GetGridChargingAsync (cancellationToken: token).ConfigureAwait (true);
		SelectedExportRule = await powerwall.GetGridExportAsync (cancellationToken: token).ConfigureAwait (true);

		// Storm Watch is intentionally not exposed in FleetAPI mode.
		if (IsStormWatchAvailable)
			StormWatchEnabled = await powerwall.GetStormWatchAsync (cancellationToken: token).ConfigureAwait (true);

		if (!IsCloudMode)
			return;
		var sites = await powerwall.GetSitesAsync (token).ConfigureAwait (true);
		Sites.Clear ();
		foreach (var site in sites)
			Sites.Add (site);

		// Preselect the site the library resolved on connect (its remembered or default site), without
		// triggering a redundant switch since _isLoading is set.
		var rememberedSiteId = powerwall.CloudSiteId ?? powerwall.FleetApiSiteId;
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
