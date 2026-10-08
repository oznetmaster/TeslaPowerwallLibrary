// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.App.ViewModels;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Verifies exclusive LAN/cloud drawing through the actual chart model without accounts or hardware.</summary>
[TestFixture, Apartment (ApartmentState.STA)]
public sealed class EnergySourceSelectionTests
	{
	private static DateTimeOffset Start => new (new DateTime (2026, 10, 8, 0, 0, 0));

	/// <summary>Dense LAN readings suppress coincident cloud values, including a measured LAN zero.</summary>
	[Test]
	public void LanCoverage_SuppressesCloudAndKeepsNormalArrivalsIncremental ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = Model (connection);
		model.ApplyHistory (Cloud (0, 5, 10, 15));
		Sample (model, 4.9, 1000);
		var series = model.Series;
		var axes = model.XAxes;
		var cloud = CloudLine (model).Values!;
		var originalCloudPoint = cloud.First ();
		var lan = LanLine (model).Values!;
		Sample (model, 5, 0);
		Sample (model, 5.1, 1200);
		Assert.That (model.Series, Is.SameAs (series));
		Assert.That (model.XAxes, Is.SameAs (axes));
		Assert.That (CloudLine (model).Values, Is.SameAs (cloud));
		Assert.That (cloud.First (), Is.SameAs (originalCloudPoint));
		Assert.That (LanLine (model).Values, Is.SameAs (lan));
		Assert.That (cloud.Single (p => p.X == 5).Y, Is.Null);
		Assert.That (lan.Single (p => p.X == 5).Y, Is.Zero);
		Assert.That (cloud.Where (p => p.X == 0 || p.X == 10 || p.X == 15).All (p => p.Y.HasValue), Is.True);
		AssertNoOverlap (model);
		}

	/// <summary>A short LAN run between cloud timestamps must also break cloud interpolation.</summary>
	[Test]
	public void BetweenCloudSamples_LanRunBreaksCloudLine ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = Model (connection);
		model.ApplyHistory (Cloud (0, 5, 10));
		Sample (model, 2, 1000);
		Sample (model, 2.1, 1200);
		Assert.That (CloudLine (model).Values!.Any (p => p.X > 0 && p.X < 5 && p.Y is null), Is.True);
		AssertNoOverlap (model);
		}

	/// <summary>Missing component values and real polling gaps retain cloud backfill, without bridging LAN gaps.</summary>
	[Test]
	public void MissingMeasurementsAndOutages_KeepCloudHistory ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = Model (connection);
		model.ApplyHistory (Cloud (0, 5, 10, 15));
		Sample (model, 0, 1000);
		Sample (model, 0.1, 1200);
		Sample (model, 5, null);
		Sample (model, 10, 1500);
		Sample (model, 10.1, 1700);
		Assert.That (CloudLine (model).Values!.Single (p => p.X == 5).Y, Is.Not.Null);
		Assert.That (LanLine (model).Values!.Any (p => p.X == 10 && p.Y is null), Is.True);
		AssertNoOverlap (model);
		model.Components.Single (c => c.Name == "Grid").IsSelected = true;
		Assert.That (CloudLine (model).Values!.Count (p => p.Y.HasValue), Is.EqualTo (4), "Missing LAN Grid data must not be hidden just because Solar exists.");
		}

	/// <summary>Delayed and corrected samples rebuild coverage in place, and later history loads cannot restore an overlap.</summary>
	[Test]
	public void CorrectionsAndCloudReload_ReevaluateCoverage ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = Model (connection);
		Sample (model, 4.9, 1000);
		Sample (model, 5.1, 1200);
		model.ApplyHistory (Cloud (0, 5, 10));
		Assert.That (CloudLine (model).Values!.Single (p => p.X == 5).Y, Is.Null);
		var series = model.Series;
		Sample (model, 5, null); // An actual missing-value response splits the formerly continuous run.
		Assert.That (model.Series, Is.SameAs (series));
		Assert.That (CloudLine (model).Values!.Single (p => p.X == 5).Y, Is.Not.Null);
		Sample (model, 5, 0); // Correct the same timestamp, without leaving the earlier null slot behind.
		Assert.That (LanLine (model).Values!.Count (p => p.X == 5), Is.EqualTo (1));
		Assert.That (CloudLine (model).Values!.Single (p => p.X == 5).Y, Is.Null);
		model.ApplyHistory (Cloud (0, 5, 10, 15));
		Assert.That (CloudLine (model).Values!.Single (p => p.X == 5).Y, Is.Null);
		AssertNoOverlap (model);
		}

	/// <summary>Only continuous readings cover an interval; the configured cadence controls gap detection.</summary>
	/// <param name="pollSeconds">Configured polling cadence.</param>
	/// <param name="suppressed">Whether the interval is sufficiently continuous to suppress its cloud sample.</param>
	[TestCase (5, false), TestCase (60, true)]
	public void Coverage_RespectsPollingCadence (int pollSeconds, bool suppressed)
		{
		using var connection = new PowerwallConnectionService { LocalPollInterval = TimeSpan.FromSeconds (pollSeconds) };
		using var model = Model (connection);
		model.ApplyHistory (Cloud (0, 5, 10));
		Sample (model, 4, 1000);
		Sample (model, 6, 1200);
		Assert.That (CloudLine (model).Values!.Single (p => p.X == 5).Y is null, Is.EqualTo (suppressed));
		AssertNoOverlap (model);
		}

	/// <summary>Every accumulated-energy period uses bars with existing units, totals and missing intervals.</summary>
	/// <param name="period">Energy period to plot.</param>
	[TestCase ("week"), TestCase ("month"), TestCase ("year"), TestCase ("lifetime")]
	public void EnergyPeriods_UseBarsAndDayUsesLines (string period)
		{
		using var connection = new PowerwallConnectionService ();
		using var model = Model (connection);
		model.SelectedPeriod = period;
		model.ApplyHistory (Cloud (0, 5));
		Assert.That (model.Series.Single (), Is.InstanceOf<ColumnSeries<double?>> ());
		Assert.That (((ColumnSeries<double?>)model.Series.Single ()).Values!.Where (v => v.HasValue).Sum (), Is.EqualTo (0.2));
		Assert.That (model.YAxes[0].Name, Is.EqualTo ("kWh"));
		Assert.That (model.YAxes[0].MinLimit, Is.Zero, "Energy bars must start at zero rather than a cropped auto-scaled baseline.");
		model.SelectedPeriod = "day";
		model.ApplyHistory (Cloud (0, 5));
		Assert.That (model.Series.Single (), Is.InstanceOf<LineSeries<ObservablePoint>> ());
		Assert.That (model.YAxes[0].Name, Is.EqualTo ("kW"));
		}

	/// <summary>An entirely exported-energy period still includes zero as the top of its negative bars.</summary>
	[Test]
	public void ExportBars_IncludeZeroBaseline ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = Model (connection);
		model.SelectedPeriod = "month";
		model.Components.Single (c => c.Name == "Grid").IsSelected = true;
		model.ApplyHistory (new[] { new StoredEnergyPoint (Start, 0, 0, 0, 5, 0, 0) });
		Assert.That (model.YAxes[0].MaxLimit, Is.Zero);
		Assert.That (model.YAxes[0].MinLimit, Is.Null);
		Assert.That (((ColumnSeries<double?>)model.Series.Single ()).Values!.Where (v => v.HasValue).Single (), Is.EqualTo (-5));
		}

	/// <summary>Renders the mixed-source day and accumulated-energy bars for visual regression inspection.</summary>
	[Test]
	public void SourceSelectionAndBars_RenderBothViews ()
		{
		using var connection = new PowerwallConnectionService ();
		using var model = Model (connection);
		model.ApplyHistory (Enumerable.Range (0, 37).Select (i => new StoredEnergyPoint (Start.AddHours (6).AddMinutes (i * 5), 0.08 + i * 0.002, 0.2, 0.3, 0, 0.1, 0, 0.1)).ToArray ());
		for (int seconds = 0; seconds <= 3600; seconds += 5)
			Sample (model, 420 + seconds / 60.0, 1500 + 400 * Math.Sin (seconds / 180.0));
		AssertNoOverlap (model);
		model.XAxes[0].MinLimit = 350;
		model.XAxes[0].MaxLimit = 550;
		string day = System.IO.Path.Combine (TestContext.CurrentContext.WorkDirectory, "energy-lan-preferred.png");
		new LiveChartsCore.SkiaSharpView.SKCharts.SKCartesianChart { Width = 1000, Height = 450, Series = model.Series, XAxes = model.XAxes, YAxes = model.YAxes }.SaveImage (day);
		using (var bitmap = SkiaSharp.SKBitmap.Decode (day))
			{
			Assert.That (bitmap.Pixels.Count (p => p.Red > 90 && p.Red < 155 && p.Green < 100 && p.Blue > 200), Is.GreaterThan (100));
			Assert.That (bitmap.Pixels.Count (p => p.Red > 220 && p.Green > 140 && p.Green < 210 && p.Blue < 60), Is.GreaterThan (100));
			}
		TestContext.AddTestAttachment (day);
		model.SelectedPeriod = "month";
		model.ApplyHistory (Enumerable.Range (0, 8).Select (i => new StoredEnergyPoint (Start.AddDays (-7 + i), 8 + i, 0, 0, 0, 0, 0)).ToArray ());
		string month = System.IO.Path.Combine (TestContext.CurrentContext.WorkDirectory, "energy-month-bars.png");
		new LiveChartsCore.SkiaSharpView.SKCharts.SKCartesianChart { Width = 1000, Height = 450, Series = model.Series, XAxes = model.XAxes, YAxes = model.YAxes }.SaveImage (month);
		using (var bitmap = SkiaSharp.SKBitmap.Decode (month))
			Assert.That (bitmap.Pixels.Count (p => p.Red > 220 && p.Green > 140 && p.Green < 210 && p.Blue < 60), Is.GreaterThan (5000), "Bars must render filled areas, rather than only a line or empty axes.");
		TestContext.AddTestAttachment (month);
		}

	private static EnergyViewModel Model (PowerwallConnectionService connection) => new (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => Start.AddHours (12)) { IsActive = true };
	private static StoredEnergyPoint[] Cloud (params double[] minutes) => minutes.Select (minute => new StoredEnergyPoint (Start.AddMinutes (minute), 0.1, 0.2, 0.3, 0, 0.1, 0, 0.1)).ToArray ();
	private static void Sample (EnergyViewModel model, double minutes, double? solar) => model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (solar, null, 2000, null, null, null, null), Start.AddMinutes (minutes)).GetAwaiter ().GetResult ();
	private static LineSeries<ObservablePoint> CloudLine (EnergyViewModel model) => model.Series.OfType<LineSeries<ObservablePoint>> ().Single (s => s.Name!.Contains ("cloud"));
	private static LineSeries<ObservablePoint> LanLine (EnergyViewModel model) => model.Series.OfType<LineSeries<ObservablePoint>> ().Single (s => s.Name!.Contains ("LAN"));

	private static void AssertNoOverlap (EnergyViewModel model)
		{
		var cloud = CloudLine (model).Values!.ToArray ();
		var lan = LanLine (model).Values!.ToArray ();
		foreach (var point in lan.Where (p => p.Y.HasValue))
			Assert.That (cloud.Any (p => p.Y.HasValue && p.X == point.X), Is.False, "Two sources occupy the same timestamp.");
		for (int c = 1; c < cloud.Length; c++)
			{
			if (cloud[c - 1].Y is null || cloud[c].Y is null) continue;
			foreach (var point in lan.Where (p => p.Y.HasValue))
				Assert.That (point.X > cloud[c - 1].X && point.X < cloud[c].X, Is.False, "Cloud interpolation crosses a reported LAN point.");
			for (int l = 1; l < lan.Length; l++)
				if (lan[l - 1].Y.HasValue && lan[l].Y.HasValue)
					Assert.That (Math.Max (cloud[c - 1].X!.Value, lan[l - 1].X!.Value), Is.GreaterThanOrEqualTo (Math.Min (cloud[c].X!.Value, lan[l].X!.Value)), "Cloud and LAN segments overlap.");
			}
		}
	}
