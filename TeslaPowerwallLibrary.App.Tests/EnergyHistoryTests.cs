// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.SKCharts;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Converters;
using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.App.ViewModels;
using TeslaPowerwallLibrary.App.Views;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Exercises real SQLite persistence and conditional cloud access without live credentials.</summary>
[TestFixture]
public sealed class EnergyHistoryCacheTests
	{
	private string _directory = null!;
	private string _path = null!;
	private DateTimeOffset _now;
	private EnergyHistoryRequest _request = null!;
	private int _fetches;

	/// <summary>Creates an isolated database location and a settled historical range.</summary>
	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (Path.GetTempPath (), "PowerwallHistoryTests", Guid.NewGuid ().ToString ("N"));
		_path = Path.Combine (_directory, "history.sqlite");
		_now = new DateTimeOffset (2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
		_request = new EnergyHistoryRequest ("Owner|test", "site-a", "day", "Europe/London", _now.AddDays (-4), _now.AddDays (-3));
		_fetches = 0;
		}

	/// <summary>Deletes only this test's isolated files after all database connections close.</summary>
	[TearDown]
	public void TearDown ()
		{
		if (Directory.Exists (_directory))
			Directory.Delete (_directory, true);
		}

	private Task<IReadOnlyList<EnergyHistoryPoint>> Fetch (CancellationToken token)
		{
		token.ThrowIfCancellationRequested ();
		_fetches++;
		return Task.FromResult<IReadOnlyList<EnergyHistoryPoint>> (new[] { System.Text.Json.JsonSerializer.Deserialize<EnergyHistoryPoint> ("{\"timestamp\":\"2026-10-03T12:00:00Z\",\"solar_energy_exported\":1250,\"grid_energy_imported\":500,\"grid_energy_exported_from_solar\":250,\"battery_energy_exported\":750,\"battery_energy_imported_from_grid\":125,\"consumer_energy_imported_from_solar\":1000}")! });
		}

	/// <summary>Old batches are reused until the missing contribution is required, then upgraded once without guessing.</summary>
	[Test]
	public async Task LegacyCache_RefreshesHomeContributionOnce ()
		{
		var cache = new EnergyHistoryCache (_path, () => _now);
		await cache.GetAsync (_request, Fetch, false, default);
		using (var database = new Microsoft.Data.Sqlite.SqliteConnection ("Pooling=False;Data Source=" + _path))
			{
			database.Open ();
			using var command = database.CreateCommand ();
			command.CommandText = "DELETE FROM history_contributions"; // Reproduce a pre-migration batch.
			command.ExecuteNonQuery ();
			}
		var old = await cache.GetAsync (_request, Fetch, false, default);
		Assert.That (old.FromCache, Is.True);
		Assert.That (old.IncludesHomeContribution, Is.False);
		Assert.That (old.Points.Single ().BatteryToHomeKwh, Is.Null);
		Task<IReadOnlyList<EnergyHistoryPoint>> FetchContribution (CancellationToken token)
			{
			_fetches++;
			return Task.FromResult<IReadOnlyList<EnergyHistoryPoint>> (new[] { System.Text.Json.JsonSerializer.Deserialize<EnergyHistoryPoint> ("""
				{"timestamp":"2026-10-03T12:00:00Z","consumer_energy_imported_from_battery":1250,"battery_energy_exported":5000}
				""")! });
			}
		var upgraded = await cache.GetAsync (_request, FetchContribution, false, default, true);
		Assert.That (upgraded.Points.Single ().BatteryToHomeKwh, Is.EqualTo (1.25));
		Assert.That (upgraded.Points.Single ().BatteryDischargeKwh, Is.EqualTo (5));
		var saved = await cache.GetAsync (_request, FetchContribution, false, default, true);
		Assert.That (saved.FromCache, Is.True);
		Assert.That (saved.IncludesHomeContribution, Is.True);
		Assert.That (saved.Points.Single ().BatteryToHomeKwh, Is.EqualTo (1.25));
		Assert.That (_fetches, Is.EqualTo (2));
		}

	/// <summary>Both cloud providers use their own saved settings and cannot replace or control LAN access.</summary>
	[TestCase ("Owner"), TestCase ("Fleet")]
	public void HistoryOptions_SupportEitherSavedProvider (string provider)
		{
		var settings = new AppSettings { Email = "test@example.invalid", FleetApiClientId = "test-client", FleetApiRegion = "eu", Host = "local.example.invalid" };
		var options = CloudHistorySource.BuildOptions (provider, settings);
		Assert.That (options.CloudMode, Is.True);
		Assert.That (options.FleetApi, Is.EqualTo (provider == "Fleet"));
		Assert.That (options.Host, Is.Empty);
		Assert.That (options.AllowLocalControl, Is.False);
		if (provider == "Owner")
			Assert.That (options.Email, Is.EqualTo (settings.Email));
		else
			{
			Assert.That (options.FleetApiClientId, Is.EqualTo (settings.FleetApiClientId));
			Assert.That (options.FleetApiRegion, Is.EqualTo ("auto"));
			}
		Assert.That (CloudHistorySource.AccountScope ("Owner", settings), Is.Not.EqualTo (CloudHistorySource.AccountScope ("Fleet", settings)));
		}

	/// <summary>Missing accounts yield a useful sign-in instruction rather than trying an unrelated provider.</summary>
	[TestCase ("Owner"), TestCase ("Fleet")]
	public void HistoryOptions_MissingCredentialsDoNotFallBack (string provider)
		{
		var error = Assert.Throws<InvalidOperationException> (() => CloudHistorySource.BuildOptions (provider, new AppSettings (), Path.Combine (_directory, "missing-owner.json")));
		Assert.That (error!.Message, Does.Contain ("No saved " + provider + " account"));
		}

	/// <summary>Owner accounts stored by the library remain usable when the app has never saved an email.</summary>
	[Test]
	public void OwnerAccount_IsFoundInLibraryCacheWithoutCopyingTokens ()
		{
		Directory.CreateDirectory (_directory);
		string path = Path.Combine (_directory, "owner.json");
		const string content = "{\"saved@example.invalid\":{\"access_token\":\"protected-value\",\"protected\":true}}";
		File.WriteAllText (path, content);
		var options = CloudHistorySource.BuildOptions ("Owner", new AppSettings (), path);
		Assert.That (options.Email, Is.EqualTo ("saved@example.invalid"));
		Assert.That (options.AccessToken, Is.Null);
		Assert.That (options.RefreshToken, Is.Null);
		Assert.That (File.ReadAllText (path), Is.EqualTo (content));
		}

	/// <summary>Different saved Owner accounts are not selected arbitrarily.</summary>
	[Test]
	public void MultipleOwnerAccounts_RequireAnExplicitEmail ()
		{
		Directory.CreateDirectory (_directory);
		string path = Path.Combine (_directory, "owner.json");
		File.WriteAllText (path, "{\"one@example.invalid\":{\"access_token\":\"a\"},\"two@example.invalid\":{\"access_token\":\"b\"}}");
		Assert.That (CloudHistorySource.ResolveOwnerEmail (new AppSettings (), path), Is.Null);
		Assert.That (CloudHistorySource.ResolveOwnerEmail (new AppSettings { Email = "two@example.invalid" }, path), Is.EqualTo ("two@example.invalid"));
		}

	/// <summary>Actual LAN samples survive restart, preserve missing values and signs, and remain isolated by host.</summary>
	[Test]
	public async Task LocalReadings_PersistWithoutAnyCloudAccount ()
		{
		var sample = new LocalPowerSample (_now, null, -1250, 2400, 0);
		await new LocalPowerHistoryStore (_path).AppendAsync ("HOST.local", sample);
		var store = new LocalPowerHistoryStore (_path);
		var saved = await store.ReadAsync ("host.local", _now.AddMinutes (-1), _now.AddMinutes (1));
		Assert.That (saved, Is.EqualTo (new[] { sample }));
		Assert.That (await store.ReadAsync ("other.local", _now.AddMinutes (-1), _now.AddMinutes (1)), Is.Empty);
		Assert.That (await store.ReadAsync ("host.local", _now.AddMinutes (1), _now.AddMinutes (2)), Is.Empty);
		}

	/// <summary>A settled batch survives reopening the database and avoids even invoking cloud authentication.</summary>
	[Test]
	public async Task SettledHistory_SurvivesRestartWithoutCloudAccess ()
		{
		var first = await new EnergyHistoryCache (_path, () => _now).GetAsync (_request, Fetch, false, default);
		_now = _now.AddYears (1);
		var second = await new EnergyHistoryCache (_path, () => _now).GetAsync (_request,
			_ => throw new AssertionException ("A settled cache hit must not access the cloud."), false, default);
		Assert.That (_fetches, Is.EqualTo (1));
		Assert.That (second.FromCache, Is.True);
		Assert.That (second.Points, Is.EqualTo (first.Points));
		Assert.That (second.RetrievedAt, Is.EqualTo (first.RetrievedAt));
		Assert.That (second.Points.Single ().SolarKwh, Is.EqualTo (1.25));
		Assert.That (second.Points.Single ().HomeKwh, Is.EqualTo (1));
		Assert.That (second.Points.Single ().FromGridKwh, Is.EqualTo (0.5));
		Assert.That (second.Points.Single ().ToGridKwh, Is.EqualTo (0.25));
		Assert.That (second.Points.Single ().BatteryChargeKwh, Is.EqualTo (0.125));
		Assert.That (second.Points.Single ().BatteryDischargeKwh, Is.EqualTo (0.75));
		}

	/// <summary>Current data is reused briefly, refreshed after five minutes, then finalized after settlement.</summary>
	[Test]
	public async Task ProvisionalBatch_IsNotFrozenWhenItsDayEnds ()
		{
		_request = _request with { Start = _now.AddHours (-12), End = _now.AddHours (12) };
		var cache = new EnergyHistoryCache (_path, () => _now);
		await cache.GetAsync (_request, Fetch, false, default);
		_now = _now.AddMinutes (4);
		await cache.GetAsync (_request, Fetch, false, default);
		Assert.That (_fetches, Is.EqualTo (1));
		_now = _now.AddMinutes (2);
		await cache.GetAsync (_request, Fetch, false, default);
		Assert.That (_fetches, Is.EqualTo (2));
		_now = _now.AddDays (3);
		await cache.GetAsync (_request, Fetch, false, default);
		_now = _now.AddDays (1);
		await cache.GetAsync (_request, Fetch, false, default);
		Assert.That (_fetches, Is.EqualTo (3));
		}

	/// <summary>Provider, site, time zone, period, and time boundaries cannot share an unrelated cache entry.</summary>
	[TestCase ("account"), TestCase ("site"), TestCase ("timezone"), TestCase ("period"), TestCase ("range")]
	public async Task CacheKey_IsolatesHistory (string field)
		{
		var cache = new EnergyHistoryCache (_path, () => _now);
		await cache.GetAsync (_request, Fetch, false, default);
		var other = field switch
			{
			"account" => _request with { Account = "Fleet|test" },
			"site" => _request with { Site = "site-b" },
			"timezone" => _request with { Timezone = "America/Los_Angeles" },
			"period" => _request with { Period = "week" },
			_ => _request with { Start = _request.Start!.Value.AddDays (-1) }
			};
		await cache.GetAsync (other, Fetch, false, default);
		Assert.That (_fetches, Is.EqualTo (2));
		}

	/// <summary>Explicit refresh can correct a settled historical period.</summary>
	[Test]
	public async Task ForceRefresh_ReplacesSettledHistory ()
		{
		var cache = new EnergyHistoryCache (_path, () => _now);
		await cache.GetAsync (_request, Fetch, false, default);
		await cache.GetAsync (_request, Fetch, true, default);
		Assert.That (_fetches, Is.EqualTo (2));
		}

	/// <summary>A failed or empty refresh retains previous samples and explains their provenance.</summary>
	[TestCase (true), TestCase (false)]
	public async Task BadRefresh_DoesNotEraseGoodHistory (bool failure)
		{
		var cache = new EnergyHistoryCache (_path, () => _now);
		var first = await cache.GetAsync (_request, Fetch, false, default);
		var result = await cache.GetAsync (_request, _ => failure
			? Task.FromException<IReadOnlyList<EnergyHistoryPoint>> (new HttpRequestException ("offline"))
			: Task.FromResult<IReadOnlyList<EnergyHistoryPoint>> (Array.Empty<EnergyHistoryPoint> ()), true, default);
		Assert.That (result.Points, Is.EqualTo (first.Points));
		Assert.That (result.Warning, Is.Not.Null);
		Assert.That (result.FromCache, Is.True);
		}

	/// <summary>An empty past response remains provisional so delayed cloud samples can still arrive.</summary>
	[Test]
	public async Task EmptyPastResponse_IsRetriedAfterExpiry ()
		{
		var cache = new EnergyHistoryCache (_path, () => _now);
		await cache.GetAsync (_request, _ => Task.FromResult<IReadOnlyList<EnergyHistoryPoint>> (Array.Empty<EnergyHistoryPoint> ()), false, default);
		_now = _now.AddMinutes (6);
		var result = await cache.GetAsync (_request, Fetch, false, default);
		Assert.That (result.Points, Has.Count.EqualTo (1));
		Assert.That (_fetches, Is.EqualTo (1));
		}

	/// <summary>Cancellation does not replace a valid cached batch with partial results.</summary>
	[Test]
	public async Task Cancellation_PreservesCache ()
		{
		var cache = new EnergyHistoryCache (_path, () => _now);
		await cache.GetAsync (_request, Fetch, false, default);
		using var cancel = new CancellationTokenSource ();
		cancel.Cancel ();
		Assert.CatchAsync<OperationCanceledException> (async () => await cache.GetAsync (_request, Fetch, true, cancel.Token));
		var result = await cache.GetAsync (_request, Fetch, false, default);
		Assert.That (result.FromCache, Is.True);
		Assert.That (_fetches, Is.EqualTo (1));
		}
	}

