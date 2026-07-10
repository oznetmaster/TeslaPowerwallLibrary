// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

using log4net;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.FleetApi;

/// <summary>
/// Encapsulates the Tesla™ FleetAPI connection: refresh-token-based OAuth access-token renewal against the
/// Tesla SSO service, and authenticated energy-site/profile REST calls against the regional FleetAPI base
/// URL. Adapts the relevant behavior of the upstream <c>fleetapi.FleetAPI</c> class used by
/// <c>pypowerwall</c>. The <c>site_info</c>/<c>live_status</c>/<c>site_status</c> response shapes are
/// identical to the Tesla Owners (cloud) API, so this type reuses the existing internal
/// <see cref="TeslaPowerwallLibrary.Cloud"/> response models instead of duplicating them.
/// </summary>
/// <remarks>
/// FleetAPI mode is token-based: the caller supplies a Client ID and, on the first run, a refresh token
/// obtained externally (there is no interactive login helper). <see cref="TeslaFleetApiTokenCache"/> persists
/// the client id, tokens, and selected site so later runs do not need to supply the refresh token again.
/// Mirroring the upstream refresh-token grant, only the client id and refresh token are sent when renewing
/// an access token - no client secret and no scope parameter.
/// </remarks>
internal sealed class FleetApiConnection : IDisposable
	{
	private const string SSO_BASE_URL = "https://auth.tesla.com/";
	private const string TOKEN_ENDPOINT = "oauth2/v3/token";

	private static readonly ILog _log = LogManager.GetLogger (typeof (FleetApiConnection));

	private readonly HttpClient _httpClient;
	private readonly string _clientId;
	private readonly string _baseUrl;
	private string? _accessToken;
	private string? _refreshToken;

	/// <summary>
	/// Raised after every successful access-token refresh, carrying the current tokens and whether the
	/// refresh token itself changed. Raised on the calling (possibly background) thread.
	/// </summary>
	public event EventHandler<ConnectionTokensRefreshedEventArgs>? TokensRefreshed;

	/// <summary>
	/// Initializes a new instance of the <see cref="FleetApiConnection"/> class.
	/// </summary>
	/// <param name="clientId">Tesla FleetAPI application Client ID.</param>
	/// <param name="accessToken">Tesla FleetAPI OAuth access token, when already available.</param>
	/// <param name="refreshToken">Tesla FleetAPI OAuth refresh token used to renew the access token.</param>
	/// <param name="baseUrl">The regional FleetAPI base URL (see <see cref="FleetApiRegions"/>).</param>
	/// <param name="timeout">Per-request HTTP timeout.</param>
	public FleetApiConnection (string clientId, string? accessToken, string? refreshToken, string baseUrl, TimeSpan timeout)
		{
		_clientId = clientId;
		_baseUrl = baseUrl.TrimEnd ('/');
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
	public bool AccessTokenProvidedAtConstruction { get; }

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

		var body = new JObject
			{
			["grant_type"] = "refresh_token",
			["client_id"] = _clientId,
			["refresh_token"] = _refreshToken
			};

		HttpResponseMessage response;
		try
			{
			using var request = new HttpRequestMessage (HttpMethod.Post, SSO_BASE_URL + TOKEN_ENDPOINT)
				{
				Content = new StringContent (body.ToString (Formatting.None), Encoding.UTF8, "application/json")
				};
			response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
			_log.Error ($"Unable to refresh Tesla FleetAPI token: {exc.Message}");
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
				_log.Error ($"Tesla FleetAPI token refresh failed (HTTP {(int) response.StatusCode}).");
				return false;
				}

			try
				{
				var tokens = JsonConvert.DeserializeObject<TeslaCloudTokenResponse> (payload);
				if (tokens is null || string.IsNullOrWhiteSpace (tokens.AccessToken))
					{
					_log.Error ("Tesla FleetAPI token refresh response did not contain an access token.");
					return false;
					}

				var priorRefreshToken = _refreshToken;
				_accessToken = tokens.AccessToken;
				if (!string.IsNullOrWhiteSpace (tokens.RefreshToken))
					_refreshToken = tokens.RefreshToken;

				_log.Debug ("Tesla FleetAPI access token refreshed.");

				var refreshTokenChanged = !string.Equals (priorRefreshToken, _refreshToken, StringComparison.Ordinal);
				TokensRefreshed?.Invoke (this, new ConnectionTokensRefreshedEventArgs (_accessToken, _refreshToken, refreshTokenChanged));

				return true;
				}
			catch (JsonException exc)
				{
				_log.Error ($"Unable to parse Tesla FleetAPI token refresh response: {exc.Message}");
				return false;
				}
			}
		}

	/// <summary>Retrieves the list of Tesla energy products (batteries and solar) for the account.</summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The product list from the <c>response</c> envelope, or <see langword="null"/> when unavailable.</returns>
	public async Task<List<EnergyProduct>?> GetProductsAsync (CancellationToken cancellationToken = default)
		{
		JToken? response = await SendApiAsync (HttpMethod.Get, "api/1/products", null, null, cancellationToken).ConfigureAwait (false);
		return ToTypedResponse<List<EnergyProduct>> (response);
		}

	/// <summary>Retrieves the site configuration (<c>site_info</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<SiteConfigResponse?> GetSiteConfigAsync (string siteId, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<SiteConfigResponse> (siteId, "site_info", null, cancellationToken);

	/// <summary>Retrieves the live site power data (<c>live_status</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="counter">Rolling request counter mirrored from the upstream SITE_DATA API.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<SitePowerResponse?> GetSitePowerAsync (string siteId, int counter, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<SitePowerResponse> (
			siteId,
			"live_status",
			new Dictionary<string, string> { ["counter"] = counter.ToString (CultureInfo.InvariantCulture) },
			cancellationToken);

	/// <summary>Retrieves the battery summary (<c>site_status</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<SiteSummaryResponse?> GetSiteSummaryAsync (string siteId, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<SiteSummaryResponse> (siteId, "site_status", null, cancellationToken);

	/// <summary>Retrieves the estimated backup time remaining for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The typed <c>response</c> body, or <see langword="null"/> when unavailable.</returns>
	public Task<BackupTimeRemainingResponse?> GetBackupTimeRemainingAsync (string siteId, CancellationToken cancellationToken = default) =>
		GetSiteEndpointAsync<BackupTimeRemainingResponse> (siteId, "backup_time_remaining", null, cancellationToken);

	/// <summary>Retrieves energy history (<c>history</c>) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="kind">The history kind (for example <c>power</c>, <c>energy</c>, <c>backup</c>, or <c>self_consumption</c>).</param>
	/// <param name="period">The aggregation period (for example <c>day</c>, <c>week</c>, <c>month</c>, <c>year</c>, or <c>lifetime</c>).</param>
	/// <param name="timeZone">IANA time zone name (for example <c>America/Los_Angeles</c>).</param>
	/// <param name="startDate">Inclusive RFC 3339 start timestamp.</param>
	/// <param name="endDate">Inclusive RFC 3339 end timestamp.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when unavailable.</returns>
	public Task<JObject?> GetHistoryAsync (
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
	public Task<JObject?> GetCalendarHistoryAsync (
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
	public async Task<JObject?> SetBackupReserveAsync (string siteId, int percent, CancellationToken cancellationToken = default)
		{
		var body = new JObject { ["backup_reserve_percent"] = percent };
		var uri = $"api/1/energy_sites/{siteId}/backup";
		return await SendApiAsync (HttpMethod.Post, uri, body, null, cancellationToken).ConfigureAwait (false) as JObject;
		}

	/// <summary>Sets the battery operation mode for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="mode">The operation mode (for example <c>self_consumption</c>, <c>backup</c>, or <c>autonomous</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when the call fails.</returns>
	public async Task<JObject?> SetOperationModeAsync (string siteId, string mode, CancellationToken cancellationToken = default)
		{
		var body = new JObject { ["default_real_mode"] = mode };
		var uri = $"api/1/energy_sites/{siteId}/operation";
		return await SendApiAsync (HttpMethod.Post, uri, body, null, cancellationToken).ConfigureAwait (false) as JObject;
		}

	/// <summary>Updates the grid import/export configuration (grid charging and export rules) for the specified site.</summary>
	/// <param name="siteId">The Tesla energy site identifier.</param>
	/// <param name="settings">The grid import/export settings to apply (for example <c>disallow_charge_from_grid_with_solar_installed</c> or <c>customer_preferred_export_rule</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The full response envelope, or <see langword="null"/> when the call fails.</returns>
	public async Task<JObject?> SetGridImportExportAsync (string siteId, JObject settings, CancellationToken cancellationToken = default)
		{
		var uri = $"api/1/energy_sites/{siteId}/grid_import_export";
		return await SendApiAsync (HttpMethod.Post, uri, settings, null, cancellationToken).ConfigureAwait (false) as JObject;
		}

	/// <summary>Retrieves the account summary (<c>response</c> body) from <c>/api/1/users/me</c>.</summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The account summary object, or <see langword="null"/> when unavailable.</returns>
	public async Task<JObject?> GetProfileAsync (CancellationToken cancellationToken = default)
		{
		JToken? response = await SendApiAsync (HttpMethod.Get, "api/1/users/me", null, null, cancellationToken).ConfigureAwait (false);
		return (response as JObject)?["response"] as JObject;
		}

	/// <summary>Retrieves the account region and Fleet API base URL (<c>response</c> body) from <c>/api/1/users/region</c>.</summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The region info object, or <see langword="null"/> when unavailable.</returns>
	public async Task<JObject?> GetRegionAsync (CancellationToken cancellationToken = default)
		{
		JToken? response = await SendApiAsync (HttpMethod.Get, "api/1/users/region", null, null, cancellationToken).ConfigureAwait (false);
		return (response as JObject)?["response"] as JObject;
		}

	private async Task<JObject?> GetSiteEndpointAsync (string siteId, string segment, IReadOnlyDictionary<string, string>? query, CancellationToken cancellationToken)
		{
		var uri = $"api/1/energy_sites/{siteId}/{segment}";
		return await SendApiAsync (HttpMethod.Get, uri, null, query, cancellationToken).ConfigureAwait (false) as JObject;
		}

	private async Task<T?> GetSiteEndpointAsync<T> (string siteId, string segment, IReadOnlyDictionary<string, string>? query, CancellationToken cancellationToken)
		where T : class
		{
		var uri = $"api/1/energy_sites/{siteId}/{segment}";
		JToken? response = await SendApiAsync (HttpMethod.Get, uri, null, query, cancellationToken).ConfigureAwait (false);
		return ToTypedResponse<T> (response);
		}

	// Unwraps the Fleet-API-style "response" envelope and maps it onto T, logging and swallowing a
	// malformed/unexpected shape rather than throwing (matching the tolerant style of the rest of this class).
	private static T? ToTypedResponse<T> (JToken? root)
		where T : class
		{
		JToken? body = (root as JObject)?["response"];
		if (body is null || body.Type == JTokenType.Null)
			return null;

		try
			{
			return body.ToObject<T> ();
			}
		catch (JsonException exc)
			{
			_log.Error ($"Unable to map Tesla FleetAPI response to {typeof (T).Name}: {exc.Message}");
			return null;
			}
		}

	private async Task<JToken?> SendApiAsync (
		HttpMethod method,
		string uri,
		JObject? jsonBody,
		IReadOnlyDictionary<string, string>? query,
		CancellationToken cancellationToken,
		bool allowRetry = true)
		{
		var url = $"{_baseUrl}/{uri}";
		if (query is { Count: > 0 })
			url += "?" + string.Join ("&", query.Select (static kv => $"{Uri.EscapeDataString (kv.Key)}={Uri.EscapeDataString (kv.Value)}"));

		HttpResponseMessage response;
		try
			{
			using var request = new HttpRequestMessage (method, url);
			if (_accessToken is not null)
				request.Headers.TryAddWithoutValidation ("Authorization", $"Bearer {_accessToken}");

			if (jsonBody is not null)
				request.Content = new StringContent (jsonBody.ToString (Formatting.None), Encoding.UTF8, "application/json");

			response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
			}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			_log.Error ($"Timeout waiting for Tesla FleetAPI {uri}");
			return null;
			}
		catch (HttpRequestException exc)
			{
			_log.Error ($"Unable to connect to Tesla FleetAPI {uri} - {exc.Message}");
			return null;
			}

		using (response)
			{
			if ((int) response.StatusCode is 401 or 403 && allowRetry)
				{
				_log.Debug ("Tesla FleetAPI session expired - attempting token refresh");
				if (await RefreshAccessTokenAsync (cancellationToken).ConfigureAwait (false))
					return await SendApiAsync (method, uri, jsonBody, query, cancellationToken, allowRetry: false).ConfigureAwait (false);

				_log.Error ($"Tesla FleetAPI {uri} unauthorized and token refresh failed - supply a valid refresh token");
				return null;
				}

			var payload = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
			if (!response.IsSuccessStatusCode)
				{
				if ((int) response.StatusCode == 410)
					{
					_log.Error ($"Tesla FleetAPI {uri} returned HTTP 410 (Gone) - endpoint permanently removed");
					throw new PowerwallFleetApiEndpointRemovedException (ExtractServerError (payload)
						?? $"The Tesla FleetAPI endpoint '{uri}' has been permanently removed (HTTP 410 Gone).");
					}

				_log.Error ($"Tesla FleetAPI {uri} returned HTTP {(int) response.StatusCode}");
				return null;
				}

			if (string.IsNullOrWhiteSpace (payload))
				return null;

			try
				{
				return JToken.Parse (payload);
				}
			catch (JsonException exc)
				{
				_log.Error ($"Unable to parse Tesla FleetAPI {uri} response: {exc.Message}");
				return null;
				}
			}
		}

	// Pulls the human-readable "error" text out of a Tesla API failure body, when present.
	private static string? ExtractServerError (string? payload)
		{
		if (string.IsNullOrWhiteSpace (payload))
			return null;

		try
			{
			return JToken.Parse (payload!) is JObject obj
				? obj.Value<string> ("error")
				: null;
			}
		catch (JsonException)
			{
			return null;
			}
		}

	/// <summary>Releases the underlying <see cref="HttpClient"/>.</summary>
	public void Dispose () => _httpClient.Dispose ();
	}
