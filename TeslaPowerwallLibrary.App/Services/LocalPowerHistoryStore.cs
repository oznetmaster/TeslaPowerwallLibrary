// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>An actual LAN reading, timestamped when received by the app, in watts.</summary>
/// <param name="ReceivedAt">Application receipt time.</param>
/// <param name="SolarWatts">Solar power, or null when unreported.</param>
/// <param name="BatteryWatts">Battery power, positive for discharge.</param>
/// <param name="HomeWatts">Home consumption.</param>
/// <param name="GridWatts">Grid power, positive for import.</param>
internal sealed record LocalPowerSample (DateTimeOffset ReceivedAt, double? SolarWatts, double? BatteryWatts,
	double? HomeWatts, double? GridWatts);

/// <summary>Desktop-only storage for actual LAN samples, separate from cloud energy batches.</summary>
internal sealed class LocalPowerHistoryStore
	{
	private readonly string _path;
	private readonly SemaphoreSlim _gate = new (1, 1);

	/// <summary>Creates a local sample store at the desktop history database path.</summary>
	/// <param name="path">SQLite database path.</param>
	internal LocalPowerHistoryStore (string path) => _path = path;

	/// <summary>Saves one real sample without contacting either the Powerwall or the cloud.</summary>
	/// <param name="host">Local connection identity.</param>
	/// <param name="sample">The received sample; missing measurements remain null.</param>
	/// <returns>A task completing once the sample is committed.</returns>
	internal async Task AppendAsync (string host, LocalPowerSample sample)
		{
		await _gate.WaitAsync ().ConfigureAwait (false);
		try
			{
			await Task.Run (() =>
				{
				using var connection = Open ();
				using var command = connection.CreateCommand ();
				command.CommandText = "INSERT OR REPLACE INTO local_power VALUES ($host, $time, $solar, $battery, $home, $grid)";
				command.Parameters.AddWithValue ("$host", host.ToLowerInvariant ());
				command.Parameters.AddWithValue ("$time", sample.ReceivedAt.ToUnixTimeMilliseconds ());
				command.Parameters.AddWithValue ("$solar", (object?)sample.SolarWatts ?? DBNull.Value);
				command.Parameters.AddWithValue ("$battery", (object?)sample.BatteryWatts ?? DBNull.Value);
				command.Parameters.AddWithValue ("$home", (object?)sample.HomeWatts ?? DBNull.Value);
				command.Parameters.AddWithValue ("$grid", (object?)sample.GridWatts ?? DBNull.Value);
				command.ExecuteNonQuery ();
				}).ConfigureAwait (false);
			}
		finally { _gate.Release (); }
		}

	/// <summary>Reads only the requested host and time window, preserving missing values and gaps.</summary>
	/// <param name="host">Local connection identity.</param>
	/// <param name="start">Inclusive start.</param>
	/// <param name="end">Exclusive end.</param>
	/// <returns>The saved real samples in timestamp order.</returns>
	internal async Task<IReadOnlyList<LocalPowerSample>> ReadAsync (string host, DateTimeOffset start, DateTimeOffset end)
		{
		await _gate.WaitAsync ().ConfigureAwait (false);
		try
			{
			return await Task.Run<IReadOnlyList<LocalPowerSample>> (() =>
				{
				using var connection = Open ();
				using var command = connection.CreateCommand ();
				command.CommandText = "SELECT received, solar, battery, home, grid FROM local_power WHERE host = $host AND received >= $start AND received < $end ORDER BY received";
				command.Parameters.AddWithValue ("$host", host.ToLowerInvariant ());
				command.Parameters.AddWithValue ("$start", start.ToUnixTimeMilliseconds ());
				command.Parameters.AddWithValue ("$end", end.ToUnixTimeMilliseconds ());
				using var reader = command.ExecuteReader ();
				var result = new List<LocalPowerSample> ();
				while (reader.Read ())
					{
					double? Value (int ordinal) => reader.IsDBNull (ordinal) ? null : reader.GetDouble (ordinal);
					result.Add (new LocalPowerSample (DateTimeOffset.FromUnixTimeMilliseconds (reader.GetInt64 (0)), Value (1), Value (2), Value (3), Value (4)));
					}
				return result;
				}).ConfigureAwait (false);
			}
		finally { _gate.Release (); }
		}

	private SqliteConnection Open ()
		{
		Directory.CreateDirectory (Path.GetDirectoryName (Path.GetFullPath (_path))!);
		var connection = new SqliteConnection (new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString ());
		try
			{
			connection.Open ();
			using var command = connection.CreateCommand ();
			command.CommandText = "CREATE TABLE IF NOT EXISTS local_power (host TEXT NOT NULL, received INTEGER NOT NULL, solar REAL, battery REAL, home REAL, grid REAL, PRIMARY KEY(host, received))";
			command.ExecuteNonQuery ();
			return connection;
			}
		catch { connection.Dispose (); throw; }
		}
	}
