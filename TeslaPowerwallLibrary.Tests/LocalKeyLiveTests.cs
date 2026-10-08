// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;

using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

public sealed partial class LiveSiteTests
	{
	/// <summary>Reads authorization state for a fresh, deliberately unregistered key using existing private test credentials.</summary>
	[Test, Explicit ("Read-only cloud key-status check; does not register the generated public key.")]
	public async Task RealSite_ReadsUnregisteredLocalKeyStatus ()
		{
#if NETFRAMEWORK
		using RSA key = new RSACng (4096);
#else
		using RSA key = RSA.Create (4096);
#endif
		LocalKeyRegistration result = await _client!.GetLocalKeyStatusAsync (key, _deadline!.Token);
		Assert.That (result.State, Is.EqualTo (LocalKeyState.Unknown), "A newly generated, unregistered key must not be authorized.");
		TestContext.Out.WriteLine ("The existing cloud session accepted the key-status request. This fresh key is not authorized; no enrollment was performed.");
		}

	/// <summary>Explicitly enrolls the prepared Windows signing key after operator authorization.</summary>
	[Test, Explicit ("Registers the prepared public key; requires separate explicit enrollment enablement.")]
	public async Task RealSite_EnrollsPreparedLocalKey ()
		{
		if (!TestContext.Parameters.Get ("EnableLocalKeyEnrollment", "").Equals ("true", StringComparison.OrdinalIgnoreCase))
			Assert.Ignore ("Local key enrollment requires explicit operator enablement.");
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("This fixture uses the Windows user key store.");
			return;
			}
#endif
		const string keyName = "TeslaPowerwallLibrary.LocalDevelopment.Powerwall3";
		using CngKey stored = CngKey.Open (keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		LocalKeyRegistration registration = await _client!.RegisterLocalKeyAsync (key, "TeslaPowerwallLibrary LAN development", _deadline!.Token);
		TestContext.Out.WriteLine ("Enrollment state: " + registration.State + "; public-key fingerprint: " + registration.Fingerprint);
		LocalKeyRegistration status = await _client.GetLocalKeyStatusAsync (key, _deadline.Token);
		TestContext.Out.WriteLine ("Subsequent key status: " + status.State);
		Assert.That (registration.State != LocalKeyState.Unknown || status.State != LocalKeyState.Unknown, Is.True,
			"Enrollment outcome was not recognized. Do not repeat registration automatically; inspect authorization status.");
		}

	/// <summary>Watches the prepared key for up to two minutes after the operator performs physical verification.</summary>
	[Test, Explicit ("Bounded read-only status checks; never enrolls a key or changes power state.")]
	public async Task RealSite_WaitsForPreparedKeyVerification ()
		{
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("This fixture uses the Windows user key store.");
			return;
			}
#endif
		using CngKey stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		var timer = System.Diagnostics.Stopwatch.StartNew ();
		while (true)
			{
			LocalKeyRegistration result = await _client!.GetLocalKeyStatusAsync (key, _deadline!.Token);
			TestContext.Progress.WriteLine ($"{DateTimeOffset.Now:HH:mm:ss} — prepared key: {result.State}");
			if (result.State == LocalKeyState.Verified)
				return;
			if (result.State != LocalKeyState.PendingVerification || timer.Elapsed >= TimeSpan.FromMinutes (2))
				{
				Assert.Fail ("Key verification was not confirmed: " + result.State + ". No re-registration was attempted.");
				return;
				}
			await Task.Delay (TimeSpan.FromSeconds (10), _deadline.Token);
			}
		}

	/// <summary>Reads the prepared key's current state without repeating enrollment.</summary>
	[Test, Explicit ("Reads authorization state only; does not register or verify a key.")]
	public async Task RealSite_ReadsPreparedLocalKeyStatus ()
		{
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("This fixture uses the Windows user key store.");
			return;
			}
#endif
		using CngKey stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		LocalKeyRegistration status = await _client!.GetLocalKeyStatusAsync (key, _deadline!.Token);
		TestContext.Out.WriteLine ("Prepared local key status: " + status.State + "; public-key fingerprint: " + status.Fingerprint);
		}
	}
