// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using TeslaPowerwallLibrary.Login;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// The outcome of attempting to acquire Tesla™ cloud tokens through the interactive browser login.
/// </summary>
public enum TeslaLoginStatus
	{
	/// <summary>Tokens were acquired successfully.</summary>
	Success,

	/// <summary>The user closed the login window before completing authentication.</summary>
	Cancelled,

	/// <summary>The login process started but failed or timed out.</summary>
	Failed
	}

/// <summary>The Tesla cloud tokens captured from a successful browser login.</summary>
/// <param name="RefreshToken">The long-lived refresh token.</param>
/// <param name="AccessToken">The short-lived access token.</param>
/// <param name="Email">The Tesla account email parsed from the id_token, when available.</param>
public sealed record TeslaCloudTokens (string RefreshToken, string AccessToken, string Email);

/// <summary>The result of a Tesla login attempt.</summary>
/// <param name="Status">The outcome category.</param>
/// <param name="Tokens">The captured tokens when <see cref="Status"/> is <see cref="TeslaLoginStatus.Success"/>.</param>
/// <param name="Message">An explanatory message for non-success outcomes.</param>
public readonly record struct TeslaLoginResult (TeslaLoginStatus Status, TeslaCloudTokens? Tokens, string? Message);

/// <summary>The FleetAPI access and refresh tokens captured from a successful FleetAPI setup/login.</summary>
/// <param name="AccessToken">The short-lived FleetAPI access token.</param>
/// <param name="RefreshToken">The long-lived FleetAPI refresh token.</param>
public sealed record TeslaFleetApiLoginTokens (string AccessToken, string RefreshToken);

