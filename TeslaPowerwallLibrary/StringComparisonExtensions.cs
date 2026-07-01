// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary;

internal static class StringComparisonExtensions
	{
#if NETFRAMEWORK
	/// <summary>
	/// Polyfill for string.Contains(string, StringComparison) on .NET Framework.
	/// </summary>
	/// <param name="source">Source string.</param>
	/// <param name="value">Substring to find.</param>
	/// <param name="comparisonType">Comparison rules.</param>
	/// <returns>True if <paramref name="value"/> occurs within <paramref name="source"/> using the specified comparison; otherwise false.</returns>
	public static bool Contains (this string? source, string value, StringComparison comparisonType) =>
		source?.IndexOf (value, comparisonType) >=0;
#endif
	}

