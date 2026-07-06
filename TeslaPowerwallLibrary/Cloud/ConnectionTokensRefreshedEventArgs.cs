// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Carries the Tesla™ Owners API tokens that are current after a <see cref="TeslaCloudConnection"/>
/// access-token refresh, plus whether the refresh token itself changed. Raised unconditionally on every
/// successful refresh so <see cref="PowerwallCloudClient"/> can always persist to its internal token cache;
/// <see cref="RefreshTokenChanged"/> lets it decide whether the refresh is also significant enough to surface
/// on the library's public <see cref="Powerwall.CloudTokensRefreshed"/> event.
/// </summary>
internal sealed class ConnectionTokensRefreshedEventArgs : EventArgs
	{
	/// <summary>Initializes a new instance of the <see cref="ConnectionTokensRefreshedEventArgs"/> class.</summary>
	/// <param name="accessToken">The current Tesla Owners API access token.</param>
	/// <param name="refreshToken">The current Tesla Owners API refresh token, which may have been rotated.</param>
	/// <param name="refreshTokenChanged">
	/// <see langword="true"/> when this refresh returned a refresh token value different from the one used to
	/// request it; otherwise <see langword="false"/>.
	/// </param>
	public ConnectionTokensRefreshedEventArgs (string? accessToken, string? refreshToken, bool refreshTokenChanged)
		{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		RefreshTokenChanged = refreshTokenChanged;
		}

	/// <summary>Gets the current Tesla Owners API access token.</summary>
	public string? AccessToken { get; }

	/// <summary>Gets the current Tesla Owners API refresh token, which may have been rotated by Tesla.</summary>
	public string? RefreshToken { get; }

	/// <summary>Gets a value indicating whether this refresh rotated the refresh token itself.</summary>
	public bool RefreshTokenChanged { get; }
	}
