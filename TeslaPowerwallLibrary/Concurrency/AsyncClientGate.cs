// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TeslaPowerwallLibrary.Concurrency;

// Fair reader/writer admission and lifetime tracking. Queued operations count as
// active until their cancellation registrations have been removed. Cancellation
// callbacks, continuations and resource cleanup never run under the Monitor.
internal sealed class AsyncClientGate (Type owner) : IDisposable
	{
#pragma warning disable IDE0330 // Keep Monitor semantics on net472 and processor Mono.
	private readonly object _sync = new ();
#pragma warning restore IDE0330
	private readonly LinkedList<Waiter> _waiting = new ();
	private readonly CancellationTokenSource _stop = new ();
	private readonly TaskCompletionSource<bool> _drained = new (TaskCreationOptions.RunContinuationsAsynchronously);
	private int _admitted, _readers;
	private bool _writer, _stopped, _cancelled, _cleaned;
	private AggregateException? _cancellationFailure;
	internal AggregateException? CancellationFailure => Volatile.Read (ref _cancellationFailure);
	internal CancellationToken StopToken => _stop.Token;
	internal bool IsStopped
		{
		get
			{
			lock (_sync)
				return _stopped;
			}
		}
	internal void CheckRunning ()
		{
		lock (_sync)
			ThrowIfStopped ();
		}
	private void ThrowIfStopped ()
		{
#if NETFRAMEWORK
		if (_stopped)
			throw new ObjectDisposedException (owner.FullName);
#else
		ObjectDisposedException.ThrowIf (_stopped, owner);
#endif
		}
	public void Dispose () => _ = Stop ();
	internal T Observe<T> (Func<T> read)
		{
		lock (_sync)
			return read ();
		}
	internal void Commit (Action publish)
		{
		lock (_sync)
			{
			ThrowIfStopped ();
			publish ();
			}
		}
	internal async Task<Operation> EnterAsync (bool exclusive, CancellationToken cancellationToken = default)
		{
		lock (_sync)
			{
			ThrowIfStopped ();
			_admitted++;
			}
		CancellationTokenSource? linked = null;
		Waiter? waiter = null;
		bool granted = false;
		try
			{
			linked = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken, _stop.Token);
			waiter = new Waiter (exclusive);
			lock (_sync)
				{
				ThrowIfStopped ();
				linked.Token.ThrowIfCancellationRequested ();
				waiter.Node = _waiting.AddLast (waiter);
				Dispatch ();
				}
			using (linked.Token.Register (() => Cancel (waiter)))
				await waiter.Ready.Task.ConfigureAwait (false);
			granted = true;
			linked.Token.ThrowIfCancellationRequested ();
			CheckRunning ();
			return new Operation (this, exclusive, linked);
			}
		catch
			{
			// A granted waiter owns its slot even if cancellation wins immediately
			// afterwards. A cancelled queued waiter has already been unlinked.
			if (!granted && waiter is not null)
				lock (_sync)
					granted = waiter.Granted;
			linked?.Dispose ();
			Exit (exclusive, granted);
			throw;
			}
		}
	private void Cancel (Waiter waiter)
		{
		lock (_sync)
			{
			if (waiter.Node is null)
				return;
			_waiting.Remove (waiter.Node);
			waiter.Node = null;
			_ = waiter.Ready.TrySetCanceled ();
			Dispatch ();
			}
		}
	private void Dispatch ()
		{
		if (_stopped || _writer)
			return;
		while (_waiting.First is { } node)
			{
			Waiter waiter = node.Value;
			if (waiter.Exclusive && _readers != 0)
				return;
			_waiting.RemoveFirst ();
			waiter.Node = null;
			waiter.Granted = true;
			if (waiter.Exclusive)
				_writer = true;
			else
				_readers++;
			_ = waiter.Ready.TrySetResult (true);
			if (waiter.Exclusive)
				return;
			}
		}
	internal Task Stop (Action? onStopping = null)
		{
		lock (_sync)
			{
			if (_stopped)
				return _drained.Task;
			_stopped = true;
			onStopping?.Invoke ();
			}
		try
			{
			_stop.Cancel ();
			}
		catch (AggregateException error)
			{
			// A failing transport cancellation callback must not strand queued
			// callers or prevent drain/cleanup. Preserve the callback failure.
			Volatile.Write (ref _cancellationFailure, error);
			}
		finally
			{
			bool cleanup;
			lock (_sync)
				{
				_cancelled = true;
				cleanup = ClaimCleanup ();
				}
			if (cleanup)
				Cleanup ();
			}
		return _drained.Task;
		}
	private bool ClaimCleanup ()
		{
		if (!_stopped || !_cancelled || _admitted != 0 || _cleaned)
			return false;
		_cleaned = true;
		return true;
		}
	private void Exit (bool exclusive, bool granted)
		{
		bool cleanup;
		lock (_sync)
			{
			if (granted)
				{
				if (exclusive)
					_writer = false;
				else
					_readers--;
				}
			_admitted--;
			Dispatch ();
			cleanup = ClaimCleanup ();
			}
		if (cleanup)
			Cleanup ();
		}
	private void Cleanup ()
		{
		_stop.Dispose ();
		_ = _drained.TrySetResult (true);
		}
	private sealed class Waiter (bool exclusive)
		{
		internal bool Exclusive { get; } = exclusive;
		internal bool Granted;
		internal LinkedListNode<Waiter>? Node;
		internal TaskCompletionSource<bool> Ready { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		}
	internal sealed class Operation (AsyncClientGate owner, bool exclusive, CancellationTokenSource linked) : IDisposable
		{
		private AsyncClientGate? _owner = owner;
		internal CancellationToken Token { get; } = linked.Token;
		public void Dispose ()
			{
			AsyncClientGate? active = Interlocked.Exchange (ref _owner, null);
			if (active is null)
				return;
			linked.Dispose ();
			active.Exit (exclusive, granted: true);
			}
		}
	}