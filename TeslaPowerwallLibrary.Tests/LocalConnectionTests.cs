// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using Google.Protobuf;
using Classic = TeslaPowerwallLibrary.Tedapi.Protocol.Classic;
using System.Net;
using System.Net.Http;
using System.Text;

using TeslaPowerwallLibrary.Local;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class LocalConnectionTests
	{
	[TestCase ("192.0.2.1", "192.0.2.1", 443)]
	[TestCase ("powerwall.local", "powerwall.local", 443)]
	[TestCase ("powerwall.local:8443", "powerwall.local", 8443)]
	[TestCase ("[2001:db8::1]:8443", "2001:db8::1", 8443)]
	[TestCase ("2001:db8::1", "2001:db8::1", 443)]
	public void LocalAuthority_AcceptsHostnamesAndIpLiterals (string host, string expected, int port)
		{
		Uri endpoint = LocalEndpoint.Create (host);
		Assert.That (endpoint.DnsSafeHost.Trim ('[', ']'), Is.EqualTo (expected));
		Assert.That (endpoint.Port, Is.EqualTo (port));
		using var powerwall = new Powerwall (new PowerwallOptions { Host = host });
		Assert.That (powerwall.Mode, Is.EqualTo (PowerwallMode.Local));
		}

	[TestCase ("https://powerwall.local")]
	[TestCase ("user@powerwall.local")]
	[TestCase ("powerwall.local/api/status")]
	[TestCase ("powerwall.local#fragment")]
	[TestCase ("powerwall.local?query")]
	[TestCase ("powerwall.local:0")]
	[TestCase ("powerwall.local:65536")]
	[TestCase (" ")]
	public void InvalidAuthority_IsRejectedBeforeAnyNetworkCall (string host)
		{
		Assert.That (() => LocalEndpoint.Create (host), Throws.ArgumentException);
		}

	[Test]
	public async Task ResolveLiteral_DoesNotRequireDns ()
		{
		PowerwallHost host = await PowerwallDiscovery.ResolveAsync ("192.0.2.1:8443");
		Assert.That (host.Host, Is.EqualTo ("192.0.2.1"));
		Assert.That (host.Port, Is.EqualTo (8443));
		Assert.That (host.Addresses.Single (), Is.EqualTo (IPAddress.Parse ("192.0.2.1")));
		}

	[Test]
	public void CanceledDiscovery_DoesNotStartNetworkRequests ()
		{
		using var cancellation = new CancellationTokenSource ();
		cancellation.Cancel ();
		Assert.That (async () => await PowerwallDiscovery.DiscoverAsync (cancellationToken: cancellation.Token),
			Throws.InstanceOf<OperationCanceledException> ());
		}

	[Test]
	public void MdnsQuestion_RequestsUnicastResponse ()
		{
		byte[] query = MdnsPacket.Query ("_https._tcp.local", 12);
		Assert.That (query.Skip (query.Length - 4), Is.EqualTo (new byte[] { 0, 12, 128, 1 }));
		Assert.That (MdnsPacket.Read (query), Is.Empty);
		}

	[Test]
	public void MdnsResponse_ReadsCompressedAddressRecord ()
		{
		byte[] query = MdnsPacket.Query ("powerwall.local", 1);
		query[2] = 0x84;
		query[7] = 1;
		byte[] record = { 0xc0, 12, 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4, 192, 0, 2, 7 };
		MdnsRecord result = MdnsPacket.Read (query.Concat (record).ToArray ()).Single ();
		Assert.That (result.Name, Is.EqualTo ("powerwall.local"));
		Assert.That (result.Address, Is.EqualTo (IPAddress.Parse ("192.0.2.7")));
		}

	[Test]
	public void MdnsResponse_RejectsCompressionLoop ()
		{
		byte[] packet = { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xc0, 12 };
		Assert.Throws<InvalidDataException> (() => MdnsPacket.Read (packet));
		}

	[TestCase (0)]
	[TestCase (1)]
	[TestCase (11)]
	public void MdnsResponse_RejectsTruncation (int size)
		{
		Assert.Throws<InvalidDataException> (() => MdnsPacket.Read (new byte[size]));
		}

	[Test]
	public async Task LocalLogin_UsesConfiguredHostname_AndHasNoCloudDependency ()
		{
		var requests = new List<string> ();
		using var handler = new Handler (async request =>
			{
			requests.Add (request.RequestUri!.AbsolutePath);
			Assert.That (request.RequestUri.Host, Is.EqualTo ("powerwall.test"));
			if (request.RequestUri.AbsolutePath == "/api/login/Basic")
				{
				var payload = await request.Content!.ReadAsStringAsync ();
				Assert.That (payload, Does.Contain ("force_sm_off").And.Contain ("false"));
				return Response ("{\"token\":\"synthetic-token\"}");
				}
			Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo ("synthetic-token"));
			return Response ("{\"percentage\":50}");
			});
		using var client = new PowerwallLocalClient (new PowerwallOptions
			{
			Host = "powerwall.test", NoLocalSessionPersistence = true, Password = "synthetic", AuthMode = "token",
			CacheFile = Path.Combine (Path.GetTempPath (), Guid.NewGuid () + ".json")
			}, handler);
		await client.AuthenticateAsync ();
		Assert.That (await client.PollAsync ("/api/system_status/soe"), Does.Contain ("50"));
		Assert.That (requests, Is.EqualTo (new[] { "/api/login/Basic", "/api/system_status/soe" }));
		}

	[Test]
	public async Task MissingEndpointCooldown_IsHonoredEvenWithForce ()
		{
		var calls = 0;
		using var handler = new Handler (request =>
			{
			calls++;
			return Task.FromResult (request.RequestUri!.AbsolutePath == "/api/login/Basic"
				? Response ("{\"token\":\"synthetic\"}") : new HttpResponseMessage (HttpStatusCode.NotFound));
			});
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", NoLocalSessionPersistence = true, AuthMode = "token" }, handler);
		await client.AuthenticateAsync ();
		Assert.That (await client.PollAsync ("/api/not-supported"), Is.Null);
		Assert.That (await client.PollAsync ("/api/not-supported", force: true), Is.Null);
		Assert.That (calls, Is.EqualTo (2));
		}

	[Test]
	public void LocalControl_ExplicitReadOnlyRejectsBeforeAuthentication ()
		{
		using var handler = new Handler (_ => throw new AssertionException ("Unexpected network call"));
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", AllowLocalControl = false, NoLocalSessionPersistence = true }, handler);
		Assert.ThrowsAsync<PowerwallNotSupportedException> (async () => await client.PostAsync ("/api/operation", new { }));
		}

	[Test]
	public void MissingLocalToken_DoesNotReportSuccessfulLogin ()
		{
		using var handler = new Handler (_ => Task.FromResult (Response ("{}")));
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", NoLocalSessionPersistence = true, AuthMode = "token" }, handler);
		Assert.ThrowsAsync<LoginException> (async () => await client.AuthenticateAsync ());
		}

	[Test]
	public async Task Reauthentication_ReusesTransport_AndDiscardsPreviousReadings ()
		{
		int logins = 0, reads = 0;
		using var handler = new Handler (request =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
				return Task.FromResult (Response ($"{{\"token\":\"token-{++logins}\"}}"));
			reads++;
			Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo ($"token-{logins}"));
			return Task.FromResult (Response ($"{{\"percentage\":{logins * 10}}}"));
			});
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", NoLocalSessionPersistence = true, AuthMode = "token" }, handler);
		await client.AuthenticateAsync ();
		Assert.That (await client.PollAsync ("/api/system_status/soe"), Does.Contain ("10"));
		await client.AuthenticateAsync ();
		Assert.That (await client.PollAsync ("/api/system_status/soe"), Does.Contain ("20"));
		Assert.That (logins, Is.EqualTo (2));
		Assert.That (reads, Is.EqualTo (2));
		}

	[Test]
	public async Task FailedReauthentication_CannotServeCachedAuthorizedData ()
		{
		int logins = 0, reads = 0;
		using var handler = new Handler (request =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
				return Task.FromResult (++logins == 1 ? Response ("""{"token":"old"}""") : Response ("{}"));
			reads++;
			return Task.FromResult (Response ("""{"percentage":50}"""));
			});
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", NoLocalSessionPersistence = true, AuthMode = "token" }, handler);
		await client.AuthenticateAsync ();
		await client.PollAsync ("/api/system_status/soe");
		Assert.ThrowsAsync<LoginException> (async () => await client.AuthenticateAsync ());
		Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.PollAsync ("/api/system_status/soe"));
		Assert.That (reads, Is.EqualTo (1));
		}

	[Test]
	public async Task Logout_InvalidatesCachedMeasurements ()
		{
		using var handler = new Handler (request => Task.FromResult (Response (
			request.RequestUri!.AbsolutePath == "/api/login/Basic" ? """{"token":"old"}""" : """{"percentage":50}""")));
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", NoLocalSessionPersistence = true, AuthMode = "token" }, handler);
		await client.AuthenticateAsync ();
		await client.PollAsync ("/api/system_status/soe");
		await client.CloseSessionAsync ();
		Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.PollAsync ("/api/system_status/soe"));
		}

	[Test]
	public async Task ClassicVitals_PreservesProtobufValuePresenceAndIntegerPrecision ()
		{
		var device = new Classic.SiteControllerConnectedDeviceWithVitals
			{
			Device = new Classic.SiteControllerConnectedDevice { Device = new Classic.Device
				{ Din = new Classic.StringValue { Value = "TEPOD--part--serial" } } }
			};
		device.Vitals.Add (new Classic.DeviceVital { Name = "integer", IntValue = 9007199254740993L });
		device.Vitals.Add (new Classic.DeviceVital { Name = "zero", FloatValue = 0 });
		device.Vitals.Add (new Classic.DeviceVital { Name = "false", BoolValue = false });
		device.Vitals.Add (new Classic.DeviceVital { Name = "missing" });
		device.Alerts.Add ("example-alert");
		var payload = new Classic.DevicesWithVitals ();
		payload.Devices.Add (device);
		using var handler = new Handler (request => Task.FromResult (request.RequestUri!.AbsolutePath == "/api/login/Basic"
			? Response ("""{"token":"synthetic"}""")
			: new HttpResponseMessage (HttpStatusCode.OK) { Content = new ByteArrayContent (payload.ToByteArray ()) }));
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", NoLocalSessionPersistence = true, AuthMode = "token" }, handler);
		await client.AuthenticateAsync ();
		var values = (await client.VitalsAsync ())!["TEPOD--part--serial"];
		Assert.That (values["integer"], Is.TypeOf<long> ().And.EqualTo (9007199254740993L));
		Assert.That (values["zero"], Is.TypeOf<double> ().And.EqualTo (0));
		Assert.That (values["false"], Is.False);
		Assert.That (values["missing"], Is.Null);
		Assert.That (values["firmwareVersion"], Is.Null);
		Assert.That (values["alerts"], Is.EqualTo (new[] { "example-alert" }));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ClassicVitals_RejectsMalformedOrOversizedResponse (bool oversized)
		{
		using var handler = new Handler (request =>
			{
			if (request.RequestUri!.AbsolutePath == "/api/login/Basic")
				return Task.FromResult (Response ("""{"token":"synthetic"}"""));
			var content = new ByteArrayContent (new byte[] { 255 });
			if (oversized)
				content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
			return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = content });
			});
		using var client = new PowerwallLocalClient (new PowerwallOptions { Host = "powerwall.test", NoLocalSessionPersistence = true, AuthMode = "token" }, handler);
		await client.AuthenticateAsync ();
		Assert.ThrowsAsync<PowerwallConnectionException> (async () => await client.VitalsAsync ());
		}

	private static HttpResponseMessage Response (string json) => new (HttpStatusCode.OK) { Content = new StringContent (json) };

	private sealed class Handler (Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
		{
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken) => callback (request);
		}
	}
