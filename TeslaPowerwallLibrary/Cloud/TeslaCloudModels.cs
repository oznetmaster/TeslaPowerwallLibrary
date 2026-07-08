// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Tolerantly converts a JSON string or numeric token into a <see cref="long"/>, mirroring the upstream
/// pypowerwall behavior of accepting either representation for fields such as <c>nameplate_power</c>.
/// Missing, null, or unparsable values convert to <c>0</c> rather than throwing.
/// </summary>
internal sealed class FlexibleLongConverter : JsonConverter
	{
	/// <inheritdoc/>
	public override bool CanConvert (Type objectType) => objectType == typeof (long) || objectType == typeof (long?);

	/// <inheritdoc/>
	public override object? ReadJson (JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
		reader.TokenType switch
			{
			JsonToken.Integer or JsonToken.Float => Convert.ToInt64 (reader.Value, CultureInfo.InvariantCulture),
			JsonToken.String => long.TryParse ((string?) reader.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L,
			_ => 0L
			};

	/// <inheritdoc/>
	public override void WriteJson (JsonWriter writer, object? value, JsonSerializer serializer) => writer.WriteValue (value);
	}

/// <summary>
/// Tolerantly converts a JSON string or numeric token into a <see cref="string"/>, mirroring the upstream
/// pypowerwall behavior of accepting either representation for fields such as <c>energy_site_id</c>.
/// </summary>
internal sealed class FlexibleStringConverter : JsonConverter
	{
	/// <inheritdoc/>
	public override bool CanConvert (Type objectType) => objectType == typeof (string);

	/// <inheritdoc/>
	public override object? ReadJson (JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
		reader.TokenType switch
			{
			JsonToken.String => (string?) reader.Value,
			JsonToken.Integer or JsonToken.Float => Convert.ToString (reader.Value, CultureInfo.InvariantCulture),
			JsonToken.Null or JsonToken.Undefined => null,
			_ => reader.Value?.ToString ()
			};

	/// <inheritdoc/>
	public override void WriteJson (JsonWriter writer, object? value, JsonSerializer serializer) => writer.WriteValue ((string?) value);
	}

/// <summary>
/// A single Tesla™ energy product (battery or solar) entry from the <c>/api/1/products</c> response.
/// </summary>
internal sealed record EnergyProduct
	{
	/// <summary>Tesla resource type (for example <c>battery</c> or <c>solar</c>).</summary>
	[JsonProperty ("resource_type")]
	public string? ResourceType { get; init; }

	/// <summary>Energy site identifier; Tesla returns this as either a JSON string or number.</summary>
	[JsonProperty ("energy_site_id"), JsonConverter (typeof (FlexibleStringConverter))]
	public string? EnergySiteId { get; init; }

	/// <summary>Fallback product identifier, used when <see cref="EnergySiteId"/> is absent.</summary>
	[JsonProperty ("id")]
	public string? Id { get; init; }

	/// <summary>Human-readable site name.</summary>
	[JsonProperty ("site_name")]
	public string? SiteName { get; init; }
	}

/// <summary>
/// Gateway component flags nested under a <see cref="SiteConfigResponse"/>.
/// </summary>
internal sealed record SiteComponents
	{
	/// <summary>Indicates whether charging the battery from the grid is currently disallowed.</summary>
	[JsonProperty ("disallow_charge_from_grid_with_solar_installed")]
	public bool? DisallowChargeFromGridWithSolarInstalled { get; init; }

	/// <summary>Pre-PTO flag that, when set, overrides <see cref="CustomerPreferredExportRule"/> to "never".</summary>
	[JsonProperty ("non_export_configured")]
	public bool? NonExportConfigured { get; init; }

	/// <summary>The configured grid export rule (<c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>).</summary>
	[JsonProperty ("customer_preferred_export_rule")]
	public string? CustomerPreferredExportRule { get; init; }

	/// <summary>Raw gateway component descriptor; shape is opaque and passed through untouched.</summary>
	[JsonProperty ("gateway")]
	public JToken? Gateway { get; init; }

	/// <summary>Solar inverter component entries, when present.</summary>
	[JsonProperty ("inverters")]
	public IReadOnlyList<object>? Inverters { get; init; }

	/// <summary>Raw solar component descriptor, used only to detect the presence of solar hardware.</summary>
	[JsonProperty ("solar")]
	public JToken? Solar { get; init; }
	}

/// <summary>
/// User-configurable site settings nested under a <see cref="SiteConfigResponse"/>.
/// </summary>
internal sealed record SiteUserSettings
	{
	/// <summary>Indicates whether Storm Watch is currently enabled.</summary>
	[JsonProperty ("storm_mode_enabled")]
	public bool? StormModeEnabled { get; init; }
	}

/// <summary>
/// Tariff information nested under a <see cref="SiteConfigResponse"/>.
/// </summary>
internal sealed record SiteTariffContent
	{
	/// <summary>Utility company name.</summary>
	[JsonProperty ("utility")]
	public string? Utility { get; init; }
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>site_info</c> endpoint.
/// </summary>
internal sealed record SiteConfigResponse
	{
	/// <summary>Device identification number (DIN).</summary>
	[JsonProperty ("id")]
	public string? Id { get; init; }

	/// <summary>Configured site name.</summary>
	[JsonProperty ("site_name")]
	public string? SiteName { get; init; }

	/// <summary>Raw installation date/time; opaque and passed through untouched to avoid reformatting.</summary>
	[JsonProperty ("installation_date")]
	public JToken? InstallationDate { get; init; }

	/// <summary>Configured site IANA time zone name.</summary>
	[JsonProperty ("installation_time_zone")]
	public string? InstallationTimeZone { get; init; }

	/// <summary>Gateway firmware version string.</summary>
	[JsonProperty ("version")]
	public string? Version { get; init; }

	/// <summary>Configured backup reserve percentage (raw gateway scale).</summary>
	[JsonProperty ("backup_reserve_percent")]
	public double? BackupReservePercent { get; init; }

	/// <summary>Active battery operation mode (for example <c>self_consumption</c>).</summary>
	[JsonProperty ("default_real_mode")]
	public string? DefaultRealMode { get; init; }

	/// <summary>Number of battery packs at the site.</summary>
	[JsonProperty ("battery_count")]
	public int? BatteryCount { get; init; }

	/// <summary>Nameplate power rating in watts; Tesla returns this as either a JSON string or number.</summary>
	[JsonProperty ("nameplate_power"), JsonConverter (typeof (FlexibleLongConverter))]
	public long NameplatePower { get; init; }

	/// <summary>Nameplate energy rating in watt-hours; Tesla returns this as either a JSON string or number.</summary>
	[JsonProperty ("nameplate_energy"), JsonConverter (typeof (FlexibleLongConverter))]
	public long NameplateEnergy { get; init; }

	/// <summary>Raw maximum site meter power (AC); opaque and passed through untouched.</summary>
	[JsonProperty ("max_site_meter_power_ac")]
	public JToken? MaxSiteMeterPowerAc { get; init; }

	/// <summary>Raw minimum site meter power (AC); opaque and passed through untouched.</summary>
	[JsonProperty ("min_site_meter_power_ac")]
	public JToken? MinSiteMeterPowerAc { get; init; }

	/// <summary>Gateway component flags.</summary>
	[JsonProperty ("components")]
	public SiteComponents? Components { get; init; }

	/// <summary>User-configurable site settings.</summary>
	[JsonProperty ("user_settings")]
	public SiteUserSettings? UserSettings { get; init; }

	/// <summary>Tariff information.</summary>
	[JsonProperty ("tariff_content")]
	public SiteTariffContent? TariffContent { get; init; }
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>live_status</c> endpoint.
/// </summary>
internal sealed record SitePowerResponse
	{
	/// <summary>Raw reading timestamp; opaque and passed through untouched to avoid reformatting.</summary>
	[JsonProperty ("timestamp")]
	public JToken? Timestamp { get; init; }

	/// <summary>Island (grid connection) status (for example <c>on_grid</c>, <c>off_grid</c>, or <c>off_grid_intentional</c>).</summary>
	[JsonProperty ("island_status")]
	public string? IslandStatus { get; init; }

	/// <summary>Raw grid status string (for example <c>Active</c> or <c>Unknown</c>).</summary>
	[JsonProperty ("grid_status")]
	public string? GridStatus { get; init; }

	/// <summary>Indicates whether grid services are currently active.</summary>
	[JsonProperty ("grid_services_active")]
	public bool? GridServicesActive { get; init; }

	/// <summary>Raw grid services power; opaque and passed through untouched.</summary>
	[JsonProperty ("grid_services_power")]
	public JToken? GridServicesPower { get; init; }

	/// <summary>Raw grid (site) power; opaque and passed through untouched.</summary>
	[JsonProperty ("grid_power")]
	public JToken? GridPower { get; init; }

	/// <summary>Raw battery power; opaque and passed through untouched.</summary>
	[JsonProperty ("battery_power")]
	public JToken? BatteryPower { get; init; }

	/// <summary>Raw home (load) power; opaque and passed through untouched.</summary>
	[JsonProperty ("load_power")]
	public JToken? LoadPower { get; init; }

	/// <summary>Raw solar generation power; opaque and passed through untouched.</summary>
	[JsonProperty ("solar_power")]
	public JToken? SolarPower { get; init; }
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>site_status</c> (battery summary) endpoint.
/// </summary>
internal sealed record SiteSummaryResponse
	{
	/// <summary>Battery charge level as a percentage (raw gateway scale).</summary>
	[JsonProperty ("percentage_charged")]
	public double? PercentageCharged { get; init; }

	/// <summary>Raw total pack energy; opaque and passed through untouched.</summary>
	[JsonProperty ("total_pack_energy")]
	public JToken? TotalPackEnergy { get; init; }

	/// <summary>Raw remaining energy; opaque and passed through untouched.</summary>
	[JsonProperty ("energy_left")]
	public JToken? EnergyLeft { get; init; }
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>backup_time_remaining</c> endpoint.
/// </summary>
internal sealed record BackupTimeRemainingResponse
	{
	/// <summary>Estimated backup time remaining, in hours.</summary>
	[JsonProperty ("time_remaining_hours")]
	public double? TimeRemainingHours { get; init; }
	}

/// <summary>
/// The Tesla SSO OAuth token response returned by the <c>oauth2/v3/token</c> endpoint.
/// </summary>
internal sealed record TeslaCloudTokenResponse
	{
	/// <summary>The current OAuth access token.</summary>
	[JsonProperty ("access_token")]
	public string? AccessToken { get; init; }

	/// <summary>The current OAuth refresh token, possibly rotated.</summary>
	[JsonProperty ("refresh_token")]
	public string? RefreshToken { get; init; }
	}

#pragma warning restore CA1507
