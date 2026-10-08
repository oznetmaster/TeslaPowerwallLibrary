// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Native PVAC firmware identifiers, retaining numeric hash elements in wire order.</summary>
public sealed record LocalPvacFirmware : LocalBusMessage
	{
	/// <summary>Firmware hash elements exactly as reported; not reinterpreted as text.</summary>
	[JsonPropertyName ("PVAC_appGitHash")]
	public IReadOnlyList<long>? ApplicationGitHash { get; init; }

	}

/// <summary>Native THC firmware identifiers, retaining numeric hash elements in wire order.</summary>
public sealed record LocalThcFirmware : LocalBusMessage
	{
	/// <summary>Firmware hash elements exactly as reported; not reinterpreted as text.</summary>
	[JsonPropertyName ("THC_appGitHash")]
	public IReadOnlyList<long>? ApplicationGitHash { get; init; }

	}

/// <summary>Native POD firmware identifiers, retaining numeric hash elements in wire order.</summary>
public sealed record LocalPodFirmware : LocalBusMessage
	{
	/// <summary>Firmware hash elements exactly as reported; not reinterpreted as text.</summary>
	[JsonPropertyName ("POD_appGitHash")]
	public IReadOnlyList<long>? ApplicationGitHash { get; init; }

	}

/// <summary>Native SYNC firmware identifiers, retaining numeric hash elements in wire order.</summary>
public sealed record LocalSyncFirmware : LocalBusMessage
	{
	/// <summary>Firmware hash elements exactly as reported; not reinterpreted as text.</summary>
	[JsonPropertyName ("SYNC_appGitHash")]
	public IReadOnlyList<long>? ApplicationGitHash { get; init; }

	/// <summary>Native assembly identifier.</summary>
	[JsonPropertyName ("SYNC_assemblyId")]
	public long? AssemblyId { get; init; }

	}

/// <summary>PV string-controller diagnostic flags.</summary>
public sealed record LocalSolarStringsLogging : LocalBusMessage
	{
	/// <summary>Reported string lockout bit field; bit meanings remain firmware-defined.</summary>
	[JsonPropertyName ("PVS_numStringsLockoutBits")]
	public long? StringLockoutBits { get; init; }

	/// <summary>Native SBS completion flag, without inferring connected strings.</summary>
	[JsonPropertyName ("PVS_sbsComplete")]
	public bool? SbsComplete { get; init; }

	}

/// <summary>Legacy thermal-controller enable-line diagnostics.</summary>
public sealed record LocalThermalLogging : LocalBusMessage
	{
	/// <summary>Firmware-provided enable-line state.</summary>
	[JsonPropertyName ("THC_LOG_PW_2_0_EnableLineState")]
	public string? EnableLineState { get; init; }

	}

/// <summary>Legacy energy-bus update state. Reading this model never starts an update.</summary>
public sealed record LocalLegacyFirmwareUpdate
	{
	/// <summary>Whether the bus reports an update in progress; null means unreported.</summary>
	[JsonPropertyName ("isUpdating")]
	public bool? IsUpdating { get; init; }

	/// <summary>Reported msa firmware progress, preserving object/array encoding and null slots.</summary>
	[JsonPropertyName ("msa")]
	public LocalDiagnosticRecords<LocalUpdateProgress>? MeterAssembly { get; init; }

	/// <summary>Reported msa1 firmware progress, preserving object/array encoding and null slots.</summary>
	[JsonPropertyName ("msa1")]
	public LocalDiagnosticRecords<LocalUpdateProgress>? MeterAssembly1 { get; init; }

	/// <summary>Reported sync firmware progress, preserving object/array encoding and null slots.</summary>
	[JsonPropertyName ("sync")]
	public LocalDiagnosticRecords<LocalUpdateProgress>? SiteController { get; init; }

	/// <summary>Reported pvInverters firmware progress, preserving object/array encoding and null slots.</summary>
	[JsonPropertyName ("pvInverters")]
	public LocalDiagnosticRecords<LocalUpdateProgress>? SolarInverters { get; init; }

	/// <summary>Powerwall update slots in device order. Null slots are retained and do not imply completed updates.</summary>
	[JsonPropertyName ("powerwalls")]
	public IReadOnlyList<LocalUpdateProgress?>? Powerwalls { get; init; }
	}
