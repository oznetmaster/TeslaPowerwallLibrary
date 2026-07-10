// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary;

/// <summary>
/// Carries the Tesla™ FleetAPI tokens that are current after an OAuth token refresh. Raised by
/// <see cref="Powerwall.FleetApiTokensRefreshed"/> after the library has already persisted the current
/// tokens to its own cache (unless <see cref="PowerwallOptions.NoFleetApiTokenPersistence"/> is
/// <see langword="true"/>, in which case callers relying on <see cref="PowerwallOptions.FleetApiRefreshToken"/>
/// across process restarts must persist <see cref="RefreshToken"/> themselves). When the connection was
/// bootstrapped from a refresh token alone (no access token supplied), this is only raised when the refresh
/// token itself changed, and <see cref="AccessToken"/> is <see langword="null"/>; when an access token was
/// supplied, this is raised on every refresh with both tokens populated.
/// </summary>
public sealed class FleetApiTokensRefreshedEventArgs : EventArgs
	{
	/// <summary>Initializes a new instance of the <see cref="FleetApiTokensRefreshedEventArgs"/> class.</summary>
	/// <param name="accessToken">The current Tesla FleetAPI access token.</param>
	/// <param name="refreshToken">The current Tesla FleetAPI refresh token, which may have been rotated.</param>
	public FleetApiTokensRefreshedEventArgs (string? accessToken, string? refreshToken)
		{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		}

	/// <summary>
	/// Gets the current Tesla FleetAPI access token, or <see langword="null"/> when the connection was
	/// bootstrapped from a refresh token alone (no access token was supplied).
	/// </summary>
	public string? AccessToken { get; }

	/// <summary>Gets the current Tesla FleetAPI refresh token, which may have been rotated by Tesla.</summary>
	public string? RefreshToken { get; }
	}
