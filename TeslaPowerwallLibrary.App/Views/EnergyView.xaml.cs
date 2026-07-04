// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Windows.Controls;

namespace TeslaPowerwallLibrary.App.Views;

/// <summary>Interaction logic for <c>EnergyView.xaml</c>.</summary>
public partial class EnergyView : UserControl
	{
	/// <summary>Initializes a new instance of the <see cref="EnergyView"/> class.</summary>
	public EnergyView ()
		{
		// Diagnostic timing: on an un-warmed-up process, constructing the embedded lvc:CartesianChart forces
		// a one-time SkiaSharp/LiveChartsCore/OpenTK assembly load, native-library load, and JIT. See
		// ChartWarmup, which pays this cost ahead of time during idle startup.
		var stopwatch = Stopwatch.StartNew ();
		InitializeComponent ();
		Debug.WriteLine ($"[Perf] EnergyView: constructed in {stopwatch.ElapsedMilliseconds} ms.");
		}
	}
