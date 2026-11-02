// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Reported phase-detection status; reading it never starts detection.</summary>
public sealed record LocalPhaseDetection
	{
	/// <summary>Whether detection is running; null means unreported.</summary>
	/// <value>Whether detection is running; null means unreported.</value>
	[JsonPropertyName ("inProgress")]
	public bool? InProgress { get; init; }

	/// <summary>Reported update timestamp without an assumed epoch, unit or format.</summary>
	/// <value>Reported update timestamp without an assumed epoch, unit or format.</value>
	[JsonPropertyName ("lastUpdateTimestamp")]
	public LocalDiagnosticScalar? LastUpdateTimestamp { get; init; }

	/// <summary>Per-Powerwall records, preserving single/array shape and null slots.</summary>
	/// <value>Per-Powerwall records, preserving single/array shape and null slots.</value>
	[JsonPropertyName ("powerwalls")]
	public LocalDiagnosticRecords<LocalPhaseDetectionResult>? Powerwalls { get; init; }

	}

/// <summary>A reported Powerwall phase-detection result without an inferred phase topology.</summary>
public sealed record LocalPhaseDetectionResult
	{
	/// <summary>Reported device identification number.</summary>
	/// <value>Reported device identification number.</value>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }

	/// <summary>Progress in the representation and scale supplied by the firmware.</summary>
	/// <value>Progress in the representation and scale supplied by the firmware.</value>
	[JsonPropertyName ("progress")]
	public LocalDiagnosticScalar? Progress { get; init; }

	/// <summary>Reported phase designation; no phase numbering scheme is imposed.</summary>
	/// <value>Reported phase designation; no phase numbering scheme is imposed.</value>
	[JsonPropertyName ("phase")]
	public LocalDiagnosticScalar? Phase { get; init; }

	}

/// <summary>Read-only inverter self-test state and stored results.</summary>
public sealed record LocalInverterSelfTests
	{
	/// <summary>Whether a self-test is running; null means unreported.</summary>
	/// <value>Whether a self-test is running; null means unreported.</value>
	[JsonPropertyName ("isRunning")]
	public bool? IsRunning { get; init; }

	/// <summary>Whether the device reports cancellation; null means unreported.</summary>
	/// <value>Whether the device reports cancellation; null means unreported.</value>
	[JsonPropertyName ("isCanceled")]
	public bool? IsCanceled { get; init; }

	/// <summary>Inverter result records in their reported container and order.</summary>
	/// <value>Inverter result records in their reported container and order.</value>
	[JsonPropertyName ("pinvSelfTestsResults")]
	public LocalDiagnosticRecords<LocalInverterSelfTestResults>? Inverters { get; init; }

	}

/// <summary>Stored self-test results for an identified inverter.</summary>
public sealed record LocalInverterSelfTestResults
	{
	/// <summary>Reported inverter identification number.</summary>
	/// <value>Reported inverter identification number.</value>
	[JsonPropertyName ("din")]
	public string? Din { get; init; }

	/// <summary>Overall results without assuming a single-record or list encoding.</summary>
	/// <value>Overall results without assuming a single-record or list encoding.</value>
	[JsonPropertyName ("overall")]
	public LocalDiagnosticRecords<LocalInverterSelfTestResult>? Overall { get; init; }

	/// <summary>Individual test results, retaining missing slots and order.</summary>
	/// <value>Individual test results, retaining missing slots and order.</value>
	[JsonPropertyName ("testResults")]
	public LocalDiagnosticRecords<LocalInverterSelfTestResult>? Tests { get; init; }

	}

