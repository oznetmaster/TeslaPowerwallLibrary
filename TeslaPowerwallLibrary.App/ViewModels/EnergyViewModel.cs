// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the Energy history screen. Loads calendar-aligned energy history for a selectable period and renders
/// exactly one of solar, home, Powerwall, or grid as a single line chart at a time, mirroring the Tesla app's
/// own single-series presentation instead of combining every component onto one chart. Energy history is a
/// cloud-mode feature.
/// </summary>
public sealed partial class EnergyViewModel : ViewModelBase
	{
	private const string LifetimePeriod = "lifetime";

	// Tesla's cloud calendar-history "energy" kind reports one sample every 5 minutes for the day period
	// (confirmed against raw payloads), i.e. 12 samples per hour; this drives both the X axis label thinning
	// in BuildSeries and the average-power fallback duration in BuildDayPoints. Local mode does not yet
	// support Energy history (see IsAvailable) so this constant only matters for cloud mode today.
	private const int DaySamplesPerHour = 12;

	private readonly PowerwallConnectionService _connection;
	private IReadOnlyList<EnergyBucket> _buckets = Array.Empty<EnergyBucket> ();
	private DateTimeOffset _anchor = DateTimeOffset.Now;

	/// <summary>Initializes a new instance of the <see cref="EnergyViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public EnergyViewModel (PowerwallConnectionService connection)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		_connection.SiteLabelChanged += OnSiteLabelChanged;
		_siteLabel = _connection.SiteLabel;
		Periods = new ObservableCollection<string> (Powerwall.HistoryPeriods);
		_selectedPeriod = Powerwall.DEFAULT_HISTORY_PERIOD;

		// Exactly one component is selected at a time (see Components below and EnergyView.xaml's RadioButton
		// picker), mirroring the Tesla app's single Solar / Home / Powerwall / Grid selector instead of
		// combining every series onto one chart.
		Components = new ObservableCollection<EnergySeriesOption>
			{
			new ("Solar", new SKColor (0xF5, 0xB3, 0x01), p => p.SolarKwh) { IsSelected = true },
			new ("Home", new SKColor (0x3E, 0x6A, 0xE1), p => p.HomeKwh),
			// Discharge (giving power) plots above the zero line and charge (taking power) plots below it,
			// mirroring the Tesla app's single "Powerwall" graph (an "up = out, down = in" shape) instead of
			// two separate charge/discharge series. The Y axis labeler and tooltip formatter below both take
			// the absolute value so negative kWh is never shown to the user - only the line's position
			// conveys charge vs. discharge.
			new ("Powerwall", new SKColor (0x34, 0xC7, 0x59), p => p.BatteryDischargeKwh - p.BatteryChargeKwh),
			// From-grid (importing) plots above the zero line and to-grid (exporting) plots below it, for the
			// same reason as Powerwall above, mirroring the Tesla app's single "Grid" graph.
			new ("Grid", new SKColor (0x8E, 0x8E, 0x93), p => p.FromGridKwh - p.ToGridKwh)
			};

		foreach (var component in Components)
			component.PropertyChanged += OnComponentSelectionChanged;

		// ForceStepToMin pins the X axis step to exactly one index (one bucket/sample) instead of LiveCharts
		// auto-calculating a "nice" step for the available chart width; without it, labels are skipped at
		// inconsistent index intervals and the last bucket can be left unlabeled, making the period look like
		// it does not span the full range. The day period can have close to 288 raw 5-minute samples (see
		// BuildDayPoints); BuildSeries sets CustomSeparators so only every DaySamplesPerHour-th one gets a
		// visible axis label/gridline (a clean hourly axis), while every other period keeps one label per bucket.
		XAxes = new[] { new Axis { LabelsRotation = 45, TextSize = 11, NamePaint = null, MinStep = 1, ForceStepToMin = true } };

		// The Y axis is shared by every period, whose value ranges vary hugely (a day's samples are a few kW;
		// a year's monthly totals can be hundreds of kWh). MinStep alone only acts as a floor - it raises an
		// auto-calculated step of e.g. 0.05 up to 0.1 - without forcing an exact 0.1 step for large ranges,
		// which would otherwise create an impractical number of gridlines (ForceStepToMin is intentionally not
		// used here). Name is initialized to the common case and overwritten per period in BuildSeries (the day
		// period plots average power in kW; every other period plots total energy in kWh).
		YAxes = new[]
			{
			new Axis
				{
				Name = "kWh",
				TextSize = 11,
				MinStep = 0.1,
				// Math.Abs mirrors the Tesla app's graphs, whose axis shows the same unsigned scale on both
				// sides of zero (e.g. "2" appears both above, for discharge/import, and below, for
				// charge/export) rather than negative numbers. Powerwall and Grid are the only series ever
				// plotted as negative (see Components above); Solar and Home are already non-negative, so
				// this is a no-op there.
				Labeler = value => Math.Abs (value).ToString ("0.0", CultureInfo.InvariantCulture)
				}
			};
		}

	/// <summary>Gets the selectable aggregation periods.</summary>
	public ObservableCollection<string> Periods { get; }

	/// <summary>Gets or sets the label for the currently connected Tesla site or gateway host.</summary>
	[ObservableProperty]
	private string? _siteLabel;

	/// <summary>
	/// Gets the selectable chart components (Solar, Home, Powerwall, Grid). Exactly one is graphed at a
	/// time - selecting one clears the others, mirroring the Tesla app - so switching never reloads history.
	/// </summary>
	public ObservableCollection<EnergySeriesOption> Components { get; }

	/// <summary>Gets or sets the selected aggregation period.</summary>
	[ObservableProperty]
	private string _selectedPeriod;

	/// <summary>Gets the human-readable label describing the currently displayed period.</summary>
	[ObservableProperty]
	private string _periodLabel = string.Empty;

	/// <summary>Gets or sets the chart series collection bound to the cartesian chart.</summary>
	[ObservableProperty]
	private ISeries[] _series = Array.Empty<ISeries> ();

	/// <summary>Gets the chart X axes.</summary>
	public Axis[] XAxes { get; }

	/// <summary>Gets the chart Y axes.</summary>
	public Axis[] YAxes { get; }

	/// <summary>Gets a value indicating whether energy history is available (cloud or FleetAPI mode only).</summary>
	public bool IsAvailable => _connection.Mode is PowerwallMode.Cloud or PowerwallMode.FleetApi;

	partial void OnSelectedPeriodChanged (string value)
		{
		// Switching the aggregation period resets navigation back to the current, most-recent bucket.
		_anchor = DateTimeOffset.Now;
		PreviousPeriodCommand.NotifyCanExecuteChanged ();
		NextPeriodCommand.NotifyCanExecuteChanged ();
		_ = LoadAsync ();
		}

	/// <summary>Loads energy history for the selected period and rebuilds the chart series.</summary>
	/// <returns>A task that completes when the history has been loaded.</returns>
	[RelayCommand]
	private async Task LoadAsync ()
		{
		StatusMessage = null;

		if (!IsAvailable)
			{
			Series = Array.Empty<ISeries> ();
			StatusMessage = "Energy history is available in cloud mode only.";
			return;
			}

		var (startDate, endDate) = ResolveRange ();

		IsBusy = true;
		// Diagnostic timing: separates network latency (bounded by the 30 second cts below) from
		// everything else LoadAsync does, so a slow first Energy navigation can be attributed correctly
		// instead of assumed to be a network issue. See also EnergyView's constructor and ChartWarmup.
		var totalStopwatch = Stopwatch.StartNew ();
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			IReadOnlyList<EnergyHistoryPoint> points;
			var networkStopwatch = Stopwatch.StartNew ();
			try
				{
				points = await _connection.Powerwall
					.GetEnergyCalendarHistoryAsync (ToHistoryPeriod (SelectedPeriod), startDate: startDate, endDate: endDate, cancellationToken: cts.Token)
					.ConfigureAwait (true);
				}
			catch (OperationCanceledException)
				{
				StatusMessage = "Loading energy history timed out.";
				return;
				}
			catch (PowerwallException exc)
				{
				StatusMessage = $"Could not load energy history: {exc.Message}";
				return;
				}
			finally
				{
				Debug.WriteLine ($"[Perf] EnergyViewModel.LoadAsync: GetEnergyCalendarHistoryAsync took {networkStopwatch.ElapsedMilliseconds} ms.");
				}

			if (points.Count == 0)
				{
				_buckets = Array.Empty<EnergyBucket> ();
				Series = Array.Empty<ISeries> ();
				StatusMessage = "No energy history was returned for this period.";
				return;
				}

			_buckets = BuildBuckets (SelectedPeriod, _anchor, points);
			BuildSeries ();
			}
		finally
			{
			IsBusy = false;
			PreviousPeriodCommand.NotifyCanExecuteChanged ();
			NextPeriodCommand.NotifyCanExecuteChanged ();
			Debug.WriteLine ($"[Perf] EnergyViewModel.LoadAsync: total {totalStopwatch.ElapsedMilliseconds} ms.");
			}
		}

	/// <summary>Steps the graph back to the previous period and reloads.</summary>
	/// <returns>A task that completes when the previous period has loaded.</returns>
	[RelayCommand (CanExecute = nameof (CanGoPrevious))]
	private Task PreviousPeriodAsync ()
		{
		_anchor = StepAnchor (-1);
		NextPeriodCommand.NotifyCanExecuteChanged ();
		return LoadAsync ();
		}

	/// <summary>Steps the graph forward to the next period and reloads.</summary>
	/// <returns>A task that completes when the next period has loaded.</returns>
	[RelayCommand (CanExecute = nameof (CanGoNext))]
	private Task NextPeriodAsync ()
		{
		_anchor = StepAnchor (+1);
		NextPeriodCommand.NotifyCanExecuteChanged ();
		return LoadAsync ();
		}

	private bool CanGoPrevious () => IsAvailable && SelectedPeriod != LifetimePeriod;

	private bool CanGoNext () => IsAvailable && SelectedPeriod != LifetimePeriod && IsBeforeCurrentPeriod ();

	// The current, most-recent bucket is the newest data available; stepping past it would request the future.
	private bool IsBeforeCurrentPeriod () =>
		GetPeriodRange (SelectedPeriod, _anchor).Start < GetPeriodRange (SelectedPeriod, DateTimeOffset.Now).Start;

	private DateTimeOffset StepAnchor (int direction) =>
		SelectedPeriod switch
			{
			"week" => _anchor.AddDays (7 * direction),
			"month" => _anchor.AddMonths (direction),
			"year" => _anchor.AddYears (direction),
			_ => _anchor.AddDays (direction)
			};

	// Resolves the RFC 3339 window for the current anchor and updates the display label as a side effect.
	private (string? StartDate, string? EndDate) ResolveRange ()
		{
		if (SelectedPeriod == LifetimePeriod)
			{
			PeriodLabel = "Lifetime";
			return (null, null);
			}

		var (start, end) = GetPeriodRange (SelectedPeriod, _anchor);
		PeriodLabel = BuildPeriodLabel (SelectedPeriod, start, end);
		return (ToRfc3339 (start), ToRfc3339 (end));
		}

	// Converts the UI's string period (bound to Powerwall.HistoryPeriods via the Periods combo box) to the
	// HistoryPeriod enum required by the typed Get*CalendarHistoryAsync methods.
	private static HistoryPeriod ToHistoryPeriod (string period) =>
		period switch
			{
			"week" => HistoryPeriod.Week,
			"month" => HistoryPeriod.Month,
			"year" => HistoryPeriod.Year,
			LifetimePeriod => HistoryPeriod.Lifetime,
			_ => HistoryPeriod.Day
			};

	private static (DateTimeOffset Start, DateTimeOffset End) GetPeriodRange (string period, DateTimeOffset anchor)
		{
		var local = anchor.ToLocalTime ();
		switch (period)
			{
			case "week":
				var weekStart = StartOfWeek (local);
				return (weekStart, weekStart.AddDays (7).AddSeconds (-1));
			case "month":
				var monthStart = LocalMidnight (local.Year, local.Month, 1);
				return (monthStart, monthStart.AddMonths (1).AddSeconds (-1));
			case "year":
				var yearStart = LocalMidnight (local.Year, 1, 1);
				return (yearStart, yearStart.AddYears (1).AddSeconds (-1));
			default:
				var dayStart = LocalMidnight (local.Year, local.Month, local.Day);
				return (dayStart, dayStart.AddDays (1).AddSeconds (-1));
			}
		}

	private static string BuildPeriodLabel (string period, DateTimeOffset start, DateTimeOffset end)
		{
		var culture = CultureInfo.CurrentCulture;
		return period switch
			{
			"week" => start.Year == end.Year
				? $"{start.ToString ("MMM d", culture)} - {end.ToString ("MMM d, yyyy", culture)}"
				: $"{start.ToString ("MMM d, yyyy", culture)} - {end.ToString ("MMM d, yyyy", culture)}",
			"month" => start.ToString ("MMMM yyyy", culture),
			"year" => start.ToString ("yyyy", culture),
			_ => start.ToString ("MMMM d, yyyy", culture)
			};
		}

	// Weeks always run Sunday-Saturday, regardless of the current culture's first-day-of-week (e.g. en-GB
	// defaults to Monday), matching the Tesla app's convention.
	private static DateTimeOffset StartOfWeek (DateTimeOffset local)
		{
		int current = (int) local.DayOfWeek;
		var day = local.Date.AddDays (-current);
		return LocalMidnight (day.Year, day.Month, day.Day);
		}

	private static DateTimeOffset LocalMidnight (int year, int month, int day)
		{
		var midnight = new DateTime (year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
		return new DateTimeOffset (midnight, TimeZoneInfo.Local.GetUtcOffset (midnight));
		}

	private static string ToRfc3339 (DateTimeOffset value) =>
		value.ToString ("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

	private void OnComponentSelectionChanged (object? sender, PropertyChangedEventArgs e)
		{
		if (e.PropertyName != nameof (EnergySeriesOption.IsSelected))
			return;

		// Enforce single selection: choosing one component deselects every other one, mirroring the Tesla
		// app's one-graph-at-a-time picker (a RadioButton group in EnergyView.xaml also enforces this
		// visually, but this keeps the invariant true regardless of the control used to select).
		if (sender is EnergySeriesOption { IsSelected: true } selected)
			{
			foreach (var component in Components)
				{
				if (!ReferenceEquals (component, selected))
					component.IsSelected = false;
				}
			}

		BuildSeries ();
		}

	private void OnSiteLabelChanged (object? sender, EventArgs e) =>
		RunOnUi (() => SiteLabel = _connection.SiteLabel);

	private void BuildSeries ()
		{
		var selected = Components.FirstOrDefault (c => c.IsSelected);
		if (_buckets.Count == 0 || selected is null)
			{
			Series = Array.Empty<ISeries> ();
			return;
			}

		bool isDay = SelectedPeriod == "day";

		XAxes[0].Labels = _buckets.Select (b => b.Label).ToArray ();

		// The day period now plots one raw ~5-minute sample per point (see BuildDayPoints); labeling every one
		// produces a cluttered axis. MinStep/ForceStepToMin only hint at a "nice" step and LiveCharts can still
		// apply its own separator-density heuristic on a category axis (this previously produced inconsistent
		// spacing). CustomSeparators instead pins labels to exact bucket indices - every DaySamplesPerHour-th
		// one, i.e. the top of each hour - deterministically. Buckets not on a separator index still bind
		// Labels[index] for their tooltip; only the visible axis label/gridline is skipped. Other periods keep
		// one label per bucket via the default (null) automatic separators.
		XAxes[0].CustomSeparators = isDay
			? Enumerable.Range (0, _buckets.Count).Where (i => i % DaySamplesPerHour == 0).Select (i => (double) i).ToArray ()
			: null;

		// The day period plots average power (kW) per raw sample instead of summed energy (kWh) per bucket
		// (see BuildDayPoints); every other period still plots summed energy. The Y axis title and each
		// series' tooltip both need to reflect whichever unit is currently being plotted.
		string unit = isDay ? "kW" : "kWh";
		YAxes[0].Name = unit;

		// Exactly one series is ever plotted at a time (see Components above), matching the Tesla app rather
		// than overlaying every component on a single chart.
		Series = new ISeries[] { LineSeries (selected.Name, _buckets.Select (selected.ValueSelector), selected.Color, unit) };
		}

	// Resamples raw history points into period-appropriate buckets. Buckets with no matching raw points
	// (including any not yet reached in the current period) naturally sum to zero rather than being omitted.
	// The day period is handled separately (see BuildDayPoints): it plots each raw sample directly as average
	// power rather than summing into fixed slots.
	private static IReadOnlyList<EnergyBucket> BuildBuckets (string period, DateTimeOffset anchor, IReadOnlyList<EnergyHistoryPoint> points)
		{
		if (period == LifetimePeriod)
			return BuildLifetimeBuckets (points);

		if (period == "day")
			return BuildDayPoints (points);

		var slots = BuildSlots (period, anchor);
		var buckets = new List<EnergyBucket> (slots.Count);

		foreach (var slot in slots)
			{
			double solar = 0, home = 0, fromGrid = 0, toGrid = 0, batteryCharge = 0, batteryDischarge = 0;
			foreach (var point in points)
				{
				var local = point.Timestamp.ToLocalTime ();
				if (local >= slot.Start && local < slot.End)
					{
					solar += point.SolarKwh;
					home += point.HomeKwh;
					fromGrid += point.FromGridKwh;
					toGrid += point.ToGridKwh;
					batteryCharge += point.BatteryChargeKwh;
					batteryDischarge += point.BatteryDischargeKwh;
					}
				}

			buckets.Add (new EnergyBucket (
				slot.Label,
				RoundToTenth (solar),
				RoundToTenth (home),
				RoundToTenth (fromGrid),
				RoundToTenth (toGrid),
				RoundToTenth (batteryCharge),
				RoundToTenth (batteryDischarge)));
			}

		return buckets;
		}

	// Lifetime has no fixed period length to bucket against, so each raw point (as returned by Tesla) becomes
	// its own bucket, labeled by month and year.
	private static IReadOnlyList<EnergyBucket> BuildLifetimeBuckets (IReadOnlyList<EnergyHistoryPoint> points) =>
		points
			.OrderBy (p => p.Timestamp)
			.Select (p => new EnergyBucket (
				p.Timestamp.ToLocalTime ().ToString ("MMM yyyy", CultureInfo.CurrentCulture),
				RoundToTenth (p.SolarKwh),
				RoundToTenth (p.HomeKwh),
				RoundToTenth (p.FromGridKwh),
				RoundToTenth (p.ToGridKwh),
				RoundToTenth (p.BatteryChargeKwh),
				RoundToTenth (p.BatteryDischargeKwh)))
			.ToArray ();

	// The day period plots every raw sample directly instead of summing into fixed slots, so its line closely
	// follows the source data (compare the Tesla app's own day view, which does the same). Tesla's
	// calendar-history "energy" kind reports energy accumulated over each interval (kWh), not instantaneous
	// power, so each sample is converted to average power (kW) over its interval - dividing the energy delta
	// by the elapsed time - which is the closest available approximation of an instantaneous reading.
	private static IReadOnlyList<EnergyBucket> BuildDayPoints (IReadOnlyList<EnergyHistoryPoint> points)
		{
		var ordered = points.OrderBy (p => p.Timestamp).ToArray ();
		var buckets = new List<EnergyBucket> (ordered.Length);

		for (int i = 0; i < ordered.Length; i++)
			{
			var point = ordered[i];

			// The interval a sample's energy was accumulated over is the gap to the *next* sample; the last
			// sample of the (day-bounded) response has none, so it reuses the previous gap instead. The literal
			// fallback only applies when there is just a single sample total, which should not happen in practice.
			var duration = i + 1 < ordered.Length
				? ordered[i + 1].Timestamp - point.Timestamp
				: i > 0
					? point.Timestamp - ordered[i - 1].Timestamp
					: TimeSpan.FromMinutes (5);

			double ToKw (double kwh) => duration.TotalHours > 0 ? kwh / duration.TotalHours : 0;

			buckets.Add (new EnergyBucket (
				point.Timestamp.ToLocalTime ().ToString ("HH:mm", CultureInfo.CurrentCulture),
				RoundToTenth (ToKw (point.SolarKwh)),
				RoundToTenth (ToKw (point.HomeKwh)),
				RoundToTenth (ToKw (point.FromGridKwh)),
				RoundToTenth (ToKw (point.ToGridKwh)),
				RoundToTenth (ToKw (point.BatteryChargeKwh)),
				RoundToTenth (ToKw (point.BatteryDischargeKwh))));
			}

		return buckets;
		}

	// Builds the fixed bucket slots for a period: week = 4 x 6 hours per day (starting Sunday), month = one
	// per calendar day, year = one per calendar month. The day period does not use fixed slots - see
	// BuildDayPoints.
	private static IReadOnlyList<BucketSlot> BuildSlots (string period, DateTimeOffset anchor)
		{
		var (rangeStart, _) = GetPeriodRange (period, anchor);
		var slots = new List<BucketSlot> ();

		switch (period)
			{
			case "week":
				for (int day = 0; day < 7; day++)
					{
					var date = rangeStart.Date.AddDays (day);
					var dayStart = LocalMidnight (date.Year, date.Month, date.Day);
					var dayName = dayStart.ToString ("ddd", CultureInfo.CurrentCulture);
					for (int slot = 0; slot < 4; slot++)
						{
						var slotStart = dayStart.AddHours (slot * 6);
						slots.Add (new BucketSlot (slotStart, slotStart.AddHours (6), $"{dayName} {slotStart:HH:mm}"));
						}
					}
				break;

			case "month":
				int daysInMonth = DateTime.DaysInMonth (rangeStart.Year, rangeStart.Month);
				for (int day = 1; day <= daysInMonth; day++)
					{
					var dayStart = LocalMidnight (rangeStart.Year, rangeStart.Month, day);
					slots.Add (new BucketSlot (dayStart, dayStart.AddDays (1), day.ToString (CultureInfo.CurrentCulture)));
					}
				break;

			case "year":
				for (int month = 1; month <= 12; month++)
					{
					var monthStart = LocalMidnight (rangeStart.Year, month, 1);
					slots.Add (new BucketSlot (monthStart, monthStart.AddMonths (1), monthStart.ToString ("MMM", CultureInfo.CurrentCulture)));
					}
				break;

			default:
				throw new ArgumentOutOfRangeException (nameof (period), period, "BuildSlots only supports week, month, and year; day and lifetime are handled separately.");
			}

		return slots;
		}

	private static double RoundToTenth (double value) =>
		Math.Round (value, 1, MidpointRounding.AwayFromZero);

	private static LineSeries<double> LineSeries (string name, IEnumerable<double> values, SKColor color, string unit) =>
		new ()
			{
			Name = name,
			Values = values.ToArray (),
			Fill = null,
			Stroke = new SolidColorPaint (color, 2),
			GeometryFill = new SolidColorPaint (color),
			GeometryStroke = null,
			GeometrySize = 4,
			LineSmoothness = 0,
			// Bucket values are already rounded to the nearest 0.1, but LiveCharts' default tooltip formatter
			// still prints full double precision (e.g. from floating-point summation); format explicitly so the
			// hover details always match the 0.1 rounding shown everywhere else. Math.Abs undoes the Powerwall
			// and Grid series' sign (see Components above) so the tooltip always reads as a positive amount
			// rather than exposing the sign used purely to position the line. unit is "kW" for the day period
			// (average power per raw sample) and "kWh" for every other period (summed energy per bucket) - see
			// BuildSeries.
			YToolTipLabelFormatter = point => $"{Math.Abs (point.Coordinate.PrimaryValue).ToString ("0.0", CultureInfo.InvariantCulture)} {unit}"
			};

	private readonly record struct BucketSlot (DateTimeOffset Start, DateTimeOffset End, string Label);
	}

/// <summary>
/// A single, period-aligned bucket of resampled energy-history data ready for charting, rounded to the
/// nearest 0.1. For every period except day, each value is total energy in kilowatt-hours summed over the
/// bucket. For the day period (see <c>BuildDayPoints</c>), each value is instead average power in kilowatts
/// for that single raw sample, since the day period plots samples directly rather than summed buckets.
/// </summary>
/// <param name="Label">The formatted time label for the X axis.</param>
/// <param name="SolarKwh">Solar energy produced (or average power, for the day period).</param>
/// <param name="HomeKwh">Home (consumer) energy used (or average power, for the day period).</param>
/// <param name="FromGridKwh">Energy imported from the grid (or average power, for the day period).</param>
/// <param name="ToGridKwh">Energy exported to the grid (or average power, for the day period).</param>
/// <param name="BatteryChargeKwh">Gross energy charged into the Powerwall battery (or average power, for the day period).</param>
/// <param name="BatteryDischargeKwh">Gross energy discharged from the Powerwall battery (or average power, for the day period).</param>
public sealed record EnergyBucket (
	string Label,
	double SolarKwh,
	double HomeKwh,
	double FromGridKwh,
	double ToGridKwh,
	double BatteryChargeKwh,
	double BatteryDischargeKwh);

/// <summary>
/// A single, selectable energy component shown in the chart picker. Exactly one component is selected at a
/// time (see <see cref="EnergyViewModel.Components"/>); toggling <see cref="IsSelected"/> swaps which single
/// series is graphed.
/// </summary>
public sealed partial class EnergySeriesOption : ObservableObject
	{
	/// <summary>Initializes a new instance of the <see cref="EnergySeriesOption"/> class.</summary>
	/// <param name="name">The display name shown in the key and chart tooltip.</param>
	/// <param name="color">The series fill color.</param>
	/// <param name="valueSelector">Projects a bucket onto this component's value (kilowatt-hours, or kilowatts for the day period).</param>
	public EnergySeriesOption (string name, SKColor color, Func<EnergyBucket, double> valueSelector)
		{
		Name = name;
		Color = color;
		ValueSelector = valueSelector ?? throw new ArgumentNullException (nameof (valueSelector));
		}

	/// <summary>Gets the display name shown in the key.</summary>
	public string Name { get; }

	/// <summary>Gets the series fill color.</summary>
	public SKColor Color { get; }

	/// <summary>Gets the media brush used to tint the key swatch, matching <see cref="Color"/>.</summary>
	public System.Windows.Media.Brush Swatch =>
		new System.Windows.Media.SolidColorBrush (
			System.Windows.Media.Color.FromArgb (Color.Alpha, Color.Red, Color.Green, Color.Blue));

	/// <summary>Gets the selector that projects a bucket onto this component's value.</summary>
	public Func<EnergyBucket, double> ValueSelector { get; }

	/// <summary>Gets or sets a value indicating whether this is the single currently-graphed component.</summary>
	[ObservableProperty]
	private bool _isSelected;
	}