/// <summary>
/// Performs the Tesla cloud browser login in-process via <see cref="TeslaCloudLogin"/> and adapts the
/// result to the app's own login contract. Tesla's real browser login handles captcha and multi-factor
/// authentication, so the user never has to copy and paste tokens by hand, mirroring the behavior of the
/// test console's login flow.
/// </summary>
public static class TeslaLoginService
	{
	/// <summary>
	/// Opens the Tesla browser login and returns the captured Tesla cloud tokens.
	/// </summary>
	/// <param name="region">The Tesla region to authenticate against (<c>us</c> or <c>cn</c>).</param>
	/// <param name="timeout">Maximum time to wait for the user to complete the login.</param>
	/// <param name="email">
	/// An optional email address used only to prefill the Tesla sign-in page. The user can still complete
	/// login with a different account; the returned <see cref="TeslaCloudTokens.Email"/> reflects whichever
	/// account actually signed in.
	/// </param>
	/// <param name="cancellationToken">A token used to abandon the login early.</param>
	/// <returns>The login result, including tokens on success.</returns>
	public static async Task<TeslaLoginResult> AcquireTokensAsync (
		string region, TimeSpan timeout, string? email = null, CancellationToken cancellationToken = default)
		{
		var result = await TeslaCloudLogin.SignInAsync (region, timeout, email, cancellationToken).ConfigureAwait (true);

		return result.Status switch
			{
			TeslaCloudLoginStatus.Success => new TeslaLoginResult (
				TeslaLoginStatus.Success,
				new TeslaCloudTokens (result.Tokens!.RefreshToken, result.Tokens.AccessToken, result.Tokens.Email),
				null),
			TeslaCloudLoginStatus.Cancelled => new TeslaLoginResult (TeslaLoginStatus.Cancelled, null, result.Message),
			_ => new TeslaLoginResult (TeslaLoginStatus.Failed, null, result.Message)
			};
		}

	/// <summary>
	/// Verifies that the public PEM key required by Tesla FleetAPI is reachable at
	/// <c>https://{domain}/.well-known/appspecific/com.tesla.3p.public-key.pem</c>. A cheap sanity check to
	/// perform before requesting a partner token or registering the partner account.
	/// </summary>
	/// <param name="domain">The registered application domain (no scheme, for example <c>example.com</c>).</param>
	/// <param name="cancellationToken">A token used to abandon the check early.</param>
	public static Task<bool> VerifyFleetApiPemKeyAsync (string domain, CancellationToken cancellationToken = default) =>
		TeslaFleetApiLogin.VerifyPemKeyAsync (domain, cancellationToken);

	/// <summary>
	/// Generates a Tesla FleetAPI partner authentication token via the OAuth <c>client_credentials</c> grant.
	/// Required once before the first partner account registration.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="clientSecret">The registered FleetAPI application Client Secret.</param>
	/// <param name="audience">The regional FleetAPI base URL to authorize against.</param>
	/// <param name="cancellationToken">A token used to abandon the request early.</param>
	/// <returns>The partner token, or <see langword="null"/> on failure (see out message).</returns>
	public static async Task<(string? PartnerToken, string? Message)> GetFleetApiPartnerTokenAsync (
		string clientId, string clientSecret, string audience, CancellationToken cancellationToken = default)
		{
		var result = await TeslaFleetApiLogin.GetPartnerTokenAsync (clientId, clientSecret, audience, cancellationToken).ConfigureAwait (true);
		return (result.PartnerToken, result.Message);
		}

	/// <summary>
	/// Registers the Tesla FleetAPI partner account for the supplied domain against the given region. Tesla
	/// treats this as idempotent - registering an already-registered domain still returns success.
	/// </summary>
	/// <param name="partnerToken">The partner token obtained from <see cref="GetFleetApiPartnerTokenAsync"/>.</param>
	/// <param name="audience">The regional FleetAPI base URL to register against.</param>
	/// <param name="domain">The registered application domain (no scheme, for example <c>example.com</c>).</param>
	/// <param name="cancellationToken">A token used to abandon the request early.</param>
	/// <returns><see langword="true"/> on success, along with the raw response JSON or an error message.</returns>
	public static async Task<(bool Success, string? ResponseOrMessage)> RegisterFleetApiPartnerAccountAsync (
		string partnerToken, string audience, string domain, CancellationToken cancellationToken = default)
		{
		var result = await TeslaFleetApiLogin.RegisterPartnerAccountAsync (partnerToken, audience, domain, cancellationToken).ConfigureAwait (true);
		return (result.Status == TeslaFleetApiLoginStatus.Success, result.Status == TeslaFleetApiLoginStatus.Success ? result.ResponseJson : result.Message);
		}

	/// <summary>
	/// Builds the Tesla FleetAPI authorize URL for the user to visit in a browser. The caller is responsible
	/// for opening this URL and capturing the authorization code from the resulting redirect to its own
	/// registered redirect URI.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="redirectUri">The registered redirect URI to receive the authorization code.</param>
	/// <param name="state">An optional CSRF state value; when omitted, a random value is generated.</param>
	/// <returns>The authorize URL and the state value used, for later validation.</returns>
	public static (string AuthorizeUrl, string State) BuildFleetApiAuthorizeUrl (string clientId, string redirectUri, string? state = null) =>
		TeslaFleetApiLogin.BuildAuthorizeUrl (clientId, redirectUri, state);

	/// <summary>
	/// Exchanges a FleetAPI authorization code (captured from the redirect after the user visits the
	/// authorize URL) for FleetAPI access and refresh tokens.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="clientSecret">The registered FleetAPI application Client Secret.</param>
	/// <param name="code">The authorization code captured from the redirect.</param>
	/// <param name="redirectUri">The redirect URI used to build the authorize URL (must match exactly).</param>
	/// <param name="audience">The regional FleetAPI base URL the tokens will be used against.</param>
	/// <param name="cancellationToken">A token used to abandon the request early.</param>
	/// <returns>The FleetAPI tokens on success, or <see langword="null"/> along with an error message.</returns>
	public static async Task<(TeslaFleetApiLoginTokens? Tokens, string? Message)> ExchangeFleetApiCodeAsync (
		string clientId, string clientSecret, string code, string redirectUri, string audience,
		CancellationToken cancellationToken = default)
		{
		var result = await TeslaFleetApiLogin.ExchangeCodeAsync (clientId, clientSecret, code, redirectUri, audience, cancellationToken).ConfigureAwait (true);
		if (result.Status != TeslaFleetApiLoginStatus.Success)
			return (null, result.Message);

		return (new TeslaFleetApiLoginTokens (result.Tokens!.AccessToken, result.Tokens.RefreshToken), null);
		}
	}
