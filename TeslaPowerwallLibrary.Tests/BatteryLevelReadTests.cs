// Copyright (c) 2026 Neil Colvin. MIT License; see TeslaPowerwallLibrary/LICENSE.
using System.Net;
using System.Net.Http;
using System.Text.Json;
using TeslaPowerwallLibrary.Local;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Fresh battery reads retain validation, scaling and cache ownership in the library.</summary>
[TestFixture]
public sealed class BatteryLevelReadTests
	{
	/// <summary>Both public APIs use the same reserve floor without extra HTTP observations.</summary>
	[TestCase (0d, 0d)]
	[TestCase (4.99d, 0d)]
	[TestCase (5d, 0d)]
	[TestCase (5.01d, 0.0105263157894737d)]
	[TestCase (75d, 73.6842105263158d)]
	[TestCase (100d, 100d)]
	public async Task ValidatedReadPreservesRawAndSharesLegacyScale (double raw, double scaled)
		{
		using var fixture = await Fixture.CreateAsync (JsonSerializer.Serialize (new { percentage = raw }));
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (), Is.EqualTo (raw));
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (scale: true), Is.EqualTo (scaled).Within (1e-10));
		Assert.That (await fixture.Facade.LevelAsync (scale: true), Is.EqualTo (scaled).Within (1e-10));
		Assert.That (fixture.Handler.Reads, Is.EqualTo (1));
		}

	/// <summary>A forced read bypasses a valid cached measurement and replaces it for later projections.</summary>
	[TestCase (true)]
	[TestCase (false)]
	public async Task ForceReadsOnceAndRefreshesSharedCache (bool scale)
		{
		using var fixture = await Fixture.CreateAsync ("{\"percentage\":75}");
		double initial = scale ? 73.6842105263158d : 75d;
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (scale: scale), Is.EqualTo (initial).Within (1e-10));
		fixture.Handler.Payload = "{\"percentage\":0}";
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (scale: scale), Is.EqualTo (initial).Within (1e-10));
		Assert.That (fixture.Handler.Reads, Is.EqualTo (1));
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (scale: scale, force: true), Is.Zero);
		Assert.That (await fixture.Facade.LevelAsync (scale: scale), Is.Zero);
		Assert.That (fixture.Handler.Reads, Is.EqualTo (2), "Force must add exactly one observation and refresh the existing cache.");
		}

	/// <summary>Unusable fresh responses cannot become a genuine zero battery reading.</summary>
	[TestCase (null)]
	[TestCase ("")]
	[TestCase (" ")]
	[TestCase ("null")]
	[TestCase ("{}")]
	[TestCase ("[]")]
	[TestCase ("not JSON")]
	[TestCase ("{\"percentage\":null}")]
	[TestCase ("{\"percentage\":\"0\"}")]
	[TestCase ("{\"percentage\":\"NaN\"}")]
	[TestCase ("{\"percentage\":true}")]
	[TestCase ("{\"percentage\":1e400}")]
	public async Task InvalidFreshResponseRemainsUnavailable (string? payload)
		{
		using var fixture = await Fixture.CreateAsync ("{\"percentage\":75}");
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (), Is.EqualTo (75));
		fixture.Handler.Payload = payload;
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (scale: true, force: true), Is.Null);
		Assert.That (fixture.Handler.Reads, Is.EqualTo (2));
		}

	/// <summary>The released legacy missing-field and numeric-string behavior remains compatible.</summary>
	[TestCase ("{}", 0d)]
	[TestCase ("{\"percentage\":\"75\"}", 75d)]
	public async Task LegacyReadRetainsItsPublishedParsingContract (string payload, double expected)
		{
		using var fixture = await Fixture.CreateAsync (payload);
		Assert.That (await fixture.Facade.LevelAsync (), Is.EqualTo (expected));
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (), Is.Null);
		}

	/// <summary>Cancellation cannot turn a cached reading into evidence of a successful fresh observation.</summary>
	[Test]
	public async Task CancellationPropagatesBeforeCachedOrForcedReads ()
		{
		using var fixture = await Fixture.CreateAsync ("{\"percentage\":75}");
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (), Is.EqualTo (75));
		using var canceled = new CancellationTokenSource ();
		canceled.Cancel ();
		await Assert.ThrowsAsync<OperationCanceledException> (async () => await fixture.Facade.GetBatteryLevelAsync (cancellationToken: canceled.Token));
		await Assert.ThrowsAsync<OperationCanceledException> (async () => await fixture.Facade.GetBatteryLevelAsync (force: true, cancellationToken: canceled.Token));
		Assert.That (fixture.Handler.Reads, Is.EqualTo (1));
		}

	/// <summary>A failed fresh observation cannot fall back to the cached battery percentage.</summary>
	[Test]
	public async Task FreshTransportFailureRemainsUnavailableInsteadOfUsingCache ()
		{
		using var fixture = await Fixture.CreateAsync ("{\"percentage\":75}");
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (), Is.EqualTo (75));
		fixture.Handler.Fail = true;
		Assert.That (await fixture.Facade.GetBatteryLevelAsync (force: true), Is.Null);
		Assert.That (fixture.Handler.Reads, Is.EqualTo (2));
		}

	private sealed class Fixture : IDisposable
		{
		internal BatteryHandler Handler { get; } = new ();
		internal Powerwall Facade { get; } = new (new PowerwallOptions { Host = "battery.test" });
		private PowerwallLocalClient? _transport;
		internal static async Task<Fixture> CreateAsync (string payload)
			{
			var fixture = new Fixture ();
			fixture.Handler.Payload = payload;
			fixture._transport = new PowerwallLocalClient (new PowerwallOptions
				{ Host = "battery.test", AuthMode = "token", CacheExpireSeconds = 60, NoLocalSessionPersistence = true }, fixture.Handler);
			await fixture._transport.AuthenticateAsync ();
			OperationRegressionTests.SetField (fixture.Facade, "_client", fixture._transport);
			return fixture;
			}
		public void Dispose () { Facade.Dispose (); _transport?.Dispose (); Handler.Dispose (); }
		}

	private sealed class BatteryHandler : HttpMessageHandler
		{
		internal string? Payload { get; set; }
		internal int Reads { get; private set; }
		internal bool Fail { get; set; }
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("battery.test"));
			if (request.RequestUri.AbsolutePath == "/api/login/Basic")
				return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent ("{\"token\":\"synthetic\"}") });
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/api/system_status/soe"));
			Reads++;
			if (Fail) throw new HttpRequestException ("Synthetic battery read failure.");
			return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (Payload ?? "null") });
			}
		}
	}
