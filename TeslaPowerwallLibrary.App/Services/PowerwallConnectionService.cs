// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Security.Cryptography;
using System.Net.Http;
using System.Text.Json;
using TeslaPowerwallLibrary.Tools;
using System.Threading.Tasks;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// Owns the live <see cref="Powerwall"/> connection for the desktop app and runs a cancellable polling loop
/// that periodically refreshes a <see cref="PowerFlowSnapshot"/>. View-models subscribe to
/// <see cref="SnapshotUpdated"/> to receive updates on a background thread; the UI marshals to the dispatcher.
/// </summary>
public sealed class PowerwallConnectionService : IDisposable
	{
	/// <summary>Poll cadence for local gateway connections, which serve fresh data on every request.</summary>
	private TimeSpan _localPollInterval = TimeSpan.FromSeconds (5);

	/// <summary>Gets or sets the delay between completed local refreshes; zero means manual refresh only.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The interval is negative or a nonzero interval is less than one second.</exception>
	public TimeSpan LocalPollInterval
		{
		get => _localPollInterval;
		set
			{
			if (value < TimeSpan.Zero || (value > TimeSpan.Zero && value < TimeSpan.FromSeconds (1)) || value.TotalSeconds > 3600)
				throw new ArgumentOutOfRangeException (nameof (value), "Use zero for manual refresh or 1–3600 seconds.");
			_localPollInterval = value;
			}
		}


	/// <summary>
	/// Poll cadence for cloud-backed connections (Owners API / FleetAPI). Tesla™ only refreshes cloud data
	/// every few minutes, but the client cannot know where it sits in that cycle, so a one-minute poll
	/// bounds worst-case staleness while staying light on the API.
	/// </summary>
	private static readonly TimeSpan _cloudPollInterval = TimeSpan.FromMinutes (1);

	private Powerwall? _powerwall;
	private RSA? _localSigningKey;
	private PowerwallOptions? _connectionOptions;
	private string? _localKeyName;
	private readonly Func<Powerwall, CancellationToken, Task<bool>> _authenticate;

	/// <summary>Creates a desktop connection service using normal library authentication.</summary>
	public PowerwallConnectionService () : this ((powerwall, token) => powerwall.ConnectAsync (token)) { }

	/// <summary>Creates a service with an injectable authentication boundary for offline connection tests.</summary>
	/// <param name="authenticate">Authenticates a candidate without changing device settings.</param>
	internal PowerwallConnectionService (Func<Powerwall, CancellationToken, Task<bool>> authenticate) =>
		_authenticate = authenticate ?? throw new ArgumentNullException (nameof (authenticate));

	/// <summary>Gets whether the current read-only local session can be reconnected with controls enabled.</summary>
	public bool CanEnableLocalControls => Mode == PowerwallMode.Local && !AllowsLocalControl && _connectionOptions is not null;

	/// <summary>Reauthenticates the same local device with permission for explicit control commands for this session only.</summary>
	/// <param name="cancellationToken">Cancels authentication, leaving the previous connection available.</param>
	/// <returns>Whether the controls are enabled. This method sends no settings or power-control commands.</returns>
	public Task<bool> EnableLocalControlsAsync (CancellationToken cancellationToken = default) => SetLocalControlsAsync (true, cancellationToken);

	/// <summary>Reauthenticates the same local device with control writes disabled, without changing device settings.</summary>
	/// <param name="cancellationToken">Cancels authentication; failure retains the previous connection and permission state.</param>
	/// <returns>Whether the connection is now read-only.</returns>
	public Task<bool> DisableLocalControlsAsync (CancellationToken cancellationToken = default) => SetLocalControlsAsync (false, cancellationToken);

	/// <summary>Changes local refresh cadence and cache lifetime together, preserving the session's control permission.</summary>
	/// <param name="seconds">Zero for manual refresh, or 1–3600 seconds between completed reads.</param>
	/// <param name="cancellationToken">Cancels reauthentication; failure retains the previous interval and connection.</param>
	/// <returns>Whether the new refresh preference took effect. No Powerwall settings are changed.</returns>
	public async Task<bool> SetLocalPollIntervalAsync (int seconds, CancellationToken cancellationToken = default)
		{
		if (seconds < 0 || seconds > 3600) throw new ArgumentOutOfRangeException (nameof (seconds), "Use zero for manual refresh or 1–3600 seconds.");
		if (Mode != PowerwallMode.Local || _connectionOptions is null) return false;
		if (LocalPollInterval.TotalSeconds == seconds && _connectionOptions.CacheExpireSeconds == seconds) return true;
		PowerwallOptions options = _connectionOptions with { CacheExpireSeconds = seconds };
		string? keyName = _localKeyName;
		bool wasPolling = _pollTask is not null;
		bool changed = false;
		await StopPollingAsync ().ConfigureAwait (false);
		try
			{
			changed = keyName is not null
				? await ConnectLocalAsync (options, keyName, cancellationToken).ConfigureAwait (false)
				: await ConnectAsync (options, cancellationToken).ConfigureAwait (false);
			if (changed) LocalPollInterval = TimeSpan.FromSeconds (seconds);
			return changed;
			}
		finally
			{
			if ((changed || wasPolling) && LocalPollInterval > TimeSpan.Zero) StartPolling ();
			}
		}

	private async Task<bool> SetLocalControlsAsync (bool enabled, CancellationToken cancellationToken)
		{
		if (Mode != PowerwallMode.Local || _connectionOptions is null) return false;
		if (AllowsLocalControl == enabled) return true;
		PowerwallOptions options = _connectionOptions with { AllowLocalControl = enabled };
		string? keyName = _localKeyName;
		bool wasPolling = _pollTask is not null;
		await StopPollingAsync ().ConfigureAwait (false);
		try
			{
			return keyName is not null
				? await ConnectLocalAsync (options, keyName, cancellationToken).ConfigureAwait (false)
				: await ConnectAsync (options, cancellationToken).ConfigureAwait (false);
			}
		finally
			{
			if (wasPolling) StartPolling ();
			}
		}

	/// <summary>Gets the active local protocol, or Gateway when using cloud access.</summary>
	public PowerwallLocalProtocol LocalProtocol { get; private set; }

	/// <summary>Gets whether the current connection explicitly permits local control.</summary>
	public bool AllowsLocalControl { get; private set; }
	private CancellationTokenSource? _pollCts;
	private Task? _pollTask;

	/// <summary>Raised when the active connection is replaced or disconnected; previous readings are no longer current.</summary>
	public event EventHandler? ConnectionChanged;

	/// <summary>Raised on each successful poll with the latest system snapshot.</summary>
	public event EventHandler<PowerFlowSnapshot>? SnapshotUpdated;

	/// <summary>Raised when a poll iteration fails, carrying a short human-readable message.</summary>
	public event EventHandler<string>? PollFailed;

	/// <summary>
	/// Raised when <see cref="SiteLabel"/> changes: after a successful connect resolves the active site (or
	/// local gateway host), and after a site switch from the Settings screen.
	/// </summary>
	public event EventHandler? SiteLabelChanged;

	/// <summary>Gets the connected Powerwall, or throws when no connection has been established.</summary>
	/// <exception cref="InvalidOperationException">Thrown when not connected.</exception>
	public Powerwall Powerwall =>
		_powerwall ?? throw new InvalidOperationException ("Not connected. Call ConnectAsync first.");

	/// <summary>Gets a value indicating whether an active connection exists.</summary>
	public bool IsConnected => _powerwall is not null;

	/// <summary>Gets the resolved connection mode, or <see cref="PowerwallMode.Unknown"/> when not connected.</summary>
	public PowerwallMode Mode => _powerwall?.Mode ?? PowerwallMode.Unknown;

	/// <summary>Gets the authenticated local hardware identifier, independent of its IP address or hostname.</summary>
	public string? LocalDeviceId { get; private set; }

	/// <summary>Gets the customer email of the active connection, or <see langword="null"/> when not connected.</summary>
	public string? Email => _powerwall?.Email;

	/// <summary>
	/// Gets a human-readable label for what the app is currently connected to: the Tesla energy site name
	/// (cloud mode) or the gateway host (local mode). <see langword="null"/> when not yet resolved.
	/// </summary>
	public string? SiteLabel { get; private set; }

	/// <summary>Gets the reported or previously associated site name, separately from its network address.</summary>
	public string? SiteName { get; private set; }

	/// <summary>Raised when the site name is resolved, changed or cleared.</summary>
	public event EventHandler? SiteNameChanged;

	/// <summary>Updates the display name without changing the connection or its history identity.</summary>
	/// <param name="name">Reported name, or null to clear a previous connection's name.</param>
	internal void SetSiteName (string? name)
		{
		name = string.IsNullOrWhiteSpace (name) ? null : name.Trim ();
		if (SiteName == name) return;
		SiteName = name;
		SiteNameChanged?.Invoke (this, EventArgs.Empty);
		}

	/// <summary>Sets <see cref="SiteLabel"/> and raises <see cref="SiteLabelChanged"/>.</summary>
	/// <param name="label">The label to display, or <see langword="null"/> to clear it.</param>
	public void SetSiteLabel (string? label)
		{
		SiteLabel = label;
		if (Mode != PowerwallMode.Local) SetSiteName (label);
		SiteLabelChanged?.Invoke (this, EventArgs.Empty);
		}

	/// <summary>
	/// Gets the poll cadence for the current connection: a fast interval for local gateway access and a
	/// slower interval for cloud-backed modes whose upstream data only changes every few minutes.
	/// </summary>
	public TimeSpan PollInterval =>
		Mode == PowerwallMode.Local ? _localPollInterval : _cloudPollInterval;

	/// <summary>
	/// Establishes a connection using the supplied options, replacing any existing connection. The new
	/// connection is validated before the previous one is discarded, so a failed attempt leaves the prior
	/// connection intact.
	/// </summary>
	/// <param name="options">Connection and behavior options.</param>
	/// <param name="cancellationToken">Token used to cancel the connect attempt.</param>
	/// <returns><see langword="true"/> when the connection succeeds; otherwise <see langword="false"/>.</returns>
	public async Task<bool> ConnectAsync (PowerwallOptions options, CancellationToken cancellationToken = default)
		{
		if (options is null)
			throw new ArgumentNullException (nameof (options));

		var candidate = new Powerwall (options);

		bool connected;
		string? deviceId = null;
		string? siteName = null;
		try
			{
			connected = await _authenticate (candidate, cancellationToken).ConfigureAwait (false);
			if (connected && candidate.Mode == PowerwallMode.Local)
				{
				deviceId = candidate.LocalDeviceIdentificationNumber ?? await candidate.DinAsync (cancellationToken).ConfigureAwait (false);
				siteName = LocalHistoryBinding.Find (AppSettingsStore.Load (), deviceId)?.SiteName;
				if (options.LocalProtocol != PowerwallLocalProtocol.Gateway)
					{
					try
						{
						var configuration = await candidate.GetLocalConfigurationAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
						if (!string.IsNullOrWhiteSpace (configuration.Site?.Name)) siteName = configuration.Site.Name;
						}
					catch (Exception exc) when (!cancellationToken.IsCancellationRequested
						&& exc is PowerwallException or HttpRequestException or JsonException or OperationCanceledException)
						{
						// An unavailable optional display name must not prevent a working LAN connection.
						}
					}
				}
			}
		catch
			{
			candidate.Dispose ();
			throw;
			}

		if (!connected)
			{
			candidate.Dispose ();
			return false;
			}

		if (_powerwall is not null)
			_powerwall.FleetApiTokensRefreshed -= OnFleetApiTokensRefreshed;

		await StopPollingAsync ().ConfigureAwait (false);
		_powerwall?.Dispose ();
		_localSigningKey?.Dispose ();
		_localSigningKey = null;
		_powerwall = candidate;
		_connectionOptions = options;
		_localKeyName = null;
		LocalDeviceId = deviceId;
		LocalProtocol = options.LocalProtocol;
		AllowsLocalControl = options.AllowLocalControl;
		_powerwall.FleetApiTokensRefreshed += OnFleetApiTokensRefreshed;

		// Keep the local address separate from the reported site name. Cloud/FleetAPI
		// mode's label (the Tesla site name) is set by the caller once GetSitesAsync resolves it, and again on
		// any later site switch from the Settings screen.
		SetSiteName (siteName);
		SetSiteLabel (candidate.Mode == PowerwallMode.Local ? options.Host : null);
		ConnectionChanged?.Invoke (this, EventArgs.Empty);
		return true;
		}

	/// <summary>Connects using a caller-selected Windows key, owned for the lifetime of the new local connection.</summary>
	/// <param name="options">Local connection options.</param>
	/// <param name="keyName">Existing Windows signing key name; used only for signed TEDAPI.</param>
	/// <param name="cancellationToken">Cancels the connection attempt.</param>
	/// <returns>Whether the candidate connection authenticated successfully.</returns>
	public async Task<bool> ConnectLocalAsync (PowerwallOptions options, string keyName, CancellationToken cancellationToken = default)
		{
		if (options.CloudMode || options.FleetApi)
			throw new ArgumentException ("Local connection options are required.", nameof (options));
		RSA? key = options.LocalProtocol == PowerwallLocalProtocol.TedapiSigned ? LocalSigningKeyStore.Open (keyName) : null;
		try
			{
			bool connected = await ConnectAsync (options with { LocalSigningKey = key }, cancellationToken).ConfigureAwait (false);
			if (connected)
				{
				_localSigningKey = key;
				_localKeyName = options.LocalProtocol == PowerwallLocalProtocol.TedapiSigned ? keyName : null;
				_connectionOptions = options with { LocalSigningKey = null };
				key = null;
				}
			return connected;
			}
		finally
			{
			key?.Dispose ();
			}
		}

	// The library now persists FleetAPI tokens internally (mirroring cloud mode), but the app also keeps its
	// own encrypted copy of the refresh token in AppSettings so the sign-in fields can be pre-populated
	// before a connection has ever been established, mirroring the test console's equivalent behavior.
	// Best-effort: never throws into the caller, which may be the background polling thread that triggered
	// the refresh.
	private void OnFleetApiTokensRefreshed (object? sender, FleetApiTokensRefreshedEventArgs e)
		{
		try
			{
			var settings = AppSettingsStore.Load ();
			if (!string.Equals (settings.Mode, "FleetApi", StringComparison.OrdinalIgnoreCase))
				return;

			if (!string.IsNullOrWhiteSpace (e.RefreshToken))
				settings.ProtectedFleetApiRefreshToken = CredentialProtector.Protect (e.RefreshToken);

			AppSettingsStore.Save (settings);
			}
		catch (Exception)
			{
			// Persisting rotated tokens is best-effort; a failure here must not crash a background poll.
			}
		}

	/// <summary>Starts the background polling loop if it is not already running.</summary>
	public void StartPolling ()
		{
		if (_powerwall is null || _pollTask is not null)
			return;

		_pollCts = new CancellationTokenSource ();
		_pollTask = PollLoopAsync (ReadSnapshotAsync, Task.Delay, _pollCts.Token);
		}

	/// <summary>Stops the background polling loop and waits for it to finish.</summary>
	/// <returns>A task that completes when polling has stopped.</returns>
	public async Task StopPollingAsync ()
		{
		if (_pollCts is null || _pollTask is null)
			return;

		_pollCts.Cancel ();
		try
			{
			await _pollTask.ConfigureAwait (false);
			}
		catch (OperationCanceledException)
			{
			// Expected when cancelling the loop.
			}
		finally
			{
			_pollCts.Dispose ();
			_pollCts = null;
			_pollTask = null;
			}
		}

	/// <summary>Reads a snapshot, reusing local data within the configured cache lifetime.</summary>
	/// <param name="cancellationToken">Token used to cancel the read.</param>
	/// <returns>The latest snapshot.</returns>
	public Task<PowerFlowSnapshot> ReadSnapshotAsync (CancellationToken cancellationToken = default) =>
		ReadSnapshotAsync (false, cancellationToken);

	private async Task<PowerFlowSnapshot> ReadSnapshotAsync (bool force, CancellationToken cancellationToken)
		{
		var powerwall = Powerwall;

		if (Mode == PowerwallMode.Local && LocalProtocol != PowerwallLocalProtocol.Gateway)
			{
			var telemetry = await powerwall.GetLocalTelemetryAsync (force, cancellationToken).ConfigureAwait (false);
			return PowerFlowSnapshot.FromLocal (telemetry);
			}
		var power = await powerwall.GetPowerReadingsAsync (cancellationToken).ConfigureAwait (false);
		var level = await powerwall.LevelAsync (scale: true, cancellationToken).ConfigureAwait (false);
		var gridStatus = await powerwall.GridStatusAsync (cancellationToken).ConfigureAwait (false);
		var timeRemaining = await SafeTimeRemainingAsync (powerwall, cancellationToken).ConfigureAwait (false);

		return new PowerFlowSnapshot (
			power.Solar,
			power.Battery,
			power.Load,
			power.Site,
			level,
			gridStatus,
			timeRemaining);
		}

	/// <summary>Requests a fresh snapshot and notifies the app; also works with automatic refresh disabled.</summary>
	/// <param name="cancellationToken">Cancels the explicit refresh.</param>
	/// <returns>A task that completes after publishing the snapshot.</returns>
	public async Task RefreshAsync (CancellationToken cancellationToken = default)
		{
		var connection = Powerwall;
		var snapshot = await ReadSnapshotAsync (true, cancellationToken).ConfigureAwait (false);
		if (ReferenceEquals (connection, _powerwall))
			SnapshotUpdated?.Invoke (this, snapshot);
		}

	/// <summary>Runs scheduled reads, recovering from per-request timeouts until the consumer cancels.</summary>
	/// <param name="read">One measurement read.</param>
	/// <param name="delay">Delay between completed attempts; injectable for offline scheduling tests.</param>
	/// <param name="cancellationToken">Stops this polling session.</param>
	/// <returns>A task completing on manual-only mode or session cancellation.</returns>
	internal async Task PollLoopAsync (Func<CancellationToken, Task<PowerFlowSnapshot>> read,
		Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
		{
		do
			{
			try
				{
				cancellationToken.ThrowIfCancellationRequested ();
				var snapshot = await read (cancellationToken).ConfigureAwait (false);
				SnapshotUpdated?.Invoke (this, snapshot);
				}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
				throw;
				}
			catch (Exception exc) when (exc is PowerwallException or HttpRequestException or JsonException or OperationCanceledException)
				{
				PollFailed?.Invoke (this, exc is OperationCanceledException ? "The local or cloud reading timed out; the next scheduled read will retry." : exc.Message);
				}
			if (PollInterval == TimeSpan.Zero)
				return;
			await delay (PollInterval, cancellationToken).ConfigureAwait (false);
			}
		while (!cancellationToken.IsCancellationRequested);
		}

	private static async Task<double?> SafeTimeRemainingAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		try
			{
			return await powerwall.GetTimeRemainingAsync (cancellationToken).ConfigureAwait (false);
			}
		catch (PowerwallException)
			{
			// Time-remaining is not available in every mode/firmware; treat as unknown rather than failing the poll.
			return null;
			}
		}

	/// <summary>
	/// Stops polling and tears down the active connection so the app returns to a disconnected state. Unlike
	/// <see cref="StopPollingAsync"/>, this clears the underlying <see cref="Powerwall"/>, allowing the user
	/// to sign in again with a different account.
	/// </summary>
	/// <returns>A task that completes once polling has stopped and the connection has been released.</returns>
	public async Task DisconnectAsync ()
		{
		await StopPollingAsync ().ConfigureAwait (false);
		if (_powerwall is not null)
			_powerwall.FleetApiTokensRefreshed -= OnFleetApiTokensRefreshed;

		_powerwall?.Dispose ();
		_powerwall = null;
		_connectionOptions = null;
		_localKeyName = null;
		LocalDeviceId = null;
		_localSigningKey?.Dispose ();
		_localSigningKey = null;
		AllowsLocalControl = false;
		SetSiteLabel (null);
		ConnectionChanged?.Invoke (this, EventArgs.Empty);
		}

	/// <summary>Stops polling and releases the underlying connection.</summary>
	public void Dispose ()
		{
		try
			{
			StopPollingAsync ().GetAwaiter ().GetResult ();
			}
		catch (Exception)
			{
			// Best-effort shutdown.
			}

		_powerwall?.Dispose ();
		_powerwall = null;
		_connectionOptions = null;
		_localKeyName = null;
		LocalDeviceId = null;
		_localSigningKey?.Dispose ();
		_localSigningKey = null;
		AllowsLocalControl = false;
		}
	}
