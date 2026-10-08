// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;
using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Explicit, bounded local hardware checks. No test sends a power-control command.</summary>
[TestFixture]
[Category ("Live")]
[NonParallelizable]
public sealed class LocalReadOnlyLiveTests
	{
	/// <summary>Reads the core Powerwall 3 telemetry through customer authentication on the local device.</summary>
	[Test]
	[Explicit ("Requires the operator's local host and private DPAPI credential file.")]
	public async Task ReadOnlyGatewayTelemetry ()
		{
		var host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		var credentials = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE");
		if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (credentials))
			{
			Assert.Ignore ("Set the local test hostname and private encrypted label-password file.");
			return;
			}
		#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("This live fixture uses Windows DPAPI credentials.");
			return;
			}
#endif
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (credentials), null, DataProtectionScope.CurrentUser);
		var labelPassword = Encoding.UTF8.GetString (clear);
		Array.Clear (clear, 0, clear.Length);
		Assert.That (labelPassword.Length, Is.GreaterThanOrEqualTo (5));
		// The upstream local customer login uses the final five gateway-label password characters.
		using var powerwall = new Powerwall (new PowerwallOptions
			{
			Host = host,
			Password = labelPassword.Substring (labelPassword.Length - 5),
			AuthMode = "token",
			NoLocalSessionPersistence = true,
			Timeout = TimeSpan.FromSeconds (8)
			});
		Assert.That (await powerwall.ConnectAsync (), Is.True, "The local gateway did not accept customer authentication.");
		double? level = await powerwall.LevelAsync ();
		Assert.That (level, Is.Not.Null.And.InRange (0, 100));
		TestContext.Out.WriteLine ("Sample captured UTC: " + DateTimeOffset.UtcNow.ToString ("O"));
		var metersJson = await powerwall.PollAsync ("/api/meters/aggregates", force: true);
		Models.MeterAggregates? meters = JsonHelper.DeserializeOrNull<Models.MeterAggregates> (metersJson);
		TestContext.Out.WriteLine ("Typed meter readings: " + JsonSerializer.Serialize (meters));
		Models.GatewayStatus? status = await powerwall.StatusAsync ();
		TestContext.Out.WriteLine ("Gateway identity: " + status?.Din + "; firmware: " + status?.Version);
		Models.PowerReadings power = await powerwall.GetPowerReadingsAsync ();
		GridStatus? grid = await powerwall.GridStatusAsync ();
		TestContext.Out.WriteLine ("Local battery percentage: " + level);
		TestContext.Out.WriteLine ("Local power (watts): " + JsonSerializer.Serialize (power));
		TestContext.Out.WriteLine ("Local grid state: " + grid);
		}

	/// <summary>Checks richer direct TEDAPI access without registering keys or changing any device setting.</summary>
	[Test]
	[Explicit ("Requires local access and the operator's private encrypted label-password file.")]
	public async Task ReadOnlyTedapiTelemetry ()
		{
		var host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		var credentials = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE");
		if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (credentials))
			{
			Assert.Ignore ("Set local hostname and encrypted label-password file.");
			return;
			}
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("This live fixture uses Windows DPAPI credentials.");
			return;
			}
