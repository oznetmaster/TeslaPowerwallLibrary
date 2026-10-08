// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

public sealed partial class PowerwallTedapiClient
	{
	private string? _nativeToken;

	/// <summary>Reads the local customer meter endpoint, including lifetime energy counters when this firmware exposes them.</summary>
	/// <remarks>This is an explicit local HTTPS read, separate from TEDAPI. No cloud request is made.
	/// Setup-password modes use the configured customer password, or the final five label characters when it is absent.</remarks>
	/// <param name="force">Bypasses cached readings, but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Typed native meter readings; unavailable fields remain null.</returns>
	public Task<MeterAggregates> GetNativeMeterAggregatesAsync (bool force = false, CancellationToken cancellationToken = default) =>
		ReadWithFailoverAsync (recovering => GetPrimaryNativeMeterAggregatesAsync (force || recovering, cancellationToken),
			fallback => fallback.GetNativeMeterAggregatesAsync (force, cancellationToken), cancellationToken);

	private async Task<MeterAggregates> GetPrimaryNativeMeterAggregatesAsync (bool force, CancellationToken cancellationToken)
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			if (!force && _cache.TryGetValue ("nativeMeters", out var cached) && _clock.Elapsed.TotalSeconds - cached.Time < CacheExpireSeconds)
				return (MeterAggregates)cached.Value;
			for (int attempt = 0; attempt < 2; attempt++)
				{
				if (IsSigned) _nativeToken = _token;
				if (_nativeToken is null) await LoginNativeAsync (cancellationToken).ConfigureAwait (false);
				try
					{
					var bytes = await SendAsync (HttpMethod.Get, "/api/meters/aggregates", null, cancellationToken, bearerOverride: _nativeToken).ConfigureAwait (false);
					var meters = JsonHelper.Deserialize<MeterAggregates> (Encoding.UTF8.GetString (bytes))
						?? throw new PowerwallConnectionException ("The local gateway returned no native meter data.");
					_cache["nativeMeters"] = (meters, _clock.Elapsed.TotalSeconds);
					return meters;
					}
				catch (LocalAuthenticationException) when (attempt == 0)
					{
					if (IsSigned) await LoginAsync (cancellationToken).ConfigureAwait (false);
					else _nativeToken = null;
					}
				}
			throw new PowerwallConnectionException ("Local meter authentication did not recover.");
			}
		catch (System.Text.Json.JsonException exc)
			{ throw new PowerwallConnectionException ("The local gateway returned malformed native meter data.", exc); }
		finally { _gate.Release (); }
		}

	private async Task LoginNativeAsync (CancellationToken cancellationToken)
		{
		string password = _options.Password;
		if (string.IsNullOrEmpty (password) && GatewayPassword.Length >= 5)
			password = GatewayPassword.Substring (GatewayPassword.Length - 5);
		if (string.IsNullOrEmpty (password))
			throw new PowerwallInvalidConfigurationException ("Native meter readings require a local customer password.");
		var login = new LocalLoginRequest { Username = "customer", Password = password, Email = Email,
			ClientInfo = new LocalClientInfo { Timezone = _options.Timezone } };
		var bytes = await SendAsync (HttpMethod.Post, "/api/login/Basic", Encoding.UTF8.GetBytes (JsonHelper.Serialize (login)), cancellationToken, login: true).ConfigureAwait (false);
		var response = JsonHelper.Deserialize<LocalLoginResponse> (Encoding.UTF8.GetString (bytes));
		_nativeToken = !string.IsNullOrWhiteSpace (response?.Token) ? response!.Token
			: throw new PowerwallConnectionException ("Local customer login returned no token.");
		}
	}
