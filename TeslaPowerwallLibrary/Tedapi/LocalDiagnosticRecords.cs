// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Named diagnostic records reported as either one object or an ordered array.</summary>
/// <typeparam name="T">The attributed model for each record.</typeparam>
/// <remarks>No device count or fixed slot count is inferred. Null array slots and the original container shape are preserved.</remarks>
[JsonConverter (typeof (LocalDiagnosticRecordsConverter))]
public sealed class LocalDiagnosticRecords<T> : IReadOnlyList<T?> where T : class
	{
	private readonly System.Collections.ObjectModel.ReadOnlyCollection<T?> _items;
	/// <summary>Creates a record container from deserialized, ordered entries.</summary>
	/// <param name="items">Entries, including any null slots.</param>
	/// <param name="array">Whether the device supplied an array.</param>
	internal LocalDiagnosticRecords (IEnumerable<T?> items, bool array) { _items = Array.AsReadOnly (items.ToArray ()); WasReportedAsArray = array; }
	/// <summary>Gets whether the device supplied an array rather than a single object.</summary>
	public bool WasReportedAsArray { get; }
	/// <summary>Gets the number of entries, including null slots.</summary>
	public int Count => _items.Count;
	/// <summary>Gets an entry by its original position; null is not replaced by an empty record.</summary>
	/// <param name="index">Zero-based reported slot.</param>
	/// <returns>The typed record or null.</returns>
	public T? this[int index] => _items[index];
	/// <summary>Enumerates entries without filtering empty slots.</summary>
	/// <returns>The ordered enumerator.</returns>
	public IEnumerator<T?> GetEnumerator () => _items.GetEnumerator ();
	IEnumerator IEnumerable.GetEnumerator () => GetEnumerator ();
	}

/// <summary>Creates shape-preserving converters for attributed diagnostic record containers.</summary>
internal sealed class LocalDiagnosticRecordsConverter : JsonConverterFactory
	{
	/// <inheritdoc/>
	public override bool CanConvert (Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition () == typeof (LocalDiagnosticRecords<>);
	/// <inheritdoc/>
	public override JsonConverter CreateConverter (Type typeToConvert, JsonSerializerOptions options) =>
		(JsonConverter)Activator.CreateInstance (typeof (RecordsConverter<>).MakeGenericType (typeToConvert.GetGenericArguments ()), nonPublic: true)!;
	private sealed class RecordsConverter<T> : JsonConverter<LocalDiagnosticRecords<T>> where T : class
		{
		/// <inheritdoc/>
		public override LocalDiagnosticRecords<T> Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
			if (reader.TokenType == JsonTokenType.StartObject)
				return new (new[] { JsonSerializer.Deserialize<T> (ref reader, options) }, array: false);
			if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException ("Expected diagnostic records as an object or an array.");
			var items = new List<T?> ();
			while (reader.Read () && reader.TokenType != JsonTokenType.EndArray)
				{
				if (reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.Null)) throw new JsonException ("Expected a diagnostic record or null array slot.");
				items.Add (JsonSerializer.Deserialize<T> (ref reader, options));
				}
			if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException ("Incomplete diagnostic array.");
			return new (items, array: true);
			}
		/// <inheritdoc/>
		public override void Write (Utf8JsonWriter writer, LocalDiagnosticRecords<T> value, JsonSerializerOptions options)
			{
			if (!value.WasReportedAsArray) { JsonSerializer.Serialize (writer, value[0], options); return; }
			writer.WriteStartArray ();
			foreach (T? item in value) JsonSerializer.Serialize (writer, item, options);
			writer.WriteEndArray ();
			}
		}
	}
