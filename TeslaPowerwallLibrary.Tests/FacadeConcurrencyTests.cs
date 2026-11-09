// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.


using static TeslaPowerwallLibrary.Tests.ConcurrencyTestAwait;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class FacadeConcurrencyTests
	{
	[Test]
	public async Task Reconnects_AreSerializedAndReuseTheOwnedBackend ()
		{
		using var backend = new ControlledBackend ();
		using var client = Create (backend);
		Task<bool> first = client.ConnectAsync ();
		await Ready (backend.Entered.Task);
		Task<bool> second = client.ConnectAsync ();
		try
			{
			Assert.That (backend.Authentications, Is.EqualTo (1));
			}
		finally { _ = backend.Release.TrySetResult (true); }
		Assert.That (await Ready (first), Is.True);
		Assert.That (await Ready (second), Is.True);
		Assert.That (backend.Authentications, Is.EqualTo (2));
		Assert.That (backend.DisposeCalls, Is.Zero);
		client.Dispose ();
		Assert.That (backend.DisposeCalls, Is.EqualTo (1));
		}

	[Test]
	public async Task DisposeDuringReconnect_RejectsQueuedConnectAndLateSuccess ()
		{
		using var backend = new ControlledBackend ();
		using var client = Create (backend);
		Task<bool> first = client.ConnectAsync ();
		await Ready (backend.Entered.Task);
		Task<bool> queued = client.ConnectAsync ();
		client.Dispose ();
		client.Dispose ();
		try
			{
			Assert.That (client.IsClientConnected, Is.False);
			Assert.That (backend.DisposeCalls, Is.Zero);
			await Assert.ThatAsync (async () => await Ready (queued), Throws.InstanceOf<OperationCanceledException> ());
			}
		finally { _ = backend.Release.TrySetResult (true); }
		await Assert.ThatAsync (async () => await Ready (first), Throws.TypeOf<ObjectDisposedException> ());
		await Ready (backend.Disposed.Task);
		Assert.That (backend.DisposeCalls, Is.EqualTo (1));
		await Assert.ThatAsync (() => client.ConnectAsync (), Throws.TypeOf<ObjectDisposedException> ());
		Assert.That (() => client.PollAsync ("/api/status"), Throws.TypeOf<ObjectDisposedException> ());
		}
	private static Powerwall Create (PowerwallClientBase backend)
		{
		var client = new Powerwall (new PowerwallOptions { Host = "audit.invalid" });
		OperationRegressionTests.SetField (client, "_client", backend);
		return client;
		}
	private sealed class ControlledBackend () : PowerwallClientBase ("fake@example.invalid"), IDisposable
		{
		internal TaskCompletionSource<bool> Entered { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource<bool> Release { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource<bool> Disposed { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal int Authentications, DisposeCalls;
		public override async Task AuthenticateAsync (CancellationToken cancellationToken = default)
			{
			Interlocked.Increment (ref Authentications);
			_ = Entered.TrySetResult (true);
			await Release.Task; // Test a backend returning after cancellation.
			}
		public override Task CloseSessionAsync (CancellationToken cancellationToken = default) => Task.CompletedTask;
		public override Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) => Task.FromResult<string?> ("{}");
		public override Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?> (null);
		public override Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default) => Task.FromResult<string?> (null);
		public override Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> (null);
		public override Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default) => Task.FromResult<double?> (null);
		public void Dispose ()
			{
			Interlocked.Increment (ref DisposeCalls);
			_ = Disposed.TrySetResult (true);
			}
		}
	}