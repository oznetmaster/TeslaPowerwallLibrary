// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using System.Globalization;

using System.Text.Json;
using System.Text.Json.Serialization;


namespace TeslaPowerwallLibrary.Cloud;

internal sealed class FlexibleLongConverter : JsonConverter<long>
	{
	public override long Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
		{
			JsonTokenType.Number => reader.TryGetInt64 (out long integer) ? integer : Convert.ToInt64 (reader.GetDouble ()),
			JsonTokenType.String => long.TryParse (reader.GetString (), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0,
			JsonTokenType.Null => 0,
			_ => throw new JsonException ("Expected an integer or numeric string.")
			};
	public override void Write (Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue (value);
	}

/// <summary>
/// A single Tesla™ energy product (battery or solar) entry from the <c>/api/1/products</c> response.
/// </summary>
internal sealed record EnergyProduct
	{
	/// <summary>Tesla resource type (for example <c>battery</c> or <c>solar</c>).</summary>
	[JsonPropertyName ("resource_type")]
	public string? ResourceType { get; init; }

	/// <summary>Energy site identifier; Tesla returns this as either a JSON string or number.</summary>
	[JsonPropertyName ("energy_site_id"), JsonConverter (typeof (ScalarStringConverter))]
	public string? EnergySiteId { get; init; }

	/// <summary>Fallback product identifier, used when <see cref="EnergySiteId"/> is absent.</summary>
	[JsonPropertyName ("id")]
	public string? Id { get; init; }

	/// <summary>Human-readable site name.</summary>
	[JsonPropertyName ("site_name")]
	public string? SiteName { get; init; }
	}

/// <summary>
/// Gateway component flags nested under a <see cref="SiteConfigResponse"/>.
/// </summary>
internal sealed record SiteComponents
	{
	/// <summary>Indicates whether charging the battery from the grid is currently disallowed.</summary>
	[JsonPropertyName ("disallow_charge_from_grid_with_solar_installed")]
	public bool? DisallowChargeFromGridWithSolarInstalled { get; init; }

	/// <summary>Pre-PTO flag that, when set, overrides <see cref="CustomerPreferredExportRule"/> to "never".</summary>
	[JsonPropertyName ("non_export_configured")]
	public bool? NonExportConfigured { get; init; }

	/// <summary>The configured grid export rule (<c>battery_ok</c>, <c>pv_only</c>, or <c>never</c>).</summary>
	[JsonPropertyName ("customer_preferred_export_rule")]
	public string? CustomerPreferredExportRule { get; init; }

	/// <summary>Raw gateway component descriptor; shape is optional numeric value from Tesla.</summary>
	[JsonPropertyName ("gateway")]
	public object? Gateway
		{
		get; init;
		}

	/// <summary>Solar inverter component entries, when present.</summary>
	[JsonPropertyName ("inverters")]
	public IReadOnlyList<object>? Inverters { get; init; }

	/// <summary>Raw solar component descriptor, used only to detect the presence of solar hardware.</summary>
	[JsonPropertyName ("solar")]
	public object? Solar
		{
		get; init;
		}
	}

/// <summary>
/// User-configurable site settings nested under a <see cref="SiteConfigResponse"/>.
/// </summary>
internal sealed record SiteUserSettings
	{
	/// <summary>Indicates whether Storm Watch is currently enabled.</summary>
	[JsonPropertyName ("storm_mode_enabled")]
	public bool? StormModeEnabled { get; init; }
	}

/// <summary>
/// Tariff information nested under a <see cref="SiteConfigResponse"/>.
/// </summary>
internal sealed record SiteTariffContent
	{
	/// <summary>Utility company name.</summary>
	[JsonPropertyName ("utility")]
	public string? Utility { get; init; }
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>site_info</c> endpoint.
/// </summary>
internal sealed record SiteConfigResponse
	{
	/// <summary>Device identification number (DIN).</summary>
	[JsonPropertyName ("id")]
	public string? Id { get; init; }

	/// <summary>Configured site name.</summary>
	[JsonPropertyName ("site_name")]
	public string? SiteName { get; init; }

	/// <summary>Installation date/time, preserved as its original string.</summary>
	[JsonPropertyName ("installation_date")]
	public string? InstallationDate
		{
		get; init;
		}

	/// <summary>Configured site IANA time zone name.</summary>
	[JsonPropertyName ("installation_time_zone")]
	public string? InstallationTimeZone { get; init; }

	/// <summary>Gateway firmware version string.</summary>
	[JsonPropertyName ("version")]
	public string? Version { get; init; }

	/// <summary>Configured backup reserve percentage (raw gateway scale).</summary>
	[JsonPropertyName ("backup_reserve_percent")]
	public double? BackupReservePercent { get; init; }

	/// <summary>Active battery operation mode (for example <c>self_consumption</c>).</summary>
	[JsonPropertyName ("default_real_mode")]
	public string? DefaultRealMode { get; init; }

	/// <summary>Number of battery packs at the site.</summary>
	[JsonPropertyName ("battery_count")]
	public int? BatteryCount { get; init; }

	/// <summary>Nameplate power rating in watts; Tesla returns this as either a JSON string or number.</summary>
	[JsonPropertyName ("nameplate_power"), JsonConverter (typeof (FlexibleLongConverter))]
	public long NameplatePower { get; init; }

	/// <summary>Nameplate energy rating in watt-hours; Tesla returns this as either a JSON string or number.</summary>
	[JsonPropertyName ("nameplate_energy"), JsonConverter (typeof (FlexibleLongConverter))]
	public long NameplateEnergy { get; init; }

	/// <summary>Raw maximum site meter power (AC); optional numeric value from Tesla.</summary>
	[JsonPropertyName ("max_site_meter_power_ac")]
	public double? MaxSiteMeterPowerAc
		{
		get; init;
		}

	/// <summary>Raw minimum site meter power (AC); optional numeric value from Tesla.</summary>
	[JsonPropertyName ("min_site_meter_power_ac")]
	public double? MinSiteMeterPowerAc
		{
		get; init;
		}

	/// <summary>Gateway component flags.</summary>
	[JsonPropertyName ("components")]
	public SiteComponents? Components { get; init; }

	/// <summary>User-configurable site settings.</summary>
	[JsonPropertyName ("user_settings")]
	public SiteUserSettings? UserSettings { get; init; }

	/// <summary>Tariff information.</summary>
	[JsonPropertyName ("tariff_content")]
	public SiteTariffContent? TariffContent { get; init; }
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>live_status</c> endpoint.
/// </summary>
internal sealed record SitePowerResponse
	{
	/// <summary>Reading timestamp, preserved as its original string.</summary>
	[JsonPropertyName ("timestamp")]
	public string? Timestamp
		{
		get; init;
		}

	/// <summary>Island (grid connection) status (for example <c>on_grid</c>, <c>off_grid</c>, or <c>off_grid_intentional</c>).</summary>
	[JsonPropertyName ("island_status")]
	public string? IslandStatus { get; init; }

	/// <summary>Raw grid status string (for example <c>Active</c> or <c>Unknown</c>).</summary>
	[JsonPropertyName ("grid_status")]
	public string? GridStatus { get; init; }

	/// <summary>Indicates whether grid services are currently active.</summary>
	[JsonPropertyName ("grid_services_active")]
	public bool? GridServicesActive { get; init; }

	/// <summary>Raw grid services power; optional numeric value from Tesla.</summary>
	[JsonPropertyName ("grid_services_power")]
	public double? GridServicesPower
		{
		get; init;
		}

	/// <summary>Raw grid (site) power; optional numeric value from Tesla.</summary>
	[JsonPropertyName ("grid_power")]
	public double? GridPower
		{
		get; init;
		}

	/// <summary>Raw battery power; optional numeric value from Tesla.</summary>
	[JsonPropertyName ("battery_power")]
	public double? BatteryPower
		{
		get; init;
		}

	/// <summary>Raw home (load) power; optional numeric value from Tesla.</summary>
	[JsonPropertyName ("load_power")]
	public double? LoadPower
		{
		get; init;
		}

	/// <summary>Raw solar generation power; optional numeric value from Tesla.</summary>
	[JsonPropertyName ("solar_power")]
	public double? SolarPower
		{
		get; init;
		}
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>site_status</c> (battery summary) endpoint.
/// </summary>
internal sealed record SiteSummaryResponse
	{
	/// <summary>Battery charge level as a percentage (raw gateway scale).</summary>
	[JsonPropertyName ("percentage_charged")]
	public double? PercentageCharged { get; init; }

	/// <summary>Raw total pack energy; optional numeric value from Tesla.</summary>
	[JsonPropertyName ("total_pack_energy")]
	public double? TotalPackEnergy
		{
		get; init;
		}

	/// <summary>Raw remaining energy; optional numeric value from Tesla.</summary>
	[JsonPropertyName ("energy_left")]
	public double? EnergyLeft
		{
		get; init;
		}
	}

/// <summary>
/// The <c>response</c> body of the Tesla Owners API <c>backup_time_remaining</c> endpoint.
/// </summary>
internal sealed record BackupTimeRemainingResponse
	{
	/// <summary>Estimated backup time remaining, in hours.</summary>
	[JsonPropertyName ("time_remaining_hours")]
	public double? TimeRemainingHours { get; init; }
	}

/// <summary>
/// The Tesla SSO OAuth token response returned by the <c>oauth2/v3/token</c> endpoint.
/// </summary>
internal sealed record TeslaCloudTokenResponse
	{
	/// <summary>The current OAuth access token.</summary>
	[JsonPropertyName ("access_token")]
	public string? AccessToken { get; init; }

	/// <summary>The current OAuth refresh token, possibly rotated.</summary>
	[JsonPropertyName ("refresh_token")]
	public string? RefreshToken { get; init; }
	}

#pragma warning restore CA1507