// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Windows;

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
