// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Offline regression checks for portable local live-test settings.</summary>
[TestFixture]
public sealed class LiveLocalSettingsTests
	{
	/// <summary>Verifies typed local JSON and read-only connection options.</summary>
	/// <param name="protocol">The portable protocol selection.</param>
	[TestCase ("Gateway"), TestCase ("Setup Wi-Fi")]
	public void PortableInputCreatesReadOnlyLocalOptions (string protocol)
		{
		var original = new LiveSiteTests.LiveSettings
			{ Enabled = false, Mode = "local", LocalProtocol = protocol, Host = "powerwall.example.invalid", Password = "synthetic", LocalRefreshIntervalSeconds = 30 };
		var settings = JsonSerializer.Deserialize<LiveSiteTests.LiveSettings> (JsonSerializer.Serialize (original))!;
		PowerwallOptions options = settings.LocalOptions (out RSA? key);
		Assert.That (key, Is.Null);
		Assert.That (options.AllowLocalControl, Is.False);
		Assert.That (options.CloudMode || options.FleetApi, Is.False);
		Assert.That (options.NoLocalSessionPersistence, Is.True);
		Assert.That (options.CacheExpireSeconds, Is.EqualTo (30));
		Assert.That (options.Host, Is.EqualTo (original.Host));
		}

	/// <summary>Rejects missing signing credentials rather than silently switching protocols.</summary>
	[Test]
	public void SignedInputWithoutRegisteredKeyIsRejected ()
		{
		var settings = new LiveSiteTests.LiveSettings { Mode = "local", LocalProtocol = "Signed LAN", Host = "powerwall.example.invalid", Password = "synthetic" };
		Assert.Throws<InvalidDataException> (() => settings.ValidateLocal ());
		}

	/// <summary>Rejects an unrecognized protocol rather than falling back to cloud.</summary>
	[Test]
	public void UnknownProtocolIsRejected ()
		{
		var settings = new LiveSiteTests.LiveSettings { Mode = "local", LocalProtocol = "unknown", Host = "powerwall.example.invalid", Password = "synthetic" };
		Assert.Throws<InvalidDataException> (() => settings.ValidateLocal ());
		}
	}
