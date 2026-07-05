// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Base type for all view-models, providing change notification via CommunityToolkit.Mvvm and a shared
/// <see cref="IsBusy"/> flag used to reflect in-progress background work in the UI.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
	{
	/// <summary>Gets or sets a value indicating whether the view-model is performing background work.</summary>
	[ObservableProperty]
	private bool _isBusy;

	/// <summary>Gets or sets the most recent user-facing status or error message, when any.</summary>
	[ObservableProperty]
	private string? _statusMessage;

	/// <summary>
	/// Runs <paramref name="action"/> on the UI dispatcher thread, invoking it directly when already there.
	/// Used by view-models that subscribe to events raised from background threads (polling, connection
	/// callbacks) so bound properties are only ever mutated on the UI thread.
	/// </summary>
	/// <param name="action">The action to run on the UI thread.</param>
	protected static void RunOnUi (Action action)
		{
		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher is null || dispatcher.CheckAccess ())
			action ();
		else
			dispatcher.Invoke (action);
		}
	}

