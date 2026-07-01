// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Globalization;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Shared command-line options for connecting to a Powerwall, plus resolution logic that merges
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

	/// <summary>Forces Tesla Owners (cloud) mode even when a host is configured.</summary>
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
	public static ResolvedConnection Resolve (ParseResult parseResult)
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
			Environment.GetEnvironmentVariable ("PW_ACCESS_TOKEN"),
			CredentialProtector.Unprotect (settings.ProtectedAccessToken));
		var refreshToken = Coalesce (
			parseResult.GetValue (RefreshToken),
			Environment.GetEnvironmentVariable ("PW_REFRESH_TOKEN"),
			CredentialProtector.Unprotect (settings.ProtectedRefreshToken));
		var siteId = Coalesce (parseResult.GetValue (SiteId), Environment.GetEnvironmentVariable ("PW_SITE_ID"), settings.SiteId);

		// Cloud mode is selected explicitly via --cloud, or implicitly when no host is available.
		var cloudMode = parseResult.GetValue (Cloud) || string.IsNullOrWhiteSpace (host);

		if (!Console.IsInputRedirected && !cloudMode)
			{
			if (string.IsNullOrWhiteSpace (host))
				host = ConsoleHelpers.Prompt ("Powerwall host or IP");

			if (string.IsNullOrWhiteSpace (password))
				password = ConsoleHelpers.ReadPassword ("Powerwall password");
			}

		// In cloud mode, when no tokens are available yet, offer to launch the Tesla browser login
		// (handled by the WebView2 setup app) and capture the returned tokens for this session.
		var region = Coalesce (parseResult.GetValue (Region), Environment.GetEnvironmentVariable ("PW_REGION"), settings.Region);
		if (cloudMode
			&& !Console.IsInputRedirected
			&& string.IsNullOrWhiteSpace (accessToken)
			&& string.IsNullOrWhiteSpace (refreshToken))
			{
			var acquired = TryAcquireCloudTokensInteractively (region);
			if (acquired is not null)
				{
				accessToken = acquired.AccessToken;
				refreshToken = acquired.RefreshToken;
				if (string.IsNullOrWhiteSpace (email) && !string.IsNullOrWhiteSpace (acquired.Email))
					email = acquired.Email;
				}
			}

		var options = new PowerwallOptions
			{
			Host = host?.Trim () ?? string.Empty,
			Password = password ?? string.Empty,
			Email = string.IsNullOrWhiteSpace (email) ? Constants.DEFAULT_EMAIL : email!.Trim (),
			Timezone = string.IsNullOrWhiteSpace (timezone) ? Constants.DEFAULT_TIMEZONE : timezone!.Trim (),
			Timeout = TimeSpan.FromSeconds (timeout ?? Constants.DEFAULT_TIMEOUT_SECONDS),
			CacheExpireSeconds = cacheExpire ?? Constants.DEFAULT_CACHE_EXPIRE_SECONDS,
			CloudMode = cloudMode,
			AccessToken = accessToken,
			RefreshToken = refreshToken,
			SiteId = string.IsNullOrWhiteSpace (siteId) ? null : siteId!.Trim ()
			};

		if (!parseResult.GetValue (NoSave))
			Persist (options, timeout, cacheExpire, NormalizeRegion (region));

		return new ResolvedConnection (options, parseResult.GetValue (Verbose), NormalizeRegion (region), parseResult.GetValue (NoSave));
		}

	// Prompts the user to launch the Tesla browser login and returns the captured tokens, or null
	// when the user declines, cancels, or the setup application is unavailable.
	private static CloudTokens? TryAcquireCloudTokensInteractively (string? region)
		{
		ConsoleHelpers.WriteHeading ("Tesla Cloud Login");
		Console.WriteLine ("  No Tesla cloud tokens were found. Tesla requires a browser login (it handles");
		Console.WriteLine ("  captcha and two-factor sign-in), after which the tokens are cached securely.");

		var answer = ConsoleHelpers.Prompt ("  Open the Tesla login window now? [Y/n]")?.Trim ();
		if (answer is { Length: > 0 } && answer.StartsWith ("n", StringComparison.OrdinalIgnoreCase))
			return null;

		return LaunchTeslaLogin (region);
		}

	/// <summary>
	/// Launches the Tesla browser login (handled by the WebView2 setup app) and returns the captured
	/// tokens, or <see langword="null"/> when the user cancels or the setup application is unavailable.
	/// Used both at startup and by the interactive <c>login cloud</c> command.
	/// </summary>
	internal static CloudTokens? LaunchTeslaLogin (string? region)
		{
		Console.WriteLine ("  Launching Tesla login. Complete the sign-in in the window that opens...");
		var result = SetupLauncher.AcquireTokens (NormalizeRegion (region), TimeSpan.FromMinutes (5));

		switch (result.Status)
			{
			case SetupLaunchStatus.Success:
				ConsoleHelpers.WriteSuccess ("  Tesla login successful. Tokens captured and will be cached.");
				return result.Tokens;

			case SetupLaunchStatus.Cancelled:
				ConsoleHelpers.WriteError ("  Tesla login was cancelled.");
				return null;

			case SetupLaunchStatus.NotFound:
				ConsoleHelpers.WriteError ($"  {result.Message}");
				Console.WriteLine ("  Alternatively, run TeslaPowerwallSetup.exe manually and pass --access-token / --refresh-token.");
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

	internal static string NormalizeRegion (string? region) =>
		string.Equals (region?.Trim (), "cn", StringComparison.OrdinalIgnoreCase) ? "cn" : "us";

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

		Persist (options, timeoutSeconds, cacheExpireSeconds, NormalizeRegion (region));
		}

	private static void Persist (PowerwallOptions options, int? timeoutSeconds, int? cacheExpireSeconds, string region)
		{
		// Persist when there is a host (local) or a usable cloud token worth remembering;
		// the password and tokens are always stored encrypted.
		var hasCloudToken = !string.IsNullOrWhiteSpace (options.AccessToken) || !string.IsNullOrWhiteSpace (options.RefreshToken);
		if (string.IsNullOrWhiteSpace (options.Host) && !hasCloudToken)
			return;

		SettingsStore.Save (new ConsoleSettings
			{
			Host = string.IsNullOrWhiteSpace (options.Host) ? null : options.Host,
			ProtectedPassword = CredentialProtector.Protect (options.Password),
			Email = string.Equals (options.Email, Constants.DEFAULT_EMAIL, StringComparison.Ordinal) ? null : options.Email,
			Timezone = string.Equals (options.Timezone, Constants.DEFAULT_TIMEZONE, StringComparison.Ordinal) ? null : options.Timezone,
			TimeoutSeconds = timeoutSeconds,
			CacheExpireSeconds = cacheExpireSeconds,
			ProtectedAccessToken = CredentialProtector.Protect (options.AccessToken),
			ProtectedRefreshToken = CredentialProtector.Protect (options.RefreshToken),
			SiteId = options.SiteId,
			Region = string.Equals (region, "us", StringComparison.Ordinal) ? null : region
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
