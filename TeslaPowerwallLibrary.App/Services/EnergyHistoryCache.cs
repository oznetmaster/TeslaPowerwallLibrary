// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>A typed energy sample saved by the desktop app; all measurements are kWh.</summary>
/// <param name="Timestamp">The source timestamp.</param>
/// <param name="SolarKwh">Solar production.</param>
/// <param name="HomeKwh">Home consumption.</param>
/// <param name="FromGridKwh">Grid import.</param>
/// <param name="ToGridKwh">Grid export.</param>
/// <param name="BatteryChargeKwh">Battery charge.</param>
/// <param name="BatteryDischargeKwh">Battery discharge.</param>
/// <param name="BatteryToHomeKwh">Battery contribution to home loads, or null for unreported or older cached data.</param>
internal sealed record StoredEnergyPoint (DateTimeOffset Timestamp, double SolarKwh, double HomeKwh,
	double FromGridKwh, double ToGridKwh, double BatteryChargeKwh, double BatteryDischargeKwh, double? BatteryToHomeKwh = null)
	{
	/// <summary>Copies a typed cloud response without retaining its raw payload.</summary>
	/// <param name="point">The cloud sample.</param>
	/// <returns>The stored sample.</returns>
	internal static StoredEnergyPoint From (EnergyHistoryPoint point) => new (point.Timestamp, point.SolarKwh,
		point.HomeKwh, point.FromGridKwh, point.ToGridKwh, point.BatteryChargeKwh, point.BatteryDischargeKwh, point.BatteryToHomeKwh);
	}

/// <summary>Identifies one account, site, time zone, and requested aggregation window.</summary>
/// <param name="Account">Provider and account scope, without credentials.</param>
/// <param name="Site">Cloud site identifier.</param>
/// <param name="Period">Cloud aggregation period.</param>
/// <param name="Timezone">Explicit IANA time zone used in the request.</param>
/// <param name="Start">Inclusive start, or null for lifetime.</param>
/// <param name="End">Inclusive end, or null for lifetime.</param>
internal sealed record EnergyHistoryRequest (string Account, string Site, string Period, string Timezone,
	DateTimeOffset? Start, DateTimeOffset? End)
	{
	/// <summary>Gets an unambiguous, non-secret database key for this request.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	internal string Key => Convert.ToHexString (SHA256.HashData (Encoding.UTF8.GetBytes (JsonSerializer.Serialize (this))));
	}

/// <summary>A cached or fetched batch and its provenance.</summary>
/// <param name="Points">Typed samples.</param>
/// <param name="RetrievedAt">Time the cloud batch was retrieved.</param>
/// <param name="FromCache">Whether no cloud fetch was required.</param>
/// <param name="Warning">A stale-cache or storage warning, when applicable.</param>
/// <param name="IncludesHomeContribution">Whether this batch was stored with the battery-to-home field, including explicitly unavailable values.</param>
internal sealed record EnergyHistoryResult (IReadOnlyList<StoredEnergyPoint> Points, DateTimeOffset RetrievedAt,
	bool FromCache, string? Warning = null, bool IncludesHomeContribution = true);

