// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.App.ViewModels;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Checks calendar rollover without waiting for midnight or contacting a device.</summary>
[TestFixture, Apartment (ApartmentState.STA)]
public sealed class EnergyCalendarTests
	{
	/// <summary>New-day samples replace the previous day's view once, then append to the same new-day series.</summary>
	[Test]
	public void Midnight_AdvancesTodayAndKeepsSubsequentUpdatesIncremental ()
		{
		var now = At (2026, 10, 7, 23, 59, 55);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now)
			{ IsActive = true };
		model.RecordLocalSnapshotAsync (Sample (1000), now).GetAwaiter ().GetResult ();
		now = At (2026, 10, 8, 0, 0, 5);
		model.RecordLocalSnapshotAsync (Sample (2000), now).GetAwaiter ().GetResult ();
		Assert.That (model.PeriodLabel, Is.EqualTo (now.ToString ("MMMM d, yyyy", CultureInfo.CurrentCulture)));
		var series = model.Series;
		var values = ((LineSeries<ObservablePoint>)series.Single ()).Values!;
		Assert.That (values.Select (point => point.Y), Is.EqualTo (new double?[] { 2 }));
		Assert.That (values.Single ().X, Is.EqualTo (5.0 / 60).Within (0.000001));
		Assert.That (model.NextPeriodCommand.CanExecute (null), Is.False);
		now = now.AddSeconds (5);
		model.RecordLocalSnapshotAsync (Sample (3000), now).GetAwaiter ().GetResult ();
		Assert.That (model.Series, Is.SameAs (series));
		Assert.That (values.Select (point => point.Y), Is.EqualTo (new double?[] { 2, 3 }));
		}

	/// <summary>The final fractional second belongs to its day; midnight belongs only to the next day.</summary>
	[Test]
	public void DayBoundary_RetainsFractionalLastSecond ()
		{
		var now = At (2026, 10, 7, 23, 59, 59).AddMilliseconds (500);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now)
			{ IsActive = true };
		model.RecordLocalSnapshotAsync (Sample (1000), now).GetAwaiter ().GetResult ();
		Assert.That (((LineSeries<ObservablePoint>)model.Series.Single ()).Values!.Single ().Y, Is.EqualTo (1));
		now = now.AddMilliseconds (500);
		model.RecordLocalSnapshotAsync (Sample (2000), now).GetAwaiter ().GetResult ();
		var values = ((LineSeries<ObservablePoint>)model.Series.Single ()).Values!.ToArray ();
		Assert.That (values, Has.Length.EqualTo (1));
		Assert.That (values[0].X, Is.Zero);
		Assert.That (values[0].Y, Is.EqualTo (2));
		}

	/// <summary>A calendar notification rolls over with polling disabled and ignores repeated notifications.</summary>
	[Test]
	public void CalendarNotification_AdvancesWithoutAnyLiveReading ()
		{
		var now = At (2026, 10, 7, 23, 59, 55);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now);
		now = now.AddDays (3); // Includes a suspended app resuming days later.
		Assert.That (model.AdvanceCurrentPeriod (now), Is.True);
		Assert.That (model.PeriodLabel, Is.EqualTo (now.ToString ("MMMM d, yyyy", CultureInfo.CurrentCulture)));
		Assert.That (model.AdvanceCurrentPeriod (now), Is.False);
		model.Dispose ();
		Assert.That (model.AdvanceCurrentPeriod (now.AddDays (1)), Is.False);
		}

	/// <summary>Browsing yesterday remains intentional across midnight; returning to today restores following.</summary>
	[Test]
	public void HistoricalSelection_IsPreservedUntilUserReturnsToToday ()
		{
		var now = At (2026, 10, 7, 23, 59, 55);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now);
		model.PreviousPeriodCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
		string historicalLabel = model.PeriodLabel;
		now = At (2026, 10, 8, 0, 0, 5);
		Assert.That (model.AdvanceCurrentPeriod (now), Is.False);
		Assert.That (model.PeriodLabel, Is.EqualTo (historicalLabel));
		model.NextPeriodCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
		model.NextPeriodCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
		now = now.AddDays (1);
		Assert.That (model.AdvanceCurrentPeriod (now), Is.True);
		}

	/// <summary>Month and year boundaries follow the same calendar rule; lifetime never rolls over.</summary>
	[TestCase ("month", true), TestCase ("year", true), TestCase ("lifetime", false)]
	public void CalendarPeriod_FollowsItsOwnBoundary (string period, bool changed)
		{
		var now = At (2026, 12, 31, 23, 59, 55);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now)
			{ SelectedPeriod = period };
		now = At (2027, 1, 1, 0, 0, 5);
		Assert.That (model.AdvanceCurrentPeriod (now), Is.EqualTo (changed));
		}

	/// <summary>The plot and numeric display both show zero for readings below their 0.01 kW precision.</summary>
	[Test]
	public void OvernightSolar_MatchesDisplayedPrecisionAndPlotsZeroAtZero ()
		{
		var now = At (2026, 10, 8, 1, 0, 0);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now)
			{ IsActive = true };
		model.RecordLocalSnapshotAsync (Sample (0), now).GetAwaiter ().GetResult ();
		model.RecordLocalSnapshotAsync (Sample (1), now.AddSeconds (5)).GetAwaiter ().GetResult ();
		var series = (LineSeries<ObservablePoint>)model.Series.Single ();
		Assert.That (series.Values!.Select (p => p.Y), Is.EqualTo (new double?[] { 0, 0 }));
		Assert.That (model.YAxes[0].MinLimit, Is.Zero);
		Assert.That (model.YAxes[0].MaxLimit, Is.EqualTo (1));
		Assert.That (series.GeometrySize, Is.Zero);
		Assert.That (model.LiveReadingText, Does.Contain ("0.00 kW"));
		model.RecordLocalSnapshotAsync (Sample (5100), now.AddSeconds (10)).GetAwaiter ().GetResult ();
		Assert.That (model.Series.Single (), Is.SameAs (series));
		Assert.That (model.YAxes[0].MaxLimit, Is.EqualTo (5.5));
		model.RecordLocalSnapshotAsync (Sample (-200), now.AddSeconds (15)).GetAwaiter ().GetResult ();
		Assert.That (model.YAxes[0].MinLimit, Is.EqualTo (-0.5));
		}

	/// <summary>Cloud history and live readings remain visually distinct for every selected component.</summary>
	[TestCase ("Solar"), TestCase ("Home"), TestCase ("Powerwall"), TestCase ("Grid")]
	public void CloudAndLanSeries_HaveDifferentColours (string component)
		{
		var now = At (2026, 10, 8, 1, 0, 0);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now)
			{ IsActive = true };
		model.Components.Single (c => c.Name == component).IsSelected = true;
		model.ApplyHistory (new[] { new StoredEnergyPoint (now.AddMinutes (-5), 1, 1, 1, 1, 1, 1) });
		model.RecordLocalSnapshotAsync (Sample (1000), now).GetAwaiter ().GetResult ();
		var series = model.Series.Cast<LineSeries<ObservablePoint>> ().ToArray ();
		Assert.That (series, Has.Length.EqualTo (2));
		var cloud = (LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint)series[0].Stroke!;
		var lan = (LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint)series[1].Stroke!;
		Assert.That (cloud.Color, Is.Not.EqualTo (lan.Color));
		Assert.That (series[0].Name, Does.Contain ("cloud average"));
		Assert.That (series[1].Name, Does.Contain ("LAN"));
		}

	/// <summary>Weekly ranges include Monday through Sunday and never manufacture measurements for empty slots.</summary>
	[Test]
	public void Week_AlignsWithCloudCalendarAndLeavesMissingSlotsEmpty ()
		{
		var now = At (2026, 10, 11, 3, 0, 0); // Sunday belongs to the week starting Monday 5th.
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now)
			{ SelectedPeriod = "week" };
		model.Components.Single (c => c.Name == "Home").IsSelected = true;
		model.ApplyHistory (new[]
			{
			new StoredEnergyPoint (At (2026, 10, 5, 0, 0, 0), 0, 0, 0, 0, 0, 0),
			new StoredEnergyPoint (now, 1, 12, 1, 1, 1, 1)
			});
		var values = ((ColumnSeries<double?>)model.Series.Single ()).Values!.ToArray ();
		Assert.That (values, Has.Length.EqualTo (28));
		Assert.That (values[0], Is.Zero, "A reported zero remains zero.");
		Assert.That (values[1], Is.Null, "No sample means unavailable, not zero.");
		Assert.That (values[24], Is.EqualTo (12), "Sunday's actual consumption belongs at the end of this week.");
		Assert.That (values[25], Is.Null, "Future slots must not show zero usage.");
		Assert.That (model.XAxes[0].Labels![0], Does.StartWith (At (2026, 10, 5, 0, 0, 0).ToString ("ddd", CultureInfo.CurrentCulture)));
		}

	/// <summary>Changing an empty period clears the previous day's limits and power units.</summary>
	[Test]
	public void EmptyPeriod_ClearsPreviousScaleAndSamples ()
		{
		var now = At (2026, 10, 8, 1, 0, 0);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now) { IsActive = true };
		model.RecordLocalSnapshotAsync (Sample (1000), now).GetAwaiter ().GetResult ();
		Assert.That (model.HasChartData, Is.True);
		model.SelectedPeriod = "month";
		Assert.That (model.HasChartData, Is.False);
		Assert.That (model.YAxes[0].Name, Is.EqualTo ("kWh"));
		Assert.That (model.XAxes[0].MaxLimit, Is.Null);
		Assert.That (model.YAxes[0].MaxLimit, Is.Null);
		}

	/// <summary>Day retains signed battery flow and makes its direction explicit on the axis.</summary>
	[Test]
	public void PowerwallDay_LabelsChargingAndDischargingWithSignedTicks ()
		{
		var now = At (2026, 10, 8, 12, 0, 0);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now) { IsActive = true };
		model.Components.Single (c => c.Name == "Powerwall").IsSelected = true;
		model.ApplyHistory (new[] {
			new StoredEnergyPoint (now.AddMinutes (-10), 0, 0, 0, 0, 0.5, 0, 0),
			new StoredEnergyPoint (now.AddMinutes (-5), 0, 0, 0, 0, 0, 0.25, 0.1) });
		model.RecordLocalSnapshotAsync (new PowerFlowSnapshot (null, -1000, null, null, null, null, null), now).GetAwaiter ().GetResult ();
		Assert.That (model.YAxes[0].Name, Does.Contain ("+ discharge").And.Contain ("- charge"));
		Assert.That (model.YAxes[0].Labeler (-2), Does.StartWith ("-"));
		Assert.That (model.YAxes[0].Labeler (2), Does.StartWith ("+"));
		Assert.That (model.YAxes[0].Labeler (0), Does.Not.Contain ("+").And.Not.Contain ("-"));
		Assert.That (((LineSeries<ObservablePoint>)model.Series[0]).Values!.Select (p => p.Y), Is.EqualTo (new double?[] { -6, 3 }));
		Assert.That (model.YAxes[0].MinLimit, Is.LessThanOrEqualTo (-6));
		Assert.That (model.YAxes[0].MaxLimit, Is.GreaterThanOrEqualTo (3));
		}

	/// <summary>Longer periods use the dedicated home contribution instead of total discharge or net battery energy.</summary>
	[TestCase ("week"), TestCase ("month"), TestCase ("year"), TestCase ("lifetime")]
	public void PowerwallHistory_PlotsOnlyReportedHomeContribution (string period)
		{
		var now = At (2026, 10, 8, 12, 0, 0);
		using var connection = new PowerwallConnectionService ();
		using var model = new EnergyViewModel (connection, new EnergyHistoryCache ("unused.sqlite"), () => PowerwallMode.Local, () => now) { SelectedPeriod = period };
		model.Components.Single (c => c.Name == "Powerwall").IsSelected = true;
		model.ApplyHistory (new[] { new StoredEnergyPoint (now, 0, 10, 0, 3, 20, 8, 5) });
		var series = (ColumnSeries<double?>)model.Series.Single ();
		Assert.That (series.Name, Is.EqualTo ("Powerwall to home"));
		Assert.That (series.Values!.Where (v => v.HasValue), Is.EqualTo (new double?[] { 5 }));
		model.ApplyHistory (new[] { new StoredEnergyPoint (now, 0, 10, 0, 3, 20, 8) });
		Assert.That (((ColumnSeries<double?>)model.Series.Single ()).Values, Is.All.Null, "Total battery discharge cannot substitute for unreported home contribution.");
		}

	/// <summary>The shell title tracks site names and clears the old name when the connection is reset.</summary>
	[Test]
	public void WindowTitle_TracksSiteNameWithoutChangingHost ()
		{
		using var connection = new PowerwallConnectionService ();
		using var shell = new ShellViewModel (connection);
		connection.SetSiteLabel ("offline-test.local");
		connection.SetSiteName ("Portincaple");
		Assert.That (shell.WindowTitle, Is.EqualTo ("Tesla™ Powerwall™ — Portincaple"));
		Assert.That (shell.SiteName, Is.EqualTo ("Portincaple"));
		Assert.That (connection.SiteLabel, Is.EqualTo ("offline-test.local"));
		connection.SetSiteName ("Renamed site");
		Assert.That (shell.WindowTitle, Does.EndWith ("Renamed site"));
		connection.SetSiteLabel (null);
		Assert.That (shell.WindowTitle, Is.EqualTo ("Tesla™ Powerwall™"));
		}

	private static PowerFlowSnapshot Sample (double solar) => new (solar, null, null, null, null, null, null);

	private static DateTimeOffset At (int year, int month, int day, int hour, int minute, int second)
		{
		var local = new DateTime (year, month, day, hour, minute, second, DateTimeKind.Unspecified);
		return new DateTimeOffset (local, TimeZoneInfo.Local.GetUtcOffset (local));
		}
	}
