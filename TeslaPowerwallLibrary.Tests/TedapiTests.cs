// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Google.Protobuf;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;

using Graphql = TeslaPowerwallLibrary.Tedapi.Protocol.Graphql;
using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class TedapiTests
	{
	private const string DIN = "1707000-00-A--TEST0000000000";
	private const string STATUS = """{"control":{"systemStatus":{"nominalFullPackEnergyWh":13500,"nominalEnergyRemainingWh":0},"islanding":{"contactorClosed":false,"gridOK":false},"meterAggregates":[{"location":"SITE","realPowerW":-200},{"location":"SOLAR","realPowerW":1400},{"location":"BATTERY","realPowerW":0},{"location":"LOAD","realPowerW":1200}],"alerts":{"active":[]}},"system":{"time":"2026-10-07T12:00:00Z"}}""";

	/// <summary>Firmware details retain wide counters and optional messages through every local envelope type.</summary>
	[TestCase (PowerwallLocalProtocol.Tedapi), TestCase (PowerwallLocalProtocol.TedapiSigned), TestCase (PowerwallLocalProtocol.TedapiBearer)]
	public async Task SystemInformation_PreservesMetadataAndCachesAcrossStatusCalls (PowerwallLocalProtocol protocol)
		{
		using RSA key = RSA.Create (4096);
		using var handler = new ScriptedHandler (async (request, _) =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
				{
				var login = JsonSerializer.Deserialize<LocalLoginRequest> (await request.Content!.ReadAsStringAsync ())!;
				Assert.That (login.Username, Is.EqualTo (protocol == PowerwallLocalProtocol.TedapiBearer ? "installer" : "customer"));
				Assert.That (login.ForceSiteManagerOff, Is.False);
				return Text ("""{"token":"test-token"}""");
				}
			if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
			Assert.That (request.Headers.Authorization!.Scheme, Is.EqualTo (protocol == PowerwallLocalProtocol.Tedapi ? "Basic" : "Bearer"));
			byte[] bytes = await request.Content!.ReadAsByteArrayAsync ();
			var envelope = protocol switch
				{
				PowerwallLocalProtocol.Tedapi => Legacy.Message.Parser.ParseFrom (bytes).Message_,
				PowerwallLocalProtocol.TedapiBearer => Legacy.MessageEnvelope.Parser.ParseFrom (Signed.AuthEnvelope.Parser.ParseFrom (bytes).Payload),
				_ => Legacy.MessageEnvelope.Parser.ParseFrom (Signed.RoutableMessage.Parser.ParseFrom (bytes).ProtobufMessageAsBytes)
				};
			Assert.That (envelope.Firmware.IdCase, Is.EqualTo (Legacy.FirmwareType.IdOneofCase.Request));
			var response = new Legacy.MessageEnvelope { Firmware = new Legacy.FirmwareType { System = new Legacy.FirmwarePayload
				{
				Din = DIN, Gateway = new Legacy.EcuId { PartNumber = "part", SerialNumber = "serial" },
				Version = new Legacy.FirmwareVersion { Text = "26.34.0", Githash = ByteString.CopyFrom (new byte[] { 0xab, 0xcd }) },
				SystemUpdate = new Legacy.SystemUpdate { TotalBytes = ulong.MaxValue, BytesOffset = 0, UpdateStatus = 7 }
				} } };
			bytes = protocol switch
				{
				PowerwallLocalProtocol.Tedapi => new Legacy.Message { Message_ = response }.ToByteArray (),
				PowerwallLocalProtocol.TedapiBearer => new Signed.AuthEnvelope { Payload = response.ToByteString () }.ToByteArray (),
				_ => new Signed.RoutableMessage { ProtobufMessageAsBytes = response.ToByteString () }.ToByteArray ()
				};
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new ByteArrayContent (bytes) };
			});
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{ Host = "powerwall.test", GatewayPassword = "label", Password = "customer", LocalProtocol = protocol, LocalSigningKey = key }, handler);
		await client.AuthenticateAsync ();
		var info = await client.GetSystemInformationAsync ();
		Assert.That (info.Gateway!.SerialNumber, Is.EqualTo ("serial"));
		Assert.That (info.Version!.GitHash, Is.EqualTo ("abcd"));
		Assert.That (info.Update!.TotalBytes, Is.EqualTo ((decimal)ulong.MaxValue));
		Assert.That (info.Update.BytesOffset, Is.Zero);
		Assert.That (info.Update.StagedVersion, Is.Null);
		int count = handler.Count;
		Assert.That (await client.PollAsync ("/api/status"), Does.Contain ("26.34.0"));
		Assert.That (handler.Count, Is.EqualTo (count));
		}

	/// <summary>Native counters use a customer token without changing the configured TEDAPI authentication.</summary>
	[TestCase (PowerwallLocalProtocol.Tedapi), TestCase (PowerwallLocalProtocol.TedapiBearer)]
	public async Task NativeMeters_PreserveZeroCountersAndSeparateAuthentication (PowerwallLocalProtocol protocol)
		{
		int meterReads = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("powerwall.test"));
			if (request.RequestUri.AbsolutePath == "/api/login/Basic")
				{
				var login = JsonSerializer.Deserialize<LocalLoginRequest> (await request.Content!.ReadAsStringAsync ())!;
				Assert.That (login.ForceSiteManagerOff, Is.False);
				if (login.Username == "customer") Assert.That (login.Password, Is.EqualTo ("abcde"));
				return Text (JsonSerializer.Serialize (new LocalLoginResponse { Token = login.Username }));
				}
			if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
			if (request.RequestUri.AbsolutePath == "/api/meters/aggregates")
				{
				Assert.That (request.Method, Is.EqualTo (HttpMethod.Get));
				Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo ("customer"));
				meterReads++;
				return Text ("""{"site":{"energy_imported":12345.75,"energy_exported":0}}""");
				}
			Assert.That (request.Headers.Authorization!.Scheme, Is.EqualTo (protocol == PowerwallLocalProtocol.Tedapi ? "Basic" : "Bearer"));
			if (protocol == PowerwallLocalProtocol.Tedapi) return QueryResponse (STATUS);
			Assert.That (request.Headers.Authorization.Parameter, Is.EqualTo ("installer"));
			var envelope = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = STATUS } } };
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new ByteArrayContent (new Signed.AuthEnvelope { Payload = envelope.ToByteString () }.ToByteArray ()) };
			});
		using var client = new PowerwallTedapiClient (new PowerwallOptions { Host = "powerwall.test", GatewayPassword = "label-abcde", LocalProtocol = protocol }, handler);
		await client.AuthenticateAsync ();
		var meters = await client.GetNativeMeterAggregatesAsync ();
		Assert.That (meters.Site!.EnergyImported, Is.EqualTo (12345.75));
		Assert.That (meters.Site.EnergyExported, Is.Zero);
		Assert.That (meters.Site.InstantPower, Is.Null);
		Assert.That (meters.Battery, Is.Null);
		await client.GetNativeMeterAggregatesAsync ();
		Assert.That (meterReads, Is.EqualTo (1));
		Assert.That ((await client.GetTelemetryAsync ()).Control!.SystemStatus!.RemainingWattHours, Is.Zero);
		}

	/// <summary>An expired installer session is retried once, and persistent rejection stops.</summary>
	[TestCase (false), TestCase (true)]
	public async Task BearerRead_RenewsOnceWithoutRetryingIndefinitely (bool rejectAgain)
		{
		int logins = 0, queries = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
				{ logins++; return Text ("""{"token":"installer"}"""); }
			if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
			var auth = Signed.AuthEnvelope.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			Assert.That ((int)auth.ExternalAuth.Type, Is.EqualTo (1));
			Assert.That (Legacy.MessageEnvelope.Parser.ParseFrom (auth.Payload).Payload.Send.Payload.Text, Does.Contain ("DeviceControllerQuery"));
			queries++;
			if (queries == 1 || rejectAgain) return new HttpResponseMessage (HttpStatusCode.Unauthorized);
			var answer = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = STATUS } } };
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new ByteArrayContent (new Signed.AuthEnvelope { Payload = answer.ToByteString () }.ToByteArray ()) };
			});
		using var client = new PowerwallTedapiClient (new PowerwallOptions { Host = "powerwall.test", GatewayPassword = "label", LocalProtocol = PowerwallLocalProtocol.TedapiBearer }, handler);
		await client.AuthenticateAsync ();
		if (rejectAgain) await Assert.CatchAsync<PowerwallException> (async () => await client.GetTelemetryAsync ());
		else Assert.That ((await client.GetTelemetryAsync ()).Control!.SystemStatus!.RemainingWattHours, Is.Zero);
		Assert.That (queries, Is.EqualTo (2));
		Assert.That (logins, Is.EqualTo (2));
		}

	[Test]
	public async Task BasicTransport_AuthenticatesLocally_AndUsesExactSignedQuery ()
		{
		using var handler = new ScriptedHandler (async (request, index) =>
			{
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("powerwall.test"));
			Assert.That (request.Headers.Authorization!.Scheme, Is.EqualTo ("Basic"));
			if (index == 1)
				{
				Assert.That (request.Method, Is.EqualTo (HttpMethod.Get));
				Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/din"));
				return Text (DIN);
				}
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/v1"));
			Legacy.Message message = Legacy.Message.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			Assert.That (message.Message_.Recipient.Din, Is.EqualTo (DIN));
			Assert.That (message.Message_.Payload.Send.Num, Is.EqualTo (2));
			Assert.That (message.Message_.Payload.Send.Code.Length, Is.GreaterThan (100));
			Assert.That (message.Message_.Config, Is.Null);
			Assert.That (message.Message_.Payload.Send.Payload.Text, Does.Contain ("DeviceControllerQuery"));
			return QueryResponse (STATUS);
			});
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		LocalTelemetry telemetry = await client.GetTelemetryAsync ();
		Assert.That (telemetry.Control!.SystemStatus!.RemainingWattHours, Is.Zero);
		Assert.That (telemetry.Control.Islanding!.ContactorClosed, Is.False);
		Assert.That (handler.Count, Is.EqualTo (2));
		}

	[Test]
	public async Task Cache_CoalescesConcurrentReads_ForceRefreshesOnce ()
		{
		using var handler = BasicHandler ();
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		await Task.WhenAll (Enumerable.Range (0, 12).Select (_ => client.GetTelemetryAsync ()));
		Assert.That (handler.Count, Is.EqualTo (2));
		await client.GetTelemetryAsync (force: true);
		Assert.That (handler.Count, Is.EqualTo (3));
		}

	[Test]
	public async Task TypedMapping_PreservesZeroEnergy_ExportSign_AndFalseContactor ()
		{
		using var handler = BasicHandler ();
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		Assert.That (await client.PollAsync ("/api/system_status/soe"), Does.Contain ("\"percentage\":0"));
		Assert.That ((await client.PowerAsync ()).Site, Is.EqualTo (-200));
		Assert.That (await client.GetTimeRemainingAsync (), Is.Zero);
		Assert.That (await client.PollAsync ("/api/system_status/grid_status"), Does.Contain ("SystemIslandedActive"));
		}

	[Test]
	public async Task MissingMeasurements_AreNotInvented ()
		{
		using var handler = new ScriptedHandler ((_, index) => Task.FromResult (index == 1 ? Text (DIN) : QueryResponse ("{}")));
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		Assert.That (await client.PollAsync ("/api/system_status/soe"), Is.Null);
		Assert.That (await client.PollAsync ("/api/system_status/grid_status"), Is.Null);
		Assert.That (await client.GetTimeRemainingAsync (), Is.Null);
		var power = await client.GetPowerReadingsAsync ();
		Assert.That (new[] { power.Site, power.Solar, power.Battery, power.Load }, Is.All.Null);
		}

	[Test]
	public async Task GzipResponses_AreDecodedWithoutDependingOnContentEncoding ()
		{
		using var handler = new ScriptedHandler (async (_, index) =>
			{
			using HttpResponseMessage plain = index == 1 ? Text (DIN) : QueryResponse (STATUS);
			byte[] body = await plain.Content.ReadAsByteArrayAsync ();
			using var stream = new MemoryStream ();
			using (var gzip = new GZipStream (stream, CompressionMode.Compress, true))
				{
				gzip.Write (body, 0, body.Length);
				}
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new ByteArrayContent (stream.ToArray ()) };
			});
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		Assert.That ((await client.GetTelemetryAsync ()).Control, Is.Not.Null);
		}

	[TestCase (429)]
	[TestCase (503)]
	public async Task BusyGateway_BacksOffEvenForForcedRequests (int status)
		{
		using var handler = new ScriptedHandler ((_, index) => Task.FromResult (
			index == 1 ? Text (DIN) : new HttpResponseMessage ((HttpStatusCode)status)));
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		await Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.GetTelemetryAsync ());
		await Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.GetTelemetryAsync (force: true));
		Assert.That (handler.Count, Is.EqualTo (2));
		}

	[Test]
	public async Task CancellationBeforeCachedRead_StillCancels ()
		{
		using var handler = BasicHandler ();
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		await client.GetTelemetryAsync ();
		using var cancellation = new CancellationTokenSource ();
		cancellation.Cancel ();
		Assert.That (async () => await client.GetTelemetryAsync (cancellationToken: cancellation.Token),
			Throws.InstanceOf<OperationCanceledException> ());
		Assert.That (handler.Count, Is.EqualTo (2));
		}

	[Test]
	public async Task UnimplementedControl_DoesNotSendAnyRequest ()
		{
		using var handler = BasicHandler ();
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		await Assert.ThrowsAsync<PowerwallNotSupportedException> (async () => await client.PostAsync ("/api/operation", new { real_mode = "backup" }));
		Assert.That (handler.Count, Is.EqualTo (1));
		}

	[Test]
	public void DefaultSerializer_UsesWireNamesAndPreservesFalseAndZero ()
		{
		LocalTelemetry telemetry = JsonSerializer.Deserialize<LocalTelemetry> (STATUS)!;
		Assert.That (telemetry.Control!.SystemStatus!.RemainingWattHours, Is.Zero);
		Assert.That (telemetry.Control.Islanding!.GridOk, Is.False);
		var json = JsonSerializer.Serialize (new LocalSignal { Name = "test", Value = 0, BoolValue = false });
		Assert.That (json, Does.Contain ("\"value\":0").And.Contain ("\"boolValue\":false"));
		}

	[Test]
	public void CustomerLogin_CannotRequestSiteManagerShutdown ()
		{
		var login = new LocalLoginRequest { Username = "customer", Password = "synthetic" };
		Assert.That (JsonSerializer.Serialize (login), Does.Contain ("\"force_sm_off\":false"));
		Assert.That (typeof (LocalLoginRequest).GetProperty (nameof (LocalLoginRequest.ForceSiteManagerOff))!.CanWrite, Is.False);
		}

	[Test]
	public void Signing_UsesDocumentedTlvAndSha512Pkcs1 ()
		{
		#if NETFRAMEWORK
		using RSA key = new RSACng (4096);
#else
		using RSA key = RSA.Create (4096);
#endif
		byte[] envelope = { 1, 2, 3 };
		const uint expiry = 0x12345678;
		Signed.RoutableMessage result = TedapiSigning.Sign (key, DIN, envelope, expiry);
		using var signed = new MemoryStream ();
		signed.Write (new byte[] { 0, 1, 7, 1, 1, 7, 2, (byte)DIN.Length }, 0, 8);
		byte[] din = Encoding.UTF8.GetBytes (DIN);
		signed.Write (din, 0, din.Length);
		signed.Write (new byte[] { 4, 4, 0x12, 0x34, 0x56, 0x78, 255, 1, 2, 3 }, 0, 10);
		Assert.That (key.VerifyData (signed.ToArray (), result.SignatureData.RsaData.Signature.ToByteArray (),
			HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1), Is.True);
		Assert.That (result.ToDestination.Domain, Is.EqualTo (Signed.Domain.EnergyDevice));
		Assert.That (result.SignatureData.RsaData.ExpiresAt, Is.EqualTo (expiry));
		Assert.That (result.ProtobufMessageAsBytes.ToByteArray (), Is.EqualTo (envelope));
#if !NETFRAMEWORK
		Assert.That (TedapiSigning.PublicKeyDer (key), Is.EqualTo (key.ExportRSAPublicKey ()));
#endif
		}

	[Test]
	public async Task SignedRead_RenewsRejectedSessionOnce_AndUsesFreshTokenAndSignature ()
		{
		#if NETFRAMEWORK
		using RSA key = new RSACng (4096);
#else
		using RSA key = RSA.Create (4096);
#endif
		var logins = 0;
		var queries = new List<Signed.RoutableMessage> ();
		using var handler = new ScriptedHandler (async (request, _) =>
			{
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("powerwall.test"));
			if (request.RequestUri.AbsolutePath == "/api/login/Basic")
				{
				logins++;
				var payload = await request.Content!.ReadAsStringAsync ();
				Assert.That (payload, Does.Contain ("\"force_sm_off\":false"));
				return Text ("{\"token\":\"token-" + logins + "\"}");
				}
			if (request.RequestUri.AbsolutePath == "/tedapi/din")
				{
				return Text (DIN);
				}
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/v1r"));
			Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo ("token-" + logins));
			queries.Add (Signed.RoutableMessage.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ()));
			if (queries.Count == 1)
				{
				return new HttpResponseMessage (HttpStatusCode.Unauthorized);
				}
			var envelope = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = STATUS } } };
			return Binary (new Signed.RoutableMessage { ProtobufMessageAsBytes = envelope.ToByteString () }.ToByteArray ());
			});
		using var client = new PowerwallTedapiClient (Options () with
			{ LocalProtocol = PowerwallLocalProtocol.TedapiSigned, Password = "synthetic", LocalSigningKey = key }, handler);
		await client.AuthenticateAsync ();
		Assert.That ((await client.GetTelemetryAsync ()).Control, Is.Not.Null);
		Assert.That (logins, Is.EqualTo (2));
		Assert.That (queries[0].Uuid, Is.Not.EqualTo (queries[1].Uuid));
		client.Dispose ();
		Assert.That (key.ExportParameters (false).Modulus, Is.Not.Empty);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task June2026Queries_UseCapturedVendorSignatures_AndCorrectTransportFraming (bool signed)
		{
#if NETFRAMEWORK
		using RSA key = new RSACng (4096);
#else
		using RSA key = RSA.Create (4096);
#endif
		using var handler = new ScriptedHandler (async (request, _) =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
				return Text ("""{"token":"synthetic"}""");
			if (request.RequestUri.AbsolutePath == "/tedapi/din")
				return Text (DIN);
			byte[] body = await request.Content!.ReadAsByteArrayAsync ();
			byte[] envelope = signed ? Signed.RoutableMessage.Parser.ParseFrom (body).ProtobufMessageAsBytes.ToByteArray ()
				: Graphql.Frame.Parser.ParseFrom (body).Message.ToByteArray ();
			var query = Graphql.Envelope.Parser.ParseFrom (envelope);
			Assert.That (query.Recipient.Din, Is.EqualTo (DIN));
			Assert.That (query.Graphql.QueryRequest.Format, Is.EqualTo (2));
			Assert.That (query.Graphql.QueryRequest.Signature.Length, Is.GreaterThan (64));
			Assert.That (Encoding.UTF8.GetString (query.Graphql.QueryRequest.Query.ToByteArray ()), Does.Contain ("DeviceControllerQuery"));
			Assert.That (query.Graphql.QueryRequest.VariablesJson.Value, Is.EqualTo ("{}"));
			var response = new Graphql.Envelope
				{ Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = 1, Data = STATUS } } };
			return Binary (signed
				? new Signed.RoutableMessage { ProtobufMessageAsBytes = response.ToByteString () }.ToByteArray ()
				: new Graphql.Frame { Message = response.ToByteString (), Tail = new Graphql.Tail { Value = 1 } }.ToByteArray ());
			});
		using var client = new PowerwallTedapiClient (Options () with
			{
			LocalProtocol = signed ? PowerwallLocalProtocol.TedapiSigned : PowerwallLocalProtocol.Tedapi,
			LocalQueryVersion = TedapiQueryVersion.June2026,
			Password = "synthetic", LocalSigningKey = signed ? key : null
			}, handler);
		await client.AuthenticateAsync ();
		var data = await client.GetTelemetryAsync ();
		Assert.That (data.Control!.SystemStatus!.RemainingWattHours, Is.Zero);
		Assert.That (data.Control.Islanding!.ContactorClosed, Is.False);
		}

	[TestCase (0)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task June2026Queries_RejectIncompleteResponse_WithoutFallback (int status)
		{
		using var handler = new ScriptedHandler ((_, index) => Task.FromResult (index == 1 ? Text (DIN)
			: Binary (new Graphql.Frame
				{
				Message = new Graphql.Envelope
					{ Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = status, Data = STATUS } } }.ToByteString ()
				}.ToByteArray ())));
		using var client = new PowerwallTedapiClient (Options () with { LocalQueryVersion = TedapiQueryVersion.June2026 }, handler);
		await client.AuthenticateAsync ();
		await Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.GetTelemetryAsync ());
		Assert.That (handler.Count, Is.EqualTo (2));
		}

	[TestCase ("{")]
	[TestCase ("null")]
	public async Task MalformedQuery_IsNotCached_AndCanBeRetried (string invalid)
		{
		using var handler = new ScriptedHandler ((_, index) => Task.FromResult (index == 1 ? Text (DIN)
			: QueryResponse (index == 2 ? invalid : STATUS)));
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		await Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.GetTelemetryAsync ());
		Assert.That ((await client.GetTelemetryAsync ()).Control, Is.Not.Null);
		Assert.That (handler.Count, Is.EqualTo (3));
		}

	/// <summary>Explicit setup-network followers retain separate caches, credentials and caller-owned lifetimes.</summary>
	[TestCase (TedapiQueryVersion.June2024), TestCase (TedapiQueryVersion.June2026)]
	public async Task SignedFollowers_UseOnlyTheExplicitCallerOwnedConnection (TedapiQueryVersion version)
		{
		using RSA key = RSA.Create (4096);
		var requested = new List<string> ();
		using var followerHandler = new ScriptedHandler (async (request, _) =>
			{
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("setup.test"));
			Assert.That (request.Headers.Authorization!.Scheme, Is.EqualTo ("Basic"));
			if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text ("setup--controller");
			var frame = Graphql.Frame.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			var envelope = Graphql.Envelope.Parser.ParseFrom (frame.Message);
			Assert.That (envelope.Sender.Din, Is.EqualTo ("setup--controller"));
			requested.Add (envelope.Recipient.Din);
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/device/" + envelope.Recipient.Din + "/v1"));
			const string data = """{"components":{"pch":[]}}""";
			return version == TedapiQueryVersion.June2024 ? QueryResponse (data)
				: Binary (new Graphql.Frame { Message = new Graphql.Envelope
					{ Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = 1, Data = data } } }.ToByteString () }.ToByteArray ());
			});
		using var follower = new PowerwallTedapiClient (Options () with { Host = "setup.test", LocalQueryVersion = version }, followerHandler);
		using var primaryHandler = new ScriptedHandler (async (request, _) =>
			{
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("primary.test"));
			if (request.RequestUri.AbsolutePath == "/api/login/Basic") return Text ("""{"token":"primary-only"}""");
			if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/v1r"));
			var envelope = Legacy.MessageEnvelope.Parser.ParseFrom (Signed.RoutableMessage.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ()).ProtobufMessageAsBytes);
			ByteString reply;
			if (envelope.Config is not null)
				reply = new Signed.MessageEnvelope { Filestore = new Signed.FileStoreMessages { ReadFileResponse = new Signed.FileStoreAPIReadFileResponse
					{ File = new Signed.FileStoreAPIFile { Blob = ByteString.CopyFromUtf8 ("""{"battery_blocks":[{"vin":"part--one","type":"Powerwall3"},{"vin":"part--two","type":"Powerwall3"}]}""") } } } }.ToByteString ();
			else
				{
				Assert.That (envelope.Payload.Send.Payload.Text, Does.Contain ("DeviceControllerQuery"));
				reply = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = "{}" } } }.ToByteString ();
				}
			return Binary (new Signed.RoutableMessage { ProtobufMessageAsBytes = reply }.ToByteArray ());
			});
		var options = Options () with { Host = "primary.test", LocalProtocol = PowerwallLocalProtocol.TedapiSigned,
			Password = "customer", LocalSigningKey = key, LocalFollowerConnection = follower };
		Assert.That (JsonSerializer.Serialize (options), Does.Not.Contain ("LocalFollowerConnection").And.Not.Contain ("setup.test"));
		using (var primary = new PowerwallTedapiClient (options, primaryHandler))
			{
			await primary.AuthenticateAsync ();
			Assert.Throws<PowerwallConnectionException> (() => primary.GetComponentsAsync ("part--one"));
			Assert.That (followerHandler.Count, Is.Zero, "The primary must not authenticate another connection implicitly.");
			await follower.AuthenticateAsync ();
			await primary.GetComponentsAsync ("part--one");
			await primary.GetComponentsAsync ("part--one");
			await primary.GetComponentsAsync ("part--two");
			Assert.That (primaryHandler.Count, Is.EqualTo (2));
			Assert.That (requested, Is.EqualTo (new[] { "part--one", "part--two" }));
			var snapshot = await primary.GetDeviceSnapshotAsync ();
			Assert.That (snapshot.Devices.Select (d => d.Din), Is.EqualTo (new[] { "part--one", "part--two" }));
			Assert.That (snapshot.Devices.All (d => d.Telemetry is not null && d.UnavailableReason is null), Is.True);
			Assert.That (requested.Count, Is.EqualTo (2), "Snapshot reuse must retain per-follower caches.");
			using var cancelled = new CancellationTokenSource ();
			cancelled.Cancel ();
			await Assert.CatchAsync<OperationCanceledException> (async () => await primary.GetComponentsAsync ("part--three", cancellationToken: cancelled.Token));
			Assert.That (requested.Count, Is.EqualTo (2));
			}
		await follower.GetComponentsAsync ("part--three");
		Assert.That (requested.Last (), Is.EqualTo ("part--three"), "Disposing the primary must not dispose a caller-owned follower connection.");
		}

	[TestCase (TedapiQueryVersion.June2024)]
	[TestCase (TedapiQueryVersion.June2026)]
	public async Task FollowerQuery_UsesExplicitRouteAndSeparateCache (TedapiQueryVersion version)
		{
		var requested = new List<string> ();
		using var handler = new ScriptedHandler (async (request, index) =>
			{
			if (index == 1)
				return Text (DIN);
			var frame = Graphql.Frame.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			Assert.That (frame.Tail.Value, Is.EqualTo (2));
			var envelope = Graphql.Envelope.Parser.ParseFrom (frame.Message);
			Assert.That (envelope.Sender.Din, Is.EqualTo (DIN));
			requested.Add (envelope.Recipient.Din);
			Assert.That (request.RequestUri!.AbsolutePath, Is.EqualTo ("/tedapi/device/" + envelope.Recipient.Din + "/v1"));
			const string data = """{"components":{"pch":[]}}""";
			return version == TedapiQueryVersion.June2024 ? QueryResponse (data)
				: Binary (new Graphql.Frame { Message = new Graphql.Envelope
					{ Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = 1, Data = data } } }.ToByteString () }.ToByteArray ());
			});
		using var client = new PowerwallTedapiClient (Options () with { LocalQueryVersion = version }, handler);
		await client.AuthenticateAsync ();
		await client.GetComponentsAsync ("part--followerA");
		await client.GetComponentsAsync ("part--followerA");
		await client.GetComponentsAsync ("part--followerB");
		Assert.That (requested, Is.EqualTo (new[] { "part--followerA", "part--followerB" }));
		Assert.That (handler.Count, Is.EqualTo (3));
		Assert.Throws<ArgumentException> (() => client.GetComponentsAsync ("../another-host"));
		Assert.That (handler.Count, Is.EqualTo (3));
		}

	[Test]
	public async Task SignedFollowerQuery_RejectsUnsupportedRouteWithoutFallback ()
		{
#if NETFRAMEWORK
		using RSA key = new RSACng (4096);
#else
		using RSA key = RSA.Create (4096);
#endif
		using var handler = new ScriptedHandler ((request, _) => Task.FromResult (
			request.RequestUri!.AbsolutePath == "/api/login/Basic" ? Text ("""{"token":"synthetic"}""") : Text (DIN)));
		using var client = new PowerwallTedapiClient (Options () with
			{ LocalProtocol = PowerwallLocalProtocol.TedapiSigned, Password = "synthetic", LocalSigningKey = key }, handler);
		await client.AuthenticateAsync ();
		int requests = handler.Count;
		await Assert.ThrowsAsync<PowerwallNotSupportedException> (async () => await client.GetComponentsAsync ("part--follower"));
		Assert.That (handler.Count, Is.EqualTo (requests));
		}

	/// <summary>Automatic Wi-Fi aggregation queries each configured Powerwall and never overwrites another device's readings.</summary>
	[Test]
	public async Task DeviceSnapshot_QueriesConfiguredUnitsWithSeparateIdentities ()
		{
		var queried = new List<string> ();
		using var handler = new ScriptedHandler (async (request, index) =>
			{
			if (index == 1) return Text (DIN);
			var frame = Graphql.Frame.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			var message = Legacy.MessageEnvelope.Parser.ParseFrom (frame.Message);
			if (message.Config is not null)
				return Binary (new Legacy.Message { Message_ = new Legacy.MessageEnvelope { Config = new Legacy.ConfigType
					{ Recv = new Legacy.PayloadConfigRecv { File = new Legacy.ConfigString { Text = """{"battery_blocks":[{"vin":"part--one","type":"Powerwall3"},{"vin":"part--two","type":"Powerwall3"}]}""" } } } } }.ToByteArray ());
			if (message.Payload.Send.Payload.Text.Contains ("DeviceControllerQuery"))
				return QueryResponse ("{}");
			queried.Add (message.Recipient.Din);
			int energy = message.Recipient.Din == "part--one" ? 4 : 7;
			return QueryResponse ("{\"components\":{\"bms\":[{\"signals\":[{\"name\":\"BMS_nominalEnergyRemaining\",\"value\":" + energy + "}]}]}}");
			});
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		var snapshot = await client.GetDeviceSnapshotAsync ();
		Assert.That (queried, Is.EqualTo (new[] { "part--one", "part--two" }));
		Assert.That (snapshot.Batteries!.Select (b => b.NominalEnergyRemaining), Is.EqualTo (new double?[] { 4000, 7000 }));
		Assert.That (snapshot.Devices.All (d => d.Telemetry is not null && d.UnavailableReason is null), Is.True);
		await client.GetDeviceSnapshotAsync ();
		Assert.That (queried.Count, Is.EqualTo (2), "Cached device reads should not start another set of queries.");
		}

	/// <summary>Signed LAN reports followers unavailable without issuing a routed request or substituting leader measurements.</summary>
	[Test]
	public async Task DeviceSnapshot_SignedFollowersAreExplicitlyUnavailable ()
		{
#if NETFRAMEWORK
		using RSA key = new RSACng (4096);
#else
		using RSA key = RSA.Create (4096);
#endif
		int queries = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic") return Text ("""{"token":"synthetic"}""");
			if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/v1r"));
			var envelope = Legacy.MessageEnvelope.Parser.ParseFrom (Signed.RoutableMessage.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ()).ProtobufMessageAsBytes);
			Google.Protobuf.ByteString reply;
			if (envelope.Config is not null)
				reply = new Signed.MessageEnvelope { Filestore = new Signed.FileStoreMessages { ReadFileResponse = new Signed.FileStoreAPIReadFileResponse
					{ File = new Signed.FileStoreAPIFile { Blob = ByteString.CopyFromUtf8 ("""{"battery_blocks":[{"vin":"part--follower","type":"Powerwall3"}]}""") } } } }.ToByteString ();
			else
				{
				queries++;
				Assert.That (envelope.Payload.Send.Payload.Text, Does.Contain ("DeviceControllerQuery"));
				reply = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = "{}" } } }.ToByteString ();
				}
			return Binary (new Signed.RoutableMessage { ProtobufMessageAsBytes = reply }.ToByteArray ());
			});
		using var client = new PowerwallTedapiClient (Options () with { LocalProtocol = PowerwallLocalProtocol.TedapiSigned, Password = "synthetic", LocalSigningKey = key }, handler);
		await client.AuthenticateAsync ();
		var snapshot = await client.GetDeviceSnapshotAsync ();
		Assert.That (queries, Is.EqualTo (1));
		Assert.That (snapshot.Devices.Single ().UnavailableReason, Does.Contain ("cannot route"));
		Assert.That (snapshot.Devices.Single ().Telemetry, Is.Null);
		Assert.That (snapshot.Batteries!.Single ().NominalEnergyRemaining, Is.Null);
		}

	/// <summary>A transport or payload failure in one Powerwall preserves the next device's independent result.</summary>
	[TestCase ("network"), TestCase ("timeout"), TestCase ("malformed"), TestCase ("cancel")]
	public async Task DeviceSnapshot_IsolatesFailuresButPropagatesCallerCancellation (string failure)
		{
		using var cancel = new CancellationTokenSource ();
		var queried = new List<string> ();
		using var handler = new ScriptedHandler (async (request, index) =>
			{
			if (index == 1) return Text (DIN);
			var frame = Graphql.Frame.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			var message = Legacy.MessageEnvelope.Parser.ParseFrom (frame.Message);
			if (message.Config is not null)
				return Binary (new Legacy.Message { Message_ = new Legacy.MessageEnvelope { Config = new Legacy.ConfigType
					{ Recv = new Legacy.PayloadConfigRecv { File = new Legacy.ConfigString { Text = """{"battery_blocks":[{"vin":"part--one","type":"Powerwall3"},{"vin":"part--two","type":"Powerwall3"}]}""" } } } } }.ToByteArray ());
			if (message.Payload.Send.Payload.Text.Contains ("DeviceControllerQuery")) return QueryResponse ("{}");
			queried.Add (message.Recipient.Din);
			if (message.Recipient.Din == "part--one")
				{
				if (failure == "cancel") { cancel.Cancel (); throw new OperationCanceledException (cancel.Token); }
				if (failure == "network") throw new HttpRequestException ("simulated disconnect");
				if (failure == "timeout") throw new TaskCanceledException ("simulated HTTP timeout");
				return QueryResponse ("not-json");
				}
			return QueryResponse ("""{"components":{"bms":[{"signals":[{"name":"BMS_nominalEnergyRemaining","value":7}]}]}}""");
			});
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		if (failure == "cancel")
			{
			await Assert.CatchAsync<OperationCanceledException> (async () => await client.GetDeviceSnapshotAsync (cancellationToken: cancel.Token));
			Assert.That (queried, Is.EqualTo (new[] { "part--one" }));
			return;
			}
		var snapshot = await client.GetDeviceSnapshotAsync (cancellationToken: cancel.Token);
		Assert.That (snapshot.Devices[0].Telemetry, Is.Null);
		Assert.That (snapshot.Devices[0].UnavailableReason, Is.Not.Null.And.Not.Empty);
		Assert.That (snapshot.Devices[1].Telemetry, Is.Not.Null);
		Assert.That (snapshot.Batteries![1].NominalEnergyRemaining, Is.EqualTo (7000));
		}

	/// <summary>Supplemental reads retain vendor bytes, use distinct caches and never send a mutation.</summary>
	/// <param name="protocol">Local authentication transport.</param>
	[TestCase (PowerwallLocalProtocol.Tedapi), TestCase (PowerwallLocalProtocol.TedapiSigned), TestCase (PowerwallLocalProtocol.TedapiBearer)]
	public async Task SupplementalQueries_UseExactDefinitionsAndIndependentCaches (PowerwallLocalProtocol protocol)
		{
		#if NETFRAMEWORK
		using RSA key = new RSACng (4096);
#else
		using RSA key = RSA.Create (4096);
#endif
		using var resource = typeof (Powerwall).Assembly.GetManifestResourceStream ("TeslaPowerwallLibrary.Tedapi.Protocol.Queries2026.json")!;
		using var definitions = JsonDocument.Parse (resource);
		var names = new List<string> ();
		using var handler = new ScriptedHandler (async (request, _) =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic") return Text ("""{"token":"synthetic"}""");
			if (request.RequestUri.AbsolutePath == "/tedapi/din") return Text (DIN);
			byte[] body = await request.Content!.ReadAsByteArrayAsync ();
			ByteString envelope = protocol switch
				{
				PowerwallLocalProtocol.TedapiSigned => Signed.RoutableMessage.Parser.ParseFrom (body).ProtobufMessageAsBytes,
				PowerwallLocalProtocol.TedapiBearer => Signed.AuthEnvelope.Parser.ParseFrom (body).Payload,
				_ => Graphql.Frame.Parser.ParseFrom (body).Message
				};
			var query = Graphql.Envelope.Parser.ParseFrom (envelope).Graphql.QueryRequest;
			string queryText = query.Query.ToStringUtf8 ();
			string name = new[] { "IEEE20305Query", "PinvSelfTestQuery", "ProtectionTripTestQuery" }.Single (queryText.Contains);
			names.Add (name);
			var definition = definitions.RootElement.GetProperty (name);
			Assert.That (definition.GetProperty ("text").GetString ()!.TrimStart (), Does.StartWith ("query "));
			Assert.That (query.Format, Is.EqualTo (2));
			Assert.That (BitConverter.ToString (query.Query.ToByteArray ()).Replace ("-", "").ToLowerInvariant (), Is.EqualTo (definition.GetProperty ("signed_bytes").GetString ()));
			Assert.That (BitConverter.ToString (query.Signature.ToByteArray ()).Replace ("-", "").ToLowerInvariant (), Is.EqualTo (definition.GetProperty ("code").GetString ()));
			string data = name switch
				{
				"IEEE20305Query" => """{"ieee20305":{"longFormDeviceID":"synthetic"}}""",
				"PinvSelfTestQuery" => """{"esCan":{"inverterSelfTests":{"isRunning":false}}}""",
				_ => """{"control":{"protectionTripTests":{"isRunning":false,"results":[{"status":"not-run"}]}}}"""
				};
			var response = new Graphql.Envelope { Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = 1, Data = data } } };
			return Binary (protocol switch
				{
				PowerwallLocalProtocol.TedapiSigned => new Signed.RoutableMessage { ProtobufMessageAsBytes = response.ToByteString () }.ToByteArray (),
				PowerwallLocalProtocol.TedapiBearer => new Signed.AuthEnvelope { Payload = response.ToByteString () }.ToByteArray (),
				_ => new Graphql.Frame { Message = response.ToByteString () }.ToByteArray ()
				});
			});
		using var client = new PowerwallTedapiClient (Options () with { LocalProtocol = protocol, Password = "synthetic", LocalSigningKey = key }, handler);
		await client.AuthenticateAsync ();
		for (int repeat = 0; repeat < 2; repeat++)
			{
			Assert.That ((await client.GetIeee20305Async ())!.LongFormDeviceId!.Text, Is.EqualTo ("synthetic"));
			Assert.That ((await client.GetInverterSelfTestsAsync ())!.IsRunning, Is.False);
			Assert.That ((await client.GetProtectionTestStatusAsync ())!.Results![0]!.Status!.Text, Is.EqualTo ("not-run"));
			}
		Assert.That (names, Is.EqualTo (new[] { "IEEE20305Query", "PinvSelfTestQuery", "ProtectionTripTestQuery" }));
		await client.GetIeee20305Async (force: true);
		Assert.That (names.Count, Is.EqualTo (4));
		Assert.That (names[3], Is.EqualTo ("IEEE20305Query"));
		}

	/// <summary>Partial diagnostic responses never enter the cache or turn into an empty successful result.</summary>
	[Test]
	public async Task SupplementalQueries_PartialResponseIsNotCached ()
		{
		int reads = 0;
		using var handler = new ScriptedHandler ((_, index) => Task.FromResult (index == 1 ? Text (DIN) : Binary (new Graphql.Frame
			{ Message = new Graphql.Envelope { Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse
				{ Status = ++reads == 1 ? 2 : 1, Data = """{"ieee20305":{"longFormDeviceID":"synthetic"}}""" } } }.ToByteString () }.ToByteArray ())));
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		await Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.GetIeee20305Async ());
		Assert.That ((await client.GetIeee20305Async ())!.LongFormDeviceId!.Text, Is.EqualTo ("synthetic"));
		Assert.That (reads, Is.EqualTo (2));
		}

	private static PowerwallOptions Options () => new ()
		{
		Host = "powerwall.test", GatewayPassword = "synthetic",
		LocalProtocol = PowerwallLocalProtocol.Tedapi, CacheExpireSeconds = 60, Timeout = TimeSpan.FromSeconds (2)
		};

	private static PowerwallTedapiClient Create (HttpMessageHandler handler) => new (Options (), handler);
	private static ScriptedHandler BasicHandler () => new ((_, index) => Task.FromResult (index == 1 ? Text (DIN) : QueryResponse (STATUS)));
	private static HttpResponseMessage Text (string value) => new (HttpStatusCode.OK) { Content = new StringContent (value) };
	private static HttpResponseMessage Binary (byte[] value) => new (HttpStatusCode.OK) { Content = new ByteArrayContent (value) };
	private static HttpResponseMessage QueryResponse (string json) => Binary (new Legacy.Message
		{ Message_ = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = json } } } }.ToByteArray ());

	private sealed class ScriptedHandler (Func<HttpRequestMessage, int, Task<HttpResponseMessage>> callback) : HttpMessageHandler
		{
		internal int Count { get; private set; }
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			return callback (request, ++Count);
			}
		}
	}
