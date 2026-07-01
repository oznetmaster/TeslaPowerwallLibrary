// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Tesla OAuth 2.0 PKCE flow based on tesla_auth (Rust) by Adrian Kumpf — https://github.com/adriankumpf/tesla_auth.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TeslaPowerwallLibrary.Setup;

/// <summary>
/// Faithful C# adaptation of the upstream <c>pypowerwall</c> <c>tesla_auth</c> module that backs the
/// <c>python -m pypowerwall authtoken</c> command. Implements the Tesla OAuth 2.0 PKCE flow: building the
/// authorize URL, parsing the <c>tesla://auth/callback</c> redirect, exchanging the authorization code for
/// tokens, and extracting the account email from the returned id_token.
/// </summary>
internal static class TeslaAuth
	{
	// Constants — match tesla_auth (Rust) and pypowerwall exactly.
	// See: https://github.com/adriankumpf/tesla_auth/blob/master/src/auth.rs
	private const string ClientId = "ownerapi";
	private const string AuthUrlPath = "/oauth2/v3/authorize";
	private const string TokenUrlPath = "/oauth2/v3/token";

	/// <summary>The custom redirect scheme Tesla uses for the native PKCE callback.</summary>
	public const string RedirectUri = "tesla://auth/callback";

	// owner-api.teslamotors.com only accepts access tokens produced by a fresh PKCE code exchange.
	// Energy scopes are NOT needed for the Owners API.
	private const string Scopes = "openid email offline_access";

	private static readonly IReadOnlyDictionary<string, string> RegionHosts = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase)
		{
		["us"] = "https://auth.tesla.com",
		["cn"] = "https://auth.tesla.cn"
		};

	// Tesla requires HTTP/2 for the auth.tesla.com token endpoints.
	private static readonly HttpClient _httpClient = CreateHttpClient ();

	private static HttpClient CreateHttpClient ()
		{
		var client = new HttpClient
			{
			DefaultRequestVersion = HttpVersion.Version20,
			DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
			Timeout = TimeSpan.FromSeconds (30)
			};
		return client;
		}

	/// <summary>
	/// Resolves the Tesla SSO host for the supplied region, defaulting to the US host for unknown regions.
	/// </summary>
	/// <param name="region">The region code, <c>us</c> or <c>cn</c>.</param>
	/// <returns>The SSO base host, for example <c>https://auth.tesla.com</c>.</returns>
	public static string ResolveAuthHost (string region) =>
		RegionHosts.TryGetValue (region ?? "us", out var host) ? host : RegionHosts["us"];

	/// <summary>
	/// Builds the Tesla OAuth authorize URL with PKCE parameters.
	/// Mirrors upstream <c>_build_auth_url</c>: S256 challenge, <c>response_type=code</c>, the
	/// <c>tesla://auth/callback</c> redirect, and the <c>openid email offline_access</c> scopes.
	/// </summary>
	/// <param name="region">The region code, <c>us</c> or <c>cn</c>.</param>
	/// <returns>The authorize request containing the URL, PKCE code verifier, and CSRF state.</returns>
	public static TeslaAuthRequest BuildAuthUrl (string region)
		{
		var authHost = ResolveAuthHost (region);

		var codeVerifier = Base64UrlEncode (RandomNumberGenerator.GetBytes (32));
		var codeChallenge = Base64UrlEncode (SHA256.HashData (Encoding.ASCII.GetBytes (codeVerifier)));
		var state = Base64UrlEncode (RandomNumberGenerator.GetBytes (16));

		// Preserve the upstream parameter order.
		var query = new StringBuilder ();
		AppendQueryParameter (query, "client_id", ClientId);
		AppendQueryParameter (query, "code_challenge", codeChallenge);
		AppendQueryParameter (query, "code_challenge_method", "S256");
		AppendQueryParameter (query, "redirect_uri", RedirectUri);
		AppendQueryParameter (query, "response_type", "code");
		AppendQueryParameter (query, "scope", Scopes);
		AppendQueryParameter (query, "state", state);

		var authUrl = $"{authHost}{AuthUrlPath}?{query}";
		return new TeslaAuthRequest (authUrl, codeVerifier, state);
		}

	/// <summary>
	/// Parses a <c>tesla://auth/callback</c> redirect, extracting the authorization code, CSRF state, and any error.
	/// Mirrors the redirect handling in upstream <c>_patch_pywebview_win32</c>.
	/// </summary>
	/// <param name="uri">The full callback URI intercepted from the browser navigation.</param>
	/// <returns><see langword="true"/> when the URI is a Tesla callback; otherwise <see langword="false"/>.</returns>
	public static bool TryParseCallback (string uri, out TeslaCallback callback)
		{
		callback = default;
		if (string.IsNullOrEmpty (uri) || !uri.StartsWith ("tesla://", StringComparison.OrdinalIgnoreCase))
			return false;

		var queryStart = uri.IndexOf ('?');
		var query = queryStart >= 0 ? uri[(queryStart + 1)..] : string.Empty;
		var values = ParseQuery (query);

		values.TryGetValue ("code", out var code);
		values.TryGetValue ("state", out var state);
		values.TryGetValue ("error", out var error);
		callback = new TeslaCallback (code, state, error);
		return true;
		}

	/// <summary>
	/// Exchanges an authorization code for Tesla tokens.
	/// Mirrors upstream <c>_exchange_code</c>: <c>POST /oauth2/v3/token</c> with
	/// <c>grant_type=authorization_code</c> over HTTP/2, returning the refresh and access tokens.
	/// </summary>
	/// <param name="authCode">The authorization code captured from the redirect.</param>
	/// <param name="codeVerifier">The PKCE code verifier generated for this login.</param>
	/// <param name="region">The region code, <c>us</c> or <c>cn</c>.</param>
	/// <param name="cancellationToken">Token used to cancel the request.</param>
	/// <returns>The Tesla tokens, including the account email parsed from the id_token.</returns>
	/// <exception cref="TeslaAuthException">Thrown when the exchange fails or returns no refresh token.</exception>
	public static async Task<TeslaTokens> ExchangeCodeAsync (
		string authCode,
		string codeVerifier,
		string region,
		CancellationToken cancellationToken = default)
		{
		var authHost = ResolveAuthHost (region);
		var url = $"{authHost}{TokenUrlPath}";

		var payload = new Dictionary<string, string>
			{
			["grant_type"] = "authorization_code",
			["client_id"] = ClientId,
			["code"] = authCode,
			["code_verifier"] = codeVerifier,
			["redirect_uri"] = RedirectUri
			};

		HttpResponseMessage response;
		string body;
		try
			{
			using var request = new HttpRequestMessage (HttpMethod.Post, url)
				{
				Version = HttpVersion.Version20,
				VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
				Content = new StringContent (JsonSerializer.Serialize (payload), Encoding.UTF8, "application/json")
				};
			response = await _httpClient.SendAsync (request, cancellationToken).ConfigureAwait (false);
			body = await response.Content.ReadAsStringAsync (cancellationToken).ConfigureAwait (false);
			}
		catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
			throw new TeslaAuthException ($"Token exchange request failed: {exc.Message}", exc);
			}

		using (response)
			{
			if (!response.IsSuccessStatusCode)
				throw new TeslaAuthException ($"Token exchange failed (HTTP {(int) response.StatusCode}): {body}");

			return ParseTokenResponse (body);
			}
		}

	private static TeslaTokens ParseTokenResponse (string body)
		{
		JsonElement root;
		try
			{
			using var document = JsonDocument.Parse (body);
			root = document.RootElement.Clone ();
			}
		catch (JsonException exc)
			{
			throw new TeslaAuthException ($"Unable to parse Tesla token response: {exc.Message}", exc);
			}

		var refreshToken = GetString (root, "refresh_token");
		if (string.IsNullOrEmpty (refreshToken))
			throw new TeslaAuthException ($"No refresh_token in Tesla token response: {body}");

		var accessToken = GetString (root, "access_token") ?? string.Empty;
		var tokenType = GetString (root, "token_type") ?? "Bearer";
		var idToken = GetString (root, "id_token");
		var expiresIn = root.TryGetProperty ("expires_in", out var expires) && expires.TryGetInt32 (out var seconds)
			? seconds
			: 28800;
		var email = idToken is null ? string.Empty : ExtractEmailFromToken (idToken);

		return new TeslaTokens (refreshToken!, accessToken, email, tokenType, expiresIn, idToken);
		}

	/// <summary>
	/// Extracts the account email from a Tesla id_token JWT payload.
	/// Mirrors upstream <c>_extract_email_from_token</c>.
	/// </summary>
	/// <param name="idToken">The JWT id_token returned by the token endpoint.</param>
	/// <returns>The email address, or an empty string when it cannot be determined.</returns>
	public static string ExtractEmailFromToken (string idToken)
		{
		if (string.IsNullOrEmpty (idToken))
			return string.Empty;

		try
			{
			var parts = idToken.Split ('.');
			if (parts.Length < 2)
				return string.Empty;

			using var document = JsonDocument.Parse (Base64UrlDecode (parts[1]));
			var root = document.RootElement;

			var email = GetString (root, "email");
			if (!string.IsNullOrEmpty (email))
				return email!;

			if (root.TryGetProperty ("data", out var data) && data.ValueKind == JsonValueKind.Object)
				return GetString (data, "email") ?? string.Empty;
			}
		catch (Exception exc) when (exc is JsonException or FormatException)
			{
			// A malformed id_token simply means we cannot pre-fill the email.
			}

		return string.Empty;
		}

	private static string? GetString (JsonElement element, string propertyName) =>
		element.TryGetProperty (propertyName, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString ()
			: null;

	private static void AppendQueryParameter (StringBuilder builder, string name, string value)
		{
		if (builder.Length > 0)
			builder.Append ('&');
		builder.Append (Uri.EscapeDataString (name));
		builder.Append ('=');
		builder.Append (Uri.EscapeDataString (value));
		}

	private static Dictionary<string, string> ParseQuery (string query)
		{
		var values = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty (query))
			return values;

		foreach (var pair in query.Split ('&', StringSplitOptions.RemoveEmptyEntries))
			{
			var separator = pair.IndexOf ('=');
			if (separator < 0)
				{
				values[Uri.UnescapeDataString (pair)] = string.Empty;
				continue;
				}

			var key = Uri.UnescapeDataString (pair[..separator]);
			var value = Uri.UnescapeDataString (pair[(separator + 1)..]);
			values[key] = value;
			}

		return values;
		}

	private static string Base64UrlEncode (byte[] bytes) =>
		Convert.ToBase64String (bytes).TrimEnd ('=').Replace ('+', '-').Replace ('/', '_');

	private static byte[] Base64UrlDecode (string input)
		{
		var builder = new StringBuilder (input.Replace ('-', '+').Replace ('_', '/'));
		switch (builder.Length % 4)
			{
			case 2: builder.Append ("=="); break;
			case 3: builder.Append ('='); break;
			}

		return Convert.FromBase64String (builder.ToString ());
		}
	}

