// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary;

/// <summary>
/// Carries the Tesla™ Owners API tokens that are current after an OAuth access-token refresh. Raised by
/// <see cref="Powerwall.CloudTokensRefreshed"/> so long-running callers can persist rotated tokens (the
/// refresh token may change on each refresh) and reuse them on a later run instead of re-authenticating.
/// </summary>
public sealed class CloudTokensRefreshedEventArgs : EventArgs
	{
	/// <summary>Initializes a new instance of the <see cref="CloudTokensRefreshedEventArgs"/> class.</summary>
	/// <param name="accessToken">The current Tesla Owners API access token.</param>
	/// <param name="refreshToken">The current Tesla Owners API refresh token, which may have been rotated.</param>
	public CloudTokensRefreshedEventArgs (string? accessToken, string? refreshToken)
		{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		}

	/// <summary>Gets the current Tesla Owners API access token.</summary>
	public string? AccessToken { get; }

	/// <summary>Gets the current Tesla Owners API refresh token, which may have been rotated by Tesla.</summary>
	public string? RefreshToken { get; }
	}
