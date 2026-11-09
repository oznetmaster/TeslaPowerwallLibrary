// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;

using TeslaPowerwallLibrary.Local;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class LocalLifetimeTests
	{
	[Test]
	public async Task DisposeDuringLogin_CancelsQueuedReadAndRejectsLateLogin ()
		{
		using var handler = new LateLoginHandler ();
		using var client = new PowerwallLocalClient (new PowerwallOptions
			{
			Host = "audit.invalid",
			Password = "fake-password",
			AuthMode = "token",
			NoLocalSessionPersistence = true
			}, handler);
		Task login = client.AuthenticateAsync ();
		await Ready (handler.Entered.Task);
		Task read = client.PollAsync ("/api/status");
		client.Dispose ();
		try
			{
			Assert.That (handler.Disposed.Task.IsCompleted, Is.False);
			await Ready (Stopped (read));
			Assert.That (handler.Requests, Is.EqualTo (1));
			}
		finally { _ = handler.Release.TrySetResult (true); }
		await Ready (Stopped (login));
		await Ready (handler.Disposed.Task);
		await Assert.ThatAsync (() => client.AuthenticateAsync (), Throws.TypeOf<ObjectDisposedException> ());
		}
	private static async Task Stopped (Task task)
		{
		try
			{
			await task;
			}
		catch (Exception error) when (error is ObjectDisposedException or OperationCanceledException) { return; }
		Assert.Fail ("Stopped local operation unexpectedly succeeded.");
		}
	private static async Task Ready (Task task)
		{
		Assert.That (await Task.WhenAny (task, Task.Delay (5000)), Is.SameAs (task), "Local lifetime test timed out.");
		await task;
		}
	private sealed class LateLoginHandler : HttpMessageHandler
		{
		internal TaskCompletionSource<bool> Entered { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource<bool> Release { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource<bool> Disposed { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal int Requests;
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Interlocked.Increment (ref Requests);
			_ = Entered.TrySetResult (true);
			await Release.Task;
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent ("{\"token\":\"fake-late-token\"}") };
			}
		protected override void Dispose (bool disposing)
			{
			base.Dispose (disposing);
			_ = Disposed.TrySetResult (true);
			}
		}
	}