#endif
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (credentials), null, DataProtectionScope.CurrentUser);
		var password = Encoding.UTF8.GetString (clear);
		Array.Clear (clear, 0, clear.Length);
		using var client = new PowerwallTedapiClient (password, 30, TimeSpan.FromSeconds (8), host);
		await client.AuthenticateAsync ();
		LocalTelemetry telemetry = await client.GetTelemetryAsync ();
		Assert.That (telemetry.Control?.SystemStatus?.FullCapacityWattHours, Is.GreaterThan (0));
		TestContext.Out.WriteLine ("Direct TEDAPI remaining Wh: " + telemetry.Control!.SystemStatus!.RemainingWattHours);
		}
	/// <summary>Reads signed LAN telemetry using the prepared key; all local power controls remain disabled.</summary>
	[Test, Explicit ("Requires completed physical key verification and private local test settings.")]
	public Task ReadOnlySignedTedapiTelemetry () => ReadSignedTelemetryAsync (TedapiQueryVersion.June2024);

	/// <summary>Checks the optional June 2026 signed queries without changing the default or any power setting.</summary>
	[Test, Explicit ("Requires the verified local key and a gateway supporting the June 2026 query set.")]
	public Task ReadOnlySignedJune2026Telemetry () => ReadSignedTelemetryAsync (TedapiQueryVersion.June2026);

	/// <summary>Exercises public typed diagnostic reads on the verified LAN connection without starting any procedure.</summary>
	[Test, Explicit ("Requires the verified local key and private DPAPI settings; sends read-only queries only.")]
	public async Task ReadOnlyTypedExtendedDiagnostics ()
		{
		string? host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		string? credentials = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE");
		if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (credentials))
			{ Assert.Ignore ("Set the local hostname and protected label-password file."); return; }
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ()) { Assert.Ignore ("This fixture uses Windows DPAPI and CNG."); return; }
#endif
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (credentials), null, DataProtectionScope.CurrentUser);
		string password;
		try { password = Encoding.UTF8.GetString (clear); }
		finally { Array.Clear (clear, 0, clear.Length); }
		Assert.That (password.Length, Is.GreaterThanOrEqualTo (5));
		using CngKey stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (60));
		using var powerwall = new Powerwall (new PowerwallOptions
			{
			Host = host, Password = password.Substring (password.Length - 5),
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned, LocalSigningKey = key,
			AllowLocalControl = false, NoLocalSessionPersistence = true, Timeout = TimeSpan.FromSeconds (10)
			});
		Assert.That (await powerwall.ConnectAsync (deadline.Token), Is.True);
		var ieee = await powerwall.GetLocalIeee20305Async (cancellationToken: deadline.Token);
		var inverter = await powerwall.GetLocalInverterSelfTestsAsync (cancellationToken: deadline.Token);
		var protection = await powerwall.GetLocalProtectionTestStatusAsync (cancellationToken: deadline.Token);
		TestContext.Out.WriteLine ("Typed IEEE service section reported: " + (ieee is not null));
		TestContext.Out.WriteLine ("Typed inverter-test section reported: " + (inverter is not null));
		TestContext.Out.WriteLine ("Typed protection-test section reported: " + (protection is not null) + "; result count: " + protection?.Results?.Count);
		Assert.That (ieee is not null || inverter is not null || protection is not null, Is.True, "All optional diagnostic sections were absent; hardware model coverage cannot be confirmed.");
		}

	private static async Task ReadSignedTelemetryAsync (TedapiQueryVersion queryVersion)
		{
		string? host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		string? credentials = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE");
		if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (credentials))
			{
			Assert.Ignore ("Set the local hostname and protected label-password file.");
			return;
			}
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("This fixture uses Windows DPAPI and CNG.");
			return;
			}
