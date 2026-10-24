// Copyright (c) 2026 Neil Colvin. MIT licensed; see LICENSE.
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Google.Protobuf;

using TeslaPowerwallLibrary.Tedapi;

using Graphql = TeslaPowerwallLibrary.Tedapi.Protocol.Graphql;
using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;
namespace TeslaPowerwallLibrary.Tests;

public sealed partial class TedapiTests
	{
	/// <summary>Leaves delivery time after RSA-4096 processing on slower supported runtimes.</summary>
	[TestCase (TedapiQueryVersion.June2024), TestCase (TedapiQueryVersion.June2026)]
	public async Task SignedRequestsAllowEmbeddedSigningTimeBeforeDelivery (TedapiQueryVersion version)
		{
		using RSA key = TestRsaKey.Create ();
		long before = 0;
		int posts = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
		{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
				return Text ("""{"token":"synthetic"}""");
			if (request.RequestUri.AbsolutePath == "/tedapi/din")
				return Text (DIN);
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/v1r"));
			posts++;
			var message = Signed.RoutableMessage.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			long delivered = DateTimeOffset.UtcNow.ToUnixTimeSeconds ();
			long expiry = message.SignatureData.RsaData.ExpiresAt;
			Assert.That (expiry, Is.InRange (before + 60, delivered + 60), "The finite validity window includes signing and delivery, separately from HTTP timeout.");
			Assert.That (expiry - delivered, Is.GreaterThan (30), "Measured embedded RSA signing must leave sufficient time to deliver this request.");
			var response = version == TedapiQueryVersion.June2026
		? new Graphql.Envelope { Graphql = new Graphql.GraphQLMessages { QueryResponse = new Graphql.QueryResponse { Status = 1, Data = STATUS } } }.ToByteString ()
		: new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = STATUS } } }.ToByteString ();
			return Binary (new Signed.RoutableMessage { ProtobufMessageAsBytes = response }.ToByteArray ());
		});
		using var client = new PowerwallTedapiClient (Options () with
			{
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned,
			LocalQueryVersion = version,
			Password = "synthetic",
			LocalSigningKey = key
			}, handler);
		await client.AuthenticateAsync ();
		before = DateTimeOffset.UtcNow.ToUnixTimeSeconds ();
		Assert.That ((await client.GetTelemetryAsync ()).Control, Is.Not.Null);
		Assert.That (posts, Is.EqualTo (1));
		}
	}