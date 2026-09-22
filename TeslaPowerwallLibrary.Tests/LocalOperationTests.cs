using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class LocalOperationTests
	{
	[Test]
	public async Task ModeOnly_PreservesRawLocalReserveInsteadOfAppScaledReserve ()
		{
		var client = new RecordingLocalClient ();
		using var powerwall = Create (client);
		await powerwall.SetModeAsync ("autonomous");
		Assert.That (client.Posted!.BackupReservePercent, Is.EqualTo (24));
		Assert.That (client.Posted.RealMode, Is.EqualTo ("autonomous"));
		}

	[Test]
	public async Task ZeroReserve_PreservesLocalModeAndSerializesNumericZero ()
		{
		var client = new RecordingLocalClient ();
		using var powerwall = Create (client);
		await powerwall.SetReserveAsync (0);
		Assert.That (client.Posted!.BackupReservePercent, Is.Zero);
		Assert.That (client.Posted.RealMode, Is.EqualTo ("self_consumption"));
		Assert.That (client.Json, Does.Contain ("\"backup_reserve_percent\":0"));
		}

	[Test]
	public async Task UnavailableLocalSettings_DoNotOverwriteWithInventedDefaults ()
		{
		var client = new RecordingLocalClient { MissingOperation = true };
		using var powerwall = Create (client);
		Assert.That (await powerwall.SetModeAsync ("autonomous"), Is.Null);
		Assert.That (client.Posted, Is.Null);
		}

	[TestCase (double.NaN)]
	[TestCase (double.PositiveInfinity)]
	[TestCase (double.NegativeInfinity)]
	public void NonFiniteReserve_IsRejectedBeforeWriting (double reserve)
		{
		var client = new RecordingLocalClient ();
		using var powerwall = Create (client);
		Assert.That (async () => await powerwall.SetReserveAsync (reserve), Throws.TypeOf<InvalidBatteryReserveLevelException> ());
		Assert.That (client.Posted, Is.Null);
		}

	private static Powerwall Create (RecordingLocalClient client)
		{
		var powerwall = new Powerwall (new PowerwallOptions { Host = "192.0.2.1", Password = "synthetic" });
		OperationRegressionTests.SetField (powerwall, "_client", client);
		return powerwall;
		}

	private sealed class RecordingLocalClient () : PowerwallClientBase ("unit@example.test")
		{
		internal bool MissingOperation
			{
			get; init;
			}
		internal OperationRequest? Posted
			{
			get; private set;
			}
		internal string? Json
			{
			get; private set;
			}
		public override Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) =>
			 Task.FromResult (api == "/api/operation" ? (MissingOperation ? null : "{\"backup_reserve_percent\":24,\"real_mode\":\"self_consumption\"}") : "{\"din\":\"test--din\"}");
		public override Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default)
			{
			Assert.That (api, Is.EqualTo ("/api/operation"));
			Json = JsonHelper.Serialize (payload);
			Posted = JsonHelper.Deserialize<OperationRequest> (Json);
			return Task.FromResult<string?> ("{}");
			}
		public override Task AuthenticateAsync (CancellationToken cancellationToken = default) => throw new AssertionException ("Unexpected authentication");
		public override Task CloseSessionAsync (CancellationToken cancellationToken = default) => Task.CompletedTask;
		public override Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) => throw new AssertionException ("Unexpected poll");
		public override Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default) => throw new AssertionException ("Unexpected vitals");
		public override Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default) => throw new AssertionException ("Unexpected time remaining");
		}
	}