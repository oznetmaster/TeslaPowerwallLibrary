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
	/// <param name="powerwall">The connected Powerwall instance.</param>
	/// <param name="cancellationToken">Token used to cancel the session.</param>
	/// <returns>A process exit code.</returns>
	public static async Task<int> RunAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		ConsoleHelpers.WriteSuccess ($"Connected in {powerwall.Mode} mode. Type 'help' for commands, 'exit' to quit.");

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
				if (!await DispatchAsync (powerwall, command, argument, cancellationToken).ConfigureAwait (false))
					ConsoleHelpers.WriteError ($"Unknown command '{command}'. Type 'help' for the list of commands.");
				}
			catch (PowerwallException exc)
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

	private static async Task<bool> DispatchAsync (Powerwall powerwall, string command, string? argument, CancellationToken cancellationToken)
		{
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
				await ChangeSiteAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
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

			case "poll":
				await PollAsync (powerwall, argument, cancellationToken).ConfigureAwait (false);
				return true;

			default:
				return false;
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

	private static async Task ChangeSiteAsync (Powerwall powerwall, string? argument, CancellationToken cancellationToken)
		{
		if (string.IsNullOrWhiteSpace (argument))
			{
			ConsoleHelpers.WriteError ("Usage: changesite <site-id>");
			return;
			}

		await PowerwallActions.ChangeSiteAsync (powerwall, argument!, cancellationToken).ConfigureAwait (false);
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
		Console.WriteLine ("  level             Battery charge level (raw and scaled)");
		Console.WriteLine ("  power             Instantaneous site/solar/battery/load power");
		Console.WriteLine ("  grid              Normalized grid status");
		Console.WriteLine ("  operation         Operation mode and backup reserve");
		Console.WriteLine ("  timeremaining     Estimated backup time remaining");
		Console.WriteLine ("  system            Full system status with battery blocks");
		Console.WriteLine ("  summary           Combined dashboard of the above");
		Console.WriteLine ("  setreserve <n>    Set backup reserve level (0-100)");
		Console.WriteLine ("  setmode <mode>    Set mode (self_consumption|backup|autonomous)");
		Console.WriteLine ("  sites             List Tesla energy sites (cloud mode)");
		Console.WriteLine ("  changesite <id>   Switch the active site (cloud mode)");
		Console.WriteLine ("  gridconfig        Show grid charging and export settings (cloud mode)");
		Console.WriteLine ("  setgridcharging <on|off>              Enable/disable grid charging (cloud mode)");
		Console.WriteLine ("  setgridexport <battery_ok|pv_only|never>  Set grid export rule (cloud mode)");
		Console.WriteLine ("  poll <api>        GET a raw API endpoint and print the response");
		Console.WriteLine ("  help              Show this list");
		Console.WriteLine ("  exit              Quit the interactive session");
		}
	}
