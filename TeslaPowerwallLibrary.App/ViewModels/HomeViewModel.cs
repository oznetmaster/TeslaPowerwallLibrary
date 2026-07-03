// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;

using TeslaPowerwallLibrary.App.Services;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the Home power-flow screen. Subscribes to the connection service's polling loop and exposes
/// live, formatted metrics for solar, battery, home, and grid flows plus charge level and grid status.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase
	{
	private readonly PowerwallConnectionService _connection;

	/// <summary>Initializes a new instance of the <see cref="HomeViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public HomeViewModel (PowerwallConnectionService connection)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		_connection.SnapshotUpdated += OnSnapshotUpdated;
		_connection.PollFailed += OnPollFailed;
		}

	/// <summary>Gets or sets the solar generation power in watts.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (SolarText))]
	private double _solarWatts;

	/// <summary>Gets or sets the battery power in watts (positive indicates discharge).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (BatteryText))]
	[NotifyPropertyChangedFor (nameof (BatteryFlowText))]
	private double _batteryWatts;

	/// <summary>Gets or sets the home (load) power in watts.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (HomeText))]
	private double _homeWatts;

	/// <summary>Gets or sets the grid power in watts (positive indicates import).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (GridText))]
	[NotifyPropertyChangedFor (nameof (GridFlowText))]
	private double _gridWatts;

	/// <summary>Gets or sets the battery charge level as an app-scaled percentage.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (BatteryPercentText))]
	private double? _batteryPercent;

	/// <summary>Gets or sets the normalized grid connection status.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (GridStatusText))]
	private GridStatus? _gridStatus;

	/// <summary>Gets or sets the estimated backup time remaining, in hours.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor (nameof (TimeRemainingText))]
	private double? _timeRemainingHours;

	/// <summary>Gets the formatted solar power.</summary>
	public string SolarText => FormatPower (SolarWatts);

	/// <summary>Gets the formatted battery power magnitude.</summary>
	public string BatteryText => FormatPower (Math.Abs (BatteryWatts));

	/// <summary>Gets the formatted home power.</summary>
	public string HomeText => FormatPower (HomeWatts);

	/// <summary>Gets the formatted grid power magnitude.</summary>
	public string GridText => FormatPower (Math.Abs (GridWatts));

	/// <summary>Gets a description of the current battery flow direction.</summary>
	public string BatteryFlowText => BatteryWatts switch
		{
		> 50 => "Discharging",
		< -50 => "Charging",
		_ => "Idle"
		};

	/// <summary>Gets a description of the current grid flow direction.</summary>
	public string GridFlowText => GridWatts switch
		{
		> 50 => "Importing",
		< -50 => "Exporting",
		_ => "No flow"
		};

	/// <summary>Gets the formatted battery charge percentage.</summary>
	public string BatteryPercentText => BatteryPercent is double p ? $"{p:0}%" : "--";

	/// <summary>Gets the formatted grid status.</summary>
	public string GridStatusText => GridStatus switch
		{
		TeslaPowerwallLibrary.GridStatus.Up => "Grid connected",
		TeslaPowerwallLibrary.GridStatus.Down => "Off-grid (islanded)",
		TeslaPowerwallLibrary.GridStatus.Syncing => "Reconnecting…",
		_ => "Grid status unknown"
		};

	/// <summary>Gets the formatted backup time remaining.</summary>
	public string TimeRemainingText => TimeRemainingHours is double h
		? $"{Math.Floor (h)}h {Math.Round ((h - Math.Floor (h)) * 60)}m backup"
		: "Backup time unavailable";

	private void OnSnapshotUpdated (object? sender, PowerFlowSnapshot snapshot) =>
		RunOnUi (() =>
			{
			SolarWatts = snapshot.SolarWatts;
			BatteryWatts = snapshot.BatteryWatts;
			HomeWatts = snapshot.HomeWatts;
			GridWatts = snapshot.GridWatts;
			BatteryPercent = snapshot.BatteryPercent;
			GridStatus = snapshot.GridStatus;
			TimeRemainingHours = snapshot.TimeRemainingHours;
			StatusMessage = null;
			});

	private void OnPollFailed (object? sender, string message) =>
		RunOnUi (() => StatusMessage = message);

	private static void RunOnUi (Action action)
		{
		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher is null || dispatcher.CheckAccess ())
			action ();
		else
			dispatcher.Invoke (action);
		}

	private static string FormatPower (double watts) =>
		$"{watts / 1000.0:0.00} kW";
	}
