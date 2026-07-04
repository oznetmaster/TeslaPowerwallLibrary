// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

using LiveChartsCore.SkiaSharpView.WPF;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// Pre-warms the LiveChartsCore/SkiaSharp rendering pipeline used by the Energy screen's chart. The first
/// time a <see cref="CartesianChart"/> is constructed, the CLR must load and JIT the SkiaSharp, LiveChartsCore
/// and (transitively) OpenTK assemblies and load the native SkiaSharp library - a one-time cost that otherwise
/// runs synchronously on the UI thread exactly when the user first navigates to the Energy screen, making that
/// navigation appear to hang. Scheduling <see cref="ScheduleWarmUp"/> once, early, and at a low dispatcher
/// priority pays this cost during idle time (for example while the user is signing in) instead.
/// </summary>
public static class ChartWarmup
	{
	private static bool _scheduled;

	/// <summary>
	/// Schedules a one-time, best-effort warm-up of the chart rendering pipeline at
	/// <see cref="DispatcherPriority.ApplicationIdle"/>, so it only runs once there is no pending user
	/// interaction to service. Safe to call more than once; only the first call has any effect.
	/// </summary>
	public static void ScheduleWarmUp ()
		{
		if (_scheduled)
			return;

		_scheduled = true;
		Application.Current?.Dispatcher.BeginInvoke (DispatcherPriority.ApplicationIdle, new Action (WarmUp));
		}

	private static void WarmUp ()
		{
		var stopwatch = Stopwatch.StartNew ();
		try
			{
			// Never added to a visual tree or shown; construction alone forces the one-time assembly
			// load/JIT/native-library cost that would otherwise happen on first navigation to the Energy
			// screen.
			_ = new CartesianChart ();
			}
		catch (Exception exc)
			{
			// Best-effort optimization only; a failure here must never affect app behavior. The Energy
			// screen will simply pay the (one-time) cost itself on first navigation, as it did before.
			Debug.WriteLine ($"[Perf] ChartWarmup: warm-up failed after {stopwatch.ElapsedMilliseconds} ms: {exc.Message}");
			return;
			}

		Debug.WriteLine ($"[Perf] ChartWarmup: CartesianChart warmed up in {stopwatch.ElapsedMilliseconds} ms.");
		}
	}
