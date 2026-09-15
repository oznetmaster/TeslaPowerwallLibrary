// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See TeslaPowerwallLibrary/LICENSE.

using NUnit.Framework;

namespace TeslaPowerwallLibrary.TestCredentials.Tests;

[TestFixture]
public sealed class TestPreparationTests
	{
	private string _directory = "";
	[SetUp] public void SetUp () => _directory = Path.Combine (Path.GetTempPath (), "TeslaPreparationTests", Guid.NewGuid ().ToString ("N"));
	[TearDown]
	public void TearDown ()
		{
		if (Directory.Exists (_directory))
			Directory.Delete (_directory, true);
		}
	private static CredentialState Seed (string mode) => new () { Mode = mode, ClientId = "test-client", RefreshToken = "initial", SiteId = "123" };

	[TestCase ("cloud")]
	[TestCase ("fleet")]
	public async Task Preparation_PersistsRotatedCredentials_AndNextSessionReceivesThem (string mode)
		{
		using (var store = new CredentialStore (_directory))
			{
			store.Initialize (Seed (mode));
			var fake = new RotatingConnection ();
			await TestPreparation.PrepareAsync (store, CancellationToken.None, _ => fake);
			Assert.That (fake.Disposed, Is.True);
			Assert.That (File.Exists (Path.Combine (store.InputsDirectory, "LiveTestSettings.json")), Is.True);
			store.MarkPrepared ();
			store.Complete ();
			}
		using var second = new CredentialStore (_directory);
		PowerwallOptions? received = null;
		await TestPreparation.PrepareAsync (second, CancellationToken.None, options => { received = options; return new RotatingConnection (); });
		Assert.That (mode == "fleet" ? received!.FleetApiRefreshToken : received!.RefreshToken, Is.EqualTo ("rotated"));
		Assert.That (mode == "fleet" ? received!.FleetApiAccessToken : received!.AccessToken, Is.EqualTo ("current-access"));
		second.MarkPrepared ();
		second.Complete ();
		}

	[TestCase ("cloud")]
	[TestCase ("fleet")]
	public void RotationFollowedByConnectionFailure_RetainsLatestTokenAndBlocksRetry (string mode)
		{
		using (var store = new CredentialStore (_directory))
			{
			store.Initialize (Seed (mode));
			Assert.ThrowsAsync<InvalidOperationException> (() => TestPreparation.PrepareAsync (store, CancellationToken.None, _ => new RotatingConnection { Succeeds = false }));
			}
		using var reopened = new CredentialStore (_directory);
		Assert.That (reopened.Current!.RefreshToken, Is.EqualTo ("rotated"));
		Assert.Throws<InvalidOperationException> (() => reopened.Begin ());
		Assert.That (Directory.Exists (reopened.InputsDirectory), Is.False);
		}

	[Test]
	public void WrongSite_DoesNotProduceTestInputs ()
		{
		using var store = new CredentialStore (_directory);
		store.Initialize (Seed ("fleet"));
		Assert.ThrowsAsync<InvalidOperationException> (() => TestPreparation.PrepareAsync (store, CancellationToken.None, _ => new RotatingConnection { SiteId = "999" }));
		Assert.That (Directory.Exists (store.InputsDirectory), Is.False);
		}

	private sealed class RotatingConnection : ICredentialConnection
		{
		public event Action<string?, string?>? TokensChanged;
		public bool Succeeds { get; init; } = true;
		public bool Disposed
			{
			get; private set;
			}
		public string? SiteId { get; init; } = "123";
		public string? AccessToken => "current-access";
		public string? RefreshToken => "rotated";
		public Task<bool> ConnectAsync (CancellationToken cancellationToken)
			{
			TokensChanged?.Invoke (null, RefreshToken);
			return Task.FromResult (Succeeds);
			}
		public void Dispose () => Disposed = true;
		}
	}