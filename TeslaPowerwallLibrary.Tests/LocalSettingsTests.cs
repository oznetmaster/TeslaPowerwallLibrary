// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

using Google.Protobuf;

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;

using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture, NonParallelizable]
public sealed partial class LocalSettingsTests
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

	[TestCase (0, 5)]
	[TestCase (20, 24)]
	[TestCase (100, 100)]
	public async Task ReserveUpdate_UsesAppScaleAndRetainsUnrelatedFields (double app, double raw)
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		Assert.That ((await rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { BackupReservePercent = app })).Acknowledged, Is.True);
		Assert.That (rig.Handler.Reads, Is.EqualTo (1));
		Assert.That (rig.Handler.Writes, Has.Count.EqualTo (1));
		Signed.FileStoreAPIUpdateFileRequest write = rig.Handler.Writes[0];
		Assert.That (write.Hash.ToByteArray (), Is.EqualTo (new byte[] { 1, 2, 3 }));
		Assert.That (write.Domain, Is.EqualTo (Signed.FileStoreAPIDomain.ConfigJson));
		Assert.That (write.File.Name, Is.EqualTo ("config.json"));
		using JsonDocument result = JsonDocument.Parse (write.File.Blob.ToStringUtf8 ());
		JsonElement root = result.RootElement;
		Assert.That (root.GetProperty ("site_info").GetProperty ("backup_reserve_percent").GetDouble (), Is.EqualTo (raw));
		Assert.That (root.GetProperty ("default_real_mode").GetString (), Is.EqualTo ("autonomous"));
		Assert.That (root.GetProperty ("private_network").GetProperty ("password").GetString (), Is.EqualTo ("offline-fixture-only"));
		Assert.That (root.GetProperty ("site_info").GetProperty ("unknown_nested").GetProperty ("array")[1].ValueKind, Is.EqualTo (JsonValueKind.Null));
		Assert.That (root.GetProperty ("site_info").GetProperty ("customer_preferred_export_rule").ValueKind, Is.EqualTo (JsonValueKind.Null));
		}

	[Test]
	public async Task ExistingHighLevelModeWrite_LeavesRawReserveUntouched ()
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		await rig.Powerwall.SetModeAsync ("self_consumption");
		using JsonDocument result = JsonDocument.Parse (rig.Handler.Writes.Single ().File.Blob.ToStringUtf8 ());
		Assert.That (result.RootElement.GetProperty ("site_info").GetProperty ("backup_reserve_percent").GetRawText (), Is.EqualTo ("24"));
		Assert.That (result.RootElement.GetProperty ("default_real_mode").GetString (), Is.EqualTo ("self_consumption"));
		Assert.That (rig.Handler.Reads, Is.EqualTo (1), "No stale cached back-fill or firmware request is needed.");
		}

	[Test]
	public async Task CombinedUpdate_PreservesFalseAndZero ()
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		await rig.Powerwall.UpdateLocalSettingsAsync (new LocalSettingsUpdate
			{ BackupReservePercent = 0, GridChargingEnabled = false, GridExport = "never", OperationMode = "backup" });
		using JsonDocument result = JsonDocument.Parse (rig.Handler.Writes.Single ().File.Blob.ToStringUtf8 ());
		JsonElement site = result.RootElement.GetProperty ("site_info");
		Assert.That (site.GetProperty ("backup_reserve_percent").GetDouble (), Is.EqualTo (5));
		Assert.That (site.GetProperty ("disallow_charge_from_grid_with_solar_installed").GetBoolean (), Is.True);
		Assert.That (site.GetProperty ("customer_preferred_export_rule").GetString (), Is.EqualTo ("never"));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ConcurrentUpdates_EachReadFreshHash (bool combine)
		{
		using var rig = new Rig ();
		rig.Handler.ChangeHashAfterWrite = true;
		await rig.Client.AuthenticateAsync ();
		Task first = rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { GridChargingEnabled = true });
		Task second = rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { GridExport = combine ? "battery_ok" : "pv_only" });
		await Task.WhenAll (first, second);
		Assert.That (rig.Handler.Reads, Is.EqualTo (2));
		Assert.That (rig.Handler.Writes.Select (w => w.Hash[0]), Is.EqualTo (new byte[] { 1, 2 }));
		}

	[TestCase ("missing-hash")]
	[TestCase ("missing-site")]
	[TestCase ("wrong-file")]
	[TestCase ("empty-file")]
	public async Task UnusableRead_NeverWrites (string failure)
		{
		using var rig = new Rig ();
		rig.Handler.ReadFailure = failure;
		await rig.Client.AuthenticateAsync ();
		Assert.That (async () => await rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { OperationMode = "backup" }), Throws.TypeOf<PowerwallConnectionException> ());
		Assert.That (rig.Handler.Writes, Is.Empty);
		}

	[TestCase ("rpc-error")]
	[TestCase ("empty")]
	[TestCase ("wrong")]
	[TestCase ("unauthorized")]
	[TestCase ("timeout")]
	public async Task UncertainWrite_IsNotRetriedAndInvalidatesCachedConfiguration (string failure)
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		await rig.Client.GetConfigurationAsync ();
		rig.Handler.WriteFailure = failure;
		Assert.That (async () => await rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { GridExport = "never" }), Throws.Exception);
		Assert.That (rig.Handler.Writes, Has.Count.EqualTo (1));
		Assert.That (rig.Handler.Logins, Is.EqualTo (1), "Writes do not trigger authentication replay.");
		await rig.Client.GetConfigurationAsync ();
		Assert.That (rig.Handler.Reads, Is.EqualTo (3), "An uncertain write invalidates the old cached configuration.");
		}

	/// <summary>A device error must remain a failure without leaking its diagnostic payload or replaying the write.</summary>
	[Test]
	public async Task RpcError_ReportsCodeWithoutPrivateDetails ()
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		rig.Handler.WriteFailure = "rpc-error";
		Assert.That (async () => await rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { BackupReservePercent = 20 }),
			Throws.TypeOf<PowerwallConnectionException> ().With.Message.Contains ("RPC status 7").And.Message.Not.Contains ("private device diagnostic"));
		Assert.That (rig.Handler.Writes, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task EmptyUpdate_PerformsNoNetworkOperation ()
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		Assert.That ((await rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate ())).Acknowledged, Is.False);
		Assert.That (rig.Handler.Reads, Is.Zero);
		Assert.That (rig.Handler.Writes, Is.Empty);
		}

	[Test]
	public async Task ControlDisabled_RejectsBeforeConfigurationRead ()
		{
		using var rig = new Rig (allowControl: false);
		await rig.Client.AuthenticateAsync ();
		Assert.That (async () => await rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { OperationMode = "backup" }), Throws.TypeOf<PowerwallNotSupportedException> ());
		Assert.That (rig.Handler.Reads, Is.Zero);
		Assert.That (rig.Handler.Writes, Is.Empty);
		}

	[TestCase (double.NaN)]
	[TestCase (double.PositiveInfinity)]
	[TestCase (-1)]
	[TestCase (101)]
	public void InvalidReserve_IsRejectedBeforeNetwork (double value)
		{
		using var rig = new Rig ();
		Assert.That (async () => await rig.Client.UpdateSettingsAsync (new LocalSettingsUpdate { BackupReservePercent = value }), Throws.TypeOf<ArgumentOutOfRangeException> ());
		Assert.That (rig.Handler.Logins, Is.Zero);
		}

	[Test]
	public async Task PublicConfiguration_DoesNotExposePrivateFields ()
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		LocalConfiguration result = await rig.Powerwall.GetLocalConfigurationAsync ();
		Assert.That (JsonSerializer.Serialize (result), Does.Not.Contain ("offline-fixture-only").And.Not.Contain ("unknown_nested"));
		Assert.That (await rig.Powerwall.GetGridChargingAsync (), Is.True);
		Assert.That (await rig.Powerwall.GetGridExportAsync (), Is.Null);
		}

	private sealed class Rig : IDisposable
		{
		internal Handler Handler { get; } = new ();
		internal PowerwallTedapiClient Client { get; }
		internal Powerwall Powerwall { get; }
		internal Rig (bool allowControl = true)
			{
			var options = new PowerwallOptions
				{
				Host = "powerwall.test", Password = "synthetic", LocalProtocol = PowerwallLocalProtocol.TedapiSigned,
				LocalSigningKey = _key, AllowLocalControl = allowControl, NoLocalSessionPersistence = true
				};
			Client = new PowerwallTedapiClient (options, Handler);
			Powerwall = new Powerwall (options);
			OperationRegressionTests.SetField (Powerwall, "_client", Client);
			}
		public void Dispose () => Powerwall.Dispose ();
		}

	private sealed class Handler : HttpMessageHandler
		{
		private const string CONFIG = """{"vin":"test--powerwall","default_real_mode":"autonomous","site_info":{"backup_reserve_percent":24,"disallow_charge_from_grid_with_solar_installed":false,"customer_preferred_export_rule":null,"unknown_nested":{"array":[1,null,true]}},"private_network":{"password":"offline-fixture-only"}}""";
		internal List<Signed.FileStoreAPIUpdateFileRequest> Writes { get; } = new ();
		internal List<Signed.TEGMessages> TegRequests { get; } = new ();
		internal string? TegFailure { get; set; }
		internal int IslandResult { get; set; } = 1;
		internal int Reads { get; private set; }
		internal int Logins { get; private set; }
		internal string? ReadFailure { get; set; }
		internal string? WriteFailure { get; set; }
		internal bool ChangeHashAfterWrite { get; set; }

		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Assert.That (request.RequestUri!.Host, Is.EqualTo ("powerwall.test"));
			if (request.RequestUri.AbsolutePath == "/api/login/Basic")
				{
				Logins++;
				return new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent ("""{"token":"synthetic"}""") };
				}
			if (request.RequestUri.AbsolutePath == "/tedapi/din")
				return new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent ("test--powerwall") };
			Assert.That (request.RequestUri.AbsolutePath, Is.EqualTo ("/tedapi/v1r"));
			Signed.RoutableMessage routable = Signed.RoutableMessage.Parser.ParseFrom (await request.Content!.ReadAsByteArrayAsync ());
			Signed.MessageEnvelope envelope = Signed.MessageEnvelope.Parser.ParseFrom (routable.ProtobufMessageAsBytes);
			Assert.That (envelope.DeliveryChannel, Is.EqualTo (Signed.DeliveryChannel.HermesCommand));
			Assert.That (envelope.Sender.AuthorizedClient, Is.EqualTo (1));
			Assert.That (envelope.Recipient.Din, Is.EqualTo ("test--powerwall"));

			if (envelope.Teg is Signed.TEGMessages teg)
				{
				TegRequests.Add (teg);
				var answer = new Signed.TEGMessages ();
				if (TegFailure == "unauthorized")
					return new HttpResponseMessage (HttpStatusCode.Unauthorized);
				if (TegFailure == "timeout")
					throw new TaskCanceledException ("Synthetic lost command response");
				if (TegFailure != "empty")
					{
					if (teg.GetBackupEventsRequest is not null)
						answer.GetBackupEventsResponse = new Signed.TEGAPIGetBackupEventsResponse
							{
							ManualBackupEvent = new Signed.ManualBackupEvent
								{
								SchedulingInfo = new Signed.ControlEventSchedulingInfo
									{ StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset (DateTimeOffset.UtcNow.AddMinutes (10)), DurationSeconds = 120, Priority = 1 }
								}
							};
					else if (teg.CancelManualBackupEventRequest is not null)
						answer.CancelManualBackupEventResponse = new Signed.TEGAPICancelManualBackupEventResponse ();
					else if (teg.ScheduleManualBackupEventRequest is not null)
						answer.ScheduleManualBackupEventResponse = new Signed.TEGAPIScheduleManualBackupEventResponse ();
					else if (teg.SetIslandModeRequest is not null)
						answer.SetIslandModeResponse = new Signed.TEGAPISetIslandModeResponse { Result = IslandResult };
					else
						Assert.Fail ("Unexpected local command.");
					}
				return new HttpResponseMessage (HttpStatusCode.OK)
					{ Content = new ByteArrayContent (new Signed.RoutableMessage
						{ ProtobufMessageAsBytes = new Signed.MessageEnvelope { Teg = answer }.ToByteString () }.ToByteArray ()) };
				}
			var reply = new Signed.MessageEnvelope { Filestore = new Signed.FileStoreMessages () };
			if (envelope.Filestore?.ReadFileRequest is Signed.FileStoreAPIReadFileRequest read)
				{
				Assert.That (read.Name, Is.EqualTo ("config.json"));
				Assert.That (read.Domain, Is.EqualTo (Signed.FileStoreAPIDomain.ConfigJson));
				Reads++;
				reply.Filestore.ReadFileResponse = new Signed.FileStoreAPIReadFileResponse
					{
					Hash = ReadFailure == "missing-hash" ? ByteString.Empty : ByteString.CopyFrom (new byte[] { (byte)(ChangeHashAfterWrite ? Writes.Count + 1 : 1), 2, 3 }),
					File = new Signed.FileStoreAPIFile
						{
						Name = ReadFailure == "wrong-file" ? "different.json" : "config.json",
						Blob = ByteString.CopyFromUtf8 (ReadFailure == "missing-site" ? "{}" : ReadFailure == "empty-file" ? "" : CONFIG)
						}
					};
				}
			else
				{
				Assert.That (envelope.Filestore?.UpdateFileRequest, Is.Not.Null);
				Writes.Add (envelope.Filestore!.UpdateFileRequest);
				if (WriteFailure == "unauthorized")
					return new HttpResponseMessage (HttpStatusCode.Unauthorized);
				if (WriteFailure == "timeout")
					throw new TaskCanceledException ("Synthetic lost response");
				if (WriteFailure == "rpc-error")
					reply.Common = new Signed.CommonMessages { ErrorResponse = new Signed.ErrorResponse
						{ Status = new Signed.RpcStatus { Code = 7, Message = "private device diagnostic" } } };
				else if (WriteFailure == "wrong")
					reply.Filestore.ReadFileResponse = new Signed.FileStoreAPIReadFileResponse ();
				else if (WriteFailure != "empty")
					reply.Filestore.UpdateFileResponse = new Signed.FileStoreAPIUpdateFileResponse ();
				}
			return new HttpResponseMessage (HttpStatusCode.OK)
				{ Content = new ByteArrayContent (new Signed.RoutableMessage { ProtobufMessageAsBytes = reply.ToByteString () }.ToByteArray ()) };
			}
		}
	}
