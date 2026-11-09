// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.FleetApi;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class TokenCacheConcurrencyTests
	{
	[TestCase (false)]
	[TestCase (true)]
	public async Task DifferentCacheInstances_SharingOneFile_PreserveBothAccountsAndFields (bool fleet)
		{
		string directory = Path.Combine (Path.GetTempPath (), "Powerwall-cache-race-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (directory);
		try
			{
			string path = Path.Combine (directory, "fake-tokens.json");
			using var start = new ManualResetEventSlim (false);
			Task first = Task.Run (() => Exercise (fleet, path, "first@example.invalid", start));
			Task second = Task.Run (() => Exercise (fleet, path, "second@example.invalid", start));
			start.Set ();
			await Task.WhenAll (first, second);
			Verify (fleet, path, "first@example.invalid");
			Verify (fleet, path, "second@example.invalid");
			Assert.That (Directory.GetFiles (directory, "*.tmp"), Is.Empty);
			}
		finally
			{
			// Only the fresh, explicitly named test directory contains these fake credentials.
			foreach (string path in Directory.GetFiles (directory))
				File.Delete (path);
			Directory.Delete (directory);
			}
		}
	private static void Exercise (bool fleet, string path, string email, ManualResetEventSlim start)
		{
		start.Wait ();
		for (var index = 0; index < 12; index++)
			{
			if (fleet)
				{
				new TeslaFleetApiTokenCache (path, email).SaveTokens ("fake-client", "fake-access", "fake-refresh");
				new TeslaFleetApiTokenCache (path, email).SaveSite ("fake-site");
				}
			else
				{
				new TeslaCloudTokenCache (path, email).SaveTokens ("fake-access", "fake-refresh");
				new TeslaCloudTokenCache (path, email).SaveSite ("fake-site");
				}
			}
		}
	private static void Verify (bool fleet, string path, string email)
		{
		if (fleet)
			{
			FleetApiTokenCacheEntry entry = new TeslaFleetApiTokenCache (path, email).Load ();
			Assert.That (entry.ClientId, Is.EqualTo ("fake-client"));
			Assert.That (entry.AccessToken, Is.EqualTo ("fake-access"));
			Assert.That (entry.RefreshToken, Is.EqualTo ("fake-refresh"));
			Assert.That (entry.SiteId, Is.EqualTo ("fake-site"));
			}
		else
			{
			CloudTokenCacheEntry entry = new TeslaCloudTokenCache (path, email).Load ();
			Assert.That (entry.AccessToken, Is.EqualTo ("fake-access"));
			Assert.That (entry.RefreshToken, Is.EqualTo ("fake-refresh"));
			Assert.That (entry.SiteId, Is.EqualTo ("fake-site"));
			}
		}
	}