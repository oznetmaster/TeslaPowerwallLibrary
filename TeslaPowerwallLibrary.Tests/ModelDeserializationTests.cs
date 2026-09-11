// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests that verify the JSON property mappings of the strongly typed model records against
/// representative Tesla Energy Gateway payloads.
/// </summary>
[TestFixture]
public sealed class ModelDeserializationTests
	{
	[Test]
	public void WhenMeterAggregatesPayloadIsDeserializedThenInstantPowerIsMapped ()
		{
		const string json = """
			{
			  "site": { "instant_power": -1234.5, "frequency": 60.0 },
			  "battery": { "instant_power": 250.0 },
			  "load": { "instant_power": 980.25 },
			  "solar": { "instant_power": 1500.75 }
			}
			""";

		var aggregates = JsonConvert.DeserializeObject<MeterAggregates> (json);

		Assert.That (aggregates, Is.Not.Null);
		Assert.That (aggregates!.Site!.InstantPower, Is.EqualTo (-1234.5));
		Assert.That (aggregates.Site.Frequency, Is.EqualTo (60.0));
		Assert.That (aggregates.Battery!.InstantPower, Is.EqualTo (250.0));
		Assert.That (aggregates.Load!.InstantPower, Is.EqualTo (980.25));
		Assert.That (aggregates.Solar!.InstantPower, Is.EqualTo (1500.75));
		}

	[Test]
	public void WhenStateOfEnergyPayloadIsDeserializedThenPercentageIsMapped ()
		{
		const string json = """{ "percentage": 72.5 }""";

		var soe = JsonConvert.DeserializeObject<StateOfEnergy> (json);

		Assert.That (soe, Is.Not.Null);
		Assert.That (soe!.Percentage, Is.EqualTo (72.5));
		}

	[Test]
	public void WhenGridStatusPayloadIsDeserializedThenGridStatusIsMapped ()
		{
		const string json = """{ "grid_status": "SystemGridConnected", "grid_services_active": false }""";

		var status = JsonConvert.DeserializeObject<GridStatusResponse> (json);

		Assert.That (status, Is.Not.Null);
		Assert.That (status!.GridStatus, Is.EqualTo ("SystemGridConnected"));
		Assert.That (status.GridServicesActive, Is.False);
		}

	[Test]
	public void WhenOperationPayloadIsDeserializedThenReserveAndModeAreMapped ()
		{
		const string json = """{ "backup_reserve_percent": 24.0, "real_mode": "self_consumption" }""";

		var operation = JsonConvert.DeserializeObject<OperationResponse> (json);

		Assert.That (operation, Is.Not.Null);
		Assert.That (operation!.BackupReservePercent, Is.EqualTo (24.0));
		Assert.That (operation.RealMode, Is.EqualTo ("self_consumption"));
		}

	[Test]
	public void WhenStatusPayloadIsDeserializedThenVersionAndDinAreMapped ()
		{
		const string json = """
			{
			  "din": "1234567-00-E--TG0000000000000",
			  "version": "23.44.1 27c790c5",
			  "git_hash": "27c790c5",
			  "up_time_seconds": "1541h38m20.998412744s"
			}
			""";

		var status = JsonConvert.DeserializeObject<GatewayStatus> (json);

		Assert.That (status, Is.Not.Null);
		Assert.That (status!.Din, Is.EqualTo ("1234567-00-E--TG0000000000000"));
		Assert.That (status.Version, Is.EqualTo ("23.44.1 27c790c5"));
		Assert.That (status.UpTimeSeconds, Is.EqualTo ("1541h38m20.998412744s"));
		}

	[Test]
	public void WhenSystemStatusPayloadIsDeserializedThenBatteryBlocksAreMapped ()
		{
		const string json = """
			{
			  "nominal_full_pack_energy": 13500.0,
			  "nominal_energy_remaining": 6750.0,
			  "battery_blocks": [
				 { "PackageSerialNumber": "TG000", "nominal_energy_remaining": 6750.0, "pinv_state": "PINV_Active" }
			  ]
			}
			""";

		var status = JsonConvert.DeserializeObject<SystemStatus> (json);

		Assert.That (status, Is.Not.Null);
		Assert.That (status!.NominalFullPackEnergy, Is.EqualTo (13500.0));
		Assert.That (status.NominalEnergyRemaining, Is.EqualTo (6750.0));
		Assert.That (status.BatteryBlocks, Is.Not.Null);
		Assert.That (status.BatteryBlocks!.Count, Is.EqualTo (1));
		Assert.That (status.BatteryBlocks[0].PackageSerialNumber, Is.EqualTo ("TG000"));
		Assert.That (status.BatteryBlocks[0].PinvState, Is.EqualTo ("PINV_Active"));
		}

	[Test]
	public void WhenSiteNamePayloadIsDeserializedThenNameAndTimezoneAreMapped ()
		{
		const string json = """{ "site_name": "My Home", "timezone": "America/Los_Angeles" }""";

		var siteName = JsonConvert.DeserializeObject<SiteName> (json);

		Assert.That (siteName, Is.Not.Null);
		Assert.That (siteName!.Name, Is.EqualTo ("My Home"));
		Assert.That (siteName.Timezone, Is.EqualTo ("America/Los_Angeles"));
		}

	[Test]
	public void WhenSolarPowerwallAlertsPayloadIsDeserializedThenAlertFlagsAreMapped ()
		{
		const string json = """
			{
			  "pvac_alerts": { "PVAC_Fault": false, "PVAC_Overtemp": true },
			  "pvs_alerts": { "PVS_Fault": true }
			}
			""";

		var alerts = JsonConvert.DeserializeObject<SolarPowerwallAlertsResponse> (json);

		Assert.That (alerts, Is.Not.Null);
		Assert.That (alerts!.PvacAlerts, Is.Not.Null);
		Assert.That (alerts.PvacAlerts!["PVAC_Fault"], Is.False);
		Assert.That (alerts.PvacAlerts["PVAC_Overtemp"], Is.True);
		Assert.That (alerts.PvsAlerts, Is.Not.Null);
		Assert.That (alerts.PvsAlerts!["PVS_Fault"], Is.True);
		}
	}