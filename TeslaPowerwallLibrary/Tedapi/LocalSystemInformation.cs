// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;
using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Models;
using Google.Protobuf;
using Legacy = TeslaPowerwallLibrary.Tedapi.Protocol.Legacy;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Reported gateway firmware, hardware identity and update metadata.</summary>
public sealed record LocalSystemInformation
	{
	/// <summary>Gateway hardware identifiers.</summary>
	[JsonPropertyName ("gateway")]
	public LocalHardwareIdentity? Gateway { get; init; }
	/// <summary>Reported system device identifier.</summary>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }
	/// <summary>Installed firmware version and hexadecimal Git hash.</summary>
	[JsonPropertyName ("version")]
	public LocalFirmwareVersion? Version { get; init; }
	/// <summary>Native protobuf device-type number; zero denotes its default value.</summary>
	[JsonPropertyName ("deviceType")]
	public int DeviceType { get; init; }
	/// <summary>Read-only system update state, when the message is present.</summary>
	[JsonPropertyName ("update")]
	public LocalSystemUpdate? Update { get; init; }
	/// <summary>Reported radio compliance information.</summary>
	[JsonPropertyName ("wireless")]
	public IReadOnlyList<LocalRadioInformation>? Wireless { get; init; }
	}

/// <summary>A reported hardware package identity.</summary>
public sealed record LocalHardwareIdentity
	{
	/// <summary>Package part number.</summary>
	[JsonPropertyName ("partNumber")]
	public string? PartNumber { get; init; }
	/// <summary>Package serial number.</summary>
	[JsonPropertyName ("serialNumber")]
	public string? SerialNumber { get; init; }
	}

/// <summary>Reported radio compliance metadata.</summary>
public sealed record LocalRadioInformation
	{
	/// <summary>Radio manufacturer.</summary>
	[JsonPropertyName ("company")]
	public string? Company { get; init; }
	/// <summary>Radio model.</summary>
	[JsonPropertyName ("model")]
	public string? Model { get; init; }
	/// <summary>FCC identifier.</summary>
	[JsonPropertyName ("fccId")]
	public string? FccId { get; init; }
	/// <summary>Industry Canada identifier.</summary>
	[JsonPropertyName ("industryCanadaId")]
	public string? IndustryCanadaId { get; init; }
	}

/// <summary>Native protobuf update values; scalar defaults are retained when this message is present.</summary>
public sealed record LocalSystemUpdate
	{
	/// <summary>Native handshake result code.</summary>
	[JsonPropertyName ("handshakeResult")]
	public int HandshakeResult { get; init; }
	/// <summary>Native update status code.</summary>
	[JsonPropertyName ("status")]
	public int Status { get; init; }
	/// <summary>Server-staged firmware version.</summary>
	[JsonPropertyName ("stagedVersion")]
	public LocalFirmwareVersion? StagedVersion { get; init; }
	/// <summary>Reported total download bytes.</summary>
	[JsonPropertyName ("totalBytes")]
	public decimal TotalBytes { get; init; }
	/// <summary>Reported downloaded byte offset.</summary>
	[JsonPropertyName ("bytesOffset")]
	public decimal BytesOffset { get; init; }
	/// <summary>Estimated download bytes per second.</summary>
	[JsonPropertyName ("bytesPerSecond")]
	public decimal BytesPerSecond { get; init; }
	/// <summary>Native handshake timestamp.</summary>
	[JsonPropertyName ("lastHandshakeTimestamp")]
	public decimal LastHandshakeTimestamp { get; init; }
	/// <summary>Native last-update result code.</summary>
	[JsonPropertyName ("lastUpdateResult")]
	public int LastUpdateResult { get; init; }
	/// <summary>Whether the gateway reports sideloading.</summary>
	[JsonPropertyName ("isSideloading")]
	public bool IsSideloading { get; init; }
	/// <summary>Server-staged firmware packages.</summary>
	[JsonPropertyName ("packages")]
	public IReadOnlyList<LocalStagedFirmware>? Packages { get; init; }
	}