#endif
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (credentials), null, DataProtectionScope.CurrentUser);
		string password;
		try { password = Encoding.UTF8.GetString (clear); }
		finally { Array.Clear (clear, 0, clear.Length); }
		Assert.That (password.Length, Is.GreaterThanOrEqualTo (5));
		using CngKey stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (60));
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = host,
			Password = password.Substring (password.Length - 5),
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned,
			LocalSigningKey = key,
			LocalQueryVersion = queryVersion,
			AllowLocalControl = false,
			NoLocalSessionPersistence = true,
			Timeout = TimeSpan.FromSeconds (10)
			});
		await client.AuthenticateAsync (deadline.Token);
		LocalTelemetry telemetry = await client.GetTelemetryAsync (force: true, deadline.Token);
		Assert.That (telemetry.Control?.SystemStatus?.FullCapacityWattHours, Is.GreaterThan (0));
		TestContext.Progress.WriteLine ("Signed LAN telemetry accepted. Device time: " + telemetry.System?.Time);
		TestContext.Out.WriteLine ("Local power readings: " + JsonSerializer.Serialize (telemetry.Control?.MeterAggregates));
		LocalTelemetry detailed = await client.GetDetailedTelemetryAsync (force: true, deadline.Token);
		TestContext.Out.WriteLine ("Detailed controller time: " + detailed.System?.Time
			+ "; Neurio meters: " + detailed.Neurio?.Readings?.Count
			+ "; remote meters: " + detailed.RemoteMeters?.Meters?.Count);
		LocalComponentTelemetry components = await client.GetComponentsAsync (force: true, deadline.Token);
		TestContext.Out.WriteLine ("Reported component families: " + string.Join (", ", components.Components?.Keys ?? Array.Empty<string> ()));
		LocalConfiguration configuration = await client.GetConfigurationAsync (force: true, deadline.Token);
		TestContext.Out.WriteLine ("Typed non-secret local settings: " + JsonSerializer.Serialize (configuration));
		TestContext.Out.WriteLine ("Configured meter projection count: " + LocalMeterProjection.Create (configuration, detailed).Count);
		var batteries = LocalBatteryProjection.Create (configuration, configuration.Din!, components);
		TestContext.Out.WriteLine ("Typed local battery summaries: " + JsonSerializer.Serialize (batteries));
		TestContext.Out.WriteLine ("Firmware update state: " + JsonSerializer.Serialize (detailed.Powerwall3Bus));
		LocalDeviceSnapshot snapshot = await client.GetDeviceSnapshotAsync (cancellationToken: deadline.Token);
		Assert.That (snapshot.Configuration.Din, Is.EqualTo (configuration.Din));
		Assert.That (snapshot.Devices.Any (device => device.Din == client.DeviceIdentificationNumber
			&& device.Telemetry is not null), Is.True, "The authenticated Powerwall must have its own component readings.");
		Assert.That (snapshot.Batteries, Is.Not.Null.And.Not.Empty);
		var diagnostics = LocalDiagnosticsProjection.Create (snapshot);
		Assert.That (diagnostics.Any (component => component.DeviceDin == client.DeviceIdentificationNumber && component.SolarStrings.Count > 0), Is.True);
		TestContext.Out.WriteLine ("Component diagnostics: " + JsonSerializer.Serialize (diagnostics));
		TestContext.Out.WriteLine ("Collected device results: " + snapshot.Devices.Count
			+ "; available: " + snapshot.Devices.Count (device => device.Telemetry is not null));
		var aggregates = await client.GetDetailedMeterAggregatesAsync (cancellationToken: deadline.Token);
		TestContext.Out.WriteLine ("Detailed normalized meter summaries: " + JsonSerializer.Serialize (aggregates));
		var information = await client.GetSystemInformationAsync (force: true, deadline.Token);
		Assert.That (information.Din, Is.EqualTo (client.DeviceIdentificationNumber));
		Assert.That (information.Version?.Version, Is.Not.Null.And.Not.Empty);
		TestContext.Out.WriteLine ("System information firmware: " + information.Version!.Version);
		var native = await client.GetNativeMeterAggregatesAsync (force: true, deadline.Token);
		Assert.That (native.Site, Is.Not.Null);
		TestContext.Out.WriteLine ("Native meter counters: " + JsonSerializer.Serialize (native));
		}

	/// <summary>Inspects only site identity field names and values from a read-only local configuration response.</summary>
	[Test, Explicit ("Read-only identity investigation using the existing verified key; does not change settings.")]
	public async Task ReadOnlyLocalSiteIdentity ()
		{
		string? host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		string? credentials = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE");
		if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (credentials))
			{ Assert.Ignore ("Set private local test settings."); return; }
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{ Assert.Ignore ("This fixture uses Windows DPAPI and CNG."); return; }
#endif
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (credentials), null, DataProtectionScope.CurrentUser);
		string password;
		try { password = Encoding.UTF8.GetString (clear); }
		finally { Array.Clear (clear, 0, clear.Length); }
		using var stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		using var handler = new SiteIdentityProbeHandler ();
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = host, Password = password.Substring (password.Length - 5), LocalSigningKey = key,
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned, AllowLocalControl = false, Timeout = TimeSpan.FromSeconds (10)
			}, handler);
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		await client.AuthenticateAsync (deadline.Token);
		await client.GetConfigurationAsync (force: true, deadline.Token);
		Assert.That (handler.Inspected, Is.True);
		}

	/// <summary>Inspects controller field names and JSON kinds without logging any field values.</summary>
	[TestCase (TedapiQueryVersion.June2024), TestCase (TedapiQueryVersion.June2026)]
	[Explicit ("Read-only schema investigation using the existing verified key; does not change settings.")]
	public async Task ReadOnlyControllerFieldTypes (TedapiQueryVersion version)
		{
		string? host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		string? credentials = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE");
		if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (credentials))
			{ Assert.Ignore ("Set private local test settings."); return; }
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{ Assert.Ignore ("This fixture uses Windows DPAPI and CNG."); return; }
#endif
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (credentials), null, DataProtectionScope.CurrentUser);
		string password;
		try { password = Encoding.UTF8.GetString (clear); }
		finally { Array.Clear (clear, 0, clear.Length); }
		using var stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		using var handler = new SiteIdentityProbeHandler (inspectQuery: true, version: version);
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = host, Password = password.Substring (password.Length - 5), LocalSigningKey = key,
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned, LocalQueryVersion = version, AllowLocalControl = false, Timeout = TimeSpan.FromSeconds (10)
			}, handler);
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		await client.AuthenticateAsync (deadline.Token);
		await client.GetDetailedTelemetryAsync (force: true, deadline.Token);
		Assert.That (handler.Inspected, Is.True);
		}

	/// <summary>Reads supplemental vendor-signed diagnostic status without starting any diagnostic procedure.</summary>
	/// <param name="queryName">One of the three explicitly listed read-only vendor query names.</param>
	[TestCase ("IEEE20305Query"), TestCase ("PinvSelfTestQuery"), TestCase ("ProtectionTripTestQuery")]
	[Explicit ("Read-only schema investigation using the existing verified key; does not change settings.")]
	public async Task ReadOnlySupplementalQueryFieldTypes (string queryName)
		{
		const TedapiQueryVersion version = TedapiQueryVersion.June2026;
		string? reference = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_QUERY_REFERENCE");
		if (string.IsNullOrWhiteSpace (reference)) { Assert.Ignore ("Set the pinned vendor-query reference file."); return; }
		string? host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		string? credentials = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE");
		if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (credentials))
			{ Assert.Ignore ("Set private local test settings."); return; }
