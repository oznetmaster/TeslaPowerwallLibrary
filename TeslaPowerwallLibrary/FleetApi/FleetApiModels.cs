// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.FleetApi;

/// <summary>
/// Tesla™ FleetAPI regional base URLs and region-code resolution. Mirrors the upstream pypowerwall
/// <c>fleet_api_urls</c> mapping.
/// </summary>
internal static class FleetApiRegions
	{
	/// <summary>North America / Asia-Pacific Fleet API base URL.</summary>
	public const string NORTH_AMERICA = "https://fleet-api.prd.na.vn.cloud.tesla.com";

	/// <summary>Europe / Middle East / Africa Fleet API base URL.</summary>
	public const string EUROPE = "https://fleet-api.prd.eu.vn.cloud.tesla.com";

	/// <summary>China Fleet API base URL.</summary>
	public const string CHINA = "https://fleet-api.prd.cn.vn.cloud.tesla.cn";

	/// <summary>
	/// Resolves the Fleet API base URL for the specified region code (<c>na</c>, <c>eu</c>, or <c>cn</c>).
	/// Unrecognized or absent values select <see cref="NORTH_AMERICA"/>. Automatic discovery uses this
	/// address only to bootstrap the account-region lookup.
	/// </summary>
	/// <param name="region">The region code.</param>
	/// <returns>The resolved Fleet API base URL.</returns>
	public static string ResolveBaseUrl (string? region) =>
		region?.Trim ().ToLowerInvariant () switch
			{
				"eu" => EUROPE,
				"cn" => CHINA,
				_ => NORTH_AMERICA
				};

	internal static bool IsAutomatic (string? region) =>
		region is null || string.IsNullOrWhiteSpace (region) || string.Equals (region.Trim (), "auto", StringComparison.OrdinalIgnoreCase);

	internal static string? ValidateDiscoveredUrl (string? region, string? baseUrl)
		{
		if (region?.ToLowerInvariant () is not ("na" or "eu" or "cn") || baseUrl is null || string.IsNullOrWhiteSpace (baseUrl))
			return null;
		string expected = ResolveBaseUrl (region);
		// Never send bearer credentials to an arbitrary URL supplied in a response.
		return string.Equals (expected, baseUrl.TrimEnd ('/'), StringComparison.OrdinalIgnoreCase) ? expected : null;
		}
	}

internal sealed class FleetAccountRegion
	{
	[System.Text.Json.Serialization.JsonPropertyName ("region")]
	public string? Region
		{
		get; set;
		}

	[System.Text.Json.Serialization.JsonPropertyName ("fleet_api_base_url")]
	public string? BaseUrl
		{
		get; set;
		}
	}