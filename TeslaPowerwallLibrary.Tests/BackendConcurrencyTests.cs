// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.FleetApi;
using TeslaPowerwallLibrary.Models;

using static TeslaPowerwallLibrary.Tests.ConcurrencyTestAwait;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class BackendConcurrencyTests
	{
	[TestCase (false)]
	[TestCase (true)]
	public async Task OlderReadFinishingLast_CannotReplaceNewerCachedSnapshot (bool fleet)
		{
		using var rig = new Rig (fleet);
		Task<string?> first = rig.Client.PollAsync ("/api/operation", force: true);
		await Ready (rig.Handler.FirstEntered.Task);
		Task<string?> second = rig.Client.PollAsync ("/api/operation", force: true);
		await Ready (rig.Handler.SecondEntered.Task);
		_ = rig.Handler.ReleaseSecond.TrySetResult (true);
		string? newest = await Ready (second);
		_ = rig.Handler.ReleaseFirst.TrySetResult (true);
		string? oldest = await Ready (first);
		Assert.That (oldest, Is.Not.EqualTo (newest));
		Assert.That (await rig.Client.PollAsync ("/api/operation"), Is.EqualTo (newest));
		Assert.That (rig.Handler.Reads, Is.EqualTo (2));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task WriteWaitsForRead_ThenInvalidatesItsConfiguration (bool fleet)
		{
		using var rig = new Rig (fleet);
		Task<string?> read = rig.Client.PollAsync ("/api/operation");
		await Ready (rig.Handler.FirstEntered.Task);
		Task<string?> write = rig.Client.PostAsync ("/api/operation", new
			{
			backup_reserve_percent = 30
			});
		Assert.That (rig.Handler.Writes, Is.Zero);
		Assert.That (write.IsCompleted, Is.False);
		_ = rig.Handler.ReleaseFirst.TrySetResult (true);
		await Ready (read);
		await Ready (write);
		_ = rig.Handler.ReleaseSecond.TrySetResult (true);
		string? updated = await rig.Client.PollAsync ("/api/operation");
		Assert.That (JsonHelper.Deserialize<OperationResponse> (updated!)!.BackupReservePercent, Is.EqualTo (33.5).Within (0.001));
		Assert.That (rig.Handler.Reads, Is.EqualTo (2));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task SiteSwitchDrainsOldRead_AndDiscardsItsCache (bool fleet)
		{
		using var rig = new Rig (fleet);
		Task<string?> read = rig.Client.PollAsync ("/api/operation");
		await Ready (rig.Handler.FirstEntered.Task);
		Task<bool> change = rig.ChangeSite ("456");
		Assert.That (change.IsCompleted, Is.False);
		_ = rig.Handler.ReleaseFirst.TrySetResult (true);
		await Ready (read);
		Assert.That (await Ready (change), Is.True);
		_ = rig.Handler.ReleaseSecond.TrySetResult (true);
		string? updated = await rig.Client.PollAsync ("/api/operation");
		Assert.That (JsonHelper.Deserialize<OperationResponse> (updated!)!.BackupReservePercent, Is.EqualTo (99.05).Within (0.001));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task DisposeCancelsQueuedChange_AndRejectsLateCachePublication (bool fleet)
		{
		using var rig = new Rig (fleet);
		Task<string?> read = rig.Client.PollAsync ("/api/operation");
		await Ready (rig.Handler.FirstEntered.Task);
		Task<bool> change = rig.ChangeSite ("456");
		rig.Dispose ();
		try
			{
			await Ready (Stopped (change));
			}
		finally { _ = rig.Handler.ReleaseFirst.TrySetResult (true); }
		await Ready (Stopped (read));
		await Ready (rig.Handler.Disposed.Task);
		await Assert.ThatAsync (() => rig.Client.PollAsync ("/api/operation"), Throws.TypeOf<ObjectDisposedException> ());
		}

	private static async Task Stopped (Task task)
		{
		try
			{
			await task.ConfigureAwait (false);
			}
		catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { return; }
		Assert.Fail ("Stopped backend call succeeded.");
		}
	private sealed class Rig : IDisposable
		{
		internal StateHandler Handler { get; } = new ();
		internal PowerwallClientBase Client
			{
			get;
			}
		internal Func<string, Task<bool>> ChangeSite
			{
			get;
			}
		internal Rig (bool fleet)
			{
			if (fleet)
				{
				var connection = new FleetApiConnection ("fake-client", "fake-token", null, "https://fleet.invalid", TimeSpan.FromSeconds (10), Handler);
				var client = new PowerwallFleetApiClient ("unit@example.invalid", "fake-client", 30, TimeSpan.FromSeconds (10), noFleetApiTokenPersistence: true);
				OperationRegressionTests.SetField (client, "_connection", connection);
				Client = client;
				ChangeSite = site => client.ChangeSiteAsync (site);
				}
			else
				{
				var connection = new TeslaCloudConnection ("fake-token", null, TimeSpan.FromSeconds (10));
				((HttpClient)OperationRegressionTests.GetField (connection, "_httpClient")).Dispose ();
				OperationRegressionTests.SetField (connection, "_httpClient", new HttpClient (Handler));
				var client = new PowerwallCloudClient ("unit@example.invalid", 30, TimeSpan.FromSeconds (10), noCloudTokenPersistence: true);
				OperationRegressionTests.SetField (client, "_connection", connection);
				Client = client;
				ChangeSite = site => client.ChangeSiteAsync (site);
				}
			OperationRegressionTests.SetField (Client, "_resolvedSiteId", "123");
			}
		public void Dispose () => ((IDisposable)Client).Dispose ();
		}
	private sealed class StateHandler : HttpMessageHandler
		{
		internal TaskCompletionSource<bool> FirstEntered { get; } = Signal ();
		internal TaskCompletionSource<bool> SecondEntered { get; } = Signal ();
		internal TaskCompletionSource<bool> ReleaseFirst { get; } = Signal ();
		internal TaskCompletionSource<bool> ReleaseSecond { get; } = Signal ();
		internal TaskCompletionSource<bool> Disposed { get; } = Signal ();
		internal int Reads, Writes;
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			string path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith ("/products", StringComparison.Ordinal))
				return Reply ("{\"response\":[{\"energy_site_id\":123,\"resource_type\":\"battery\"},{\"energy_site_id\":456,\"resource_type\":\"battery\"}]}");
			if (request.Method == HttpMethod.Post)
				{
				Interlocked.Increment (ref Writes);
				return Reply ("{\"response\":{\"code\":201,\"message\":\"updated\"}}");
				}
			int count = Interlocked.Increment (ref Reads);
			int reserve = path.Contains ("/456/") ? 99 : count == 1 ? 20 : 30;
			_ = (count == 1 ? FirstEntered : SecondEntered).TrySetResult (true);
			await (count == 1 ? ReleaseFirst.Task : ReleaseSecond.Task); // Intentionally tolerate cancellation.
			return Reply ("{\"response\":{\"backup_reserve_percent\":" + reserve + ",\"default_real_mode\":\"self_consumption\"}}");
			}
		private static HttpResponseMessage Reply (string body) => new (HttpStatusCode.OK) { Content = new StringContent (body) };
		private static TaskCompletionSource<bool> Signal () => new (TaskCreationOptions.RunContinuationsAsynchronously);
		protected override void Dispose (bool disposing)
			{
			base.Dispose (disposing);
			_ = Disposed.TrySetResult (true);
			}
		}
	}