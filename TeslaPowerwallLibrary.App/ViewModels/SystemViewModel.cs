// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the System information screen: firmware version, DIN, uptime, per-battery blocks, and active alerts.
/// </summary>
public sealed partial class SystemViewModel : ViewModelBase
	{
	private readonly PowerwallConnectionService _connection;

	/// <summary>Initializes a new instance of the <see cref="SystemViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public SystemViewModel (PowerwallConnectionService connection)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		Batteries = new ObservableCollection<BatteryBlockView> ();
		Alerts = new ObservableCollection<string> ();
		}

	/// <summary>Gets the per-battery block summaries.</summary>
	public ObservableCollection<BatteryBlockView> Batteries { get; }

	/// <summary>Gets the active alert names.</summary>
	public ObservableCollection<string> Alerts { get; }

	/// <summary>Gets or sets the gateway firmware version.</summary>
	[ObservableProperty]
	private string? _version;

	/// <summary>Gets or sets the gateway device identification number.</summary>
	[ObservableProperty]
	private string? _din;

	/// <summary>Gets or sets the gateway uptime.</summary>
	[ObservableProperty]
	private string? _uptime;

	/// <summary>Gets or sets the connection mode label.</summary>
	[ObservableProperty]
	private string? _modeLabel;

	/// <summary>Gets or sets a value indicating whether any alerts are active.</summary>
	[ObservableProperty]
	private bool _hasAlerts;

	/// <summary>Loads system information, battery blocks, and alerts.</summary>
	/// <returns>A task that completes when the system data has been read.</returns>
	[RelayCommand]
	private async Task LoadAsync ()
		{
		StatusMessage = null;
		IsBusy = true;
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			var powerwall = _connection.Powerwall;

			ModeLabel = _connection.Mode.ToString ();

			var status = await powerwall.StatusAsync (cts.Token).ConfigureAwait (true);
			Version = status?.Version ?? "unknown";
			Din = status?.Din ?? "unknown";
			Uptime = status?.UpTimeSeconds ?? "unknown";

			await LoadBatteriesAsync (powerwall, cts.Token).ConfigureAwait (true);
			await LoadAlertsAsync (powerwall, cts.Token).ConfigureAwait (true);
			}
		catch (OperationCanceledException)
			{
			StatusMessage = "Loading system information timed out.";
			}
		catch (PowerwallException exc)
			{
			StatusMessage = $"Could not load system information: {exc.Message}";
			}
		finally
			{
			IsBusy = false;
			}
		}

	private async Task LoadBatteriesAsync (Powerwall powerwall, CancellationToken token)
		{
		Batteries.Clear ();
		var blocks = await powerwall.BatteryBlocksAsync (token).ConfigureAwait (true);
		if (blocks is null)
			return;

		foreach (var pair in blocks)
			Batteries.Add (BatteryBlockView.From (pair.Key, pair.Value));
		}

	private async Task LoadAlertsAsync (Powerwall powerwall, CancellationToken token)
		{
		Alerts.Clear ();
		var alerts = await powerwall.AlertsAsync (token).ConfigureAwait (true);
		foreach (var alert in alerts)
			Alerts.Add (alert);

		HasAlerts = Alerts.Count > 0;
		}
	}

/// <summary>
/// A read-only, display-friendly projection of a <see cref="BatteryBlock"/> for the System screen.
/// </summary>
public sealed class BatteryBlockView
	{
	private BatteryBlockView (string serial, string energyText, string powerText, string state)
		{
		Serial = serial;
		EnergyText = energyText;
		PowerText = powerText;
		State = state;
		}

	/// <summary>Gets the battery package serial number.</summary>
	public string Serial { get; }

	/// <summary>Gets the formatted remaining / full pack energy.</summary>
	public string EnergyText { get; }

	/// <summary>Gets the formatted instantaneous output power.</summary>
	public string PowerText { get; }

	/// <summary>Gets the inverter grid state.</summary>
	public string State { get; }

	/// <summary>Creates a display projection from a battery serial and its <see cref="BatteryBlock"/>.</summary>
	/// <param name="serial">The battery package serial number.</param>
	/// <param name="block">The battery block data.</param>
	/// <returns>A new <see cref="BatteryBlockView"/>.</returns>
	public static BatteryBlockView From (string serial, BatteryBlock block)
		{
		var remaining = (block.NominalEnergyRemaining ?? 0) / 1000.0;
		var full = (block.NominalFullPackEnergy ?? 0) / 1000.0;
		var power = (block.PowerOut ?? 0) / 1000.0;

		return new BatteryBlockView (
			serial,
			$"{remaining:0.0} / {full:0.0} kWh",
			$"{power:0.00} kW",
			block.PinvGridState ?? "unknown");
		}
	}
