using TeslaPowerwallLibrary.Models;
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using System.Text.Json;
using System.Text.Json.Serialization;


namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Encapsulates the Tesla™ Owners API connection: OAuth access-token refresh against the Tesla SSO
/// service and authenticated energy-site REST calls against <c>owner-api.teslamotors.com</c>.
/// Adapts the relevant behavior of the upstream <c>teslapy</c> library used by <c>pypowerwall</c>.
/// </summary>
internal sealed class TeslaCloudConnection : IDisposable
	{
	private const string SSO_BASE_URL = "https://auth.tesla.com/";
	private const string OWNER_API_BASE_URL = "https://owner-api.teslamotors.com/";
	private const string SSO_CLIENT_ID = "ownerapi";
	private const string TOKEN_ENDPOINT = "oauth2/v3/token";
	private const string O_AUTH_SCOPE = "openid email offline_access";

	private readonly ILogger _log;

	private readonly HttpClient _httpClient;
	private string? _accessToken;
	private string? _refreshToken;

	/// <summary>
	/// Raised after every successful access-token refresh, carrying the current tokens and whether the
	/// refresh token itself changed. Raised on the calling (possibly background) thread.
	/// </summary>
	public event EventHandler<ConnectionTokensRefreshedEventArgs>? TokensRefreshed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TeslaCloudConnection"/> class.
	/// </summary>
	/// <param name="accessToken">Tesla Owners API OAuth access token, when already available.</param>
	/// <param name="refreshToken">Tesla Owners API OAuth refresh token used to renew the access token.</param>
	/// <param name="timeout">Per-request HTTP timeout.</param>
	/// <param name="logger">Caller-owned logger; null disables logging.</param>
	public TeslaCloudConnection (string? accessToken, string? refreshToken, TimeSpan timeout, ILogger? logger = null)
		{
		_log = logger ?? NullLogger.Instance;
		_accessToken = string.IsNullOrWhiteSpace (accessToken) ? null : accessToken;
		_refreshToken = string.IsNullOrWhiteSpace (refreshToken) ? null : refreshToken;
		AccessTokenProvidedAtConstruction = _accessToken is not null;
		_httpClient = new HttpClient { Timeout = timeout };
		}

	/// <summary>Gets a value indicating whether any usable token (access or refresh) is available.</summary>
	public bool HasToken => _accessToken is not null || _refreshToken is not null;

	/// <summary>
	/// Gets a value indicating whether a non-null access token was supplied to the constructor (as opposed to
	/// this connection having bootstrapped its first access token from the refresh token alone).
	/// </summary>
	public bool AccessTokenProvidedAtConstruction
		{
		get;
		}

	/// <summary>Gets the current access token, which may be renewed after a refresh.</summary>
	public string? AccessToken => _accessToken;

	/// <summary>Gets the current refresh token, which may be rotated after a refresh.</summary>
	public string? RefreshToken => _refreshToken;

	/// <summary>
	/// Renews the access token using the refresh token against the Tesla SSO service. Always raises
	/// <see cref="TokensRefreshed"/> on success, reporting whether the refresh token itself was rotated.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when a new access token was obtained; otherwise <see langword="false"/>.</returns>
	public async Task<bool> RefreshAccessTokenAsync (CancellationToken cancellationToken = default)
		{
		if (_refreshToken is null)
			return false;

		var body = new CloudRefreshRequest
			{
			GrantType = "refresh_token",
			ClientId = SSO_CLIENT_ID,
			RefreshToken = _refreshToken,
			Scope = O_AUTH_SCOPE
			};

		HttpResponseMessage response;
		try
			{
			using var request = new HttpRequestMessage (HttpMethod.Post, SSO_BASE_URL + TOKEN_ENDPOINT)
				{
				Content = new StringContent (JsonHelper.Serialize (body), Encoding.UTF8, "application/json")
				};
			response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
			LibraryLog.UnableToRefreshTeslaCloudToken (_log, exc.Message);
			return false;
			}

		using (response)
			{
#if NETFRAMEWORK
			var payload = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
#else
			var payload = await response.Content.ReadAsStringAsync (cancellationToken).ConfigureAwait (false);
#endif
			if (!response.IsSuccessStatusCode)
				{
				LibraryLog.TeslaCloudTokenRefreshFailedHTTP (_log, (int)response.StatusCode);
				return false;
				}

			try
				{
				var tokens = JsonHelper.Deserialize<TeslaCloudTokenResponse> (payload);
				if (tokens is null || string.IsNullOrWhiteSpace (tokens.AccessToken))
					{
					LibraryLog.TeslaCloudTokenRefreshResponseDidNotContainAn (_log);
					return false;
					}

				var priorRefreshToken = _refreshToken;
				_accessToken = tokens.AccessToken;
				if (!string.IsNullOrWhiteSpace (tokens.RefreshToken))
					_refreshToken = tokens.RefreshToken;

				LibraryLog.TeslaCloudAccessTokenRefreshed (_log);

				var refreshTokenChanged = !string.Equals (priorRefreshToken, _refreshToken, StringComparison.Ordinal);
				TokensRefreshed?.Invoke (this, new ConnectionTokensRefreshedEventArgs (_accessToken, _refreshToken, refreshTokenChanged));

				return true;
				}
			catch (JsonException exc)
				{
				LibraryLog.UnableToParseTeslaCloudTokenRefreshResponse (_log, exc.Message);
				return false;
				}
			}
		}

	/// <summary>
	/// Retrieves the list of Tesla energy products (batteries and solar) for the account.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The product list from the <c>response</c> envelope, or <see langword="null"/> when unavailable.</returns>
	public async Task<List<EnergyProduct>?> GetProductsAsync (CancellationToken cancellationToken = default)
		{
		string? response = await SendApiAsync (HttpMethod.Get, "api/1/products", null, null, cancellationToken).ConfigureAwait (false);
		return ToTypedResponse<List<EnergyProduct>> (response);
		}

	/// <summary>Retrieves the site configuration (<c>site_info</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<SiteConfigResponse?> GetSiteConfigAsync (string siteId, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<SiteConfigResponse> (siteId, "site_info", new Dictionary<string, string> { ["language"] = "en" }, cancellationToken);

	/// <summary>Retrieves the live site power data (<c>live_status</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="counter">Rolling request counter mirrored from the upstream SITE_DATA API.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<SitePowerResponse?> GetSitePowerAsync (string siteId, int counter, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<SitePowerResponse> (
			siteId,
			"live_status",
			new Dictionary<string, string>
				{
				["counter"] = counter.ToString (CultureInfo.InvariantCulture),
				["language"] = "en"
				},
			cancellationToken);

	/// <summary>Retrieves the battery summary (<c>site_status</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<SiteSummaryResponse?> GetSiteSummaryAsync (string siteId, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<SiteSummaryResponse> (siteId, "site_status", new Dictionary<string, string> { ["language"] = "en" }, cancellationToken);

	/// <summary>Retrieves the estimated backup time remaining for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<BackupTimeRemainingResponse?> GetBackupTimeRemainingAsync (string siteId, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<BackupTimeRemainingResponse> (siteId, "backup_time_remaining", new Dictionary<string, string> { ["language"] = "en" }, cancellationToken);

	/// <summary>Retrieves energy history (<c>history</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="kind">The history kind (for example <c>power</c>, <c>energy</c>, <c>backup</c>, or <c>self_consumption</c>).</param>
	/// <param name="period">The aggregation period (for example <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>).</param>
	/// <param name="timeZone">IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when unavailable.</returns>
	public Task<string?> GetHistoryAsync (
		string siteId,
		string? kind = null,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync (siteId, "history", BuildHistoryQuery (kind, period, timeZone, startDate, endDate), cancellationToken);

	/// <summary>Retrieves calendar-aligned energy history (<c>calendar_history</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="kind">The history kind (for example <c>power</c>, <c>energy</c>, <c>soe</c>, <c>backup</c>, <c>self_consumption</c>, <c>time_of_use_energy</c>, or <c>savings</c>).</param>
	/// <param name="period">The aggregation period (for example <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>).</param>
	/// <param name="timeZone">IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when unavailable.</returns>
	public Task<string?> GetCalendarHistoryAsync (
		string siteId,
		string? kind = null,
		string? period = null,
		string? timeZone = null,
		string? startDate = null,
		string? endDate = null,
		CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync (siteId, "calendar_history", BuildHistoryQuery (kind, period, timeZone, startDate, endDate), cancellationToken);

	private static Dictionary<string, string> BuildHistoryQuery (string? kind, string? period, string? timeZone, string? startDate, string? endDate)
		{
		var query = new Dictionary<string, string> ();
		if (!string.IsNullOrWhiteSpace (kind))
			query["kind"] = kind!;
		if (!string.IsNullOrWhiteSpace (period))
			query["period"] = period!;
		if (!string.IsNullOrWhiteSpace (timeZone))
			query["time_zone"] = timeZone!;
		if (!string.IsNullOrWhiteSpace (startDate))
			query["start_date"] = startDate!;
		if (!string.IsNullOrWhiteSpace (endDate))
			query["end_date"] = endDate!;

		return query;
		}

	/// <summary>Sets the backup reserve percentage for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="percent">The reserve percentage to apply (0 - 100).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when the call fails.</returns>
	public async Task<ApiResponse<object>?> SetBackupReserveAsync (string siteId, int percent, CancellationToken cancellationToken = default)
		{
		var body = new BackupReserveRequest { BackupReservePercent = percent };
		var uri = $"api/1/energy_sites/{siteId}/backup";
		return JsonHelper.DeserializeOrNull<ApiResponse<object>> (await SendApiAsync (HttpMethod.Post, uri, body, null, cancellationToken).ConfigureAwait (false));
		}

	/// <summary>Sets the battery operation mode for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="mode">The operation mode (for example <c>self_consumption</c>, <c>backup</c>, or <c>autonomous</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when the call fails.</returns>
	public async Task<ApiResponse<object>?> SetOperationModeAsync (string siteId, string mode, CancellationToken cancellationToken = default)
		{
		var body = new OperationModeRequest { DefaultRealMode = mode };
		var uri = $"api/1/energy_sites/{siteId}/operation";
		return JsonHelper.DeserializeOrNull<ApiResponse<object>> (await SendApiAsync (HttpMethod.Post, uri, body, null, cancellationToken).ConfigureAwait (false));
		}

	/// <summary>Updates the grid import/export configuration (grid charging and export rules) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="settings">The grid import/export settings to apply (for example <c>disallow_charge_from_grid_with_solar_installed</c> or <c>customer_preferred_export_rule</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when the call fails.</returns>
	public async Task<ApiResponse<object>?> SetGridImportExportAsync (string siteId, GridImportExportRequest settings, CancellationToken cancellationToken = default)
		{
		var uri = $"api/1/energy_sites/{siteId}/grid_import_export";
		return JsonHelper.DeserializeOrNull<ApiResponse<object>> (await SendApiAsync (HttpMethod.Post, uri, settings, null, cancellationToken).ConfigureAwait (false));
		}

	/// <summary>Enables or disables Storm Watch (predictive storm pre-charging) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="enabled"><see langword="true"/> to enable Storm Watch; <see langword="false"/> to disable it.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when the call fails.</returns>
	public async Task<ApiResponse<object>?> SetStormModeAsync (string siteId, bool enabled, CancellationToken cancellationToken = default)
		{
		var body = new StormModeRequest { Enabled = enabled };
		var uri = $"api/1/energy_sites/{siteId}/storm_mode";
		return JsonHelper.DeserializeOrNull<ApiResponse<object>> (await SendApiAsync (HttpMethod.Post, uri, body, null, cancellationToken).ConfigureAwait (false));
		}

	private async Task<string?> GetSiteEndpointAsync (string siteId, string segment, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
		{
		var uri = $"api/1/energy_sites/{siteId}/{segment}";
		return await SendApiAsync (HttpMethod.Get, uri, null, query, cancellationToken).ConfigureAwait (false);
		}

	private async Task<T?> GetSiteEndpointAsync<T> (string siteId, string segment, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
		where T : class
		{
		var uri = $"api/1/energy_sites/{siteId}/{segment}";
		string? response = await SendApiAsync (HttpMethod.Get, uri, null, query, cancellationToken).ConfigureAwait (false);
		return ToTypedResponse<T> (response);
		}

	private T? ToTypedResponse<T> (string? payload) where T : class
		{
		if (string.IsNullOrWhiteSpace (payload))
			return null;
		try
			{
			return JsonHelper.Deserialize<ApiResponse<T>> (payload!)?.Response;
			}
		catch (JsonException)
			{
			LibraryLog.UnableToMapAPIResponseTo (_log, typeof (T).Name);
			return null;
			}
		}

	private async Task<string?> SendApiAsync (
		HttpMethod method,
		string uri,
		object? jsonBody,
		IReadOnlyDictionary<string, string>? query,
		CancellationToken cancellationToken,
		bool allowRetry = true)
		{
		var url = OWNER_API_BASE_URL + uri;
		if (query is { Count: > 0 })
			url += "?" + string.Join ("&", query.Select (static kv => $"{Uri.EscapeDataString (kv.Key)}={Uri.EscapeDataString (kv.Value)}"));

		HttpResponseMessage response;
		try
			{
			using var request = new HttpRequestMessage (method, url);
			if (_accessToken is not null)
				request.Headers.TryAddWithoutValidation ("Authorization", $"Bearer {_accessToken}");

			if (jsonBody is not null)
				request.Content = new StringContent (JsonHelper.Serialize (jsonBody), Encoding.UTF8, "application/json");

			response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			LibraryLog.TimeoutWaitingForTeslaCloudAPI (_log, uri);
			return null;
			}
		catch (HttpRequestException exc)
			{
			LibraryLog.UnableToConnectToTeslaCloudAPI (_log, uri, exc.Message);
			return null;
			}

		using (response)
			{
			if ((int)response.StatusCode is 401 or 403 && allowRetry)
				{
				LibraryLog.TeslaCloudSessionExpiredAttemptingTokenRefresh (_log);
				if (await RefreshAccessTokenAsync (cancellationToken).ConfigureAwait (false))
					return await SendApiAsync (method, uri, jsonBody, query, cancellationToken, allowRetry: false).ConfigureAwait (false);

				LibraryLog.TeslaCloudAPIUnauthorizedAndTokenRefreshFailedRun (_log, uri);
				return null;
				}

			var payload = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
			if (!response.IsSuccessStatusCode)
				{
				if ((int)response.StatusCode == 410)
					{
					LibraryLog.TeslaCloudAPIReturnedHTTPGoneEndpointPermanentlyRemoved (_log, uri);
					throw new PowerwallCloudEndpointRemovedException (ExtractServerError (payload)
						?? $"The Tesla cloud endpoint '{uri}' has been permanently removed (HTTP 410 Gone).");
					}

				LibraryLog.TeslaCloudAPIReturnedHTTP (_log, uri, (int)response.StatusCode);
				return null;
				}

			if (string.IsNullOrWhiteSpace (payload))
				return null;

			return payload;
			}
		}

	private static string? ExtractServerError (string? payload) =>
		  JsonHelper.DeserializeOrNull<ApiError> (payload)?.Error;

	/// <summary>Releases the underlying <see cref="HttpClient"/>.</summary>
	public void Dispose () => _httpClient.Dispose ();
	}