// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace TeslaPowerwallLibrary;

/// <summary>
/// Helpers for parsing Tesla Powerwall firmware version strings into comparable integers.
/// </summary>
public static class VersionHelper
	{
	/// <summary>
	/// Converts a firmware version string (for example <c>"23.44.1"</c>) into a comparable integer,
	/// mirroring the algorithm used by the Python pypowerwall project.
	/// </summary>
	/// <remarks>
	/// The first whitespace-delimited token is taken, every character other than a digit or one of
	/// <c>. / \</c> is removed, the value is padded to at least three dot-separated components, and the
	/// components are combined least-significant first using a base of 100 per component. For example,
	/// <c>"23.44.1"</c> yields <c>1 + 44*100 + 23*10000 = 234401</c>.
	/// </remarks>
	/// <param name="version">The version string to parse; may be <see langword="null"/>.</param>
	/// <returns>
	/// The comparable integer value, or <see langword="null"/> when <paramref name="version"/> is <see langword="null"/>.
	/// </returns>
	public static long? ParseVersion (string? version)
		{
		if (version is null)
			return null;

		var token = version.Split (' ')[0];
		var filtered = new string (token.Where (static c => char.IsDigit (c) || c is '.' or '/' or '\\').ToArray ());
		List<string> parts = filtered.Split ('.').ToList ();

		while (parts.Count < 3)
			parts.Add ("0");

		var components = new List<long> (parts.Count);
		foreach (var part in parts)
			{
			components.Add (long.TryParse (part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0L);
			}

		components.Reverse ();

		long result = 0;
		long multiplier = 1;
		foreach (var component in components)
			{
			result += component * multiplier;
			multiplier *= 100;
			}

		return result;
		}
	}
