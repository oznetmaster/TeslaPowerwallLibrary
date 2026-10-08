// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.CommandLine;
using NUnit.Framework;
using TeslaPowerwallLibrary.TestConsole;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.TestConsole.Tests;

/// <summary>Exercises the actual shared console parser without accessing accounts or hardware.</summary>
[TestFixture]
public sealed class LocalConsoleTests
	{
	/// <summary>Console diagnostics omit provisioning credentials even inside the full controller response.</summary>
	[Test]
	public void DiagnosticOutput_OmitsProvisioningPinAndRetainsZero ()
		{
		var value = System.Text.Json.JsonSerializer.Deserialize<LocalTelemetry> ("""{"ieee20305":{"registration":{"dateTimeRegistered":0,"pin":"secret-test-pin"}}}""")!;
		string output = LocalConsoleCommands.SerializeForDisplay (value);
		Assert.That (output, Does.Not.Contain ("secret-test-pin").And.Not.Contain ("\"pin\""));
		using var parsed = System.Text.Json.JsonDocument.Parse (output);
		Assert.That (parsed.RootElement.GetProperty ("ieee20305").GetProperty ("registration").GetProperty ("dateTimeRegistered").GetInt32 (), Is.Zero);
		}

	/// <summary>Missing session permission rejects every local control before connection or action execution.</summary>
	[TestCase ("local-backup-start 5")]
	[TestCase ("local-backup-cancel")]
	[TestCase ("local-off-grid")]
	[TestCase ("local-reconnect-grid")]
	[TestCase ("local-settings --reserve 0")]
	public async Task Controls_RequireExplicitSessionPermission (string invocation)
		{
		int connections = 0;
		var root = Root (false, () => connections++);
		Assert.That (await root.Parse (invocation).InvokeAsync (), Is.EqualTo (2));
		Assert.That (connections, Is.Zero);
		}

	/// <summary>Invalid input cannot open a connection even with control permission.</summary>
	[TestCase ("local-backup-start 0")]
	[TestCase ("local-backup-start 1441")]
	[TestCase ("local-backup-start nonsense")]
	[TestCase ("local-backup-start")]
	[TestCase ("local-settings")]
	[TestCase ("local-settings --reserve -1")]
	[TestCase ("local-settings --reserve 101")]
	[TestCase ("local-settings --reserve NaN")]
	[TestCase ("local-settings --mode wrong")]
	[TestCase ("local-settings --grid-export wrong")]
	[TestCase ("local-settings --grid-charging maybe")]
	[TestCase ("local-settings --grid-charging")]
	[TestCase ("local-components first second")]
	public async Task InvalidArguments_DoNotConnect (string invocation)
		{
		int connections = 0;
		Assert.That (await Root (true, () => connections++).Parse (invocation).InvokeAsync (), Is.Not.Zero);
		Assert.That (connections, Is.Zero);
		}

	/// <summary>All local read commands reach the shared connection owner without executing a device call in this fixture.</summary>
	[TestCase ("local-telemetry")]
	[TestCase ("local-detailed")]
	[TestCase ("local-ieee20305")]
	[TestCase ("local-inverter-tests")]
	[TestCase ("local-protection-tests")]
	[TestCase ("local-system")]
	[TestCase ("local-meter-aggregates")]
	[TestCase ("local-native-meters")]
	[TestCase ("local-meters")]
	[TestCase ("local-diagnostics")]
	[TestCase ("local-devices")]
	[TestCase ("local-configuration")]
	[TestCase ("local-backup-events")]
	[TestCase ("local-connection")]
	[TestCase ("local-components")]
	[TestCase ("local-components part--follower")]
	public async Task ReadCommands_UseSharedCatalogue (string invocation)
		{
		int connections = 0;
		Assert.That (await Root (false, () => connections++).Parse (invocation).InvokeAsync (), Is.EqualTo (73));
		Assert.That (connections, Is.EqualTo (1));
		}

	/// <summary>Help remains available without login or control permission.</summary>
	[TestCase ("local-settings --help")]
	[TestCase ("local-components --help")]
	[TestCase ("local-off-grid --help")]
	public async Task Help_DoesNotConnect (string invocation)
		{
		int connections = 0;
		Assert.That (await Root (false, () => connections++).Parse (invocation).InvokeAsync (), Is.Zero);
		Assert.That (connections, Is.Zero);
		}

	/// <summary>Explicit zero and false are retained while omitted update fields remain null.</summary>
	[Test]
	public void Settings_KeepZeroFalseAndOmittedFields ()
		{
		var settings = LocalConsoleCommands.Settings (0, null, false, null);
		Assert.That (settings.BackupReservePercent, Is.Zero);
		Assert.That (settings.GridChargingEnabled, Is.False);
		Assert.That (settings.OperationMode, Is.Null);
		Assert.That (settings.GridExport, Is.Null);
		}

	/// <summary>Valid partial settings and duration boundaries are accepted; the fake owner never executes controls.</summary>
	[TestCase ("local-settings --reserve 0 --grid-charging false")]
	[TestCase ("local-settings --mode self_consumption --grid-export pv_only")]
	[TestCase ("local-backup-start 1")]
	[TestCase ("local-backup-start 1440")]
	public async Task ValidControls_ReachOwnerOnce (string invocation)
		{
		int connections = 0;
		Assert.That (await Root (true, () => connections++).Parse (invocation).InvokeAsync (), Is.EqualTo (73));
		Assert.That (connections, Is.EqualTo (1));
		}

	/// <summary>The alternate gets only local read credentials; cloud settings and control permission cannot leak into it.</summary>
	[Test]
	public void SetupRoute_IsExplicitAndReadOnly ()
		{
		var primary = new PowerwallOptions { LocalProtocol = PowerwallLocalProtocol.TedapiSigned, Host = "lan.test",
			Password = "customer-test", AccessToken = "cloud-test", AllowLocalControl = true,
			LocalQueryVersion = TedapiQueryVersion.June2026, CacheExpireSeconds = 17 };
		Assert.That (LocalConsoleResources.SetupOptions (primary, null, "ignored", false, 60), Is.Null);
		var alternate = LocalConsoleResources.SetupOptions (primary, " setup.test ", "label-test", true, 45)!;
		Assert.That (alternate.Host, Is.EqualTo ("setup.test"));
		Assert.That (alternate.Password, Is.EqualTo ("customer-test"));
		Assert.That (alternate.GatewayPassword, Is.EqualTo ("label-test"));
		Assert.That (alternate.LocalProtocol, Is.EqualTo (PowerwallLocalProtocol.Tedapi));
		Assert.That (alternate.LocalQueryVersion, Is.EqualTo (TedapiQueryVersion.June2026));
		Assert.That (alternate.CacheExpireSeconds, Is.EqualTo (17));
		Assert.That (alternate.AllowLocalControl, Is.False);
		Assert.That (alternate.EnableLocalReadFailover, Is.False);
		Assert.That (alternate.LocalFollowerConnection, Is.Null);
		Assert.That (alternate.NoLocalSessionPersistence, Is.True);
		Assert.That (alternate.AccessToken, Is.Null);
		Assert.That (alternate.CloudMode, Is.False);
		}

	/// <summary>Invalid or incomplete alternate configuration fails before opening any key or connection.</summary>
	[TestCase (PowerwallLocalProtocol.Gateway, "setup.test", "test", false, 60, false)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, null, "test", true, 60, false)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, "setup.test", null, false, 60, false)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, "setup.test", "test", true, 0, false)]
	[TestCase (PowerwallLocalProtocol.TedapiSigned, "setup.test", "test", false, 60, true)]
	public void InvalidSetupRoute_IsRejected (PowerwallLocalProtocol protocol, string? host, string? password, bool failover, int retry, bool cloud)
		{
		Assert.Throws<ArgumentException> (() => LocalConsoleResources.SetupOptions (
			new PowerwallOptions { LocalProtocol = protocol, CloudMode = cloud }, host, password, failover, retry));
		}

	private static RootCommand Root (bool canControl, Action connected)
		{
		var root = new RootCommand ();
		foreach (var item in LocalConsoleCommands.Create ((_, _, _) => { connected (); return Task.FromResult (73); }, _ => canControl)) root.Subcommands.Add (item);
		return root;
		}
	}
