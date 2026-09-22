using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary;

internal static class JsonHelper
	{
	internal static JsonSerializerOptions Options { get; } = CreateOptions (false);
	private static JsonSerializerOptions IndentedOptions { get; } = CreateOptions (true);

	private static JsonSerializerOptions CreateOptions (bool indented)
		{
		var options = new JsonSerializerOptions
			{
			PropertyNameCaseInsensitive = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString,
			WriteIndented = indented
			};
		options.Converters.Add (new PlainObjectConverter ());
		options.Converters.Add (new ScalarStringConverter ());
		return options;
		}

	internal static T? Deserialize<T> (string json) => JsonSerializer.Deserialize<T> (json, Options);
	internal static string Serialize<T> (T value, bool indented = false) => JsonSerializer.Serialize (value, indented ? IndentedOptions : Options);

	internal static T? DeserializeOrNull<T> (string? payload) where T : class
		{
		if (string.IsNullOrWhiteSpace (payload))
			return null;
		try
			{
			return Deserialize<T> (payload!);
			}
		catch (JsonException) { return null; }
		}

	internal static string? UnwrapPayload (string? payload)
		{
		if (string.IsNullOrWhiteSpace (payload))
			return null;
		try
			{
			var envelope = Deserialize<Models.ApiResponse<object>> (payload!);
			return envelope?.Response is object response ? Serialize (response) : payload;
			}
		catch (JsonException) { return null; }
		}

	// Opaque telemetry values remain CLR dictionaries/lists/scalars, never serializer DOM objects.
	private sealed class PlainObjectConverter : JsonConverter<object>
		{
		public override object? Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
			{
				JsonTokenType.StartObject => JsonSerializer.Deserialize<Dictionary<string, object?>> (ref reader, options),
				JsonTokenType.StartArray => JsonSerializer.Deserialize<List<object?>> (ref reader, options),
				JsonTokenType.String => reader.GetString (),
				JsonTokenType.Number => reader.TryGetInt64 (out long integer) ? (object)integer : reader.GetDouble (),
				JsonTokenType.True => true,
				JsonTokenType.False => false,
				JsonTokenType.Null => null,
				_ => throw new JsonException ("Expected a JSON value.")
				};
		public override void Write (Utf8JsonWriter writer, object value, JsonSerializerOptions options)
			{
			if (value.GetType () == typeof (object))
				{
				writer.WriteStartObject ();
				writer.WriteEndObject ();
				}
			else
				JsonSerializer.Serialize (writer, value, value.GetType (), options);
			}
		}
	}

// Tesla sometimes represents identifiers as numbers; preserve that contract without culture dependence.
internal sealed class ScalarStringConverter : JsonConverter<string>
	{
	public override string? Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
		{
			JsonTokenType.String => reader.GetString (),
			JsonTokenType.Number => reader.GetDecimal ().ToString (System.Globalization.CultureInfo.InvariantCulture),
			JsonTokenType.True => "True",
			JsonTokenType.False => "False",
			_ => throw new JsonException ("Expected a string or scalar identifier.")
			};
	public override void Write (Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue (value);
    public override string ReadAsPropertyName (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString ()!;
    public override void WriteAsPropertyName (Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName (value);

	}