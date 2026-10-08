// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using TeslaPowerwallLibrary.Tedapi;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.App.ViewModels;

/// <summary>
/// Drives the System information screen: firmware version, DIN, uptime, per-battery blocks, and active alerts.
/// </summary>
public sealed partial class SystemViewModel : ViewModelBase
	{
	private readonly PowerwallConnectionService _connection;

	/// <summary>Initializes a new instance of the <see cref="SystemViewModel"/> class.</summary>
	/// <param name="connection">The shared connection service.</param>
	public SystemViewModel (PowerwallConnectionService connection)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		Batteries = new ObservableCollection<BatteryBlockView> ();
		Alerts = new ObservableCollection<string> ();
		}

	/// <summary>Gets whether rich local telemetry is available through the selected transport.</summary>
	public bool HasTedapi => _connection.Mode == PowerwallMode.Local && _connection.LocalProtocol != PowerwallLocalProtocol.Gateway;

	/// <summary>Gets the detailed measurements obtained by an explicit read.</summary>
	public ObservableCollection<LocalMeasurementView> LocalMeasurements { get; } = new ();

	/// <summary>Gets or sets the result of the most recent explicit detailed read.</summary>
	[ObservableProperty]
	private string _localDetailsStatus = "Detailed telemetry has not been requested.";

	/// <summary>Fetches component signals and meter channels once; does not install another polling loop.</summary>
	/// <returns>A task completing after the requested detailed telemetry has been displayed.</returns>
	[RelayCommand]
	private async Task LoadLocalDetailsAsync ()
		{
		if (!HasTedapi || IsBusy)
			return;
		IsBusy = true;
		LocalMeasurements.Clear ();
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (60));
			var snapshot = await _connection.Powerwall.GetLocalDeviceSnapshotAsync (force: true, cts.Token).ConfigureAwait (true);
			var telemetry = snapshot.Controller;
			AddDetail ("Controller", "Device time", telemetry.System?.Time);
			AddDetail ("Controller", "Remote service enabled", telemetry.System?.SupportMode?.RemoteService?.IsEnabled);
			AddDetail ("Controller", "Remote service expiry", telemetry.System?.SupportMode?.RemoteService?.ExpiryTime);
			AddDetail ("Controller", "Grid contactor closed", telemetry.Control?.Islanding?.ContactorClosed);
			AddDetail ("Controller", "Grid healthy", telemetry.Control?.Islanding?.GridOk);
			AddDetail ("Controller", "Remaining energy (Wh)", telemetry.Control?.SystemStatus?.RemainingWattHours);
			AddDetail ("Controller", "Full capacity (Wh)", telemetry.Control?.SystemStatus?.FullCapacityWattHours);
			AddDetail ("Controller", "Site shutdown", telemetry.Control?.Shutdown?.IsShutDown);
			AddDetail ("Controller", "Protection test running", telemetry.Control?.ProtectionTripTests?.IsRunning);
			AddDetail ("Controller", "Site manager running", telemetry.System?.SiteManager?.IsRunning);
			AddDetail ("Firmware", "Available version", telemetry.System?.FirmwareUpdate?.Version?.Version);
			AddDetail ("Firmware", "Update running", telemetry.Powerwall3Bus?.FirmwareUpdate?.IsUpdating);
			AddDetail ("Firmware", "Update progress (device scale)", telemetry.Powerwall3Bus?.FirmwareUpdate?.Progress?.Progress);
			AddDetail ("Controller", "Device enumeration running", telemetry.Powerwall3Bus?.Enumeration?.InProgress);
			var configuration = snapshot.Configuration;
			if (!string.IsNullOrWhiteSpace (configuration.Site?.Name)) _connection.SetSiteName (configuration.Site.Name);
			foreach (var meter in LocalMeterProjection.Create (configuration, telemetry))
				foreach (var channel in meter.Channels)
					{
					string name = $"{meter.Family} {meter.DeviceId ?? "unreported identity"} / CT {channel.Index}";
					AddDetail (name, "Location", channel.Location);
					AddDetail (name, "Device time", meter.Timestamp);
					AddDetail (name, "Real power (scaled W)", channel.RealPowerWatts);
					AddDetail (name, "Real power (reported W)", channel.Reported.RealPowerWatts);
					AddDetail (name, "Voltage (V)", channel.Reported.VoltageVolts);
					AddDetail (name, "Current (A)", channel.Reported.CurrentAmps);
					AddDetail (name, "Reactive power (var)", channel.Reported.ReactivePowerVars);
					AddDetail (name, "Imported energy (Ws)", channel.Reported.EnergyImportedWattSeconds);
					AddDetail (name, "Exported energy (Ws)", channel.Reported.EnergyExportedWattSeconds);
					}
			AddBusDetails (telemetry.EnergyBus, "Energy bus");
			await ReadExtendedDiagnosticsAsync (cts.Token).ConfigureAwait (true);
			foreach (var device in snapshot.Devices)
				{
				if (device.Telemetry is null)
					{ AddDetail (device.Din, "Unavailable", device.UnavailableReason); continue; }
				if (device.Telemetry.Components is not { } families) continue;
				foreach (var family in families)
					foreach (var component in family.Value)
						{
						string name = device.Din + " / " + family.Key + " " + (component.SerialNumber ?? "unreported serial");
						if (component.Signals is { } signals)
							foreach (var signal in signals)
								LocalMeasurements.Add (LocalMeasurementView.FromSignal (name, signal));
						if (component.ActiveAlerts is { } alerts)
							foreach (var alert in alerts)
								AddDetail (name, "Active alert", alert.Name);
						}
				}
			AddDiagnostics (LocalDiagnosticsProjection.Create (snapshot));
			var meters = LocalMeterProjection.Aggregate (snapshot);
			AddMeters ("TEDAPI", meters);
			var information = await _connection.Powerwall.GetLocalSystemInformationAsync (cancellationToken: cts.Token).ConfigureAwait (true);
			AddDetail ("Firmware", "Installed version", information.Version?.Version);
			AddDetail ("Firmware", "Update status", information.Update?.Status);
			AddDetail ("Firmware", "Downloaded bytes", information.Update?.BytesOffset);
			AddDetail ("Firmware", "Total bytes", information.Update?.TotalBytes);
			try
				{
				AddMeters ("Native local endpoint", await _connection.Powerwall.GetLocalNativeMeterAggregatesAsync (cancellationToken: cts.Token).ConfigureAwait (true));
				}
			catch (Exception exc) when (exc is PowerwallException or System.Net.Http.HttpRequestException)
				{
				AddDetail ("Native local endpoint", "Availability", exc.Message);
				}
			LocalDetailsStatus = $"Detailed response received {DateTimeOffset.Now:T}. Values update only when requested.";
			}
		catch (Exception exc) when (exc is PowerwallException or OperationCanceledException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
			{
			LocalDetailsStatus = "Detailed read incomplete: " + exc.Message;
			}
		finally
			{
			IsBusy = false;
			}
		}

	/// <summary>Reads supplemental metadata only during an explicit detailed refresh; no diagnostic procedures are started.</summary>
	/// <param name="cancellationToken">Cancels the detailed refresh.</param>
	/// <returns>A task completing when the three bounded read-only requests finish.</returns>
	private async Task ReadExtendedDiagnosticsAsync (CancellationToken cancellationToken)
		{
		async Task Read (string label, Func<Task<object?>> get)
			{
			try { AddBusDetails (await get ().ConfigureAwait (true), label); }
			catch (Exception exc) when (exc is PowerwallException or System.Net.Http.HttpRequestException)
				{ AddDetail (label, "Availability", exc.Message); }
			}
		await Read ("IEEE 2030.5", async () => await _connection.Powerwall.GetLocalIeee20305Async (cancellationToken: cancellationToken).ConfigureAwait (true)).ConfigureAwait (true);
		await Read ("Inverter tests", async () => await _connection.Powerwall.GetLocalInverterSelfTestsAsync (cancellationToken: cancellationToken).ConfigureAwait (true)).ConfigureAwait (true);
		await Read ("Protection tests", async () => await _connection.Powerwall.GetLocalProtectionTestStatusAsync (cancellationToken: cancellationToken).ConfigureAwait (true)).ConfigureAwait (true);
		}

	/// <summary>Displays typed fan, temperature and solar-input readings with explicit units and unavailable values.</summary>
	/// <param name="components">Identified component summaries.</param>
	internal void AddDiagnostics (System.Collections.Generic.IReadOnlyList<LocalComponentDiagnostics> components)
		{
		foreach (var component in components)
			{
			string source = (component.DeviceDin ?? "Unreported device") + " / " + component.Family + " [" + component.Index + "] " + component.SerialNumber;
			foreach (var fan in component.Fans)
				{
				AddDetail (source, "Fan " + fan.Name + " speed (RPM)", fan.SpeedRpm);
				AddDetail (source, "Fan " + fan.Name + " target (RPM)", fan.TargetSpeedRpm);
				AddDetail (source, "Fan " + fan.Name + " duty (%)", fan.DutyPercent);
				}
			foreach (var sensor in component.Temperatures) AddDetail (source, sensor.SignalName + " (°C)", sensor.Celsius);
			foreach (var input in component.SolarStrings)
				{
				string name = "PV input " + input.Name;
				AddDetail (source, name + " state", input.State);
				AddDetail (source, name + " connection flag", input.Connected);
				AddDetail (source, name + " voltage (V)", input.VoltageVolts);
				AddDetail (source, name + " current (A)", input.CurrentAmps);
				AddDetail (source, name + " calculated power (W)", input.PowerWatts);
				}
			}
		}

	private void AddMeters (string source, MeterAggregates meters)
		{
		foreach (var meter in new[] { ("Site", meters.Site), ("Solar", meters.Solar), ("Battery", meters.Battery), ("Load", meters.Load) })
			{
			string name = source + " / " + meter.Item1;
			AddDetail (name, "Power (W)", meter.Item2?.InstantPower);
			AddDetail (name, "Average reported voltage (V)", meter.Item2?.InstantAverageVoltage);
			AddDetail (name, "Frequency (Hz)", meter.Item2?.Frequency);
			AddDetail (name, "Phase A current (A)", meter.Item2?.PhaseACurrent);
			AddDetail (name, "Phase B current (A)", meter.Item2?.PhaseBCurrent);
			AddDetail (name, "Phase C current (A)", meter.Item2?.PhaseCCurrent);
			AddDetail (name, "Lifetime imported energy (Wh)", meter.Item2?.EnergyImported);
			AddDetail (name, "Lifetime exported energy (Wh)", meter.Item2?.EnergyExported);
			}
		}

	/// <summary>Displays reported bus fields, suppressing retained values from missing or incomplete messages.</summary>
	/// <param name="value">Typed bus object or scalar.</param>
	/// <param name="path">Display path identifying the reported family and slot.</param>
	internal void AddBusDetails (object? value, string path)
		{
		if (value is LocalBusMessage { IsMissing: true } or LocalBusMessage { IsComplete: false })
			{
			AddDetail ("Legacy bus", path + " / Availability", "Unavailable (message missing or incomplete)");
			return;
			}
		if (value is LocalDiagnosticScalar scalar) { AddDetail ("Local diagnostics", path, scalar.ToString ()); return; }
		if (value is null || value is string || value.GetType ().IsValueType)
			{ AddDetail ("Local diagnostics", path, value); return; }
		if (value is System.Collections.IEnumerable entries)
			{
			int index = 0;
			foreach (var entry in entries) AddBusDetails (entry, path + " [" + index++ + "]");
			return;
			}
		foreach (var property in value.GetType ().GetProperties (System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
			.Where (p => p.CanRead && p.GetIndexParameters ().Length == 0
				&& !(value is LocalIeee20305Registration && p.Name == nameof (LocalIeee20305Registration.Pin))))
			AddBusDetails (property.GetValue (value), path + " / " + property.Name);
		}

	/// <summary>Adds a typed value with an explicit unavailable label when it was not reported.</summary>
	/// <param name="source">Measurement source.</param>
	/// <param name="name">Measurement description including units where known.</param>
	/// <param name="value">Reported value, or null.</param>
	private void AddDetail (string source, string name, object? value) =>
		LocalMeasurements.Add (new LocalMeasurementView (source, name, value is null ? "Unavailable" : Convert.ToString (value, CultureInfo.CurrentCulture) ?? "Unavailable", null));

	/// <summary>Gets the per-battery block summaries.</summary>
	public ObservableCollection<BatteryBlockView> Batteries { get; }

	/// <summary>Gets or sets whether alert state was successfully read for the current system.</summary>
	[ObservableProperty]
	private string _alertsStatus = "Alert status has not been read.";

	/// <summary>Gets the active alert names.</summary>
	public ObservableCollection<string> Alerts { get; }

	/// <summary>Gets or sets the gateway firmware version.</summary>
	[ObservableProperty]
	private string? _version;

	/// <summary>Gets or sets the gateway device identification number.</summary>
	[ObservableProperty]
	private string? _din;

	/// <summary>Gets or sets the gateway uptime.</summary>
	[ObservableProperty]
	private string? _uptime;

	/// <summary>Gets or sets the connection mode label.</summary>
	[ObservableProperty]
	private string? _modeLabel;

	/// <summary>Gets or sets a value indicating whether any alerts are active.</summary>
	[ObservableProperty]
	private bool _hasAlerts;

	/// <summary>Loads system information, battery blocks, and alerts.</summary>
	/// <returns>A task that completes when the system data has been read.</returns>
	[RelayCommand]
	private async Task LoadAsync ()
		{
		StatusMessage = null;
		Version = null;
		Din = null;
		Uptime = null;
		Batteries.Clear ();
		Alerts.Clear ();
		HasAlerts = false;
		AlertsStatus = "Alert status has not been read.";
		LocalMeasurements.Clear ();
		LocalDetailsStatus = "Detailed telemetry has not been requested.";
		OnPropertyChanged (nameof (HasTedapi));
		IsBusy = true;
		try
			{
			using var cts = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			var powerwall = _connection.Powerwall;

			ModeLabel = _connection.Mode.ToString ();

			var status = await powerwall.StatusAsync (cts.Token).ConfigureAwait (true);
			Version = status?.Version ?? "unknown";
			Din = status?.Din ?? "unknown";
			Uptime = status?.UpTimeSeconds ?? "unknown";

			await LoadBatteriesAsync (powerwall, cts.Token).ConfigureAwait (true);
			await LoadAlertsAsync (powerwall, cts.Token).ConfigureAwait (true);
			}
		catch (OperationCanceledException)
			{
			StatusMessage = "Loading system information timed out.";
			}
		catch (PowerwallException exc)
			{
			StatusMessage = $"Could not load system information: {exc.Message}";
			}
		finally
			{
			IsBusy = false;
			}
		}

	private async Task LoadBatteriesAsync (Powerwall powerwall, CancellationToken token)
		{
		Batteries.Clear ();
		var blocks = await powerwall.BatteryBlocksAsync (token).ConfigureAwait (true);
		if (blocks is null)
			return;

		foreach (var pair in blocks)
			Batteries.Add (BatteryBlockView.From (pair.Key, pair.Value));
		}

	private async Task LoadAlertsAsync (Powerwall powerwall, CancellationToken token)
		{
		var alerts = await powerwall.AlertsAsync (token).ConfigureAwait (true);
		ApplyAlerts (alerts, DateTimeOffset.Now);
		}

	/// <summary>Displays reported firmware codes without inferring fault severity or occurrence time.</summary>
	/// <param name="alerts">Reported status and diagnostic identifiers.</param>
	/// <param name="receivedAt">When the application received this response, not when an alert first occurred.</param>
	internal void ApplyAlerts (System.Collections.Generic.IEnumerable<string> alerts, DateTimeOffset receivedAt)
		{
		Alerts.Clear ();
		foreach (var alert in alerts.Distinct (StringComparer.Ordinal)) Alerts.Add (alert);
		HasAlerts = Alerts.Count > 0;
		AlertsStatus = (HasAlerts ? $"{Alerts.Count} reported status and diagnostic codes." : "No codes returned in this response; this does not confirm system health.")
			+ $" Response received {receivedAt:g}. Reopen this page or choose Reload to update.";
		}
	}

