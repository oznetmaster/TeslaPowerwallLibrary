// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Text.Json;
using System.Linq;
using TeslaPowerwallLibrary.Tedapi;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.App.ViewModels;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Offline checks for local refresh configuration and truthful presentation of missing data.</summary>
[TestFixture]
public sealed class LocalPresentationTests
	{
	/// <summary>Diagnostic display retains zero, absent slots and scalar text without exposing provisioning PINs.</summary>
	[Test]
	public void ExtendedDiagnostics_DisplayReportedValuesAndHideProvisioningPin ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SystemViewModel (connection);
		var telemetry = System.Text.Json.JsonSerializer.Deserialize<LocalTelemetry> ("""{"ieee20305":{"registration":{"dateTimeRegistered":0,"pin":"secret-test-pin"},"controls":{"activeControls":[null,{"opModEnergize":false,"opModExpLimW":0}]}}}""")!;
		model.AddBusDetails (telemetry.Ieee20305, "IEEE");
		Assert.That (model.LocalMeasurements.Any (m => m.Name.EndsWith ("RegistrationTime") && m.Value == "0"), Is.True);
		Assert.That (model.LocalMeasurements.Any (m => m.Name.EndsWith ("[0]") && m.Value == "Unavailable"), Is.True);
		Assert.That (model.LocalMeasurements.Any (m => m.Value == "false"), Is.True);
		Assert.That (model.LocalMeasurements.Any (m => m.Name.Contains ("Pin") || m.Value.Contains ("secret-test-pin")), Is.False);
		Assert.That (model.LocalMeasurements.Any (m => m.Name.EndsWith ("Kind") || m.Name.EndsWith ("NumberText")), Is.False);
		}

	/// <summary>Retained values in missing or incomplete bus messages must not look like live measurements.</summary>
	[TestCase (true, true), TestCase (false, false)]
	public void BusDiagnostics_DoNotDisplayStaleReadings (bool missing, bool complete)
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SystemViewModel (connection);
		model.AddBusDetails (new LocalSolarInverterStatus { IsMissing = missing, IsComplete = complete, PowerWatts = 42 }, "PVAC [0]");
		Assert.That (model.LocalMeasurements, Has.Count.EqualTo (1));
		Assert.That (model.LocalMeasurements[0].Value, Is.EqualTo ("Unavailable (message missing or incomplete)"));
		model.AddBusDetails (new LocalSolarInverterStatus { IsMissing = false, PowerWatts = 0 }, "PVAC [1]");
		Assert.That (model.LocalMeasurements.Single (v => v.Name == "PVAC [1] / PowerWatts").Value, Is.EqualTo ("0"));
		}

	/// <summary>Rounded minutes carry into hours and invalid estimates remain unavailable.</summary>
	[TestCase (0.999, "1h 0m estimated backup")]
	[TestCase (1.999, "2h 0m estimated backup")]
	[TestCase (0, "0h 0m estimated backup")]
	[TestCase (-1, "Backup time unavailable")]
	[TestCase (double.NaN, "Backup time unavailable")]
	[TestCase (double.PositiveInfinity, "Backup time unavailable")]
	public void BackupEstimate_NormalizesRoundedMinutes (double hours, string expected)
		{
		using var connection = new PowerwallConnectionService ();
		var model = new HomeViewModel (connection) { TimeRemainingHours = hours };
		Assert.That (model.TimeRemainingText, Is.EqualTo (expected));
		}

	/// <summary>Detailed diagnostics label units and calculated power while retaining missing and zero readings.</summary>
	[Test]
	public void Diagnostics_PreserveIdentityUnitsAndUnavailableValues ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SystemViewModel (connection);
		model.AddDiagnostics (new[] { new LocalComponentDiagnostics
			{
			DeviceDin = "unit-a", Family = "pch", Index = 2, SerialNumber = "component-a",
			Fans = new[] { new LocalFanReading { Name = "A", SpeedRpm = 0 } },
			Temperatures = new[] { new LocalTemperatureReading { SignalName = "Ambient", Celsius = -2 } },
			SolarStrings = new[] { new LocalSolarStringReading { Name = "F", VoltageVolts = 100, CurrentAmps = 2 } }
			} });
		Assert.That (model.LocalMeasurements.All (v => v.Source == "unit-a / pch [2] component-a"), Is.True);
		Assert.That (model.LocalMeasurements.Single (v => v.Name == "Fan A speed (RPM)").Value, Is.EqualTo ("0"));
		Assert.That (model.LocalMeasurements.Single (v => v.Name == "Fan A target (RPM)").Value, Is.EqualTo ("Unavailable"));
		Assert.That (model.LocalMeasurements.Single (v => v.Name == "Ambient (°C)").Value, Is.EqualTo ("-2"));
		Assert.That (model.LocalMeasurements.Single (v => v.Name == "PV input F connection flag").Value, Is.EqualTo ("Unavailable"));
		Assert.That (model.LocalMeasurements.Single (v => v.Name == "PV input F calculated power (W)").Value, Is.EqualTo ("200"));
		}

	/// <summary>Manual refresh and user-selected cadences are accepted without connecting or polling.</summary>
	[TestCase (0)]
	[TestCase (1)]
	[TestCase (5)]
	[TestCase (3600)]
	public void RefreshInterval_IsConsumerConfigurable (int seconds)
		{
		using var connection = new PowerwallConnectionService ();
		connection.LocalPollInterval = TimeSpan.FromSeconds (seconds);
		Assert.That (connection.LocalPollInterval.TotalSeconds, Is.EqualTo (seconds));
		Assert.That (connection.IsConnected, Is.False);
		}

	/// <summary>Local connection options use the requested interval as cache lifetime; manual-only mode disables caching.</summary>
	[TestCase (0), TestCase (5), TestCase (30), TestCase (3600)]
	public void LocalCacheLifetime_MatchesSelectedPollingInterval (int seconds)
		{
		using var connection = new PowerwallConnectionService ();
		var model = new ConnectViewModel (connection) { IsCloudMode = false, IsFleetApiMode = false, LocalPollSeconds = seconds };
		Assert.That (model.BuildOptions ().CacheExpireSeconds, Is.EqualTo (seconds));
		}

	/// <summary>Invalid cadences cannot create tight retry loops.</summary>
	[TestCase (-1)]
	[TestCase (0.5)]
	[TestCase (3601)]
	public void RefreshInterval_RejectsInvalidValues (double seconds)
		{
		using var connection = new PowerwallConnectionService ();
		Assert.Throws<ArgumentOutOfRangeException> (() => connection.LocalPollInterval = TimeSpan.FromSeconds (seconds));
		}

	/// <summary>Missing flows are unavailable; measured zero remains a meaningful zero.</summary>
	[Test]
	public void Home_DistinguishesMissingDataFromZero ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new HomeViewModel (connection);
		Assert.That (model.SolarText, Is.EqualTo ("Unavailable"));
		Assert.That (model.BatteryFlowText, Is.EqualTo ("Unavailable"));
		Assert.That (model.GridFlowText, Is.EqualTo ("Unavailable"));
		model.SolarWatts = 0;
		model.BatteryWatts = 0;
		model.GridWatts = 0;
		Assert.That (model.SolarText, Does.Contain ("0.0"));
		Assert.That (model.BatteryFlowText, Is.EqualTo ("Idle"));
		Assert.That (model.GridFlowText, Is.EqualTo ("No flow"));
		}

	/// <summary>Unreported battery readings are not displayed as measured zero.</summary>
	[Test]
	public void BatteryBlock_DoesNotInventMeasurements ()
		{
		var view = BatteryBlockView.From ("test", new BatteryBlock ());
		Assert.That (view.EnergyText, Is.EqualTo ("Energy unavailable"));
		Assert.That (view.PowerText, Is.EqualTo ("Power unavailable"));
		}

	/// <summary>Missing settings remain unknown until explicitly supplied by the device or user.</summary>
	[Test]
	public void Settings_DoNotDefaultUnknownValuesToZeroOrFalse ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SettingsViewModel (connection);
		Assert.That (model.ReservePercent, Is.Null);
		Assert.That (model.GridChargingEnabled, Is.Null);
		Assert.That (model.StormWatchEnabled, Is.Null);
		Assert.That (model.ReserveText, Is.EqualTo ("Unavailable"));
		}

	/// <summary>One local response is sufficient to populate Home without inferred flows or extra network reads.</summary>
	[Test]
	public void LocalSnapshot_PreservesMissingAndZeroMeasurements ()
		{
		var empty = PowerFlowSnapshot.FromLocal (new TeslaPowerwallLibrary.Tedapi.LocalTelemetry ());
		Assert.That (empty.SolarWatts, Is.Null);
		Assert.That (empty.BatteryPercent, Is.Null);
		Assert.That (empty.GridStatus, Is.Null);
		var reported = PowerFlowSnapshot.FromLocal (new TeslaPowerwallLibrary.Tedapi.LocalTelemetry
			{
			Control = new TeslaPowerwallLibrary.Tedapi.LocalControlTelemetry
				{
				MeterAggregates = new[] { new TeslaPowerwallLibrary.Tedapi.LocalPowerReading { Location = "SOLAR", Watts = 0 } },
				Islanding = new TeslaPowerwallLibrary.Tedapi.LocalIslandingStatus { ContactorClosed = false }
				}
			});
		Assert.That (reported.SolarWatts, Is.Zero);
		Assert.That (reported.HomeWatts, Is.Null);
		Assert.That (reported.GridStatus, Is.EqualTo (GridStatus.Down));
		}

	/// <summary>Component signals retain zero, false and absence independently.</summary>
	[Test]
	public void ComponentDisplay_PreservesReportedValues ()
		{
		var missing = LocalMeasurementView.FromSignal ("test", new TeslaPowerwallLibrary.Tedapi.LocalSignal ());
		var zero = LocalMeasurementView.FromSignal ("test", new TeslaPowerwallLibrary.Tedapi.LocalSignal { Value = 0 });
		var flag = LocalMeasurementView.FromSignal ("test", new TeslaPowerwallLibrary.Tedapi.LocalSignal { BoolValue = false });
		Assert.That (missing.Value, Is.EqualTo ("Unavailable"));
		Assert.That (missing.Timestamp, Is.Null);
		Assert.That (zero.Value, Is.EqualTo ("0"));
		Assert.That (flag.Value, Is.EqualTo ("False"));
		}

	/// <summary>Disconnected controls return without opening confirmations or contacting any device.</summary>
	[Test]
	public async System.Threading.Tasks.Task Settings_DisconnectedCommandsCannotWrite ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SettingsViewModel (connection) { ReservePercent = 0, GridChargingEnabled = false, SelectedExportRule = "never" };
		Assert.That (model.CanChangeSettings, Is.False);
		Assert.That (model.IsSignedLocal, Is.False);
		await model.ApplyOperationCommand.ExecuteAsync (null);
		await model.ApplyGridChargingCommand.ExecuteAsync (null);
		await model.ApplyGridExportCommand.ExecuteAsync (null);
		await model.StartLocalBackupCommand.ExecuteAsync (null);
		await model.CancelLocalBackupCommand.ExecuteAsync (null);
		await model.GoOffGridCommand.ExecuteAsync (null);
		await model.ReconnectGridCommand.ExecuteAsync (null);
		Assert.That (model.IsBusy, Is.False);
		Assert.That (connection.IsConnected, Is.False);
		}

	/// <summary>Readings from the previous site must not appear to belong to a newly selected site.</summary>
	[Test]
	public void Home_SiteChangeClearsPreviousMeasurements ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new HomeViewModel (connection) { SolarWatts = 1000, BatteryPercent = 50 };
		connection.SetSiteLabel ("Another site");
		Assert.That (model.SolarWatts, Is.Null);
		Assert.That (model.BatteryPercent, Is.Null);
		Assert.That (model.FreshnessText, Does.Contain ("No readings"));
		Assert.That (new SystemViewModel (connection).AlertsStatus, Does.Contain ("not been read"));
		}

	/// <summary>Local key names and manual refresh survive serialization without persisting permission to write.</summary>
	[Test]
	public void Settings_RoundTripLocalOptionsWithoutControlPermission ()
		{
		var original = new AppSettings
			{
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned,
			LocalSigningKeyName = "test-key",
			LocalPollSeconds = 0
			};
		string json = JsonSerializer.Serialize (original);
		var copy = JsonSerializer.Deserialize<AppSettings> (json)!;
		Assert.That (copy.LocalProtocol, Is.EqualTo (original.LocalProtocol));
		Assert.That (copy.LocalSigningKeyName, Is.EqualTo ("test-key"));
		Assert.That (copy.LocalPollSeconds, Is.Zero);
		Assert.That (json, Does.Not.Contain ("allowLocalControl").IgnoreCase);
		}
	}
