// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.Models;
using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the Energy history screen. Loads calendar-aligned energy history for a selectable period and renders
/// exactly one of solar, home, Powerwall, or grid as a single line chart at a time, mirroring the Tesla app's
/// own single-series presentation instead of combining every component onto one chart. Energy history is a
/// cloud-sourced feature, cached locally by the desktop app independently of live LAN readings.
/// </summary>
public sealed partial class EnergyViewModel : ViewModelBase, IDisposable
	{
	private const string LifetimePeriod = "lifetime";

	private readonly PowerwallConnectionService _connection;
	private readonly EnergyHistoryCache _historyCache;
	private readonly CloudHistorySource _historySource = new ();
	private readonly Func<PowerwallMode> _mode;
	private IReadOnlyList<StoredEnergyPoint> _historyPoints = Array.Empty<StoredEnergyPoint> ();
	private string? _historySiteId;
	private int _generation;
	private IReadOnlyList<EnergyBucket> _buckets = Array.Empty<EnergyBucket> ();
	private DateTimeOffset _anchor;
	private readonly Func<DateTimeOffset> _now;
	private readonly System.Windows.Threading.DispatcherTimer? _calendarTimer;
	private bool _followCurrentPeriod = true;
	private bool _disposed;
	private bool _pendingHistoryLoad;
	private bool _pendingHistoryForce;

	/// <summary>Initializes a new instance of the <see cref="EnergyViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public EnergyViewModel (PowerwallConnectionService connection)
		: this (connection, new EnergyHistoryCache (Path.Combine (Path.GetDirectoryName (AppSettingsStore.FilePath)!, "energy-history.sqlite")))
		{
		}

	/// <summary>Creates the screen with a testable history store and optional mode source.</summary>
	/// <param name="connection">The live connection, kept separate from cloud history.</param>
	/// <param name="historyCache">The desktop history store.</param>
	/// <param name="mode">Optional mode source for offline presentation tests.</param>
	/// <param name="now">Optional clock for deterministic calendar-boundary tests.</param>
	internal EnergyViewModel (PowerwallConnectionService connection, EnergyHistoryCache historyCache, Func<PowerwallMode>? mode = null, Func<DateTimeOffset>? now = null)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		_now = now ?? (() => DateTimeOffset.Now);
		_anchor = _now ();
		_historyCache = historyCache;
		_localStore = new LocalPowerHistoryStore (historyCache.DatabasePath);
		_connection.SnapshotUpdated += OnLocalSnapshot;
		_connection.PollFailed += OnLocalPollFailed;
		_mode = mode ?? (() => _connection.Mode);
		var settings = AppSettingsStore.Load ();
		_historyProvider = settings.HistoryMode == "FleetApi" || (settings.HistoryMode is null && !string.IsNullOrWhiteSpace (settings.FleetApiClientId)) ? "Fleet" : "Owner";
		_connection.ConnectionChanged += OnConnectionChanged;
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
			// Day displays signed battery power. Longer periods display the separately reported
			// contribution to home loads, excluding charging and grid exports.
			new ("Powerwall", new SKColor (0x34, 0xC7, 0x59), p => SelectedPeriod == "day" ? p.BatteryDischargeKwh - p.BatteryChargeKwh : p.BatteryToHomeKwh),
			// From-grid (importing) plots above the zero line and to-grid (exporting) plots below it, for the
			// same reason as Powerwall above, mirroring the Tesla app's single "Grid" graph.
			new ("Grid", new SKColor (0x8E, 0x8E, 0x93), p => p.FromGridKwh - p.ToGridKwh)
			};

		foreach (var component in Components)
			component.PropertyChanged += OnComponentSelectionChanged;

		// Day uses elapsed minutes; aggregated periods use categorical bucket labels.
		XAxes = new[] { new Axis { LabelsRotation = 0, TextSize = 11, NamePaint = null, MinStep = 1, ForceStepToMin = true } };

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
		if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
			{
			_calendarTimer = new System.Windows.Threading.DispatcherTimer (System.Windows.Threading.DispatcherPriority.Background, dispatcher);
			_calendarTimer.Tick += OnCalendarBoundary;
			ScheduleCalendarBoundary ();
			}
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
	[NotifyPropertyChangedFor (nameof (HasChartData))]
	private ISeries[] _series = Array.Empty<ISeries> ();

	/// <summary>Gets the chart X axes.</summary>
	public Axis[] XAxes { get; }

	/// <summary>Gets the chart Y axes.</summary>
	public Axis[] YAxes { get; }

	/// <summary>Gets whether the connected mode can display local readings or cloud history.</summary>
	public bool IsAvailable => _mode () is PowerwallMode.Cloud or PowerwallMode.FleetApi or PowerwallMode.Local;

	/// <summary>Gets whether a cloud source has been selected for historical backfill.</summary>
	public bool CanLoadCloudHistory => _mode () is PowerwallMode.Cloud or PowerwallMode.FleetApi || (IsLocal && _historySiteId is not null);

	/// <summary>Gets whether live readings use LAN while cloud history is configured separately.</summary>
	public bool IsLocal => _mode () == PowerwallMode.Local;

	/// <summary>Gets whether the chart has actual samples instead of default empty axes.</summary>
	public bool HasChartData => Series.Length > 0;

	/// <summary>Gets the supported saved cloud providers.</summary>
	public string[] HistoryProviders { get; } = { "Owner", "Fleet" };

	/// <summary>Gets or sets the cloud provider used only for past history.</summary>
	[ObservableProperty]
	private string _historyProvider;

	/// <summary>Gets the sites returned by the chosen history account.</summary>
	public ObservableCollection<CloudSite> HistorySites { get; } = new ();

	/// <summary>Gets or sets the site selected for association with this local host.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor (nameof (UseHistorySiteCommand))]
	private CloudSite? _selectedHistorySite;

	/// <summary>Gets the source and site of the displayed history, independently of the live connection.</summary>
	[ObservableProperty]
	private string _historySourceLabel = "History has not been loaded.";

	partial void OnHistoryProviderChanged (string value)
		{
		_generation++;
		_historySiteId = null;
		SelectedHistorySite = null;
		HistorySites.Clear ();
		ClearHistory ();
		RestoreHistoryAssociation ();
		}

	/// <summary>Lists the selected account's sites without replacing or polling the live LAN connection.</summary>
	/// <returns>A task completing when site choices are available.</returns>
	[RelayCommand]
	private async Task ConnectHistoryAsync ()
		{
		if (!IsLocal || IsBusy)
			return;
		IsBusy = true;
		StatusMessage = "Reading sites from the saved " + HistoryProvider + " account...";
		int generation = _generation;
		try
			{
			using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			var sites = await _historySource.GetSitesAsync (HistoryProvider, timeout.Token).ConfigureAwait (true);
			if (generation != _generation)
				return;
			if (_historySiteId is not null)
				{
				if (!sites.Any (site => site.SiteId == _historySiteId))
					throw new InvalidOperationException ("This account cannot access the site belonging to this local Powerwall. Choose an account that can.");
				var preferences = AppSettingsStore.Load ();
				preferences.HistoryMode = HistoryProvider == "Fleet" ? "FleetApi" : "Cloud";
				AppSettingsStore.Save (preferences);
				StatusMessage = "Account can access this Powerwall's history site.";
				return;
				}
			HistorySites.Clear ();
			foreach (var site in sites)
				HistorySites.Add (site);
			SelectedHistorySite = sites.Count == 1 ? sites[0] : null;
			StatusMessage = sites.Count == 0 ? "No cloud history sites were returned." : "Choose the cloud site matching this Powerwall, then select Use site.";
			}
		catch (Exception exc) when (exc is PowerwallException or InvalidOperationException or HttpRequestException or OperationCanceledException)
			{
			if (generation == _generation)
				StatusMessage = exc is OperationCanceledException ? "Cloud history sign-in timed out." : exc.Message;
			}
		finally
			{
			IsBusy = false;
			}
		}

	private bool CanUseHistorySite () => SelectedHistorySite is not null && !HasBoundHistorySite;

	/// <summary>Gets whether this local device already has its permanent cloud-site association.</summary>
	public bool HasBoundHistorySite => _historySiteId is not null;

	/// <summary>Gets the history account action label for an unbound or previously identified device.</summary>
	public string HistoryAccountAction => HasBoundHistorySite ? "Verify account" : "Find site";

	/// <summary>Remembers an explicit host-to-site association and loads its history.</summary>
	/// <returns>A task completing after the selected period is loaded.</returns>
	[RelayCommand (CanExecute = nameof (CanUseHistorySite))]
	private async Task UseHistorySiteAsync ()
		{
		if (!IsLocal || IsBusy || SelectedHistorySite is null)
			return;
		_generation++;
		ClearHistory ();
		var settings = AppSettingsStore.Load ();
		LocalHistoryBinding.Bind (settings, _connection.LocalDeviceId,
			new LocalHistorySite { SiteId = SelectedHistorySite.SiteId, SiteName = SelectedHistorySite.SiteName });
		settings.HistoryMode = HistoryProvider == "Fleet" ? "FleetApi" : "Cloud";
		settings.HistoryHost = _connection.SiteLabel;
		settings.HistoryAccount = CloudHistorySource.AccountScope (HistoryProvider, settings);
		settings.HistorySiteId = SelectedHistorySite.SiteId;
		settings.HistorySiteName = SelectedHistorySite.SiteName;
		AppSettingsStore.Save (settings);
		_historySiteId = SelectedHistorySite.SiteId;
		if (_connection.SiteName is null) _connection.SetSiteName (SelectedHistorySite.SiteName);
		NotifyHistoryBinding ();
		HistorySourceLabel = $"Live readings: LAN. History: {HistoryProvider} / {SelectedHistorySite.SiteName ?? _historySiteId}.";
		OnPropertyChanged (nameof (IsAvailable));
		OnPropertyChanged (nameof (CanLoadCloudHistory));
		await LoadAsync ().ConfigureAwait (true);
		}

	partial void OnSelectedPeriodChanged (string value)
		{
		// Switching the aggregation period resets navigation back to the current, most-recent bucket.
		_generation++;
		_anchor = _now ();
		_followCurrentPeriod = true;
		ResolveRange ();
		ClearHistory ();
		PreviousPeriodCommand.NotifyCanExecuteChanged ();
		NextPeriodCommand.NotifyCanExecuteChanged ();
		_ = LoadAsync ();
		}

	/// <summary>Loads energy history for the selected period and rebuilds the chart series.</summary>
	/// <returns>A task that completes when the history has been loaded.</returns>
	[RelayCommand]
	private Task LoadAsync () => LoadHistoryAsync (false);

	/// <summary>Explicitly refreshes history from the cloud, including a settled cached period.</summary>
	/// <returns>A task completing when refreshed history is displayed.</returns>
	[RelayCommand]
	private Task RefreshHistoryAsync () => LoadHistoryAsync (true);

	private async Task LoadHistoryAsync (bool force)
		{
		if (IsBusy)
			{
			_pendingHistoryLoad = true;
			_pendingHistoryForce |= force;
			return;
			}
		if (_disposed) return;
		AdvanceCurrentPeriod (_now ());
		ResolveRange ();
		ClearHistory ();
		if (IsLocal)
			await LoadLocalHistoryAsync ().ConfigureAwait (true);
		if (!CanLoadCloudHistory)
			{
			StatusMessage = IsLocal
				? "LAN readings are plotted and saved as they arrive. To fill earlier history, choose a saved Owner or Fleet account and link its site."
				: "Connect to a Powerwall to view energy history.";
			return;
			}
		var (startDate, endDate) = ResolveRange ();
		string timezone = TimeZoneInfo.TryConvertWindowsIdToIanaId (TimeZoneInfo.Local.Id, out var iana) ? iana : TimeZoneInfo.Local.Id;
		var settings = AppSettingsStore.Load ();
		string provider = IsLocal ? HistoryProvider : _mode () == PowerwallMode.FleetApi ? "Fleet" : "Owner";
		string site = IsLocal ? _historySiteId! : _connection.Powerwall.CloudSiteId ?? throw new InvalidOperationException ("No cloud site selected.");
		var request = new EnergyHistoryRequest (CloudHistorySource.AccountScope (provider, settings), site, SelectedPeriod, timezone,
			startDate is null ? null : DateTimeOffset.Parse (startDate, CultureInfo.InvariantCulture),
			endDate is null ? null : DateTimeOffset.Parse (endDate, CultureInfo.InvariantCulture));
		bool local = IsLocal;
		var cloud = local ? null : _connection.Powerwall;
		var period = ToHistoryPeriod (SelectedPeriod);
		int generation = _generation;
		IsBusy = true;
		StatusMessage = "Loading energy history...";
		try
			{
			using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			Task<IReadOnlyList<EnergyHistoryPoint>> Fetch (CancellationToken token)
				{
				if (local)
					return _historySource.FetchAsync (provider, request, period, token);
				if (cloud!.CloudSiteId != request.Site || generation != _generation)
					throw new InvalidOperationException ("The connected history site changed. Load the selected site again.");
				return cloud.GetEnergyCalendarHistoryAsync (period, timezone, startDate, endDate, token);
				}
			bool contribution = SelectedPeriod != "day" && Components.Any (component => component.Name == "Powerwall" && component.IsSelected);
			var result = await _historyCache.GetAsync (request, Fetch, force, timeout.Token, contribution).ConfigureAwait (true);
			if (generation != _generation)
				return;
			ApplyHistory (result.Points);
			string siteName = (local ? LocalHistoryBinding.Find (settings, _connection.LocalDeviceId)?.SiteName : _connection.SiteLabel)?.Trim () ?? site;
			HistorySourceLabel = $"{(local ? "Live readings: LAN. " : string.Empty)}History: {provider} / {siteName}. "
				+ $"{(result.FromCache ? "Saved locally" : "Retrieved from cloud")}, {result.RetrievedAt.ToLocalTime ():g}.";
			StatusMessage = result.Warning ?? (result.Points.Count == 0 ? "No energy history was returned for this period." : null);
			}
		catch (Exception exc) when (exc is OperationCanceledException or PowerwallException or InvalidOperationException or HttpRequestException)
			{
			if (generation == _generation)
				StatusMessage = exc is OperationCanceledException ? "Loading energy history timed out." : $"Could not load energy history: {exc.Message}";
			}
		finally
			{
			IsBusy = false;
			PreviousPeriodCommand.NotifyCanExecuteChanged ();
			NextPeriodCommand.NotifyCanExecuteChanged ();
			if (_pendingHistoryLoad && !_disposed)
				{
				bool pendingForce = _pendingHistoryForce;
				_pendingHistoryLoad = _pendingHistoryForce = false;
				await LoadHistoryAsync (pendingForce).ConfigureAwait (true);
				}
			}
		}

	/// <summary>Projects typed history into the selected graph without network access.</summary>
	/// <param name="points">Samples from the requested period.</param>
	internal void ApplyHistory (IReadOnlyList<StoredEnergyPoint> points)
		{
		_historyPoints = points.OrderBy (p => p.Timestamp).ToArray ();
		_buckets = BuildBuckets (SelectedPeriod, _anchor, _historyPoints);
		BuildSeries ();
		}

	/// <summary>Steps the graph back to the previous period and reloads.</summary>
	/// <returns>A task that completes when the previous period has loaded.</returns>
	[RelayCommand (CanExecute = nameof (CanGoPrevious))]
	private Task PreviousPeriodAsync ()
		{
		_generation++;
		_anchor = StepAnchor (-1);
		_followCurrentPeriod = false;
		NextPeriodCommand.NotifyCanExecuteChanged ();
		return LoadAsync ();
		}

	/// <summary>Steps the graph forward to the next period and reloads.</summary>
	/// <returns>A task that completes when the next period has loaded.</returns>
	[RelayCommand (CanExecute = nameof (CanGoNext))]
	private Task NextPeriodAsync ()
		{
		_generation++;
		_anchor = StepAnchor (+1);
		_followCurrentPeriod = !IsBeforeCurrentPeriod ();
		NextPeriodCommand.NotifyCanExecuteChanged ();
		return LoadAsync ();
		}

	private bool CanGoPrevious () => IsAvailable && SelectedPeriod != LifetimePeriod;

	private bool CanGoNext () => IsAvailable && SelectedPeriod != LifetimePeriod && IsBeforeCurrentPeriod ();

	// The current, most-recent bucket is the newest data available; stepping past it would request the future.
	private bool IsBeforeCurrentPeriod () =>
		GetPeriodRange (SelectedPeriod, _anchor).Start < GetPeriodRange (SelectedPeriod, _now ()).Start;

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
				var nextWeek = weekStart.Date.AddDays (7);
				return (weekStart, LocalMidnight (nextWeek.Year, nextWeek.Month, nextWeek.Day).AddSeconds (-1));
			case "month":
				var monthStart = LocalMidnight (local.Year, local.Month, 1);
				var nextMonth = monthStart.Date.AddMonths (1);
				return (monthStart, LocalMidnight (nextMonth.Year, nextMonth.Month, nextMonth.Day).AddSeconds (-1));
			case "year":
				var yearStart = LocalMidnight (local.Year, 1, 1);
				return (yearStart, LocalMidnight (local.Year + 1, 1, 1).AddSeconds (-1));
			default:
				var dayStart = LocalMidnight (local.Year, local.Month, local.Day);
				var nextDay = local.Date.AddDays (1);
				return (dayStart, LocalMidnight (nextDay.Year, nextDay.Month, nextDay.Day).AddSeconds (-1));
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

	// Match the Monday-Sunday calendar window returned by Tesla weekly history.
	private static DateTimeOffset StartOfWeek (DateTimeOffset local)
		{
		int current = ((int) local.DayOfWeek + 6) % 7;
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
		if (sender is EnergySeriesOption { IsSelected: true, Name: "Powerwall" } && SelectedPeriod != "day"
			&& _historyPoints.Any (point => point.BatteryToHomeKwh is null) && CanLoadCloudHistory)
			_ = LoadAsync ();
		}

	private void OnSiteLabelChanged (object? sender, EventArgs e) => OnConnectionChanged (sender, e);

	private void OnConnectionChanged (object? sender, EventArgs e) => RunOnUi (() =>
		{
		_generation++;
		SiteLabel = _connection.SiteLabel;
		_latestLocal = null;
		_localSamples.Clear ();
		LiveReadingText = "Waiting for a LAN reading.";
		_historySiteId = null;
		SelectedHistorySite = null;
		HistorySites.Clear ();
		ClearHistory ();
		RestoreHistoryAssociation ();
		_ = LoadLocalHistoryAsync ();
		OnPropertyChanged (nameof (IsLocal));
		OnPropertyChanged (nameof (IsAvailable));
		OnPropertyChanged (nameof (CanLoadCloudHistory));
		});

	private void ClearHistory ()
		{
		_historyPoints = Array.Empty<StoredEnergyPoint> ();
		_buckets = Array.Empty<EnergyBucket> ();
		Series = Array.Empty<ISeries> ();
		StatusMessage = null;
		BuildSeries ();
		}

	private void RestoreHistoryAssociation ()
		{
		var settings = AppSettingsStore.Load ();
		var binding = IsLocal ? LocalHistoryBinding.Find (settings, _connection.LocalDeviceId) : null;
		// Upgrade the previously confirmed host association once, then use hardware identity thereafter.
		if (binding is null && IsLocal && _connection.LocalDeviceId is not null
			&& string.Equals (settings.HistoryHost, _connection.SiteLabel, StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace (settings.HistorySiteId))
			{
			binding = new LocalHistorySite { SiteId = settings.HistorySiteId, SiteName = settings.HistorySiteName };
			LocalHistoryBinding.Bind (settings, _connection.LocalDeviceId, binding);
			settings.HistoryHost = null;
			AppSettingsStore.Save (settings);
			}
		if (binding is not null)
			{
			_historySiteId = binding.SiteId;
			if (_connection.SiteName is null) _connection.SetSiteName (binding.SiteName);
			HistorySourceLabel = $"Live readings: LAN. History: {HistoryProvider} / {binding.SiteName ?? _historySiteId}.";
			}
		else
			HistorySourceLabel = IsLocal ? "Live readings: LAN. Cloud history is not linked." : "Cloud history is cached on this computer.";
		OnPropertyChanged (nameof (IsAvailable));
		OnPropertyChanged (nameof (CanLoadCloudHistory));
		NotifyHistoryBinding ();
		PreviousPeriodCommand.NotifyCanExecuteChanged ();
		NextPeriodCommand.NotifyCanExecuteChanged ();
		}

	private void NotifyHistoryBinding ()
		{
		OnPropertyChanged (nameof (HasBoundHistorySite));
		OnPropertyChanged (nameof (HistoryAccountAction));
		UseHistorySiteCommand.NotifyCanExecuteChanged ();
		}

	/// <summary>Unsubscribes notifications and releases the separate history connection.</summary>
	public void Dispose ()
		{
		_disposed = true;
		if (_calendarTimer is not null)
			{
			_calendarTimer.Stop ();
			_calendarTimer.Tick -= OnCalendarBoundary;
			}
		_connection.SiteLabelChanged -= OnSiteLabelChanged;
		_connection.ConnectionChanged -= OnConnectionChanged;
		_connection.SnapshotUpdated -= OnLocalSnapshot;
		_connection.PollFailed -= OnLocalPollFailed;
		_historySource.Dispose ();
		}


	private void BuildCloudSeries ()
		{
		var selected = Components.FirstOrDefault (c => c.IsSelected);
		// Clear the previous period's limits even when the new response is empty.
		// Keep one orientation across all periods. LiveCharts 2.0.5 retains the old
		// rotation on reused label geometries when an axis changes back to zero.
		XAxes[0].LabelsRotation = 0;
		XAxes[0].Labels = null;
		XAxes[0].CustomSeparators = null;
		XAxes[0].MinLimit = null;
		XAxes[0].MaxLimit = null;
		XAxes[0].Labeler = value => value.ToString ("0", CultureInfo.CurrentCulture);
		YAxes[0].Name = SelectedPeriod == "day" ? "kW" : "kWh";
		if (_buckets.Count == 0 || selected is null)
			{
			Series = Array.Empty<ISeries> ();
			return;
			}

		bool isDay = SelectedPeriod == "day";
		if (isDay)
			{
			var (start, end) = GetPeriodRange ("day", _anchor);
			XAxes[0].Labels = null;
			XAxes[0].CustomSeparators = null;
			XAxes[0].MinLimit = 0;
			XAxes[0].MaxLimit = (end.AddSeconds (1) - start).TotalMinutes;
			XAxes[0].MinStep = 120;
			XAxes[0].ForceStepToMin = false;
			XAxes[0].Labeler = minutes => TimeZoneInfo.ConvertTime (start.AddMinutes (minutes), TimeZoneInfo.Local).ToString ("HH:mm", CultureInfo.CurrentCulture);
			YAxes[0].Name = "kW";
			_cloudDaySource = _historyPoints.Select ((p, i) => new ObservablePoint ((p.Timestamp - start).TotalMinutes, selected.ValueSelector (_buckets[i]) is double value && double.IsFinite (value) ? Math.Round (value, 2, MidpointRounding.ToEven) : null)).OrderBy (p => p.X).ToArray ();
			var values = new ObservableCollection<ObservablePoint> (_cloudDaySource.Select (point => new ObservablePoint (point.X, point.Y)));
			_cloudDayValues = values;
			Series = new ISeries[] { new LineSeries<ObservablePoint>
				{
				Name = selected.Name + " (cloud average)", Values = values, Fill = null, Stroke = new SolidColorPaint (new SKColor (0x7C, 0x3A, 0xED), 2),
				GeometrySize = 0, GeometryFill = new SolidColorPaint (new SKColor (0x7C, 0x3A, 0xED)), GeometryStroke = null, LineSmoothness = 0,
				XToolTipLabelFormatter = point => XAxes[0].Labeler (point.Coordinate.SecondaryValue),
				YToolTipLabelFormatter = point => selected.Name == "Powerwall"
					? $"{point.Coordinate.PrimaryValue:+0.00;-0.00;0.00} kW ({(point.Coordinate.PrimaryValue < 0 ? "charging" : point.Coordinate.PrimaryValue > 0 ? "discharging" : "idle")})"
					: $"{Math.Abs (point.Coordinate.PrimaryValue):0.00} kW"
				} };
			return;
			}
		XAxes[0].MinLimit = -0.5;
		XAxes[0].MaxLimit = _buckets.Count - 0.5;
		XAxes[0].MinStep = SelectedPeriod == LifetimePeriod ? Math.Max (1, Math.Ceiling (_buckets.Count / 8.0)) : 1;
		XAxes[0].ForceStepToMin = false;

		XAxes[0].Labels = _buckets.Select (b => b.Label).ToArray ();

		XAxes[0].CustomSeparators = null;
		const string unit = "kWh";
		YAxes[0].Name = unit;

		// Exactly one component is ever plotted at a time (see Components above), matching the Tesla app rather
		// than overlaying every component on a single chart.
		Series = new ISeries[] { EnergyBars (selected.Name == "Powerwall" ? "Powerwall to home" : selected.Name, _buckets.Select (bucket => bucket.HasData ? selected.ValueSelector (bucket) : null), selected.Color, unit) };
		}

	// Resamples raw history points into period-appropriate buckets. Empty slots remain gaps,
	// including future slots; only actual reported zero consumption plots at zero.
	// The day period is handled separately (see BuildDayPoints): it plots each raw sample directly as average
	// power rather than summing into fixed slots.
	private static IReadOnlyList<EnergyBucket> BuildBuckets (string period, DateTimeOffset anchor, IReadOnlyList<StoredEnergyPoint> points)
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
			bool hasData = false, contributionKnown = true;
			double batteryToHome = 0;
			foreach (var point in points)
				{
				var local = point.Timestamp.ToLocalTime ();
				if (local >= slot.Start && local < slot.End)
					{
					hasData = true;
					solar += point.SolarKwh;
					home += point.HomeKwh;
					fromGrid += point.FromGridKwh;
					toGrid += point.ToGridKwh;
					batteryCharge += point.BatteryChargeKwh;
					batteryDischarge += point.BatteryDischargeKwh;
					contributionKnown &= point.BatteryToHomeKwh.HasValue;
					batteryToHome += point.BatteryToHomeKwh ?? 0;
					}
				}

			buckets.Add (new EnergyBucket (
				slot.Label,
				RoundToTenth (solar),
				RoundToTenth (home),
				RoundToTenth (fromGrid),
				RoundToTenth (toGrid),
				RoundToTenth (batteryCharge),
				RoundToTenth (batteryDischarge)) { HasData = hasData, BatteryToHomeKwh = hasData && contributionKnown ? Math.Max (0, RoundToTenth (batteryToHome)) : null });
			}

		return buckets;
		}

	// Tesla may return many intervals per month for lifetime. Sum first, round once per calendar month,
	// and retain missing intervening months as gaps rather than compressing the timeline or inventing zero.
	private static IReadOnlyList<EnergyBucket> BuildLifetimeBuckets (IReadOnlyList<StoredEnergyPoint> points)
		{
		if (points.Count == 0) return Array.Empty<EnergyBucket> ();
		var months = points.GroupBy (p =>
			{
			var local = p.Timestamp.ToLocalTime ();
			return new DateTime (local.Year, local.Month, 1);
			}).ToDictionary (group => group.Key, group => group.ToArray ());
		DateTime last = months.Keys.Max ();
		var buckets = new List<EnergyBucket> ();
		for (DateTime month = months.Keys.Min (); month <= last; month = month.AddMonths (1))
			{
			bool hasData = months.TryGetValue (month, out var values);
			values ??= Array.Empty<StoredEnergyPoint> ();
			buckets.Add (new EnergyBucket (month.ToString ("MMM yyyy", CultureInfo.CurrentCulture),
				RoundToTenth (values.Sum (p => p.SolarKwh)), RoundToTenth (values.Sum (p => p.HomeKwh)),
				RoundToTenth (values.Sum (p => p.FromGridKwh)), RoundToTenth (values.Sum (p => p.ToGridKwh)),
				RoundToTenth (values.Sum (p => p.BatteryChargeKwh)), RoundToTenth (values.Sum (p => p.BatteryDischargeKwh)))
				{ HasData = hasData, BatteryToHomeKwh = hasData && values.All (p => p.BatteryToHomeKwh.HasValue) ? Math.Max (0, RoundToTenth (values.Sum (p => p.BatteryToHomeKwh!.Value))) : null });
			if (month.Year == 9999 && month.Month == 12) break;
			}
		return buckets;
		}

	// The day period plots every raw sample directly instead of summing into fixed slots, so its line closely
	// follows the source data (compare the Tesla app's own day view, which does the same). Tesla's
	// calendar-history "energy" kind reports energy accumulated over each interval (kWh), not instantaneous
	// power, so each sample is converted to average power (kW) over its interval - dividing the energy delta
	// by the elapsed time - which is the closest available approximation of an instantaneous reading.
	private static IReadOnlyList<EnergyBucket> BuildDayPoints (IReadOnlyList<StoredEnergyPoint> points)
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
				ToKw (point.SolarKwh),
				ToKw (point.HomeKwh),
				ToKw (point.FromGridKwh),
				ToKw (point.ToGridKwh),
				ToKw (point.BatteryChargeKwh),
				ToKw (point.BatteryDischargeKwh)));
			}

		return buckets;
		}

	// Builds the fixed bucket slots for a period: week = 4 x 6 hours per day (starting Monday), month = one
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

	private static ColumnSeries<double?> EnergyBars (string name, IEnumerable<double?> values, SKColor color, string unit) =>
		new ()
			{
			Name = name,
			Values = values.ToArray (),
			Fill = new SolidColorPaint (color),
			Stroke = null,
			MaxBarWidth = 48,
			Padding = 3,
			// Energy totals belong to their own interval; missing buckets stay empty.
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
	double BatteryDischargeKwh)
	{
	/// <summary>Gets whether this bucket contains reported samples; false means unavailable, not zero.</summary>
	public bool HasData { get; init; } = true;

	/// <summary>Reported battery energy supplied to home loads in this bucket, or null when unavailable.</summary>
	public double? BatteryToHomeKwh { get; init; }
	}

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
	public EnergySeriesOption (string name, SKColor color, Func<EnergyBucket, double?> valueSelector)
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
	public Func<EnergyBucket, double?> ValueSelector { get; }

	/// <summary>Gets or sets a value indicating whether this is the single currently-graphed component.</summary>
	[ObservableProperty]
	private bool _isSelected;
	}
