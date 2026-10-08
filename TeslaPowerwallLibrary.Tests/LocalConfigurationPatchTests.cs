// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Text.Json;

using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Verifies private configuration edits preserve every unrelated byte.</summary>
[TestFixture]
public sealed class LocalConfigurationPatchTests
	{
	/// <summary>Unknown values retain spacing, Unicode, escaped strings, large numbers and exponent spelling.</summary>
	[Test]
	public void RequestedValuesOnly_UnknownBytesStayExact ()
		{
		const string original = """{ "label": "é 🔋", "x":1.234567890123456789e+10, "site_info": { "backup_reserve_percent" : 9.75, "unknown": [null, "a\u002Bb", 18446744073709551615] }, "default_real_mode" : "self_consumption" }""";
		byte[] result = LocalConfigurationPatch.Apply (Encoding.UTF8.GetBytes (original),
			new LocalSettingsUpdate { BackupReservePercent = 20, OperationMode = "autonomous" });
		Assert.That (Encoding.UTF8.GetString (result), Is.EqualTo (original.Replace ("9.75", "24").Replace ("self_consumption", "autonomous")));
		}

	/// <summary>Missing supported fields can be added without changing unknown fields or dropping explicit nulls.</summary>
	/// <param name="site">Original site object with or without an unknown field.</param>
	[TestCase ("{}"), TestCase ("{ \"unknown\": null }")]
	public void OmittedFields_AreInsertedAsRequested (string site)
		{
		string original = "{\"site_info\":" + site + "}";
		using var result = JsonDocument.Parse (LocalConfigurationPatch.Apply (Encoding.UTF8.GetBytes (original),
			new LocalSettingsUpdate { BackupReservePercent = 0, OperationMode = "backup", GridChargingEnabled = false, GridExport = "never" }));
		Assert.That (result.RootElement.GetProperty ("default_real_mode").GetString (), Is.EqualTo ("backup"));
		JsonElement settings = result.RootElement.GetProperty ("site_info");
		Assert.That (settings.GetProperty ("backup_reserve_percent").GetDouble (), Is.EqualTo (5));
		Assert.That (settings.GetProperty ("disallow_charge_from_grid_with_solar_installed").GetBoolean (), Is.True);
		Assert.That (settings.GetProperty ("customer_preferred_export_rule").GetString (), Is.EqualTo ("never"));
		if (site.Contains ("unknown")) Assert.That (settings.GetProperty ("unknown").ValueKind, Is.EqualTo (JsonValueKind.Null));
		}

	/// <summary>Malformed, ambiguous and incomplete configurations cannot be used for replacement writes.</summary>
	/// <param name="json">Invalid or unsafe original configuration.</param>
	[TestCase ("{}"), TestCase ("{\"site_info\":null}"), TestCase ("{\"site_info\":{\"x\":1,\"x\":2}}"), TestCase ("{\"site_info\":{}} trailing")]
	public void UnsafeConfiguration_IsRejected (string json) => Assert.That (() =>
		LocalConfigurationPatch.Apply (Encoding.UTF8.GetBytes (json), new LocalSettingsUpdate { OperationMode = "backup" }), Throws.InstanceOf<JsonException> ());
	}
