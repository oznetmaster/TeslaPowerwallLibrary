// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;
using TeslaPowerwallLibrary.Tedapi;

using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;

namespace TeslaPowerwallLibrary.Tests;

public sealed partial class TedapiTests
	{
	[Test]
	public async Task ReauthenticationRejected_InvalidatesSessionAndCachedTelemetry ()
		{
		int probes = 0, reads = 0;
		using var handler = new ScriptedHandler ((request, _) =>
			{
				if (request.RequestUri!.AbsolutePath == "/tedapi/din")
					return Task.FromResult (++probes == 1 ? Text (DIN) : new HttpResponseMessage (HttpStatusCode.Unauthorized));
				reads++;
				return Task.FromResult (QueryResponse (STATUS));
			});
		using var client = Create (handler);
		await client.AuthenticateAsync ();
		await client.GetTelemetryAsync ();
		await Assert.ThatAsync (() => client.AuthenticateAsync (), Throws.InstanceOf<PowerwallConnectionException> ());
		Assert.That (client.DeviceIdentificationNumber, Is.Null);
		await Assert.ThatAsync (() => client.GetTelemetryAsync (), Throws.InstanceOf<PowerwallConnectionException> ());
		Assert.That (reads, Is.EqualTo (1));
		}

	[TestCase (PowerwallLocalProtocol.Tedapi)]
	[TestCase (PowerwallLocalProtocol.TedapiBearer)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned)]
	public async Task FailedReauthentication_KeepsThePreviousWorkingSession (PowerwallLocalProtocol protocol)
		{
		using RSA key = TestRsaKey.Create ();
		int logins = 0, probes = 0, reads = 0;
		bool usesToken = protocol != PowerwallLocalProtocol.Tedapi;
		using var handler = new ScriptedHandler ((request, _) =>
			{
				string path = request.RequestUri!.AbsolutePath;
				if (path == "/api/login/Basic")
					return Task.FromResult (Text (JsonSerializer.Serialize (new { token = "session-" + ++logins })));
				if (path == "/tedapi/din")
					{
					probes++;
					if (usesToken)
						Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo ("session-" + probes));
					return Task.FromResult (probes == 2 ? new HttpResponseMessage (HttpStatusCode.InternalServerError) : Text (DIN));
					}
				if (usesToken)
					Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo (probes == 2 ? "session-1" : "session-3"));
				reads++;
				return Task.FromResult (WrapLegacyResponse (protocol, new Legacy.MessageEnvelope
					{
					Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = STATUS } }
					}));
			});
		using var client = new PowerwallTedapiClient (Options () with
			{
			LocalProtocol = protocol, Password = "synthetic", LocalSigningKey = key
			}, handler);

		await client.AuthenticateAsync ();
		await Assert.ThatAsync (() => client.AuthenticateAsync (), Throws.InstanceOf<PowerwallConnectionException> ());
		Assert.That (client.DeviceIdentificationNumber, Is.EqualTo (DIN));
		Assert.That ((await client.GetTelemetryAsync (force: true)).Control, Is.Not.Null);
		await client.AuthenticateAsync ();
		Assert.That ((await client.GetTelemetryAsync ()).Control, Is.Not.Null);
		Assert.That (reads, Is.EqualTo (2), "Successful reauthentication invalidates the earlier session's readings.");
		}

	[TestCase (PowerwallLocalProtocol.Tedapi)]
	[TestCase (PowerwallLocalProtocol.TedapiBearer)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned)]
	public async Task Status_UsesNativeCustomerUptimeWhenSyncUptimeIsMissing (PowerwallLocalProtocol protocol)
		{
		using RSA key = TestRsaKey.Create ();
		int statusReads = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
				switch (request.RequestUri!.AbsolutePath)
					{
					case "/api/login/Basic":
						var login = JsonSerializer.Deserialize<LocalLoginRequest> (await request.Content!.ReadAsStringAsync ())!;
						return Text (login.Username == "installer" ? """{"token":"installer-token"}""" : """{"token":"customer-token"}""");
					case "/tedapi/din":
						return Text (DIN);
					case "/api/status":
						statusReads++;
						Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo ("customer-token"));
						return Text ("""{"start_time":"2026-11-22 04:04:05 +0000","up_time_seconds":"2h3m4s","version":"do not replace firmware"}""");
					}
				var envelope = DecodeLegacyRequest (protocol, await request.Content!.ReadAsByteArrayAsync ());
				return WrapLegacyResponse (protocol, envelope.Firmware is not null
					? new Legacy.MessageEnvelope
						{
						Firmware = new Legacy.FirmwareType
							{
							System = new Legacy.FirmwarePayload { Din = DIN, Version = new Legacy.FirmwareVersion { Text = "26.34.2" } }
							}
						}
					: new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = STATUS } } });
			});
		using var client = new PowerwallTedapiClient (Options () with
			{
			LocalProtocol = protocol, Password = "synthetic", LocalSigningKey = key, CacheExpireSeconds = int.MaxValue
			}, handler);

		await client.AuthenticateAsync ();
		for (int read = 0; read < 2; read++)
			{
			var status = JsonSerializer.Deserialize<GatewayStatus> ((await client.PollAsync ("/api/status"))!)!;
			Assert.That (status.StartTime, Is.EqualTo ("2026-11-22 04:04:05 +0000"));
			Assert.That (status.UpTimeSeconds, Is.EqualTo ("2h3m4s"));
			Assert.That (status.Version, Is.EqualTo ("26.34.2"));
			}
		Assert.That (statusReads, Is.EqualTo (1), "Native uptime should use the ordinary read cache.");
		}
	}