/// <summary>Tests real Energy bindings and chart rendering in an isolated WPF host.</summary>
[TestFixture, Apartment (ApartmentState.STA)]
public sealed class EnergyPresentationTests
	{
	/// <summary>Hundreds of lifetime intervals become monthly totals without repeated labels or lost energy.</summary>
	[Test]
	public void Lifetime_AggregatesIntervalsAndKeepsMissingMonths ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local)
			{ SelectedPeriod = "lifetime" };
		var first = new DateTimeOffset (new DateTime (2026, 6, 1));
		var samples = Enumerable.Range (0, 600).Select (i => new StoredEnergyPoint (first.AddHours (i), 0.01, 0.02, 0.03, 0.01, 0.04, 0.02, 0.015)).ToList ();
		samples.Add (new StoredEnergyPoint (first.AddMonths (2), 0, 0, 0, 0, 0, 0, 0));
		model.ApplyHistory (samples);
		Assert.That (model.XAxes[0].Labels, Is.EqualTo (Enumerable.Range (0, 3).Select (i => first.AddMonths (i).ToString ("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture))));
		Assert.That (model.XAxes[0].LabelsRotation, Is.Zero);
		Assert.That (((ColumnSeries<double?>)model.Series.Single ()).Values, Is.EqualTo (new double?[] { 6, null, 0 }));
		model.Components[1].IsSelected = true;
		Assert.That (((ColumnSeries<double?>)model.Series.Single ()).Values, Is.EqualTo (new double?[] { 12, null, 0 }));
		model.Components[2].IsSelected = true;
		Assert.That (((ColumnSeries<double?>)model.Series.Single ()).Values, Is.EqualTo (new double?[] { 9, null, 0 }));
		model.Components[3].IsSelected = true;
		Assert.That (((ColumnSeries<double?>)model.Series.Single ()).Values, Is.EqualTo (new double?[] { 12, null, 0 }));
		Assert.That (model.YAxes[0].Name, Is.EqualTo ("kWh"));
		}

	/// <summary>Calendar dates are horizontal and a long lifetime range does not demand a label for every month.</summary>
	[TestCase ("month"), TestCase ("year"), TestCase ("lifetime")]
	public void LongPeriod_UsesReadableHorizontalLabels (string period)
		{
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local)
			{ SelectedPeriod = period };
		var now = DateTimeOffset.Now;
		model.ApplyHistory (new[] { new StoredEnergyPoint (now.AddYears (-10), 12, 1, 1, 0, 0, 0), new StoredEnergyPoint (now, 24, 2, 2, 0, 0, 0) });
		Assert.That (model.XAxes[0].LabelsRotation, Is.Zero);
		Assert.That (model.XAxes[0].ForceStepToMin, Is.False);
		if (period == "lifetime")
			{
			Assert.That (model.XAxes[0].Labels, Has.Count.EqualTo (121));
			Assert.That (model.XAxes[0].MinStep, Is.GreaterThanOrEqualTo (15));
			}
		var output = Path.Combine (TestContext.CurrentContext.WorkDirectory, "energy-" + period + "-labels.png");
		new SKCartesianChart { Width = 900, Height = 450, Series = model.Series, XAxes = model.XAxes, YAxes = model.YAxes }.SaveImage (output);
		TestContext.AddTestAttachment (output);
		}

	/// <summary>The local page explains cloud history setup and hides misleading default axes.</summary>
	[Test]
	public void LocalWithoutHistory_HidesEmptyChartAndShowsSetup ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local);
		model.LoadCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
		Assert.That (model.StatusMessage, Does.Contain ("Owner or Fleet"));
		Assert.That (model.PeriodLabel, Is.Not.Null.And.Not.Empty);
		var view = CreateView (model);
		Assert.That (((FrameworkElement)view.FindName ("HistoryChart")).Visibility, Is.EqualTo (Visibility.Collapsed));
		Assert.That (((FrameworkElement)view.FindName ("EmptyHistory")).Visibility, Is.EqualTo (Visibility.Visible));
		Assert.That (((FrameworkElement)view.FindName ("HistorySetup")).Visibility, Is.EqualTo (Visibility.Visible));
		Assert.That (connection.IsConnected, Is.False);
		}

	/// <summary>Cloud failure does not prevent LAN values, component selection, and a rendered graph.</summary>
	[Test]
	public void LiveGraph_WorksWithoutCloudAndKeepsMissingValuesMissing ()
		{
		string folder = Path.Combine (Path.GetTempPath (), "PowerwallLiveGraph", Guid.NewGuid ().ToString ("N"));
		try
			{
			using var connection = new PowerwallConnectionService ();
			connection.SetSiteLabel ("offline-test.local");
			using var model = new EnergyViewModel (connection, new EnergyHistoryCache (Path.Combine (folder, "history.sqlite")), () => PowerwallMode.Local)
				{ IsActive = true };
			var now = DateTimeOffset.Now;
			model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (0, -1100, 2400, null, 50, null, null), now).GetAwaiter ().GetResult ();
			model.StatusMessage = "Simulated cloud sign-in failure";
			Assert.That (model.HasChartData, Is.True);
			Assert.That (model.LiveReadingText, Does.Contain ("0.00 kW"));
			model.Components[2].IsSelected = true;
			var points = ((LineSeries<ObservablePoint>)model.Series.Single ()).Values!.ToArray ();
			Assert.That (points.Single ().Y, Is.EqualTo (-1.1));
			Assert.That (model.LiveReadingText, Does.Contain ("-1.10 kW"));
			model.Components[3].IsSelected = true;
			Assert.That (((LineSeries<ObservablePoint>)model.Series.Single ()).Values!.Single ().Y, Is.Null);
			Assert.That (model.LiveReadingText, Does.Contain ("Unavailable"));
			model.Components[1].IsSelected = true;
			var view = CreateView (model);
			Assert.That (((FrameworkElement)view.FindName ("HistoryChart")).Visibility, Is.EqualTo (Visibility.Visible));
			Assert.That (model.StatusMessage, Does.Contain ("cloud sign-in failure"));
			Assert.That (connection.IsConnected, Is.False, "The test must not establish any network connection.");
			}
		finally
			{
			if (Directory.Exists (folder)) Directory.Delete (folder, true);
			}
		}

	/// <summary>Successive samples append to the same rendered series without replacing axes or existing points.</summary>
	[Test]
	public void LiveGraph_AppendsWithoutRebuildingExistingSeries ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local)
			{ IsActive = true };
		var now = new DateTimeOffset (DateTime.Today).AddHours (12);
		model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (1000, null, 2000, 1000, null, null, null), now).GetAwaiter ().GetResult ();
		var seriesArray = model.Series;
		var series = (LineSeries<ObservablePoint>)model.Series.Single ();
		var values = series.Values!;
		var first = values.Single ();
		var axes = model.XAxes;
		model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (1500, null, 2000, 500, null, null, null), now.AddSeconds (5)).GetAwaiter ().GetResult ();
		Assert.That (model.Series, Is.SameAs (seriesArray));
		Assert.That (model.Series.Single (), Is.SameAs (series));
		Assert.That (series.Values, Is.SameAs (values));
		Assert.That (series.Values!.First (), Is.SameAs (first));
		Assert.That (model.XAxes, Is.SameAs (axes));
		Assert.That (series.Values!.Select (p => p.Y), Is.EqualTo (new double?[] { 1, 1.5 }));
		model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (2000, null, 2000, 0, null, null, null), now.AddMinutes (2)).GetAwaiter ().GetResult ();
		Assert.That (model.Series, Is.SameAs (seriesArray));
		Assert.That (series.Values!.Select (p => p.Y), Is.EqualTo (new double?[] { 1, 1.5, null, 2 }), "An interrupted data stream must not be bridged with invented measurements.");
		}

	/// <summary>Day uses actual timestamps, a full-day range, and no invented leading or trailing samples.</summary>
	[Test]
	public void DayGraph_UsesTimeCoordinatesAndRendersRealSamples ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Cloud);
		DateTimeOffset midnight = new (DateTime.Today);
		model.ApplyHistory (new[]
			{
			new StoredEnergyPoint (midnight.AddHours (6), 1, 0, 0, 0, 0, 0),
			new StoredEnergyPoint (midnight.AddHours (6).AddMinutes (5), 2, 0, 0, 0, 0, 0),
			new StoredEnergyPoint (midnight.AddHours (8), 1, 0, 0, 0, 0, 0)
			});
		Assert.That (model.XAxes[0].MinLimit, Is.Zero);
		Assert.That (model.XAxes[0].MaxLimit, Is.EqualTo ((new DateTimeOffset (DateTime.Today.AddDays (1)) - midnight).TotalMinutes));
		var series = (LineSeries<ObservablePoint>)model.Series.Single ();
		Assert.That (series.Values!.Select (v => v.X), Is.EqualTo (new double?[] { 360, 365, 480 }));
		Assert.That (model.YAxes[0].Name, Is.EqualTo ("kW"));
		var view = CreateView (model);
		var chart = (LiveChartsCore.SkiaSharpView.WPF.CartesianChart)view.FindName ("HistoryChart");
		Assert.That (chart.Visibility, Is.EqualTo (Visibility.Visible));
		Assert.That (chart.Series, Is.SameAs (model.Series));
		var output = Path.Combine (TestContext.CurrentContext.WorkDirectory, "energy-day-render.png");
		var rendered = new SKCartesianChart { Width = 900, Height = 450, Series = model.Series, XAxes = model.XAxes, YAxes = model.YAxes };
		rendered.SaveImage (output);
		using var bitmap = SkiaSharp.SKBitmap.Decode (output);
		Assert.That (bitmap.Pixels.Count (pixel => pixel.Red > 90 && pixel.Red < 155 && pixel.Green < 100 && pixel.Blue > 200),
			Is.GreaterThan (100), "The renderer must draw the purple cloud data series, not merely save an empty image.");
		TestContext.AddTestAttachment (output);
		connection.SetSiteLabel ("different-site");
		Assert.That (model.HasChartData, Is.False, "Old-site data must not remain under the new site's label.");
		}

	/// <summary>A loaded chart must measure the new period after being hidden while history loads.</summary>
	[Test]
	public void PeriodChange_RemeasuresVisibleWpfChart ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local);
		model.SelectedPeriod = "week";
		model.ApplyHistory (new[] { new StoredEnergyPoint (DateTimeOffset.Now, 1, 2, 3, 4, 5, 6) });
		var view = CreateView (model);
		var chart = (LiveChartsCore.SkiaSharpView.WPF.CartesianChart)view.FindName ("HistoryChart");
		var window = new Window { Content = view, Width = 1000, Height = 700, ShowActivated = false, ShowInTaskbar = false, Title = "Offline chart regression test", WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
		try
			{
			window.Show ();
			PumpChart ();
			Assert.That (chart.CoreChart.Series.Single (), Is.SameAs (model.Series.Single ()));
			foreach (var period in new[] { "month", "year", "lifetime", "day", "week" })
				{
				var before = ChartPixels (chart);
				model.SelectedPeriod = period;
				PumpChart ();
				model.ApplyHistory (new[] { new StoredEnergyPoint (DateTimeOffset.Now, 10, 20, 30, 40, 50, 60) });
				PumpChart ();
				Assert.That (chart.Series, Is.SameAs (model.Series), "The WPF binding must receive the new " + period + " series.");
				Assert.That (chart.CoreChart.Series.Single (), Is.SameAs (model.Series.Single ()), "The rendered chart must measure the new series.");
				var after = ChartPixels (chart);
				Assert.That (before.Zip (after).Count (pair => pair.First != pair.Second), Is.GreaterThan (3000), "The displayed pixels must change for " + period + ", not only the chart data model.");
				var labelGeometries = (System.Collections.Generic.IEnumerable<LiveChartsCore.Drawing.IDrawnElement>)typeof (LiveChartsCore.Painting.Paint)
					.GetMethod ("GetGeometries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
					.Invoke (model.XAxes[0].LabelsPaint, new object[] { chart.CoreChart.Canvas })!;
				var renderedLabels = labelGeometries.OfType<LiveChartsCore.Drawing.BaseLabelGeometry> ().ToArray ();
				Assert.That (renderedLabels.Length, Is.GreaterThan (2), "Check rendered labels, not only the axis configuration.");
				Assert.That (renderedLabels.Select (label => label.RotateTransform), Is.All.EqualTo (0), "Every rendered " + period + " date/time label must be horizontal after switching periods.");
				if (period == "month") Assert.That (model.XAxes[0].Labels!.Count, Is.EqualTo (DateTime.DaysInMonth (DateTime.Today.Year, DateTime.Today.Month)));
				if (period == "year") Assert.That (model.XAxes[0].Labels!.Count, Is.EqualTo (12));
				if (period == "day") Assert.That (model.XAxes[0].MaxLimit, Is.GreaterThan (1300));
				}
			}
		finally { window.Close (); }
		}

	/// <summary>Appending a LAN point changes actual pixels without replacing the series or its existing points.</summary>
	[Test]
	public void LiveAppend_RepaintsVisibleWpfChart ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local) { IsActive = true };
		var now = new DateTimeOffset (DateTime.Today).AddHours (12);
		model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (1000, null, null, null, null, null, null), now).GetAwaiter ().GetResult ();
		model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (1000, null, null, null, null, null, null), now.AddSeconds (5)).GetAwaiter ().GetResult ();
		var view = CreateView (model);
		var chart = (LiveChartsCore.SkiaSharpView.WPF.CartesianChart)view.FindName ("HistoryChart");
		var window = new Window { Content = view, Width = 1000, Height = 700, ShowActivated = false, ShowInTaskbar = false,
			Title = "Offline chart regression test", WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
		try
			{
			window.Show ();
			PumpChart ();
			var before = ChartPixels (chart);
			var series = model.Series;
			model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (5000, null, null, null, null, null, null), now.AddSeconds (10)).GetAwaiter ().GetResult ();
			PumpChart ();
			Assert.That (model.Series, Is.SameAs (series));
			Assert.That (before.Zip (ChartPixels (chart)).Count (pair => pair.First != pair.Second), Is.GreaterThan (100), "A received LAN reading must become visible without changing period or component.");
			}
		finally { window.Close (); }
		}

	private static byte[] ChartPixels (FrameworkElement chart)
		{
		var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap ((int)chart.ActualWidth, (int)chart.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
		bitmap.Render (chart);
		var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
		bitmap.CopyPixels (pixels, bitmap.PixelWidth * 4, 0);
		return pixels;
		}

	private static void PumpChart ()
		{
		var frame = new System.Windows.Threading.DispatcherFrame ();
		var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds (750) };
		timer.Tick += (_, _) => { timer.Stop (); frame.Continue = false; };
		timer.Start ();
		System.Windows.Threading.Dispatcher.PushFrame (frame);
		}

	private static EnergyView CreateView (EnergyViewModel model)
		{
		var resources = new ResourceDictionary { Source = new Uri ("/TeslaPowerwallApp;component/Themes/Theme.xaml", UriKind.Relative) };
		resources.Add ("BoolToVisibility", new BoolToVisibilityConverter ());
		resources.Add ("InverseBoolToVisibility", new InverseBoolToVisibilityConverter ());
		resources.Add ("InverseBool", new InverseBoolConverter ());
		resources.Add ("NullToCollapsed", new NullToCollapsedConverter ());
		var view = new EnergyView (resources) { DataContext = model, Width = 1000, Height = 700 };
		view.Measure (new Size (1000, 700));
		view.Arrange (new Rect (0, 0, 1000, 700));
		view.UpdateLayout ();
		return view;
		}
	}
