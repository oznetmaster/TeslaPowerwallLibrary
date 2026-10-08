// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Synthetic response contracts; these are not claims that a hardware diagnostic has been run.</summary>
[TestFixture]
public sealed class LocalExtendedDiagnosticsTests
	{
	/// <summary>Scalar kind and exact numeric spelling survive default-serializer round trips.</summary>
	/// <param name="json">Synthetic scalar token.</param>
	/// <param name="kind">Expected actual scalar kind.</param>
	[TestCase ("0", LocalDiagnosticScalarKind.Number)]
	[TestCase ("-0.00", LocalDiagnosticScalarKind.Number)]
	[TestCase ("1.234567890123456789012345678901e+120", LocalDiagnosticScalarKind.Number)]
	[TestCase ("1e400", LocalDiagnosticScalarKind.Number)]
	[TestCase ("18446744073709551615", LocalDiagnosticScalarKind.Number)]
	[TestCase ("false", LocalDiagnosticScalarKind.Boolean)]
	[TestCase ("true", LocalDiagnosticScalarKind.Boolean)]
	[TestCase ("\"0\"", LocalDiagnosticScalarKind.Text)]
	[TestCase ("\"\"", LocalDiagnosticScalarKind.Text)]
	public void Scalar_RetainsWireKindAndPrecision (string json, LocalDiagnosticScalarKind kind)
		{
		var scalar = JsonSerializer.Deserialize<LocalDiagnosticScalar> (json)!;
		Assert.That (scalar.Kind, Is.EqualTo (kind));
		Assert.That (JsonSerializer.Serialize (scalar), Is.EqualTo (json));
		Assert.That (scalar.Boolean.HasValue, Is.EqualTo (kind == LocalDiagnosticScalarKind.Boolean));
		Assert.That (scalar.Text is not null, Is.EqualTo (kind == LocalDiagnosticScalarKind.Text));
		Assert.That (scalar.NumberText is not null, Is.EqualTo (kind == LocalDiagnosticScalarKind.Number));
		}

	/// <summary>Absent diagnostics stay null and never become a synthetic zero, false or completed result.</summary>
	[Test]
	public void NullAndMissing_DoNotInventValues ()
		{
		Assert.That (JsonSerializer.Deserialize<LocalDiagnosticScalar> ("null"), Is.Null);
		var empty = JsonSerializer.Deserialize<LocalTelemetry> ("{}")!;
		Assert.That (empty.Ieee20305, Is.Null);
		var value = JsonSerializer.Deserialize<LocalTelemetry> ("""{"esCan":{"phaseDetection":{"inProgress":false,"powerwalls":[null,{}]},"inverterSelfTests":null},"ieee20305":{"controls":null}}""")!;
		Assert.That (value.EnergyBus!.PhaseDetection!.InProgress, Is.False);
		Assert.That (value.EnergyBus.PhaseDetection.Powerwalls![0], Is.Null);
		Assert.That (value.EnergyBus.PhaseDetection.Powerwalls[1]!.Progress, Is.Null);
		Assert.That (value.EnergyBus.InverterSelfTests, Is.Null);
		Assert.That (value.Ieee20305!.Controls, Is.Null);
		}

	/// <summary>Typed containers preserve object versus array, including empty arrays and null slots.</summary>
	/// <param name="json">Synthetic record container.</param>
	/// <param name="array">Whether the wire form is an array.</param>
	/// <param name="count">Expected record count including nulls.</param>
	[TestCase ("{}", false, 1)]
	[TestCase ("[]", true, 0)]
	[TestCase ("[null,{}]", true, 2)]
	public void Records_RetainShapeAndSlots (string json, bool array, int count)
		{
		var records = JsonSerializer.Deserialize<LocalDiagnosticRecords<LocalPhaseDetectionResult>> (json)!;
		Assert.That (records.WasReportedAsArray, Is.EqualTo (array));
		Assert.That (records.Count, Is.EqualTo (count));
		using var wire = JsonDocument.Parse (JsonSerializer.Serialize (records));
		Assert.That (wire.RootElement.ValueKind, Is.EqualTo (array ? JsonValueKind.Array : JsonValueKind.Object));
		if (count == 2) Assert.That (wire.RootElement[0].ValueKind, Is.EqualTo (JsonValueKind.Null));
		}

	/// <summary>Diagnostic values are not an arbitrary JSON payload escape hatch.</summary>
	/// <param name="json">Structured data that is not a scalar.</param>
	[TestCase ("{}"), TestCase ("[]")]
	public void Scalar_RejectsStructuredPayload (string json) => Assert.That (() => JsonSerializer.Deserialize<LocalDiagnosticScalar> (json), Throws.InstanceOf<JsonException> ());

	/// <summary>Invalid record shapes fail rather than being discarded or converted to empty records.</summary>
	/// <param name="json">Invalid record payload.</param>
	[TestCase ("1"), TestCase ("true"), TestCase ("[1]"), TestCase ("[[]]")]
	public void Records_RejectInvalidEntries (string json) => Assert.That (() => JsonSerializer.Deserialize<LocalDiagnosticRecords<LocalPhaseDetectionResult>> (json), Throws.InstanceOf<JsonException> ());

	/// <summary>Numeric conversion never treats text as a number and rejects non-finite double overflow.</summary>
	[Test]
	public void Scalar_ConversionsRequireActualNumbers ()
		{
		var zero = JsonSerializer.Deserialize<LocalDiagnosticScalar> ("0")!;
		Assert.That (zero.TryGetDecimal (out decimal numeric), Is.True); Assert.That (numeric, Is.Zero);
		var text = JsonSerializer.Deserialize<LocalDiagnosticScalar> ("\"0\"")!;
		Assert.That (text.TryGetDecimal (out _), Is.False);
		var huge = JsonSerializer.Deserialize<LocalDiagnosticScalar> ("1e400")!;
		Assert.That (huge.TryGetDouble (out double unavailable), Is.False); Assert.That (unavailable, Is.Zero);
		Assert.That (huge.TryGetDecimal (out _), Is.False);
		}

	/// <summary>Supplemental bus identities, timestamps and states retain their reported representations.</summary>
	[Test]
	public void AdditionalBusFields_PreserveZeroAndAvailability ()
		{
		const string json = """{"neurio":{"pairings":[{"shortId":0,"macAddress":"01:23:45:67:89:ab"}]},"esCan":{"bus":{"MSA":{"MSA_InfoMsg":{"MSA_appGitHash":[0,255],"MSA_assemblyId":0,"isMIA":false},"MSA_Status":{"lastRxTime":"2026-10-08T08:00:00Z"},"METER_Z_AcMeasurements":{"lastRxTime":0}},"SYNC":{"SYNC_Status":{"lastRxTime":null}},"POD":[{"alerts":{"active":[],"isComplete":true}}],"PVAC":[{"PVAC_ControlMeasurements":{"PVAC_FanSelfTestState":"not-run"}}]}}}""";
		var value = JsonSerializer.Deserialize<LocalTelemetry> (json)!;
		using var serialized = JsonDocument.Parse (JsonSerializer.Serialize (value));
		var root = serialized.RootElement;
		Assert.That (root.GetProperty ("neurio").GetProperty ("pairings")[0].GetProperty ("shortId").GetInt32 (), Is.Zero);
		Assert.That (root.GetProperty ("neurio").GetProperty ("pairings")[0].GetProperty ("macAddress").GetString (), Is.EqualTo ("01:23:45:67:89:ab"));
		var bus = root.GetProperty ("esCan").GetProperty ("bus");
		Assert.That (bus.GetProperty ("MSA").GetProperty ("MSA_InfoMsg").GetProperty ("MSA_assemblyId").GetInt32 (), Is.Zero);
		Assert.That (bus.GetProperty ("MSA").GetProperty ("MSA_InfoMsg").GetProperty ("MSA_appGitHash")[1].GetInt64 (), Is.EqualTo (255));
		Assert.That (bus.GetProperty ("MSA").GetProperty ("MSA_Status").GetProperty ("lastRxTime").GetString (), Is.EqualTo ("2026-10-08T08:00:00Z"));
		Assert.That (bus.GetProperty ("MSA").GetProperty ("METER_Z_AcMeasurements").GetProperty ("lastRxTime").GetInt32 (), Is.Zero);
		Assert.That (bus.GetProperty ("SYNC").GetProperty ("SYNC_Status").GetProperty ("lastRxTime").ValueKind, Is.EqualTo (JsonValueKind.Null));
		Assert.That (bus.GetProperty ("POD")[0].GetProperty ("alerts").GetProperty ("isComplete").GetBoolean (), Is.True);
		Assert.That (bus.GetProperty ("PVAC")[0].GetProperty ("PVAC_ControlMeasurements").GetProperty ("PVAC_FanSelfTestState").GetString (), Is.EqualTo ("not-run"));
		}

	/// <summary>Every newly mapped diagnostic branch survives serialization with its actual scalar representations.</summary>
	[Test]
	public void PopulatedMetadata_UsesAttributedModelsWithoutAssumedUnits ()
		{
		const string fixture = """
		{"esCan":{"phaseDetection":{"inProgress":false,"lastUpdateTimestamp":"2026-01-01T00:00:00Z","powerwalls":{"din":"synthetic","progress":0,"phase":"L1"}},
		"inverterSelfTests":{"isRunning":false,"isCanceled":true,"pinvSelfTestsResults":[{"din":"synthetic","overall":{"status":"unknown","test":7,"summary":"fixture","setMagnitude":1.234567890123456789,"setTime":"2s","tripMagnitude":0,"tripTime":null,"accuracyMagnitude":0,"accuracyTime":0,"currentMagnitude":0,"timestamp":0,"lastError":false},"testResults":[null,{"status":0}]}]},
		"firmwareUpdate":{"isUpdating":false,"powerwalls":[null],"msa":{"updating":false,"progress":0},"msa1":[null,{"numSteps":3}],"sync":[],"pvInverters":[{"currentStep":0,"currentStepProgress":0}]}},
		"ieee20305":{"longFormDeviceID":"synthetic-id","polledResources":[{"url":"https://example.invalid/resource","name":"fixture","pollRateSeconds":60,"lastPolledTimestamp":0}],"controls":{"defaultControl":{"mRID":"synthetic","setGradW":0,"opModEnergize":false,"opModMaxLimW":1,"opModImpLimW":2,"opModExpLimW":3,"opModGenLimW":4,"opModLoadLimW":5},"activeControls":[null,{"opModExpLimW":0}]},"registration":{"dateTimeRegistered":0,"pin":"synthetic-pin"}},
		"control":{"protectionTripTests":{"isRunning":false,"results":[{"testType":"synthetic","status":"not-run","timestamp":null,"mandatedTripThreshold":{"value":0,"unit":"V"},"observedTripTime":null}]}}}
		""";
		var result = JsonSerializer.Deserialize<LocalTelemetry> (fixture)!;
		var again = JsonSerializer.Deserialize<LocalTelemetry> (JsonSerializer.Serialize (result))!;
		Assert.That (again.EnergyBus!.InverterSelfTests!.Inverters![0]!.Overall![0]!.SetMagnitude!.NumberText, Is.EqualTo ("1.234567890123456789"));
		Assert.That (again.EnergyBus.PhaseDetection!.Powerwalls!.WasReportedAsArray, Is.False);
		Assert.That (again.EnergyBus.FirmwareUpdate!.MeterAssembly1![0], Is.Null);
		Assert.That (again.EnergyBus.FirmwareUpdate.SiteController, Is.Empty);
		Assert.That (again.Ieee20305!.Controls!.DefaultControl![0]!.Energize!.Boolean, Is.False);
		Assert.That (again.Ieee20305.Registration!.Pin!.Text, Is.EqualTo ("synthetic-pin"));
		Assert.That (again.Control!.ProtectionTripTests!.Results![0]!.MandatedTripThreshold![0]!.Value!.NumberText, Is.EqualTo ("0"));
		Assert.That (again.Control.ProtectionTripTests.Results[0]!.ObservedTripTime, Is.Null);
		}
	}
