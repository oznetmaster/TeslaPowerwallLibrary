// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the Energy history screen. Loads calendar-aligned energy history for a selectable period and renders
/// solar, home, and grid import/export as a column chart. Energy history is a cloud-mode feature.
/// </summary>
public sealed partial class EnergyViewModel : ViewModelBase
	{
	private const string LifetimePeriod = "lifetime";

	private readonly PowerwallConnectionService _connection;
	private IReadOnlyList<EnergyHistoryPoint> _points = Array.Empty<EnergyHistoryPoint> ();
	private DateTimeOffset _anchor = DateTimeOffset.Now;

	/// <summary>Initializes a new instance of the <see cref="EnergyViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public EnergyViewModel (PowerwallConnectionService connection)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		Periods = new ObservableCollection<string> (Powerwall.HistoryPeriods);
		_selectedPeriod = Powerwall.DefaultHistoryPeriod;

		Components = new ObservableCollection<EnergySeriesOption>
			{
			new ("Solar", new SKColor (0xF5, 0xB3, 0x01), p => p.SolarKwh),
			new ("Home", new SKColor (0x3E, 0x6A, 0xE1), p => p.HomeKwh),
			new ("From battery", new SKColor (0x30, 0xD1, 0x58), p => p.FromBatteryKwh),
			new ("From grid", new SKColor (0x8E, 0x8E, 0x93), p => p.FromGridKwh),
			new ("To grid", new SKColor (0xAF, 0x52, 0xDE), p => p.ToGridKwh)
			};

		foreach (var component in Components)
			component.PropertyChanged += OnComponentSelectionChanged;

		XAxes = new[] { new Axis { LabelsRotation = 45, TextSize = 11, NamePaint = null } };
		YAxes = new[] { new Axis { Name = "kWh", TextSize = 11 } };
		}

	/// <summary>Gets the selectable aggregation periods.</summary>
	public ObservableCollection<string> Periods { get; }

	/// <summary>
	/// Gets the per-component chart toggles. Each selected component is graphed together; clearing a
	/// component removes just that series without reloading the history.
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

	/// <summary>Gets a value indicating whether energy history is available (cloud mode only).</summary>
	public bool IsAvailable => _connection.Mode == PowerwallMode.Cloud;

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
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			string? body;
			try
				{
				body = await _connection.Powerwall
					.GetCalendarHistoryAsync ("energy", SelectedPeriod, startDate: startDate, endDate: endDate, cancellationToken: cts.Token)
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

			var points = EnergyHistoryParser.ParseEnergy (body);
			if (points.Count == 0)
				{
				_points = Array.Empty<EnergyHistoryPoint> ();
				Series = Array.Empty<ISeries> ();
				StatusMessage = "No energy history was returned for this period.";
				return;
				}

			_points = points;
			BuildSeries ();
			}
		finally
			{
			IsBusy = false;
			PreviousPeriodCommand.NotifyCanExecuteChanged ();
			NextPeriodCommand.NotifyCanExecuteChanged ();
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

	private static DateTimeOffset StartOfWeek (DateTimeOffset local)
		{
		int firstDay = (int) CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
		int current = (int) local.DayOfWeek;
		int diff = (7 + current - firstDay) % 7;
		var day = local.Date.AddDays (-diff);
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
		if (e.PropertyName == nameof (EnergySeriesOption.IsSelected))
			BuildSeries ();
		}

	private void BuildSeries ()
		{
		if (_points.Count == 0)
			{
			Series = Array.Empty<ISeries> ();
			return;
			}

		XAxes[0].Labels = _points.Select (p => p.Label).ToArray ();

		Series = Components
			.Where (c => c.IsSelected)
			.Select (c => ColumnSeries (c.Name, _points.Select (c.ValueSelector), c.Color))
			.Cast<ISeries> ()
			.ToArray ();
		}

	private static ColumnSeries<double> ColumnSeries (string name, IEnumerable<double> values, SKColor color) =>
		new ()
			{
			Name = name,
			Values = values.ToArray (),
			Fill = new SolidColorPaint (color),
			Stroke = null,
			MaxBarWidth = 22
			};
	}

/// <summary>
/// A single, independently selectable energy component shown in the chart key. Toggling <see cref="IsSelected"/>
/// adds or removes just this series from the graph.
/// </summary>
public sealed partial class EnergySeriesOption : ObservableObject
	{
	/// <summary>Initializes a new instance of the <see cref="EnergySeriesOption"/> class.</summary>
	/// <param name="name">The display name shown in the key and chart tooltip.</param>
	/// <param name="color">The series fill color.</param>
	/// <param name="valueSelector">Projects a history point onto this component's kilowatt-hour value.</param>
	public EnergySeriesOption (string name, SKColor color, Func<EnergyHistoryPoint, double> valueSelector)
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

	/// <summary>Gets the selector that projects a history point onto this component's value.</summary>
	public Func<EnergyHistoryPoint, double> ValueSelector { get; }

	/// <summary>Gets or sets a value indicating whether this component is currently graphed.</summary>
	[ObservableProperty]
	private bool _isSelected = true;
	}
