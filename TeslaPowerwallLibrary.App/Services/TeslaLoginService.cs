// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// The outcome of attempting to acquire Tesla cloud tokens through the WebView2 setup application.
/// </summary>
public enum TeslaLoginStatus
	{
	/// <summary>Tokens were acquired successfully.</summary>
	Success,

	/// <summary>The setup executable could not be located.</summary>
	NotFound,

	/// <summary>The user closed the login window before completing authentication.</summary>
	Cancelled,

	/// <summary>The login process started but failed or timed out.</summary>
	Failed
	}

/// <summary>The Tesla cloud tokens captured from a successful browser login.</summary>
/// <param name="RefreshToken">The long-lived refresh token.</param>
/// <param name="AccessToken">The short-lived access token.</param>
/// <param name="Email">The Tesla account email parsed from the id_token, when available.</param>
public sealed record TeslaCloudTokens (string RefreshToken, string AccessToken, string Email);

/// <summary>The result of a Tesla login attempt.</summary>
/// <param name="Status">The outcome category.</param>
/// <param name="Tokens">The captured tokens when <see cref="Status"/> is <see cref="TeslaLoginStatus.Success"/>.</param>
/// <param name="Message">An explanatory message for non-success outcomes.</param>
public readonly record struct TeslaLoginResult (TeslaLoginStatus Status, TeslaCloudTokens? Tokens, string? Message);

