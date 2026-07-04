// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Windows;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.App.ViewModels;

namespace TeslaPowerwallLibrary.App;

/// <summary>Interaction logic for <c>MainWindow.xaml</c>, the application shell hosting navigation and screens.</summary>
public partial class MainWindow : Window
	{
	/// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
	public MainWindow ()
		{
		InitializeComponent ();
		Loaded += OnLoaded;
		Closed += OnClosed;

		// Pays the Energy screen's one-time chart-rendering warm-up cost during idle time (for example while
		// the user is signing in) instead of blocking the UI thread the first time they navigate to Energy.
		ChartWarmup.ScheduleWarmUp ();
		}

	private async void OnLoaded (object sender, RoutedEventArgs e)
		{
		if (DataContext is ShellViewModel shell)
			await shell.InitializeAsync ();
		}

	private void OnClosed (object? sender, System.EventArgs e)
		{
		if (DataContext is ShellViewModel shell)
			shell.Dispose ();
		}
	}
