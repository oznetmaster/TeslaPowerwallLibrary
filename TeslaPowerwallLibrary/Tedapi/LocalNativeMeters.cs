// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tedapi;

public sealed partial class PowerwallTedapiClient
	{
	private const string NATIVE_STATUS_PATH = "/api/status";
	private string? _nativeToken;
	private double _nativeStatusRetryAt;

	private sealed record NativeUptime (
		[property: JsonPropertyName ("start_time")] string? StartTime,
		[property: JsonPropertyName ("up_time_seconds")] string? UpTimeSeconds);

	/// <summary>Reads the local customer meter endpoint, including lifetime energy counters when this firmware exposes them.</summary>
	/// <remarks>This is an explicit local HTTPS read, separate from TEDAPI. No cloud request is made.
	/// Setup-password modes use the configured customer password, or the final five label characters when it is absent.</remarks>
	/// <param name="force">Bypasses cached readings, but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Typed native meter readings; unavailable fields remain null.</returns>
	public async Task<MeterAggregates> GetNativeMeterAggregatesAsync (bool force = false, CancellationToken cancellationToken = default) =>
		(await ReadWithFailoverAsync<MeterAggregates?> ((recovering, token) => GetPrimaryNativeAsync<MeterAggregates> ("/api/meters/aggregates", force || recovering, token),
			async (fallback, token) => await fallback.GetNativeMeterAggregatesAsync (force, token).ConfigureAwait (false), cancellationToken).ConfigureAwait (false))!;

	private Task<NativeUptime?> GetNativeStatusAsync (bool force, CancellationToken cancellationToken) =>
		ReadWithFailoverAsync ((recovering, token) => GetPrimaryNativeAsync<NativeUptime> (NATIVE_STATUS_PATH, force || recovering, token),
			(fallback, token) => fallback.GetNativeStatusAsync (force, token), cancellationToken);

	private async Task<T?> GetPrimaryNativeAsync<T> (string path, bool force, CancellationToken cancellationToken) where T : class
		{
		bool optionalStatus = path == NATIVE_STATUS_PATH;
		using AsyncClientGate.Operation lifetime = await _lifetime.EnterAsync (exclusive: false, cancellationToken).ConfigureAwait (false);
		cancellationToken = lifetime.Token;
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			if (!force && _cache.TryGetValue (path, out var cached) && _clock.Elapsed.TotalSeconds - cached.Time < CacheExpireSeconds)
				return (T)cached.Value;
			if (optionalStatus && _clock.Elapsed.TotalSeconds < _nativeStatusRetryAt)
				return null;
			for (int attempt = 0; attempt < 2; attempt++)
				{
				if (IsSigned)
					_nativeToken = _token;
				if (_nativeToken is null)
					await LoginNativeAsync (cancellationToken).ConfigureAwait (false);
				try
					{
					var bytes = await SendAsync (HttpMethod.Get, path, null, cancellationToken, bearerOverride: _nativeToken).ConfigureAwait (false);
					var result = JsonHelper.Deserialize<T> (Encoding.UTF8.GetString (bytes))
						?? throw new PowerwallConnectionException ("The local gateway returned no data for " + path + ".");
					if (result is NativeUptime uptime && (string.IsNullOrWhiteSpace (uptime.StartTime) || string.IsNullOrWhiteSpace (uptime.UpTimeSeconds)))
						throw new PowerwallConnectionException ("The local gateway did not report both native uptime fields.");
					_cache[path] = (result, _clock.Elapsed.TotalSeconds);
					return result;
					}
				catch (LocalAuthenticationException) when (attempt == 0)
					{
					if (IsSigned)
						await LoginAsync (cancellationToken).ConfigureAwait (false);
					else
						_nativeToken = null;
					}
				}
			throw new PowerwallConnectionException ("Local customer authentication did not recover.");
			}
		catch (Exception error) when (optionalStatus && !cancellationToken.IsCancellationRequested
			&& error is PowerwallException or HttpRequestException or OperationCanceledException or JsonException)
			{
			// Older gateways can lack this endpoint or its uptime fields altogether.
			_nativeStatusRetryAt = _clock.Elapsed.TotalSeconds + 60;
			return null;
			}
		catch (JsonException exc)
			{
			throw new PowerwallConnectionException ("The local gateway returned malformed native meter data.", exc);
			}
		finally { _gate.Release (); }
		}

	private async Task LoginNativeAsync (CancellationToken cancellationToken)
		{
		string password = _options.Password;
		if (string.IsNullOrEmpty (password) && GatewayPassword.Length >= 5)
			password = GatewayPassword.Substring (GatewayPassword.Length - 5);
		if (string.IsNullOrEmpty (password))
			throw new PowerwallInvalidConfigurationException ("Native meter readings require a local customer password.");
		var login = new LocalLoginRequest
			{
			Username = "customer",
			Password = password,
			Email = Email,
			ClientInfo = new LocalClientInfo { Timezone = _options.Timezone }
			};
		var bytes = await SendAsync (HttpMethod.Post, "/api/login/Basic", Encoding.UTF8.GetBytes (JsonHelper.Serialize (login)), cancellationToken, login: true).ConfigureAwait (false);
		var response = JsonHelper.Deserialize<LocalLoginResponse> (Encoding.UTF8.GetString (bytes));
		_nativeToken = !string.IsNullOrWhiteSpace (response?.Token) ? response!.Token
			: throw new PowerwallConnectionException ("Local customer login returned no token.");
		}
	}
