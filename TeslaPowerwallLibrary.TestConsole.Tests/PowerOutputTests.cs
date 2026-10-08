// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using NUnit.Framework;
using TeslaPowerwallLibrary.TestConsole;

namespace TeslaPowerwallLibrary.TestConsole.Tests;

/// <summary>Checks actual console output when measured zero and missing telemetry coexist.</summary>
[TestFixture, NonParallelizable]
public sealed class PowerOutputTests
	{
	/// <summary>The console uses nullable readings rather than the compatibility API's zero defaults.</summary>
	[Test]
	public async Task PowerCommand_DistinguishesUnavailableFromZero ()
		{
		using var powerwall = new Powerwall (new PowerwallOptions { Host = "synthetic.test" });
		typeof (Powerwall).GetField ("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (powerwall, new ReadingsClient ());
		TextWriter original = Console.Out;
		using var output = new StringWriter ();
		try
			{
			Console.SetOut (output);
			await PowerwallActions.PowerAsync (powerwall, CancellationToken.None);
			}
		finally { Console.SetOut (original); }
		string[] lines = output.ToString ().Split ('\n');
		Assert.That (lines.Single (line => line.Contains ("Solar")), Does.Contain ("n/a"));
		Assert.That (lines.Single (line => line.Contains ("Site (grid)")), Does.Contain ("0.0 W"));
		Assert.That (lines.Single (line => line.Contains ("Battery")), Does.Contain ("-250.0 W"));
		}

	/// <summary>Provides only a synthetic meter response; all writes are rejected.</summary>
	private sealed class ReadingsClient () : PowerwallClientBase ("unit@example.test")
		{
		/// <inheritdoc/>
		public override Task AuthenticateAsync (CancellationToken cancellationToken = default) => Task.CompletedTask;
		/// <inheritdoc/>
		public override Task CloseSessionAsync (CancellationToken cancellationToken = default) => Task.CompletedTask;
		/// <inheritdoc/>
		public override Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) =>
			Task.FromResult<string?> ("""{"site":{"instant_power":0},"battery":{"instant_power":-250}}""");
		/// <inheritdoc/>
		public override Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		/// <inheritdoc/>
		public override Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default) => throw new AssertionException ("Unexpected write");
		/// <inheritdoc/>
		public override Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		/// <inheritdoc/>
		public override Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		}
	}
