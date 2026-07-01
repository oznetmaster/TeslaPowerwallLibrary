// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>
/// TEDAPI mode client used for local link-local access to the Tesla Energy Gateway. This is a scaffold
/// for a future milestone; TEDAPI uses protobuf-encoded payloads (via Google.Protobuf) and is not yet
/// implemented, so its members throw <see cref="PowerwallTedapiNotImplementedException"/>.
/// </summary>
public sealed class PowerwallTedapiClient : PowerwallClientBase
	{
	/// <summary>
	/// Initializes a new instance of the <see cref="PowerwallTedapiClient"/> class.
	/// </summary>
	/// <param name="gatewayPassword">Full gateway password from the QR sticker used for TEDAPI access.</param>
	/// <param name="cacheExpireSeconds">Number of seconds before cached responses expire.</param>
	/// <param name="timeout">Per-request HTTP timeout.</param>
	/// <param name="host">Gateway host; defaults to the link-local <see cref="Constants.GW_IP"/> when empty.</param>
	public PowerwallTedapiClient (
		string gatewayPassword,
		int cacheExpireSeconds,
		TimeSpan timeout,
		string host = Constants.GW_IP)
		: base (Constants.DEFAULT_EMAIL)
		{
		if (string.IsNullOrWhiteSpace (gatewayPassword))
			throw new ArgumentException ("Gateway password is required for TEDAPI mode.", nameof (gatewayPassword));

		GatewayPassword = gatewayPassword;
		CacheExpireSeconds = cacheExpireSeconds;
		Timeout = timeout;
		Host = string.IsNullOrWhiteSpace (host) ? Constants.GW_IP : host;
		}

	/// <summary>Gets the full gateway password used for TEDAPI authentication.</summary>
	public string GatewayPassword { get; }

	/// <summary>Gets the gateway host used for TEDAPI access.</summary>
	public string Host { get; }

	/// <summary>Gets the number of seconds before cached responses expire.</summary>
	public int CacheExpireSeconds { get; }

	/// <summary>Gets the per-request HTTP timeout.</summary>
	public TimeSpan Timeout { get; }

	/// <inheritdoc/>
	public override Task AuthenticateAsync (CancellationToken cancellationToken = default) =>
		throw new PowerwallTedapiNotImplementedException ("TEDAPI mode is not yet implemented in this version of the library.");

	/// <inheritdoc/>
	public override Task CloseSessionAsync (CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	/// <inheritdoc/>
	public override Task<string?> PollAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) =>
		throw new PowerwallTedapiNotImplementedException ("TEDAPI mode is not yet implemented in this version of the library.");

	/// <inheritdoc/>
	public override Task<byte[]?> PollRawAsync (string api, bool force = false, bool recursive = false, CancellationToken cancellationToken = default) =>
		throw new PowerwallTedapiNotImplementedException ("TEDAPI mode is not yet implemented in this version of the library.");

	/// <inheritdoc/>
	public override Task<string?> PostAsync (string api, object? payload, string? din = null, bool recursive = false, CancellationToken cancellationToken = default) =>
		throw new PowerwallTedapiNotImplementedException ("TEDAPI mode is not yet implemented in this version of the library.");

	/// <inheritdoc/>
	public override Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>?> VitalsAsync (CancellationToken cancellationToken = default) =>
		throw new PowerwallTedapiNotImplementedException ("TEDAPI mode is not yet implemented in this version of the library.");

	/// <inheritdoc/>
	public override Task<double?> GetTimeRemainingAsync (CancellationToken cancellationToken = default) =>
		throw new PowerwallTedapiNotImplementedException ("TEDAPI mode is not yet implemented in this version of the library.");
	}
