// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests for the <see cref="Powerwall"/> facade construction, mode resolution, and guard behavior.
/// </summary>
[TestClass]
public sealed class PowerwallTests
	{
	[TestMethod]
	public void WhenHostIsProvidedThenModeIsLocal ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		Assert.AreEqual (PowerwallMode.Local, powerwall.Mode);
		}

	[TestMethod]
	public void WhenHostIsProvidedWithPortThenModeIsLocal ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99:8443", Password = "secret" });

		Assert.AreEqual (PowerwallMode.Local, powerwall.Mode);
		}

	[TestMethod]
	public void WhenHostIsEmptyThenModeIsCloud ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		Assert.AreEqual (PowerwallMode.Cloud, powerwall.Mode);
		}

	[TestMethod]
	public void WhenHostIsEmptyAndFleetApiIsTrueThenModeIsFleetApi ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com", FleetApi = true });

		Assert.AreEqual (PowerwallMode.FleetApi, powerwall.Mode);
		}

	[TestMethod]
	public void WhenOptionsAreNullThenConstructorThrowsArgumentNullException ()
		{
		Assert.ThrowsExactly<ArgumentNullException> (static () => _ = new Powerwall (null!));
		}

	[TestMethod]
	public void WhenHostIsInvalidThenConstructorThrowsInvalidConfiguration ()
		{
		Assert.ThrowsExactly<PowerwallInvalidConfigurationException> (
			static () => _ = new Powerwall (new PowerwallOptions { Host = "not a valid host" }));
		}

	[TestMethod]
	public void WhenCloudModeEmailIsInvalidThenConstructorThrowsInvalidConfiguration ()
		{
		Assert.ThrowsExactly<PowerwallInvalidConfigurationException> (
			static () => _ = new Powerwall (new PowerwallOptions { CloudMode = true, Email = "not-an-email" }));
		}

	[TestMethod]
	public void WhenNewlyConstructedThenIsClientConnectedIsFalse ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		Assert.IsFalse (powerwall.IsClientConnected);
		}

	[TestMethod]
	public async Task WhenNotConnectedThenDataMethodThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.StatusAsync ());
		}

	[TestMethod]
	[DataRow (-1.0)]
	[DataRow (101.0)]
	public async Task WhenReserveLevelIsOutOfRangeThenSetOperationThrows (double level)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "10.0.1.99", Password = "secret" });

		await Assert.ThrowsExactlyAsync<InvalidBatteryReserveLevelException> (
			async () => await powerwall.SetOperationAsync (level));
		}

	[TestMethod]
	public async Task WhenCloudModeHasNoTokensThenConnectThrowsNoAuthFile ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<PowerwallCloudNoTeslaAuthFileException> (
			async () => await powerwall.ConnectAsync ());
		}

	[TestMethod]
	public async Task WhenNotConnectedThenGetSitesThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.GetSitesAsync ());
		}

	[TestMethod]
	public async Task WhenNotConnectedThenChangeSiteThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.ChangeSiteAsync ("1234567890"));
		}

	[TestMethod]
	public async Task WhenNotConnectedThenGetGridChargingThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.GetGridChargingAsync ());
		}

	[TestMethod]
	public async Task WhenNotConnectedThenGetGridExportThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.GetGridExportAsync ());
		}

	[TestMethod]
	public async Task WhenNotConnectedThenSetGridChargingThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.SetGridChargingAsync (true));
		}

	[TestMethod]
	[DataRow ("battery_ok")]
	[DataRow ("pv_only")]
	[DataRow ("never")]
	public async Task WhenGridExportModeIsValidThenSetGridExportReachesConnectionGuard (string mode)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.SetGridExportAsync (mode));
		}

	[TestMethod]
	[DataRow ("bogus")]
	[DataRow ("")]
	[DataRow ("BATTERY_OK")]
	public async Task WhenGridExportModeIsInvalidThenSetGridExportThrowsArgumentException (string mode)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<ArgumentException> (
			async () => await powerwall.SetGridExportAsync (mode));
		}

	[TestMethod]
	public async Task WhenNotConnectedThenVitalsThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.VitalsAsync ());
		}

	[TestMethod]
	public async Task WhenNotConnectedThenAlertsThrowsInvalidOperation ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.AlertsAsync ());
		}

	[TestMethod]
	[DataRow ("power")]
	[DataRow ("energy")]
	[DataRow ("backup")]
	[DataRow ("self_consumption")]
	public async Task WhenHistoryKindIsValidThenGetHistoryReachesConnectionGuard (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.GetHistoryAsync (kind));
		}

	[TestMethod]
	[DataRow ("bogus")]
	[DataRow ("")]
	[DataRow ("POWER")]
	[DataRow ("soe")]
	public async Task WhenHistoryKindIsInvalidThenGetHistoryThrowsArgumentException (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<ArgumentException> (
			async () => await powerwall.GetHistoryAsync (kind));
		}

	[TestMethod]
	[DataRow ("hour")]
	[DataRow ("DAY")]
	public async Task WhenHistoryPeriodIsInvalidThenGetHistoryThrowsArgumentException (string period)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<ArgumentException> (
			async () => await powerwall.GetHistoryAsync ("power", period));
		}

	[TestMethod]
	[DataRow ("power")]
	[DataRow ("soe")]
	[DataRow ("time_of_use_energy")]
	[DataRow ("savings")]
	public async Task WhenCalendarHistoryKindIsValidThenGetCalendarHistoryReachesConnectionGuard (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await powerwall.GetCalendarHistoryAsync (kind));
		}

	[TestMethod]
	[DataRow ("bogus")]
	[DataRow ("")]
	public async Task WhenCalendarHistoryKindIsInvalidThenGetCalendarHistoryThrowsArgumentException (string kind)
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Email = "user@example.com" });

		await Assert.ThrowsExactlyAsync<ArgumentException> (
			async () => await powerwall.GetCalendarHistoryAsync (kind));
		}
	}
