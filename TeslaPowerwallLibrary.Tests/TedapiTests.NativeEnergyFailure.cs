// Copyright (c) 2026 Neil Colvin.
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
	/// <summary>A native-meter read failure cannot manufacture a successful zero-counter snapshot, and the next read can recover.</summary>
	/// <param name="protocol">The local authentication protocol.</param>
	/// <param name="failure">The simulated failed fetch.</param>
	[TestCase (PowerwallLocalProtocol.Tedapi, "http")]
	[TestCase (PowerwallLocalProtocol.Tedapi, "transport")]
	[TestCase (PowerwallLocalProtocol.Tedapi, "json")]
	[TestCase (PowerwallLocalProtocol.TedapiBearer, "http")]
	[TestCase (PowerwallLocalProtocol.TedapiBearer, "transport")]
	[TestCase (PowerwallLocalProtocol.TedapiBearer, "json")]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, "http")]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, "transport")]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, "json")]
	public async Task NativeLifetimeCounters_SuccessFailureRecoveryNeverSubstitutesZero (PowerwallLocalProtocol protocol, string failure)
		{
		using RSA key = TestRsaKey.Create ();
		int meterReads = 0;
		using var handler = new ScriptedHandler (async (request, _) =>
			{
				if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
					return Text ("""{"token":"synthetic-token"}""");
				if (request.RequestUri.AbsolutePath == "/tedapi/din")
					return Text (DIN);
				if (request.RequestUri.AbsolutePath != "/api/meters/aggregates")
					return WrapLegacyResponse (protocol, new Legacy.MessageEnvelope { Payload = new Legacy.QueryType { Recv = new Legacy.PayloadString { Text = STATUS } } });
				meterReads++;
				if (meterReads == 2)
					{
					if (failure == "transport")
						throw new HttpRequestException ("Synthetic native meter outage.");
					return failure == "json"
						? Text ("malformed-json")
						: new HttpResponseMessage (HttpStatusCode.InternalServerError) { Content = new StringContent ("Synthetic native meter outage.") };
					}

				await Task.Yield ();
				return Text (meterReads == 1
					? """{"site":{"energy_imported":12345.75,"energy_exported":100},"battery":{"energy_imported":4614110,"energy_exported":4517355}}"""
					: """{"site":{"energy_imported":12346.75,"energy_exported":101},"battery":{"energy_imported":4614111,"energy_exported":4517356}}""");
			});
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = "powerwall.test",
			GatewayPassword = "label-abcde",
			Password = "synthetic-customer",
			LocalProtocol = protocol,
			LocalSigningKey = key,
			CacheExpireSeconds = 0
			}, handler);
		await client.AuthenticateAsync ();
		MeterAggregates before = await client.GetNativeMeterAggregatesAsync ();
		Assert.That (before.Site!.EnergyImported, Is.EqualTo (12345.75));
		Assert.That (before.Battery!.EnergyExported, Is.EqualTo (4517355));
		if (failure == "transport")
			await Assert.ThatAsync (() => client.GetNativeMeterAggregatesAsync (), Throws.TypeOf<HttpRequestException> ());
		else
			await Assert.ThatAsync (() => client.GetNativeMeterAggregatesAsync (), Throws.InstanceOf<PowerwallConnectionException> ());
		Assert.That (before.Site.EnergyImported, Is.EqualTo (12345.75), "Failed reads must not mutate previously returned counters.");
		Assert.That (before.Battery.EnergyExported, Is.EqualTo (4517355));
		MeterAggregates recovered = await client.GetNativeMeterAggregatesAsync ();
		Assert.That (recovered.Site!.EnergyImported, Is.EqualTo (12346.75));
		Assert.That (recovered.Battery!.EnergyExported, Is.EqualTo (4517356));
		Assert.That (recovered.Site.EnergyExported, Is.EqualTo (101));
		Assert.That (recovered.Battery.EnergyImported, Is.EqualTo (4614111));
		Assert.That (meterReads, Is.EqualTo (3), "The failure must not wedge the read gate or hide recovery behind a cache.");
		}
	}