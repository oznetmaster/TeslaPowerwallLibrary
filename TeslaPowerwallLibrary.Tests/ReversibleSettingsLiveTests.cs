// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Net.Http;

using Google.Protobuf;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Explicit reversible setting checks; never switches the grid or schedules backup events.</summary>
[TestFixture, Category ("Live"), NonParallelizable]
public sealed class ReversibleSettingsLiveTests
	{
	private bool _restorationFailed;

	/// <summary>Changes one approved setting, reads it back, and restores the independently recorded original.</summary>
	/// <param name="setting">The single setting authorized for this invocation.</param>
	[TestCase ("reserve"), TestCase ("mode"), TestCase ("stormwatch")]
	[Explicit ("Changes live settings. Requires per-setting operator authorization and private credentials.")]
	public async Task ChangeConfirmAndRestore (string setting)
		{
		if (_restorationFailed) Assert.Fail ("A preceding restoration failed. No further setting may change.");
		if (Environment.GetEnvironmentVariable ("TESLA_APPROVED_SETTING_TEST") != setting)
			{ Assert.Ignore ("Explicit approval for this single setting is required."); return; }
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ()) { Assert.Ignore ("Windows DPAPI and CNG are required."); return; }
#endif
		string host = Required ("TESLA_LOCAL_TEST_HOST");
		string expectedDin = Required ("TESLA_LOCAL_TEST_DIN");
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (Required ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE")), null, DataProtectionScope.CurrentUser);
		string password;
		try { password = Encoding.UTF8.GetString (clear); }
		finally { Array.Clear (clear, 0, clear.Length); }
		using var stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		var options = new PowerwallOptions
			{
			Host = host, Password = password.Substring (password.Length - 5), LocalSigningKey = key,
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned, AllowLocalControl = setting != "stormwatch",
			NoLocalSessionPersistence = true, Timeout = TimeSpan.FromSeconds (10), CacheExpireSeconds = 0
			};
		using var local = new Powerwall (options);
		using var probe = new RejectionProbe ();
		var transport = new PowerwallTedapiClient (options, probe);
		OperationRegressionTests.SetField (local, "_client", transport);
		await transport.AuthenticateAsync ();
		var initial = await local.GetLocalConfigurationAsync (force: true);
		Assert.That (initial.Din, Is.EqualTo (expectedDin), "The exact approved device must match before a write.");
		var grid = (await local.GetLocalTelemetryAsync (force: true)).Control?.Islanding;
		Assert.That (grid?.GridOk, Is.True, "Mains must be healthy before a reversible control test.");
		Assert.That (grid?.ContactorClosed, Is.True, "The system must already be grid connected.");

		if (setting == "reserve")
			{
			double? original = await local.GetReserveAsync (force: true);
			Assert.That (original, Is.Not.Null.And.InRange (0, 100));
			double target = original!.Value >= 1 ? original.Value - 1 : original.Value + 1;
			await Exercise (setting, original.Value, target,
				async () => await local.GetReserveAsync (force: true) ?? throw new InvalidDataException ("Reserve missing."),
				async value => { await local.UpdateLocalSettingsAsync (new LocalSettingsUpdate { BackupReservePercent = value }); },
				(a, b) => Math.Abs (a - b) < 0.001);
			}
		else if (setting == "mode")
			{
			string? original = initial.OperationMode;
			Assert.That (original, Is.AnyOf ("self_consumption", "autonomous", "backup"));
			string target = original == "self_consumption" ? "autonomous" : "self_consumption";
			await Exercise (setting, original!, target,
				async () => await local.GetModeAsync (force: true) ?? throw new InvalidDataException ("Mode missing."),
				async value => { await local.UpdateLocalSettingsAsync (new LocalSettingsUpdate { OperationMode = value }); },
				(a, b) => a == b);
			}
		else
			{
			string site = Required ("TESLA_LOCAL_TEST_CLOUD_SITE");
			using var cloud = new Powerwall (new PowerwallOptions
				{
				CloudMode = true, Email = Required ("TESLA_LOCAL_TEST_OWNER_EMAIL"), SiteId = site,
				AuthPath = Required ("TESLA_LOCAL_TEST_OWNER_CACHE"), Timeout = TimeSpan.FromSeconds (15), CacheExpireSeconds = 0
				});
			Assert.That (await cloud.ConnectAsync (), Is.True);
			Assert.That (cloud.CloudSiteId, Is.EqualTo (site));
			bool? original = await cloud.GetStormWatchAsync (force: true);
			Assert.That (original, Is.Not.Null, "Storm Watch must be readable before changing it.");
			await Exercise (setting, original!.Value, !original.Value,
				async () => await cloud.GetStormWatchAsync (force: true) ?? throw new InvalidDataException ("Storm Watch missing."),
				async value => { await cloud.SetStormWatchAsync (value); }, (a, b) => a == b);
			}

		var final = await local.GetLocalConfigurationAsync (force: true);
		Assert.Multiple (() =>
			{
			Assert.That (final.OperationMode, Is.EqualTo (initial.OperationMode));
			Assert.That (final.Site?.BackupReservePercent, Is.EqualTo (initial.Site?.BackupReservePercent).Within (0.001));
			Assert.That (final.Site?.GridChargingDisallowed, Is.EqualTo (initial.Site?.GridChargingDisallowed));
			Assert.That (final.Site?.GridExport, Is.EqualTo (initial.Site?.GridExport));
			});
		}

	private Task Exercise<T> (string setting, T original, T target, Func<Task<T>> read, Func<T, Task> write, Func<T, T, bool> equal) =>
		SettingRoundTrip.ExerciseAsync (setting, original, target, read, write, equal, () => _restorationFailed = true);

	/// <summary>Captures only the rejected command's RPC diagnostic, never the configuration or credentials.</summary>
	private sealed class RejectionProbe : DelegatingHandler
		{
		private string? _lastConfiguration;
		/// <summary>Creates the same local TLS transport used by the read-only hardware probe.</summary>
		internal RejectionProbe () : base (new HttpClientHandler
			{ UseProxy = false, AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { }
		/// <inheritdoc/>
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			if (request.RequestUri!.AbsolutePath == "/tedapi/v1r")
				{
				var outgoing = Signed.RoutableMessage.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
				var update = Signed.MessageEnvelope.Parser.ParseFrom (outgoing.ProtobufMessageAsBytes).Filestore?.UpdateFileRequest;
				if (update is not null)
					{
					using var before = JsonDocument.Parse (_lastConfiguration ?? throw new InvalidDataException ("No original configuration captured."));
					using var after = JsonDocument.Parse (update.File.Blob.ToStringUtf8 ());
					var changes = new List<string> ();
					Compare (before.RootElement, after.RootElement, "config", changes);
					TestContext.Out.WriteLine ("Configuration changed paths: " + string.Join (", ", changes));
					string allowed = Environment.GetEnvironmentVariable ("TESLA_APPROVED_SETTING_TEST") == "reserve"
						? "config.site_info.backup_reserve_percent" : "config.default_real_mode";
					if (changes.Any (path => path != allowed)) throw new InvalidDataException ("Unexpected configuration changes; write blocked.");
					if (Environment.GetEnvironmentVariable ("TESLA_LOCAL_DIAGNOSTIC_DRYRUN") == "1")
						throw new InvalidDataException ("Diagnostic dry run: write blocked before transmission.");
					}
				}
			var response = await base.SendAsync (request, cancellationToken);
			if (request.RequestUri!.AbsolutePath == "/tedapi/v1r" && response.IsSuccessStatusCode)
				{
				var routed = Signed.RoutableMessage.Parser.ParseFrom (await response.Content.ReadAsByteArrayAsync ());
				var envelope = Signed.MessageEnvelope.Parser.ParseFrom (routed.ProtobufMessageAsBytes);
				if (envelope.Filestore?.ReadFileResponse?.File?.Blob is ByteString blob && !blob.IsEmpty)
					_lastConfiguration = blob.ToStringUtf8 ();
				var error = envelope.Common?.ErrorResponse;
				string? path = Environment.GetEnvironmentVariable ("TESLA_PRIVATE_REJECTION_LOG");
				if (error is not null && !string.IsNullOrWhiteSpace (path))
					File.WriteAllText (path, error.Status?.Message ?? "No diagnostic message");
				}
			return response;
			}
		private static void Compare (JsonElement a, JsonElement b, string path, List<string> changes)
			{
			if (a.ValueKind != b.ValueKind) { changes.Add (path); return; }
			if (a.ValueKind == JsonValueKind.Object)
				{
				var left = a.EnumerateObject ().ToDictionary (p => p.Name, p => p.Value);
				var right = b.EnumerateObject ().ToDictionary (p => p.Name, p => p.Value);
				foreach (string name in left.Keys.Union (right.Keys))
					if (!left.ContainsKey (name) || !right.ContainsKey (name)) changes.Add (path + "." + name);
					else Compare (left[name], right[name], path + "." + name, changes);
				}
			else if (a.ValueKind == JsonValueKind.Array)
				{
				if (a.GetArrayLength () != b.GetArrayLength ()) { changes.Add (path); return; }
				for (int i = 0; i < a.GetArrayLength (); i++) Compare (a[i], b[i], path + "[" + i + "]", changes);
				}
			else if (a.ValueKind == JsonValueKind.String ? a.GetString () != b.GetString () : a.GetRawText () != b.GetRawText ())
				changes.Add (path);
			}
		}

	private static string Required (string name) => Environment.GetEnvironmentVariable (name) is string value && !string.IsNullOrWhiteSpace (value)
		? value : throw new InvalidDataException ("Required private test setting missing: " + name);
	}

/// <summary>Ensures a restoration attempt also runs after a lost write acknowledgement or failed confirmation.</summary>
internal static class SettingRoundTrip
	{
	/// <summary>Records a live setting trial and its restoration without adding generated helpers to the test fixture.</summary>
	/// <typeparam name="T">Typed setting value.</typeparam>
	/// <param name="setting">Authorized setting name.</param>
	/// <param name="original">Value recorded before the trial.</param>
	/// <param name="target">Temporary value.</param>
	/// <param name="read">Uncached setting read.</param>
	/// <param name="write">Single setting update.</param>
	/// <param name="equal">Value comparison.</param>
	/// <param name="failed">Blocks later trials if restoration fails.</param>
	/// <returns>The trial and restoration completion.</returns>
	internal static async Task ExerciseAsync<T> (string setting, T original, T target, Func<Task<T>> read, Func<T, Task> write, Func<T, T, bool> equal, Action failed)
		{
		string journal = Path.Combine (TestContext.CurrentContext.WorkDirectory, "setting-restore-" + setting + "-" + DateTime.UtcNow.ToString ("yyyyMMddHHmmss") + ".json");
		File.WriteAllText (journal, JsonSerializer.Serialize (new { Setting = setting, Original = original, Target = target, Restored = false }));
		TestContext.Out.WriteLine ($"{setting}: original={original}; temporary={target}; UTC={DateTime.UtcNow:O}");
		try
			{
			await SettingRoundTrip.RunAsync (original, target, read, write, equal,
				async (expected, verify) =>
					{
					for (int attempt = 0; attempt < 12; attempt++)
						{
						if (equal (await verify (), expected)) return;
						await Task.Delay (2000);
						}
					throw new InvalidDataException ("The requested setting was not confirmed by a fresh read.");
					},
				() =>
					{
					File.WriteAllText (journal, JsonSerializer.Serialize (new { Setting = setting, Original = original, Target = target, Restored = true }));
					TestContext.Out.WriteLine ($"{setting}: original value restored and confirmed; UTC={DateTime.UtcNow:O}");
					}, failed);
			}
		finally { TestContext.AddTestAttachment (journal, "Non-secret setting restoration record"); }
		}

	/// <summary>Writes once, confirms, then independently restores and confirms the initial setting.</summary>
	/// <typeparam name="T">Typed setting value.</typeparam>
	/// <param name="original">Value read before testing.</param>
	/// <param name="target">Temporary value.</param>
	/// <param name="read">Uncached setting read.</param>
	/// <param name="write">Single setting update; no automatic replay.</param>
	/// <param name="equal">Comparison preserving the setting's precision.</param>
	/// <param name="confirm">Bounded fresh-read confirmation.</param>
	/// <param name="restored">Called only after restoration is independently confirmed.</param>
	/// <param name="failed">Stops subsequent tests if restoration cannot be confirmed.</param>
	internal static async Task RunAsync<T> (T original, T target, Func<Task<T>> read, Func<T, Task> write,
		Func<T, T, bool> equal, Func<T, Func<Task<T>>, Task> confirm, Action restored, Action failed)
		{
		try
			{
			await write (target);
			await confirm (target, read);
			TestContext.Out.WriteLine ("Temporary setting confirmed by a fresh read.");
			}
		finally
			{
			try
				{
				// A failed request may have applied. Never use its cancellation token for recovery.
				bool alreadyRestored = false;
				try { alreadyRestored = equal (await read (), original); }
				catch (Exception) { /* Still attempt the known original value after a failed read. */ }
				if (!alreadyRestored)
					{
					try { await write (original); }
					catch (Exception) { /* A lost response must be followed by an independent read. */ }
					}
				await confirm (original, read);
				restored ();
				}
			catch (Exception)
				{
				failed ();
				throw;
				}
			}
		}
	}
