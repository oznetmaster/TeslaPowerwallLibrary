// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.Login;

/// <summary>
/// Faithful C# adaptation of the FleetAPI portion of the upstream <c>pypowerwall</c> <c>fleetapi.setup()</c>
/// method: generating a partner authentication token, registering the partner account, verifying the hosted
/// PEM public key, building the authorize URL, and exchanging an authorization code for FleetAPI tokens.
/// Unlike the interactive Python CLI wizard, every step here is a discrete, stateless, non-interactive
/// operation - the caller supplies its own registered Client ID/Secret, domain, redirect URI, and PEM
/// hosting, and orchestrates the (browser) interaction itself. Nothing is persisted or cached here; each
/// method simply performs one Tesla API call and returns its result.
/// </summary>
internal static class FleetApiAuth
	{
	private const string SSO_BASE_URL = "https://auth.tesla.com";
	private const string TOKEN_URL_PATH = "/oauth2/v3/token";
	private const string AUTHORIZE_URL_PATH = "/oauth2/v3/authorize";

	/// <summary>The OAuth scopes requested for FleetAPI, matching upstream <c>SCOPE</c>.</summary>
	public const string Scope = "openid offline_access energy_device_data energy_cmds";

	private static readonly HttpClient _httpClient = new () { Timeout = TimeSpan.FromSeconds (30) };

	/// <summary>
	/// Verifies that the public PEM key required by Tesla is reachable at
	/// <c>https://{domain}/.well-known/appspecific/com.tesla.3p.public-key.pem</c>. Mirrors the upstream
	/// setup wizard's PEM check, performed as a cheap sanity check before attempting registration or login.
	/// </summary>
	/// <param name="domain">The registered application domain (no scheme, for example <c>example.com</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the PEM key is reachable and returns HTTP 200; otherwise <see langword="false"/>.</returns>
	public static async Task<bool> VerifyPemKeyAsync (string domain, CancellationToken cancellationToken = default)
		{
		var url = $"https://{domain}/.well-known/appspecific/com.tesla.3p.public-key.pem";
		try
			{
			using var response = await _httpClient.GetAsync (url, cancellationToken).ConfigureAwait (false);
			return response.IsSuccessStatusCode;
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
			return false;
			}
		}

	/// <summary>
	/// Generates a partner authentication token via the OAuth <c>client_credentials</c> grant.
	/// Mirrors upstream <c>setup()</c> Step 3A.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="clientSecret">The registered FleetAPI application Client Secret.</param>
	/// <param name="audience">The regional FleetAPI base URL to authorize against (see <c>FleetApiRegions</c> in the main library).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The partner token.</returns>
	/// <exception cref="FleetApiAuthException">Thrown when the request fails or returns no access token.</exception>
	public static async Task<string> GetPartnerTokenAsync (
		string clientId, string clientSecret, string audience, CancellationToken cancellationToken = default)
		{
		var form = new FormUrlEncodedContent (new[]
			{
			new KeyValuePair<string, string> ("grant_type", "client_credentials"),
			new KeyValuePair<string, string> ("client_id", clientId),
			new KeyValuePair<string, string> ("client_secret", clientSecret),
			new KeyValuePair<string, string> ("scope", Scope),
			new KeyValuePair<string, string> ("audience", audience)
			});

		var body = await PostFormAsync (SSO_BASE_URL + TOKEN_URL_PATH, form, cancellationToken).ConfigureAwait (false);
		var token = JObject.Parse (body).Value<string> ("access_token");
		if (string.IsNullOrWhiteSpace (token))
			throw new FleetApiAuthException ($"No access_token in Tesla partner token response: {body}");

		return token!;
		}

	/// <summary>
	/// Registers the partner account for the supplied domain against the given FleetAPI region.
	/// Mirrors upstream <c>setup()</c> Step 3B. Tesla treats this as idempotent - registering an
	/// already-registered domain still returns success.
	/// </summary>
	/// <param name="partnerToken">The partner token obtained from <see cref="GetPartnerTokenAsync"/>.</param>
	/// <param name="audience">The regional FleetAPI base URL to register against.</param>
	/// <param name="domain">The registered application domain (no scheme, for example <c>example.com</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The raw JSON response body from Tesla.</returns>
	/// <exception cref="FleetApiAuthException">Thrown when the request fails.</exception>
	public static async Task<string> RegisterPartnerAccountAsync (
		string partnerToken, string audience, string domain, CancellationToken cancellationToken = default)
		{
		var url = $"{audience.TrimEnd ('/')}/api/1/partner_accounts";
		var body = new JObject { ["domain"] = domain };

		using var request = new HttpRequestMessage (HttpMethod.Post, url)
			{
			Content = new StringContent (body.ToString (Formatting.None), Encoding.UTF8, "application/json")
			};
		request.Headers.TryAddWithoutValidation ("Authorization", $"Bearer {partnerToken}");

		HttpResponseMessage response;
		string responseBody;
		try
			{
			response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
			responseBody = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
			throw new FleetApiAuthException ($"Partner account registration request failed: {exc.Message}", exc);
			}

		using (response)
			{
			if (!response.IsSuccessStatusCode)
				throw new FleetApiAuthException ($"Partner account registration failed (HTTP {(int) response.StatusCode}): {responseBody}");

			return responseBody;
			}
		}

	/// <summary>
	/// Builds the Tesla FleetAPI authorize URL for the user to visit in a browser. Mirrors upstream
	/// <c>setup()</c> Step 3C. Unlike the cloud PKCE login, FleetAPI's authorize flow redirects back to the
	/// caller's own registered redirect URI rather than a native <c>tesla://</c> scheme, so the caller is
	/// responsible for capturing the returned <c>code</c> (for example from its own web server or by asking
	/// the user to paste the redirected URL).
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="redirectUri">The registered redirect URI to receive the authorization code.</param>
	/// <param name="state">
	/// An optional CSRF state value to include and later validate against the redirect. When omitted, a
	/// random value is generated and returned as part of the result.
	/// </param>
	/// <returns>The authorize URL and the state value used, for later validation.</returns>
	public static (string AuthorizeUrl, string State) BuildAuthorizeUrl (string clientId, string redirectUri, string? state = null)
		{
		var effectiveState = string.IsNullOrWhiteSpace (state) ? Guid.NewGuid ().ToString ("N") : state!;
		var scope = Uri.EscapeDataString (Scope);
		var url =
			$"{SSO_BASE_URL}{AUTHORIZE_URL_PATH}?&client_id={Uri.EscapeDataString (clientId)}&locale=en-US&prompt=login" +
			$"&redirect_uri={Uri.EscapeDataString (redirectUri)}&response_type=code&scope={scope}&state={Uri.EscapeDataString (effectiveState)}";

		return (url, effectiveState);
		}

	/// <summary>
	/// Exchanges an authorization code for FleetAPI access and refresh tokens. Mirrors upstream
	/// <c>setup()</c> Step 3D.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="clientSecret">The registered FleetAPI application Client Secret.</param>
	/// <param name="code">The authorization code captured from the redirect.</param>
	/// <param name="redirectUri">The redirect URI used to build the authorize URL (must match exactly).</param>
	/// <param name="audience">The regional FleetAPI base URL the tokens will be used against.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The FleetAPI access and refresh tokens.</returns>
	/// <exception cref="FleetApiAuthException">Thrown when the exchange fails or returns no tokens.</exception>
	public static async Task<FleetApiTokens> ExchangeCodeAsync (
		string clientId, string clientSecret, string code, string redirectUri, string audience,
		CancellationToken cancellationToken = default)
		{
		var form = new FormUrlEncodedContent (new[]
			{
			new KeyValuePair<string, string> ("grant_type", "authorization_code"),
			new KeyValuePair<string, string> ("client_id", clientId),
			new KeyValuePair<string, string> ("client_secret", clientSecret),
			new KeyValuePair<string, string> ("code", code),
			new KeyValuePair<string, string> ("audience", audience),
			new KeyValuePair<string, string> ("redirect_uri", redirectUri),
			new KeyValuePair<string, string> ("scope", Scope)
			});

		var body = await PostFormAsync (SSO_BASE_URL + TOKEN_URL_PATH, form, cancellationToken).ConfigureAwait (false);
		JObject root;
		try
			{
			root = JObject.Parse (body);
			}
		catch (JsonException exc)
			{
			throw new FleetApiAuthException ($"Unable to parse Tesla FleetAPI token response: {exc.Message}", exc);
			}

		var accessToken = root.Value<string> ("access_token");
		var refreshToken = root.Value<string> ("refresh_token");
		if (string.IsNullOrWhiteSpace (accessToken) || string.IsNullOrWhiteSpace (refreshToken))
			throw new FleetApiAuthException ($"Tesla FleetAPI token exchange did not return both tokens: {body}");

		return new FleetApiTokens (accessToken!, refreshToken!);
		}

	private static async Task<string> PostFormAsync (string url, FormUrlEncodedContent form, CancellationToken cancellationToken)
		{
		HttpResponseMessage response;
		string body;
		try
			{
			response = await _httpClient.PostAsync (url, form, cancellationToken).ConfigureAwait (false);
			body = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
			throw new FleetApiAuthException ($"Tesla FleetAPI auth request failed: {exc.Message}", exc);
			}

		using (response)
			{
			if (!response.IsSuccessStatusCode)
				throw new FleetApiAuthException ($"Tesla FleetAPI auth request failed (HTTP {(int) response.StatusCode}): {body}");

			return body;
			}
		}
	}

/// <summary>
/// The FleetAPI access and refresh tokens returned by a successful authorization-code exchange.
/// </summary>
/// <param name="AccessToken">The short-lived FleetAPI access token.</param>
/// <param name="RefreshToken">The long-lived FleetAPI refresh token.</param>
internal sealed record FleetApiTokens (string AccessToken, string RefreshToken);

/// <summary>
/// The exception thrown when a Tesla FleetAPI setup/login request fails.
/// </summary>
internal sealed class FleetApiAuthException : Exception
	{
	/// <summary>Initializes a new instance of the <see cref="FleetApiAuthException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public FleetApiAuthException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="FleetApiAuthException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public FleetApiAuthException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}
