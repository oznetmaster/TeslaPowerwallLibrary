// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;

using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

public sealed partial class TedapiTests
	{
	[Test]
	public async Task Disposal_DrainsActiveAndQueuedAuthenticationWithoutPublishingDin ()
		{
		using var handler = new LifetimeHandler ();
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = "audit.invalid",
			GatewayPassword = "fake-password",
			LocalProtocol = PowerwallLocalProtocol.Tedapi
			}, handler);
		Task first = client.AuthenticateAsync ();
		await LifetimeReady (handler.Entered.Task);
		Task queued = client.AuthenticateAsync ();
		client.Dispose ();
		client.Dispose ();
		try
			{
			Assert.That (handler.Disposed.Task.IsCompleted, Is.False, "Transport must outlive the active operation.");
			await LifetimeReady (ExpectStopped (queued));
			Assert.That (handler.Requests, Is.EqualTo (1), "Cancelled queued operation must not reach HTTP.");
			}
		finally { _ = handler.Release.TrySetResult (true); }
		Exception error = await ExpectStopped (first);
		if (error is ObjectDisposedException disposed)
			Assert.That (disposed.ObjectName, Is.Not.EqualTo (nameof (SemaphoreSlim)));
		await LifetimeReady (handler.Disposed.Task);
		Assert.That (client.DeviceIdentificationNumber, Is.Null);
		await Assert.ThatAsync (() => client.AuthenticateAsync (), Throws.TypeOf<ObjectDisposedException> ());
		}

	private static async Task<Exception> ExpectStopped (Task task)
		{
		try
			{
			await LifetimeReady (task);
			}
		catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { return error; }
		throw new AssertionException ("Stopped operation unexpectedly succeeded.");
		}
	private static async Task LifetimeReady (Task task)
		{
		Assert.That (await Task.WhenAny (task, Task.Delay (5000)), Is.SameAs (task), "Lifetime regression timed out.");
		await task;
		}
	private sealed class LifetimeHandler : HttpMessageHandler
		{
		internal TaskCompletionSource<bool> Entered { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource<bool> Release { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource<bool> Disposed { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal int Requests;
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Interlocked.Increment (ref Requests);
			Assert.That (request.RequestUri!.AbsolutePath, Is.EqualTo ("/tedapi/din"));
			_ = Entered.TrySetResult (true);
			await Release.Task; // Deliberately return a valid late response after cancellation.
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (DIN) };
			}
		protected override void Dispose (bool disposing)
			{
			base.Dispose (disposing);
			_ = Disposed.TrySetResult (true);
			}
		}
	}