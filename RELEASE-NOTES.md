# TeslaPowerwallLibrary 2.1.0

Released 8 October 2026.

This release adds local TEDAPI support to the existing Owner, Fleet and classic gateway clients. Signed Powerwall 3 connections over Ethernet or home Wi-Fi provide on-demand local telemetry without cloud authentication during normal operation.

## Library

- Typed controller, battery, expansion, meter, PV, temperature, fan, firmware and diagnostic readings.
- Signed LAN, setup-network Basic and installer bearer transports; explicit RSA signing-key registration and status through an existing Owner or Fleet connection.
- Hostname/IP configuration, DNS-SD discovery, bounded LAN DNS resolution, configurable caching and optional local read failover.
- Explicit partial operating settings, manual backup and grid controls. TEDAPI writes require signed access and session permission.
- Nullable `GetPowerReadingsAsync()` for callers that need to distinguish missing data from a measured zero.

The existing `PowerAsync()`/`PowerSnapshot` contract, public API and classic gateway defaults remain compatible with 2.0.0. See [upgrading to 2.1](UPGRADING-2.1.md). The library continues to target .NET Framework 4.7.2 and .NET 10.

See [choosing a connection](LOCAL-ACCESS.md#choosing-a-connection) for authentication, data freshness and the tradeoffs between cloud and local access.

## Desktop app and console

The Windows desktop app supports local connections, detailed diagnostics, configurable refresh and read-only controls that can be enabled for a session. Local samples take precedence over overlapping cloud history. Earlier history can be retrieved through a saved Owner or Fleet account for the associated site and cached by the app. Power charts retain missing intervals; energy charts use bars.

The console exposes local discovery, connection details, telemetry, diagnostics, enrollment and guarded commands in one-shot and interactive modes. Reads are on demand. Both tools display missing telemetry as unavailable.

## Validation and limits

The new 2.1.0 package is checked for backward compatibility with the previous 2.0.0 release on both target frameworks, without suppressing API differences. Offline tests cover protocol framing, attributed models, missing values, local defaults, control guards, history, console commands and desktop presentation. See [test instructions](TeslaPowerwallLibrary.Tests/README.md) and [development validation](DEVELOPMENT-HISTORY.md) for results.

Signed LAN reads using both bundled query versions were exercised on one Powerwall 3 on net472 and .NET 10. The default signed query set also passed read-only checks through the Powerwall's home Wi-Fi address on both runtimes, confirming the same device identity and existing signing key. Backup reserve and operating-mode changes were confirmed and restored; Owner Storm Watch was separately confirmed and restored. Setup-network access, installer bearer, multiple physical devices and local failover have offline coverage only. Grid switching, backup scheduling, self-test, phase-detection and firmware-update procedures were not executed on the house system.

See [local access](LOCAL-ACCESS.md) for the capability matrix, pinned upstream protocol reference and exact validation boundaries. No unconditional claim of compatibility with every hardware or firmware variant is made.