/// <summary>
/// A read-only, display-friendly projection of a <see cref="BatteryBlock"/> for the System screen.
/// </summary>
public sealed class BatteryBlockView
	{
	private BatteryBlockView (string serial, string energyText, string powerText, string state)
		{
		Serial = serial;
		EnergyText = energyText;
		PowerText = powerText;
		State = state;
		}

	/// <summary>Gets the battery package serial number.</summary>
	public string Serial { get; }

	/// <summary>Gets the formatted remaining / full pack energy.</summary>
	public string EnergyText { get; }

	/// <summary>Gets the formatted instantaneous output power.</summary>
	public string PowerText { get; }

	/// <summary>Gets the inverter grid state.</summary>
	public string State { get; }

	/// <summary>Creates a display projection from a battery serial and its <see cref="BatteryBlock"/>.</summary>
	/// <param name="serial">The battery package serial number.</param>
	/// <param name="block">The battery block data.</param>
	/// <returns>A new <see cref="BatteryBlockView"/>.</returns>
	public static BatteryBlockView From (string serial, BatteryBlock block)
		{
		var remaining = block.NominalEnergyRemaining / 1000.0;
		var full = block.NominalFullPackEnergy / 1000.0;
		var power = block.PowerOut / 1000.0;

		return new BatteryBlockView (
			serial,
			remaining.HasValue && full.HasValue ? $"{remaining:0.0} / {full:0.0} kWh" : "Energy unavailable",
			power.HasValue ? $"{power:0.0} kW" : "Power unavailable",
			block.PinvGridState ?? "unknown");
		}
	}

/// <summary>A detailed local value with separate device timestamp and firmware signal name.</summary>
/// <param name="Source">Reported device/component identity.</param>
/// <param name="Name">Measurement label or untranslated firmware signal name.</param>
/// <param name="Value">Reported value, or an explicit unavailable label.</param>
/// <param name="Timestamp">Device timestamp, when provided.</param>
public sealed record LocalMeasurementView (string Source, string Name, string Value, string? Timestamp)
	{
	/// <summary>Formats a signal without substituting zero, false or a fabricated timestamp.</summary>
	/// <param name="source">Reported component identity.</param>
	/// <param name="signal">Typed device signal.</param>
	/// <returns>A display row retaining false and zero as real readings.</returns>
	public static LocalMeasurementView FromSignal (string source, LocalSignal signal) =>
		new (source, signal.Name ?? "Unnamed signal",
			signal.Value?.ToString ("G", CultureInfo.CurrentCulture) ?? signal.TextValue ?? signal.BoolValue?.ToString () ?? "Unavailable",
			signal.Timestamp);
	}
