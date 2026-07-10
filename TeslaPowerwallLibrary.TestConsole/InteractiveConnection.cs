// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Cloud;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Owns the live <see cref="Powerwall"/> connection for an interactive session and supports switching the
/// active cloud or local account in place. The cloud (tokens/email/site) and local (host/password) credential
/// groups are tracked independently, so changing one never disturbs the other.
/// </summary>
internal sealed class InteractiveConnection : IDisposable
	{
	private readonly string _region;
	private readonly bool _noSave;

	/// <summary>Initializes a new session around an already-connected <see cref="Powerwall"/>.</summary>
	/// <param name="powerwall">The connected Powerwall instance.</param>
	/// <param name="options">The options used to establish <paramref name="powerwall"/>.</param>
	/// <param name="region">The normalized Tesla region used for cloud browser login.</param>
	/// <param name="noSave">When <see langword="true"/>, switching accounts does not persist the new settings.</param>
	public InteractiveConnection (Powerwall powerwall, PowerwallOptions options, string region, bool noSave)
		{
		Powerwall = powerwall ?? throw new ArgumentNullException (nameof (powerwall));
		Options = options ?? throw new ArgumentNullException (nameof (options));
		_region = string.IsNullOrWhiteSpace (region) ? "us" : region;
		_noSave = noSave;
		}

	/// <summary>Gets the currently connected Powerwall™ for the session.</summary>
	public Powerwall Powerwall { get; private set; }

	/// <summary>Gets the options backing the current connection, carrying both credential groups.</summary>
	public PowerwallOptions Options { get; private set; }

	/// <summary>Gets the normalized Tesla™ region (<c>us</c> or <c>cn</c>) used for cloud browser login.</summary>
	public string Region => _region;

	/// <summary>
	/// Switches the active connection to the Tesla cloud using the supplied tokens, changing only the
	/// cloud credential group (tokens, email, and site selection) and leaving any local host/password intact.
	/// On failure the current connection is preserved.
	/// </summary>
	/// <param name="tokens">The Tesla cloud tokens (and email) to connect with.</param>
	/// <param name="cancellationToken">Token used to cancel the reconnect.</param>
	/// <returns><see langword="true"/> when the switch and reconnect succeed; otherwise <see langword="false"/>.</returns>
	public Task<bool> SwitchCloudAsync (CloudTokens tokens, CancellationToken cancellationToken)
		{
		if (tokens is null)
			throw new ArgumentNullException (nameof (tokens));

		return ReconnectAsync (BuildCloudOptions (Options, tokens), cancellationToken);
		}

	/// <summary>
	/// Switches the active connection to a local gateway using the supplied host and password, changing only
	/// the local credential group and leaving any cloud tokens intact. On failure the current connection is preserved.
	/// </summary>
	/// <param name="host">The Powerwall gateway host or IP address.</param>
	/// <param name="password">The Powerwall customer password.</param>
	/// <param name="cancellationToken">Token used to cancel the reconnect.</param>
	/// <returns><see langword="true"/> when the switch and reconnect succeed; otherwise <see langword="false"/>.</returns>
	public Task<bool> SwitchLocalAsync (string host, string password, CancellationToken cancellationToken)
		{
		if (string.IsNullOrWhiteSpace (host))
			throw new ArgumentException ("A host is required for a local connection.", nameof (host));

		return ReconnectAsync (BuildLocalOptions (Options, host, password), cancellationToken);
		}

	/// <summary>
	/// Switches the active connection to Tesla FleetAPI using the supplied credentials, changing only the
	/// FleetAPI credential group and leaving any cloud/local credentials intact. The caller-supplied refresh
	/// token (and Client ID needed to renew it) are required here since this is an explicit switch command;
	/// the library persists them internally afterward so a later plain reconnect can omit them. On failure
	/// the current connection is preserved.
	/// </summary>
	/// <param name="clientId">Tesla FleetAPI application Client ID.</param>
	/// <param name="refreshToken">Tesla FleetAPI OAuth refresh token.</param>
	/// <param name="region">Tesla FleetAPI region (<c>na</c>, <c>eu</c>, or <c>cn</c>).</param>
	/// <param name="cancellationToken">Token used to cancel the reconnect.</param>
	/// <returns><see langword="true"/> when the switch and reconnect succeed; otherwise <see langword="false"/>.</returns>
	public Task<bool> SwitchFleetApiAsync (string clientId, string refreshToken, string region, CancellationToken cancellationToken)
		{
		if (string.IsNullOrWhiteSpace (clientId))
			throw new ArgumentException ("A Tesla FleetAPI Client ID is required.", nameof (clientId));

		if (string.IsNullOrWhiteSpace (refreshToken))
			throw new ArgumentException ("A Tesla FleetAPI refresh token is required.", nameof (refreshToken));

		return ReconnectAsync (BuildFleetApiOptions (Options, clientId, refreshToken, region), cancellationToken);
		}

	// Builds a new connection from the candidate options, connecting before swapping so a failed switch
	// preserves the current connection. Disposes the previous connection only after a successful reconnect.
	private async Task<bool> ReconnectAsync (PowerwallOptions candidate, CancellationToken cancellationToken)
		{
		Powerwall newConnection;
		try
			{
			newConnection = new Powerwall (candidate);
			}
		catch (Exception exc) when (exc is PowerwallInvalidConfigurationException or ArgumentException)
			{
			ConsoleHelpers.WriteError ($"Configuration error: {exc.Message}");
			return false;
			}

		try
			{
			if (!await newConnection.ConnectAsync (cancellationToken).ConfigureAwait (false))
				{
				ConsoleHelpers.WriteError ("Failed to connect with the new credentials; keeping the current connection.");
				newConnection.Dispose ();
				return false;
				}
			}
		catch (OperationCanceledException)
			{
			newConnection.Dispose ();
			throw;
			}
		catch (PowerwallException exc)
			{
			ConsoleHelpers.WriteError ($"Connection error: {exc.Message}; keeping the current connection.");
			newConnection.Dispose ();
			return false;
			}

		var previous = Powerwall;
		Powerwall = newConnection;
		Options = candidate;
		previous.Dispose ();

		if (!_noSave)
			CliOptions.PersistOptions (candidate, _region);

		return true;
		}

	/// <summary>
	/// Records a site selection made via <c>changesite</c> so the session's options stay consistent. The
	/// library persists the selected site itself (keyed by email), so no console-side save is needed.
	/// </summary>
	/// <param name="siteId">The Tesla energy site identifier that is now active.</param>
	public void UpdateSelectedSite (string siteId)
		{
		if (string.IsNullOrWhiteSpace (siteId))
			return;

		Options = Options with { SiteId = siteId };
		}

	// Produces options that activate cloud mode with the supplied tokens, changing only the cloud credential
	// group. Site selection is reset so the new account resolves its own default site. Local host/password
	// are preserved via the record copy so a later local switch remains possible.
	internal static PowerwallOptions BuildCloudOptions (PowerwallOptions current, CloudTokens tokens) =>
		current with
			{
			CloudMode = true,
			AccessToken = tokens.AccessToken,
			RefreshToken = tokens.RefreshToken,
			Email = string.IsNullOrWhiteSpace (tokens.Email) ? current.Email : tokens.Email,
			SiteId = null
			};

	// Produces options that activate local mode with the supplied host/password, changing only the local
	// credential group. Cloud tokens, email, and site are preserved via the record copy so a later cloud
	// switch remains possible.
	internal static PowerwallOptions BuildLocalOptions (PowerwallOptions current, string host, string password) =>
		current with
			{
			CloudMode = false,
			Host = host,
			Password = password
			};

	// Produces options that activate FleetAPI mode with the supplied credentials, changing only the FleetAPI
	// credential group. Site selection is reset so the new account resolves its own default site. Cloud and
	// local credentials are preserved via the record copy so a later switch back remains possible.
	internal static PowerwallOptions BuildFleetApiOptions (PowerwallOptions current, string clientId, string refreshToken, string region) =>
		current with
			{
			CloudMode = true,
			FleetApi = true,
			FleetApiClientId = clientId,
			FleetApiRefreshToken = refreshToken,
			FleetApiAccessToken = null,
			FleetApiRegion = CliOptions.NormalizeFleetApiRegion (region),
			SiteId = null
			};

	/// <summary>Disposes the current connection.</summary>
	public void Dispose () => Powerwall.Dispose ();
	}
