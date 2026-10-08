// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Security.Cryptography;
using TeslaPowerwallLibrary.Tedapi;
using TeslaPowerwallLibrary.Tools;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>Owns console-created signing keys and explicit setup-network connections.</summary>
/// <remarks>Dispose only after the primary Powerwall session. No adapter selection or cloud fallback is performed.</remarks>
internal sealed class LocalConsoleResources : IDisposable
	{
	private RSA? _key;
	private PowerwallTedapiClient? _setup;

	/// <summary>Resolved options containing resources owned by this scope.</summary>
	internal PowerwallOptions Options { get; private set; } = null!;

	/// <summary>Creates optional resources requested for this invocation, authenticating the setup route before primary use.</summary>
	/// <param name="resolved">Resolved primary connection.</param>
	/// <param name="result">Parsed invocation options.</param>
	/// <param name="cancellationToken">Cancels authentication.</param>
	/// <returns>Resource scope to keep alive until after the primary connection is disposed.</returns>
	internal static async Task<LocalConsoleResources> PrepareAsync (ResolvedConnection resolved, ParseResult result, CancellationToken cancellationToken)
		{
		var resources = new LocalConsoleResources ();
		try
			{
			string? setupHost = result.GetValue (CliOptions.LocalSetupHost);
			bool failover = result.GetValue (CliOptions.LocalReadFailover);
			int retry = result.GetValue (CliOptions.LocalRetrySeconds) ?? 60;
			var setupOptions = SetupOptions (resolved.Options, setupHost, Environment.GetEnvironmentVariable ("PW_SETUP_PASSWORD"), failover, retry);
			if (resolved.Options.LocalProtocol == PowerwallLocalProtocol.TedapiSigned)
				resources._key = LocalSigningKeyStore.Open (resolved.LocalKeyName);
			if (setupOptions is not null)
				{
				resources._setup = new PowerwallTedapiClient (setupOptions);
				await resources._setup.AuthenticateAsync (cancellationToken).ConfigureAwait (false);
				}
			resources.Options = resolved.Options with { LocalSigningKey = resources._key,
				LocalFollowerConnection = resources._setup, EnableLocalReadFailover = failover,
				LocalReadFailoverRetryInterval = TimeSpan.FromSeconds (retry) };
			return resources;
			}
		catch { resources.Dispose (); throw; }
		}

	/// <summary>Validates an explicit setup route without opening a key, contacting a device or persisting its password.</summary>
	/// <param name="primary">Primary connection settings.</param>
	/// <param name="host">Explicit setup-network host, or null for no alternate.</param>
	/// <param name="password">Setup-network label password from the invocation environment.</param>
	/// <param name="failover">Whether controller read failover is explicitly enabled.</param>
	/// <param name="retrySeconds">Positive LAN recovery delay.</param>
	/// <returns>Separate read-only setup options, or null when no alternate was requested.</returns>
	internal static PowerwallOptions? SetupOptions (PowerwallOptions primary, string? host, string? password, bool failover, int retrySeconds)
		{
		if (retrySeconds <= 0) throw new ArgumentException ("Local recovery delay must be positive.");
		if (string.IsNullOrWhiteSpace (host))
			{
			if (failover) throw new ArgumentException ("--local-read-failover requires --local-setup-host.");
			return null;
			}
		if (primary.CloudMode || primary.FleetApi || primary.LocalProtocol != PowerwallLocalProtocol.TedapiSigned)
			throw new ArgumentException ("--local-setup-host requires a signed local LAN connection.");
		if (string.IsNullOrWhiteSpace (password)) throw new ArgumentException ("Set PW_SETUP_PASSWORD to the equipment-label password for the explicit setup connection.");
		return new PowerwallOptions { Host = host!.Trim (), LocalProtocol = PowerwallLocalProtocol.Tedapi,
			GatewayPassword = password!, Password = primary.Password, Email = primary.Email, Timezone = primary.Timezone,
			LocalQueryVersion = primary.LocalQueryVersion, Timeout = primary.Timeout, CacheExpireSeconds = primary.CacheExpireSeconds,
			NoLocalSessionPersistence = true, AllowLocalControl = false, Logger = primary.Logger };
		}

	/// <summary>Disposes the owned alternate and key after the primary connection has finished.</summary>
	public void Dispose ()
		{
		_setup?.Dispose ();
		_setup = null;
		_key?.Dispose ();
		_key = null;
		}
	}
