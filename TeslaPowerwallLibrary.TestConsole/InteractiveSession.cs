// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Interactive read-eval-print loop that dispatches named commands against a connected
/// <see cref="Powerwall"/> instance until the user exits.
/// </summary>
internal static class InteractiveSession
	{
	/// <summary>Runs the interactive loop until the user types <c>exit</c> or cancellation is requested.</summary>
	/// <param name="session">The session that owns the current connection and supports account switching.</param>
	/// <param name="cancellationToken">Token used to cancel the session.</param>
	/// <returns>A process exit code.</returns>
	public static async Task<int> RunAsync (InteractiveConnection session, CancellationToken cancellationToken)
		{
		ConsoleHelpers.WriteSuccess ($"Connected in {session.Powerwall.Mode} mode. Type 'help' for commands, 'exit' to quit.");

		while (!cancellationToken.IsCancellationRequested)
			{
			Console.Write ("powerwall> ");
			var line = Console.ReadLine ();
			if (line is null)
				break;

			line = line.Trim ();
			if (line.Length == 0)
				continue;

			var parts = line.Split (new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
			var command = parts[0].ToLowerInvariant ();
			var argument = parts.Length > 1 ? parts[1].Trim () : null;

			if (command is "exit" or "quit")
				break;

			try
				{
				if (!await DispatchAsync (session, command, argument, cancellationToken).ConfigureAwait (false))
					ConsoleHelpers.WriteError ($"Unknown command '{command}'. Type 'help' for the list of commands.");
				}
			catch (PowerwallException exc)
				{
				ConsoleHelpers.WriteError ($"Error: {exc.Message}");
				}
			catch (ArgumentException exc)
				{
				ConsoleHelpers.WriteError ($"Error: {exc.Message}");
				}
			catch (InvalidOperationException exc)
				{
				ConsoleHelpers.WriteError ($"Error: {exc.Message}");
				}
			}

		return 0;
		}

	private static async Task<bool> DispatchAsync (InteractiveConnection session, string command, string? argument, CancellationToken cancellationToken)
		{
		var powerwall = session.Powerwall;
		switch (command)
			{
			case "help":
			case "?":
				WriteHelp ();
				return true;

			case "status":
				await PowerwallActions.StatusAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "sitename":
				await PowerwallActions.SiteNameAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "email":
				await PowerwallActions.EmailAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "level":
				await PowerwallActions.LevelAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "power":
				await PowerwallActions.PowerAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "grid":
				await PowerwallActions.GridAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "operation":
				await PowerwallActions.OperationAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "timeremaining":
				await PowerwallActions.TimeRemainingAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "system":
				await PowerwallActions.SystemStatusAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "summary":
				await PowerwallActions.SummaryAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "setreserve":
				await SetReserveAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "setmode":
				await SetModeAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "sites":
				await PowerwallActions.SitesAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "changesite":
				await ChangeSiteAsync (session, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "gridconfig":
				await PowerwallActions.GridConfigAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "setgridcharging":
				await SetGridChargingAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "setgridexport":
				await SetGridExportAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "stormwatch":
				await PowerwallActions.StormWatchAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "setstormwatch":
				await SetStormWatchAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "vitals":
				await PowerwallActions.VitalsAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "alerts":
				await PowerwallActions.AlertsAsync (powerwall, cancellationToken).ConfigureAwait (false);
				return true;

			case "history":
				await HistoryAsync (powerwall, argument, calendar: false, cancellationToken).ConfigureAwait (false);
				return true;

			case "calendarhistory":
				await HistoryAsync (powerwall, argument, calendar: true, cancellationToken).ConfigureAwait (false);
				return true;

			case "login":
				await LoginAsync (session, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "switchaccount":
				await SwitchAccountAsync (session, argument, cancellationToken).ConfigureAwait (false);
				return true;

			case "poll":
				await PollAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
				return true;

			default:
				return false;
			}
		}

	private static async Task LoginAsync (InteractiveConnection session, string? argument, CancellationToken cancellationToken)
		{
		var target = argument?.Trim ().ToLowerInvariant ();
		switch (target)
			{
			case "cloud":
				var tokens = await CliOptions.LaunchTeslaLoginAsync (session.Region).ConfigureAwait (false);
				if (tokens is null)
					return;

				if (await session.SwitchCloudAsync (tokens, cancellationToken).ConfigureAwait (false))
					ConsoleHelpers.WriteSuccess ($"Switched to cloud account ({tokens.Email}).");
				return;

			case "local":
				var credentials = CliOptions.PromptLocalCredentials ();
				if (credentials is null)
					return;

				if (await session.SwitchLocalAsync (credentials.Value.Host, credentials.Value.Password, cancellationToken).ConfigureAwait (false))
					ConsoleHelpers.WriteSuccess ($"Switched to local gateway ({credentials.Value.Host}).");
				return;

			default:
				ConsoleHelpers.WriteError ("Usage: login <cloud|local>");
				return;
			}
		}

	private static async Task SwitchAccountAsync (InteractiveConnection session, string? argument, CancellationToken cancellationToken)
		{
		var target = argument?.Trim ().ToLowerInvariant ();
		var options = session.Options;
		switch (target)
			{
			case "cloud":
				var hasSessionTokens = !string.IsNullOrWhiteSpace (options.AccessToken) || !string.IsNullOrWhiteSpace (options.RefreshToken);
				if (!hasSessionTokens && !Powerwall.HasStoredCloudTokens (options.Email))
					{
					ConsoleHelpers.WriteError ("No cloud tokens are available for this session. Use 'login cloud' to sign in first.");
					return;
					}

				// Empty tokens are fine here: the library falls back to the tokens it cached for this email.
				var tokens = new CloudTokens (options.RefreshToken ?? string.Empty, options.AccessToken ?? string.Empty, options.Email);
				if (await session.SwitchCloudAsync (tokens, cancellationToken).ConfigureAwait (false))
					ConsoleHelpers.WriteSuccess ($"Switched to cloud account ({options.Email}).");
				return;

			case "local":
				if (string.IsNullOrWhiteSpace (options.Host))
					{
					ConsoleHelpers.WriteError ("No local gateway is configured for this session. Use 'login local' to set one first.");
					return;
					}

				if (await session.SwitchLocalAsync (options.Host, options.Password, cancellationToken).ConfigureAwait (false))
					ConsoleHelpers.WriteSuccess ($"Switched to local gateway ({options.Host}).");
				return;

			default:
				ConsoleHelpers.WriteError ("Usage: switchaccount <cloud|local>");
				return;
			}
		}

	private static async Task SetReserveAsync (Powerwall powerwall, string? argument, CancellationToken cancellationToken)
		{
		if (!double.TryParse (argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var level))
			{
			ConsoleHelpers.WriteError ("Usage: setreserve <0-100>");
			return;
			}

		await PowerwallActions.SetReserveAsync (powerwall, level, cancellationToken).ConfigureAwait (false);
		}

	private static async Task SetModeAsync (Powerwall powerwall, string? argument, CancellationToken cancellationToken)
		{
		if (string.IsNullOrWhiteSpace (argument))
			{
			ConsoleHelpers.WriteError ("Usage: setmode <self_consumption|backup|autonomous>");
			return;
			}

		await PowerwallActions.SetModeAsync (powerwall, argument!, cancellationToken).ConfigureAwait (false);
		}

	private static async Task ChangeSiteAsync (InteractiveConnection session, string? argument, CancellationToken cancellationToken)
		{
		if (string.IsNullOrWhiteSpace (argument))
			{
			ConsoleHelpers.WriteError ("Usage: changesite <site-id or site name>");
			return;
			}

		var site = await PowerwallActions.ChangeSiteAsync (session.Powerwall, argument!, cancellationToken).ConfigureAwait (false);
		if (site is not null)
			session.UpdateSelectedSite (site.SiteId);
		}

	private static async Task SetGridChargingAsync (Powerwall powerwall, string? argument, CancellationToken cancellationToken)
		{
		var value = argument?.Trim ().ToLowerInvariant ();
		bool enabled;
		if (value is "on" or "yes" or "true")
			enabled = true;
		else if (value is "off" or "no" or "false")
			enabled = false;
		else
			{
			ConsoleHelpers.WriteError ("Usage: setgridcharging <on|off>");
			return;
			}

		await PowerwallActions.SetGridChargingAsync (powerwall, enabled, cancellationToken).ConfigureAwait (false);
		}

	private static async Task SetGridExportAsync (Powerwall powerwall, string? argument, CancellationToken cancellationToken)
		{
		var mode = argument?.Trim ();
		if (mode is not ("battery_ok" or "pv_only" or "never"))
			{
			ConsoleHelpers.WriteError ("Usage: setgridexport <battery_ok|pv_only|never>");
			return;
			}

		await PowerwallActions.SetGridExportAsync (powerwall, mode, cancellationToken).ConfigureAwait (false);
		}

	private static async Task SetStormWatchAsync (Powerwall powerwall, string? argument, CancellationToken cancellationToken)
		{
		var value = argument?.Trim ().ToLowerInvariant ();
		bool enabled;
		if (value is "on" or "yes" or "true")
			enabled = true;
		else if (value is "off" or "no" or "false")
			enabled = false;
		else
			{
			ConsoleHelpers.WriteError ("Usage: setstormwatch <on|off>");
			return;
			}

		await PowerwallActions.SetStormWatchAsync (powerwall, enabled, cancellationToken).ConfigureAwait (false);
		}

	private static async Task HistoryAsync (Powerwall powerwall, string? argument, bool calendar, CancellationToken cancellationToken)
		{
		var command = calendar ? "calendarhistory" : "history";
		var kinds = calendar ? Powerwall.CalendarHistoryKinds : Powerwall.HistoryKinds;

		if (string.IsNullOrWhiteSpace (argument))
			{
			ConsoleHelpers.WriteError ($"Usage: {command} <kind> [period]");
			ConsoleHelpers.WriteError ($"  kind:   {ConsoleHelpers.FormatChoices (kinds)}");
			ConsoleHelpers.WriteError ($"  period: {ConsoleHelpers.FormatChoices (Powerwall.HistoryPeriods, Powerwall.DEFAULT_HISTORY_PERIOD)} (optional; {ConsoleHelpers.DefaultChoiceLegend})");
			ConsoleHelpers.WriteError ($"  example: {command} {kinds[0]} {Powerwall.HistoryPeriods[0]}");
			return;
			}

		var tokens = argument!.Split ((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
		var kind = tokens[0];
		var period = tokens.Length > 1 ? tokens[1] : null;

		if (calendar)
			await PowerwallActions.CalendarHistoryAsync (powerwall, kind, period, cancellationToken).ConfigureAwait (false);
		else
			await PowerwallActions.HistoryAsync (powerwall, kind, period, cancellationToken).ConfigureAwait (false);
		}

	private static async Task PollAsync (Powerwall powerwall, string? argument, CancellationToken cancellationToken)
		{
		if (string.IsNullOrWhiteSpace (argument))
			{
			ConsoleHelpers.WriteError ("Usage: poll <api-endpoint>   e.g. poll /api/meters/aggregates");
			return;
			}

		await PowerwallActions.PollAsync (powerwall, argument!, cancellationToken).ConfigureAwait (false);
		}

	private static void WriteHelp ()
		{
		ConsoleHelpers.WriteHeading ("Commands");
		Console.WriteLine ("  status            Gateway status, version, DIN, and uptime");
		Console.WriteLine ("  sitename          Configured site name");
		Console.WriteLine ("  email             Email address of the account currently in use");
		Console.WriteLine ("  level             Battery charge level (raw and scaled)");
		Console.WriteLine ("  power             Instantaneous site/solar/battery/load power");
		Console.WriteLine ("  grid              Normalized grid status");
		Console.WriteLine ("  operation         Operation mode and backup reserve");
		Console.WriteLine ("  timeremaining     Estimated backup time remaining");
		Console.WriteLine ("  system            Full system status with battery blocks");
		Console.WriteLine ("  summary           Combined dashboard of the above");
		Console.WriteLine ("  setreserve <n>    Set backup reserve level (0-100)");
		Console.WriteLine ("  setmode <mode>    Set mode (self_consumption|backup|autonomous)");
		Console.WriteLine ("  sites             List Tesla™ energy sites (cloud mode)");
		Console.WriteLine ("  changesite <id|name>   Switch the active site by id or name (cloud mode)");
		Console.WriteLine ("  gridconfig        Show grid charging and export settings (cloud mode)");
		Console.WriteLine ("  setgridcharging <on|off>              Enable/disable grid charging (cloud mode)");
		Console.WriteLine ("  setgridexport <battery_ok|pv_only|never>  Set grid export rule (cloud mode)");
		Console.WriteLine ("  stormwatch        Show whether Storm Watch is enabled (cloud mode)");
		Console.WriteLine ("  setstormwatch <on|off>                Enable/disable Storm Watch (cloud mode)");
		Console.WriteLine ("  vitals            Device vitals (cloud mode, or local firmware that exposes vitals)");
		Console.WriteLine ("  alerts            Active device alerts");
		Console.WriteLine ("  history <kind> [period]           Raw energy history (DEPRECATED - Tesla removed this endpoint; use calendarhistory)");
		Console.WriteLine ($"                      kind:   {ConsoleHelpers.FormatChoices (Powerwall.HistoryKinds)}");
		Console.WriteLine ($"                      period: {ConsoleHelpers.FormatChoices (Powerwall.HistoryPeriods, Powerwall.DEFAULT_HISTORY_PERIOD)}");
		Console.WriteLine ("  calendarhistory <kind> [period]   Calendar-aligned energy history (cloud mode)");
		Console.WriteLine ($"                      kind:   {ConsoleHelpers.FormatChoices (Powerwall.CalendarHistoryKinds)}");
		Console.WriteLine ($"                      period: {ConsoleHelpers.FormatChoices (Powerwall.HistoryPeriods, Powerwall.DEFAULT_HISTORY_PERIOD)}");
		Console.WriteLine ("  login <cloud|local>       Sign in to a new account and reconnect");
		Console.WriteLine ("  switchaccount <cloud|local>  Reconnect using this session's known cloud/local credentials");
		Console.WriteLine ("  poll <api>        GET a raw API endpoint and print the response");
		Console.WriteLine ("  help              Show this list");
		Console.WriteLine ("  exit              Quit the interactive session");
		Console.WriteLine ();
		Console.WriteLine ($"  {ConsoleHelpers.DefaultChoiceLegend}");
		}
	}
