// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;
using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;
using Graphql = TeslaPowerwallLibrary.Tedapi.Protocol.Graphql;

namespace TeslaPowerwallLibrary.Tests;

public sealed partial class TedapiTests
	{
	/// <summary>Public power helpers and detailed aggregates hold the same sample across transports and query versions.</summary>
	[TestCase (PowerwallLocalProtocol.Tedapi, TedapiQueryVersion.June2024)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, TedapiQueryVersion.June2024)]
	[TestCase (PowerwallLocalProtocol.TedapiBearer, TedapiQueryVersion.June2024)]
	[TestCase (PowerwallLocalProtocol.Tedapi, TedapiQueryVersion.June2026)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, TedapiQueryVersion.June2026)]
	[TestCase (PowerwallLocalProtocol.TedapiBearer, TedapiQueryVersion.June2026)]
	public async Task SolarMeterHold_PublicPathsCacheForceAndRecover (PowerwallLocalProtocol protocol, TedapiQueryVersion version)
		{
		using RSA key = TestRsaKey.Create ();
		using var harness = new SolarHoldHarness (protocol, version, key);
		await harness.Client.AuthenticateAsync ();
		var power = await harness.Client.GetPowerReadingsAsync ();
		Assert.That (power!.Solar, Is.EqualTo (3000));
		Assert.That (power.Load, Is.EqualTo (2500));
		Assert.That (harness.Queries, Is.EqualTo (2));
		Assert.That (harness.Configurations, Is.EqualTo (1));
		await harness.Client.GetPowerReadingsAsync ();
		Assert.That (harness.Queries, Is.EqualTo (2), "Repeated polls reuse both query caches.");
		var detailed = await harness.Client.GetDetailedMeterAggregatesAsync ();
		Assert.That (detailed.Solar!.InstantPower, Is.EqualTo (3000));
		Assert.That (detailed.Load!.InstantPower, Is.EqualTo (2500));
		Assert.That (detailed.Solar.PowerHoldAgeSeconds, Is.EqualTo (4));
		Assert.That (harness.Queries, Is.EqualTo (2));
		Assert.That ((await harness.Client.GetDetailedTelemetryAsync ()).Control!.MeterAggregates![0].Watts, Is.Zero, "Raw telemetry is unchanged.");
		harness.Telemetry = LocalSolarMeterHoldTests.Telemetry (solar: null, age: 61);
		var expired = JsonSerializer.Deserialize<TeslaPowerwallLibrary.Models.MeterAggregates> ((await harness.Client.PollAsync ("/api/meters/aggregates", force: true))!)!;
		Assert.That (expired.Solar!.InstantPower, Is.Null);
		Assert.That (expired.Load!.InstantPower, Is.EqualTo (-500));
		Assert.That ((await harness.Client.GetDetailedMeterAggregatesAsync ()).Solar!.InstantPower, Is.Null);
		harness.Telemetry = LocalSolarMeterHoldTests.Telemetry (solar: 0, alert: null);
		int queries = harness.Queries;
		var recovered = JsonSerializer.Deserialize<TeslaPowerwallLibrary.Models.MeterAggregates> ((await harness.Client.PollAsync ("/api/meters/aggregates", force: true))!)!;
		Assert.That (recovered.Solar!.InstantPower, Is.Zero);
		Assert.That (recovered.Load!.InstantPower, Is.EqualTo (-500));
		Assert.That (recovered.Solar.PowerHoldAgeSeconds, Is.Null);
		Assert.That (harness.Queries, Is.EqualTo (queries + 1), "Recovered polls need only the basic query.");
		}

	/// <summary>Ordinary healthy or genuine zero readings do not trigger extra network queries.</summary>
	[TestCase (0d, null), TestCase (1000d, "SolarMeterComms"), TestCase (-1d, "SolarMeterComms")]
	public async Task SolarMeterHold_NonCandidateDoesNotFetchDetails (double watts, string? alert)
		{
		using var harness = new SolarHoldHarness { Telemetry = LocalSolarMeterHoldTests.Telemetry (watts, alert: alert) };
		await harness.Client.AuthenticateAsync ();
		Assert.That ((await harness.Client.GetPowerReadingsAsync ())!.Solar, Is.EqualTo (watts));
		Assert.That (harness.Queries, Is.EqualTo (1));
		Assert.That (harness.Configurations, Is.Zero);
		}

	/// <summary>Optional detail failures preserve valid controller power; cancellation still reaches the caller.</summary>
	[TestCase ("network"), TestCase ("malformed"), TestCase ("configuration"), TestCase ("cancel")]
	public async Task SolarMeterHold_EnrichmentFailurePreservesBasicRead (string failure)
		{
		using var harness = new SolarHoldHarness { Failure = failure };
		await harness.Client.AuthenticateAsync ();
		if (failure == "cancel")
			await Assert.CatchAsync<OperationCanceledException> (async () => await harness.Client.GetPowerReadingsAsync (harness.Cancel.Token));
		else
			{
			var power = await harness.Client.GetPowerReadingsAsync ();
			Assert.That (power!.Solar, Is.Zero);
			Assert.That (power.Load, Is.EqualTo (-500));
			}
		}

	private sealed class SolarHoldHarness : IDisposable
		{
		internal LocalTelemetry Telemetry { get; set; } = LocalSolarMeterHoldTests.Telemetry ();
		internal int Queries { get; private set; }
		internal int Configurations { get; private set; }
		internal string? Failure { get; set; }
		internal CancellationTokenSource Cancel { get; } = new ();
		internal PowerwallTedapiClient Client { get; }

		internal SolarHoldHarness (PowerwallLocalProtocol protocol = PowerwallLocalProtocol.Tedapi,
			TedapiQueryVersion version = TedapiQueryVersion.June2024, RSA? key = null)
			{
			Client = new PowerwallTedapiClient (Options () with { LocalProtocol = protocol, LocalQueryVersion = version,
				Password = "synthetic", LocalSigningKey = key }, new ScriptedHandler (async (request, _) =>
				{
				if (request.RequestUri!.AbsolutePath == "/api/login/Basic") return Text ("{\"token\":\"synthetic\"}");
				if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
				byte[] bytes = await request.Content!.ReadAsByteArrayAsync ();
				ByteString incoming = protocol switch
					{
					PowerwallLocalProtocol.TedapiSigned => Signed.RoutableMessage.Parser.ParseFrom (bytes).ProtobufMessageAsBytes,
					PowerwallLocalProtocol.TedapiBearer => Signed.AuthEnvelope.Parser.ParseFrom (bytes).Payload,
					_ => Graphql.Frame.Parser.ParseFrom (bytes).Message
					};
				var legacy = Legacy.MessageEnvelope.Parser.ParseFrom (incoming);
				ByteString reply;
				if (legacy.Config is not null)
					{
					Configurations++;
					if (Failure == "configuration") throw new HttpRequestException ("synthetic config failure");
					string config = JsonSerializer.Serialize (LocalSolarMeterHoldTests.Configuration ());
					reply = protocol != PowerwallLocalProtocol.Tedapi ? new Signed.MessageEnvelope { Filestore = new Signed.FileStoreMessages
						{ ReadFileResponse = new Signed.FileStoreAPIReadFileResponse { File = new Signed.FileStoreAPIFile { Blob = ByteString.CopyFromUtf8 (config) } } } }.ToByteString ()
						: new Legacy.Message { Message_ = new Legacy.MessageEnvelope { Config = new Legacy.ConfigType
							{ Recv = new Legacy.PayloadConfigRecv { File = new Legacy.ConfigString { Text = config } } } } }.ToByteString ();
					}
				else
					{
					Queries++;
					if (Queries > 1 && Failure == "network") throw new HttpRequestException ("synthetic detail failure");
					if (Queries > 1 && Failure == "cancel") { Cancel.Cancel (); throw new OperationCanceledException (Cancel.Token); }
					string json = Queries > 1 && Failure == "malformed" ? "bad-json" : JsonSerializer.Serialize (Telemetry);
					if (version == TedapiQueryVersion.June2026)
						{
						var envelope = new Graphql.Envelope { Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = 1, Data = json } } };
						reply = protocol == PowerwallLocalProtocol.Tedapi ? new Graphql.Frame { Message = envelope.ToByteString () }.ToByteString () : envelope.ToByteString ();
						}
					else
						{
						var envelope = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = json } } };
						reply = protocol == PowerwallLocalProtocol.Tedapi ? new Legacy.Message { Message_ = envelope }.ToByteString () : envelope.ToByteString ();
						}
					}
				return Binary (protocol switch
					{
					PowerwallLocalProtocol.TedapiSigned => new Signed.RoutableMessage { ProtobufMessageAsBytes = reply }.ToByteArray (),
					PowerwallLocalProtocol.TedapiBearer => new Signed.AuthEnvelope { Payload = reply }.ToByteArray (),
					_ => reply.ToByteArray ()
					});
				}));
			}
		public void Dispose () { Client.Dispose (); Cancel.Dispose (); }
		}
	}