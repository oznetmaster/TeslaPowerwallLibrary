// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.FleetApi;
using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
[NonParallelizable]
public sealed class LocalKeyEnrollmentTests
	{
	private static RSA _key = null!;

	[OneTimeSetUp]
	public static void CreateKey ()
		{
#if NETFRAMEWORK
		_key = new RSACng (4096);
#else
		_key = RSA.Create (4096);
#endif
		}

	[OneTimeTearDown]
	public static void DisposeKey () => _key.Dispose ();

	[TestCase (false)]
	[TestCase (true)]
	public async Task Enrollment_UsesSelectedCloudSiteAndOnlyPublicKey (bool fleet)
		{
		using var rig = new EnrollmentRig (fleet);
		rig.Handler.Response = Reply ("AddAuthorizedClientResponse", new { client = new { state = 1 } });
		LocalKeyRegistration result = await rig.Powerwall.RegisterLocalKeyAsync (_key, "Offline test client");
		Assert.That (result.State, Is.EqualTo (LocalKeyState.PendingVerification));
		Assert.That (result.Fingerprint, Has.Length.EqualTo (64));
		Assert.That (rig.Handler.Requests, Has.Count.EqualTo (1));
		var captured = rig.Handler.Requests[0];
		Assert.That (captured.Uri.AbsolutePath, Is.EqualTo ("/api/1/energy_sites/123/command"));
		Assert.That (captured.Uri.Host, Is.EqualTo (fleet ? "fleet-api.prd.na.vn.cloud.tesla.com" : "owner-api.teslamotors.com"));
		Assert.That (captured.Token, Is.EqualTo ("Bearer synthetic-access"));
		using JsonDocument json = JsonDocument.Parse (captured.Body);
		JsonElement root = json.RootElement;
		Assert.That (root.GetProperty ("command_type").GetString (), Is.EqualTo ("grpc_command"));
		JsonElement properties = root.GetProperty ("command_properties");
		Assert.That (properties.GetProperty ("identifier_type").GetInt32 (), Is.EqualTo (1));
		JsonElement authorization = properties.GetProperty ("message").GetProperty ("authorization");
		Assert.That (authorization.EnumerateObject ().Count (), Is.EqualTo (1));
		JsonElement add = authorization.GetProperty ("add_authorized_client_request");
		Assert.That (add.EnumerateObject ().Count (), Is.EqualTo (4));
		Assert.That (add.GetProperty ("key_type").GetInt32 (), Is.EqualTo (1));
		Assert.That (add.GetProperty ("authorized_client_type").GetInt32 (), Is.EqualTo (1));
		Assert.That (add.GetProperty ("description").GetString (), Is.EqualTo ("Offline test client"));
		Assert.That (Convert.FromBase64String (add.GetProperty ("public_key").GetString ()!), Is.EqualTo (TedapiSigning.PublicKeyDer (_key)));
		Assert.That (_key.ExportParameters (false).Modulus, Is.Not.Empty, "The caller still owns the key.");
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task StatusRead_DoesNotEnrollAndSelectsOnlyTheRequestedKey (bool fleet)
		{
		using var rig = new EnrollmentRig (fleet);
		string publicKey = Convert.ToBase64String (TedapiSigning.PublicKeyDer (_key));
		rig.Handler.Response = Reply ("list_authorized_clients_response", new
			{
			clients = new[]
				{
				new { public_key = "AQID", state = 3 },
				new { public_key = publicKey, state = 2 }
				}
			});
		LocalKeyRegistration result = await rig.Powerwall.GetLocalKeyStatusAsync (_key);
		Assert.That (result.State, Is.EqualTo (LocalKeyState.VerificationTimedOut));
		using JsonDocument json = JsonDocument.Parse (rig.Handler.Requests.Single ().Body);
		JsonElement authorization = json.RootElement.GetProperty ("command_properties").GetProperty ("message").GetProperty ("authorization");
		Assert.That (authorization.EnumerateObject ().Select (p => p.Name), Is.EqualTo (new[] { "list_authorized_clients_request" }));
		Assert.That (authorization.GetProperty ("list_authorized_clients_request").EnumerateObject (), Is.Empty);
		}

	[TestCase ("AddAuthorizedClientResponse")]
	[TestCase ("add_authorized_client_response")]
	public void RegistrationReply_AcceptsBothWireForms (string operation)
		{
		byte[] expected = TedapiSigning.PublicKeyDer (_key);
		string json = Reply (operation, new { Client = new { PublicKey = Convert.ToBase64String (expected), State = 3 } });
		Assert.That (LocalKeyEnrollmentProtocol.Parse (json, expected, true).State, Is.EqualTo (LocalKeyState.Verified));
		}

	[Test]
	public void WrongOrMissingKey_NeverReportsAnotherClientVerified ()
		{
		byte[] expected = TedapiSigning.PublicKeyDer (_key);
		foreach (string? key in new[] { null, "AQID", "invalid base64" })
			{
			string json = Reply ("ListAuthorizedClientsResponse", new { clients = new[] { new { PublicKey = key, state = 3 } } });
			Assert.That (LocalKeyEnrollmentProtocol.Parse (json, expected, false).State, Is.EqualTo (LocalKeyState.Unknown));
			}
		string wrongRegistration = Reply ("AddAuthorizedClientResponse", new { client = new { PublicKey = "AQID", state = 3 } });
		Assert.That (LocalKeyEnrollmentProtocol.Parse (wrongRegistration, expected, true).State, Is.EqualTo (LocalKeyState.Unknown));
		}

	[TestCase (1, LocalKeyState.PendingVerification)]
	[TestCase (2, LocalKeyState.VerificationTimedOut)]
	[TestCase (3, LocalKeyState.Verified)]
	[TestCase (4, LocalKeyState.Removed)]
	[TestCase (0, LocalKeyState.Unknown)]
	[TestCase (99, LocalKeyState.Unknown)]
	public void StateMapping_IsExplicit (int state, LocalKeyState expected)
		{
		string json = Reply ("AddAuthorizedClientResponse", new { client = new { state } });
		Assert.That (LocalKeyEnrollmentProtocol.Parse (json, TedapiSigning.PublicKeyDer (_key), true).State, Is.EqualTo (expected));
		}

	[TestCase (null)]
	[TestCase ("{}")]
	[TestCase ("invalid JSON")]
	[TestCase ("{\"response\":{\"message\":{\"Payload\":null}}}")]
	public void MissingOrMalformedReply_DoesNotClaimAuthorization (string? json)
		{
		Assert.That (LocalKeyEnrollmentProtocol.Parse (json, TedapiSigning.PublicKeyDer (_key), true).State, Is.EqualTo (LocalKeyState.Unknown));
		}

	[TestCase (false)]
	[TestCase (true)]
	public void UncertainTransportFailure_DoesNotRetryRegistration (bool fleet)
		{
		using var rig = new EnrollmentRig (fleet);
		rig.Handler.FailTransport = true;
		Assert.That (async () => await rig.Powerwall.RegisterLocalKeyAsync (_key, "Test"), Throws.TypeOf<PowerwallConnectionException> ());
		Assert.That (rig.Handler.Requests, Has.Count.EqualTo (1));
		}

	[TestCase (false)]
	[TestCase (true)]
	public void InvalidOrCancelledRequests_DoNotContactCloud (bool fleet)
		{
		using var rig = new EnrollmentRig (fleet);
		Assert.That (() => rig.Powerwall.RegisterLocalKeyAsync (_key, " "), Throws.ArgumentException);
		Assert.That (() => rig.Powerwall.RegisterLocalKeyAsync (null!, "Test"), Throws.ArgumentNullException);
		using RSA small = RSA.Create ();
		Assert.That (() => rig.Powerwall.GetLocalKeyStatusAsync (small), Throws.ArgumentException);
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		Assert.That (() => rig.Powerwall.GetLocalKeyStatusAsync (_key, cancelled.Token), Throws.InstanceOf<OperationCanceledException> ());
		Assert.That (rig.Handler.Requests, Is.Empty);
		}

	[Test]
	public void LocalConnection_CannotEnrollOrQueryCloudKeys ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "192.0.2.1", Password = "synthetic" });
		var client = new PowerwallLocalClient (new PowerwallOptions { Host = "192.0.2.1", Password = "synthetic" });
		OperationRegressionTests.SetField (powerwall, "_client", client);
		Assert.That (() => powerwall.GetLocalKeyStatusAsync (_key), Throws.TypeOf<PowerwallNotSupportedException> ());
		Assert.That (() => powerwall.RegisterLocalKeyAsync (_key, "Test"), Throws.TypeOf<PowerwallNotSupportedException> ());
		}

	private static string Reply (string operation, object body) => JsonHelper.Serialize (new
		{
		response = new
			{
			message = new
				{
				Payload = new
					{
					Authorization = new
						{
						Message = new Dictionary<string, object> { [operation] = body }
						}
					}
				}
			}
		});

	private sealed class EnrollmentRig : IDisposable
		{
		internal EnrollmentHandler Handler { get; } = new ();
		internal Powerwall Powerwall { get; }

		internal EnrollmentRig (bool fleet)
			{
			PowerwallClientBase client;
			if (fleet)
				{
				var connection = new FleetApiConnection ("synthetic-client", "synthetic-access", null, FleetApiRegions.NORTH_AMERICA, TimeSpan.FromSeconds (5), Handler);
				client = new PowerwallFleetApiClient ("unit@example.test", "synthetic-client", 30, TimeSpan.FromSeconds (5), noFleetApiTokenPersistence: true);
				OperationRegressionTests.SetField (client, "_connection", connection);
				}
			else
				{
				var connection = new TeslaCloudConnection ("synthetic-access", null, TimeSpan.FromSeconds (5));
				((HttpClient)OperationRegressionTests.GetField (connection, "_httpClient")).Dispose ();
				OperationRegressionTests.SetField (connection, "_httpClient", new HttpClient (Handler));
				client = new PowerwallCloudClient ("unit@example.test", 30, TimeSpan.FromSeconds (5), noCloudTokenPersistence: true);
				OperationRegressionTests.SetField (client, "_connection", connection);
				}
			OperationRegressionTests.SetField (client, "_resolvedSiteId", "123");
			Powerwall = new Powerwall (new PowerwallOptions { Email = "unit@example.test", FleetApi = fleet, NoCloudTokenPersistence = true, NoFleetApiTokenPersistence = true });
			OperationRegressionTests.SetField (Powerwall, "_client", client);
			}

		public void Dispose () => Powerwall.Dispose ();
		}

	private sealed class EnrollmentHandler : HttpMessageHandler
		{
		internal List<(Uri Uri, string Body, string Token)> Requests { get; } = new ();
		internal string Response { get; set; } = "{}";
		internal bool FailTransport { get; set; }

		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Post));
			Requests.Add ((request.RequestUri!, await request.Content!.ReadAsStringAsync (), request.Headers.Authorization!.ToString ()));
			if (FailTransport)
				throw new HttpRequestException ("Synthetic uncertain response");
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (Response) };
			}
		}
	}
