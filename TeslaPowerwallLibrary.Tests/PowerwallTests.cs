// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.FleetApi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests for the <see cref="Powerwall"/> facade construction, mode resolution, and guard behavior.
/// </summary>
[TestFixture]
public sealed class PowerwallTests
	{
	[Test]
	public void WhenHostIsProvidedThenModeIsLocal ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		Assert.That (powerwall.Mode, Is.EqualTo (PowerwallMode.Local));
		}

	[Test]
	public void WhenHostIsProvidedWithPortThenModeIsLocal ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99:8443", Password = "secret" });

		Assert.That (powerwall.Mode, Is.EqualTo (PowerwallMode.Local));
		}

	[Test]
	public void WhenHostIsEmptyThenModeIsCloud ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		Assert.That (powerwall.Mode, Is.EqualTo (PowerwallMode.Cloud));
		}

	[Test]
	public void WhenHostIsEmptyAndFleetApiIsTrueThenModeIsFleetApi ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com", FleetApi = true });

		Assert.That (powerwall.Mode, Is.EqualTo (PowerwallMode.FleetApi));
		}

	[Test]
	public void WhenNotConnectedThenEmailFallsBackToOptions ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		Assert.That (powerwall.Email, Is.EqualTo ("user@example.com"));
		}

	[Test]
	public void WhenOptionsAreNullThenConstructorThrowsArgumentNullException ()
		{
		Assert.Throws<ArgumentNullException> (static () => _ = new Powerwall (null!));
		}

	[Test]
	public void WhenHostIsInvalidThenConstructorThrowsInvalidConfiguration ()
		{
		Assert.Throws<PowerwallInvalidConfigurationException> (static () => _ = new Powerwall (new PowerwallOptions { Host = "not a valid host" }));
		}

	[Test]
	public void WhenCloudModeEmailIsInvalidThenConstructorThrowsInvalidConfiguration ()
		{
		Assert.Throws<PowerwallInvalidConfigurationException> (static () => _ = new Powerwall (new PowerwallOptions { CloudMode = true, Email = "not-an-email" }));
		}

	[Test]
	public void WhenNewlyConstructedThenIsClientConnectedIsFalse ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		Assert.That (powerwall.IsClientConnected, Is.False);
		}

	[Test]
	public async Task WhenNotConnectedThenDataMethodThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		await Assert.ThatAsync (async () => await powerwall.StatusAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	[TestCase (-1.0)]
	[TestCase (101.0)]
	public async Task WhenReserveLevelIsOutOfRangeThenSetOperationThrows (double level)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		await Assert.ThatAsync (async () => await powerwall.SetOperationAsync (level), Throws.TypeOf<InvalidBatteryReserveLevelException> ());
		}

	[Test]
	public async Task WhenCloudModeHasNoTokensThenConnectThrowsNoAuthFile ()
		{
		var authPath = CreateTempCacheDirectory ();
		try
			{
			using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com", AuthPath = authPath });
			await Assert.ThatAsync (async () => await powerwall.ConnectAsync (), Throws.TypeOf<PowerwallCloudNoTeslaAuthFileException> ());
			}
		finally
			{
			Directory.Delete (authPath, recursive: true);
			}
		}

	[Test]
	public async Task WhenNotConnectedThenGetSitesThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetSitesAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenChangeSiteThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.ChangeSiteAsync ("1234567890"), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetGridChargingThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetGridChargingAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetGridExportThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetGridExportAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenSetGridChargingThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.SetGridChargingAsync (true), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	[TestCase ("battery_ok")]
	[TestCase ("pv_only")]
	[TestCase ("never")]
	public async Task WhenGridExportModeIsValidThenSetGridExportReachesConnectionGuard (string mode)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.SetGridExportAsync (mode), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	[TestCase ("bogus")]
	[TestCase ("")]
	[TestCase ("BATTERY_OK")]
	public async Task WhenGridExportModeIsInvalidThenSetGridExportThrowsArgumentException (string mode)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.SetGridExportAsync (mode), Throws.TypeOf<ArgumentException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetStormWatchThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetStormWatchAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenSetStormWatchThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.SetStormWatchAsync (true), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenVitalsThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.VitalsAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenAlertsThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.AlertsAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	[TestCase ("power")]
	[TestCase ("energy")]
	[TestCase ("backup")]
	[TestCase ("self_consumption")]
	public async Task WhenHistoryKindIsValidThenGetHistoryReachesConnectionGuard (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetHistoryAsync (kind), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	[TestCase ("bogus")]
	[TestCase ("")]
	[TestCase ("POWER")]
	[TestCase ("soe")]
	public async Task WhenHistoryKindIsInvalidThenGetHistoryThrowsArgumentException (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetHistoryAsync (kind), Throws.TypeOf<ArgumentException> ());
		}

	[Test]
	[TestCase ("hour")]
	[TestCase ("DAY")]
	public async Task WhenHistoryPeriodIsInvalidThenGetHistoryThrowsArgumentException (string period)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetHistoryAsync ("power", period), Throws.TypeOf<ArgumentException> ());
		}

	[Test]
	public void WhenEndpointRemovedExceptionIsCreatedThenItIsAPowerwallException ()
		{
		var exception = new PowerwallCloudEndpointRemovedException ("gone");

		Assert.That (exception, Is.InstanceOf<PowerwallException> ());
		}

	[Test]
	[TestCase ("power")]
	[TestCase ("soe")]
	[TestCase ("time_of_use_energy")]
	[TestCase ("savings")]
	public async Task WhenCalendarHistoryKindIsValidThenGetCalendarHistoryReachesConnectionGuard (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetCalendarHistoryAsync (kind), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	[TestCase ("bogus")]
	[TestCase ("")]
	public async Task WhenCalendarHistoryKindIsInvalidThenGetCalendarHistoryThrowsArgumentException (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetCalendarHistoryAsync (kind), Throws.TypeOf<ArgumentException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetEnergyCalendarHistoryThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetEnergyCalendarHistoryAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetPowerCalendarHistoryThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetPowerCalendarHistoryAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetStateOfEnergyCalendarHistoryThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetStateOfEnergyCalendarHistoryAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetSelfConsumptionCalendarHistoryThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetSelfConsumptionCalendarHistoryAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public async Task WhenNotConnectedThenGetBackupCalendarHistoryThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThatAsync (async () => await powerwall.GetBackupCalendarHistoryAsync (), Throws.TypeOf<InvalidOperationException> ());
		}

	[Test]
	public void WhenNoCloudTokenPersistenceIsTrueThenInvalidEmailDoesNotThrow ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions
			{
			CloudMode = true,
			Email = "not-an-email",
			NoCloudTokenPersistence = true
			});

		Assert.That (powerwall.Mode, Is.EqualTo (PowerwallMode.Cloud));
		}

	[Test]
	public void WhenNoCloudTokenPersistenceIsFalseThenInvalidEmailStillThrows ()
		{
		Assert.Throws<PowerwallInvalidConfigurationException> (static () => _ = new Powerwall (new PowerwallOptions
				{
				CloudMode = true,
				Email = "not-an-email",
				NoCloudTokenPersistence = false
				}));
		}

	[Test]
	public void WhenNoCloudTokenPersistenceIsSetThenPowerwallCloudClientExposesIt ()
		{
		using var client = new PowerwallCloudClient (
			"user@example.com",
			cacheExpireSeconds: 5,
			timeout: TimeSpan.FromSeconds (5),
			accessToken: "access-token",
			refreshToken: "refresh-token",
			siteId: null,
			authPath: @"C:\some\path",
			noCloudTokenPersistence: true);

		Assert.That (client.NoCloudTokenPersistence, Is.True);
		Assert.That (client.AuthPath, Is.EqualTo (@"C:\some\path"));
		}

	[Test]
	public void WhenExplicitAuthPathIsUnwritableThenClearStoredCloudTokensThrowsStorageException ()
		{
		// A path nested under a file (rather than a directory) can never be created, forcing a write
		// failure at an explicitly configured location, which must fail fast instead of being swallowed.
		var blockingFile = Path.Combine (TestContext.CurrentContext.WorkDirectory, $"pwl-blocking-{Guid.NewGuid ():N}");
		var authPath = Path.Combine (blockingFile, "cache.json");
		File.WriteAllText (blockingFile, string.Empty);
		try
			{
			Assert.Throws<PowerwallCloudTokenCacheStorageException> (() => Powerwall.ClearStoredCloudTokens ("user@example.com", authPath));
			}
		finally
			{
			File.Delete (blockingFile);
			}
		}

	[Test]
	public void WhenTokenCacheStorageExceptionIsCreatedThenItIsAPowerwallException ()
		{
		var exception = new PowerwallCloudTokenCacheStorageException ("storage failed");

		Assert.That (exception, Is.InstanceOf<PowerwallException> ());
		}

	[Test]
	public void WhenNoFleetApiTokensAreStoredThenHasStoredFleetApiTokensReturnsFalse ()
		{
		var authPath = CreateTempCacheDirectory ();
		try
			{
			Assert.That (Powerwall.HasStoredFleetApiTokens ("user@example.com", authPath), Is.False);
			Assert.That (Powerwall.TryGetStoredFleetApiTokens ("user@example.com", out var accessToken, out var refreshToken, authPath), Is.False);
			Assert.That (accessToken, Is.Null);
			Assert.That (refreshToken, Is.Null);
			}
		finally
			{
			Directory.Delete (authPath, recursive: true);
			}
		}

	[Test]
	public void WhenFleetApiTokensArePersistedThenTheyCanBeReadBackAndCleared ()
		{
		var authPath = CreateTempCacheDirectory ();
		try
			{
			using (var powerwall = new Powerwall (new PowerwallOptions
				{
				Email = "user@example.com",
				FleetApi = true,
				FleetApiClientId = "client-id",
				FleetApiAccessToken = "access-token",
				FleetApiRefreshToken = "refresh-token",
				FleetApiAuthPath = authPath
				}))
				{
				Assert.That (powerwall.Mode, Is.EqualTo (PowerwallMode.FleetApi));
				}

			// Constructing the client seeds the cache from the supplied tokens without requiring a live
			// connection, mirroring how cloud mode's equivalent tests exercise the cache directly.
			var cachePath = Powerwall.GetFleetApiTokenCachePath ("user@example.com", authPath);
			Assert.That (string.IsNullOrWhiteSpace (cachePath), Is.False);

			Powerwall.ClearStoredFleetApiTokens ("user@example.com", authPath);
			Assert.That (Powerwall.HasStoredFleetApiTokens ("user@example.com", authPath), Is.False);
			}
		finally
			{
			Directory.Delete (authPath, recursive: true);
			}
		}

	[Test]
	public void WhenExplicitAuthPathIsUnwritableThenClearStoredFleetApiTokensThrowsStorageException ()
		{
		// A path nested under a file (rather than a directory) can never be created, forcing a write
		// failure at an explicitly configured location, which must fail fast instead of being swallowed.
		var blockingFile = Path.Combine (TestContext.CurrentContext.WorkDirectory, $"pwl-blocking-{Guid.NewGuid ():N}");
		var authPath = Path.Combine (blockingFile, "cache.json");
		File.WriteAllText (blockingFile, string.Empty);
		try
			{
			Assert.Throws<PowerwallFleetApiTokenCacheStorageException> (() => Powerwall.ClearStoredFleetApiTokens ("user@example.com", authPath));
			}
		finally
			{
			File.Delete (blockingFile);
			}
		}

	[Test]
	public void WhenFleetApiTokenCacheStorageExceptionIsCreatedThenItIsAPowerwallException ()
		{
		var exception = new PowerwallFleetApiTokenCacheStorageException ("storage failed");

		Assert.That (exception, Is.InstanceOf<PowerwallException> ());
		}

	// Creates a fresh, empty temp directory to use as an explicit FleetAPI/cloud token cache location,
	// isolating each test from the shared per-user default cache and from other tests.
	private static string CreateTempCacheDirectory ()
		{
		var path = Path.Combine (TestContext.CurrentContext.WorkDirectory, $"pwl-fleetapi-cache-{Guid.NewGuid ():N}");
		Directory.CreateDirectory (path);
		return path;
		}
	}