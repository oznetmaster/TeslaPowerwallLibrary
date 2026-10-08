// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Data.Sqlite;
using TeslaPowerwallLibrary.App.Services;

namespace TeslaPowerwallLibrary.App.ViewModels;

public sealed partial class EnergyViewModel
	{
	private readonly List<LocalPowerSample> _localSamples = new ();
	private LocalPowerHistoryStore _localStore = null!;
	private LocalPowerSample? _latestLocal;
	private bool _isActive;
	private ObservableCollection<ObservablePoint>? _liveValues;
	private string? _liveComponent;
	private DateTimeOffset _liveStart;
	private DateTimeOffset? _lastPlotted;

	/// <summary>Gets or sets whether the graph is visible; hidden pages still save received readings.</summary>
	internal bool IsActive
		{
		get => _isActive;
		set { _isActive = value; if (value) { AdvanceCurrentPeriod (_now ()); BuildSeries (); } }
		}

	/// <summary>Gets the selected component's latest actual LAN measurement and receipt time.</summary>
	[ObservableProperty]
	private string _liveReadingText = "Waiting for a LAN reading.";

	/// <summary>Gets any local storage error, independently of cloud authentication messages.</summary>
	[ObservableProperty]
	private string? _liveStorageWarning;

	private async void OnLocalSnapshot (object? sender, PowerFlowSnapshot snapshot)
		{
		if (!IsLocal)
			return;
		await RecordLocalSnapshotAsync (snapshot, DateTimeOffset.UtcNow).ConfigureAwait (false);
		}

	/// <summary>Records an actual received snapshot without requesting another reading or requiring cloud access.</summary>
	/// <param name="snapshot">Actual watts, preserving missing measurements.</param>
	/// <param name="receivedAt">Application receipt time.</param>
	/// <returns>A task completing when the sample is saved, or a storage warning has been displayed.</returns>
	internal async Task RecordLocalSnapshotAsync (PowerFlowSnapshot snapshot, DateTimeOffset receivedAt)
		{
		string? host = _connection.LocalDeviceId ?? _connection.SiteLabel;
		int generation = _generation;
		var sample = new LocalPowerSample (receivedAt, snapshot.SolarWatts, snapshot.BatteryWatts, snapshot.HomeWatts, snapshot.GridWatts);
		RunOnUi (() =>
			{
			if (!IsLocal || generation != _generation)
				return;
			AdvanceCurrentPeriod (_now ());
			_latestLocal = sample;
			var (start, end) = GetPeriodRange ("day", _anchor);
			if (sample.ReceivedAt >= start && sample.ReceivedAt < end.AddSeconds (1))
				_localSamples.Add (sample);
			// Keep the active in-memory graph bounded to its selected day.
			_localSamples.RemoveAll (point => point.ReceivedAt < start || point.ReceivedAt >= end.AddSeconds (1));
			UpdateLiveReading ();
			if (IsActive)
				AppendLivePoint (sample);
			});
		if (string.IsNullOrWhiteSpace (host))
			return;
		try
			{
			await _localStore.AppendAsync (host, sample).ConfigureAwait (false);
			}
		catch (Exception exc) when (exc is SqliteException or IOException or UnauthorizedAccessException)
			{
			RunOnUi (() => LiveStorageWarning = "LAN readings are live, but could not be saved locally.");
			}
		}

	private void OnLocalPollFailed (object? sender, string message) => RunOnUi (() =>
		{
		if (IsLocal)
			LiveReadingText = "LAN refresh failed; displayed samples are from earlier successful reads.";
		});

	private async Task LoadLocalHistoryAsync ()
		{
		if (!IsLocal || string.IsNullOrWhiteSpace (_connection.SiteLabel))
			return;
		int generation = _generation;
		var (start, end) = GetPeriodRange ("day", _anchor);
		try
			{
			var saved = await _localStore.ReadAsync (_connection.LocalDeviceId ?? _connection.SiteLabel, start, end.AddSeconds (1)).ConfigureAwait (true);
			if (_connection.LocalDeviceId is not null)
				{
				var earlier = await _localStore.ReadAsync (_connection.SiteLabel, start, end.AddSeconds (1)).ConfigureAwait (true);
				saved = saved.Concat (earlier).ToArray ();
				}
			if (generation != _generation)
				return;
			var merged = saved.Concat (_localSamples).Where (p => p.ReceivedAt >= start && p.ReceivedAt < end.AddSeconds (1))
				.GroupBy (p => p.ReceivedAt.ToUnixTimeMilliseconds ()).Select (g => g.Last ()).OrderBy (p => p.ReceivedAt).ToArray ();
			_localSamples.Clear ();
			_localSamples.AddRange (merged);
			BuildSeries ();
			}
		catch (Exception exc) when (exc is SqliteException or IOException or UnauthorizedAccessException)
			{
			LiveStorageWarning = "Saved LAN history could not be read. New live readings will still be displayed.";
			}
		}

	private void UpdateLiveReading ()
		{
		var component = Components.FirstOrDefault (c => c.IsSelected)?.Name ?? "Solar";
		double? watts = _latestLocal is null ? null : LocalWatts (_latestLocal, component);
		LiveReadingText = _latestLocal is null ? "Waiting for a LAN reading."
			: $"{component}: {(watts.HasValue ? DisplayKilowatts (watts)!.Value.ToString ("0.00", CultureInfo.CurrentCulture) + " kW" : "Unavailable")} from LAN, received {_latestLocal.ReceivedAt.ToLocalTime ():HH:mm:ss}.";
		}

	private static double? LocalWatts (LocalPowerSample point, string component) => component switch
		{
		"Home" => point.HomeWatts,
		"Powerwall" => point.BatteryWatts,
		"Grid" => point.GridWatts,
		_ => point.SolarWatts
		};

	private void BuildSeries ()
		{
		_liveValues = null;
		_liveComponent = null;
		_lastPlotted = null;
		_cloudDayValues = null;
		_cloudDaySource = Array.Empty<ObservablePoint> ();
		_lanSpans.Clear ();
		BuildCloudSeries ();
		bool batteryDay = SelectedPeriod == "day" && Components.Any (component => component.IsSelected && component.Name == "Powerwall");
		YAxes[0].Labeler = value => batteryDay
			? value.ToString ("+0.0;-0.0;0.0", CultureInfo.CurrentCulture)
			: Math.Abs (value).ToString ("0.0", CultureInfo.CurrentCulture);
		if (batteryDay) YAxes[0].Name = "kW (+ discharge / - charge)";
		YAxes[0].MinLimit = SelectedPeriod == "day" ? 0 : null;
		YAxes[0].MaxLimit = SelectedPeriod == "day" ? 1 : null;
		if (SelectedPeriod != "day" && Series.OfType<ColumnSeries<double?>> ().Any ())
			{
			var energyValues = Series.OfType<ColumnSeries<double?>> ().SelectMany (series => series.Values ?? Array.Empty<double?> ()).ToArray ();
			// Bar height must be measured from zero, including an all-export (negative) period.
			YAxes[0].MinLimit = energyValues.Any (value => value < 0) ? null : 0;
			YAxes[0].MaxLimit = energyValues.Any (value => value < 0) && !energyValues.Any (value => value > 0) ? 0 : null;
			}
		UpdateLiveReading ();
		if (!IsLocal || SelectedPeriod != "day" || _localSamples.Count == 0)
			{ UpdateDayPowerScale (); return; }
		var selected = Components.FirstOrDefault (c => c.IsSelected);
		if (selected is null)
			return;
		var (start, end) = GetPeriodRange ("day", _anchor);
		XAxes[0].Labels = null;
		XAxes[0].CustomSeparators = null;
		XAxes[0].MinLimit = 0;
		XAxes[0].MaxLimit = (end.AddSeconds (1) - start).TotalMinutes;
		XAxes[0].MinStep = 120;
		XAxes[0].ForceStepToMin = false;
		XAxes[0].Labeler = minutes => TimeZoneInfo.ConvertTime (start.AddMinutes (minutes), TimeZoneInfo.Local).ToString ("HH:mm", CultureInfo.CurrentCulture);
		YAxes[0].Name = batteryDay ? "kW (+ discharge / - charge)" : "kW";
		var values = new ObservableCollection<ObservablePoint> ();
		_liveValues = values;
		_liveComponent = selected.Name;
		_liveStart = start;
		RebuildLiveValues (start, end);
		var live = new LineSeries<ObservablePoint>
			{
			Name = selected.Name + " (LAN)", Values = values, Fill = null,
			Stroke = new SolidColorPaint (selected.Color, 2), GeometryFill = new SolidColorPaint (selected.Color),
			GeometryStroke = null, GeometrySize = 0, LineSmoothness = 0,
			XToolTipLabelFormatter = point => XAxes[0].Labeler (point.Coordinate.SecondaryValue),
			YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:0.00} kW (LAN)"
			};
		Series = Series.Concat (new ISeries[] { live }).ToArray ();
		RefreshCloudMask ();
		UpdateDayPowerScale ();
		}
	private void AppendLivePoint (LocalPowerSample sample)
		{
		if (SelectedPeriod != "day")
			return;
		var (start, end) = GetPeriodRange ("day", _anchor);
		if (sample.ReceivedAt < start || sample.ReceivedAt >= end.AddSeconds (1))
			return;
		if (_liveValues is null || _liveStart != start)
			{
			BuildSeries ();
			return;
			}
		// Keep the same series, axes and observable collection for normal arrivals.
		// Delayed or duplicate callbacks are inserted/replaced at their actual timestamp.
		double x = (sample.ReceivedAt - start).TotalMinutes;
		double? y = DisplayKilowatts (LocalWatts (sample, _liveComponent!));
		ExpandPowerScale (y);
		if (_lastPlotted is not null && sample.ReceivedAt <= _lastPlotted)
			{
			RebuildLiveValues (start, end);
			RefreshCloudMask ();
			UpdateDayPowerScale ();
			return;
			}
		if (_lastPlotted is not null && sample.ReceivedAt - _lastPlotted > TimeSpan.FromSeconds (LanGapSeconds))
			_liveValues.Add (new ObservablePoint (x, null));
		AppendLanCoverage (x, y, _liveValues.LastOrDefault ());
		_liveValues.Add (new ObservablePoint (x, y));
		_lastPlotted = sample.ReceivedAt;
		if (RefreshCloudMask ()) UpdateDayPowerScale ();
		}

	private static double? DisplayKilowatts (double? watts) => watts is double value && double.IsFinite (value)
		? Math.Round (value / 1000, 2, MidpointRounding.ToEven) : null;

	private void ExpandPowerScale (double? kilowatts)
		{
		if (kilowatts is not double value || !double.IsFinite (value)) return;
		// Keep small overnight readings in proportion; expand only for a new extreme.
		if (value < YAxes[0].MinLimit) YAxes[0].MinLimit = Math.Floor (value * 2) / 2;
		if (value > YAxes[0].MaxLimit) YAxes[0].MaxLimit = Math.Ceiling (value * 2) / 2;
		}

	}
