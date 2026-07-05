// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

using Newtonsoft.Json;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Renders Powerwall™ data to the console. Each method targets a connected <see cref="Powerwall"/> and is
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

	/// <summary>Prints the battery charge level, both raw and Tesla™-app scaled.</summary>
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

	/// <summary>
	/// Switches the active Tesla energy site and prints the result (cloud mode only). The supplied value may be
	/// either the Tesla energy site identifier or the human-readable site name shown by the <c>sites</c> command.
	/// </summary>
	/// <returns>The selected <see cref="CloudSite"/> when the switch succeeds; otherwise <see langword="null"/>.</returns>
	public static async Task<CloudSite?> ChangeSiteAsync (Powerwall powerwall, string siteIdOrName, CancellationToken cancellationToken)
		{
		ConsoleHelpers.WriteHeading ("Change Site");
		if (string.IsNullOrWhiteSpace (siteIdOrName))
			{
			ConsoleHelpers.WriteError ("  No site identifier or name supplied.");
			return null;
			}

		var site = await ResolveSiteAsync (powerwall, siteIdOrName, cancellationToken).ConfigureAwait (false);
		if (site is null)
			{
			ConsoleHelpers.WriteError ($"  Site '{siteIdOrName}' was not found for this account.");
			return null;
			}

		var changed = await powerwall.ChangeSiteAsync (site.SiteId, cancellationToken).ConfigureAwait (false);
		if (!changed)
			{
			ConsoleHelpers.WriteError ($"  Site '{siteIdOrName}' was not found for this account.");
			return null;
			}

		var label = string.IsNullOrWhiteSpace (site.SiteName) ? Constants.TEXT_UNKNOWN : site.SiteName!.Trim ();
		ConsoleHelpers.WriteSuccess ($"  Active site changed to {label} ({site.SiteId}).");
		return site;
		}

	/// <summary>
	/// Resolves a user-supplied value to a known <see cref="CloudSite"/>, matching the Tesla energy site
	/// identifier first and then falling back to a trimmed, case-insensitive site-name match.
	/// </summary>
	static async Task<CloudSite?> ResolveSiteAsync (Powerwall powerwall, string siteIdOrName, CancellationToken cancellationToken)
		{
		var sites = await powerwall.GetSitesAsync (cancellationToken).ConfigureAwait (false);
		var trimmed = siteIdOrName.Trim ();

		return sites.FirstOrDefault (site => string.Equals (site.SiteId, trimmed, StringComparison.Ordinal))
			?? sites.FirstOrDefault (site => string.Equals (site.SiteName?.Trim (), trimmed, StringComparison.OrdinalIgnoreCase));
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

	/// <summary>Prints device vitals as a per-device map of telemetry values.</summary>
	public static async Task VitalsAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var vitals = await powerwall.VitalsAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Device Vitals");
		if (vitals is null || vitals.Count == 0)
			{
			ConsoleHelpers.WriteError ("  No vitals available.");
			return;
			}

		foreach (var device in vitals)
			{
			ConsoleHelpers.WriteField (device.Key, $"{device.Value.Count} value(s)");
			foreach (var value in device.Value)
				ConsoleHelpers.WriteField ($"    {value.Key}", value.Value?.ToString () ?? "null");
			}
		}

	/// <summary>Prints the distinct set of active alerts reported across all devices.</summary>
	public static async Task AlertsAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var alerts = await powerwall.AlertsAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Alerts");
		if (alerts.Count == 0)
			{
			ConsoleHelpers.WriteSuccess ("  No active alerts.");
			return;
			}

		foreach (var alert in alerts)
			ConsoleHelpers.WriteField ("Alert", alert);
		}

	/// <summary>Prints raw energy history for the active site (cloud mode only).</summary>
	public static async Task HistoryAsync (Powerwall powerwall, string kind, string? period, CancellationToken cancellationToken)
		{
		var body = await powerwall.GetHistoryAsync (kind, period, cancellationToken: cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ($"Energy History ({kind}{(period is null ? string.Empty : $", {period}")})");
		WriteJsonOrStatus (body);
		}

	/// <summary>Prints raw calendar-aligned energy history for the active site (cloud mode only).</summary>
	public static async Task CalendarHistoryAsync (Powerwall powerwall, string kind, string? period, CancellationToken cancellationToken)
		{
		var body = await powerwall.GetCalendarHistoryAsync (kind, period, cancellationToken: cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ($"Calendar History ({kind}{(period is null ? string.Empty : $", {period}")})");
		WriteJsonOrStatus (body);
		}

	/// <summary>Polls an arbitrary API endpoint and prints the raw response body.</summary>
	public static async Task PollAsync (Powerwall powerwall, string api, CancellationToken cancellationToken)
		{
		var body = await powerwall.PollAsync (api, force: true, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ($"GET {api}");
		WriteJsonOrStatus (body);
		}

	// Distinguishes a failed/unavailable call (null body) from a genuinely empty payload so the
	// console never mislabels an API failure as an "(empty response)".
	private static void WriteJsonOrStatus (string? body)
		{
		if (body is null)
			{
			ConsoleHelpers.WriteError ("  No data returned (the request failed or the endpoint is unavailable). Run with --verbose for details.");
			return;
			}

		if (body.Length == 0 || string.IsNullOrWhiteSpace (body))
			{
			Console.WriteLine ("  (empty response)");
			return;
			}

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
