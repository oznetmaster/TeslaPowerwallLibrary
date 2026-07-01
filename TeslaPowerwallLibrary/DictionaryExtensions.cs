using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using static TeslaPowerwallLibrary.Constants;

namespace TeslaPowerwallLibrary;

/// <summary>
/// Nullable flow-friendly helpers for dictionaries.
/// </summary>
public static class DictionaryExtensions
	{
	/// <summary>
	/// TryGetValue that also guarantees the value is non-null when the method returns true.
	/// </summary>
	/// <typeparam name="TKey">Dictionary key type.</typeparam>
	/// <typeparam name="TValue">Dictionary value type (reference type).</typeparam>
	/// <param name="dict">Source dictionary.</param>
	/// <param name="key">Key to look up.</param>
	/// <param name="value">Non-null value when the method returns true.</param>
	/// <returns>True when the key exists and the associated value is not null; otherwise false.</returns>
	public static bool TryGetNonNull<TKey, TValue> (
		this IDictionary<TKey, TValue> dict,
		TKey key,
		[NotNullWhen (true)] out TValue? value)
		where TValue : class
		{
		if (dict is null)
			{
			value = null;
			return false;
			}

		if (dict.TryGetValue (key, out TValue? v) && v is not null)
			{
			value = v;
			return true;
			}

		value = null;
		return false;
		}

	/// <summary>
	/// Gets a string value for a key or a fallback when not present.
	/// Assumes JSON deserialization never stores null values in the dictionary.
	/// </summary>
	/// <param name="dict">Source dictionary.</param>
	/// <param name="key">Key to look up.</param>
	/// <param name="fallback">Fallback string when key is missing.</param>
	/// <returns>String value or fallback.</returns>
	public static string GetStringOr (this IDictionary<string, object> dict, string key, string fallback = TEXT_UNKNOWN) =>
		dict.TryGetValue (key, out var v) ? (v?.ToString () ?? fallback) : fallback;

	/// <summary>
	/// Gets a nullable string value for a key or a nullable fallback when not present.
	/// </summary>
	/// <param name="dict">Source dictionary.</param>
	/// <param name="key">Key to look up.</param>
	/// <param name="fallback">Nullable fallback when key is missing.</param>
	/// <returns>String value or nullable fallback.</returns>
	public static string? GetNullableStringOr (this IDictionary<string, object> dict, string key, string? fallback = null) =>
		dict.TryGetValue (key, out var v) ? (v?.ToString () ?? fallback) : fallback;
	}
