// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

using Google.Protobuf;

using TeslaPowerwallLibrary.Local;

using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Settings supported by signed local configuration updates.</summary>
/// <remarks>Null values leave settings unchanged. No arbitrary configuration paths are exposed.</remarks>
public sealed record LocalSettingsUpdate
	{
	/// <summary>Backup reserve on the Tesla app scale, from zero through 100 percent.</summary>
	[JsonPropertyName ("backup_reserve_percent"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? BackupReservePercent { get; init; }
	/// <summary>Operating mode: self_consumption, autonomous, or backup.</summary>
	[JsonPropertyName ("operation_mode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? OperationMode { get; init; }
	/// <summary>Whether charging from the grid is allowed.</summary>
	[JsonPropertyName ("grid_charging_enabled"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? GridChargingEnabled { get; init; }
	/// <summary>Export rule: battery_ok, pv_only, or never.</summary>
	[JsonPropertyName ("grid_export"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? GridExport { get; init; }
	}

/// <summary>The gateway's acknowledgement of an explicit local command.</summary>
/// <param name="Acknowledged">Whether the expected command response was received; this does not confirm physical actuation.</param>
public sealed record LocalCommandResult ([property: JsonPropertyName ("acknowledged")] bool Acknowledged);

public sealed partial class PowerwallTedapiClient
	{
	/// <summary>Changes only the requested local settings using a fresh configuration and optimistic locking.</summary>
	/// <remarks>
	/// Requires signed local access and AllowLocalControl. Unknown configuration fields are retained privately.
	/// Requests are not replayed after an uncertain write outcome; refresh settings before deciding whether to retry.
	/// Reserve values use the app scale and are converted to the gateway's five-percent internal reserve scale.
	/// </remarks>
	/// <param name="update">Explicit settings to change; null fields preserve the current configuration.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The command acknowledgement; an empty update sends no request and returns false.</returns>
	public async Task<LocalCommandResult> UpdateSettingsAsync (LocalSettingsUpdate update, CancellationToken cancellationToken = default)
		{
#if NETFRAMEWORK
		if (update is null)
			throw new ArgumentNullException (nameof (update));
#else
		ArgumentNullException.ThrowIfNull (update);
#endif
		ValidateSettings (update);
		RequireSignedControl ();
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			if (update.BackupReservePercent is null && update.OperationMode is null && update.GridChargingEnabled is null && update.GridExport is null)
				return new LocalCommandResult (false);

			Signed.MessageEnvelope read = CommandEnvelope ();
			read.Filestore = new Signed.FileStoreMessages
				{
				ReadFileRequest = new Signed.FileStoreAPIReadFileRequest
					{ Domain = Signed.FileStoreAPIDomain.ConfigJson, Name = "config.json" }
				};
			byte[] bytes = await ExchangeEnvelopeAsync (read.ToByteArray (), cancellationToken).ConfigureAwait (false);
			Signed.FileStoreAPIReadFileResponse? current = Signed.MessageEnvelope.Parser.ParseFrom (bytes).Filestore?.ReadFileResponse;
			if (current?.File?.Name != "config.json" || current.File.Blob.IsEmpty || current.Hash.IsEmpty)
				throw new PowerwallConnectionException ("The gateway did not return config.json with an optimistic-lock hash. No settings were written.");
			byte[] updated;
			try { updated = LocalConfigurationPatch.Apply (current.File.Blob.ToByteArray (), update); }
			catch (JsonException exc) { throw new PowerwallConnectionException ("The configuration could not be safely updated. No settings were written.", exc); }
			Signed.MessageEnvelope write = CommandEnvelope ();
			write.Filestore = new Signed.FileStoreMessages
				{
				UpdateFileRequest = new Signed.FileStoreAPIUpdateFileRequest
					{
					Domain = Signed.FileStoreAPIDomain.ConfigJson,
					Hash = current.Hash,
					File = new Signed.FileStoreAPIFile { Name = "config.json", Blob = ByteString.CopyFrom (updated) }
					}
				};
			try
				{
				byte[] response = await ExchangeEnvelopeAsync (write.ToByteArray (), cancellationToken, allowAuthenticationRetry: false).ConfigureAwait (false);
				Signed.MessageEnvelope reply = Signed.MessageEnvelope.Parser.ParseFrom (response);
				if (reply.Filestore?.UpdateFileResponse is null)
					throw new PowerwallConnectionException ("The gateway did not acknowledge the configuration update. Refresh settings before retrying.");
				return new LocalCommandResult (true);
				}
			finally
				{
				// A response can be lost after the gateway applies a write.
				_cache.Clear ();
				}
			}
		finally
			{
			_gate.Release ();
			}
		}

	private static void ValidateSettings (LocalSettingsUpdate update)
		{
		if (update.BackupReservePercent is double value && (double.IsNaN (value) || double.IsInfinity (value) || value < 0 || value > 100))
			throw new ArgumentOutOfRangeException (nameof (update), "Reserve must be between zero and 100 percent.");
		if (update.OperationMode is not (null or "self_consumption" or "autonomous" or "backup"))
			throw new ArgumentException ("Unsupported local operation mode.", nameof (update));
		if (update.GridExport is not (null or "battery_ok" or "pv_only" or "never"))
			throw new ArgumentException ("Unsupported local export rule.", nameof (update));
		}

	private void RequireSignedControl ()
		{
		if (!IsSigned || !_options.AllowLocalControl)
			throw new PowerwallNotSupportedException ("Local commands require signed LAN access and explicit AllowLocalControl.");
		}

	private Signed.MessageEnvelope CommandEnvelope () => new ()
		{
		DeliveryChannel = Signed.DeliveryChannel.HermesCommand,
		Sender = new Signed.Participant { AuthorizedClient = 1 },
		Recipient = new Signed.Participant { Din = _din! }
		};

	}
