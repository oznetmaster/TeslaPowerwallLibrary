// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Console input and output helpers: prompts, masked password entry, colored messages,
/// aligned field output, and unit formatting.
/// </summary>
internal static class ConsoleHelpers
	{
	/// <summary>Legend explaining the trailing <c>*</c> marker used by <see cref="FormatChoices"/>.</summary>
	public const string DefaultChoiceLegend = "* = default";

	/// <summary>Writes a label and reads a line of input from the console.</summary>
	public static string? Prompt (string label)
		{
		Console.Write ($"{label}: ");
		return Console.ReadLine ();
		}

	/// <summary>Reads a line of input without echoing the characters, masking them with asterisks.</summary>
	public static string ReadPassword (string label)
		{
		Console.Write ($"{label}: ");
		var builder = new StringBuilder ();

		while (true)
			{
			var key = Console.ReadKey (intercept: true);
			if (key.Key == ConsoleKey.Enter)
				{
				Console.WriteLine ();
				break;
				}

			if (key.Key == ConsoleKey.Backspace)
				{
				if (builder.Length > 0)
					{
					builder.Length--;
					Console.Write ("\b \b");
					}

				continue;
				}

			if (!char.IsControl (key.KeyChar))
				{
				builder.Append (key.KeyChar);
				Console.Write ('*');
				}
			}

		return builder.ToString ();
		}

	/// <summary>Writes a section heading followed by an underline.</summary>
	public static void WriteHeading (string text)
		{
		Console.WriteLine ();
		WriteColor (text, ConsoleColor.Cyan);
		WriteColor (new string ('-', text.Length), ConsoleColor.DarkGray);
		}

	/// <summary>
	/// Formats a list of choice values as <c>a | b | c</c>, appending <c>*</c> to the default value
	/// so help text can indicate which value applies when the option is omitted.
	/// </summary>
	/// <param name="values">The valid choice values.</param>
	/// <param name="defaultValue">The value marked as the default with a trailing <c>*</c>, or <see langword="null"/> when there is no default.</param>
	public static string FormatChoices (IReadOnlyList<string> values, string? defaultValue = null) =>
		string.Join (" | ", values.Select (value =>
			string.Equals (value, defaultValue, StringComparison.Ordinal) ? value + "*" : value));

	/// <summary>Writes an aligned <c>label : value</c> pair, substituting <c>n/a</c> for missing values.</summary>
	public static void WriteField (string label, string? value) =>
		Console.WriteLine ($"  {label,-22}{(string.IsNullOrWhiteSpace (value) ? "n/a" : value)}");

	/// <summary>Writes a message in green to indicate success.</summary>
	public static void WriteSuccess (string text) =>
		WriteColor (text, ConsoleColor.Green);

	/// <summary>Writes a message in red to indicate an error.</summary>
	public static void WriteError (string text) =>
		WriteColor (text, ConsoleColor.Red);

	/// <summary>Formats a nullable power value in watts.</summary>
	public static string FormatWatts (double? value) =>
		value is double v ? v.ToString ("N1", CultureInfo.InvariantCulture) + " W" : "n/a";

	/// <summary>Formats a nullable energy value in watt-hours.</summary>
	public static string FormatWattHours (double? value) =>
		value is double v ? v.ToString ("N0", CultureInfo.InvariantCulture) + " Wh" : "n/a";

	/// <summary>Formats a nullable percentage value.</summary>
	public static string FormatPercent (double? value) =>
		value is double v ? v.ToString ("N1", CultureInfo.InvariantCulture) + " %" : "n/a";

	/// <summary>Formats a nullable duration value in hours.</summary>
	public static string FormatHours (double? value) =>
		value is double v ? v.ToString ("N2", CultureInfo.InvariantCulture) + " h" : "n/a";

	private static void WriteColor (string text, ConsoleColor color)
		{
		var original = Console.ForegroundColor;
		try
			{
			Console.ForegroundColor = color;
			Console.WriteLine (text);
			}
		finally
			{
			Console.ForegroundColor = original;
			}
		}
	}
