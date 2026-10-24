// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text.Json;

using Google.Protobuf;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;
using TeslaPowerwallLibrary.Tedapi;

using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tests;

public sealed partial class TedapiTests
	{
	/// <summary>All local envelopes expose derated limits and identified PW2 counters through the public status API.</summary>
	/// <param name="protocol">Selected local transport.</param>
	[TestCase (PowerwallLocalProtocol.Tedapi), TestCase (PowerwallLocalProtocol.TedapiSigned), TestCase (PowerwallLocalProtocol.TedapiBearer)]
	public async Task Pw2Status_IntegratesLimitsSignalsAndCacheOnEveryTransport (PowerwallLocalProtocol protocol)
		{
		const string configuration = """{"site_info":{"nominal_system_power_ac":10},"battery_blocks":[{"vin":"part--package","type":"Powerwall2"}]}""";
		const string controller = """
			{"control":{"systemStatus":{"nominalFullPackEnergyWh":13500,"nominalEnergyRemainingWh":0},"batteryBlocks":[{"din":"part--package"}],"islanding":{"contactorClosed":false}},
			"esCan":{"bus":{"THC":[{"packageSerialNumber":"package"}],"PINV":[{"PINV_PowerCapability":{"isMIA":false,"PINV_Pnom":9200}}]}},
			"components":{"msa":[{"serialNumber":"package","signals":[{"name":"THC_State","textValue":"ACTIVE"}]},
			{"serialNumber":"module","signals":[{"name":"PINV_EnergyCharged","value":0},{"name":"PINV_EnergyDischarged","value":123},{"name":"PINV_Qout","value":-42},{"name":"PINV_PllLocked","boolValue":false}]}]}}
			""";
		using RSA key = TestRsaKey.Create ();
		int configurations = 0, queries = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
				if (request.RequestUri!.AbsolutePath == "/api/login/Basic") return Text ("""{"token":"synthetic"}""");
				if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
				var envelope = DecodeLegacyRequest (protocol, await request.Content!.ReadAsByteArrayAsync ());
				if (envelope.Config is not null)
					{
					configurations++;
					if (protocol == PowerwallLocalProtocol.Tedapi)
						return WrapLegacyResponse (protocol, new Legacy.MessageEnvelope { Config = new Legacy.ConfigType { Recv = new Legacy.PayloadConfigRecv { File = new Legacy.ConfigString { Text = configuration } } } });
					ByteString payload = new Signed.MessageEnvelope
						{
						Filestore = new Signed.FileStoreMessages { ReadFileResponse = new Signed.FileStoreAPIReadFileResponse { File = new Signed.FileStoreAPIFile { Blob = ByteString.CopyFromUtf8 (configuration) } } }
						}.ToByteString ();
					return Binary (protocol == PowerwallLocalProtocol.TedapiBearer
						? new Signed.AuthEnvelope { Payload = payload }.ToByteArray ()
						: new Signed.RoutableMessage { ProtobufMessageAsBytes = payload }.ToByteArray ());
					}
				Assert.That (envelope.Payload.Send.Payload.Text, Does.Contain ("DeviceControllerQuery"));
				queries++;
				return WrapLegacyResponse (protocol, new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = controller } } });
			});
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = "powerwall.test", GatewayPassword = "label", Password = "customer",
			LocalProtocol = protocol, LocalSigningKey = key, CacheExpireSeconds = int.MaxValue
			}, handler);
		await client.AuthenticateAsync ();
		var status = JsonSerializer.Deserialize<SystemStatus> ((await client.PollAsync ("/api/system_status"))!)!;
		Assert.That (new[] { status.MaxChargePower, status.MaxDischargePower, status.MaxApparentPower, status.InverterNominalUsablePower }, Is.All.EqualTo (9200));
		Assert.That (status.NominalEnergyRemaining, Is.Zero);
		Assert.That (status.AvailableBlocks, Is.EqualTo (1));
		Assert.That (status.BatteryBlocks![0].EnergyCharged, Is.Zero);
		Assert.That (status.BatteryBlocks[0].EnergyDischarged, Is.EqualTo (123));
		Assert.That (status.BatteryBlocks[0].ReactivePowerOut, Is.EqualTo (-42));
		var vitals = await client.VitalsAsync ();
		Assert.That (vitals![DIN + "/msa/1/module"]["PINV_PllLocked"], Is.False);
		Assert.That (vitals[DIN + "/msa/0/package"]["THC_State"], Is.EqualTo ("ACTIVE"));
		await client.PollAsync ("/api/system_status");
		Assert.That (queries, Is.EqualTo (2));
		Assert.That (configurations, Is.EqualTo (1));
		await client.PollAsync ("/api/system_status", force: true);
		Assert.That (queries, Is.EqualTo (4));
		Assert.That (configurations, Is.EqualTo (2));
		}
	}
