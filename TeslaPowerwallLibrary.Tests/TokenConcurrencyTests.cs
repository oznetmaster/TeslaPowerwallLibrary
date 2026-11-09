// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.FleetApi;

using static TeslaPowerwallLibrary.Tests.ConcurrencyTestAwait;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class TokenConcurrencyTests
	{
	private static readonly int[] _notificationOrder = [1, 2];

	[TestCase (false)]
	[TestCase (true)]
	public async Task TokenNotifications_StayOrderedAcrossRotations (bool fleet)
		{
		using var handler = new RotationHandler (false);
		using var connection = new Harness (fleet, handler);
		using var releaseCallback = new ManualResetEventSlim (false);
		var callbackEntered = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		var order = new System.Collections.Concurrent.ConcurrentQueue<int> ();
		int callbacks = 0;
		connection.Notify (() =>
			{
				int number = Interlocked.Increment (ref callbacks);
				if (number == 1)
					{
					_ = callbackEntered.TrySetResult (true);
					Assert.That (releaseCallback.Wait (TimeSpan.FromSeconds (5)), Is.True);
					}
				order.Enqueue (number);
			});
		_ = handler.ReleaseRefresh.TrySetResult (true);
		Task<bool> first = Task.Run (() => connection.Refresh (CancellationToken.None));
		await Ready (callbackEntered.Task);
		Task<bool> second = connection.Refresh (CancellationToken.None);
		try
			{
			Assert.That (handler.RefreshRequests, Is.EqualTo (1));
			}
		finally { releaseCallback.Set (); }
		Assert.That (await Ready (first), Is.True);
		Assert.That (await Ready (second), Is.True);
		Assert.That (order, Is.EqualTo (_notificationOrder));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task TokenCallback_MayDisposeTheConnectionWithoutDeadlock (bool fleet)
		{
		using var handler = new RotationHandler (false);
		using var connection = new Harness (fleet, handler);
		connection.Notify (connection.Dispose);
		_ = handler.ReleaseRefresh.TrySetResult (true);
		await Ready (Stopped (connection.Refresh (CancellationToken.None)));
		await Ready (handler.Disposed.Task);
		}
	[TestCase (false, false)]
	[TestCase (true, false)]
	[TestCase (false, true)]
	[TestCase (true, true)]
	public async Task OverlappingRefreshes_ShareSuccessOrRejection (bool fleet, bool reject)
		{
		using var handler = new RotationHandler (reject);
		using var connection = new Harness (fleet, handler);
		int notifications = 0;
		connection.Notify (() => Interlocked.Increment (ref notifications));
		Task<bool> first = connection.Refresh (CancellationToken.None);
		await Ready (handler.RefreshStarted.Task);
		Task<bool> second = connection.Refresh (CancellationToken.None);
		try
			{
			Assert.That (handler.RefreshRequests, Is.EqualTo (1));
			}
		finally { _ = handler.ReleaseRefresh.TrySetResult (true); }
		Assert.That (await Ready (first), Is.EqualTo (!reject));
		Assert.That (await Ready (second), Is.EqualTo (!reject));
		Assert.That (handler.RefreshRequests, Is.EqualTo (1));
		Assert.That (notifications, Is.EqualTo (reject ? 0 : 1));
		Assert.That (connection.RefreshToken (), Is.EqualTo (reject ? "fake-original" : "fake-rotated"));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task CancellingFirstCaller_PreservesRotatedPairForOtherCaller (bool fleet)
		{
		using var handler = new RotationHandler (false);
		using var connection = new Harness (fleet, handler);
		using var cancellation = new CancellationTokenSource ();
		Task<bool> first = connection.Refresh (cancellation.Token);
		await Ready (handler.RefreshStarted.Task);
		Task<bool> second = connection.Refresh (CancellationToken.None);
		cancellation.Cancel ();
		_ = handler.ReleaseRefresh.TrySetResult (true);
		await Assert.ThatAsync (async () => await Ready (first), Throws.InstanceOf<OperationCanceledException> ());
		Assert.That (await Ready (second), Is.True);
		Assert.That (connection.AccessToken (), Is.EqualTo ("fake-new"));
		Assert.That (connection.RefreshToken (), Is.EqualTo ("fake-rotated"));
		Assert.That (handler.RefreshRequests, Is.EqualTo (1));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task LateUnauthorizedResponse_ReusesAlreadyRotatedToken (bool fleet)
		{
		using var handler = new RotationHandler (false);
		using var connection = new Harness (fleet, handler);
		Task first = connection.Read ();
		Task second = connection.Read ();
		await Ready (handler.OldRequestsStarted.Task);
		_ = handler.ReleaseFirstUnauthorized.TrySetResult (true);
		await Ready (handler.RefreshStarted.Task);
		_ = handler.ReleaseRefresh.TrySetResult (true);
		await Ready (first);
		_ = handler.ReleaseSecondUnauthorized.TrySetResult (true);
		await Ready (second);
		Assert.That (handler.RefreshRequests, Is.EqualTo (1));
		Assert.That (handler.ProductRequests, Is.EqualTo (4));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task Disposal_DrainsRefreshAndRejectsLateTokenPublication (bool fleet)
		{
		using var handler = new RotationHandler (false);
		using var connection = new Harness (fleet, handler);
		Task<bool> first = connection.Refresh (CancellationToken.None);
		await Ready (handler.RefreshStarted.Task);
		Task<bool> second = connection.Refresh (CancellationToken.None);
		connection.Dispose ();
		_ = handler.ReleaseRefresh.TrySetResult (true);
		await Ready (Stopped (first));
		await Ready (Stopped (second));
		await Ready (handler.Disposed.Task);
		Assert.That (connection.AccessToken (), Is.Null);
		Assert.That (connection.RefreshToken (), Is.Null);
		await Assert.ThatAsync (() => connection.Refresh (CancellationToken.None), Throws.TypeOf<ObjectDisposedException> ());
		}

	private static async Task Stopped (Task task)
		{
		try
			{
			await task.ConfigureAwait (false);
			}
		catch (Exception error) when (error is ObjectDisposedException or OperationCanceledException) { return; }
		Assert.Fail ("Stopped request unexpectedly succeeded.");
		}
	private sealed class Harness : IDisposable
		{
		private readonly IDisposable _owned;
		internal Func<CancellationToken, Task<bool>> Refresh
			{
			get;
			}
		internal Func<Task> Read
			{
			get;
			}
		internal Func<string?> AccessToken
			{
			get;
			}
		internal Func<string?> RefreshToken
			{
			get;
			}
		internal Action<Action> Notify
			{
			get;
			}
		internal Harness (bool fleet, HttpMessageHandler handler)
			{
			if (fleet)
				{
				var connection = new FleetApiConnection ("fake-client", "fake-old", "fake-original", "https://fleet.invalid", TimeSpan.FromSeconds (10), handler);
				_owned = connection;
				Refresh = connection.RefreshAccessTokenAsync;
				Read = () => connection.GetProductsAsync ();
				AccessToken = () => connection.AccessToken;
				RefreshToken = () => connection.RefreshToken;
				Notify = callback => connection.TokensRefreshed += (_, _) => callback ();
				}
			else
				{
				var connection = new TeslaCloudConnection ("fake-old", "fake-original", TimeSpan.FromSeconds (10));
				((HttpClient)OperationRegressionTests.GetField (connection, "_httpClient")).Dispose ();
				OperationRegressionTests.SetField (connection, "_httpClient", new HttpClient (handler));
				_owned = connection;
				Refresh = connection.RefreshAccessTokenAsync;
				Read = () => connection.GetProductsAsync ();
				AccessToken = () => connection.AccessToken;
				RefreshToken = () => connection.RefreshToken;
				Notify = callback => connection.TokensRefreshed += (_, _) => callback ();
				}
			}
		public void Dispose () => _owned.Dispose ();
		}
	private sealed class RotationHandler (bool reject) : HttpMessageHandler
		{
		internal TaskCompletionSource<bool> RefreshStarted { get; } = Signal ();
		internal TaskCompletionSource<bool> ReleaseRefresh { get; } = Signal ();
		internal TaskCompletionSource<bool> OldRequestsStarted { get; } = Signal ();
		internal TaskCompletionSource<bool> ReleaseFirstUnauthorized { get; } = Signal ();
		internal TaskCompletionSource<bool> ReleaseSecondUnauthorized { get; } = Signal ();
		internal TaskCompletionSource<bool> Disposed { get; } = Signal ();
		internal int RefreshRequests, ProductRequests;
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			if (request.RequestUri!.AbsolutePath.EndsWith ("/token", StringComparison.Ordinal))
				{
				Interlocked.Increment (ref RefreshRequests);
				_ = RefreshStarted.TrySetResult (true);
				await ReleaseRefresh.Task.ConfigureAwait (false); // Also exercise transports ignoring cancellation.
				return Response (reject ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, "{\"access_token\":\"fake-new\",\"refresh_token\":\"fake-rotated\"}");
				}
			int count = Interlocked.Increment (ref ProductRequests);
			if (request.Headers.Authorization?.Parameter == "fake-old")
				{
				if (count == 2)
					_ = OldRequestsStarted.TrySetResult (true);
				await (count == 1 ? ReleaseFirstUnauthorized.Task : ReleaseSecondUnauthorized.Task).ConfigureAwait (false);
				return Response (HttpStatusCode.Unauthorized, "{}");
				}
			Assert.That (request.Headers.Authorization?.Parameter, Is.EqualTo ("fake-new"));
			return Response (HttpStatusCode.OK, "{\"response\":[]}");
			}
		private static HttpResponseMessage Response (HttpStatusCode status, string body) => new (status) { Content = new StringContent (body) };
		private static TaskCompletionSource<bool> Signal () => new (TaskCreationOptions.RunContinuationsAsynchronously);
		protected override void Dispose (bool disposing)
			{
			base.Dispose (disposing);
			_ = Disposed.TrySetResult (true);
			}
		}
	}