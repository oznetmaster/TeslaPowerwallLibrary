// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using TeslaPowerwallLibrary.Login;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// The outcome of attempting to acquire Tesla cloud tokens through the interactive browser login.
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
	/// <param name="cancellationToken">A token used to abandon the login early.</param>
	/// <returns>The login result, including tokens on success.</returns>
	public static async Task<TeslaLoginResult> AcquireTokensAsync (string region, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		var result = await TeslaCloudLogin.SignInAsync (region, timeout, cancellationToken).ConfigureAwait (true);

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
	}