/// <summary>Reported staged firmware package metadata; reading it never installs firmware.</summary>
public sealed record LocalStagedFirmware
	{
	/// <summary>Reported package identifier.</summary>
	[JsonPropertyName ("packageId")]
	public decimal PackageId { get; init; }
	/// <summary>Device-provided download URL; may contain authorization data and should not be logged.</summary>
	[JsonPropertyName ("downloadUrl")]
	public string? DownloadUrl { get; init; }
	/// <summary>Reported package signature encoded as hexadecimal.</summary>
	[JsonPropertyName ("signatureHex")]
	public string? SignatureHex { get; init; }
	/// <summary>Staged package firmware version.</summary>
	[JsonPropertyName ("version")]
	public LocalFirmwareVersion? Version { get; init; }
	}

public sealed partial class PowerwallTedapiClient
	{
	/// <summary>Reads firmware and hardware details without starting an update.</summary>
	/// <param name="force">Bypasses cached information, but never device backoff.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Reported system information, preserving absent nested messages as null.</returns>
	public Task<LocalSystemInformation> GetSystemInformationAsync (bool force = false, CancellationToken cancellationToken = default) =>
		ReadWithFailoverAsync (recovering => GetPrimarySystemInformationAsync (force || recovering, cancellationToken),
			fallback => fallback.GetSystemInformationAsync (force, cancellationToken), cancellationToken);

	private async Task<LocalSystemInformation> GetPrimarySystemInformationAsync (bool force, CancellationToken cancellationToken)
		{
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			if (!force && _cache.TryGetValue ("systemInfo", out var cached) && _clock.Elapsed.TotalSeconds - cached.Time < CacheExpireSeconds)
				return (LocalSystemInformation)cached.Value;
			// Common getSystemInfo uses the same wire fields in both captured schema versions.
			var envelope = Envelope ();
			envelope.Firmware = new Legacy.FirmwareType { Request = string.Empty };
			byte[] response = await ExchangeAsync (envelope, cancellationToken).ConfigureAwait (false);
			var answer = BareEnvelope ? Legacy.MessageEnvelope.Parser.ParseFrom (response) : Legacy.Message.Parser.ParseFrom (response).Message_;
			var firmware = answer?.Firmware?.System ?? throw new PowerwallConnectionException ("The gateway did not return firmware information.");
			static string? Hex (ByteString value)
				{
#if NETFRAMEWORK
				return value.IsEmpty ? null : BitConverter.ToString (value.ToByteArray ()).Replace ("-", string.Empty).ToLowerInvariant ();
#else
				return value.IsEmpty ? null : Convert.ToHexStringLower (value.ToByteArray ());
#endif
				}
			static LocalFirmwareVersion? Version (Legacy.FirmwareVersion? value) => value is null ? null
				: new LocalFirmwareVersion { Version = value.Text, GitHash = Hex (value.Githash) };
			var update = firmware.SystemUpdate;
			var information = new LocalSystemInformation
				{
				Din = string.IsNullOrEmpty (firmware.Din) ? _din : firmware.Din,
				Gateway = firmware.Gateway is null ? null : new LocalHardwareIdentity { PartNumber = firmware.Gateway.PartNumber, SerialNumber = firmware.Gateway.SerialNumber },
				Version = Version (firmware.Version), DeviceType = firmware.DeviceType,
				Wireless = firmware.Wireless?.Device.Select (radio => new LocalRadioInformation
					{ Company = radio.Company?.Value, Model = radio.Model?.Value, FccId = radio.FccId?.Value, IndustryCanadaId = radio.Ic?.Value }).ToArray (),
				Update = update is null ? null : new LocalSystemUpdate
					{
					HandshakeResult = update.HandshakeResult, Status = update.UpdateStatus, StagedVersion = Version (update.ServerStagedVersion),
					TotalBytes = update.TotalBytes, BytesOffset = update.BytesOffset, BytesPerSecond = update.EstimatedBytesPerSecond,
					LastHandshakeTimestamp = update.LastHandshakeTimestamp, LastUpdateResult = update.LastUpdateResult, IsSideloading = update.IsSideloading,
					Packages = update.ServerStagedPackages.Select (package => new LocalStagedFirmware
						{ PackageId = package.PackageId, DownloadUrl = package.DownloadUrl, SignatureHex = Hex (package.PackageSignature), Version = Version (package.ServerStagedVersion) }).ToArray ()
					}
				};
			_cache["systemInfo"] = (information, _clock.Elapsed.TotalSeconds);
			return information;
			}
		catch (InvalidProtocolBufferException exc)
			{ throw new PowerwallConnectionException ("The gateway returned malformed system information.", exc); }
		finally { _gate.Release (); }
		}
	}
