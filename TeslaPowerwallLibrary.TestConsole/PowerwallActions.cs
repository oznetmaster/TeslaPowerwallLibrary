// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.IO;

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

	/// <summary>Prints the customer email of the active connection.</summary>
	public static Task EmailAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		ConsoleHelpers.WriteHeading ("Email");
		ConsoleHelpers.WriteField ("Email", powerwall.Email);
		return Task.CompletedTask;
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

	/// <summary>Prints the authenticated Tesla account summary (FleetAPI mode only).</summary>
	public static async Task ProfileAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var body = await powerwall.GetProfileAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Account Profile");
		WriteJsonOrStatus (body);
		}

	/// <summary>Prints the authenticated account's region and FleetAPI base URL (FleetAPI mode only).</summary>
	public static async Task RegionAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var body = await powerwall.GetRegionAsync (cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Account Region");
		WriteJsonOrStatus (body);
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

	/// <summary>Prints whether Storm Watch is currently enabled (cloud mode only).</summary>
	public static async Task StormWatchAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		var enabled = await powerwall.GetStormWatchAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Storm Watch");
		ConsoleHelpers.WriteField ("Storm Watch", enabled switch { true => "enabled", false => "disabled", null => "n/a" });
		}

	/// <summary>Enables or disables Storm Watch and prints the result (cloud mode only).</summary>
	public static async Task SetStormWatchAsync (Powerwall powerwall, bool enabled, CancellationToken cancellationToken)
		{
		var result = await powerwall.SetStormWatchAsync (enabled, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ("Set Storm Watch");
		ConsoleHelpers.WriteSuccess ($"  Storm Watch {(enabled ? "enabled" : "disabled")}.");
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

	/// <summary>Gets the calendar-history kinds with strongly typed <see cref="Powerwall"/> convenience methods.</summary>
	public static IReadOnlyList<string> TypedHistoryKinds { get; } =
		["energy", "power", "soe", "self_consumption", "backup"];

	/// <summary>
	/// Prints strongly typed calendar-aligned history for the active site (cloud mode only) by calling the
	/// typed <see cref="Powerwall"/> convenience method for <paramref name="kind"/> (for example
	/// <see cref="Powerwall.GetEnergyCalendarHistoryAsync"/>) instead of the raw JSON API.
	/// </summary>
	public static async Task TypedHistoryAsync (Powerwall powerwall, string kind, string? period, CancellationToken cancellationToken)
		{
		ConsoleHelpers.WriteHeading ($"Typed History ({kind}{(period is null ? string.Empty : $", {period}")})");

		switch (kind)
			{
			case "energy":
				var energy = await powerwall.GetEnergyCalendarHistoryAsync (ParsePeriod (period), cancellationToken: cancellationToken).ConfigureAwait (false);
				WritePoints (energy, p => p.Timestamp, p =>
					$"solar {p.SolarKwh:N2} kWh, home {p.HomeKwh:N2} kWh, from grid {p.FromGridKwh:N2} kWh, " +
					$"to grid {p.ToGridKwh:N2} kWh, battery charge {p.BatteryChargeKwh:N2} kWh, battery discharge {p.BatteryDischargeKwh:N2} kWh");
				break;

			case "power":
				var power = await powerwall.GetPowerCalendarHistoryAsync (ParsePeriod (period), cancellationToken: cancellationToken).ConfigureAwait (false);
				WritePoints (power, p => p.Timestamp, p =>
					$"solar {ConsoleHelpers.FormatWatts (p.SolarPower)}, battery {ConsoleHelpers.FormatWatts (p.BatteryPower)}, " +
					$"grid {ConsoleHelpers.FormatWatts (p.GridPower)}, grid services {ConsoleHelpers.FormatWatts (p.GridServicesPower)}, " +
					$"generator {ConsoleHelpers.FormatWatts (p.GeneratorPower)}");
				break;

			case "soe":
				var soe = await powerwall.GetStateOfEnergyCalendarHistoryAsync (ParsePeriod (period), cancellationToken: cancellationToken).ConfigureAwait (false);
				WritePoints (soe, p => p.Timestamp, p => $"soe {p.Soe:N1} %");
				break;

			case "self_consumption":
				var selfConsumption = await powerwall.GetSelfConsumptionCalendarHistoryAsync (ParsePeriod (period), cancellationToken: cancellationToken).ConfigureAwait (false);
				WritePoints (selfConsumption, p => p.Timestamp, p => $"solar {p.SolarPercentage:N1} %, battery {p.BatteryPercentage:N1} %");
				break;

			case "backup":
				var backup = await powerwall.GetBackupCalendarHistoryAsync (ParsePeriod (period), cancellationToken: cancellationToken).ConfigureAwait (false);
				ConsoleHelpers.WriteField ("Events", backup.EventsCount.ToString (CultureInfo.InvariantCulture));
				ConsoleHelpers.WriteField ("Total events", backup.TotalEvents.ToString (CultureInfo.InvariantCulture));
				ConsoleHelpers.WriteField ("Next start date", backup.NextStartDate?.ToString ("O", CultureInfo.InvariantCulture));
				ConsoleHelpers.WriteField ("Next end date", backup.NextEndDate?.ToString ("O", CultureInfo.InvariantCulture));
				if (backup.Events.Count == 0)
					{
					Console.WriteLine ("  (no events)");
					break;
					}

				foreach (var evt in backup.Events)
					ConsoleHelpers.WriteField ("Event", string.Join (", ", evt.Select (kv => $"{kv.Key}={kv.Value}")));
				break;

			default:
				throw new ArgumentException ($"Invalid typed history kind '{kind}'. Allowed values: {string.Join (", ", TypedHistoryKinds)}.", nameof (kind));
			}
		}

	/// <summary>Parses a CLI period string into the <see cref="HistoryPeriod"/> required by the typed calendar-history methods.</summary>
	private static HistoryPeriod ParsePeriod (string? period) =>
		period switch
			{
			null => HistoryPeriod.Day,
			"day" => HistoryPeriod.Day,
			"week" => HistoryPeriod.Week,
			"month" => HistoryPeriod.Month,
			"year" => HistoryPeriod.Year,
			"lifetime" => HistoryPeriod.Lifetime,
			_ => throw new ArgumentException ($"Invalid history period '{period}'. Allowed values: {string.Join (", ", Powerwall.HistoryPeriods)}.", nameof (period))
			};

	private static void WritePoints<T> (IReadOnlyList<T> points, Func<T, DateTimeOffset> getTimestamp, Func<T, string> formatValues)
		{
		if (points.Count == 0)
			{
			Console.WriteLine ("  (no points)");
			return;
			}

		foreach (var point in points)
			ConsoleHelpers.WriteField (getTimestamp (point).ToString ("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), formatValues (point));
		}

	/// <summary>Polls an arbitrary API endpoint and prints the raw response body.</summary>
	public static async Task PollAsync (Powerwall powerwall, string api, CancellationToken cancellationToken)
		{
		var body = await powerwall.PollAsync (api, force: true, cancellationToken).ConfigureAwait (false);
		ConsoleHelpers.WriteHeading ($"GET {api}");
		WriteJsonOrStatus (body);
		}

	/// <summary>
	/// Fetches raw calendar-aligned energy history for every kind in <see cref="Powerwall.CalendarHistoryKinds"/>
	/// (cloud mode only) and writes each response body to its own <c>calendar_history_{kind}.json</c> file under
	/// <paramref name="outputDirectory"/>. Intended as a one-off developer/diagnostic tool for capturing real
	/// payload samples used to design strongly typed history models; failures for one kind do not abort the rest.
	/// </summary>
	/// <param name="powerwall">The connected Powerwall instance (cloud mode).</param>
	/// <param name="outputDirectory">Directory to write the captured JSON files to; created if missing.</param>
	/// <param name="period">The aggregation period to request for every kind, or <see langword="null"/> for the default.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	public static async Task CaptureCalendarHistoryAsync (Powerwall powerwall, string outputDirectory, string? period, CancellationToken cancellationToken)
		{
		ConsoleHelpers.WriteHeading ("Capture Calendar History");
		Directory.CreateDirectory (outputDirectory);

		foreach (var kind in Powerwall.CalendarHistoryKinds)
			{
			try
				{
				var body = await powerwall.GetCalendarHistoryAsync (kind, period, cancellationToken: cancellationToken).ConfigureAwait (false);
				var path = Path.Combine (outputDirectory, $"calendar_history_{kind}.json");

				if (body is null)
					{
					ConsoleHelpers.WriteError ($"  {kind,-20} no data returned; skipped.");
					continue;
					}

				File.WriteAllText (path, Prettify (body));
				ConsoleHelpers.WriteSuccess ($"  {kind,-20} -> {path} ({body.Length} bytes)");
				}
			catch (PowerwallException exc)
				{
				ConsoleHelpers.WriteError ($"  {kind,-20} failed: {exc.Message}");
				}
			}
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
