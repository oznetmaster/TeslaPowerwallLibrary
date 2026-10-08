// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Net.Sockets;
using System.Text.Json;
using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>Shared local commands for one-shot and interactive console sessions.</summary>
internal static class LocalConsoleCommands
	{
	/// <summary>Runs one action with the selected connection; tests may reject execution before connecting.</summary>
	/// <param name="result">Parsed command arguments.</param>
	/// <param name="action">Requested operation.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Process-style exit code.</returns>
	internal delegate Task<int> RunConnection (ParseResult result, Func<Powerwall, CancellationToken, Task<int>> action, CancellationToken cancellationToken);

	/// <summary>Resolves the configured LAN host without claiming it is the active socket or an alternate setup route.</summary>
	/// <param name="powerwall">Connected local facade.</param>
	/// <param name="cancellationToken">Cancels the address lookup.</param>
	/// <returns>Non-secret console connection information.</returns>
	private static async Task<object> ConnectionStatusAsync (Powerwall powerwall, CancellationToken cancellationToken)
		{
		string? host = powerwall.LocalHost;
		string[] addresses = Array.Empty<string> ();
		string? resolutionError = null;
		if (host is not null)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
			deadline.CancelAfter (TimeSpan.FromSeconds (5));
			try
				{
				var resolved = await PowerwallDiscovery.ResolveLanAsync (host, deadline.Token).ConfigureAwait (false);
				addresses = resolved.Addresses.Select (address => address.ToString ()).Distinct ().ToArray ();
				}
			catch (SocketException) { resolutionError = "Name lookup unavailable"; }
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { resolutionError = "Name lookup timed out"; }
			}
		return new { deviceDin = powerwall.LocalDeviceIdentificationNumber, configuredHost = host,
			resolvedIpAddresses = addresses, resolutionError, usingSetupNetwork = powerwall.IsUsingLocalReadFallback };
		}

	/// <summary>Builds the local command catalogue without opening a connection or loading credentials.</summary>
	/// <param name="run">Connection owner for actions requiring a device.</param>
	/// <param name="canControl">Whether the current invocation explicitly permits signed local controls.</param>
	/// <returns>Commands used by both console entry points.</returns>
	internal static IReadOnlyList<Command> Create (RunConnection run, Func<ParseResult, bool> canControl)
		{
		var commands = new List<Command> ();
		void Read<T> (string name, string description, Func<Powerwall, CancellationToken, Task<T>> read)
			{
			var command = new Command (name, description);
			command.SetAction ((result, ct) => run (result, async (pw, token) => { Write (await read (pw, token).ConfigureAwait (false)); return 0; }, ct));
			commands.Add (command);
			}
		Read ("local-telemetry", "Read current typed TEDAPI telemetry once.", (pw, ct) => pw.GetLocalTelemetryAsync (true, ct));
		Read ("local-detailed", "Read detailed controller telemetry once.", (pw, ct) => pw.GetLocalDetailedTelemetryAsync (true, ct));
		Read ("local-system", "Read hardware, firmware and update status without starting an update.", (pw, ct) => pw.GetLocalSystemInformationAsync (true, ct));
		Read ("local-meter-aggregates", "Read configured meter summaries.", (pw, ct) => pw.GetLocalMeterAggregatesAsync (true, ct));
		Read ("local-native-meters", "Read native customer meter counters.", (pw, ct) => pw.GetLocalNativeMeterAggregatesAsync (true, ct));
		Read ("local-meters", "Read configured meter channels.", (pw, ct) => pw.GetLocalMeterReadingsAsync (true, ct));
		Read ("local-diagnostics", "Read identified fans, temperatures and photovoltaic inputs.", (pw, ct) => pw.GetLocalComponentDiagnosticsAsync (true, ct));
		Read ("local-devices", "Read configured devices and their availability.", (pw, ct) => pw.GetLocalDeviceSnapshotAsync (true, ct));
		Read ("local-configuration", "Read non-secret operating configuration.", (pw, ct) => pw.GetLocalConfigurationAsync (true, ct));
		Read ("local-backup-events", "Read existing backup events without changing them.", (pw, ct) => pw.GetLocalBackupEventsAsync (ct));
		Read ("local-ieee20305", "Read IEEE 2030.5 service metadata without starting a procedure.", (pw, ct) => pw.GetLocalIeee20305Async (true, ct));
		Read ("local-inverter-tests", "Read stored inverter self-test status and results without starting a procedure.", (pw, ct) => pw.GetLocalInverterSelfTestsAsync (true, ct));
		Read ("local-protection-tests", "Read stored protection-test status and results without starting a procedure.", (pw, ct) => pw.GetLocalProtectionTestStatusAsync (true, ct));
		Read ("local-connection", "Show device identity, configured host, freshly resolved IP addresses and selected read route.", ConnectionStatusAsync);

		var device = new Argument<string?> ("device-din") { Arity = ArgumentArity.ZeroOrOne, Description = "Optional configured device DIN; omitted means the connected controller." };
		var components = new Command ("local-components", "Read components for the controller or a specified configured device.");
		components.Arguments.Add (device);
		components.SetAction ((result, ct) => run (result, async (pw, token) =>
			{
			string? din = result.GetValue (device);
			Write (din is null ? await pw.GetLocalComponentsAsync (true, token).ConfigureAwait (false)
				: await pw.GetLocalComponentsAsync (din, true, token).ConfigureAwait (false));
			return 0;
			}, ct));
		commands.Add (components);

		foreach (string name in new[] { "local-backup-start", "local-backup-cancel", "local-off-grid", "local-reconnect-grid" })
			{
			var command = new Command (name, "Explicit signed local control; requires --allow-local-control for this session.");
			var minutes = new Argument<int> ("minutes") { Description = "Maximum-backup duration, 1 through 1440 minutes." };
			if (name == "local-backup-start") command.Arguments.Add (minutes);
			command.SetAction ((result, ct) =>
				{
				if (!canControl (result)) return Denied ();
				int duration = name == "local-backup-start" ? result.GetValue (minutes) : 0;
				if (name == "local-backup-start" && (duration < 1 || duration > 1440))
					return Invalid ("Choose a backup duration between 1 and 1440 minutes.");
				return run (result, async (pw, token) =>
					{
					bool acknowledged = name switch
						{
						"local-backup-start" => (await pw.ScheduleLocalMaxBackupAsync (TimeSpan.FromMinutes (duration), token).ConfigureAwait (false)).Acknowledged,
						"local-backup-cancel" => (await pw.CancelLocalMaxBackupAsync (token).ConfigureAwait (false)).Acknowledged,
						"local-off-grid" => (await pw.GoOffGridAsync (token).ConfigureAwait (false)).Acknowledged,
						_ => (await pw.ReconnectGridAsync (token).ConfigureAwait (false)).Acknowledged
						};
					return Acknowledgement (acknowledged);
					}, ct);
				});
			commands.Add (command);
			}

		var settings = new Command ("local-settings", "Apply only the specified settings together, using the current configuration hash.");
		var reserve = new Option<double?> ("--reserve") { Description = "App-scale reserve percentage, zero through 100." };
		var mode = new Option<string?> ("--mode") { Description = "self_consumption, autonomous or backup." };
		var charging = new Option<bool?> ("--grid-charging") { Arity = ArgumentArity.ExactlyOne, Description = "true or false; omitted leaves this setting unchanged." };
		var export = new Option<string?> ("--grid-export") { Description = "battery_ok, pv_only or never." };
		settings.Options.Add (reserve); settings.Options.Add (mode); settings.Options.Add (charging); settings.Options.Add (export);
		settings.SetAction ((result, ct) =>
			{
			if (!canControl (result)) return Denied ();
			LocalSettingsUpdate update;
			try { update = Settings (result.GetValue (reserve), result.GetValue (mode), result.GetValue (charging), result.GetValue (export)); }
			catch (ArgumentException exc) { return Invalid (exc.Message); }
			return run (result, async (pw, token) => Acknowledgement ((await pw.UpdateLocalSettingsAsync (update, token).ConfigureAwait (false)).Acknowledged), ct);
			});
		commands.Add (settings);

		var discover = new Command ("local-discover", "Browse advertised local devices without logging in or selecting one.");
		discover.SetAction (async (_, ct) =>
			{
			var candidates = await PowerwallDiscovery.DiscoverAsync (cancellationToken: ct).ConfigureAwait (false);
			foreach (var candidate in candidates) Console.WriteLine ($"{candidate.Host}:{candidate.Port} - {string.Join (", ", candidate.Addresses)}");
			if (candidates.Count == 0) Console.WriteLine ("No advertised devices found. An explicit hostname or IP address can still be used.");
			return 0;
			});
		commands.Add (discover);
		return commands;
		}

	/// <summary>Validates partial settings before a connection is opened, retaining explicit zero and false.</summary>
	/// <param name="reserve">Optional app-scale reserve.</param>
	/// <param name="mode">Optional operating mode.</param>
	/// <param name="charging">Optional grid charging permission.</param>
	/// <param name="export">Optional export rule.</param>
	/// <returns>Typed settings with omitted fields left null.</returns>
	internal static LocalSettingsUpdate Settings (double? reserve, string? mode, bool? charging, string? export)
		{
		if (reserve is double value && (double.IsNaN (value) || double.IsInfinity (value) || value < 0 || value > 100)) throw new ArgumentException ("Reserve must be between zero and 100 percent.");
		if (mode is not (null or "self_consumption" or "autonomous" or "backup")) throw new ArgumentException ("Unsupported local operation mode.");
		if (export is not (null or "battery_ok" or "pv_only" or "never")) throw new ArgumentException ("Unsupported local export rule.");
		if (reserve is null && mode is null && charging is null && export is null) throw new ArgumentException ("Specify at least one setting to change.");
		return new LocalSettingsUpdate { BackupReservePercent = reserve, OperationMode = mode, GridChargingEnabled = charging, GridExport = export };
		}

	/// <summary>Dispatches the same parsed local commands against the already connected interactive session.</summary>
	/// <param name="session">Current connection owner.</param>
	/// <param name="command">Command name.</param>
	/// <param name="arguments">Optional command arguments.</param>
	/// <param name="cancellationToken">Cancels the action.</param>
	/// <returns>Whether this catalogue recognizes the command.</returns>
	internal static async Task<bool> TryInteractiveAsync (InteractiveConnection session, string command, string? arguments, CancellationToken cancellationToken)
		{
		var root = new RootCommand ();
		foreach (var item in Create ((_, action, ct) => action (session.Powerwall, ct),
			_ => !session.Options.CloudMode && !session.Options.FleetApi && session.Options.LocalProtocol == PowerwallLocalProtocol.TedapiSigned && session.Options.AllowLocalControl)) root.Subcommands.Add (item);
		if (!root.Subcommands.Any (item => item.Name == command)) return false;
		await root.Parse (command + (string.IsNullOrWhiteSpace (arguments) ? string.Empty : " " + arguments)).InvokeAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		return true;
		}

	private static void Write<T> (T value) => Console.WriteLine (SerializeForDisplay (value));

	/// <summary>Formats typed local results without exposing IEEE provisioning PINs, including nested metadata.</summary>
	/// <typeparam name="T">Reported response model.</typeparam>
	/// <param name="value">Value to display.</param>
	/// <returns>Indented diagnostic text with provisioning credentials omitted.</returns>
	internal static string SerializeForDisplay<T> (T value)
		{
		var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver ();
		resolver.Modifiers.Add (info =>
			{
			if (info.Type != typeof (LocalIeee20305Registration)) return;
			foreach (var property in info.Properties)
				if (property.Name == "pin") property.ShouldSerialize = (_, _) => false;
			});
		return JsonSerializer.Serialize (value, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = resolver });
		}
	private static Task<int> Denied () => Invalid ("Local controls require a signed LAN session started with --allow-local-control.");
	private static Task<int> Invalid (string message) { ConsoleHelpers.WriteError (message); return Task.FromResult (2); }
	private static int Acknowledgement (bool acknowledged)
		{
		Console.WriteLine (acknowledged ? "Request acknowledged. Read the reported state to confirm its effect." : "Request was not acknowledged.");
		return acknowledged ? 0 : 1;
		}
	}