/// <summary>Firmware-defined self-test values; scalar kind and numeric precision are retained.</summary>
public sealed record LocalInverterSelfTestResult
	{
	/// <summary>Firmware-defined result status.</summary>
	/// <value>Firmware-defined result status.</value>
	[JsonPropertyName ("status")]
	public LocalDiagnosticScalar? Status { get; init; }

	/// <summary>Firmware-defined test identifier.</summary>
	/// <value>Firmware-defined test identifier.</value>
	[JsonPropertyName ("test")]
	public LocalDiagnosticScalar? Test { get; init; }

	/// <summary>Reported summary without generating a success or failure conclusion.</summary>
	/// <value>Reported summary without generating a success or failure conclusion.</value>
	[JsonPropertyName ("summary")]
	public LocalDiagnosticScalar? Summary { get; init; }

	/// <summary>Configured magnitude in the reported representation; units are not assumed.</summary>
	/// <value>Configured magnitude in the reported representation; units are not assumed.</value>
	[JsonPropertyName ("setMagnitude")]
	public LocalDiagnosticScalar? SetMagnitude { get; init; }

	/// <summary>Configured time in the reported representation; units are not assumed.</summary>
	/// <value>Configured time in the reported representation; units are not assumed.</value>
	[JsonPropertyName ("setTime")]
	public LocalDiagnosticScalar? SetTime { get; init; }

	/// <summary>Reported trip magnitude; absent measurements remain null.</summary>
	/// <value>Reported trip magnitude; absent measurements remain null.</value>
	[JsonPropertyName ("tripMagnitude")]
	public LocalDiagnosticScalar? TripMagnitude { get; init; }

	/// <summary>Reported trip time; units are not assumed.</summary>
	/// <value>Reported trip time; units are not assumed.</value>
	[JsonPropertyName ("tripTime")]
	public LocalDiagnosticScalar? TripTime { get; init; }

	/// <summary>Reported magnitude accuracy without an inferred percentage scale.</summary>
	/// <value>Reported magnitude accuracy without an inferred percentage scale.</value>
	[JsonPropertyName ("accuracyMagnitude")]
	public LocalDiagnosticScalar? AccuracyMagnitude { get; init; }

	/// <summary>Reported time accuracy without an inferred unit.</summary>
	/// <value>Reported time accuracy without an inferred unit.</value>
	[JsonPropertyName ("accuracyTime")]
	public LocalDiagnosticScalar? AccuracyTime { get; init; }

	/// <summary>Reported current magnitude in the firmware-defined representation.</summary>
	/// <value>Reported current magnitude in the firmware-defined representation.</value>
	[JsonPropertyName ("currentMagnitude")]
	public LocalDiagnosticScalar? CurrentMagnitude { get; init; }

	/// <summary>Reported timestamp without an inferred epoch or timezone.</summary>
	/// <value>Reported timestamp without an inferred epoch or timezone.</value>
	[JsonPropertyName ("timestamp")]
	public LocalDiagnosticScalar? Timestamp { get; init; }

	/// <summary>Reported error scalar; missing data is not treated as a successful test.</summary>
	/// <value>Reported error scalar; missing data is not treated as a successful test.</value>
	[JsonPropertyName ("lastError")]
	public LocalDiagnosticScalar? LastError { get; init; }

	}

/// <summary>Read-only IEEE 2030.5 service metadata supplied by the controller.</summary>
public sealed record LocalIeee20305Telemetry
	{
	/// <summary>Reported long-form device identifier without numeric coercion.</summary>
	/// <value>Reported long-form device identifier without numeric coercion.</value>
	[JsonPropertyName ("longFormDeviceID")]
	public LocalDiagnosticScalar? LongFormDeviceId { get; init; }

	/// <summary>Resource polling records, preserving the reported container.</summary>
	/// <value>Resource polling records, preserving the reported container.</value>
	[JsonPropertyName ("polledResources")]
	public LocalDiagnosticRecords<LocalIeee20305Resource>? PolledResources { get; init; }

	/// <summary>Reported default and active control metadata; reading does not apply controls.</summary>
	/// <value>Reported default and active control metadata; reading does not apply controls.</value>
	[JsonPropertyName ("controls")]
	public LocalIeee20305Controls? Controls { get; init; }

	/// <summary>Reported service registration metadata; may contain a provisioning PIN.</summary>
	/// <value>Reported service registration metadata; may contain a provisioning PIN.</value>
	[JsonPropertyName ("registration")]
	public LocalIeee20305Registration? Registration { get; init; }

	}

/// <summary>A resource polled by the device IEEE 2030.5 service.</summary>
public sealed record LocalIeee20305Resource
	{
	/// <summary>Reported resource URL, without requesting it.</summary>
	/// <value>Reported resource URL, without requesting it.</value>
	[JsonPropertyName ("url")]
	public string? Url { get; init; }

	/// <summary>Reported resource name.</summary>
	/// <value>Reported resource name.</value>
	[JsonPropertyName ("name")]
	public LocalDiagnosticScalar? Name { get; init; }

	/// <summary>Device-reported polling interval, labelled in seconds by the upstream field.</summary>
	/// <value>Device-reported polling interval, labelled in seconds by the upstream field.</value>
	[JsonPropertyName ("pollRateSeconds")]
	public LocalDiagnosticScalar? PollRateSeconds { get; init; }

	/// <summary>Reported last-poll timestamp without an inferred epoch or format.</summary>
	/// <value>Reported last-poll timestamp without an inferred epoch or format.</value>
	[JsonPropertyName ("lastPolledTimestamp")]
	public LocalDiagnosticScalar? LastPolledTimestamp { get; init; }

	}

