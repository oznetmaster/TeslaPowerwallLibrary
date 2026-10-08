// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace TeslaPowerwallLibrary.App.Views;

/// <summary>Interaction logic for <c>EnergyView.xaml</c>.</summary>
public partial class EnergyView : UserControl
	{
	/// <summary>Initializes a new instance of the <see cref="EnergyView"/> class.</summary>
	public EnergyView () : this (null)
		{
		}

	/// <summary>Creates the actual view with optional host resources for isolated WPF tests.</summary>
	/// <param name="resources">Host theme and converters, or null to inherit application resources.</param>
	internal EnergyView (ResourceDictionary? resources)
		{
		if (resources is not null)
			Resources = resources;
		// Diagnostic timing: on an un-warmed-up process, constructing the embedded lvc:CartesianChart forces
		// a one-time SkiaSharp/LiveChartsCore/OpenTK assembly load, native-library load, and JIT. See
		// ChartWarmup, which pays this cost ahead of time during idle startup.
		var stopwatch = Stopwatch.StartNew ();
		InitializeComponent ();
		Debug.WriteLine ($"[Perf] EnergyView: constructed in {stopwatch.ElapsedMilliseconds} ms.");
		}
	private void OnChartDataChanged (object sender, System.Windows.Data.DataTransferEventArgs e)
		{
		if (e.Property != LiveChartsCore.SkiaSharpView.WPF.CartesianChart.SeriesProperty) return;
		// Series replacement can complete while the empty-state binding hides the canvas.
		// Measure and invalidate once after bindings/layout settle; ordinary LAN appends
		// retain their observable collection and do not take this path.
		Dispatcher.BeginInvoke (System.Windows.Threading.DispatcherPriority.ContextIdle, new System.Action (() =>
			{
			if (!HistoryChart.IsLoaded || !HistoryChart.IsVisible) return;
			HistoryChart.CoreChart.Update (new LiveChartsCore.Kernel.ChartUpdateParams { IsAutomaticUpdate = false, Throttling = false });
			HistoryChart.CoreCanvas.Invalidate ();
			}));
		}

	}
