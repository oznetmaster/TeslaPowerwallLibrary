// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Converters;
using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.App.ViewModels;
using TeslaPowerwallLibrary.App.Views;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Checks session opt-in and real Settings bindings without contacting hardware or writing settings.</summary>
[TestFixture, Apartment (ApartmentState.STA)]
public sealed class SettingsControlTests
	{
	/// <summary>Unlocking enables all three requested fields, preserves pending edits and sends no writes.</summary>
	[Test]
	public void SessionUnlock_EnablesActualControlsWithoutApplyingValues ()
		{
		var optionsSeen = new List<PowerwallOptions> ();
		using var connection = CreateConnection (optionsSeen);
		var options = Options ();
		Assert.That (connection.ConnectAsync (options).GetAwaiter ().GetResult (), Is.True);
		var model = new SettingsViewModel (connection) { ReservePercent = 15, SelectedMode = "autonomous", GridChargingEnabled = true };
		_ = Application.ResourceAssembly; // Initialize WPF pack resources when this fixture runs alone.
		var resources = new ResourceDictionary { Source = new Uri ("/TeslaPowerwallApp;component/Themes/Theme.xaml", UriKind.Relative) };
		resources.Add ("BoolToVisibility", new BoolToVisibilityConverter ());
		resources.Add ("InverseBool", new InverseBoolConverter ());
		resources.Add ("NullToCollapsed", new NullToCollapsedConverter ());
		var view = new SettingsView (resources) { DataContext = model, Width = 1000, Height = 700 };
		var window = new Window { Content = view, Width = 1000, Height = 700, ShowActivated = false, ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Title = "Offline Settings regression test" };
		try
			{
			window.Show ();
			Pump ();
			var reserve = (Slider)view.FindName ("ReserveInput");
			var mode = (ComboBox)view.FindName ("ModeInput");
			var charging = (CheckBox)view.FindName ("GridChargingInput");
			var enable = (Button)view.FindName ("EnableControlsButton");
			Assert.That (new[] { reserve.IsEnabled, mode.IsEnabled, charging.IsEnabled }, Is.All.False);
			Assert.That (enable.IsVisible && enable.IsEnabled, Is.True);
			Assert.That (((TextBox)view.FindName ("PollIntervalInput")).IsEnabled, Is.True, "Refresh preferences remain editable in read-only mode.");
			// Even directly invoked commands cannot bypass read-only mode.
			model.ApplyOperationCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			model.ApplyGridChargingCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			model.EnableLocalControlsCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			Pump ();
			Assert.That (new[] { reserve.IsEnabled, mode.IsEnabled, charging.IsEnabled }, Is.All.True);
			Assert.That (enable.IsVisible, Is.True);
			Assert.That (enable.Content, Is.EqualTo ("Return to read-only"));
			Assert.That (model.ReservePercent, Is.EqualTo (15));
			Assert.That (model.SelectedMode, Is.EqualTo ("autonomous"));
			Assert.That (model.GridChargingEnabled, Is.True);
			Assert.That (model.StatusMessage, Does.Contain ("No Powerwall settings have been changed"));
			Assert.That (reserve.Minimum, Is.Zero);
			Assert.That (reserve.Maximum, Is.EqualTo (100));
			Assert.That (reserve.IsSnapToTickEnabled && reserve.TickFrequency == 1, Is.True);
			foreach (int percent in new[] { 0, 37, 100 })
				{
				reserve.SetCurrentValue (Slider.ValueProperty, (double)percent);
				Pump ();
				Assert.That (model.ReservePercent, Is.EqualTo (percent));
				Assert.That (model.ReserveText, Is.EqualTo ($"{percent}%"));
				}
			model.ReservePercent = null;
			Pump ();
			Assert.That (model.ReservePercent, Is.Null, "A missing reading must not become zero through slider coercion.");
			Assert.That (model.ReserveText, Is.EqualTo ("Unavailable"));
			Assert.That (reserve.Visibility, Is.EqualTo (Visibility.Collapsed));
			model.ReservePercent = 42;
			Pump ();
			Assert.That (reserve.Value, Is.EqualTo (42));
			Assert.That (reserve.IsVisible, Is.True);
			Assert.That (optionsSeen, Has.Count.EqualTo (2));
			Assert.That (optionsSeen[1], Is.EqualTo (options with { AllowLocalControl = true }));
			model.EnableLocalControlsCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			Assert.That (optionsSeen, Has.Count.EqualTo (2), "Already enabled must not reauthenticate.");
			model.ToggleLocalControlsCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			Pump ();
			Assert.That (new[] { reserve.IsEnabled, mode.IsEnabled, charging.IsEnabled }, Is.All.False);
			Assert.That (enable.Content, Is.EqualTo ("Enable controls for this session"));
			Assert.That (connection.AllowsLocalControl, Is.False);
			Assert.That (optionsSeen[2].AllowLocalControl, Is.False);
			model.ApplyOperationCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			model.ApplyGridChargingCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			model.ToggleLocalControlsCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
			Pump ();
			Assert.That (new[] { reserve.IsEnabled, mode.IsEnabled, charging.IsEnabled }, Is.All.True);
			Assert.That (optionsSeen[3].AllowLocalControl, Is.True);
			}
		finally { window.Close (); }
		}

	/// <summary>Authentication rejection or timeout leaves the original read-only client available.</summary>
	[TestCase (false), TestCase (true)]
	public void FailedUnlock_PreservesReadOnlyConnection (bool timeout)
		{
		var optionsSeen = new List<PowerwallOptions> ();
		using var connection = CreateConnection (optionsSeen, timeout ? "timeout" : "reject");
		connection.ConnectAsync (Options ()).GetAwaiter ().GetResult ();
		var original = connection.Powerwall;
		var model = new SettingsViewModel (connection);
		model.EnableLocalControlsCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
		Assert.That (connection.Powerwall, Is.SameAs (original));
		Assert.That (connection.AllowsLocalControl, Is.False);
		Assert.That (model.CanChangeSettings, Is.False);
		Assert.That (model.CanEnableLocalControls, Is.True);
		Assert.That (model.IsBusy, Is.False);
		Assert.That (model.StatusMessage, Does.Contain ("not").IgnoreCase);
		}

	/// <summary>A rejected read-only replacement cannot be displayed as successfully locked.</summary>
	[TestCase (false), TestCase (true)]
	public void FailedRelock_ReportsThatControlsRemainEnabled (bool timeout)
		{
		var seen = new List<PowerwallOptions> ();
		using var connection = CreateConnection (seen, timeout ? "lock-timeout" : "lock-reject");
		connection.ConnectAsync (Options () with { AllowLocalControl = true }).GetAwaiter ().GetResult ();
		var original = connection.Powerwall;
		var model = new SettingsViewModel (connection);
		model.ToggleLocalControlsCommand.ExecuteAsync (null).GetAwaiter ().GetResult ();
		Assert.That (connection.Powerwall, Is.SameAs (original));
		Assert.That (model.CanChangeSettings, Is.True);
		Assert.That (model.LocalControlButtonText, Is.EqualTo ("Return to read-only"));
		Assert.That (model.StatusMessage, Does.Contain ("Controls remain enabled"));
		Assert.That (model.IsBusy, Is.False);
		}

	/// <summary>Disconnect removes session permission and a new default connection stays read-only.</summary>
	[Test]
	public void Disconnect_DiscardsSessionPermission ()
		{
		var optionsSeen = new List<PowerwallOptions> ();
		using var connection = CreateConnection (optionsSeen);
		connection.ConnectAsync (Options ()).GetAwaiter ().GetResult ();
		Assert.That (connection.EnableLocalControlsAsync ().GetAwaiter ().GetResult (), Is.True);
		connection.DisconnectAsync ().GetAwaiter ().GetResult ();
		Assert.That (connection.CanEnableLocalControls, Is.False);
		Assert.That (connection.EnableLocalControlsAsync ().GetAwaiter ().GetResult (), Is.False);
		connection.ConnectAsync (Options ()).GetAwaiter ().GetResult ();
		Assert.That (connection.AllowsLocalControl, Is.False);
		}

	/// <summary>Refresh changes preserve permission, update caching, persist only after success and support manual mode.</summary>
	[TestCase (0), TestCase (1), TestCase (60), TestCase (3600)]
	public async Task PollInterval_ChangesCacheAndCadenceWithoutEnablingControls (int seconds)
		{
		var seen = new List<PowerwallOptions> ();
		var saved = new List<int> ();
		using var connection = CreateConnection (seen);
		await connection.ConnectAsync (Options ());
		var model = new SettingsViewModel (connection, saved.Add) { LocalPollSecondsText = seconds.ToString () };
		await model.ApplyLocalPollIntervalCommand.ExecuteAsync (null);
		Assert.That (connection.LocalPollInterval.TotalSeconds, Is.EqualTo (seconds));
		Assert.That (seen[1].CacheExpireSeconds, Is.EqualTo (seconds));
		Assert.That (seen[1].AllowLocalControl, Is.False);
		Assert.That (saved, Is.EqualTo (new[] { seconds }));
		Assert.That (model.CanChangeSettings, Is.False);
		Assert.That (model.LocalPollingStatus, Does.Contain (seconds == 0 ? "manual" : seconds.ToString ()));
		await connection.StopPollingAsync ();
		}

	/// <summary>Manual mode can resume automatic reads without silently enabling hardware writes.</summary>
	[Test]
	public async Task PollInterval_ResumesFromManualMode ()
		{
		var seen = new List<PowerwallOptions> ();
		using var connection = CreateConnection (seen);
		await connection.ConnectAsync (Options ());
		await connection.SetLocalPollIntervalAsync (0);
		int attempts = 0;
		connection.PollFailed += (_, _) => attempts++;
		await connection.SetLocalPollIntervalAsync (1);
		Assert.That (attempts, Is.EqualTo (1), "The synthetic signed client rejects telemetry; an attempt proves polling restarted.");
		Assert.That (connection.AllowsLocalControl, Is.False);
		await connection.StopPollingAsync ();
		}

	/// <summary>Invalid input cannot reconnect, change the current interval or overwrite saved preferences.</summary>
	[TestCase ("-1"), TestCase ("3601"), TestCase ("1.5"), TestCase ("abc"), TestCase ("")]
	public async Task PollInterval_RejectsInvalidInput (string input)
		{
		var seen = new List<PowerwallOptions> ();
		using var connection = CreateConnection (seen);
		await connection.ConnectAsync (Options ());
		var model = new SettingsViewModel (connection, _ => throw new AssertionException ("Unexpected preference write")) { LocalPollSecondsText = input };
		await model.ApplyLocalPollIntervalCommand.ExecuteAsync (null);
		Assert.That (seen, Has.Count.EqualTo (1));
		Assert.That (connection.LocalPollInterval.TotalSeconds, Is.EqualTo (5));
		Assert.That (model.StatusMessage, Does.Contain ("whole number"));
		}

	/// <summary>A failed refresh reconnection retains the prior interval, client and persisted preference.</summary>
	[Test]
	public async Task PollInterval_FailedReconnectRetainsPreviousPreference ()
		{
		var seen = new List<PowerwallOptions> ();
		using var connection = CreateConnection (seen, "interval-reject");
		await connection.ConnectAsync (Options ());
		var original = connection.Powerwall;
		var model = new SettingsViewModel (connection, _ => throw new AssertionException ("Unexpected preference write")) { LocalPollSecondsText = "30" };
		await model.ApplyLocalPollIntervalCommand.ExecuteAsync (null);
		Assert.That (connection.Powerwall, Is.SameAs (original));
		Assert.That (connection.LocalPollInterval.TotalSeconds, Is.EqualTo (5));
		Assert.That (model.StatusMessage, Does.Contain ("previous interval"));
		}

	/// <summary>Firmware status names are retained without turning the response count into a fault count.</summary>
	[Test]
	public void Alerts_PreserveCodesAndReceiptTimeWithoutInventingSeverity ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SystemViewModel (connection);
		var received = new DateTimeOffset (2026, 10, 8, 12, 34, 0, TimeSpan.Zero);
		model.ApplyAlerts (new[] { "SystemConnectedToGrid", "BMS_a023_SW_Assertion", "SystemConnectedToGrid" }, received);
		Assert.That (model.Alerts, Is.EqualTo (new[] { "SystemConnectedToGrid", "BMS_a023_SW_Assertion" }));
		Assert.That (model.AlertsStatus, Does.Contain ("2 reported status and diagnostic codes").And.Contain (received.ToString ("g")));
		model.ApplyAlerts (Array.Empty<string> (), received);
		Assert.That (model.HasAlerts, Is.False);
		Assert.That (model.AlertsStatus, Does.Contain ("does not confirm system health"));
		}

	private static PowerwallOptions Options () => new ()
		{ Host = "192.0.2.1", Password = "synthetic", LocalProtocol = PowerwallLocalProtocol.TedapiSigned, CacheExpireSeconds = 17 };

	private static PowerwallConnectionService CreateConnection (List<PowerwallOptions> seen, string? failure = null) => new ((candidate, token) =>
		{
		var options = (PowerwallOptions)typeof (Powerwall).GetField ("_options", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue (candidate)!;
		seen.Add (options);
		if (failure == "interval-reject" && options.CacheExpireSeconds != 17) return Task.FromResult (false);
		if (!options.AllowLocalControl && failure == "lock-timeout") throw new OperationCanceledException ("Synthetic timeout");
		if (!options.AllowLocalControl && failure == "lock-reject") return Task.FromResult (false);
		if (options.AllowLocalControl && failure == "timeout") throw new OperationCanceledException ("Synthetic timeout");
		if (options.AllowLocalControl && failure == "reject") return Task.FromResult (false);
		typeof (Powerwall).GetField ("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (candidate, new ReadOnlyClient ());
		return Task.FromResult (true);
		});

	private static void Pump () => Dispatcher.CurrentDispatcher.Invoke (() => { }, DispatcherPriority.ApplicationIdle);

	/// <summary>Supplies synthetic device identity and rejects every attempted control write.</summary>
	private sealed class ReadOnlyClient () : PowerwallClientBase ("offline@example.test")
		{
		/// <inheritdoc/>
		public override Task AuthenticateAsync (CancellationToken cancellationToken = default) => Task.CompletedTask;
		/// <inheritdoc/>
		public override Task CloseSessionAsync (CancellationToken cancellationToken = default) => Task.CompletedTask;
		/// <inheritdoc/>
		public override Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) =>
			Task.FromResult<string?> ("{\"din\":\"offline-settings-test\"}");
		/// <inheritdoc/>
		public override Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default) =>
			throw new AssertionException ("Unexpected device write: " + api);
		/// <inheritdoc/>
		public override Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) => throw new AssertionException ("Unexpected binary read");
		/// <inheritdoc/>
		public override Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default) => throw new AssertionException ("Unexpected vitals read");
		/// <inheritdoc/>
		public override Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default) => Task.FromResult<double?> (null);
		}
	}
