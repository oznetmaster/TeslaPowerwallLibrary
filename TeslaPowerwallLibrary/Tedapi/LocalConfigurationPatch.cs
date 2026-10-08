// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Text.Json;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Preserves the vendor configuration's bytes outside explicitly requested setting values.</summary>
/// <remarks>Private wire processing is necessary because configuration writes replace a complete file,
/// including fields this library must neither expose nor reinterpret.</remarks>
internal static class LocalConfigurationPatch
	{
	/// <summary>Changes only known settings while retaining unknown fields, ordering, escaping and numeric representations.</summary>
	/// <param name="original">Fresh configuration bytes received with the optimistic-lock hash.</param>
	/// <param name="update">Already validated typed settings.</param>
	/// <returns>Updated UTF-8 configuration without exposing it through the public API.</returns>
	internal static byte[] Apply (byte[] original, LocalSettingsUpdate update)
		{
		var rootChanges = new Dictionary<string, string> ();
		var siteChanges = new Dictionary<string, string> ();
		if (update.OperationMode is string mode) rootChanges.Add ("default_real_mode", JsonSerializer.Serialize (mode));
		if (update.BackupReservePercent is double reserve) siteChanges.Add ("backup_reserve_percent", JsonSerializer.Serialize (reserve * 0.95 + 5));
		if (update.GridChargingEnabled is bool charging) siteChanges.Add ("disallow_charge_from_grid_with_solar_installed", JsonSerializer.Serialize (!charging));
		if (update.GridExport is string export) siteChanges.Add ("customer_preferred_export_rule", JsonSerializer.Serialize (export));
		var edits = new List<Edit> ();
		var reader = new Utf8JsonReader (original);
		if (!reader.Read () || reader.TokenType != JsonTokenType.StartObject) throw new JsonException ("Configuration must be an object.");
		PatchObject (ref reader, rootChanges, siteChanges, edits);
		if (reader.Read ()) throw new JsonException ("Unexpected data after configuration.");
		using var output = new System.IO.MemoryStream ();
		int position = 0;
		foreach (Edit edit in edits.OrderBy (edit => edit.Start))
			{
			output.Write (original, position, edit.Start - position);
			byte[] value = Encoding.UTF8.GetBytes (edit.Value);
			output.Write (value, 0, value.Length);
			position = edit.End;
			}
		output.Write (original, position, original.Length - position);
		return output.ToArray ();
		}

	private static void PatchObject (ref Utf8JsonReader reader, Dictionary<string, string> changes,
		Dictionary<string, string>? siteChanges, List<Edit> edits)
		{
		bool hasProperties = false;
		var seen = new HashSet<string> (StringComparer.Ordinal);
		while (reader.Read () && reader.TokenType != JsonTokenType.EndObject)
			{
			if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException ("Expected a configuration field.");
			hasProperties = true;
			string name = reader.GetString ()!;
			if (!seen.Add (name)) throw new JsonException ("Duplicate configuration field; no write is safe.");
			if (!reader.Read ()) throw new JsonException ("Missing configuration value.");
			if (name == "site_info" && siteChanges is not null)
				{
				if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException ("Site configuration must be an object.");
				PatchObject (ref reader, siteChanges, null, edits);
				}
			else
				{
				int start = checked ((int)reader.TokenStartIndex);
				reader.Skip ();
				if (changes.TryGetValue (name, out string? replacement))
					{
					edits.Add (new Edit (start, checked ((int)reader.BytesConsumed), replacement));
					changes.Remove (name);
					}
				}
			}
		if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException ("Incomplete configuration object.");
		if (siteChanges is not null && !seen.Contains ("site_info")) throw new JsonException ("Site configuration is missing.");
		if (changes.Count != 0)
			{
			string suffix = (hasProperties ? "," : "") + string.Join (",", changes.Select (pair => JsonSerializer.Serialize (pair.Key) + ":" + pair.Value));
			int end = checked ((int)reader.TokenStartIndex);
			edits.Add (new Edit (end, end, suffix));
			}
		}

	private sealed record Edit (int Start, int End, string Value);
	}
