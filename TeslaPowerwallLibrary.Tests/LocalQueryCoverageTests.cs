// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Audits attributed response models against every field in the bundled vendor query selections.</summary>
[TestFixture]
public sealed class LocalQueryCoverageTests
	{
	/// <summary>Every selected response field has a typed destination, including dynamic component families.</summary>
	/// <param name="resource">Bundled vendor query resource.</param>
	[TestCase ("Queries.data"), TestCase ("Queries2026.data")]
	public void EveryBundledQueryField_HasAnAttributedDestination (string resource)
		{
		using var stream = typeof (Powerwall).Assembly.GetManifestResourceStream ("TeslaPowerwallLibrary.Tedapi.Protocol." + resource)!;
		using var definitions = JsonDocument.Parse (stream);
		var missing = new List<string> ();
		int fieldCount = 0, excludedCredentials = 0;
		foreach (var query in definitions.RootElement.EnumerateObject ())
			{
			string text = query.Value.GetProperty ("text").GetString ()!;
			// Preserve quoted strings while discarding GraphQL line comments.
			text = Regex.Replace (text, "\"(?:\\\\.|[^\"])*\"|#[^\r\n]*", match => match.Value.StartsWith ("#", StringComparison.Ordinal) ? "" : match.Value);
			var tokens = Regex.Matches (text, """[A-Za-z_][A-Za-z0-9_]*|"(?:\\.|[^"])*"|[{}():]""").Cast<Match> ().Select (match => match.Value).ToArray ();
			int position = 0;
			while (tokens[position] != "{") position++;
			List<Field> fields = ParseFields (tokens, ref position);
			Type model = query.Name is "components" or "PW3Query" ? typeof (LocalComponentTelemetry) : typeof (LocalTelemetry);
			Check (model, fields, query.Name, missing, ref fieldCount, ref excludedCredentials);
			}
		TestContext.Out.WriteLine ("Selected response fields checked: " + fieldCount);
		Assert.That (excludedCredentials, Is.EqualTo (1), "Only the remote-service session credential is deliberately excluded.");
		Assert.That (missing, Is.Empty, "Selected fields without attributed response destinations: " + string.Join (", ", missing));
		}

	/// <summary>Protocol resources cannot be mistaken for a driver manifest after assembly merging.</summary>
	[Test]
	public void BundledQueryResources_DoNotUseManifestFileNames ()
		{
		string[] resources = typeof (Powerwall).Assembly.GetManifestResourceNames ()
			.Where (name => name.StartsWith ("TeslaPowerwallLibrary.Tedapi.Protocol.", StringComparison.Ordinal)).ToArray ();
		Assert.That (resources, Has.Length.EqualTo (2));
		Assert.That (resources.Any (name => name.IndexOf (".json", StringComparison.OrdinalIgnoreCase) >= 0), Is.False,
			"Manifest packagers select embedded JSON resources as driver manifests.");
		}

	private static List<Field> ParseFields (string[] tokens, ref int position)
		{
		Assert.That (tokens[position++], Is.EqualTo ("{"));
		var fields = new List<Field> ();
		while (tokens[position] != "}")
			{
			string name = tokens[position++];
			if (tokens[position] == ":") position += 2; // Retain response alias; skip underlying field name.
			if (tokens[position] == "(")
				{
				int depth = 0;
				do { string token = tokens[position++]; if (token == "(") depth++; else if (token == ")") depth--; } while (depth != 0);
				}
			fields.Add (new Field (name, tokens[position] == "{" ? ParseFields (tokens, ref position) : new List<Field> ()));
			}
		position++;
		return fields;
		}

	private static void Check (Type type, List<Field> fields, string path, List<string> missing, ref int count, ref int excludedCredentials)
		{
		type = Nullable.GetUnderlyingType (type) ?? type;
		if (type.IsGenericType && type.GetGenericTypeDefinition () == typeof (IReadOnlyDictionary<,>))
			{
			foreach (Field field in fields) { count++; Check (type.GetGenericArguments ()[1], field.Children, path + "." + field.Name, missing, ref count, ref excludedCredentials); }
			return;
			}
		if (type.IsGenericType && (type.GetGenericTypeDefinition () == typeof (IReadOnlyList<>) || type.GetGenericTypeDefinition () == typeof (LocalDiagnosticRecords<>)))
			{ Check (type.GetGenericArguments ()[0], fields, path, missing, ref count, ref excludedCredentials); return; }
		foreach (Field field in fields)
			{
			count++;
			if (path.EndsWith (".system.supportMode.remoteService", StringComparison.Ordinal) && field.Name == "sessionId")
				{ excludedCredentials++; continue; } // Never expose the remote-service credential as telemetry.
			PropertyInfo? property = type.GetProperties ().FirstOrDefault (p => p.GetCustomAttribute<JsonPropertyNameAttribute> ()?.Name == field.Name);
			if (property is null) missing.Add (path + "." + field.Name);
			else Check (property.PropertyType, field.Children, path + "." + field.Name, missing, ref count, ref excludedCredentials);
			}
		}

	private sealed record Field (string Name, List<Field> Children);
	}
