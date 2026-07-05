# TeslaPowerwallLibrary

A .NET client library for the Tesla™ Powerwall™ Energy Gateway, providing direct local-network access and Tesla Owners (cloud) API access to status, power flow, energy history, and control operations.

Tesla and Powerwall are trademarks of Tesla, Inc. This project is an independent, unofficial .NET library and is not affiliated with or endorsed by Tesla.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](TeslaPowerwallLibrary/LICENSE)

## Supported Platforms

| Target Framework | Supported |
| --- | --- |
| .NET 10 | ✅ |
| .NET Framework 4.7.2 | ✅ |

## Overview

`TeslaPowerwallLibrary` provides a strongly typed, async-first .NET wrapper around the Tesla Energy Gateway's local API and the Tesla Owners cloud API. It supports:

- Local network access to the Powerwall gateway (status, power flow, grid status, system status, and energy/calendar history)
- Tesla Owners (cloud) API access, including interactive OAuth login and automatic token persistence
- Control operations such as backup reserve level, operating mode, grid charging, and grid export
- Response caching with configurable expiry to reduce load on the gateway
- Multi-target support for .NET Framework 4.7.2 and .NET 10

This .NET library is adapted from the Python [pypowerwall](https://pypi.org/project/pypowerwall/) project by Jason A. Cox. See the [license](TeslaPowerwallLibrary/LICENSE) for attribution details.

### Connection modes

| Mode | Status | Notes |
| --- | --- | --- |
| Local | ✅ Implemented | Direct HTTPS/JSON access to the gateway using the customer password. Targets the Gateway 2 / Powerwall+ local REST API (see hardware note below). |
| Cloud | ✅ Implemented | Tesla Owners API access using interactive OAuth login and cached tokens. Works regardless of gateway generation. |
| FleetAPI | 🚧 Not yet implemented | Scaffolded for a future milestone; currently throws a not-implemented exception. |
| TEDAPI | 🚧 Not yet implemented | Scaffolded protobuf-based local link-local access for a future milestone; currently throws a not-implemented exception. |

> **Gateway hardware compatibility:** Local mode's plain HTTPS/JSON REST API (`/api/login/Basic` plus endpoints such as `/api/system_status/soe` and `/api/meters/aggregates`) is the original Gateway 2 / Powerwall+ local interface and is well established on that hardware. On Powerwall 3, Tesla replaced this local REST API with TEDAPI (protobuf-encoded, RSA-signed); the plain REST endpoints return `403 Unable to GET to resource` on a Powerwall 3 gateway. Powerwall 3 owners need TEDAPI support (not yet implemented — see the table above) for local access; Cloud mode works today regardless of gateway generation.

## Installation

The library is available as the `TeslaPowerwallLibrary` NuGet package.

```powershell
dotnet add package TeslaPowerwallLibrary
```

## Quick Start

### Connect to a local gateway

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
	 Host = "10.0.1.99",
	 Password = "your-customer-password"
};

using var powerwall = new Powerwall(options);
await powerwall.ConnectAsync();

var status = await powerwall.StatusAsync();
```

### Connect using the Tesla Owners cloud API

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
	 Email = "you@example.com",
	 CloudMode = true,
	 AccessToken = "your-access-token",
	 RefreshToken = "your-refresh-token"
};

using var powerwall = new Powerwall(options);
await powerwall.ConnectAsync();
```

After the first successful cloud connect, the library persists the (possibly rotated) tokens internally, keyed by `Email`, so later runs can omit `AccessToken` and `RefreshToken` entirely.

## Repository Contents

- `TeslaPowerwallLibrary` — the main library project published to NuGet
- `TeslaPowerwallLibrary.Login` — shared Tesla cloud OAuth login library (interactive WebView2-based browser login), used by both the app and the test console; not published to NuGet, distributed as a DLL attached to each [GitHub release](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases) — see the [Tesla cloud login guide](https://oznetmaster.github.io/TeslaPowerwallLibrary/articles/login.html)
- `TeslaPowerwallLibrary.App` — a WPF dashboard application with live energy charts, system status, and site/account management
- `TeslaPowerwallLibrary.TestConsole` — a command-line and interactive test harness covering the library's read and control operations
- `TeslaPowerwallLibrary.Setup` — a small WPF wrapper around `TeslaPowerwallLibrary.Login` that performs a standalone Tesla cloud login and displays the resulting tokens
- `TeslaPowerwallLibrary.Tests` — MSTest-based deterministic unit test coverage

## Documentation

Public documentation for this repository is available on GitHub Pages:

- https://oznetmaster.github.io/TeslaPowerwallLibrary/

## Acknowledgements

This library is adapted from the Python [pypowerwall](https://pypi.org/project/pypowerwall/) project by Jason A. Cox.

## License

MIT © 2026 Neil Colvin, © 2022 Jason Cox — see [LICENSE](TeslaPowerwallLibrary/LICENSE).