/// <summary>
/// Represents a prepared Tesla OAuth authorize request: the URL to load plus the PKCE code verifier
/// and CSRF state that must be retained to complete the code exchange.
/// </summary>
/// <param name="AuthUrl">The fully composed authorize URL to open in the browser.</param>
/// <param name="CodeVerifier">The PKCE code verifier paired with the challenge in <paramref name="AuthUrl"/>.</param>
/// <param name="State">The CSRF state value to validate against the redirect.</param>
internal sealed record TeslaAuthRequest (string AuthUrl, string CodeVerifier, string State);

/// <summary>
/// Represents the parsed contents of a <c>tesla://auth/callback</c> redirect.
/// </summary>
/// <param name="Code">The authorization code, when present.</param>
/// <param name="State">The CSRF state echoed back by Tesla, when present.</param>
/// <param name="Error">The error code returned by Tesla, when the login failed.</param>
internal readonly record struct TeslaCallback (string? Code, string? State, string? Error);

/// <summary>
/// Represents the tokens returned by a successful Tesla OAuth code exchange.
/// </summary>
/// <param name="RefreshToken">The long-lived refresh token (valid ~90 days).</param>
/// <param name="AccessToken">The short-lived access token (valid ~8 hours).</param>
/// <param name="Email">The Tesla account email parsed from the id_token, when available.</param>
/// <param name="TokenType">The token type, normally <c>Bearer</c>.</param>
/// <param name="ExpiresIn">The access-token lifetime, in seconds.</param>
/// <param name="IdToken">The raw OpenID id_token, when returned.</param>
internal sealed record TeslaTokens (
	string RefreshToken,
	string AccessToken,
	string Email,
	string TokenType,
	int ExpiresIn,
	string? IdToken);

/// <summary>
/// The exception thrown when the Tesla OAuth token exchange fails.
/// </summary>
internal sealed class TeslaAuthException : Exception
	{
	/// <summary>Initializes a new instance of the <see cref="TeslaAuthException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public TeslaAuthException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="TeslaAuthException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public TeslaAuthException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}
