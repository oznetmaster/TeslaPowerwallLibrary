// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Local;

namespace TeslaPowerwallLibrary.Tedapi;

public sealed partial class PowerwallTedapiClient
	{
	private readonly SemaphoreSlim _readRouteGate = new (1, 1);
	private readonly Func<double> _readRouteClock;
	private string? _readRouteIdentity;
	private int _primaryReadFailures;
	private double _primaryReadRetryAt;
	private volatile bool _readFallbackActive;
	private volatile bool _primaryNeedsAuthentication;

	/// <summary>Whether supported reads currently use the explicitly configured setup-network connection.</summary>
	/// <remarks>This reports routing, not whether the last read succeeded. Signed-only reads and all controls remain on LAN.</remarks>
	public bool IsUsingLocalReadFallback => _readFallbackActive;

	private static bool CanFailOver (Exception error, CancellationToken cancellationToken) =>
		!cancellationToken.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or LocalTransportUnavailableException;

	private async Task AuthenticateWithReadFailoverAsync (CancellationToken cancellationToken)
		{
		if (!_options.EnableLocalReadFailover)
			{
			await AuthenticatePrimaryAsync (cancellationToken).ConfigureAwait (false);
			return;
			}
		await _readRouteGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			_readFallbackActive = false;
			_primaryReadFailures = 0;
			_primaryNeedsAuthentication = true;
			try
				{
				await AuthenticatePrimaryAsync (cancellationToken).ConfigureAwait (false);
				ValidatePrimaryIdentity ();
				_primaryNeedsAuthentication = false;
				}
			catch (Exception error) when (CanFailOver (error, cancellationToken))
				{
				var fallback = MatchingFallback ();
				_din = fallback.DeviceIdentificationNumber;
				TripReadFallback ();
				}
			}
		finally { _readRouteGate.Release (); }
		}

	private async Task<T> ReadWithFailoverAsync<T> (Func<bool, Task<T>> primary, Func<PowerwallTedapiClient, Task<T>> alternate, CancellationToken cancellationToken)
		{
		if (!_options.EnableLocalReadFailover)
			return await primary (false).ConfigureAwait (false);
		await _readRouteGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			cancellationToken.ThrowIfCancellationRequested ();
			EnsureConnected ();
			bool recovering = _readFallbackActive;
			if (recovering && _readRouteClock () < _primaryReadRetryAt)
				return await alternate (MatchingFallback ()).ConfigureAwait (false);
			try
				{
				if (_primaryNeedsAuthentication)
					{
					await AuthenticatePrimaryAsync (cancellationToken).ConfigureAwait (false);
					ValidatePrimaryIdentity ();
					_primaryNeedsAuthentication = false;
					}
				T value = await primary (recovering).ConfigureAwait (false);
				_primaryReadFailures = 0;
				_readFallbackActive = false;
				if (recovering) await ClearReadCacheAsync (cancellationToken).ConfigureAwait (false);
				return value;
				}
			catch (Exception error) when (CanFailOver (error, cancellationToken))
				{
				// Authentication clears DIN before sending. Retain only the already established route identity.
				_din = _readRouteIdentity;
				if (++_primaryReadFailures < 3 && !recovering) throw;
				TripReadFallback ();
				await ClearReadCacheAsync (cancellationToken).ConfigureAwait (false);
				return await alternate (MatchingFallback ()).ConfigureAwait (false);
				}
			}
		finally { _readRouteGate.Release (); }
		}

	private void TripReadFallback ()
		{
		_primaryReadFailures = 3;
		_readFallbackActive = true;
		_primaryReadRetryAt = _readRouteClock () + _options.LocalReadFailoverRetryInterval.TotalSeconds;
		}

	private PowerwallTedapiClient MatchingFallback ()
		{
		var fallback = _options.LocalFollowerConnection!;
		fallback.EnsureConnected ();
		if (_readRouteIdentity is not null && !string.Equals (_readRouteIdentity, fallback._din, StringComparison.Ordinal))
			throw new PowerwallInvalidConfigurationException ("The setup-network connection identifies a different Powerwall. Read failover was refused.");
		_readRouteIdentity ??= fallback._din;
		return fallback;
		}

	private void ValidatePrimaryIdentity ()
		{
		string? expected = _readRouteIdentity ?? _options.LocalFollowerConnection?.DeviceIdentificationNumber;
		if (expected is not null && !string.Equals (expected, _din, StringComparison.Ordinal))
			{
			_din = null;
			_token = null;
			throw new PowerwallInvalidConfigurationException ("The LAN and setup-network connections identify different Powerwalls. Connection was refused.");
			}
		_readRouteIdentity = _din;
		}

	private async Task ClearReadCacheAsync (CancellationToken cancellationToken)
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try { _cache.Clear (); }
		finally { _gate.Release (); }
		}

	private async Task CloseReadRouteAsync (CancellationToken cancellationToken)
		{
		await _readRouteGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			await ClosePrimarySessionAsync (cancellationToken).ConfigureAwait (false);
			_readFallbackActive = false;
			_primaryNeedsAuthentication = false;
			_primaryReadFailures = 0;
			}
		finally { _readRouteGate.Release (); }
		}

	/// <summary>A server response eligible for read-only transport failover; authentication and backoff are excluded.</summary>
	private sealed class LocalTransportUnavailableException (int statusCode)
		: PowerwallConnectionException ("Local gateway request failed with HTTP " + statusCode + ".");
	}
