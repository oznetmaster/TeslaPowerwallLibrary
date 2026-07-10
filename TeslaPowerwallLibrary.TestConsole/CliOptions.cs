// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Globalization;

using TeslaPowerwallLibrary.Login;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Shared command-line options for connecting to a Powerwall™, plus resolution logic that merges
/// command-line values, environment variables, and interactive prompts (in that order of precedence).
/// </summary>
internal static class CliOptions
	{
	/// <summary>Gateway host name or IP address (environment variable <c>PW_HOST</c>).</summary>
	public static Option<string?> Host { get; } = new ("--host")
		{
		Description = "Powerwall gateway host name or IP address (env: PW_HOST).",
		Recursive = true
		};

	/// <summary>Customer password (environment variable <c>PW_PASSWORD</c>).</summary>
	public static Option<string?> Password { get; } = new ("--password", "-p")
		{
		Description = "Powerwall customer password (env: PW_PASSWORD).",
		Recursive = true
		};

	/// <summary>Customer email (environment variable <c>PW_EMAIL</c>).</summary>
	public static Option<string?> Email { get; } = new ("--email", "-e")
		{
		Description = "Customer email, required for cloud mode (env: PW_EMAIL).",
		Recursive = true
		};

	/// <summary>Forces Tesla™ Owners (cloud) mode even when a host is configured.</summary>
	public static Option<bool> Cloud { get; } = new ("--cloud")
		{
		Description = "Use Tesla Owners (cloud) mode instead of local gateway access.",
		Recursive = true
		};

	/// <summary>Tesla Owners API access token for cloud mode (environment variable <c>PW_ACCESS_TOKEN</c>).</summary>
	public static Option<string?> AccessToken { get; } = new ("--access-token")
		{
		Description = "Tesla Owners API OAuth access token for cloud mode (env: PW_ACCESS_TOKEN).",
		Recursive = true
		};

	/// <summary>Tesla Owners API refresh token for cloud mode (environment variable <c>PW_REFRESH_TOKEN</c>).</summary>
	public static Option<string?> RefreshToken { get; } = new ("--refresh-token")
		{
		Description = "Tesla Owners API OAuth refresh token for cloud mode (env: PW_REFRESH_TOKEN).",
		Recursive = true
		};

	/// <summary>Tesla energy site identifier for cloud mode (environment variable <c>PW_SITE_ID</c>).</summary>
	public static Option<string?> SiteId { get; } = new ("--site-id")
		{
		Description = "Tesla energy site identifier to select in cloud mode (env: PW_SITE_ID).",
		Recursive = true
		};

	/// <summary>Tesla region used for the cloud browser login (environment variable <c>PW_REGION</c>).</summary>
	public static Option<string?> Region { get; } = new ("--region")
		{
		Description = "Tesla region for cloud login: us or cn (env: PW_REGION).",
		Recursive = true
		};

	/// <summary>Forces Tesla™ FleetAPI mode even when a host is configured.</summary>
	public static Option<bool> FleetApi { get; } = new ("--fleet-api")
		{
		Description = "Use Tesla FleetAPI mode instead of local gateway or cloud access.",
		Recursive = true
		};

	/// <summary>Tesla FleetAPI application Client ID (environment variable <c>PW_FLEETAPI_CLIENT_ID</c>).</summary>
	public static Option<string?> FleetApiClientId { get; } = new ("--fleet-api-client-id")
		{
		Description = "Tesla FleetAPI application Client ID (env: PW_FLEETAPI_CLIENT_ID).",
		Recursive = true
		};

	/// <summary>Tesla FleetAPI application Client Secret (environment variable <c>PW_FLEETAPI_CLIENT_SECRET</c>).</summary>
	public static Option<string?> FleetApiClientSecret { get; } = new ("--fleet-api-client-secret")
		{
		Description = "Tesla FleetAPI application Client Secret, reserved for future use (env: PW_FLEETAPI_CLIENT_SECRET).",
		Recursive = true
		};

	/// <summary>Tesla FleetAPI access token (environment variable <c>PW_FLEETAPI_ACCESS_TOKEN</c>).</summary>
	public static Option<string?> FleetApiAccessToken { get; } = new ("--fleet-api-access-token")
		{
		Description = "Tesla FleetAPI OAuth access token (env: PW_FLEETAPI_ACCESS_TOKEN).",
		Recursive = true
		};

	/// <summary>Tesla FleetAPI refresh token (environment variable <c>PW_FLEETAPI_REFRESH_TOKEN</c>).</summary>
	public static Option<string?> FleetApiRefreshToken { get; } = new ("--fleet-api-refresh-token")
		{
		Description = "Tesla FleetAPI OAuth refresh token (env: PW_FLEETAPI_REFRESH_TOKEN).",
		Recursive = true
		};

	/// <summary>Tesla FleetAPI region used to select the regional Fleet API base URL (environment variable <c>PW_FLEETAPI_REGION</c>).</summary>
	public static Option<string?> FleetApiRegion { get; } = new ("--fleet-api-region")
		{
		Description = "Tesla FleetAPI region: na, eu, or cn (env: PW_FLEETAPI_REGION).",
		Recursive = true
		};

	/// <summary>IANA time zone (environment variable <c>PW_TIMEZONE</c>).</summary>
	public static Option<string?> Timezone { get; } = new ("--timezone", "-z")
		{
		Description = "IANA time zone reported to the gateway (env: PW_TIMEZONE).",
		Recursive = true
		};

	/// <summary>Per-request HTTP timeout, in seconds (environment variable <c>PW_TIMEOUT</c>).</summary>
	public static Option<int?> Timeout { get; } = new ("--timeout", "-t")
		{
		Description = "Per-request HTTP timeout in seconds (env: PW_TIMEOUT).",
		Recursive = true
		};

	/// <summary>Cache expiry, in seconds (environment variable <c>PW_CACHE_EXPIRE</c>).</summary>
	public static Option<int?> CacheExpire { get; } = new ("--cache-expire", "-c")
		{
		Description = "Cached response expiry in seconds (env: PW_CACHE_EXPIRE).",
		Recursive = true
		};

	/// <summary>Enables verbose library logging to the console.</summary>
	public static Option<bool> Verbose { get; } = new ("--verbose")
		{
		Description = "Enable verbose library logging to the console.",
		Recursive = true
		};

	/// <summary>Disables persisting the resolved connection settings for the next run.</summary>
	public static Option<bool> NoSave { get; } = new ("--no-save")
		{
		Description = "Do not persist the resolved connection settings for the next run.",
		Recursive = true
		};

	/// <summary>
	/// Resolves a <see cref="PowerwallOptions"/> from the parsed command line, applying environment-variable
	/// and persisted-settings fallbacks (in that order) and prompting interactively for any missing host or
	/// password when input is not redirected. Unless <see cref="NoSave"/> is set, the resolved values are
	/// persisted (with the password encrypted) so they become the defaults on the next run.
	/// </summary>
	/// <param name="parseResult">The parsed command-line result.</param>
	/// <returns>The resolved connection options and verbose-logging flag.</returns>
	public static async Task<ResolvedConnection> ResolveAsync (ParseResult parseResult)
		{
		var settings = SettingsStore.Load ();

		var host = Coalesce (parseResult.GetValue (Host), Environment.GetEnvironmentVariable ("PW_HOST"), settings.Host);
		var password = Coalesce (
			parseResult.GetValue (Password),
			Environment.GetEnvironmentVariable ("PW_PASSWORD"),
			CredentialProtector.Unprotect (settings.ProtectedPassword));
		var email = Coalesce (parseResult.GetValue (Email), Environment.GetEnvironmentVariable ("PW_EMAIL"), settings.Email);
		var timezone = Coalesce (parseResult.GetValue (Timezone), Environment.GetEnvironmentVariable ("PW_TIMEZONE"), settings.Timezone);
		var timeout = parseResult.GetValue (Timeout)
			?? ParseInt (Environment.GetEnvironmentVariable ("PW_TIMEOUT"))
			?? settings.TimeoutSeconds;
		var cacheExpire = parseResult.GetValue (CacheExpire)
			?? ParseInt (Environment.GetEnvironmentVariable ("PW_CACHE_EXPIRE"))
			?? settings.CacheExpireSeconds;

		var accessToken = Coalesce (
			parseResult.GetValue (AccessToken),
			Environment.GetEnvironmentVariable ("PW_ACCESS_TOKEN"));
		var refreshToken = Coalesce (
			parseResult.GetValue (RefreshToken),
			Environment.GetEnvironmentVariable ("PW_REFRESH_TOKEN"));
		var siteId = Coalesce (parseResult.GetValue (SiteId), Environment.GetEnvironmentVariable ("PW_SITE_ID"));

		var fleetApiClientId = Coalesce (
			parseResult.GetValue (FleetApiClientId),
			Environment.GetEnvironmentVariable ("PW_FLEETAPI_CLIENT_ID"),
			settings.FleetApiClientId);
		var fleetApiClientSecret = Coalesce (
			parseResult.GetValue (FleetApiClientSecret),
			Environment.GetEnvironmentVariable ("PW_FLEETAPI_CLIENT_SECRET"));
		var fleetApiAccessToken = Coalesce (
			parseResult.GetValue (FleetApiAccessToken),
			Environment.GetEnvironmentVariable ("PW_FLEETAPI_ACCESS_TOKEN"));
		var fleetApiRefreshToken = Coalesce (
			parseResult.GetValue (FleetApiRefreshToken),
			Environment.GetEnvironmentVariable ("PW_FLEETAPI_REFRESH_TOKEN"),
			CredentialProtector.Unprotect (settings.ProtectedFleetApiRefreshToken));
		var fleetApiRegion = Coalesce (
			parseResult.GetValue (FleetApiRegion),
			Environment.GetEnvironmentVariable ("PW_FLEETAPI_REGION"),
			settings.FleetApiRegion);

		// Mode resolution precedence: an explicit --fleet-api always wins; then an explicit --cloud; then,
		// when no host is configured, fall back to the persisted FleetAPI preference from the last run that
		// resolved without a host (defaulting to Tesla Owners cloud mode); a configured host with neither
		// explicit flag selects local mode.
		var explicitCloud = parseResult.GetValue (Cloud);
		var explicitFleetApi = parseResult.GetValue (FleetApi);
		var preferFleetApi = settings.PreferFleetApi ?? false;
		var fleetApiMode = explicitFleetApi || (!explicitCloud && string.IsNullOrWhiteSpace (host) && preferFleetApi);
		var cloudMode = !fleetApiMode && (explicitCloud || string.IsNullOrWhiteSpace (host));

		if (!Console.IsInputRedirected && !cloudMode && !fleetApiMode)
			{
			if (string.IsNullOrWhiteSpace (host))
				host = ConsoleHelpers.Prompt ("Powerwall host or IP");

			if (string.IsNullOrWhiteSpace (password))
				password = ConsoleHelpers.ReadPassword ("Powerwall password");
			}

		// FleetAPI mode has no interactive browser login; when no refresh token was supplied and the library
		// has no cached tokens for this account, offer the full setup wizard (PEM verification, partner
		// registration, and authorize-code exchange) the same way cloud mode offers the browser login.
		// Otherwise, just prompt for the one required credential (a refresh token, plus the Client ID needed
		// to renew it). The library persists these internally after a successful connect, so this prompt is
		// normally only needed on the very first run.
		if (fleetApiMode && !Console.IsInputRedirected)
			{
			var resolvedEmailForFleetApi = string.IsNullOrWhiteSpace (email) ? Constants.DEFAULT_EMAIL : email!.Trim ();
			if (string.IsNullOrWhiteSpace (fleetApiRefreshToken) && !Powerwall.HasStoredFleetApiTokens (resolvedEmailForFleetApi))
				{
				var setup = await TryAcquireFleetApiTokensInteractivelyAsync (fleetApiRegion).ConfigureAwait (false);
				if (setup is not null)
					{
					fleetApiClientId = setup.Value.ClientId;
					fleetApiRefreshToken = setup.Value.RefreshToken;
					fleetApiRegion = setup.Value.Region;
					}
				}

			if (string.IsNullOrWhiteSpace (fleetApiClientId))
				fleetApiClientId = ConsoleHelpers.Prompt ("Tesla FleetAPI Client ID");

			if (string.IsNullOrWhiteSpace (fleetApiRefreshToken))
				fleetApiRefreshToken = ConsoleHelpers.ReadPassword ("Tesla FleetAPI refresh token");
			}

		// In cloud mode, offer the Tesla browser login only when neither explicit tokens were supplied nor the
		// library already has cached tokens for this account. After a first login the library persists and
		// reuses the tokens internally, so returning users are never prompted again.
		var region = Coalesce (parseResult.GetValue (Region), Environment.GetEnvironmentVariable ("PW_REGION"), settings.Region);
		var resolvedEmail = string.IsNullOrWhiteSpace (email) ? Constants.DEFAULT_EMAIL : email!.Trim ();
		if (cloudMode
			&& !Console.IsInputRedirected
			&& string.IsNullOrWhiteSpace (accessToken)
			&& string.IsNullOrWhiteSpace (refreshToken)
			&& !Powerwall.HasStoredCloudTokens (resolvedEmail))
			{
			var acquired = await TryAcquireCloudTokensInteractivelyAsync (region, email).ConfigureAwait (false);
			if (acquired is not null)
				{
				accessToken = acquired.AccessToken;
				refreshToken = acquired.RefreshToken;
				if (string.IsNullOrWhiteSpace (email) && !string.IsNullOrWhiteSpace (acquired.Email))
					{
					email = acquired.Email;
					resolvedEmail = acquired.Email.Trim ();
					}
				}
			}

		var options = new PowerwallOptions
			{
			Host = host?.Trim () ?? string.Empty,
			Password = password ?? string.Empty,
			Email = resolvedEmail,
			Timezone = string.IsNullOrWhiteSpace (timezone) ? Constants.DEFAULT_TIMEZONE : timezone!.Trim (),
			Timeout = TimeSpan.FromSeconds (timeout ?? Constants.DEFAULT_TIMEOUT_SECONDS),
			CacheExpireSeconds = cacheExpire ?? Constants.DEFAULT_CACHE_EXPIRE_SECONDS,
			CloudMode = cloudMode || fleetApiMode,
			AccessToken = accessToken,
			RefreshToken = refreshToken,
			SiteId = string.IsNullOrWhiteSpace (siteId) ? null : siteId!.Trim (),
			FleetApi = fleetApiMode,
			FleetApiClientId = string.IsNullOrWhiteSpace (fleetApiClientId) ? null : fleetApiClientId!.Trim (),
			FleetApiClientSecret = string.IsNullOrWhiteSpace (fleetApiClientSecret) ? null : fleetApiClientSecret!.Trim (),
			FleetApiAccessToken = fleetApiAccessToken,
			FleetApiRefreshToken = fleetApiRefreshToken,
			FleetApiRegion = NormalizeFleetApiRegion (fleetApiRegion)
			};

		if (!parseResult.GetValue (NoSave))
			Persist (options, timeout, cacheExpire, NormalizeRegion (region), fleetApiMode);

		return new ResolvedConnection (options, parseResult.GetValue (Verbose), NormalizeRegion (region), parseResult.GetValue (NoSave));
		}

	// Prompts the user to launch the Tesla browser login and returns the captured tokens, or null
	// when the user declines or cancels.
	private static async Task<CloudTokens?> TryAcquireCloudTokensInteractivelyAsync (string? region, string? email = null)
		{
		ConsoleHelpers.WriteHeading ("Tesla Cloud Login");
		Console.WriteLine ("  No Tesla cloud tokens were found. Tesla requires a browser login (it handles");
		Console.WriteLine ("  captcha and two-factor sign-in), after which the tokens are cached securely.");

		var answer = ConsoleHelpers.Prompt ("  Open the Tesla login window now? [Y/n]")?.Trim ();
		if (answer is { Length: > 0 } && answer.StartsWith ("n", StringComparison.OrdinalIgnoreCase))
			return null;

		return await LaunchTeslaLoginAsync (region, email).ConfigureAwait (false);
		}

	// Prompts the user to run the Tesla FleetAPI setup wizard and returns the captured Client ID, refresh
	// token, and region, or null when the user declines, cancels, or the wizard fails.
	private static async Task<(string ClientId, string RefreshToken, string Region)?> TryAcquireFleetApiTokensInteractivelyAsync (string? region)
		{
		ConsoleHelpers.WriteHeading ("Tesla FleetAPI Login");
		Console.WriteLine ("  No Tesla FleetAPI tokens were found. FleetAPI has no browser login; you can either");
		Console.WriteLine ("  run the setup wizard now (requires a registered Tesla developer application) or");
		Console.WriteLine ("  enter an existing Client ID and refresh token yourself.");

		var answer = ConsoleHelpers.Prompt ("  Run the FleetAPI setup wizard now? [Y/n]")?.Trim ();
		if (answer is { Length: > 0 } && answer.StartsWith ("n", StringComparison.OrdinalIgnoreCase))
			return null;

		return await PromptFleetApiSetupAsync (region).ConfigureAwait (false);
		}

	/// <summary>
	/// Launches the Tesla browser login and returns the captured tokens, or <see langword="null"/> when the
	/// user cancels or the login fails. Used both at startup and by the interactive <c>login cloud</c> command.
	/// </summary>
	/// <param name="region">The Tesla region to authenticate against (<c>us</c> or <c>cn</c>).</param>
	/// <param name="email">
	/// An optional email address used only to prefill the Tesla sign-in page. The user can still complete
	/// login with a different account; the returned <see cref="CloudTokens.Email"/> reflects whichever
	/// account actually signed in.
	/// </param>
	internal static async Task<CloudTokens?> LaunchTeslaLoginAsync (string? region, string? email = null)
		{
		Console.WriteLine ("  Launching Tesla login. Complete the sign-in in the window that opens...");
		var result = await SetupLauncher.AcquireTokensAsync (NormalizeRegion (region), TimeSpan.FromMinutes (5), email).ConfigureAwait (false);

		switch (result.Status)
			{
			case SetupLaunchStatus.Success:
				ConsoleHelpers.WriteSuccess ("  Tesla login successful. Tokens captured and will be cached.");
				return result.Tokens;

			case SetupLaunchStatus.Cancelled:
				ConsoleHelpers.WriteError ("  Tesla login was cancelled.");
				return null;

			default:
				ConsoleHelpers.WriteError ($"  Tesla login failed: {result.Message}");
				return null;
			}
		}

	/// <summary>
	/// Prompts for a Powerwall host and password for the interactive <c>login local</c> command,
	/// returning <see langword="null"/> when no host is entered.
	/// </summary>
	internal static (string Host, string Password)? PromptLocalCredentials ()
		{
		var host = ConsoleHelpers.Prompt ("Powerwall host or IP")?.Trim ();
		if (string.IsNullOrWhiteSpace (host))
			{
			ConsoleHelpers.WriteError ("  No host entered; local login cancelled.");
			return null;
			}

		var password = ConsoleHelpers.ReadPassword ("Powerwall password");
		return (host!, password);
		}

	/// <summary>
	/// Prompts for the Tesla FleetAPI Client ID and refresh token for the interactive <c>login fleetapi</c>
	/// command, returning <see langword="null"/> when either value is not entered. FleetAPI mode has no
	/// browser-login flow, so the initial refresh token must be supplied directly; the library then persists
	/// it (and subsequent rotations) internally, the same way it does for cloud mode.
	/// </summary>
	internal static (string ClientId, string RefreshToken, string Region)? PromptFleetApiCredentials (string? region)
		{
		var clientId = ConsoleHelpers.Prompt ("Tesla FleetAPI Client ID")?.Trim ();
		if (string.IsNullOrWhiteSpace (clientId))
			{
			ConsoleHelpers.WriteError ("  No Client ID entered; FleetAPI login cancelled.");
			return null;
			}

		var refreshToken = ConsoleHelpers.ReadPassword ("Tesla FleetAPI refresh token");
		if (string.IsNullOrWhiteSpace (refreshToken))
			{
			ConsoleHelpers.WriteError ("  No refresh token entered; FleetAPI login cancelled.");
			return null;
			}

		return (clientId!, refreshToken, NormalizeFleetApiRegion (region));
		}

	/// <summary>
	/// Runs the FleetAPI setup wizard adapted from upstream <c>pypowerwall</c>'s <c>fleetapi.setup()</c>:
	/// generates a partner token, registers the partner account, prints the authorize URL for the user to
	/// visit, and exchanges the returned authorization code for FleetAPI tokens. The caller must already
	/// have a registered Tesla developer application, a domain hosting the required PEM public key, and a
	/// matching redirect URI - this wizard does not create any of those, it only drives the OAuth exchange.
	/// </summary>
	/// <param name="region">The Tesla FleetAPI region (<c>na</c>, <c>eu</c>, or <c>cn</c>).</param>
	/// <returns>The Client ID, refresh token, and region on success; otherwise <see langword="null"/>.</returns>
	internal static async Task<(string ClientId, string RefreshToken, string Region)?> PromptFleetApiSetupAsync (string? region)
		{
		ConsoleHelpers.WriteHeading ("Tesla FleetAPI Setup");
		Console.WriteLine ("  This wizard requires a Tesla developer application already registered at");
		Console.WriteLine ("  https://developer.tesla.com/, with its public PEM key hosted at your domain.");
		Console.WriteLine ();

		var clientId = ConsoleHelpers.Prompt ("Tesla FleetAPI Client ID")?.Trim ();
		if (string.IsNullOrWhiteSpace (clientId))
			{
			ConsoleHelpers.WriteError ("  No Client ID entered; FleetAPI setup cancelled.");
			return null;
			}

		var clientSecret = ConsoleHelpers.ReadPassword ("Tesla FleetAPI Client Secret");
		if (string.IsNullOrWhiteSpace (clientSecret))
			{
			ConsoleHelpers.WriteError ("  No Client Secret entered; FleetAPI setup cancelled.");
			return null;
			}

		var domain = ConsoleHelpers.Prompt ("Registered domain (e.g. example.com)")?.Trim ();
		if (string.IsNullOrWhiteSpace (domain))
			{
			ConsoleHelpers.WriteError ("  No domain entered; FleetAPI setup cancelled.");
			return null;
			}

		var redirectUri = ConsoleHelpers.Prompt ($"Redirect URI [https://{domain}/access]")?.Trim ();
		if (string.IsNullOrWhiteSpace (redirectUri))
			redirectUri = $"https://{domain}/access";

		var normalizedRegion = NormalizeFleetApiRegion (region);
		var audience = FleetApiRegionAudience (normalizedRegion);

		Console.WriteLine ();
		Console.WriteLine ("  Verifying PEM key file...");
		if (!await TeslaFleetApiLogin.VerifyPemKeyAsync (domain!).ConfigureAwait (false))
			{
			ConsoleHelpers.WriteError ($"  ERROR: Could not verify PEM key file at https://{domain}/.well-known/appspecific/com.tesla.3p.public-key.pem");
			ConsoleHelpers.WriteError ("         Make sure the public key has been created and uploaded to your website.");
			return null;
			}

		ConsoleHelpers.WriteSuccess ("   * PEM key file verified.");
		Console.WriteLine ();

		Console.WriteLine ("  Generating partner authentication token...");
		var partnerTokenResult = await TeslaFleetApiLogin.GetPartnerTokenAsync (clientId!, clientSecret, audience).ConfigureAwait (false);
		if (partnerTokenResult.Status != TeslaFleetApiLoginStatus.Success)
			{
			ConsoleHelpers.WriteError ($"  ERROR: {partnerTokenResult.Message}");
			return null;
			}

		Console.WriteLine ();
		Console.WriteLine ("  Registering partner account...");
		var registerResult = await TeslaFleetApiLogin.RegisterPartnerAccountAsync (partnerTokenResult.PartnerToken!, audience, domain!).ConfigureAwait (false);
		if (registerResult.Status != TeslaFleetApiLoginStatus.Success)
			{
			ConsoleHelpers.WriteError ($"  ERROR: {registerResult.Message}");
			return null;
			}

		ConsoleHelpers.WriteSuccess ("   * Partner account registered.");
		Console.WriteLine ();

		var (authorizeUrl, _) = TeslaFleetApiLogin.BuildAuthorizeUrl (clientId!, redirectUri!);
		Console.WriteLine ("  Login to your Tesla account to authorize access.");
		Console.WriteLine ($"  Go to this URL: {authorizeUrl}");
		Console.WriteLine ();
		Console.WriteLine ("After authorizing access, copy the code from the redirected URL and paste it below.");
		var code = ConsoleHelpers.Prompt ("  Enter the code")?.Trim ();
		if (string.IsNullOrWhiteSpace (code))
			{
			ConsoleHelpers.WriteError ("  No code entered; FleetAPI setup cancelled.");
			return null;
			}

		if (code!.StartsWith ("http", StringComparison.OrdinalIgnoreCase))
			{
			var codeIndex = code.IndexOf ("code=", StringComparison.OrdinalIgnoreCase);
			if (codeIndex >= 0)
				{
				code = code.Substring (codeIndex + "code=".Length);
				var ampersandIndex = code.IndexOf ('&');
				if (ampersandIndex >= 0)
					code = code.Substring (0, ampersandIndex);
				}
			}

		Console.WriteLine ();
		Console.WriteLine ("  Exchanging authorization code for tokens...");
		var exchangeResult = await TeslaFleetApiLogin.ExchangeCodeAsync (clientId!, clientSecret, code!, redirectUri!, audience).ConfigureAwait (false);
		if (exchangeResult.Status != TeslaFleetApiLoginStatus.Success)
			{
			ConsoleHelpers.WriteError ($"  ERROR: {exchangeResult.Message}");
			return null;
			}

		ConsoleHelpers.WriteSuccess ("  Tokens generated. They will be cached by the library after connecting.");
		return (clientId!, exchangeResult.Tokens!.RefreshToken, normalizedRegion);
		}

	// Resolves the FleetAPI audience (regional base URL) matching the main library's FleetApiRegions mapping.
	private static string FleetApiRegionAudience (string region) =>
		region switch
			{
			"eu" => "https://fleet-api.prd.eu.vn.cloud.tesla.com",
			"cn" => "https://fleet-api.prd.cn.vn.cloud.tesla.cn",
			_ => "https://fleet-api.prd.na.vn.cloud.tesla.com"
			};

	internal static string NormalizeRegion (string? region) =>
		string.Equals (region?.Trim (), "cn", StringComparison.OrdinalIgnoreCase) ? "cn" : "us";

	/// <summary>Normalizes a Tesla FleetAPI region to one of <c>na</c>, <c>eu</c>, or <c>cn</c>, defaulting to <c>na</c>.</summary>
	internal static string NormalizeFleetApiRegion (string? region) =>
		region?.Trim ().ToLowerInvariant () switch
			{
			"eu" => "eu",
			"cn" => "cn",
			_ => "na"
			};

	/// <summary>
	/// Persists resolved options after a mid-session account switch, deriving the timeout and cache-expiry
	/// overrides only when they differ from the defaults so it mirrors startup persistence behavior.
	/// </summary>
	internal static void PersistOptions (PowerwallOptions options, string region)
		{
		var timeoutSeconds = (int) options.Timeout.TotalSeconds == Constants.DEFAULT_TIMEOUT_SECONDS
			? (int?) null
			: (int) options.Timeout.TotalSeconds;
		var cacheExpireSeconds = options.CacheExpireSeconds == Constants.DEFAULT_CACHE_EXPIRE_SECONDS
			? (int?) null
			: options.CacheExpireSeconds;

		Persist (options, timeoutSeconds, cacheExpireSeconds, NormalizeRegion (region), options.FleetApi);
		}

	private static void Persist (PowerwallOptions options, int? timeoutSeconds, int? cacheExpireSeconds, string region, bool fleetApiMode)
		{
		// Persist non-secret connection defaults only. Cloud and FleetAPI tokens and the selected site are
		// owned and persisted by the library itself (keyed by email), so the console never stores them. Still
		// persist the Client ID/region here (non-secret) when there is a host (local), a non-default
		// cloud/FleetAPI email worth remembering, or FleetAPI settings worth remembering, mirroring the local
		// password below.
		var hasCloudEmail = !string.Equals (options.Email, Constants.DEFAULT_EMAIL, StringComparison.Ordinal);
		var hasFleetApiSettings = fleetApiMode
			&& (!string.IsNullOrWhiteSpace (options.FleetApiClientId) || !string.IsNullOrWhiteSpace (options.FleetApiRefreshToken));
		if (string.IsNullOrWhiteSpace (options.Host) && !hasCloudEmail && !hasFleetApiSettings)
			return;

		SettingsStore.Save (new ConsoleSettings
			{
			Host = string.IsNullOrWhiteSpace (options.Host) ? null : options.Host,
			ProtectedPassword = CredentialProtector.Protect (options.Password),
			Email = hasCloudEmail ? options.Email : null,
			Timezone = string.Equals (options.Timezone, Constants.DEFAULT_TIMEZONE, StringComparison.Ordinal) ? null : options.Timezone,
			TimeoutSeconds = timeoutSeconds,
			CacheExpireSeconds = cacheExpireSeconds,
			Region = string.Equals (region, "us", StringComparison.Ordinal) ? null : region,
			FleetApiClientId = string.IsNullOrWhiteSpace (options.FleetApiClientId) ? null : options.FleetApiClientId,
			ProtectedFleetApiRefreshToken = CredentialProtector.Protect (options.FleetApiRefreshToken),
			FleetApiRegion = string.Equals (options.FleetApiRegion, "na", StringComparison.OrdinalIgnoreCase) ? null : options.FleetApiRegion,
			PreferFleetApi = fleetApiMode ? true : null
			});
		}

	private static string? Coalesce (params string?[] values) =>
		values.FirstOrDefault (static value => !string.IsNullOrWhiteSpace (value));

	private static int? ParseInt (string? value) =>
		int.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
	}

/// <summary>Resolved connection options together with the verbose-logging flag and session metadata.</summary>
/// <param name="Options">The resolved <see cref="PowerwallOptions"/>.</param>
/// <param name="Verbose">Whether verbose library logging was requested.</param>
/// <param name="Region">The normalized Tesla region (<c>us</c> or <c>cn</c>) used for cloud browser login.</param>
/// <param name="NoSave">Whether persistence of resolved settings is suppressed for the session.</param>
internal sealed record ResolvedConnection (PowerwallOptions Options, bool Verbose, string Region, bool NoSave);