/// <summary>
/// Locates and launches the Tesla Powerwall setup application (<c>TeslaPowerwallSetup.exe</c>) in
/// non-interactive emit mode, then reads the captured Tesla cloud tokens from its standard output.
/// This lets the desktop app acquire cloud tokens through Tesla's real browser login (which handles
/// captcha and multi-factor authentication) without the user copying and pasting tokens by hand,
/// mirroring the behavior of the test console's login flow.
/// </summary>
public static class TeslaLoginService
	{
	private const string SetupExecutableName = "TeslaPowerwallSetup.exe";
	private const string SetupProjectFolder = "TeslaPowerwallLibrary.Setup";
	private const string SetupTargetFramework = "net10.0-windows";

	// Mirrors TeslaPowerwallLibrary.Setup.MainWindow.TokenSentinel. Duplicated here because the
	// setup app is launched as a separate process and is not referenced at compile time.
	private const string TokenSentinel = "__PWTOKENS__=";

	/// <summary>
	/// Launches the setup application's browser login and returns the captured Tesla cloud tokens.
	/// The login window runs out-of-process so the desktop app's UI thread stays responsive.
	/// </summary>
	/// <param name="region">The Tesla region to authenticate against (<c>us</c> or <c>cn</c>).</param>
	/// <param name="timeout">Maximum time to wait for the user to complete the login.</param>
	/// <param name="cancellationToken">A token used to abandon the login early.</param>
	/// <returns>The login result, including tokens on success.</returns>
	public static async Task<TeslaLoginResult> AcquireTokensAsync (string region, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		var executable = LocateSetupExecutable ();
		if (executable is null)
			{
			return new TeslaLoginResult (
				TeslaLoginStatus.NotFound,
				null,
				"Could not locate TeslaPowerwallSetup.exe. Build the TeslaPowerwallLibrary.Setup project, "
				+ "or set the PW_SETUP_EXE environment variable to its full path.");
			}

		var safeRegion = string.Equals (region, "cn", StringComparison.OrdinalIgnoreCase) ? "cn" : "us";
		var startInfo = new ProcessStartInfo
			{
			FileName = executable,
			Arguments = $"--emit --region {safeRegion}",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = false
			};

		using var process = new Process { StartInfo = startInfo };

		var output = new StringBuilder ();
		var error = new StringBuilder ();
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine (e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine (e.Data); };

		try
			{
			if (!process.Start ())
				throw new InvalidOperationException ("Process.Start returned false.");
			}
		catch (Exception exc) when (exc is System.ComponentModel.Win32Exception or InvalidOperationException)
			{
			return new TeslaLoginResult (TeslaLoginStatus.Failed, null, $"Unable to start the setup application: {exc.Message}");
			}

		process.BeginOutputReadLine ();
		process.BeginErrorReadLine ();

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		timeoutCts.CancelAfter (timeout);

		try
			{
			await process.WaitForExitAsync (timeoutCts.Token).ConfigureAwait (false);
			}
		catch (OperationCanceledException)
			{
			TryKill (process);
			return cancellationToken.IsCancellationRequested
				? new TeslaLoginResult (TeslaLoginStatus.Cancelled, null, "Login was cancelled before completion.")
				: new TeslaLoginResult (TeslaLoginStatus.Failed, null, "Timed out waiting for the Tesla login to complete.");
			}

		// Ensure the async output handlers have flushed all buffered lines.
		process.WaitForExit ();

		if (process.ExitCode == 2)
			return new TeslaLoginResult (TeslaLoginStatus.Cancelled, null, "Login was cancelled before completion.");

		var tokens = ParseTokens (output.ToString ());
		if (process.ExitCode == 0 && tokens is not null)
			return new TeslaLoginResult (TeslaLoginStatus.Success, tokens, null);

		var message = error.Length > 0 ? error.ToString ().Trim () : "The Tesla login did not return any tokens.";
		return new TeslaLoginResult (TeslaLoginStatus.Failed, null, message);
		}

	private static TeslaCloudTokens? ParseTokens (string output)
		{
		using var reader = new StringReader (output);
		string? line;
		while ((line = reader.ReadLine ()) is not null)
			{
			var trimmed = line.Trim ();
			if (!trimmed.StartsWith (TokenSentinel, StringComparison.Ordinal))
				continue;

			var encoded = trimmed[TokenSentinel.Length..];
			try
				{
				var json = Encoding.UTF8.GetString (Convert.FromBase64String (encoded));
				var payload = JObject.Parse (json);
				var refreshToken = payload.Value<string> ("refresh_token");
				if (string.IsNullOrWhiteSpace (refreshToken))
					return null;

				return new TeslaCloudTokens (
					refreshToken!,
					payload.Value<string> ("access_token") ?? string.Empty,
					payload.Value<string> ("email") ?? string.Empty);
				}
			catch (Exception exc) when (exc is FormatException or Newtonsoft.Json.JsonException)
				{
				return null;
				}
			}

		return null;
		}

	private static string? LocateSetupExecutable ()
		{
		var overridePath = Environment.GetEnvironmentVariable ("PW_SETUP_EXE");
		if (!string.IsNullOrWhiteSpace (overridePath) && File.Exists (overridePath))
			return overridePath;

		// Probe the directory tree above the running app for the setup project's build output.
		var directory = new DirectoryInfo (AppContext.BaseDirectory);
		var configurations = PreferredConfigurations (directory.FullName);

		while (directory is not null)
			{
			var projectRoot = Path.Combine (directory.FullName, SetupProjectFolder);
			if (Directory.Exists (projectRoot))
				{
				foreach (var configuration in configurations)
					{
					var candidate = Path.Combine (
						projectRoot, "bin", configuration, SetupTargetFramework, SetupExecutableName);
					if (File.Exists (candidate))
						return candidate;
					}
				}

			directory = directory.Parent;
			}

		return null;
		}

	private static string[] PreferredConfigurations (string baseDirectory) =>
		baseDirectory.Contains (@"\Release\", StringComparison.OrdinalIgnoreCase)
			? ["Release", "Debug"]
			: ["Debug", "Release"];

	private static void TryKill (Process process)
		{
		try
			{
			if (!process.HasExited)
				process.Kill ();
			}
		catch (Exception exc) when (exc is InvalidOperationException or System.ComponentModel.Win32Exception)
			{
			// The process may have exited between the check and the kill; nothing more to do.
			}
		}
	}
