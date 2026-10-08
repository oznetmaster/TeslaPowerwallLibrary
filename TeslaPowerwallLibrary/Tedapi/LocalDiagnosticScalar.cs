// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>The scalar representation supplied for a firmware-defined diagnostic field.</summary>
public enum LocalDiagnosticScalarKind
	{
	/// <summary>A text value, including firmware-defined names or timestamps.</summary>
	Text,
	/// <summary>A JSON number retained without rounding or an assumed unit.</summary>
	Number,
	/// <summary>A boolean value.</summary>
	Boolean
	}

/// <summary>A reported diagnostic scalar whose firmware-specific units or enum representation are not assumed.</summary>
/// <remarks>Null model properties mean unavailable. Numeric zero, false and empty text remain distinct values.
/// This type cannot contain objects, arrays or arbitrary JSON payloads.</remarks>
[JsonConverter (typeof (LocalDiagnosticScalarConverter))]
public sealed record LocalDiagnosticScalar
	{
	private readonly string _value;
	/// <summary>Creates an internally validated scalar from its reported representation.</summary>
	/// <param name="kind">Reported scalar kind.</param>
	/// <param name="value">Decoded text, numeric token, or normalized boolean.</param>
	internal LocalDiagnosticScalar (LocalDiagnosticScalarKind kind, string value) { Kind = kind; _value = value; }
	/// <summary>Gets the actual reported scalar kind.</summary>
	public LocalDiagnosticScalarKind Kind { get; }
	/// <summary>Gets decoded text only when the device supplied text; otherwise null.</summary>
	public string? Text => Kind == LocalDiagnosticScalarKind.Text ? _value : null;
	/// <summary>Gets the invariant numeric token without precision loss, only for a reported number.</summary>
	public string? NumberText => Kind == LocalDiagnosticScalarKind.Number ? _value : null;
	/// <summary>Gets a reported boolean, or null for a different scalar kind.</summary>
	public bool? Boolean => Kind == LocalDiagnosticScalarKind.Boolean ? _value == "true" : null;
	/// <summary>Attempts numeric conversion without treating numeric-looking text as a measurement.</summary>
	/// <param name="value">Converted decimal, or zero on failure; the return value indicates availability.</param>
	/// <returns>True only for a numeric scalar representable as a decimal.</returns>
	public bool TryGetDecimal (out decimal value)
		{ value = default; return Kind == LocalDiagnosticScalarKind.Number && decimal.TryParse (_value, NumberStyles.Float, CultureInfo.InvariantCulture, out value); }
	/// <summary>Attempts finite floating-point conversion without assuming units or converting text.</summary>
	/// <param name="value">Converted number on success, or zero on failure.</param>
	/// <returns>True only when the numeric scalar is representable as a finite double.</returns>
	public bool TryGetDouble (out double value)
		{
		value = default;
		if (Kind != LocalDiagnosticScalarKind.Number || !double.TryParse (_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
			|| double.IsInfinity (number) || double.IsNaN (number)) return false;
		value = number;
		return true;
		}
	/// <summary>Returns the reported scalar for display, without adding units or interpreting timestamps.</summary>
	/// <returns>Decoded text or the invariant number/boolean representation.</returns>
	public override string ToString () => _value;
	}

/// <summary>Preserves reported diagnostic scalar kinds and numeric precision with the default serializer.</summary>
internal sealed class LocalDiagnosticScalarConverter : JsonConverter<LocalDiagnosticScalar>
	{
	/// <inheritdoc/>
	public override LocalDiagnosticScalar Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
		switch (reader.TokenType)
			{
			case JsonTokenType.String: return new (LocalDiagnosticScalarKind.Text, reader.GetString ()!);
			case JsonTokenType.True: return new (LocalDiagnosticScalarKind.Boolean, "true");
			case JsonTokenType.False: return new (LocalDiagnosticScalarKind.Boolean, "false");
			case JsonTokenType.Number:
				using (JsonDocument token = JsonDocument.ParseValue (ref reader))
					return new (LocalDiagnosticScalarKind.Number, token.RootElement.GetRawText ());
			default: throw new JsonException ("Expected a diagnostic scalar, not a structured payload.");
			}
		}
	/// <inheritdoc/>
	public override void Write (Utf8JsonWriter writer, LocalDiagnosticScalar value, JsonSerializerOptions options)
		{
		if (value.Kind == LocalDiagnosticScalarKind.Text) writer.WriteStringValue (value.Text);
		else if (value.Kind == LocalDiagnosticScalarKind.Boolean) writer.WriteBooleanValue (value.Boolean!.Value);
		else writer.WriteRawValue (value.NumberText!);
		}
	}
