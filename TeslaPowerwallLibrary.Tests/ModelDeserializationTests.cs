// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests that verify the JSON property mappings of the strongly typed model records against
/// representative Tesla Energy Gateway payloads.
/// </summary>
[TestClass]
public sealed class ModelDeserializationTests
	{
	[TestMethod]
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

		Assert.IsNotNull (aggregates);
		Assert.AreEqual (-1234.5, aggregates!.Site!.InstantPower);
		Assert.AreEqual (60.0, aggregates.Site.Frequency);
		Assert.AreEqual (250.0, aggregates.Battery!.InstantPower);
		Assert.AreEqual (980.25, aggregates.Load!.InstantPower);
		Assert.AreEqual (1500.75, aggregates.Solar!.InstantPower);
		}

	[TestMethod]
	public void WhenStateOfEnergyPayloadIsDeserializedThenPercentageIsMapped ()
		{
		const string json = """{ "percentage": 72.5 }""";

		var soe = JsonConvert.DeserializeObject<StateOfEnergy> (json);

		Assert.IsNotNull (soe);
		Assert.AreEqual (72.5, soe!.Percentage);
		}

	[TestMethod]
	public void WhenGridStatusPayloadIsDeserializedThenGridStatusIsMapped ()
		{
		const string json = """{ "grid_status": "SystemGridConnected", "grid_services_active": false }""";

		var status = JsonConvert.DeserializeObject<GridStatusResponse> (json);

		Assert.IsNotNull (status);
		Assert.AreEqual ("SystemGridConnected", status!.GridStatus);
		Assert.IsFalse (status.GridServicesActive);
		}

	[TestMethod]
	public void WhenOperationPayloadIsDeserializedThenReserveAndModeAreMapped ()
		{
		const string json = """{ "backup_reserve_percent": 24.0, "real_mode": "self_consumption" }""";

		var operation = JsonConvert.DeserializeObject<OperationResponse> (json);

		Assert.IsNotNull (operation);
		Assert.AreEqual (24.0, operation!.BackupReservePercent);
		Assert.AreEqual ("self_consumption", operation.RealMode);
		}

	[TestMethod]
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

		Assert.IsNotNull (status);
		Assert.AreEqual ("1234567-00-E--TG0000000000000", status!.Din);
		Assert.AreEqual ("23.44.1 27c790c5", status.Version);
		Assert.AreEqual ("1541h38m20.998412744s", status.UpTimeSeconds);
		}

	[TestMethod]
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

		Assert.IsNotNull (status);
		Assert.AreEqual (13500.0, status!.NominalFullPackEnergy);
		Assert.AreEqual (6750.0, status.NominalEnergyRemaining);
		Assert.IsNotNull (status.BatteryBlocks);
		Assert.AreEqual (1, status.BatteryBlocks!.Count);
		Assert.AreEqual ("TG000", status.BatteryBlocks[0].PackageSerialNumber);
		Assert.AreEqual ("PINV_Active", status.BatteryBlocks[0].PinvState);
		}

	[TestMethod]
	public void WhenSiteNamePayloadIsDeserializedThenNameAndTimezoneAreMapped ()
		{
		const string json = """{ "site_name": "My Home", "timezone": "America/Los_Angeles" }""";

		var siteName = JsonConvert.DeserializeObject<SiteName> (json);

		Assert.IsNotNull (siteName);
		Assert.AreEqual ("My Home", siteName!.Name);
		Assert.AreEqual ("America/Los_Angeles", siteName.Timezone);
		}
	}
