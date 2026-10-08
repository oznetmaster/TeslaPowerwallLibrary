// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

namespace TeslaPowerwallLibrary.App.ViewModels;

public sealed partial class EnergyViewModel
	{
	private ObservablePoint[] _cloudDaySource = Array.Empty<ObservablePoint> ();
	private ObservableCollection<ObservablePoint>? _cloudDayValues;
	private readonly List<(double Start, double End)> _lanSpans = new ();
	private double LanGapSeconds => Math.Max (30, _connection.LocalPollInterval.TotalSeconds * 3);

	// Rebuilding is reserved for changed periods/components or delayed/corrected samples.
	// Ordinary arrivals extend only the final coverage interval and existing chart collections.
	private void RebuildLiveValues (DateTimeOffset start, DateTimeOffset end)
		{
		var desired = new List<ObservablePoint> ();
		_lanSpans.Clear ();
		DateTimeOffset? previous = null;
		foreach (var sample in _localSamples.Where (p => p.ReceivedAt >= start && p.ReceivedAt < end.AddSeconds (1))
			.GroupBy (p => p.ReceivedAt).Select (group => group.Last ()).OrderBy (p => p.ReceivedAt))
			{
			double x = (sample.ReceivedAt - start).TotalMinutes;
			double? y = DisplayKilowatts (LocalWatts (sample, _liveComponent!));
			if (previous.HasValue && (sample.ReceivedAt - previous.Value).TotalSeconds > LanGapSeconds)
				desired.Add (new ObservablePoint (x, null));
			AppendLanCoverage (x, y, desired.LastOrDefault ());
			desired.Add (new ObservablePoint (x, y));
			previous = sample.ReceivedAt;
			}
		SynchronizePoints (_liveValues!, desired);
		_lastPlotted = previous;
		}

	private void AppendLanCoverage (double x, double? y, ObservablePoint? previous)
		{
		if (!y.HasValue) return; // Unknown is not zero and must not hide available cloud history.
		if (previous?.Y is not null && previous.X is double previousX && (x - previousX) * 60 <= LanGapSeconds && _lanSpans.Count > 0)
			_lanSpans[_lanSpans.Count - 1] = (_lanSpans[_lanSpans.Count - 1].Start, x);
		else
			_lanSpans.Add ((x, x));
		}

	private bool RefreshCloudMask ()
		{
		if (_cloudDayValues is null || !IsLocal || SelectedPeriod != "day") return false;
		var desired = new List<ObservablePoint> ();
		int spanIndex = 0;
		double? previousX = null;
		foreach (var point in _cloudDaySource)
			{
			double x = point.X!.Value;
			// A null separator prevents cloud interpolation across a LAN run even when
			// that run lies entirely between two five-minute cloud samples.
			while (spanIndex < _lanSpans.Count && _lanSpans[spanIndex].Start < x)
				{
				var span = _lanSpans[spanIndex];
				if (previousX.HasValue && span.Start > previousX.Value)
					desired.Add (new ObservablePoint (span.Start, null));
				if (span.End >= x) break;
				spanIndex++;
				}
			bool covered = spanIndex < _lanSpans.Count && _lanSpans[spanIndex].Start <= x && x <= _lanSpans[spanIndex].End;
			desired.Add (new ObservablePoint (x, covered ? null : point.Y));
			previousX = x;
			}
		return SynchronizePoints (_cloudDayValues, desired);
		}

	private static bool SynchronizePoints (ObservableCollection<ObservablePoint> current, IReadOnlyList<ObservablePoint> desired)
		{
		bool changed = false;
		for (int index = 0; index < desired.Count; index++)
			{
			var point = desired[index];
			while (index < current.Count && current[index].X < point.X)
				{ current.RemoveAt (index); changed = true; }
			if (index >= current.Count || current[index].X != point.X)
				{ current.Insert (index, new ObservablePoint (point.X, point.Y)); changed = true; }
			else if (current[index].Y != point.Y)
				{ current[index].Y = point.Y; changed = true; }
			}
		while (current.Count > desired.Count) { current.RemoveAt (current.Count - 1); changed = true; }
		return changed;
		}

	private void UpdateDayPowerScale ()
		{
		if (SelectedPeriod != "day") return;
		YAxes[0].MinLimit = 0;
		YAxes[0].MaxLimit = 1;
		foreach (var series in Series.OfType<LineSeries<ObservablePoint>> ())
			foreach (var point in series.Values ?? Array.Empty<ObservablePoint> ()) ExpandPowerScale (point.Y);
		}
	}
