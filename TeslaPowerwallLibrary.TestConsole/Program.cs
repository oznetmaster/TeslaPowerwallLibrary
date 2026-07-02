// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.CommandLine;

using TeslaPowerwallLibrary;
using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.TestConsole;

var rootCommand = new RootCommand (
	"Interactive and command-line test harness for TeslaPowerwallLibrary. " +
	"Run without a subcommand to start an interactive session.");

rootCommand.Options.Add (CliOptions.Host);
rootCommand.Options.Add (CliOptions.Password);
rootCommand.Options.Add (CliOptions.Email);
rootCommand.Options.Add (CliOptions.Cloud);
rootCommand.Options.Add (CliOptions.AccessToken);
rootCommand.Options.Add (CliOptions.RefreshToken);
rootCommand.Options.Add (CliOptions.SiteId);
rootCommand.Options.Add (CliOptions.Region);
rootCommand.Options.Add (CliOptions.Timezone);
rootCommand.Options.Add (CliOptions.Timeout);
rootCommand.Options.Add (CliOptions.CacheExpire);
rootCommand.Options.Add (CliOptions.Verbose);
rootCommand.Options.Add (CliOptions.NoSave);

rootCommand.Subcommands.Add (CreateReadCommand ("status", "Show gateway status, version, DIN, and uptime.", PowerwallActions.StatusAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("sitename", "Show the configured site name.", PowerwallActions.SiteNameAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("level", "Show the battery charge level (raw and scaled).", PowerwallActions.LevelAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("power", "Show instantaneous site/solar/battery/load power.", PowerwallActions.PowerAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("grid", "Show the normalized grid status.", PowerwallActions.GridAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("operation", "Show the operation mode and backup reserve.", PowerwallActions.OperationAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("timeremaining", "Show the estimated backup time remaining.", PowerwallActions.TimeRemainingAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("system", "Show the full system status with battery blocks.", PowerwallActions.SystemStatusAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("summary", "Show a combined dashboard of all readings.", PowerwallActions.SummaryAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("sites", "List the Tesla energy sites for the account (cloud mode).", PowerwallActions.SitesAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("gridconfig", "Show the grid charging and export settings (cloud mode).", PowerwallActions.GridConfigAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("vitals", "Show device vitals (cloud mode, or local firmware that exposes vitals).", PowerwallActions.VitalsAsync));
rootCommand.Subcommands.Add (CreateReadCommand ("alerts", "Show the active device alerts.", PowerwallActions.AlertsAsync));
rootCommand.Subcommands.Add (CreateInteractiveCommand ());
rootCommand.Subcommands.Add (CreateSetReserveCommand ());
rootCommand.Subcommands.Add (CreateSetModeCommand ());
rootCommand.Subcommands.Add (CreateChangeSiteCommand ());
rootCommand.Subcommands.Add (CreateSetGridChargingCommand ());
rootCommand.Subcommands.Add (CreateSetGridExportCommand ());
rootCommand.Subcommands.Add (CreateHistoryCommand ());
rootCommand.Subcommands.Add (CreateCalendarHistoryCommand ());
rootCommand.Subcommands.Add (CreatePollCommand ());
rootCommand.Subcommands.Add (CreateConfigCommand ());

rootCommand.SetAction ((parseResult, cancellationToken) =>
	RunInteractiveAsync (parseResult, cancellationToken));

return await rootCommand.Parse (args).InvokeAsync ().ConfigureAwait (false);

static Command CreateReadCommand (string name, string description, Func<Powerwall, CancellationToken, Task> action)
	{
	var command = new Command (name, description);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			await action (powerwall, token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateInteractiveCommand ()
	{
	var command = new Command ("interactive", "Start an interactive REPL session (the default when no subcommand is given).");
	command.SetAction ((parseResult, cancellationToken) =>
		RunInteractiveAsync (parseResult, cancellationToken));

	return command;
	}

static Command CreateSetReserveCommand ()
	{
	var levelArgument = new Argument<double> ("level")
		{
		Description = "Backup reserve level in percent (0 - 100)."
		};

	var command = new Command ("setreserve", "Set the battery backup reserve level.");
	command.Arguments.Add (levelArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			await PowerwallActions.SetReserveAsync (powerwall, parseResult.GetValue (levelArgument), token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateSetModeCommand ()
	{
	var modeArgument = new Argument<string> ("mode")
		{
		Description = "Operation mode (self_consumption | backup | autonomous)."
		};

	var command = new Command ("setmode", "Set the battery operation mode.");
	command.Arguments.Add (modeArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			await PowerwallActions.SetModeAsync (powerwall, parseResult.GetValue (modeArgument) ?? string.Empty, token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateChangeSiteCommand ()
	{
	var siteArgument = new Argument<string> ("site")
		{
		Description = "Tesla energy site identifier or site name to switch to (cloud mode)."
		};

	var command = new Command ("changesite", "Switch the active Tesla energy site without reconnecting (cloud mode).");
	command.Arguments.Add (siteArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			var site = await PowerwallActions.ChangeSiteAsync (powerwall, parseResult.GetValue (siteArgument) ?? string.Empty, token).ConfigureAwait (false);
			if (site is not null && !parseResult.GetValue (CliOptions.NoSave))
				CliOptions.PersistSelectedSite (site.SiteId);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateSetGridChargingCommand ()
	{
	var modeArgument = new Argument<string> ("mode")
		{
		Description = "Grid charging mode (on | off)."
		};

	var command = new Command ("setgridcharging", "Enable or disable charging the battery from the grid (cloud mode).");
	command.Arguments.Add (modeArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			var value = parseResult.GetValue (modeArgument);
			if (!TryParseOnOff (value, out var enabled))
				{
				ConsoleHelpers.WriteError ("Usage: setgridcharging <on|off>");
				return 2;
				}

			await PowerwallActions.SetGridChargingAsync (powerwall, enabled, token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateSetGridExportCommand ()
	{
	var modeArgument = new Argument<string> ("mode")
		{
		Description = "Grid export rule (battery_ok | pv_only | never)."
		};

	var command = new Command ("setgridexport", "Set the grid export rule (cloud mode).");
	command.Arguments.Add (modeArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			await PowerwallActions.SetGridExportAsync (powerwall, parseResult.GetValue (modeArgument) ?? string.Empty, token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateHistoryCommand ()
	{
	var kindArgument = new Argument<string> ("kind")
		{
		Description = $"History kind ({ConsoleHelpers.FormatChoices (Powerwall.HistoryKinds)})."
		};

	var periodArgument = new Argument<string?> ("period")
		{
		Description = $"Aggregation period ({ConsoleHelpers.FormatChoices (Powerwall.HistoryPeriods, Powerwall.DefaultHistoryPeriod)}). Optional; {ConsoleHelpers.DefaultChoiceLegend}.",
		Arity = ArgumentArity.ZeroOrOne
		};

	var command = new Command ("history", "Show raw energy history (DEPRECATED: Tesla removed this endpoint - use 'calendarhistory').");
	command.Arguments.Add (kindArgument);
	command.Arguments.Add (periodArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			await PowerwallActions.HistoryAsync (powerwall, parseResult.GetValue (kindArgument) ?? string.Empty, parseResult.GetValue (periodArgument), token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateCalendarHistoryCommand ()
	{
	var kindArgument = new Argument<string> ("kind")
		{
		Description = $"History kind ({ConsoleHelpers.FormatChoices (Powerwall.CalendarHistoryKinds)})."
		};

	var periodArgument = new Argument<string?> ("period")
		{
		Description = $"Aggregation period ({ConsoleHelpers.FormatChoices (Powerwall.HistoryPeriods, Powerwall.DefaultHistoryPeriod)}). Optional; {ConsoleHelpers.DefaultChoiceLegend}.",
		Arity = ArgumentArity.ZeroOrOne
		};

	var command = new Command ("calendarhistory", "Show raw calendar-aligned energy history for the active site (cloud mode).");
	command.Arguments.Add (kindArgument);
	command.Arguments.Add (periodArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			await PowerwallActions.CalendarHistoryAsync (powerwall, parseResult.GetValue (kindArgument) ?? string.Empty, parseResult.GetValue (periodArgument), token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static bool TryParseOnOff (string? value, out bool enabled)
	{
	switch (value?.Trim ().ToLowerInvariant ())
		{
		case "on":
		case "yes":
		case "true":
			enabled = true;
			return true;
		case "off":
		case "no":
		case "false":
			enabled = false;
			return true;
		default:
			enabled = false;
			return false;
		}
	}

static Command CreatePollCommand ()
	{
	var apiArgument = new Argument<string> ("api")
		{
		Description = "API endpoint to GET (for example /api/meters/aggregates)."
		};

	var command = new Command ("poll", "GET a raw API endpoint and print the response body.");
	command.Arguments.Add (apiArgument);
	command.SetAction ((parseResult, cancellationToken) =>
		RunWithConnectionAsync (parseResult, async (powerwall, token) =>
			{
			await PowerwallActions.PollAsync (powerwall, parseResult.GetValue (apiArgument) ?? string.Empty, token).ConfigureAwait (false);
			return 0;
			}, cancellationToken));

	return command;
	}

static Command CreateConfigCommand ()
	{
	var showCommand = new Command ("show", "Show the persisted connection settings and their file location.");
	showCommand.SetAction (_ =>
		{
		var settings = SettingsStore.Load ();
		ConsoleHelpers.WriteHeading ("Saved Settings");
		ConsoleHelpers.WriteField ("File", SettingsStore.FilePath);
		ConsoleHelpers.WriteField ("Host", settings.Host);
		ConsoleHelpers.WriteField ("Password", string.IsNullOrEmpty (settings.ProtectedPassword) ? null : "(stored, encrypted)");
		ConsoleHelpers.WriteField ("Email", settings.Email);
		ConsoleHelpers.WriteField ("Timezone", settings.Timezone);
		ConsoleHelpers.WriteField ("Timeout (s)", settings.TimeoutSeconds?.ToString ());
		ConsoleHelpers.WriteField ("Cache expire (s)", settings.CacheExpireSeconds?.ToString ());
		ConsoleHelpers.WriteField ("Access token", string.IsNullOrEmpty (settings.ProtectedAccessToken) ? null : "(stored, encrypted)");
		ConsoleHelpers.WriteField ("Refresh token", string.IsNullOrEmpty (settings.ProtectedRefreshToken) ? null : "(stored, encrypted)");
		ConsoleHelpers.WriteField ("Site ID", settings.SiteId);
		ConsoleHelpers.WriteField ("Region", settings.Region);
		return 0;
		});

	var clearCommand = new Command ("clear", "Delete the persisted connection settings, including the stored password.");
	clearCommand.SetAction (_ =>
		{
		SettingsStore.Clear ();
		ConsoleHelpers.WriteSuccess ("Saved settings cleared.");
		return 0;
		});

	var command = new Command ("config", "Manage the persisted connection settings stored under %LocalAppData%.");
	command.Subcommands.Add (showCommand);
	command.Subcommands.Add (clearCommand);
	command.SetAction (_ =>
		{
		ConsoleHelpers.WriteError ("Specify a subcommand: 'config show' or 'config clear'.");
		return 1;
		});

	return command;
	}

static async Task<int> RunInteractiveAsync (ParseResult parseResult, CancellationToken cancellationToken)
	{
	var resolved = CliOptions.Resolve (parseResult);
	if (resolved.Verbose)
		VerboseLogging.Enable ();

	Powerwall powerwall;
	try
		{
		powerwall = new Powerwall (resolved.Options);
		}
	catch (Exception exc) when (exc is PowerwallInvalidConfigurationException or ArgumentException)
		{
		ConsoleHelpers.WriteError ($"Configuration error: {exc.Message}");
		return 2;
		}

	try
		{
		if (!await powerwall.ConnectAsync (cancellationToken).ConfigureAwait (false))
			{
			var hint = resolved.Options.CloudMode
				? "Failed to connect to the Tesla cloud. Check the access/refresh tokens, email, and network connectivity."
				: "Failed to connect to the Powerwall. Check the host, password, and network connectivity.";
			ConsoleHelpers.WriteError (hint);
			powerwall.Dispose ();
			return 1;
			}
		}
	catch (PowerwallCloudNoTeslaAuthFileException exc)
		{
		ConsoleHelpers.WriteError ($"Cloud authentication error: {exc.Message}");
		ConsoleHelpers.WriteError ("Supply tokens with --access-token and --refresh-token (or PW_ACCESS_TOKEN / PW_REFRESH_TOKEN).");
		powerwall.Dispose ();
		return 2;
		}
	catch (OperationCanceledException)
		{
		ConsoleHelpers.WriteError ("Operation cancelled.");
		powerwall.Dispose ();
		return 130;
		}
	catch (PowerwallException exc)
		{
		ConsoleHelpers.WriteError ($"Error: {exc.Message}");
		powerwall.Dispose ();
		return 1;
		}

	using var session = new InteractiveConnection (powerwall, resolved.Options, resolved.Region, resolved.NoSave);
	return await InteractiveSession.RunAsync (session, cancellationToken).ConfigureAwait (false);
	}

static async Task<int> RunWithConnectionAsync (
	ParseResult parseResult,
	Func<Powerwall, CancellationToken, Task<int>> action,
	CancellationToken cancellationToken)
	{
	var resolved = CliOptions.Resolve (parseResult);
	if (resolved.Verbose)
		VerboseLogging.Enable ();

	Powerwall powerwall;
	try
		{
		powerwall = new Powerwall (resolved.Options);
		}
	catch (PowerwallInvalidConfigurationException exc)
		{
		ConsoleHelpers.WriteError ($"Configuration error: {exc.Message}");
		return 2;
		}
	catch (ArgumentException exc)
		{
		ConsoleHelpers.WriteError ($"Configuration error: {exc.Message}");
		return 2;
		}

	try
		{
		if (!await powerwall.ConnectAsync (cancellationToken).ConfigureAwait (false))
			{
			var hint = resolved.Options.CloudMode
				? "Failed to connect to the Tesla cloud. Check the access/refresh tokens, email, and network connectivity."
				: "Failed to connect to the Powerwall. Check the host, password, and network connectivity.";
			ConsoleHelpers.WriteError (hint);
			return 1;
			}

		return await action (powerwall, cancellationToken).ConfigureAwait (false);
		}
	catch (PowerwallCloudNoTeslaAuthFileException exc)
		{
		ConsoleHelpers.WriteError ($"Cloud authentication error: {exc.Message}");
		ConsoleHelpers.WriteError ("Supply tokens with --access-token and --refresh-token (or PW_ACCESS_TOKEN / PW_REFRESH_TOKEN).");
		return 2;
		}
	catch (OperationCanceledException)
		{
		ConsoleHelpers.WriteError ("Operation cancelled.");
		return 130;
		}
	catch (ArgumentException exc)
		{
		ConsoleHelpers.WriteError ($"Error: {exc.Message}");
		return 2;
		}
	catch (PowerwallException exc)
		{
		ConsoleHelpers.WriteError ($"Error: {exc.Message}");
		return 1;
		}
	finally
		{
		powerwall.Dispose ();
		}
	}