/// <summary>
/// Desktop-only SQLite cache. Completed periods fetched at least a day after their end are reused;
/// provisional periods expire after five minutes. Database access runs off the UI thread.
/// </summary>
internal sealed class EnergyHistoryCache
	{
	private readonly string _path;

	/// <summary>Gets the desktop database path shared with actual LAN sample storage.</summary>
	internal string DatabasePath => _path;
	private readonly Func<DateTimeOffset> _clock;
	private readonly SemaphoreSlim _gate = new (1, 1);

	/// <summary>Creates a cache at a caller-selected desktop path.</summary>
	/// <param name="path">Database path outside the repository.</param>
	/// <param name="clock">Optional clock for deterministic offline tests.</param>
	internal EnergyHistoryCache (string path, Func<DateTimeOffset>? clock = null)
		{
		_path = path;
		_clock = clock ?? (() => DateTimeOffset.UtcNow);
		}

	/// <summary>Reads the cache first, fetching only missing, provisional, or explicitly refreshed history.</summary>
	/// <param name="request">The exact history window.</param>
	/// <param name="fetch">Cloud fetch invoked only when necessary.</param>
	/// <param name="force">Whether to replace even a settled cached period.</param>
	/// <param name="cancellationToken">Cancels waiting, database work, or the cloud request.</param>
	/// <param name="requireHomeContribution">Refreshes an older batch once when battery-to-home data is needed.</param>
	/// <returns>The batch and its provenance.</returns>
	internal async Task<EnergyHistoryResult> GetAsync (EnergyHistoryRequest request,
		Func<CancellationToken, Task<IReadOnlyList<EnergyHistoryPoint>>> fetch, bool force, CancellationToken cancellationToken, bool requireHomeContribution = false)
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnergyHistoryResult? cached = null;
			string? storageWarning = null;
			try
				{
				cached = await Task.Run (() => Read (request.Key), cancellationToken).ConfigureAwait (false);
				}
			catch (Exception exc) when (exc is SqliteException or IOException or UnauthorizedAccessException)
				{
				storageWarning = "Local history storage is unavailable; this result will not be cached.";
				}
			var now = _clock ();
			bool settled = cached is not null && cached.Points.Count > 0 && request.End.HasValue
				&& cached.RetrievedAt >= request.End.Value.AddDays (1);
			if (!force && cached is not null && (!requireHomeContribution || cached.IncludesHomeContribution) && (settled || (now >= cached.RetrievedAt && now - cached.RetrievedAt < TimeSpan.FromMinutes (5))))
				return cached;

			IReadOnlyList<EnergyHistoryPoint> points;
			try
				{
				points = await fetch (cancellationToken).ConfigureAwait (false);
				}
			catch (Exception exc) when (cached is not null && !cancellationToken.IsCancellationRequested
				&& exc is PowerwallException or System.Net.Http.HttpRequestException or InvalidOperationException or OperationCanceledException)
				{
				return cached with { Warning = "Cloud history is unavailable. Showing the previously saved result." };
				}
			cancellationToken.ThrowIfCancellationRequested ();
			var result = new EnergyHistoryResult (points.Select (StoredEnergyPoint.From).OrderBy (p => p.Timestamp).ToArray (), _clock (), false, storageWarning);
			// An empty response must not erase a previously successful batch or freeze a gap permanently.
			if (result.Points.Count == 0 && cached is not null && cached.Points.Count > 0)
				return cached with { Warning = "The cloud returned no samples. Showing the previously saved result." };
			if (storageWarning is null)
				{
				try
					{
					await Task.Run (() => Write (request.Key, result), cancellationToken).ConfigureAwait (false);
					}
				catch (Exception exc) when (exc is SqliteException or IOException or UnauthorizedAccessException)
					{
					result = result with { Warning = "History loaded, but could not be saved locally." };
					}
				}
			return result;
			}
		finally
			{
			_gate.Release ();
			}
		}

	private SqliteConnection Open ()
		{
		Directory.CreateDirectory (Path.GetDirectoryName (Path.GetFullPath (_path))!);
		var connection = new SqliteConnection (new SqliteConnectionStringBuilder
			{ DataSource = _path, Pooling = false, ForeignKeys = true }.ToString ());
		try
			{
			connection.Open ();
			using var command = connection.CreateCommand ();
			command.CommandText = """
				CREATE TABLE IF NOT EXISTS history_batches (id TEXT PRIMARY KEY, retrieved TEXT NOT NULL);
				CREATE TABLE IF NOT EXISTS history_points (
				 batch TEXT NOT NULL REFERENCES history_batches(id) ON DELETE CASCADE,
				 ordinal INTEGER NOT NULL, timestamp TEXT NOT NULL,
				 solar REAL NOT NULL, home REAL NOT NULL, grid_in REAL NOT NULL, grid_out REAL NOT NULL,
				 charge REAL NOT NULL, discharge REAL NOT NULL, PRIMARY KEY(batch, ordinal));
				CREATE TABLE IF NOT EXISTS history_contributions (
				 batch TEXT NOT NULL REFERENCES history_batches(id) ON DELETE CASCADE,
				 ordinal INTEGER NOT NULL, battery_home REAL, PRIMARY KEY(batch, ordinal));
				""";
			command.ExecuteNonQuery ();
			return connection;
			}
		catch
			{
			connection.Dispose ();
			throw;
			}
		}

	private EnergyHistoryResult? Read (string key)
		{
		using var connection = Open ();
		using var transaction = connection.BeginTransaction ();
		using var command = connection.CreateCommand ();
		command.Transaction = transaction;
		command.CommandText = "SELECT retrieved FROM history_batches WHERE id = $key";
		command.Parameters.AddWithValue ("$key", key);
		if (command.ExecuteScalar () is not string retrieved)
			return null;
		command.CommandText = "SELECT p.timestamp, p.solar, p.home, p.grid_in, p.grid_out, p.charge, p.discharge, c.ordinal, c.battery_home FROM history_points p LEFT JOIN history_contributions c ON c.batch = p.batch AND c.ordinal = p.ordinal WHERE p.batch = $key ORDER BY p.ordinal";
		using var reader = command.ExecuteReader ();
		var points = new List<StoredEnergyPoint> ();
		bool includesContribution = true;
		while (reader.Read ())
			{
			includesContribution &= !reader.IsDBNull (7);
			points.Add (new StoredEnergyPoint (DateTimeOffset.Parse (reader.GetString (0), CultureInfo.InvariantCulture),
				reader.GetDouble (1), reader.GetDouble (2), reader.GetDouble (3), reader.GetDouble (4), reader.GetDouble (5), reader.GetDouble (6), reader.IsDBNull (8) ? null : reader.GetDouble (8)));
			}
		return new EnergyHistoryResult (points, DateTimeOffset.Parse (retrieved, CultureInfo.InvariantCulture), true, IncludesHomeContribution: includesContribution);
		}

	private void Write (string key, EnergyHistoryResult result)
		{
		using var connection = Open ();
		using var transaction = connection.BeginTransaction ();
		using var command = connection.CreateCommand ();
		command.Transaction = transaction;
		command.CommandText = "DELETE FROM history_batches WHERE id = $key";
		command.Parameters.AddWithValue ("$key", key);
		command.ExecuteNonQuery ();
		command.CommandText = "INSERT INTO history_batches (id, retrieved) VALUES ($key, $retrieved)";
		command.Parameters.AddWithValue ("$retrieved", result.RetrievedAt.ToString ("O", CultureInfo.InvariantCulture));
		command.ExecuteNonQuery ();
		command.Parameters.Clear ();
		command.CommandText = "INSERT INTO history_points VALUES ($key, $ordinal, $timestamp, $solar, $home, $in, $out, $charge, $discharge)";
		int ordinal = 0;
		foreach (var point in result.Points)
			{
			command.Parameters.Clear ();
			command.Parameters.AddWithValue ("$key", key);
			command.Parameters.AddWithValue ("$ordinal", ordinal++);
			command.Parameters.AddWithValue ("$timestamp", point.Timestamp.ToString ("O", CultureInfo.InvariantCulture));
			command.Parameters.AddWithValue ("$solar", point.SolarKwh);
			command.Parameters.AddWithValue ("$home", point.HomeKwh);
			command.Parameters.AddWithValue ("$in", point.FromGridKwh);
			command.Parameters.AddWithValue ("$out", point.ToGridKwh);
			command.Parameters.AddWithValue ("$charge", point.BatteryChargeKwh);
			command.Parameters.AddWithValue ("$discharge", point.BatteryDischargeKwh);
			command.ExecuteNonQuery ();
			}
		command.CommandText = "INSERT INTO history_contributions VALUES ($key, $ordinal, $batteryHome)";
		ordinal = 0;
		foreach (var point in result.Points)
			{
			command.Parameters.Clear ();
			command.Parameters.AddWithValue ("$key", key);
			command.Parameters.AddWithValue ("$ordinal", ordinal++);
			command.Parameters.AddWithValue ("$batteryHome", (object?)point.BatteryToHomeKwh ?? DBNull.Value);
			command.ExecuteNonQuery ();
			}
		transaction.Commit ();
		}
	}
