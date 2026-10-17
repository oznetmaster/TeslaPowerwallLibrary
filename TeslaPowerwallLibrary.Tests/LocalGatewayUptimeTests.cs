// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;

using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks sync identity, clock validity, reset behavior and unsupported telemetry without hardware access.</summary>
[TestFixture]
public sealed class LocalGatewayUptimeTests
	{
	/// <summary>New site metadata stays nested, nullable and lossless through ordinary System.Text.Json serialization.</summary>
	[Test]
	public void SiteMetadata_UsesNestedConfigurationAndPreservesZero ()
		{
		var configuration = JsonSerializer.Deserialize<LocalConfiguration> ("""
			{"site_info":{"nominal_system_energy_ac":27,"nominal_system_power_ac":13.44,"max_site_meter_power_ac":0,
			"min_site_meter_power_ac":-100,"panel_max_current":100,"grid_code":"grid","country":"country",
			"state":"state","utility":"utility","battery_commission_date":"2025-01-01"}}
			""")!;
		var site = JsonSerializer.Deserialize<LocalConfiguration> (JsonSerializer.Serialize (configuration))!.Site!;
		Assert.That (site.NominalEnergyKilowattHours, Is.EqualTo (27));
		Assert.That (site.NominalPowerKilowatts, Is.EqualTo (13.44));
		Assert.That (site.MaximumSiteMeterPower, Is.Zero);
		Assert.That (site.MinimumSiteMeterPower, Is.EqualTo (-100));
		Assert.That (site.PanelMaximumCurrentAmps, Is.EqualTo (100));
		Assert.That (site.GridCode, Is.EqualTo ("grid"));
		Assert.That (site.Country, Is.EqualTo ("country"));
		Assert.That (site.State, Is.EqualTo ("state"));
		Assert.That (site.Utility, Is.EqualTo ("utility"));
		Assert.That (site.BatteryCommissionDate, Is.EqualTo ("2025-01-01"));
		Assert.That (JsonSerializer.Deserialize<LocalSiteConfiguration> ("{}")!.NominalEnergyKilowattHours, Is.Null);
		}
	/// <summary>Only the matched sync serial may provide uptime, including a valid reported zero.</summary>
	/// <param name="milliseconds">Counter value in milliseconds.</param>
	[TestCase (0d), TestCase (1234.5d)]
	public void MatchedSyncBoard_PreservesCounterAndOffset (double milliseconds)
		{
		var telemetry = Sample (milliseconds);
		var result = new LocalGatewayUptimeTracker ().Update (telemetry);
		Assert.That (result!.Uptime.TotalMilliseconds, Is.EqualTo (milliseconds).Within (0.001));
		Assert.That (result.GatewayTime.Offset, Is.EqualTo (TimeSpan.FromHours (2)));
		Assert.That (result.StartedAt, Is.EqualTo (result.GatewayTime - result.Uptime));
		}

	/// <summary>Invalid counters, including values outside the clock's representable range, do not fabricate uptime.</summary>
	/// <param name="milliseconds">An invalid counter.</param>
	[TestCase (-1d), TestCase (double.NaN), TestCase (double.PositiveInfinity), TestCase (double.NegativeInfinity), TestCase (double.MaxValue)]
	public void InvalidCounter_IsUnavailable (double milliseconds) =>
		Assert.That (new LocalGatewayUptimeTracker ().Update (Sample (milliseconds)), Is.Null);

	/// <summary>A missing, malformed or offset-free clock cannot establish a gateway boot time.</summary>
	/// <param name="time">A missing or unusable gateway clock.</param>
	[TestCase (null), TestCase (""), TestCase ("not-a-date"), TestCase ("2026-10-11T12:00:00")]
	public void InvalidClock_IsUnavailable (string? time) =>
		Assert.That (new LocalGatewayUptimeTracker ().Update (Sample (1000) with
			{
			System = new LocalSystemTelemetry { Time = time }
			}), Is.Null);

	/// <summary>Missing hardware or serial-less placeholders cannot substitute for the matched sync board.</summary>
	[Test]
	public void MissingHardwareOrMatchingSignal_IsUnavailable ()
		{
		var tracker = new LocalGatewayUptimeTracker ();
		var sample = Sample (1000);
		Assert.That (tracker.Update (sample with
			{
			EnergyBus = null
			}), Is.Null);
		Assert.That (tracker.Update (sample with
			{
			Components = null
			}), Is.Null);
		var boards = sample.Components!["msa"];
		Assert.That (tracker.Update (sample with
			{
			Components = new Dictionary<string, IReadOnlyList<LocalComponent>>
				{
				["msa"] = boards.Take (2).ToArray ()
				}
			}), Is.Null);
		}

	/// <summary>Firmware null entries and null lists are unavailable, while later valid records remain readable.</summary>
	[Test]
	public void NullBoardsAndSignals_DoNotHideAValidMatchedCounter ()
		{
		var sample = Sample (1000);
		var components = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"components":{"msa":[null,{"serialNumber":"sync","signals":null},
			{"serialNumber":"sync","signals":[null,{"name":"SYNC_Uptime","value":0}]}]}}
			""")!.Components;
		Assert.That (new LocalGatewayUptimeTracker ().Update (sample with { Components = components })!.Uptime, Is.EqualTo (TimeSpan.Zero));
		var absent = JsonSerializer.Deserialize<LocalTelemetry> ("""{"components":{"msa":null}}""")!.Components;
		Assert.That (new LocalGatewayUptimeTracker ().Update (sample with { Components = absent }), Is.Null);
		}

	/// <summary>Publication jitter and older snapshots retain a boot estimate; a counter reset and a new session replace it.</summary>
	[Test]
	public void BootTracking_HandlesJitterResetAndOlderSnapshots ()
		{
		var tracker = new LocalGatewayUptimeTracker ();
		var first = tracker.Update (Sample (100000))!;
		var next = tracker.Update (Sample (101000, "2026-10-11T12:00:02+02:00"))!;
		Assert.That (next.StartedAt, Is.EqualTo (first.StartedAt));
		Assert.That (tracker.Update (Sample (99000)), Is.EqualTo (next), "An older clock sample must not replace the newest sample.");
		var restarted = tracker.Update (Sample (0, "2026-10-11T12:00:03+02:00"))!;
		Assert.That (restarted.StartedAt, Is.EqualTo (restarted.GatewayTime));
		Assert.That (tracker.Update (new LocalTelemetry ()), Is.Null, "Unavailable data must not be replaced by the last known counter.");
		tracker.Reset ();
		var fresh = tracker.Update (Sample (102000, "2026-10-11T12:00:04+02:00"))!;
		Assert.That (fresh.StartedAt, Is.EqualTo (fresh.GatewayTime - fresh.Uptime));
		}

	/// <summary>Independent connections do not share boot history and concurrent updates remain bounded to valid samples.</summary>
	[Test]
	public async Task BootTracking_IsPerConnectionAndThreadSafe ()
		{
		var first = new LocalGatewayUptimeTracker ();
		var second = new LocalGatewayUptimeTracker ();
		var initial = first.Update (Sample (100000))!;
		var independent = second.Update (Sample (1000))!;
		Assert.That (initial.StartedAt, Is.Not.EqualTo (independent.StartedAt));
		var results = await Task.WhenAll (Enumerable.Range (0, 12).Select (_ => Task.Run (() => first.Update (Sample (100000)))));
		Assert.That (results.All (result => result!.StartedAt == initial.StartedAt), Is.True);
		}

	private static LocalTelemetry Sample (double milliseconds, string time = "2026-10-11T12:00:01+02:00")
		{
		var telemetry = JsonSerializer.Deserialize<LocalTelemetry> ("""
			{"system":{"time":"2026-10-11T12:00:01+02:00"},"esCan":{"bus":{"SYNC":{"packageSerialNumber":"sync"}}},
			 "components":{"msa":[{"signals":[{"name":"SYNC_Uptime","value":999}]},
			 {"serialNumber":"other","signals":[{"name":"SYNC_Uptime","value":999}]},
			 {"serialNumber":"sync","signals":[]},{"serialNumber":"sync","signals":[]}]}}
			""")!;
		var boards = telemetry.Components!["msa"].ToArray ();
		boards[3] = boards[3] with
			{
			Signals = new[] { new LocalSignal { Name = "SYNC_Uptime", Value = milliseconds } }
			};
		return telemetry with
			{
			System = new LocalSystemTelemetry { Time = time },
			Components = new Dictionary<string, IReadOnlyList<LocalComponent>> { ["msa"] = boards }
			};
		}
	}
