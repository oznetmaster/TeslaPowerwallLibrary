// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Services;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Checks refresh recovery independently of a running UI, wall clock or physical device.</summary>
[TestFixture]
public sealed class PollingRecoveryTests
	{
	/// <summary>A request timeout reports one failure, waits, and permits the following successful read.</summary>
	[Test]
	public void RequestTimeout_DoesNotStopScheduledReads ()
		{
		using var connection = new PowerwallConnectionService ();
		using var cancel = new CancellationTokenSource ();
		int reads = 0, delays = 0, snapshots = 0;
		var errors = new List<string> ();
		connection.PollFailed += (_, message) => errors.Add (message);
		connection.SnapshotUpdated += (_, _) => { snapshots++; cancel.Cancel (); };
		Task<PowerFlowSnapshot> Read (CancellationToken token)
			{
			reads++;
			return reads == 1 ? Task.FromException<PowerFlowSnapshot> (new TaskCanceledException ("HTTP timeout"))
				: Task.FromResult (PowerFlowSnapshot.FromLocal (new TeslaPowerwallLibrary.Tedapi.LocalTelemetry ()));
			}
		Task Delay (TimeSpan interval, CancellationToken token)
			{
			Assert.That (interval, Is.GreaterThan (TimeSpan.Zero));
			token.ThrowIfCancellationRequested ();
			delays++;
			return Task.CompletedTask;
			}
		Assert.CatchAsync<OperationCanceledException> (async () => await connection.PollLoopAsync (Read, Delay, cancel.Token));
		Assert.That (reads, Is.EqualTo (2));
		Assert.That (delays, Is.EqualTo (1));
		Assert.That (snapshots, Is.EqualTo (1));
		Assert.That (errors, Has.Count.EqualTo (1));
		Assert.That (errors[0], Does.Contain ("timed out"));
		}

	/// <summary>Consumer cancellation stops immediately and is not reported as a network failure.</summary>
	[Test]
	public void ConsumerCancellation_DoesNotRetryOrReportFailure ()
		{
		using var connection = new PowerwallConnectionService ();
		using var cancel = new CancellationTokenSource ();
		int reads = 0, delays = 0, failures = 0;
		connection.PollFailed += (_, _) => failures++;
		Task<PowerFlowSnapshot> Read (CancellationToken token)
			{
			reads++;
			cancel.Cancel ();
			return Task.FromCanceled<PowerFlowSnapshot> (token);
			}
		Task Delay (TimeSpan interval, CancellationToken token) { delays++; return Task.CompletedTask; }
		Assert.CatchAsync<OperationCanceledException> (async () => await connection.PollLoopAsync (Read, Delay, cancel.Token));
		Assert.That (reads, Is.EqualTo (1));
		Assert.That (delays, Is.Zero);
		Assert.That (failures, Is.Zero);
		}
	}
