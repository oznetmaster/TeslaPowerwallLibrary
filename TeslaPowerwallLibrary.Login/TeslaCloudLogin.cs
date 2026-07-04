// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TeslaPowerwallLibrary.Login;

/// <summary>
/// The outcome of an interactive Tesla cloud login attempt.
/// </summary>
public enum TeslaCloudLoginStatus
	{
	/// <summary>Tokens were acquired successfully.</summary>
	Success,

	/// <summary>The user closed the login window, or the caller cancelled, before completing authentication.</summary>
	Cancelled,

	/// <summary>The login attempt started but failed or timed out.</summary>
	Failed
	}

/// <summary>The Tesla cloud tokens captured from a successful interactive browser login.</summary>
/// <param name="RefreshToken">The long-lived refresh token (valid ~90 days).</param>
/// <param name="AccessToken">The short-lived access token (valid ~8 hours).</param>
/// <param name="Email">The Tesla account email parsed from the id_token, when available.</param>
public sealed record TeslaCloudLoginTokens (string RefreshToken, string AccessToken, string Email);

/// <summary>The result of an interactive Tesla cloud login attempt.</summary>
/// <param name="Status">The outcome category.</param>
/// <param name="Tokens">The captured tokens when <see cref="Status"/> is <see cref="TeslaCloudLoginStatus.Success"/>.</param>
/// <param name="Message">An explanatory message for non-success outcomes.</param>
public sealed record TeslaCloudLoginResult (TeslaCloudLoginStatus Status, TeslaCloudLoginTokens? Tokens, string? Message);

/// <summary>
/// Performs an interactive Tesla OAuth 2.0 PKCE browser login and returns the resulting cloud tokens.
/// Hosts the login page in an embedded WebView2 control on a bare Win32 window (no WPF or WinForms
/// dependency), so it can be referenced directly by any .NET application — console or desktop — without
/// requiring a UI framework or launching a separate process. The caller is responsible for persisting the
/// returned tokens; this type performs no persistence of its own.
/// </summary>
public static class TeslaCloudLogin
	{
	/// <summary>
	/// Opens the interactive Tesla sign-in window and returns the captured refresh and access tokens once
	/// the user completes authentication, cancels, or the login fails or times out. The login runs on its
	/// own dedicated thread with an independent Win32 message loop, so it does not require and will not
	/// interfere with a caller's existing UI thread or message loop (if any).
	/// </summary>
	/// <param name="region">The Tesla region to authenticate against (<c>us</c> or <c>cn</c>).</param>
	/// <param name="timeout">Maximum time to wait for the user to complete the login.</param>
	/// <param name="cancellationToken">A token used to abandon the login early.</param>
	/// <returns>The login result, including tokens on success.</returns>
	public static Task<TeslaCloudLoginResult> SignInAsync (
		string region, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		var safeRegion = string.Equals (region, "cn", StringComparison.OrdinalIgnoreCase) ? "cn" : "us";
		return NativeLoginWindow.RunAsync (safeRegion, timeout, cancellationToken);
		}
	}
