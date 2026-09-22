using TeslaPowerwallLibrary.Models;
using System.Net;
using System.Net.Http;
using System.Reflection;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.FleetApi;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class OperationRegressionTests
	{
	[TestCase (false)]
	[TestCase (true)]
	public async Task ModeOnly_SendsOnlyMode (bool fleet)
		{
		using var rig = new OperationRig (fleet);
		await rig.Powerwall.SetModeAsync ("autonomous");
		Assert.That (rig.Handler.Posts.Select (p => p.Path), Is.EqualTo (new[] { "/api/1/energy_sites/123/operation" }));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ZeroReserve_SendsNumericZeroAndNoMode (bool fleet)
		{
		using var rig = new OperationRig (fleet);
		await rig.Powerwall.SetReserveAsync (0);
		Assert.That (rig.Handler.Posts.Select (p => p.Path), Is.EqualTo (new[] { "/api/1/energy_sites/123/backup" }));
		Assert.That (JsonHelper.Deserialize<BackupReserveRequest> (rig.Handler.Posts[0].Body)!.BackupReservePercent, Is.Zero);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task OperationWrite_InvalidatesSiteConfiguration (bool fleet)
		{
		using var rig = new OperationRig (fleet);
		Assert.That (await rig.Powerwall.GetModeAsync (), Is.EqualTo ("self_consumption"));
		await rig.Powerwall.SetOperationAsync (30, "autonomous");
		Assert.That (await rig.Powerwall.GetModeAsync (), Is.EqualTo ("autonomous"));
		Assert.That (rig.Handler.SiteReads, Is.EqualTo (2));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task EmptyOperation_DoesNotSendCommands (bool fleet)
		{
		using var rig = new OperationRig (fleet);
		Assert.That (await rig.Powerwall.SetOperationAsync (), Is.Null);
		Assert.That (rig.Handler.Posts, Is.Empty);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task TypedCloudProjections_PreservePowerStatusAndTelemetry (bool fleet)
		{
		using var rig = new OperationRig (fleet);
		var power = await rig.Powerwall.PowerAsync ();
		Assert.That (power.Site, Is.EqualTo (-125.5));
		Assert.That (power.Solar, Is.EqualTo (6000));
		Assert.That (power.Battery, Is.EqualTo (-2000));
		Assert.That (power.Load, Is.EqualTo (3874.5));
		var status = await rig.Powerwall.StatusAsync ();
		Assert.That (status!.Din, Is.EqualTo ("test--din"));
		Assert.That (status.Version, Is.EqualTo ("26.1"));
		var system = await rig.Powerwall.SystemStatusAsync ();
		Assert.That (system!.NominalFullPackEnergy, Is.EqualTo (13500));
		Assert.That (system.NominalEnergyRemaining, Is.EqualTo (6750));
		Assert.That (system.MaxChargePower, Is.EqualTo (5000));
		Assert.That (system.AvailableBlocks, Is.EqualTo (1));
		var vitals = await rig.Powerwall.VitalsAsync ();
		Assert.That (vitals!["STSTSM--test--din"]["alerts"], Is.InstanceOf<string[]> ());
		Assert.That (await rig.Powerwall.AlertsAsync (), Does.Contain ("SystemConnectedToGrid"));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task PartialWriteFailure_StillInvalidatesConfiguration (bool fleet)
		{
		using var rig = new OperationRig (fleet);
		Assert.That (await rig.Powerwall.GetReserveAsync (), Is.EqualTo (20).Within (0.001));
		using var cancelled = new CancellationTokenSource ();
		rig.Handler.CancelModeWriteSource = cancelled;
		rig.Handler.FailModeWrite = true;
		Assert.That (async () => await rig.Powerwall.SetOperationAsync (30, "autonomous", cancelled.Token), Throws.InstanceOf<OperationCanceledException> ());
		Assert.That (await rig.Powerwall.GetReserveAsync (), Is.EqualTo (30).Within (0.001));
		}

	internal sealed class OperationRig : IDisposable
		{
		internal OperationHandler Handler { get; } = new ();
		internal Powerwall Powerwall
			{
			get;
			}
		internal OperationRig (bool fleet)
			{
			PowerwallClientBase client;
			if (fleet)
				{
				var connection = new FleetApiConnection ("synthetic-client", "synthetic-access", null, FleetApiRegions.NORTH_AMERICA, TimeSpan.FromSeconds (5), Handler);
				client = new PowerwallFleetApiClient ("unit@example.test", "synthetic-client", 30, TimeSpan.FromSeconds (5), noFleetApiTokenPersistence: true);
				SetField (client, "_connection", connection);
				}
			else
				{
				var connection = new TeslaCloudConnection ("synthetic-access", null, TimeSpan.FromSeconds (5));
				((HttpClient)GetField (connection, "_httpClient")).Dispose ();
				SetField (connection, "_httpClient", new HttpClient (Handler));
				client = new PowerwallCloudClient ("unit@example.test", 30, TimeSpan.FromSeconds (5), noCloudTokenPersistence: true);
				SetField (client, "_connection", connection);
				}
			SetField (client, "_resolvedSiteId", "123");
			Powerwall = new Powerwall (new PowerwallOptions { Email = "unit@example.test", FleetApi = fleet, NoCloudTokenPersistence = true, NoFleetApiTokenPersistence = true });
			SetField (Powerwall, "_client", client);
			}
		public void Dispose () => Powerwall.Dispose ();
		}

	internal static object GetField (object target, string name) => target.GetType ().GetField (name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue (target)!;
	internal static void SetField (object target, string name, object value) => target.GetType ().GetField (name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue (target, value);

	internal sealed class OperationHandler : HttpMessageHandler
		{
		internal List<(string Path, string Body)> Posts { get; } = new ();
		internal int SiteReads
			{
			get; private set;
			}
		internal bool FailModeWrite
			{
			get; set;
			}
		internal CancellationTokenSource? CancelModeWriteSource
			{
			get; set;
			}
		private string _mode = "self_consumption";
		private double _reserve = 20;
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			string path = request.RequestUri!.AbsolutePath;
			if (request.Method == HttpMethod.Post)
				{
				string body = await request.Content!.ReadAsStringAsync ();
				Posts.Add ((path, body));

				if (path.EndsWith ("/operation", StringComparison.Ordinal))
					{
					if (FailModeWrite)
						{
						CancelModeWriteSource?.Cancel ();
						throw new OperationCanceledException ("Synthetic partial failure", cancellationToken);
						}
					_mode = JsonHelper.Deserialize<OperationModeRequest> (body)!.DefaultRealMode!;
					}
				else if (path.EndsWith ("/backup", StringComparison.Ordinal))
					_reserve = JsonHelper.Deserialize<BackupReserveRequest> (body)!.BackupReservePercent;
				else
					Assert.Fail ("Unexpected command: " + path);
				return Reply ("{\"response\":{\"code\":201,\"message\":\"Updated\"}}");
				}
			if (path.EndsWith ("/live_status", StringComparison.Ordinal))
				return Reply ("""{"response":{"timestamp":"2026-07-07T19:15:00+01:00","grid_power":-125.5,"solar_power":"6000","battery_power":-2000,"load_power":3874.5,"island_status":"on_grid","grid_status":"Active"}}""");
			if (path.EndsWith ("/site_status", StringComparison.Ordinal))
				return Reply ("""{"response":{"percentage_charged":50,"total_pack_energy":13500,"energy_left":6750}}""");
			Assert.That (path, Is.EqualTo ("/api/1/energy_sites/123/site_info"));
			SiteReads++;
			return Reply (JsonHelper.Serialize (new
				{
				response = new
					{
					id = "test--din",
					backup_reserve_percent = _reserve,
					default_real_mode = _mode,
					version = "26.1",
					installation_date = "2026-01-01T12:00:00+01:00",
					nameplate_power = "5000",
					battery_count = 1,
					components = new
						{
						gateway = "teg",
						solar = true
						}
					}
				}));
			}
		private static HttpResponseMessage Reply (string body) => new (HttpStatusCode.OK) { Content = new StringContent (body) };
		}
	}