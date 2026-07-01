// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary;

/// <summary>
/// Internal JSON helpers shared across the library.
/// </summary>
internal static class JsonHelper
	{
	/// <summary>
	/// Deserializes a JSON payload into the specified reference type, returning <see langword="null"/> on
	/// null, empty, or invalid input rather than throwing.
	/// </summary>
	/// <typeparam name="T">The target reference type.</typeparam>
	/// <param name="payload">The JSON payload to deserialize.</param>
	/// <returns>The deserialized value, or <see langword="null"/>.</returns>
	public static T? DeserializeOrNull<T> (string? payload)
		where T : class
		{
		if (string.IsNullOrWhiteSpace (payload))
			return null;

		try
			{
			return JsonConvert.DeserializeObject<T> (payload!);
			}
		catch (JsonException)
			{
			return null;
			}
		}
	}
