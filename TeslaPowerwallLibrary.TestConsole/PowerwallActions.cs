// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

using Newtonsoft.Json;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Renders Powerwall data to the console. Each method targets a connected <see cref="Powerwall"/> and is
/// shared between the one-shot command-line subcommands and the interactive REPL.
/// </summary>
internal static class PowerwallActions
	{
	/// <summary>Prints the gateway status, firmware version, DIN, and uptime.</summary>
	public static async Task StatusAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var status = await powerwall.StatusAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Gateway Status");
		if (status is null)
			{
			ConsoleHelpers.WriteError ("  No status available.");
			return;
			}

		ConsoleHelpers.WriteField ("DIN", status.Din);
		ConsoleHelpers.WriteField ("Version", status.Version);
		ConsoleHelpers.WriteField ("Git hash", status.GitHash);
		ConsoleHelpers.WriteField ("Uptime", status.UpTimeSeconds);
		ConsoleHelpers.WriteField ("Start time", status.StartTime);
		}

	/// <summary>Prints the configured site name.</summary>
	public static async Task SiteNameAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var name = await powerwall.SiteNameAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Site Name");
		ConsoleHelpers.WriteField ("Name", name);
		}

	/// <summary>Prints the battery charge level, both raw and Tesla-app scaled.</summary>
	public static async Task LevelAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var raw = await powerwall.LevelAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		var scaled = await powerwall.LevelAsync (scale: true, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Battery Level");
		ConsoleHelpers.WriteField ("Level (raw)", ConsoleHelpers.FormatPercent (raw));
		ConsoleHelpers.WriteField ("Level (scaled)", ConsoleHelpers.FormatPercent (scaled));
		}

	/// <summary>Prints the four instantaneous power flows.</summary>
	public static async Task PowerAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var power = await powerwall.PowerAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Power Flows");
		ConsoleHelpers.WriteField ("Site (grid)", ConsoleHelpers.FormatWatts (power.Site));
		ConsoleHelpers.WriteField ("Solar", ConsoleHelpers.FormatWatts (power.Solar));
		ConsoleHelpers.WriteField ("Battery", ConsoleHelpers.FormatWatts (power.Battery));
		ConsoleHelpers.WriteField ("Load (home)", ConsoleHelpers.FormatWatts (power.Load));
		}

	/// <summary>Prints the normalized grid status.</summary>
	public static async Task GridAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var grid = await powerwall.GridStatusAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Grid Status");
		ConsoleHelpers.WriteField ("Status", grid?.ToString () ?? "n/a");
		}

	/// <summary>Prints the current operation mode and backup reserve level.</summary>
	public static async Task OperationAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var mode = await powerwall.GetModeAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		var reserveRaw = await powerwall.GetReserveAsync (scale: false, cancellationToken: cancellationToken).ConfigureAwait (false);
		var reserveScaled = await powerwall.GetReserveAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Operation");
		ConsoleHelpers.WriteField ("Mode", mode);
		ConsoleHelpers.WriteField ("Reserve (raw)", ConsoleHelpers.FormatPercent (reserveRaw));
		ConsoleHelpers.WriteField ("Reserve (scaled)", ConsoleHelpers.FormatPercent (reserveScaled));
		}

	/// <summary>Prints the estimated backup time remaining on the battery.</summary>
	public static async Task TimeRemainingAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var hours = await powerwall.GetTimeRemainingAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Time Remaining");
		ConsoleHelpers.WriteField ("Backup time", ConsoleHelpers.FormatHours (hours));
		}

	/// <summary>Prints the full system status with per-battery block detail.</summary>
	public static async Task SystemStatusAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var status = await powerwall.SystemStatusAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("System Status");
		if (status is null)
			{
			ConsoleHelpers.WriteError ("  No system status available.");
			return;
			}

		ConsoleHelpers.WriteField ("Full pack energy", ConsoleHelpers.FormatWattHours (status.NominalFullPackEnergy));
		ConsoleHelpers.WriteField ("Energy remaining", ConsoleHelpers.FormatWattHours (status.NominalEnergyRemaining));
		ConsoleHelpers.WriteField ("Available blocks", status.AvailableBlocks?.ToString (CultureInfo.InvariantCulture));
		ConsoleHelpers.WriteField ("Island state", status.SystemIslandState);

		if (status.BatteryBlocks is { Count: > 0 } blocks)
			{
			foreach (var block in blocks)
				{
				ConsoleHelpers.WriteField (
					$"  {block.PackageSerialNumber ?? Constants.TEXT_UNKNOWN}",
					$"{ConsoleHelpers.FormatWattHours (block.NominalEnergyRemaining)} / {block.PinvState ?? "n/a"}");
				}
			}
		}

	/// <summary>Prints a consolidated dashboard combining the most useful readings.</summary>
	public static async Task SummaryAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		await StatusAsync (powerwall, cancellationToken).ConfigureAwait (false);
		await SiteNameAsync (powerwall, cancellationToken).ConfigureAwait (false);
		await LevelAsync (powerwall, cancellationToken).ConfigureAwait (false);
		await PowerAsync (powerwall, cancellationToken).ConfigureAwait (false);
		await GridAsync (powerwall, cancellationToken).ConfigureAwait (false);
		await OperationAsync (powerwall, cancellationToken).ConfigureAwait (false);
		await TimeRemainingAsync (powerwall, cancellationToken).ConfigureAwait (false);
		}

	/// <summary>Sets the backup reserve level and prints the result.</summary>
	public static async Task SetReserveAsync (Powerwall powerwall, double level, CancellationToken cancellationToken)
		{
		var result = await powerwall.SetReserveAsync (level, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Set Reserve");
		ConsoleHelpers.WriteSuccess ($"  Requested reserve level: {level.ToString ("N1", CultureInfo.InvariantCulture)} %");
		ConsoleHelpers.WriteField ("Response", result);
		}

	/// <summary>Sets the operation mode and prints the result.</summary>
	public static async Task SetModeAsync (Powerwall powerwall, string mode, CancellationToken cancellationToken)
		{
		var result = await powerwall.SetModeAsync (mode, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Set Mode");
		ConsoleHelpers.WriteSuccess ($"  Requested mode: {mode}");
		ConsoleHelpers.WriteField ("Response", result);
		}

	/// <summary>Lists the Tesla energy sites available to the account (cloud mode only).</summary>
	public static async Task SitesAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var sites = await powerwall.GetSitesAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Energy Sites");
		if (sites.Count == 0)
			{
			ConsoleHelpers.WriteError ("  No sites available.");
			return;
			}

		foreach (var site in sites)
			{
			var label = string.IsNullOrWhiteSpace (site.SiteName) ? Constants.TEXT_UNKNOWN : site.SiteName!;
			ConsoleHelpers.WriteField (site.SiteId, $"{label} ({site.ResourceType ?? "unknown"})");
			}
		}

	/// <summary>Switches the active Tesla energy site and prints the result (cloud mode only).</summary>
	public static async Task ChangeSiteAsync (Powerwall powerwall, string siteId, CancellationToken cancellationToken)
		{
		var changed = await powerwall.ChangeSiteAsync (siteId, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Change Site");
		if (changed)
			ConsoleHelpers.WriteSuccess ($"  Active site changed to {siteId}.");
		else
			ConsoleHelpers.WriteError ($"  Site {siteId} was not found for this account.");
		}

	/// <summary>Prints the current grid charging and grid export settings (cloud mode only).</summary>
	public static async Task GridConfigAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var charging = await powerwall.GetGridChargingAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		var export = await powerwall.GetGridExportAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Grid Configuration");
		ConsoleHelpers.WriteField ("Grid charging", charging switch { true => "enabled", false => "disabled", null => "n/a" });
		ConsoleHelpers.WriteField ("Grid export", export ?? "n/a");
		}

	/// <summary>Enables or disables grid charging and prints the result (cloud mode only).</summary>
	public static async Task SetGridChargingAsync (Powerwall powerwall, bool enabled, CancellationToken cancellationToken)
		{
		var result = await powerwall.SetGridChargingAsync (enabled, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Set Grid Charging");
		ConsoleHelpers.WriteSuccess ($"  Grid charging {(enabled ? "enabled" : "disabled")}.");
		ConsoleHelpers.WriteField ("Response", result);
		}

	/// <summary>Sets the grid export rule and prints the result (cloud mode only).</summary>
	public static async Task SetGridExportAsync (Powerwall powerwall, string mode, CancellationToken cancellationToken)
		{
		var result = await powerwall.SetGridExportAsync (mode, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Set Grid Export");
		ConsoleHelpers.WriteSuccess ($"  Requested export rule: {mode}");
		ConsoleHelpers.WriteField ("Response", result);
		}

	/// <summary>Polls an arbitrary API endpoint and prints the raw response body.</summary>
	public static async Task PollAsync (Powerwall powerwall, string api, CancellationToken cancellationToken)
		{
		var body = await powerwall.PollAsync (api, force: true, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ($"GET {api}");
		Console.WriteLine (Prettify (body));
		}

	private static string Prettify (string? json)
		{
		if (string.IsNullOrWhiteSpace (json))
			return "  (empty response)";

		try
			{
			var parsed = JsonConvert.DeserializeObject (json!);
			return parsed is null ? json! : JsonConvert.SerializeObject (parsed, Formatting.Indented);
			}
		catch (JsonException)
			{
			return json!;
			}
		}
	}
