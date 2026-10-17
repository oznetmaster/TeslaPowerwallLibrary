// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Net.Http;
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
	/// <summary>New signal requests retain signed captures and existing ordered variables on all supported transports.</summary>
	/// <param name="protocol">The local authentication and envelope format.</param>
	[TestCase (PowerwallLocalProtocol.Tedapi), TestCase (PowerwallLocalProtocol.TedapiSigned), TestCase (PowerwallLocalProtocol.TedapiBearer)]
	public async Task AdditionalSignals_PreserveSignaturesAndReachTheWire (PowerwallLocalProtocol protocol)
		{
		using RSA key = TestRsaKey.Create ();
		using Stream stream = typeof (Powerwall).Assembly.GetManifestResourceStream ("TeslaPowerwallLibrary.Tedapi.Protocol.Queries.data")!;
		using var captures = JsonDocument.Parse (stream);
		var expected = new Dictionary<string, string[]>
			{
			["pchSignalNames"] = ["PCH_AcRealPowerSum", "PCH_AcVoltageCN", "PCH_AcVoltageLL", "PCH_pcbaId", "PCH_usageId"],
			["bmsSignalNames"] = ["BMS_kwhDischargeTotal", "BMS_LOG_ahDischargeTotal", "BMS_LOG_cacAvg", "BMS_LOG_numFullBattCycles", "BMS_LOG_overDischargeCounter", "BMS_LOG_selfDischargeRate"],
			["hvpSignalNames"] = ["HVP_SafetyBiDisconnectState", "HVP_a061_LockoutOnIsoFaultRetry"],
			["pwsSignalNames"] = ["PWS_ExtSwitch_State", "PWS_RSDSelfTest_Result", "PWS_RSDSelfTest_State", "PWS_RSD_State", "PWS_assemblyId", "PWS_pcbaId", "PWS_reversePolarityStrings"],
			["baggrSignalNames"] = ["BAGGR_ExpectedEnergyRemaining", "BAGGR_ExpectedFullPackEnergy", "BAGGR_OperationType"]
			};
		int queries = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
				if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
					return Text ("""{"token":"synthetic"}""");
				if (request.RequestUri.AbsolutePath == "/tedapi/din")
					return Text (DIN);
				var envelope = DecodeLegacyRequest (protocol, await request.Content!.ReadAsByteArrayAsync ());
				var send = envelope.Payload.Send;
				bool components = send.Payload.Text.Contains ("query ComponentsQuery");
				var capture = captures.RootElement.GetProperty (components ? "components" : "device_controller_full");
				Assert.That (send.Payload.Text, Is.EqualTo (capture.GetProperty ("text").GetString ()));
				Assert.That (BitConverter.ToString (send.Code.ToByteArray ()).Replace ("-", "").ToLowerInvariant (),
					Is.EqualTo (capture.GetProperty ("code").GetString ()));
				using var original = JsonDocument.Parse (capture.GetProperty ("b_value").GetString ()!);
				using var variables = JsonDocument.Parse (send.B.Value);
				foreach (var property in original.RootElement.EnumerateObject ())
					{
					var actual = variables.RootElement.GetProperty (property.Name);
					if (property.Value.ValueKind == JsonValueKind.Array)
						Assert.That (actual.EnumerateArray ().Take (property.Value.GetArrayLength ()).Select (n => n.GetString ()),
							Is.EqualTo (property.Value.EnumerateArray ().Select (n => n.GetString ())));
					else
						Assert.That (System.Text.Json.Nodes.JsonNode.DeepEquals (System.Text.Json.Nodes.JsonNode.Parse (actual.GetRawText ()),
						System.Text.Json.Nodes.JsonNode.Parse (property.Value.GetRawText ())), Is.True);
					}
				var extras = components ? expected : new Dictionary<string, string[]> { ["msaSignals"] = ["SYNC_Uptime"] };
				foreach (var group in extras)
					{
					string?[] names = variables.RootElement.GetProperty (group.Key).EnumerateArray ().Select (n => n.GetString ()).ToArray ();
					foreach (string added in group.Value)
						Assert.That (names.Count (name => name == added), Is.EqualTo (1));
					Assert.That (names, Is.SupersetOf (group.Value));
					}
				queries++;
				return WrapLegacyResponse (protocol, new Legacy.MessageEnvelope
					{
					Payload = new Legacy.QueryType
						{
						Recv = new Legacy.PayloadString { Text = components ? """{"components":{"bms":[{"signals":[{"name":"BMS_kwhDischargeTotal","value":0}]}],"hvp":[{"signals":[{"name":"HVP_a061_LockoutOnIsoFaultRetry","boolValue":false},{"name":"HVP_SafetyBiDisconnectState","textValue":"OPEN"}]}]}}""" : STATUS }
						}
					});
			});
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = "powerwall.test",
			GatewayPassword = "label",
			Password = "customer",
			LocalProtocol = protocol,
			LocalSigningKey = key
			}, handler);
		await client.AuthenticateAsync ();
		var data = await client.GetComponentsAsync ();
		Assert.That (data.Components!["bms"][0].Signals![0].Value, Is.Zero);
		Assert.That (data.Components["hvp"][0].Signals![0].BoolValue, Is.False);
		Assert.That (data.Components["hvp"][0].Signals![1].TextValue, Is.EqualTo ("OPEN"));
		await client.GetDetailedTelemetryAsync ();
		await client.GetComponentsAsync ();
		Assert.That (queries, Is.EqualTo (2), "Request augmentation must not disable normal caching.");
		}

	/// <summary>Status exposes real firmware metadata and the existing duration-string facade from a matched sync board.</summary>
	/// <param name="protocol">The local authentication and envelope format.</param>
	[TestCase (PowerwallLocalProtocol.Tedapi), TestCase (PowerwallLocalProtocol.TedapiSigned), TestCase (PowerwallLocalProtocol.TedapiBearer)]
	public async Task GatewayUptime_IntegratesStatusCachingCancellationAndSessionReset (PowerwallLocalProtocol protocol)
		{
		using RSA key = TestRsaKey.Create ();
		int queries = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
				if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
					return Text ("""{"token":"synthetic"}""");
				if (request.RequestUri.AbsolutePath == "/tedapi/din")
					return Text (DIN);
				var envelope = DecodeLegacyRequest (protocol, await request.Content!.ReadAsByteArrayAsync ());
				if (envelope.Firmware is not null)
					return WrapLegacyResponse (protocol, new Legacy.MessageEnvelope
						{
						Firmware = new Legacy.FirmwareType
							{
							System = new Legacy.FirmwarePayload
								{
								Din = DIN,
								Version = new Legacy.FirmwareVersion { Text = "26.34.0", Githash = ByteString.CopyFrom (new byte[] { 0xab, 0xcd }) }
								}
							}
						});
				queries++;
				return WrapLegacyResponse (protocol, new Legacy.MessageEnvelope
					{
					Payload = new Legacy.QueryType
						{
						Recv = new Legacy.PayloadString { Text = UptimeTelemetry (queries > 2 ? 0 : 3600500, "2026-10-11T12:00:00.500Z") }
						}
					});
			});
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = "powerwall.test",
			GatewayPassword = "label",
			Password = "customer",
			LocalProtocol = protocol,
			LocalSigningKey = key
			}, handler);
		await client.AuthenticateAsync ();
		var uptime = await client.GetGatewayUptimeAsync ();
		Assert.That (uptime!.Uptime, Is.EqualTo (TimeSpan.FromMilliseconds (3600500)));
		var status = JsonSerializer.Deserialize<GatewayStatus> ((await client.PollAsync ("/api/status"))!)!;
		Assert.That (status.UpTimeSeconds, Is.EqualTo ("1h0m0.5s"));
		Assert.That (status.StartTime, Is.EqualTo ("2026-10-11 11:00:00 +0000"));
		Assert.That (status.GitHash, Is.EqualTo ("abcd"));
		Assert.That (queries, Is.EqualTo (1));
		using var cancel = new CancellationTokenSource ();
		cancel.Cancel ();
		Assert.That (async () => await client.GetGatewayUptimeAsync (cancellationToken: cancel.Token), Throws.InstanceOf<OperationCanceledException> ());
		Assert.That (queries, Is.EqualTo (1));
		await client.GetGatewayUptimeAsync (true);
		Assert.That (queries, Is.EqualTo (2));
		await client.CloseSessionAsync ();
		await client.AuthenticateAsync ();
		Assert.That ((await client.GetGatewayUptimeAsync ())!.Uptime, Is.EqualTo (TimeSpan.Zero));
		}

	/// <summary>The newer signed query cannot request unsigned uptime extras and must not silently fall back.</summary>
	[Test]
	public async Task GatewayUptime_June2026RemainsUnavailableWithoutAnExtraRequest ()
		{
		using var handler = new ScriptedHandler ((_, index) => Task.FromResult (index == 1 ? Text (DIN) : throw new AssertionException ("No query expected.")));
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = "powerwall.test",
			GatewayPassword = "label",
			LocalProtocol = PowerwallLocalProtocol.Tedapi,
			LocalQueryVersion = TedapiQueryVersion.June2026
			}, handler);
		await client.AuthenticateAsync ();
		Assert.That (await client.GetGatewayUptimeAsync (true), Is.Null);
		Assert.That (handler.Count, Is.EqualTo (1));
		}

	private static Legacy.MessageEnvelope DecodeLegacyRequest (PowerwallLocalProtocol protocol, byte[] bytes) => protocol switch
		{
			PowerwallLocalProtocol.Tedapi => Legacy.Message.Parser.ParseFrom (bytes).Message_,
			PowerwallLocalProtocol.TedapiBearer => Legacy.MessageEnvelope.Parser.ParseFrom (Signed.AuthEnvelope.Parser.ParseFrom (bytes).Payload),
			_ => Legacy.MessageEnvelope.Parser.ParseFrom (Signed.RoutableMessage.Parser.ParseFrom (bytes).ProtobufMessageAsBytes)
			};

	private static HttpResponseMessage WrapLegacyResponse (PowerwallLocalProtocol protocol, Legacy.MessageEnvelope response) => Binary (protocol switch
		{
			PowerwallLocalProtocol.Tedapi => new Legacy.Message { Message_ = response }.ToByteArray (),
			PowerwallLocalProtocol.TedapiBearer => new Signed.AuthEnvelope { Payload = response.ToByteString () }.ToByteArray (),
			_ => new Signed.RoutableMessage { ProtobufMessageAsBytes = response.ToByteString () }.ToByteArray ()
			});

	private static string UptimeTelemetry (double milliseconds, string time) => JsonSerializer.Serialize (new
		{
		system = new
			{
			time
			},
		esCan = new
			{
			bus = new
				{
				SYNC = new
					{
					packageSerialNumber = "sync"
					}
				}
			},
		components = new
			{
			msa = new[] { new { serialNumber = "sync", signals = new[] { new { name = "SYNC_Uptime", value = milliseconds } } } }
			}
		});
	}