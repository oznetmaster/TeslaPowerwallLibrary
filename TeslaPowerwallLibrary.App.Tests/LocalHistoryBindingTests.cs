// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Text.Json;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Services;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Checks that cloud credentials and network addresses cannot change the physical history-site bond.</summary>
[TestFixture]
public sealed class LocalHistoryBindingTests
	{
	/// <summary>A saved device/site association survives provider, account and address changes and serialization.</summary>
	[Test]
	public void Binding_SurvivesCredentialAndAddressChanges ()
		{
		var settings = new AppSettings { Host = "old.local", Email = "first@example.test", HistoryMode = "Cloud" };
		LocalHistoryBinding.Bind (settings, "part--device", new LocalHistorySite { SiteId = "site-1", SiteName = "Home" });
		settings.Host = "192.0.2.2";
		settings.Email = "second@example.test";
		settings.HistoryMode = "FleetApi";
		var restored = JsonSerializer.Deserialize<AppSettings> (JsonSerializer.Serialize (settings))!;
		Assert.That (LocalHistoryBinding.Find (restored, "part--device")!.SiteId, Is.EqualTo ("site-1"));
		Assert.That (LocalHistoryBinding.Find (restored, "part--different-device"), Is.Null);
		}

	/// <summary>The same device cannot be silently assigned a different site, and an address cannot stand in for missing identity.</summary>
	[Test]
	public void Binding_RejectsConflictingSiteAndMissingIdentity ()
		{
		var settings = new AppSettings ();
		var site = new LocalHistorySite { SiteId = "site-1" };
		LocalHistoryBinding.Bind (settings, "part--device", site);
		Assert.Throws<InvalidOperationException> (() => LocalHistoryBinding.Bind (settings, "part--device", new LocalHistorySite { SiteId = "site-2" }));
		Assert.Throws<InvalidOperationException> (() => LocalHistoryBinding.Bind (settings, null, site));
		Assert.That (LocalHistoryBinding.Find (settings, "part--device")!.SiteId, Is.EqualTo ("site-1"));
		}
	}
