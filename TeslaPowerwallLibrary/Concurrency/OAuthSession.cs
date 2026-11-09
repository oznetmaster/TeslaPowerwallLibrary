// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.Concurrency;

// One immutable snapshot publishes the access/refresh pair and refresh outcome.
// Refreshes are single-flight, including rejected refreshes. A delayed 401 carries
// its original snapshot, so it cannot refresh a token that another call replaced.
internal sealed class OAuthSession (Type owner, string? accessToken, string? refreshToken, Action cleanup) : IDisposable
	{
	internal sealed record Snapshot (string? AccessToken, string? RefreshToken, bool RefreshSucceeded = false);
	private readonly AsyncClientGate _requests = new (owner);
	private readonly AsyncClientGate _refreshes = new (owner);
	private Snapshot _state = new (accessToken, refreshToken);
	private int _disposed;
	internal Snapshot Current => Volatile.Read (ref _state);
	internal bool IsStopped => _requests.IsStopped;
	internal void Commit (Action publish) => _requests.Commit (publish);
	internal async Task<T> RequestAsync<T> (Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
		{
		using AsyncClientGate.Operation operation = await _requests.EnterAsync (exclusive: false, cancellationToken).ConfigureAwait (false);
		T result = await request (operation.Token).ConfigureAwait (false);
		operation.Token.ThrowIfCancellationRequested ();
		_requests.CheckRunning ();
		return result;
		}
	internal async Task<bool> RefreshAsync (
		Func<Snapshot, CancellationToken, Task<Snapshot?>> refresh,
		Action<ConnectionTokensRefreshedEventArgs> notify,
		CancellationToken cancellationToken,
		Snapshot? expected = null)
		{
		Snapshot observed = expected ?? Current;
		using AsyncClientGate.Operation request = await _requests.EnterAsync (exclusive: false, cancellationToken).ConfigureAwait (false);
		ConnectionTokensRefreshedEventArgs? notification = null;
		bool success;
		using (AsyncClientGate.Operation operation = await _refreshes.EnterAsync (exclusive: true, request.Token).ConfigureAwait (false))
			{
			Snapshot current = Current;
			if (!ReferenceEquals (observed, current))
				success = current.RefreshSucceeded;
			else
				{
				// Once sent, a rotating refresh must finish and retain its credentials
				// even if this particular caller cancels. Disposal still cancels it.
				Snapshot? renewed = await refresh (current, _requests.StopToken).ConfigureAwait (false);
				success = renewed is not null;
				Snapshot result = renewed is null ? current with
					{
					RefreshSucceeded = false
					} : renewed with
						{
						RefreshSucceeded = true
						};
				_requests.Commit (() => Volatile.Write (ref _state, result));
				if (success)
					notification = new (result.AccessToken, result.RefreshToken,
						!string.Equals (current.RefreshToken, result.RefreshToken, StringComparison.Ordinal));
				}
			// No Monitor is held during callbacks. Keep refresh sequencing until
			// notification finishes, otherwise an older callback could persist its
			// tokens after a newer rotation. Event handlers must be non-blocking.
			if (notification is not null)
				notify (notification);
			}
		request.Token.ThrowIfCancellationRequested ();
		_requests.CheckRunning ();
		return success;
		}
	public void Dispose ()
		{
		if (Interlocked.Exchange (ref _disposed, 1) != 0)
			return;
		Task requests = _requests.Stop ();
		Task refreshes = _refreshes.Stop ();
		_ = CleanupAsync (requests, refreshes);
		}
	private async Task CleanupAsync (Task requests, Task refreshes)
		{
		await Task.WhenAll (requests, refreshes).ConfigureAwait (false);
		cleanup ();
		}
	}