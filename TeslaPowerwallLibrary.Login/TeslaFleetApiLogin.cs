// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TeslaPowerwallLibrary.Login;

/// <summary>
/// The outcome of an individual Tesla™ FleetAPI setup/login step performed via <see cref="TeslaFleetApiLogin"/>.
/// </summary>
public enum TeslaFleetApiLoginStatus
	{
	/// <summary>The step completed successfully.</summary>
	Success,

	/// <summary>The step failed (see the result's <c>Message</c> for details).</summary>
	Failed
	}

/// <summary>The FleetAPI access and refresh tokens captured from a successful authorization-code exchange.</summary>
/// <param name="AccessToken">The short-lived FleetAPI access token.</param>
/// <param name="RefreshToken">The long-lived FleetAPI refresh token.</param>
public sealed record TeslaFleetApiLoginTokens (string AccessToken, string RefreshToken);

/// <summary>The result of requesting a FleetAPI partner token.</summary>
/// <param name="Status">The outcome category.</param>
/// <param name="PartnerToken">The partner token, when <see cref="Status"/> is <see cref="TeslaFleetApiLoginStatus.Success"/>.</param>
/// <param name="Message">An explanatory message for failures.</param>
public sealed record TeslaFleetApiPartnerTokenResult (TeslaFleetApiLoginStatus Status, string? PartnerToken, string? Message);

/// <summary>The result of registering a FleetAPI partner account.</summary>
/// <param name="Status">The outcome category.</param>
/// <param name="ResponseJson">The raw JSON response from Tesla, when <see cref="Status"/> is <see cref="TeslaFleetApiLoginStatus.Success"/>.</param>
/// <param name="Message">An explanatory message for failures.</param>
public sealed record TeslaFleetApiPartnerAccountResult (TeslaFleetApiLoginStatus Status, string? ResponseJson, string? Message);

/// <summary>The result of an authorization-code exchange for FleetAPI tokens.</summary>
/// <param name="Status">The outcome category.</param>
/// <param name="Tokens">The captured tokens, when <see cref="Status"/> is <see cref="TeslaFleetApiLoginStatus.Success"/>.</param>
/// <param name="Message">An explanatory message for failures.</param>
public sealed record TeslaFleetApiLoginResult (TeslaFleetApiLoginStatus Status, TeslaFleetApiLoginTokens? Tokens, string? Message);