#if !NETFRAMEWORK
		if (!OperatingSystem.IsWindows ())
			{ Assert.Ignore ("This fixture uses Windows DPAPI and CNG."); return; }
#endif
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (credentials), null, DataProtectionScope.CurrentUser);
		string password;
		try { password = Encoding.UTF8.GetString (clear); }
		finally { Array.Clear (clear, 0, clear.Length); }
		using var stored = CngKey.Open ("TeslaPowerwallLibrary.LocalDevelopment.Powerwall3", CngProvider.MicrosoftSoftwareKeyStorageProvider);
		using RSA key = new RSACng (stored);
		using var handler = new SiteIdentityProbeHandler (inspectQuery: true, version: version);
		using var client = new PowerwallTedapiClient (new PowerwallOptions
			{
			Host = host, Password = password.Substring (password.Length - 5), LocalSigningKey = key,
			LocalProtocol = PowerwallLocalProtocol.TedapiSigned, LocalQueryVersion = version, AllowLocalControl = false, Timeout = TimeSpan.FromSeconds (10)
			}, handler);
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		await client.AuthenticateAsync (deadline.Token);
		using var definitions = JsonDocument.Parse (File.ReadAllText (reference));
		var definition = definitions.RootElement.GetProperty (queryName);
		Assert.That (definition.GetProperty ("text").GetString (), Does.StartWith ("query " + queryName + "{"));
		byte[] Decode (string value) => Enumerable.Range (0, value.Length / 2).Select (i => Convert.ToByte (value.Substring (i * 2, 2), 16)).ToArray ();
		var envelope = new Tedapi.Protocol.Graphql.Envelope
			{
			DeliveryChannel = 1, Sender = new Tedapi.Protocol.Graphql.Participant { Local = 1 },
			Recipient = new Tedapi.Protocol.Graphql.Participant { Din = client.DeviceIdentificationNumber },
			Graphql = new Tedapi.Protocol.Graphql.GraphQLMessages
				{ QueryRequest = new Tedapi.Protocol.Graphql.QueryRequest
					{
					Format = 2, Query = Google.Protobuf.ByteString.CopyFrom (Decode (definition.GetProperty ("signed_bytes").GetString ()!)),
					Signature = Google.Protobuf.ByteString.CopyFrom (Decode (definition.GetProperty ("code").GetString ()!)),
					VariablesJson = new Tedapi.Protocol.Graphql.StringValue { Value = "{}" }
					} }
			};
		var exchange = typeof (PowerwallTedapiClient).GetMethod ("ExchangeEnvelopeAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		byte[] bytes = await (Task<byte[]>)exchange.Invoke (client, new object?[] { Google.Protobuf.MessageExtensions.ToByteArray (envelope), deadline.Token, true, null })!;
		var answer = Tedapi.Protocol.Graphql.Envelope.Parser.ParseFrom (bytes).Graphql?.QueryResponse;
		Assert.That (answer?.Status, Is.EqualTo (1));
		Assert.That (answer!.Errors, Is.Empty);
		Assert.That (handler.Inspected, Is.True);
		}

	private sealed class SiteIdentityProbeHandler : DelegatingHandler
		{
		private readonly bool _inspectQuery;
		private readonly TedapiQueryVersion _version;
		/// <summary>Gets whether a configuration payload was inspected without storing it.</summary>
		internal bool Inspected { get; private set; }
		/// <summary>Creates a read-only response inspector for the explicitly selected local endpoint.</summary>
		/// <param name="inspectQuery">Whether to inspect only query field types instead of site identity.</param>
		/// <param name="version">Query envelope version to inspect.</param>
		internal SiteIdentityProbeHandler (bool inspectQuery = false, TedapiQueryVersion version = TedapiQueryVersion.June2024) : base (new HttpClientHandler
			{ UseProxy = false, AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { _inspectQuery = inspectQuery; _version = version; }
		/// <inheritdoc/>
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			var response = await base.SendAsync (request, cancellationToken).ConfigureAwait (false);
			if (request.RequestUri!.AbsolutePath != "/tedapi/v1r" || !response.IsSuccessStatusCode)
				return response;
			byte[] bytes = await response.Content.ReadAsByteArrayAsync ().ConfigureAwait (false);
			var routed = Signed.RoutableMessage.Parser.ParseFrom (bytes);
			var envelope = Signed.MessageEnvelope.Parser.ParseFrom (routed.ProtobufMessageAsBytes);
			if (_inspectQuery)
				{
				var query = _version == TedapiQueryVersion.June2024
					? Tedapi.Protocol.Legacy.MessageEnvelope.Parser.ParseFrom (routed.ProtobufMessageAsBytes).Payload?.Recv?.Text
					: Tedapi.Protocol.Graphql.Envelope.Parser.ParseFrom (routed.ProtobufMessageAsBytes).Graphql?.QueryResponse?.Data;
				if (string.IsNullOrWhiteSpace (query)) return response;
				using var fields = JsonDocument.Parse (query!);
				InspectTypes (fields.RootElement, "controller");
				Inspected = true;
				return response;
				}
			var blob = envelope.Filestore?.ReadFileResponse?.File?.Blob;
			if (blob is null || blob.IsEmpty) return response;
			using var document = JsonDocument.Parse (blob.ToStringUtf8 ());
			Inspected = true;
			TestContext.Out.WriteLine ("Configuration top-level field names: " + string.Join (", ", document.RootElement.EnumerateObject ().Select (p => p.Name)));
			Inspect (document.RootElement, "config");
			return response;
			}
		private static void InspectTypes (JsonElement value, string path)
			{
			TestContext.Out.WriteLine (path + " : " + value.ValueKind);
			if (value.ValueKind == JsonValueKind.Object)
				foreach (var property in value.EnumerateObject ()) InspectTypes (property.Value, path + "." + property.Name);
			else if (value.ValueKind == JsonValueKind.Array)
				{
				int index = 0;
				foreach (var item in value.EnumerateArray ()) InspectTypes (item, path + "[" + index++ + "]");
				}
			}

		private static void Inspect (JsonElement value, string path)
			{
			if (value.ValueKind == JsonValueKind.Object)
				foreach (var property in value.EnumerateObject ())
					{
					string next = path + "." + property.Name;
					if (property.Name.Equals ("site_info", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
						TestContext.Out.WriteLine ("Site field names: " + string.Join (", ", property.Value.EnumerateObject ().Select (p => p.Name)));
					if (property.Name is "site_id" or "energy_site_id" or "siteId" or "energySiteId" or "site_name")
						TestContext.Out.WriteLine (next + " = " + property.Value.ToString ());
					else Inspect (property.Value, next);
					}
			else if (value.ValueKind == JsonValueKind.Array)
				foreach (var item in value.EnumerateArray ()) Inspect (item, path + "[]");
			}
		}

	/// <summary>Browses advertised services and resolves the configured host without authentication.</summary>
	[Test]
	[Explicit ("Requires the operator's local network and configured host.")]
	public async Task DiscoverAndResolve ()
		{
		var host = Environment.GetEnvironmentVariable ("TESLA_LOCAL_TEST_HOST");
		if (string.IsNullOrWhiteSpace (host))
			{
			Assert.Ignore ("Set the local test hostname.");
			return;
			}
		PowerwallHost resolved = await PowerwallDiscovery.ResolveAsync (host);
		Assert.That (resolved.Addresses, Is.Not.Empty);
		TestContext.Out.WriteLine ("Resolved address families: " + string.Join (", ", resolved.Addresses.Select (static address => address.AddressFamily)));
		IReadOnlyList<PowerwallHost> discovered = await PowerwallDiscovery.DiscoverAsync ();
		TestContext.Out.WriteLine ("Advertised Powerwall candidates: " + discovered.Count);
		foreach (PowerwallHost candidate in discovered)
			{
			TestContext.Out.WriteLine (candidate.Host + ":" + candidate.Port);
			}
		}
	}
