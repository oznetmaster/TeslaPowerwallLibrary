// Copyright (c) 2026 Neil Colvin. MIT License; see repository LICENSE.
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Models;

/// <summary>A battery observation that preserves an absent percentage instead of inventing zero.</summary>
internal sealed record BatteryLevelResponse
	{
	/// <summary>Gets the raw numeric battery percentage, or null when absent.</summary>
	/// <value>A raw gateway-scale percentage; numeric strings are rejected by this validated contract.</value>
	[JsonPropertyName ("percentage")]
	[JsonNumberHandling (JsonNumberHandling.Strict)]
	public double? Percentage { get; init; }
	}
