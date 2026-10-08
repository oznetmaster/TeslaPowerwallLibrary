// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Protects released power contracts and local defaults while testing additive nullable readings.</summary>
[TestFixture]
public sealed class PowerCompatibilityTests
	{
	/// <summary>Existing source and serializers retain non-nullable doubles and zero defaults.</summary>
	[Test]
	public void ReleasedPowerSnapshot_RetainsDoubleContract ()
		{
		PowerSnapshot old = JsonSerializer.Deserialize<PowerSnapshot> ("{}")!;
		double site = old.Site, solar = old.Solar, battery = old.Battery, load = old.Load;
		Assert.That (new[] { site, solar, battery, load }, Is.All.Zero);
		Assert.That (JsonSerializer.Serialize (old), Is.EqualTo ("{\"site\":0,\"solar\":0,\"battery\":0,\"load\":0}"));
		}

	/// <summary>Default serializer attributes preserve actual zero and signs without inventing absent readings.</summary>
	[Test]
	public void NullablePowerReadings_PreserveWireNamesAndMissingValues ()
		{
		var readings = JsonSerializer.Deserialize<PowerReadings> ("""{"site":0,"battery":-2400,"load":1234.5}""")!;
		Assert.That (readings.Site, Is.Zero);
		Assert.That (readings.Solar, Is.Null);
		Assert.That (readings.Battery, Is.EqualTo (-2400));
		Assert.That (readings.Load, Is.EqualTo (1234.5));
		using var json = JsonDocument.Parse (JsonSerializer.Serialize (readings));
		Assert.That (json.RootElement.GetProperty ("solar").ValueKind, Is.EqualTo (JsonValueKind.Null));
		Assert.That (json.RootElement.GetProperty ("site").GetDouble (), Is.Zero);
		}

	/// <summary>The facade exposes both contracts against the same partial response and cached transport.</summary>
	[TestCase ("{}", null, null)]
	[TestCase ("{\"site\":{\"instant_power\":0},\"battery\":{\"instant_power\":-250}}", 0d, -250d)]
	public async Task Facade_PreservesLegacyAndNullableSemantics (string response, double? site, double? battery)
		{
		using var handler = new TestHandler (response);
		using var client = new PowerwallLocalClient (new PowerwallOptions
			{ Host = "powerwall.test", AuthMode = "token", NoLocalSessionPersistence = true, AllowLocalControl = false }, handler);
		await client.AuthenticateAsync ();
		using var facade = new Powerwall (new PowerwallOptions { Host = "powerwall.test" });
		OperationRegressionTests.SetField (facade, "_client", client);
		PowerSnapshot old = await facade.PowerAsync ();
		PowerReadings current = await facade.GetPowerReadingsAsync ();
		Assert.That (old.Site, Is.EqualTo (site ?? 0));
		Assert.That (old.Battery, Is.EqualTo (battery ?? 0));
		Assert.That (old.Solar, Is.Zero);
		Assert.That (old.Load, Is.Zero);
		Assert.That (current.Site, Is.EqualTo (site));
		Assert.That (current.Battery, Is.EqualTo (battery));
		Assert.That (current.Solar, Is.Null);
		Assert.That (current.Load, Is.Null);
		Assert.That (await facade.SolarAsync (), Is.Zero, "The existing single-flow default must remain compatible too.");
		Assert.That (await facade.SolarAsync (verbose: true), Is.Null);
		Assert.That (handler.Reads, Is.EqualTo (1), "Both projections share the existing response cache.");
		using var canceled = new CancellationTokenSource ();
		canceled.Cancel ();
		Assert.That (async () => await facade.GetPowerReadingsAsync (canceled.Token), Throws.InstanceOf<OperationCanceledException> ());
		}

	/// <summary>Existing gateway callers retain controls and persistence; new TEDAPI protocols require opt-in.</summary>
	[TestCase (PowerwallLocalProtocol.Gateway, true, false)]
	[TestCase (PowerwallLocalProtocol.Tedapi, false, true)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, false, true)]
	[TestCase (PowerwallLocalProtocol.TedapiBearer, false, true)]
	public void ProtocolDefaults_AreCompatibleAndExplicit (PowerwallLocalProtocol protocol, bool control, bool noPersistence)
		{
		var options = new PowerwallOptions { LocalProtocol = protocol };
		Assert.That (options.AllowLocalControl, Is.EqualTo (control));
		Assert.That (options.NoLocalSessionPersistence, Is.EqualTo (noPersistence));
		var readOnly = options with { AllowLocalControl = false, NoLocalSessionPersistence = true };
		Assert.That (readOnly.AllowLocalControl, Is.False);
		Assert.That (readOnly.NoLocalSessionPersistence, Is.True);
		Assert.That ((readOnly with { LocalProtocol = PowerwallLocalProtocol.Gateway }).AllowLocalControl, Is.False);
		Assert.That ((new PowerwallOptions () with { LocalProtocol = PowerwallLocalProtocol.TedapiSigned }).AllowLocalControl, Is.False);
		}

	/// <summary>Both released construction paths reuse session caches and permit explicit writes without new options.</summary>
	[TestCase (false)]
	[TestCase (true)]
	public async Task ClassicGateway_RetainsSessionAndWriteDefaults (bool legacyConstructor)
		{
		string path = Path.Combine (Path.GetTempPath (), Guid.NewGuid () + ".json");
		try
			{
			for (int connection = 0; connection < 2; connection++)
				{
				using var handler = new TestHandler ("{}");
				using var client = legacyConstructor
					? new PowerwallLocalClient ("powerwall.test", "synthetic", "unit@example.test", "Europe/London", TimeSpan.FromSeconds (5), 5, "token", path)
					: new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", Password = "synthetic", AuthMode = "token", CacheFile = path }, handler);
				if (legacyConstructor)
					OperationRegressionTests.SetField (client, "_providedHandler", handler);
				await client.AuthenticateAsync ();
				Assert.That (handler.Logins, Is.EqualTo (connection == 0 ? 1 : 0));
				Assert.That (File.Exists (path), Is.True);
				await client.PostAsync ("/api/operation", new { backup_reserve_percent = 0 });
				Assert.That (handler.Writes, Is.EqualTo (1));
				}
			}
		finally { File.Delete (path); }
		}

	/// <summary>Explicit memory-only, read-only gateway sessions ignore old caches and reject writes.</summary>
	[Test]
	public async Task ClassicGateway_RespectsExplicitReadOnlyAndMemoryOnly ()
		{
		string path = Path.Combine (Path.GetTempPath (), Guid.NewGuid () + ".json");
		const string cached = "{\"Authorization\":\"Bearer old-synthetic\"}";
		try
			{
			File.WriteAllText (path, cached);
			using var handler = new TestHandler ("{}");
			using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", AuthMode = "token",
				CacheFile = path, NoLocalSessionPersistence = true, AllowLocalControl = false }, handler);
			await client.AuthenticateAsync ();
			Assert.That (handler.Logins, Is.EqualTo (1));
			await Assert.ThrowsAsync<PowerwallNotSupportedException> (async () => await client.PostAsync ("/api/operation", new { }));
			Assert.That (handler.Writes, Is.Zero);
			Assert.That (File.ReadAllText (path), Is.EqualTo (cached));
			}
		finally { File.Delete (path); }
		}

	/// <summary>Supplies synthetic local responses and rejects accidental external requests.</summary>
	private sealed class TestHandler (string reading) : HttpMessageHandler
		{
		/// <summary>Number of authentication requests.</summary>
		internal int Logins { get; private set; }
		/// <summary>Number of explicit configuration writes.</summary>
		internal int Writes { get; private set; }
		/// <summary>Number of meter reads.</summary>
		internal int Reads { get; private set; }
		/// <inheritdoc/>
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("powerwall.test"));
			string body;
			if (request.RequestUri.AbsolutePath == "/api/login/Basic")
				{
				Logins++;
				body = "{\"token\":\"synthetic\"}";
				}
			else if (request.Method == HttpMethod.Post)
				{
				Writes++;
				body = "{}";
				}
			else
				{
				Reads++;
				body = reading;
				}
			return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (body) });
			}
		}
	}
