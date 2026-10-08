// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Google.Protobuf;
using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;
using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;
using Graphql = TeslaPowerwallLibrary.Tedapi.Protocol.Graphql;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Exercises explicit local read failover using simulated endpoints, never real hardware.</summary>
[TestFixture]
public sealed class LocalReadFailoverTests
	{
	private RSA _key = null!;

	/// <summary>Creates one test-only signing key for the simulated LAN endpoint.</summary>
	[SetUp]
	public void SetUp ()
		{
#if NETFRAMEWORK
		_key = new RSACng (4096);
#else
		_key = RSA.Create (4096);
#endif
		}
	/// <summary>Releases the test-only signing key.</summary>
	[TearDown]
	public void TearDown () => _key.Dispose ();

	/// <summary>Reads switch only after three failures, hold the retry interval, and return to LAN on recovery.</summary>
	[TestCase ("network"), TestCase ("timeout"), TestCase ("502")]
	public async Task ReadFailures_TripAndRecoverWithoutBackgroundWork (string failure)
		{
		using var h = new Harness (_key);
		await h.ConnectAsync ();
		h.PrimaryFailure = failure;
		await Assert.CatchAsync (async () => await h.Primary.GetTelemetryAsync (true));
		await Assert.CatchAsync (async () => await h.Primary.GetTelemetryAsync (true));
		Assert.That (h.FallbackReads, Is.Zero);
		Assert.That ((await h.Primary.GetTelemetryAsync (true)).Control!.MeterAggregates![0].Watts, Is.EqualTo (20));
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.True);
		int primaryReads = h.PrimaryReads;
		await h.Primary.GetTelemetryAsync (true);
		Assert.That (h.PrimaryReads, Is.EqualTo (primaryReads));
		h.Now = 61;
		h.PrimaryFailure = null;
		Assert.That ((await h.Primary.GetTelemetryAsync (true)).Control!.MeterAggregates![0].Watts, Is.EqualTo (10));
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.False);
		Assert.That (h.FallbackReads, Is.EqualTo (2));
		}

	/// <summary>Caller cancellation, authentication, malformed data and device cooldown cannot activate fallback.</summary>
	[TestCase ("cancel"), TestCase ("401"), TestCase ("429"), TestCase ("503"), TestCase ("malformed")]
	public async Task NonTransportErrors_NeverRedirect (string failure)
		{
		using var h = new Harness (_key);
		await h.ConnectAsync ();
		h.PrimaryFailure = failure;
		using var cancel = new CancellationTokenSource ();
		if (failure == "cancel") cancel.Cancel ();
		for (int i = 0; i < 3; i++) await Assert.CatchAsync (async () => await h.Primary.GetTelemetryAsync (true, cancel.Token));
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.False);
		Assert.That (h.FallbackReads, Is.Zero);
		}

	/// <summary>Initial network failure can use the explicitly authenticated alternate and later establish LAN.</summary>
	[Test]
	public async Task ColdStart_UsesAlternateAndRecoversSameIdentity ()
		{
		using var h = new Harness (_key) { LoginFailure = true };
		await h.ConnectAsync ();
		Assert.That (h.Primary.DeviceIdentificationNumber, Is.EqualTo (Harness.Din));
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.True);
		await h.Primary.GetTelemetryAsync ();
		int reads = h.FallbackReads;
		await Assert.ThrowsAsync<PowerwallConnectionException> (async () => await h.Primary.GetBackupEventsAsync ());
		Assert.That (h.FallbackReads, Is.EqualTo (reads), "Signed-only operations must not redirect.");
		h.LoginFailure = false;
		h.Now = 61;
		await h.Primary.GetTelemetryAsync (true);
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.False);
		await h.Primary.CloseSessionAsync ();
		await Assert.ThrowsAsync<PowerwallConnectionException> (async () => await h.Primary.GetTelemetryAsync ());
		await h.Fallback.GetTelemetryAsync (true); // Closing primary does not close caller-owned fallback.
		}

	/// <summary>A recovered LAN host identifying a different system is refused before querying it.</summary>
	[Test]
	public async Task ColdRecovery_RejectsDifferentDevice ()
		{
		using var h = new Harness (_key) { LoginFailure = true };
		await h.ConnectAsync ();
		h.LoginFailure = false;
		h.PrimaryDin = "part--different";
		h.Now = 61;
		await Assert.ThrowsAsync<PowerwallInvalidConfigurationException> (async () => await h.Primary.GetTelemetryAsync (true));
		Assert.That (h.PrimaryReads, Is.Zero);
		Assert.That (h.Primary.DeviceIdentificationNumber, Is.Null);
		}

	/// <summary>Two configured connections must identify the same controller before failover can be used.</summary>
	[Test]
	public async Task InitialConnection_RejectsDifferentDevice ()
		{
		using var h = new Harness (_key) { PrimaryDin = "part--different" };
		await Assert.ThrowsAsync<PowerwallInvalidConfigurationException> (h.ConnectAsync);
		Assert.That (h.PrimaryReads + h.FallbackReads, Is.Zero);
		}

	/// <summary>Supplying a follower route alone does not opt into controller failover.</summary>
	[Test]
	public async Task DisabledFailover_LeavesOriginalBehavior ()
		{
		using var h = new Harness (_key, false);
		await h.ConnectAsync ();
		h.PrimaryFailure = "network";
		for (int i = 0; i < 4; i++) await Assert.ThrowsAsync<HttpRequestException> (async () => await h.Primary.GetTelemetryAsync (true));
		Assert.That (h.FallbackReads, Is.Zero);
		}

	/// <summary>Failed recovery waits again, and parallel reads do not each probe the failed LAN path.</summary>
	[Test]
	public async Task ConcurrentReads_UseOneRecoveryAttempt ()
		{
		using var h = new Harness (_key);
		await h.ConnectAsync ();
		h.PrimaryFailure = "network";
		for (int i = 0; i < 2; i++) await Assert.CatchAsync (async () => await h.Primary.GetTelemetryAsync (true));
		await h.Primary.GetTelemetryAsync (true);
		h.Now = 61;
		await Task.WhenAll (Enumerable.Range (0, 5).Select (_ => h.Primary.GetTelemetryAsync (true)));
		Assert.That (h.PrimaryReads, Is.EqualTo (4));
		Assert.That (h.FallbackReads, Is.EqualTo (6));
		}

	/// <summary>Every supported read uses its own protocol and returns to LAN after the retry interval.</summary>
	[Test]
	public async Task ReadRoutes_PreserveProtocolAndRecovery (
		[Values ("telemetry", "components", "configuration", "firmware", "nativeMeters")] string route,
		[Values (TedapiQueryVersion.June2024, TedapiQueryVersion.June2026)] TedapiQueryVersion version)
		{
		using var h = new Harness (_key, version: version);
		await h.ConnectAsync ();
		async Task Read ()
			{
			switch (route)
				{
				case "telemetry": Assert.That ((await h.Primary.GetTelemetryAsync (true)).Control, Is.Not.Null); break;
				case "components": Assert.That ((await h.Primary.GetComponentsAsync (force: true)).Components, Is.Not.Null); break;
				case "configuration": Assert.That ((await h.Primary.GetConfigurationAsync (true)).Din, Is.EqualTo (Harness.Din)); break;
				case "firmware": Assert.That ((await h.Primary.GetSystemInformationAsync (true)).Version!.Version, Is.EqualTo ("test-version")); break;
				case "nativeMeters": Assert.That ((await h.Primary.GetNativeMeterAggregatesAsync (true)).Site, Is.Not.Null); break;
				}
			}
		await Read ();
		h.PrimaryFailure = "network";
		for (int i = 0; i < 2; i++) await Assert.ThrowsAsync<HttpRequestException> (Read);
		await Read ();
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.True);
		int primaryReads = h.PrimaryReads;
		await Read ();
		Assert.That (h.PrimaryReads, Is.EqualTo (primaryReads));
		Assert.That (h.FallbackReads, Is.EqualTo (2));
		h.PrimaryFailure = null;
		h.Now = 61;
		await Read ();
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.False);
		Assert.That (h.PrimaryReads, Is.EqualTo (primaryReads + 1));
		}

	/// <summary>Even with explicit control permission, an unavailable LAN never redirects writes to setup Wi-Fi.</summary>
	[TestCase (false), TestCase (true)]
	public async Task ControlCommands_NeverUseFallback (bool coldStart)
		{
		using var h = new Harness (_key, allowControl: true) { LoginFailure = coldStart };
		await h.ConnectAsync ();
		h.PrimaryFailure = "network";
		if (!coldStart)
			{
			for (int i = 0; i < 2; i++) await Assert.ThrowsAsync<HttpRequestException> (async () => await h.Primary.GetTelemetryAsync (true));
			}
		await h.Primary.GetTelemetryAsync (true);
		Assert.That (h.Primary.IsUsingLocalReadFallback, Is.True);
		int alternateReads = h.FallbackReads;
		int primaryReads = h.PrimaryReads;
		await Assert.CatchAsync (async () => await h.Primary.GoOffGridAsync ());
		await Assert.CatchAsync (async () => await h.Primary.ReconnectGridAsync ());
		await Assert.CatchAsync (async () => await h.Primary.CancelMaxBackupAsync ());
		Assert.That (h.FallbackReads, Is.EqualTo (alternateReads));
		Assert.That (h.PrimaryReads, Is.EqualTo (primaryReads + (coldStart ? 0 : 3)), "Cold connections reject writes; warm ones only attempt LAN once per command.");
		}

	private sealed class Harness : IDisposable
		{
		internal const string Din = "part--controller";
		internal string PrimaryDin { get; set; } = Din;
		internal string? PrimaryFailure { get; set; }
		internal bool LoginFailure { get; set; }
		internal double Now { get; set; }
		internal int PrimaryReads { get; private set; }
		internal int FallbackReads { get; private set; }
		internal PowerwallTedapiClient Primary { get; }
		internal PowerwallTedapiClient Fallback { get; }
		internal Harness (RSA key, bool enabled = true, TedapiQueryVersion version = TedapiQueryVersion.June2024, bool allowControl = false)
			{
			Fallback = new PowerwallTedapiClient (new PowerwallOptions { Host = "setup.test", LocalProtocol = PowerwallLocalProtocol.Tedapi, GatewayPassword = "test-password", CacheExpireSeconds = 0, LocalQueryVersion = version },
				new Handler (async (request, _) =>
					{
					if (request.RequestUri!.AbsolutePath == "/tedapi/din") return Text (Din);
					if (request.RequestUri.AbsolutePath == "/api/login/Basic") return Text ("{\"token\":\"alternate\"}");
					FallbackReads++;
					return await Reply (request, 20, false, version);
					}));
			Primary = new PowerwallTedapiClient (new PowerwallOptions { Host = "lan.test", LocalProtocol = PowerwallLocalProtocol.TedapiSigned,
				Password = "customer", LocalSigningKey = key, CacheExpireSeconds = 0, LocalFollowerConnection = Fallback, EnableLocalReadFailover = enabled, LocalQueryVersion = version, AllowLocalControl = allowControl },
				new Handler (async (request, _) =>
					{
					string path = request.RequestUri!.AbsolutePath;
					if (path == "/api/login/Basic")
						{
						if (LoginFailure) throw new HttpRequestException ("LAN unavailable");
						return Text ("{\"token\":\"test\"}");
						}
					if (path == "/tedapi/din") return Text (PrimaryDin);
					PrimaryReads++;
					if (PrimaryFailure == "network") throw new HttpRequestException ("LAN unavailable");
					if (PrimaryFailure == "timeout") throw new TaskCanceledException ("Request timed out");
					if (int.TryParse (PrimaryFailure, out int status)) return new HttpResponseMessage ((HttpStatusCode)status);
					return await Reply (request, 10, true, version, PrimaryFailure == "malformed");
					}), () => Now);
			}
		internal async Task ConnectAsync () { await Fallback.AuthenticateAsync (); await Primary.AuthenticateAsync (); }
		public void Dispose () { Primary.Dispose (); Fallback.Dispose (); }
		private static HttpResponseMessage Text (string text) => new (HttpStatusCode.OK) { Content = new StringContent (text) };
		private static async Task<HttpResponseMessage> Reply (HttpRequestMessage request, int watts, bool signed, TedapiQueryVersion version, bool malformed = false)
			{
			if (request.RequestUri!.AbsolutePath == "/api/meters/aggregates") return Text ("{\"site\":{\"instant_power\":0}}");
			byte[] body = await request.Content!.ReadAsByteArrayAsync ();
			ByteString incoming = signed ? Signed.RoutableMessage.Parser.ParseFrom (body).ProtobufMessageAsBytes
				: Graphql.Frame.Parser.ParseFrom (body).Message;
			var query = Legacy.MessageEnvelope.Parser.ParseFrom (incoming);
			Assert.That (query.Recipient.Din, Is.EqualTo (Din));
			ByteString reply;
			if (query.Config is not null)
				{
				string json = "{\"vin\":\"" + Din + "\"}";
				reply = signed ? new Signed.MessageEnvelope { Filestore = new Signed.FileStoreMessages { ReadFileResponse = new Signed.FileStoreAPIReadFileResponse
					{ File = new Signed.FileStoreAPIFile { Blob = ByteString.CopyFromUtf8 (json) } } } }.ToByteString ()
					: new Legacy.Message { Message_ = new Legacy.MessageEnvelope { Config = new Legacy.ConfigType
					{ Recv = new Legacy.PayloadConfigRecv { File = new Legacy.ConfigString { Text = json } } } } }.ToByteString ();
				}
			else if (query.Firmware is not null)
				{
				Assert.That (query.Firmware.IdCase, Is.EqualTo (Legacy.FirmwareType.IdOneofCase.Request));
				var envelope = new Legacy.MessageEnvelope { Firmware = new Legacy.FirmwareType { System = new Legacy.FirmwarePayload
					{ Din = Din, Version = new Legacy.FirmwareVersion { Text = "test-version" } } } };
				reply = signed ? envelope.ToByteString () : new Legacy.Message { Message_ = envelope }.ToByteString ();
				}
			else
				{
				string json = malformed ? "invalid json" : "{\"components\":{},\"control\":{\"meterAggregates\":[{\"location\":\"SOLAR\",\"realPowerW\":" + watts + "}]}}";
				if (version == TedapiQueryVersion.June2026)
					{
					Assert.That (Graphql.Envelope.Parser.ParseFrom (incoming).Graphql.QueryRequest.Format, Is.EqualTo (2));
					var envelope = new Graphql.Envelope { Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = 1, Data = json } } };
					reply = signed ? envelope.ToByteString () : new Graphql.Frame { Message = envelope.ToByteString () }.ToByteString ();
					}
				else
					{
					var envelope = new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = json } } };
					reply = signed ? envelope.ToByteString () : new Legacy.Message { Message_ = envelope }.ToByteString ();
					}
				}
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new ByteArrayContent (signed
				? new Signed.RoutableMessage { ProtobufMessageAsBytes = reply }.ToByteArray () : reply.ToByteArray ()) };
			}
		}

	private sealed class Handler (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
		{
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken) => send (request, cancellationToken);
		}
	}
