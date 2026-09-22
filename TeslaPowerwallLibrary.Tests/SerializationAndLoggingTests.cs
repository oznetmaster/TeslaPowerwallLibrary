using System.Text.Json;

using Microsoft.Extensions.Logging;

using TeslaPowerwallLibrary.Cloud;
using TeslaPowerwallLibrary.FleetApi;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class SerializationAndLoggingTests
	{
	[TestCase (false)]
	[TestCase (true)]
	public async Task CallerLogger_ReachesClientAndKeepsConnectionContext (bool fleet)
		{
		var first = new CaptureLogger ("first-device");
		var second = new CaptureLogger ("second-device");
		using var a = new Powerwall (new PowerwallOptions { Logger = first, FleetApi = fleet, NoCloudTokenPersistence = true, NoFleetApiTokenPersistence = true });
		using var b = new Powerwall (new PowerwallOptions { Logger = second, FleetApi = fleet, NoCloudTokenPersistence = true, NoFleetApiTokenPersistence = true });
		using (first.BeginScope ("connection-a"))
			{
			// The client logs its mode before rejecting absent credentials. No HTTP request is possible.
			Assert.That (async () => await a.ConnectAsync (), Throws.InstanceOf<PowerwallException> ());
			}
		Assert.That (first.Entries, Has.Count.EqualTo (1));
		Assert.That (first.Entries[0], Does.Contain ("first-device/connection-a"));
		Assert.That (second.Entries, Is.Empty);
		a.Dispose ();
		Assert.That (first.Disposed, Is.False, "The host owns the logger lifetime.");
		await Task.CompletedTask;
		}

	[Test]
	public void ObjectValuedResponses_ContainOnlyClrContainersAndScalars ()
		{
		var backup = CalendarHistoryParser.ParseBackup ("""{"events":[{"nested":{"alerts":["one",2,true,null]}}],"events_count":1}""");
		var nested = backup.Events[0]["nested"];
		Assert.That (nested, Is.InstanceOf<IReadOnlyDictionary<string, object?>> ());
		var alerts = ((IReadOnlyDictionary<string, object?>)nested!)["alerts"];
		Assert.That (alerts, Is.InstanceOf<List<object?>> ());
		Assert.That ((List<object?>)alerts!, Is.EqualTo (new object?[] { "one", 2L, true, null }));
		var status = JsonHelper.Deserialize<SystemStatus> ("""{"grid_faults":[{"name":"fault","codes":[1,2]}]}""");
		Assert.That (status!.GridFaults![0], Is.InstanceOf<IReadOnlyDictionary<string, object?>> ());
		}

	[Test]
	public void CloudModels_KeepNumericStringsAndOptionalFields ()
		{
		var config = JsonHelper.Deserialize<SiteConfigResponse> ("""{"id":12345,"nameplate_power":"5000","nameplate_energy":13500,"backup_reserve_percent":"0"}""");
		Assert.That (config!.Id, Is.EqualTo ("12345"));
		Assert.That (config.NameplatePower, Is.EqualTo (5000));
		Assert.That (config.NameplateEnergy, Is.EqualTo (13500));
		Assert.That (config.BackupReservePercent, Is.Zero);
		Assert.That (config.BatteryCount, Is.Null);
		var power = JsonHelper.Deserialize<SitePowerResponse> ("""{"grid_power":"-125.5","timestamp":"2026-07-07T19:15:00+01:00"}""");
		Assert.That (power!.GridPower, Is.EqualTo (-125.5));
		Assert.That (power.Timestamp, Is.EqualTo ("2026-07-07T19:15:00+01:00"));
		}

	[Test]
	public void HistoryEnvelope_PreservesOffsetAndPrivateEnergyFields ()
		{
		var points = CalendarHistoryParser.ParseEnergy ("""{"response":{"time_series":[{"timestamp":"2026-07-07T19:15:00+01:00","solar_energy_exported":1500}]}}""");
		Assert.That (points[0].Timestamp.Offset, Is.EqualTo (TimeSpan.FromHours (1)));
		Assert.That (points[0].SolarKwh, Is.EqualTo (1.5));
		}

	[Test]
	public void LibraryAssembly_DoesNotReferenceRemovedDependencies ()
		{
		var assembly = typeof (Powerwall).Assembly;
        var references = assembly.GetReferencedAssemblies ().Select (a => a.Name).ToArray ();
		Assert.That (references, Does.Not.Contain ("Newtonsoft.Json"));
		Assert.That (references, Does.Not.Contain ("log4net"));
		// Processor packages merge the library and runtime dependencies into one assembly.
        var available = references.Concat (new[] { assembly.GetName ().Name });
        Assert.That (available, Does.Contain (typeof (JsonSerializer).Assembly.GetName ().Name));
        Assert.That (available, Does.Contain (typeof (ILogger).Assembly.GetName ().Name));
        Assert.That (assembly.GetType ("Newtonsoft.Json.Linq.JObject"), Is.Null);
        Assert.That (assembly.GetType ("log4net.LogManager"), Is.Null);
		}

	internal sealed class CaptureLogger (string context) : ILogger, IDisposable
		{
		private string? _scope;
		internal List<string> Entries { get; } = new ();
		internal bool Disposed
			{
			get; private set;
			}
		public bool IsEnabled (LogLevel logLevel) => true;
		public IDisposable BeginScope<TState> (TState state) where TState : notnull
			{
			string? old = _scope;
			_scope = state.ToString ();
			return new Scope (() => _scope = old);
			}
		public void Log<TState> (LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			 Entries.Add (context + "/" + _scope + ": " + formatter (state, exception));
		public void Dispose () => Disposed = true;
		private sealed class Scope (Action close) : IDisposable
			{
			public void Dispose () => close ();
			}
		}
	}