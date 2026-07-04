// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;

using TeslaPowerwallLibrary.Login;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// The outcome of attempting to acquire Tesla cloud tokens through the interactive browser login.
/// </summary>
internal enum SetupLaunchStatus
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
internal sealed record CloudTokens (string RefreshToken, string AccessToken, string Email);

/// <summary>The result of a setup launch attempt.</summary>
/// <param name="Status">The outcome category.</param>
/// <param name="Tokens">The captured tokens when <see cref="Status"/> is <see cref="SetupLaunchStatus.Success"/>.</param>
/// <param name="Message">An explanatory message for non-success outcomes.</param>
internal readonly record struct SetupLaunchResult (SetupLaunchStatus Status, CloudTokens? Tokens, string? Message);

/// <summary>
/// Performs the Tesla cloud browser login in-process via <see cref="TeslaCloudLogin"/>. This lets the
/// console acquire cloud tokens through Tesla's real browser login (which handles captcha and multi-factor
/// authentication) without the user copying and pasting tokens by hand.
/// </summary>
internal static class SetupLauncher
	{
	/// <summary>
	/// Opens the Tesla browser login and returns the captured Tesla cloud tokens.
	/// </summary>
	/// <param name="region">The Tesla region to authenticate against (<c>us</c> or <c>cn</c>).</param>
	/// <param name="timeout">Maximum time to wait for the user to complete the login.</param>
	/// <returns>The launch result, including tokens on success.</returns>
	public static async Task<SetupLaunchResult> AcquireTokensAsync (string region, TimeSpan timeout)
		{
		var result = await TeslaCloudLogin.SignInAsync (region, timeout).ConfigureAwait (false);

		return result.Status switch
			{
			TeslaCloudLoginStatus.Success => new SetupLaunchResult (
				SetupLaunchStatus.Success,
				new CloudTokens (result.Tokens!.RefreshToken, result.Tokens.AccessToken, result.Tokens.Email),
				null),
			TeslaCloudLoginStatus.Cancelled => new SetupLaunchResult (SetupLaunchStatus.Cancelled, null, result.Message),
			_ => new SetupLaunchResult (SetupLaunchStatus.Failed, null, result.Message)
			};
		}
	}