/// <summary>Stored IEEE 2030.5 control metadata; no control command is sent when reading it.</summary>
public sealed record LocalIeee20305Controls
	{
	/// <summary>Default control records in the reported shape.</summary>
	/// <value>Default control records in the reported shape.</value>
	[JsonPropertyName ("defaultControl")]
	public LocalDiagnosticRecords<LocalIeee20305Control>? DefaultControl { get; init; }

	/// <summary>Active control records in the reported shape and order.</summary>
	/// <value>Active control records in the reported shape and order.</value>
	[JsonPropertyName ("activeControls")]
	public LocalDiagnosticRecords<LocalIeee20305Control>? ActiveControls { get; init; }

	}

/// <summary>IEEE 2030.5 control fields retained in their reported representation without unit conversion.</summary>
public sealed record LocalIeee20305Control
	{
	/// <summary>Reported resource identifier.</summary>
	/// <value>Reported resource identifier.</value>
	[JsonPropertyName ("mRID")]
	public LocalDiagnosticScalar? ResourceId { get; init; }

	/// <summary>Reported setGradW value; no rate or multiplier is inferred.</summary>
	/// <value>Reported setGradW value; no rate or multiplier is inferred.</value>
	[JsonPropertyName ("setGradW")]
	public LocalDiagnosticScalar? SetGradient { get; init; }

	/// <summary>Reported energize control.</summary>
	/// <value>Reported energize control.</value>
	[JsonPropertyName ("opModEnergize")]
	public LocalDiagnosticScalar? Energize { get; init; }

	/// <summary>Reported maximum-power limit.</summary>
	/// <value>Reported maximum-power limit.</value>
	[JsonPropertyName ("opModMaxLimW")]
	public LocalDiagnosticScalar? MaximumPowerLimit { get; init; }

	/// <summary>Reported import limit.</summary>
	/// <value>Reported import limit.</value>
	[JsonPropertyName ("opModImpLimW")]
	public LocalDiagnosticScalar? ImportLimit { get; init; }

	/// <summary>Reported export limit.</summary>
	/// <value>Reported export limit.</value>
	[JsonPropertyName ("opModExpLimW")]
	public LocalDiagnosticScalar? ExportLimit { get; init; }

	/// <summary>Reported generation limit.</summary>
	/// <value>Reported generation limit.</value>
	[JsonPropertyName ("opModGenLimW")]
	public LocalDiagnosticScalar? GenerationLimit { get; init; }

	/// <summary>Reported load limit.</summary>
	/// <value>Reported load limit.</value>
	[JsonPropertyName ("opModLoadLimW")]
	public LocalDiagnosticScalar? LoadLimit { get; init; }

	}

/// <summary>Service registration metadata; consumers should avoid logging provisioning information.</summary>
public sealed record LocalIeee20305Registration
	{
	/// <summary>Reported registration date/time without an inferred format.</summary>
	/// <value>Reported registration date/time without an inferred format.</value>
	[JsonPropertyName ("dateTimeRegistered")]
	public LocalDiagnosticScalar? RegistrationTime { get; init; }

	/// <summary>Reported registration PIN; treat as provisioning information, not general diagnostic output.</summary>
	/// <value>Reported registration PIN; treat as provisioning information, not general diagnostic output.</value>
	[JsonPropertyName ("pin")]
	public LocalDiagnosticScalar? Pin { get; init; }

	}