/// <summary>
/// Performs the Tesla™ FleetAPI setup/login steps adapted from the upstream <c>pypowerwall</c>
/// <c>fleetapi.setup()</c> wizard: generating a partner token, registering the partner account, verifying
/// the hosted PEM key, building the authorize URL, and exchanging an authorization code for tokens. Every
/// step is exposed independently and is entirely non-interactive - the caller supplies its own registered
/// Client ID/Secret, domain, and redirect URI (obtained from https://developer.tesla.com/), and is
/// responsible for opening the authorize URL and capturing the resulting authorization code (for example
/// via its own hosted redirect endpoint, or by asking the user to paste the redirected URL). This mirrors
/// the split between <see cref="TeslaAuth"/> (mechanics) and <see cref="TeslaCloudLogin"/> (orchestration)
/// used for the cloud login flow, except FleetAPI's browser step cannot be automated in-process because it
/// redirects to the caller's own domain rather than a native <c>tesla://</c> scheme.
/// </summary>
public static class TeslaFleetApiLogin
	{
	/// <summary>
	/// Verifies that the public PEM key required by Tesla is reachable at
	/// <c>https://{domain}/.well-known/appspecific/com.tesla.3p.public-key.pem</c>. A cheap sanity check to
	/// perform before requesting a partner token or registering the partner account.
	/// </summary>
	/// <param name="domain">The registered application domain (no scheme, for example <c>example.com</c>).</param>
	/// <param name="cancellationToken">A token used to abandon the check early.</param>
	/// <returns><see langword="true"/> when the PEM key is reachable; otherwise <see langword="false"/>.</returns>
	public static Task<bool> VerifyPemKeyAsync (string domain, CancellationToken cancellationToken = default) =>
		FleetApiAuth.VerifyPemKeyAsync (domain, cancellationToken);

	/// <summary>
	/// Generates a partner authentication token via the OAuth <c>client_credentials</c> grant. Required once
	/// before the first partner account registration; the caller may cache and reuse the returned token for
	/// subsequent registrations until it expires.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="clientSecret">The registered FleetAPI application Client Secret.</param>
	/// <param name="audience">The regional FleetAPI base URL to authorize against.</param>
	/// <param name="cancellationToken">A token used to abandon the request early.</param>
	/// <returns>The partner token result.</returns>
	public static async Task<TeslaFleetApiPartnerTokenResult> GetPartnerTokenAsync (
		string clientId, string clientSecret, string audience, CancellationToken cancellationToken = default)
		{
		try
			{
			var token = await FleetApiAuth.GetPartnerTokenAsync (clientId, clientSecret, audience, cancellationToken).ConfigureAwait (false);
			return new TeslaFleetApiPartnerTokenResult (TeslaFleetApiLoginStatus.Success, token, null);
			}
		catch (FleetApiAuthException exc)
			{
			return new TeslaFleetApiPartnerTokenResult (TeslaFleetApiLoginStatus.Failed, null, exc.Message);
			}
		}

	/// <summary>
	/// Registers the partner account for the supplied domain against the given FleetAPI region. Tesla treats
	/// this as idempotent - registering an already-registered domain still returns success, so callers may
	/// invoke this on every setup run without tracking whether registration already happened.
	/// </summary>
	/// <param name="partnerToken">The partner token obtained from <see cref="GetPartnerTokenAsync"/>.</param>
	/// <param name="audience">The regional FleetAPI base URL to register against.</param>
	/// <param name="domain">The registered application domain (no scheme, for example <c>example.com</c>).</param>
	/// <param name="cancellationToken">A token used to abandon the request early.</param>
	/// <returns>The partner account registration result.</returns>
	public static async Task<TeslaFleetApiPartnerAccountResult> RegisterPartnerAccountAsync (
		string partnerToken, string audience, string domain, CancellationToken cancellationToken = default)
		{
		try
			{
			var json = await FleetApiAuth.RegisterPartnerAccountAsync (partnerToken, audience, domain, cancellationToken).ConfigureAwait (false);
			return new TeslaFleetApiPartnerAccountResult (TeslaFleetApiLoginStatus.Success, json, null);
			}
		catch (FleetApiAuthException exc)
			{
			return new TeslaFleetApiPartnerAccountResult (TeslaFleetApiLoginStatus.Failed, null, exc.Message);
			}
		}

	/// <summary>
	/// Builds the Tesla FleetAPI authorize URL for the user to visit in a browser. The caller is responsible
	/// for opening this URL and capturing the authorization <c>code</c> from the resulting redirect to its
	/// own registered redirect URI.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="redirectUri">The registered redirect URI to receive the authorization code.</param>
	/// <param name="state">
	/// An optional CSRF state value to include and later validate against the redirect. When omitted, a
	/// random value is generated and returned as part of the result.
	/// </param>
	/// <returns>The authorize URL and the state value used, for later validation.</returns>
	public static (string AuthorizeUrl, string State) BuildAuthorizeUrl (string clientId, string redirectUri, string? state = null) =>
		FleetApiAuth.BuildAuthorizeUrl (clientId, redirectUri, state);

	/// <summary>
	/// Exchanges an authorization code (captured from the redirect after the user visits the authorize URL)
	/// for FleetAPI access and refresh tokens.
	/// </summary>
	/// <param name="clientId">The registered FleetAPI application Client ID.</param>
	/// <param name="clientSecret">The registered FleetAPI application Client Secret.</param>
	/// <param name="code">The authorization code captured from the redirect.</param>
	/// <param name="redirectUri">The redirect URI used to build the authorize URL (must match exactly).</param>
	/// <param name="audience">The regional FleetAPI base URL the tokens will be used against.</param>
	/// <param name="cancellationToken">A token used to abandon the request early.</param>
	/// <returns>The login result, including tokens on success.</returns>
	public static async Task<TeslaFleetApiLoginResult> ExchangeCodeAsync (
		string clientId, string clientSecret, string code, string redirectUri, string audience,
		CancellationToken cancellationToken = default)
		{
		try
			{
			var tokens = await FleetApiAuth.ExchangeCodeAsync (clientId, clientSecret, code, redirectUri, audience, cancellationToken).ConfigureAwait (false);
			return new TeslaFleetApiLoginResult (
				TeslaFleetApiLoginStatus.Success,
				new TeslaFleetApiLoginTokens (tokens.AccessToken, tokens.RefreshToken),
				null);
			}
		catch (FleetApiAuthException exc)
			{
			return new TeslaFleetApiLoginResult (TeslaFleetApiLoginStatus.Failed, null, exc.Message);
			}
		}
	}
