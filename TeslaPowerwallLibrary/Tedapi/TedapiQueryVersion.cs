// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Selects the captured, vendor-signed TEDAPI query set. Selection never changes the authentication transport.</summary>
public enum TedapiQueryVersion
	{
	/// <summary>Original June 2024 query format, including additional supported component signal variables.</summary>
	June2024 = 0,
	/// <summary>June 2026 signed GraphQL format. Availability depends on firmware and vendor signing keys.</summary>
	June2026 = 1
	}
