// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See TeslaPowerwallLibrary/LICENSE.

using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace TeslaPowerwallLibrary.TestCredentials.Tests;

[TestFixture]
public sealed class CredentialStoreTests
	{
	private string _directory = "";
	private static CredentialState Seed (string mode = "fleet") => new () { Mode = mode, ClientId = mode == "fleet" ? "test-client" : "", RefreshToken = "original-synthetic-refresh", SiteId = "123", Region = "eu" };
	[SetUp] public void SetUp () => _directory = Path.Combine (Path.GetTempPath (), "TeslaCredentialTests", Guid.NewGuid ().ToString ("N"));
	[TearDown]
	public void TearDown ()
		{
		if (Directory.Exists (_directory))
			Directory.Delete (_directory, true);
		}

	[TestCase ("fleet")]
	[TestCase ("cloud")]
	public void Rotation_IsDurableAndEncrypted_AndNextRunUsesNewestToken (string mode)
		{
		using (var store = new CredentialStore (_directory))
			{
			store.Initialize (Seed (mode));
			store.Begin ();
			store.PersistTokens ("synthetic-access", "rotated-synthetic-refresh");
			store.Complete ();
			}
		using var reopened = new CredentialStore (_directory);
		Assert.That (reopened.Current!.RefreshToken, Is.EqualTo ("rotated-synthetic-refresh"));
		Assert.That (reopened.Current.AccessToken, Is.EqualTo ("synthetic-access"));
		Assert.That (reopened.Current.Mode, Is.EqualTo (mode));
		Assert.That (Encoding.UTF8.GetString (File.ReadAllBytes (Path.Combine (_directory, "credentials.dat"))), Does.Not.Contain ("synthetic"));
		reopened.Begin ();
		reopened.Complete ();
		}

	[Test]
	public void ConcurrentOwner_IsRejected ()
		{
		using var first = new CredentialStore (_directory);
		first.Initialize (Seed ());
		Assert.Throws<IOException> (() => { using var second = new CredentialStore (_directory); });
		}

	[Test]
	public void InterruptedRun_RemainsHeldAfterProcessReleasesFileLock ()
		{
		using (var store = new CredentialStore (_directory))
			{
			store.Initialize (Seed ());
			store.Begin ();
			store.PersistTokens (null, "rotated-before-interruption");
			}
		using var reopened = new CredentialStore (_directory);
		Assert.That (reopened.Current!.RefreshToken, Is.EqualTo ("rotated-before-interruption"));
		Assert.Throws<InvalidOperationException> (() => reopened.Begin ());
		}

	[Test]
	public void OriginalSeed_CannotReplaceRotatedState ()
		{
		using var store = new CredentialStore (_directory);
		store.Initialize (Seed ());
		store.Begin ();
		store.PersistTokens ("access", "new-refresh");
		store.Complete ();
		Assert.Throws<InvalidOperationException> (() => store.Initialize (Seed ()));
		Assert.That (store.Current!.RefreshToken, Is.EqualTo ("new-refresh"));
		}

	[TestCase ("fleet")]
	[TestCase ("cloud")]
	public void ProcessorInputs_ContainNoRefreshToken_AndAreRemovedAfterRun (string mode)
		{
		using var store = new CredentialStore (_directory);
		store.Initialize (Seed (mode));
		store.Begin ();
		store.PersistTokens ("current-access", "private-refresh");
		store.WriteInputs ();
		string path = Path.Combine (store.InputsDirectory, "LiveTestSettings.json");
		string json = File.ReadAllText (path);
		using var document = JsonDocument.Parse (json);
		Assert.That (document.RootElement.GetProperty ("accessToken").GetString (), Is.EqualTo ("current-access"));
		Assert.That (document.RootElement.GetProperty ("mode").GetString (), Is.EqualTo (mode));
		Assert.That (json, Does.Not.Contain ("refreshToken").And.Not.Contain ("private-refresh"));
		store.Complete ();
		Assert.That (File.Exists (path), Is.False);
		Assert.That (File.Exists (Path.Combine (_directory, "credentials.dat")), Is.True);
		}

	[Test]
	public void MissingAccessTokenInRotationNotification_DoesNotReusePreviousAccessToken ()
		{
		using var store = new CredentialStore (_directory);
		store.Initialize (Seed ());
		store.Begin ();
		store.PersistTokens ("old-access", "refresh-1");
		store.PersistTokens (null, "refresh-2");
		Assert.Throws<InvalidOperationException> (() => store.WriteInputs ());
		Assert.That (store.Current!.AccessToken, Is.Null);
		}

	[Test]
	public void CorruptedStore_IsRejectedWithoutReplacingCredentials ()
		{
		Directory.CreateDirectory (_directory);
		string path = Path.Combine (_directory, "credentials.dat");
		File.WriteAllText (path, "damaged");
		Assert.Catch (() => { using var store = new CredentialStore (_directory); });
		Assert.That (File.ReadAllText (path), Is.EqualTo ("damaged"));
		}

	[TestCase ("fleet")]
	[TestCase ("cloud")]
	public void Options_UseOnlySelectedAuthenticationModeAndNoExternalCache (string mode)
		{
		PowerwallOptions options = TestPreparation.OptionsFor (Seed (mode));
		Assert.That (options.FleetApi, Is.EqualTo (mode == "fleet"));
		Assert.That (options.CloudMode, Is.EqualTo (mode == "cloud"));
		Assert.That (mode == "fleet" ? options.RefreshToken : options.FleetApiRefreshToken, Is.Null);
		Assert.That (options.NoCloudTokenPersistence && options.NoFleetApiTokenPersistence, Is.True);
		}

	[Test]
	public void StoppedPreparedSession_CanBeReleased_WithoutReauthenticating ()
		{
		using (var store = new CredentialStore (_directory))
			{
			store.Initialize (Seed ());
			store.Begin ();
			store.PersistTokens ("access", "latest-refresh");
			store.WriteInputs ();
			store.MarkPrepared ();
			}
		using var reopened = new CredentialStore (_directory);
		reopened.ReleaseStoppedRun ();
		Assert.That (reopened.Current!.RefreshToken, Is.EqualTo ("latest-refresh"));
		Assert.That (reopened.Current.InProgress, Is.False);
		}

	[Test]
	public void UncertainAuthentication_CannotBeReleasedAsStoppedTests ()
		{
		using var store = new CredentialStore (_directory);
		store.Initialize (Seed ());
		store.Begin ();
		Assert.Throws<InvalidOperationException> (() => store.ReleaseStoppedRun ());
		}

	[Test]
	public void LocalMode_IsRecognizedButCannotFallBackToCloudAuthentication ()
		{
		var state = Seed ("local");
		Assert.Throws<NotSupportedException> (() => TestPreparation.OptionsFor (state));
		}
	}