/// <summary>Stored protection-test result. Reading it never starts, cancels or changes a test.</summary>
public sealed record LocalProtectionTestResult
	{
	/// <summary>Reported test type.</summary>
	/// <value>Reported test type.</value>
	[JsonPropertyName ("testType")]
	public LocalDiagnosticScalar? TestType { get; init; }

	/// <summary>Reported status without interpreting an absent measurement as a pass.</summary>
	/// <value>Reported status without interpreting an absent measurement as a pass.</value>
	[JsonPropertyName ("status")]
	public LocalDiagnosticScalar? Status { get; init; }

	/// <summary>Reported result timestamp without an assumed time format.</summary>
	/// <value>Reported result timestamp without an assumed time format.</value>
	[JsonPropertyName ("timestamp")]
	public LocalDiagnosticScalar? Timestamp { get; init; }

	/// <summary>Mandated trip threshold with its reported unit.</summary>
	/// <value>Mandated trip threshold with its reported unit.</value>
	[JsonPropertyName ("mandatedTripThreshold")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? MandatedTripThreshold { get; init; }

	/// <summary>Mandated trip time with its reported unit.</summary>
	/// <value>Mandated trip time with its reported unit.</value>
	[JsonPropertyName ("mandatedTripTime")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? MandatedTripTime { get; init; }

	/// <summary>Reported ramp step size.</summary>
	/// <value>Reported ramp step size.</value>
	[JsonPropertyName ("rampStepSize")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? RampStepSize { get; init; }

	/// <summary>Reported ramp interval.</summary>
	/// <value>Reported ramp interval.</value>
	[JsonPropertyName ("rampInterval")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? RampInterval { get; init; }

	/// <summary>Reported permitted threshold deviation.</summary>
	/// <value>Reported permitted threshold deviation.</value>
	[JsonPropertyName ("tripThresholdDeviationMax")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? MaximumTripThresholdDeviation { get; init; }

	/// <summary>Reported permitted trip-time deviation.</summary>
	/// <value>Reported permitted trip-time deviation.</value>
	[JsonPropertyName ("tripTimeDeviationMax")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? MaximumTripTimeDeviation { get; init; }

	/// <summary>Observed threshold; null means no reported measurement.</summary>
	/// <value>Observed threshold; null means no reported measurement.</value>
	[JsonPropertyName ("observedTripThreshold")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? ObservedTripThreshold { get; init; }

	/// <summary>Observed trip time.</summary>
	/// <value>Observed trip time.</value>
	[JsonPropertyName ("observedTripTime")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? ObservedTripTime { get; init; }

	/// <summary>Reported measurement at the trip point.</summary>
	/// <value>Reported measurement at the trip point.</value>
	[JsonPropertyName ("observedMeasurementAtTrip")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? ObservedMeasurementAtTrip { get; init; }

	/// <summary>Observed threshold deviation.</summary>
	/// <value>Observed threshold deviation.</value>
	[JsonPropertyName ("observedTripThresholdDeviation")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? ObservedTripThresholdDeviation { get; init; }

	/// <summary>Observed time deviation.</summary>
	/// <value>Observed time deviation.</value>
	[JsonPropertyName ("observedTripTimeDeviation")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? ObservedTripTimeDeviation { get; init; }

	/// <summary>Reported threshold accuracy.</summary>
	/// <value>Reported threshold accuracy.</value>
	[JsonPropertyName ("tripThresholdAccuracy")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? TripThresholdAccuracy { get; init; }

	/// <summary>Reported trip-time accuracy.</summary>
	/// <value>Reported trip-time accuracy.</value>
	[JsonPropertyName ("tripTimeAccuracy")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? TripTimeAccuracy { get; init; }

	/// <summary>Reported measurement accuracy.</summary>
	/// <value>Reported measurement accuracy.</value>
	[JsonPropertyName ("measurementAccuracy")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? MeasurementAccuracy { get; init; }

	/// <summary>Reported measurement-time accuracy.</summary>
	/// <value>Reported measurement-time accuracy.</value>
	[JsonPropertyName ("measurementTimeAccuracy")]
	public LocalDiagnosticRecords<LocalDiagnosticQuantity>? MeasurementTimeAccuracy { get; init; }

	}

/// <summary>A diagnostic value and the unit explicitly reported alongside it.</summary>
public sealed record LocalDiagnosticQuantity
	{
	/// <summary>Reported value without rounding or a default for missing data.</summary>
	/// <value>Reported value without rounding or a default for missing data.</value>
	[JsonPropertyName ("value")]
	public LocalDiagnosticScalar? Value { get; init; }

	/// <summary>Reported unit identifier; no conversion or fallback unit is imposed.</summary>
	/// <value>Reported unit identifier; no conversion or fallback unit is imposed.</value>
	[JsonPropertyName ("unit")]
	public LocalDiagnosticScalar? Unit { get; init; }

	}
