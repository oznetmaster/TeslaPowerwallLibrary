// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Models;

namespace TeslaPowerwallLibrary;

/// <summary>
/// Abstract, async-first base class shared by all Powerwall access modes (local, cloud, FleetAPI, TEDAPI).
/// Mirrors the behavior of the Python <c>PyPowerwallBase</c> class while exposing idiomatic, cancellable
/// <see cref="Task"/>-returning members and strongly-typed results.
/// </summary>
public abstract class PowerwallClientBase
	{
	/// <summary>
	/// Maps a write API endpoint to the read cache keys that must be invalidated after a successful write.
	/// Mirrors the upstream <c>WRITE_OP_READ_OP_CACHE_MAP</c>.
	/// </summary>
	private static readonly Dictionary<string, string[]> _writeOpReadOpCacheMap =
		new Dictionary<string, string[]>
			{
			["/api/operation"] = ["/api/operation", "SITE_CONFIG"]
			};

	/// <summary>Holds the most recent cached response payloads keyed by API endpoint.</summary>
	protected Dictionary<string, string?> Cache { get; } = [];

	/// <summary>Cached bearer token, when token-based authentication is in use.</summary>
	protected string? Token { get; set; }

	/// <summary>Customer email associated with this client.</summary>
	public string Email { get; protected set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="PowerwallClientBase"/> class.
	/// </summary>
	/// <param name="email">Customer email associated with this client.</param>
	protected PowerwallClientBase (string email)
		{
		Email = email ?? throw new ArgumentNullException (nameof (email));
		}

	/// <summary>
	/// Authenticates with the gateway or cloud service and establishes a usable session.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>A task that completes when authentication succeeds.</returns>
	public abstract Task AuthenticateAsync (CancellationToken cancellationToken = default);

	/// <summary>
	/// Closes the active session and releases any associated server-side state.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>A task that completes when the session has been closed.</returns>
	public abstract Task CloseSessionAsync (CancellationToken cancellationToken = default);

	/// <summary>
	/// Queries the specified API endpoint and returns the raw text (typically JSON) response body.
	/// </summary>
	/// <param name="api">The API endpoint to query (for example <c>/api/meters/aggregates</c>).</param>
	/// <param name="force">When <see langword="true"/>, bypasses the cache and forces a fresh request.</param>
	/// <param name="recursive">Indicates a retry following a session refresh; prevents unbounded recursion.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The response body, or <see langword="null"/> when no payload is available.</returns>
	public abstract Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Queries the specified API endpoint and returns the raw binary response body (for example, protobuf vitals).
	/// </summary>
	/// <param name="api">The API endpoint to query (for example <c>/api/devices/vitals</c>).</param>
	/// <param name="force">When <see langword="true"/>, bypasses the cache and forces a fresh request.</param>
	/// <param name="recursive">Indicates a retry following a session refresh; prevents unbounded recursion.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The binary response body, or <see langword="null"/> when no payload is available.</returns>
	public abstract Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a command to the specified API endpoint.
	/// </summary>
	/// <param name="api">The API endpoint to post to (for example <c>/api/operation</c>).</param>
	/// <param name="payload">The payload to send; serialized as JSON.</param>
	/// <param name="din">System DIN, when required by the endpoint; ignored otherwise.</param>
	/// <param name="recursive">Indicates a retry following a session refresh; prevents unbounded recursion.</param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The response body, or <see langword="null"/> when no payload is available.</returns>
	public abstract Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves device vitals as a nested map of device name to that device's telemetry values.
	/// Vitals data is inherently dynamic, so values are returned as loosely-typed objects.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The vitals map, or <see langword="null"/> when vitals are unavailable.</returns>
	public abstract Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default);

	/// <summary>
	/// Computes the estimated backup time remaining on the battery, in hours.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The time remaining in hours, or <see langword="null"/> when it cannot be determined.</returns>
	public abstract Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the instantaneous power flows for site, solar, battery, and load.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>A <see cref="PowerSnapshot"/>; flows that cannot be parsed default to zero.</returns>
	public virtual async Task<PowerSnapshot> PowerAsync (CancellationToken cancellationToken = default)
		{
		var aggregates = await GetMeterAggregatesAsync (cancellationToken).ConfigureAwait (false);
		return new PowerSnapshot
			{
			Site = aggregates?.Site?.InstantPower ?? 0.0,
			Solar = aggregates?.Solar?.InstantPower ?? 0.0,
			Battery = aggregates?.Battery?.InstantPower ?? 0.0,
			Load = aggregates?.Load?.InstantPower ?? 0.0
			};
		}

	/// <summary>
	/// Returns the instantaneous power for a single sensor.
	/// </summary>
	/// <param name="sensor">The sensor to read: <c>site</c>, <c>solar</c>, <c>battery</c>, or <c>load</c>.</param>
	/// <param name="verbose">
	/// When <see langword="true"/>, reads the value directly from <c>/api/meters/aggregates</c>;
	/// otherwise reads from the cached <see cref="PowerAsync"/> result.
	/// </param>
	/// <param name="cancellationToken">Token used to cancel the operation.</param>
	/// <returns>The sensor power in watts, or <see langword="null"/> when the sensor is unknown.</returns>
	public virtual async Task<double?> FetchPowerAsync (string sensor, bool verbose = false, CancellationToken cancellationToken = default)
		{
#if NETFRAMEWORK
		if (sensor is null)
			throw new ArgumentNullException (nameof (sensor));
#else
		ArgumentNullException.ThrowIfNull (sensor);
#endif

		if (verbose)
			{
			var aggregates = await GetMeterAggregatesAsync (cancellationToken).ConfigureAwait (false);
			return SelectSensorReading (aggregates, sensor)?.InstantPower;
			}

		var power = await PowerAsync (cancellationToken).ConfigureAwait (false);
		return sensor switch
			{
			"site" => power.Site,
			"solar" => power.Solar,
			"battery" => power.Battery,
			"load" => power.Load,
			_ => null
			};
		}

	/// <summary>
	/// Invalidates the read caches associated with a writable endpoint after a successful write.
	/// </summary>
	/// <param name="api">The write endpoint that was invoked.</param>
	protected void InvalidateCache (string api)
		{
		if (_writeOpReadOpCacheMap.TryGetValue (api, out var cacheKeys))
			{
			foreach (var cacheKey in cacheKeys)
				Cache[cacheKey] = null;
			}
		}

	private async Task<MeterAggregates?> GetMeterAggregatesAsync (CancellationToken cancellationToken)
		{
		var payload = await PollAsync ("/api/meters/aggregates", cancellationToken: cancellationToken).ConfigureAwait (false);
		return JsonHelper.DeserializeOrNull<MeterAggregates> (payload);
		}

	private static MeterReading? SelectSensorReading (MeterAggregates? aggregates, string sensor) =>
		sensor switch
			{
			"site" => aggregates?.Site,
			"solar" => aggregates?.Solar,
			"battery" => aggregates?.Battery,
			"load" => aggregates?.Load,
			_ => null
			};
	}
