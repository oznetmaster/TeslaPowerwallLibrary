// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
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
	/// Unrecognized or absent values fall back to <see cref="NORTH_AMERICA"/>, matching
	/// <see cref="PowerwallOptions.FleetApiRegion"/>'s default.
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
	}
