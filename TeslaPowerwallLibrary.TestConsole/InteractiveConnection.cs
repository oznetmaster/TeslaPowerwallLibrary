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

	/// <summary>Gets the currently connected Powerwall for the session.</summary>
	public Powerwall Powerwall { get; private set; }

	/// <summary>Gets the options backing the current connection, carrying both credential groups.</summary>
	public PowerwallOptions Options { get; private set; }

	/// <summary>Gets the normalized Tesla region (<c>us</c> or <c>cn</c>) used for cloud browser login.</summary>
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

	/// <summary>Disposes the current connection.</summary>
	public void Dispose () => Powerwall.Dispose ();
	}
