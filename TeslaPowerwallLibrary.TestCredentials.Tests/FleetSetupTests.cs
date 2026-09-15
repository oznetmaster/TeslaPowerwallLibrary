// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See TeslaPowerwallLibrary/LICENSE.

using System.Text;

using NUnit.Framework;

using TeslaPowerwallLibrary.Setup;

namespace TeslaPowerwallLibrary.TestCredentials.Tests;

[TestFixture]
public sealed class FleetSetupTests
	{
	[TestCase ("https://example.test/callback?state=expected&code=value", true)]
	[TestCase ("https://example.test/callback?error=access_denied", true)]
	[TestCase ("https://example.test/callback#invalid", true)]
	[TestCase ("https://example.test/callback/other?code=value", false)]
	[TestCase ("https://example.test:444/callback?code=value", false)]
	[TestCase ("https://example.test.attacker.test/callback?code=value", false)]
	[TestCase ("http://example.test/callback?code=value", false)]
	[TestCase ("https://auth.tesla.com/oauth2/v3/authorize", false)]
	public void Navigation_CapturesOnlyTheRegisteredCallbackAddress (string url, bool capture) =>
		Assert.That (FleetSetupPreferences.IsCallbackAddress (url, "https://example.test/callback"), Is.EqualTo (capture));

	[Test]
	public void Callback_ValidatesStateAndDecodesCode () =>
		Assert.That (FleetSetupPreferences.ParseCallback ("https://example.test/callback?state=expected&code=a%2Bb%3Dc", "https://example.test/callback", "expected"), Is.EqualTo ("a+b=c"));

	[TestCase ("https://other.test/callback?state=expected&code=value")]
	[TestCase ("https://example.test/other?state=expected&code=value")]
	[TestCase ("http://example.test/callback?state=expected&code=value")]
	[TestCase ("https://example.test/callback?state=wrong&code=value")]
	[TestCase ("https://example.test/callback?code=value")]
	[TestCase ("https://example.test/callback?state=expected&code=first&code=second")]
	[TestCase ("https://example.test/callback?state=expected&error=denied&code=value")]
	[TestCase ("bare-code")]
	public void Callback_RejectsUnrelatedOrAmbiguousAuthorization (string callback) =>
		Assert.Throws<InvalidDataException> (() => FleetSetupPreferences.ParseCallback (callback, "https://example.test/callback", "expected"));

	[Test]
	public void RememberedApp_RoundTripsEncryptedWithoutIssuedTokens ()
		{
		string directory = Path.Combine (Path.GetTempPath (), "TeslaSetupTests", Guid.NewGuid ().ToString ("N"));
		try
			{
			string path = Path.Combine (directory, "fleet.dat");
			new FleetSetupPreferences { ClientId = "synthetic-client", ClientSecret = "synthetic-secret", RedirectUri = "https://example.test/callback", Region = "eu" }.Save (path);
			Assert.That (Encoding.UTF8.GetString (File.ReadAllBytes (path)), Does.Not.Contain ("synthetic-secret"));
			Assert.That (FleetSetupPreferences.Load (path)!.ClientSecret, Is.EqualTo ("synthetic-secret"));
			Assert.That (FleetSetupPreferences.Load (path)!.Region, Is.EqualTo ("eu"));
			}
		finally { if (Directory.Exists (directory)) Directory.Delete (directory, true); }
		}
	}