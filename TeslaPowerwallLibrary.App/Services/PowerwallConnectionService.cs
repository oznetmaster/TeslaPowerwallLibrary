// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
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
	private static readonly TimeSpan _localPollInterval = TimeSpan.FromSeconds (5);

	/// <summary>
	/// Poll cadence for cloud-backed connections (Owners API / FleetAPI). Tesla™ only refreshes cloud data
	/// every few minutes, but the client cannot know where it sits in that cycle, so a one-minute poll
	/// bounds worst-case staleness while staying light on the API.
	/// </summary>
	private static readonly TimeSpan _cloudPollInterval = TimeSpan.FromMinutes (1);

	private Powerwall? _powerwall;
	private CancellationTokenSource? _pollCts;
	private Task? _pollTask;

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

	/// <summary>
	/// Gets a human-readable label for what the app is currently connected to: the Tesla energy site name
	/// (cloud mode) or the gateway host (local mode). <see langword="null"/> when not yet resolved.
	/// </summary>
	public string? SiteLabel { get; private set; }

	/// <summary>Sets <see cref="SiteLabel"/> and raises <see cref="SiteLabelChanged"/>.</summary>
	/// <param name="label">The label to display, or <see langword="null"/> to clear it.</param>
	public void SetSiteLabel (string? label)
		{
		SiteLabel = label;
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
		try
			{
			connected = await candidate.ConnectAsync (cancellationToken).ConfigureAwait (false);
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

		await StopPollingAsync ().ConfigureAwait (false);
		_powerwall?.Dispose ();
		_powerwall = candidate;

		// Local mode has no site name to resolve, so the gateway host doubles as the site label. Cloud mode's
		// label (the Tesla site name) is set by the caller once GetSitesAsync resolves it, and again on any
		// later site switch from the Settings screen.
		SetSiteLabel (candidate.Mode == PowerwallMode.Local ? options.Host : null);
		return true;
		}

	/// <summary>Starts the background polling loop if it is not already running.</summary>
	public void StartPolling ()
		{
		if (_powerwall is null || _pollTask is not null)
			return;

		_pollCts = new CancellationTokenSource ();
		_pollTask = PollLoopAsync (_pollCts.Token);
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

	/// <summary>Reads a single fresh snapshot on demand, independent of the polling loop.</summary>
	/// <param name="cancellationToken">Token used to cancel the read.</param>
	/// <returns>The latest snapshot.</returns>
	public async Task<PowerFlowSnapshot> ReadSnapshotAsync (CancellationToken cancellationToken = default)
		{
		var powerwall = Powerwall;

		var power = await powerwall.PowerAsync (cancellationToken).ConfigureAwait (false);
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

	private async Task PollLoopAsync (CancellationToken cancellationToken)
		{
		using var timer = new PeriodicTimer (PollInterval);
		do
			{
			try
				{
				var snapshot = await ReadSnapshotAsync (cancellationToken).ConfigureAwait (false);
				SnapshotUpdated?.Invoke (this, snapshot);
				}
			catch (OperationCanceledException)
				{
				throw;
				}
			catch (PowerwallException exc)
				{
				PollFailed?.Invoke (this, exc.Message);
				}
			}
		while (await timer.WaitForNextTickAsync (cancellationToken).ConfigureAwait (false));
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

	// Persists rotated Tesla tokens after the library refreshes them, so a later launch reuses the current
	// refresh token instead of a stale one. Only writes when cloud tokens were previously remembered, keeping
	// behavior correct when the user declined to save credentials. Best-effort: never throws into the caller,
	// which may be the background polling thread that triggered the refresh.

	/// <summary>
	/// Stops polling and tears down the active connection so the app returns to a disconnected state. Unlike
	/// <see cref="StopPollingAsync"/>, this clears the underlying <see cref="Powerwall"/>, allowing the user
	/// to sign in again with a different account.
	/// </summary>
	/// <returns>A task that completes once polling has stopped and the connection has been released.</returns>
	public async Task DisconnectAsync ()
		{
		await StopPollingAsync ().ConfigureAwait (false);
		_powerwall?.Dispose ();
		_powerwall = null;
		SetSiteLabel (null);
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
		}
	}
