# TeslaPowerwallLibrary

A typed .NET client for Tesla™ Powerwall™ classic gateway, local TEDAPI, Owner and Fleet APIs, targeting .NET Framework 4.7.2 and .NET 10.

Tesla and Powerwall are trademarks of Tesla, Inc. This independent, unofficial project is not affiliated with or endorsed by Tesla.

## Version 2.5.1

Version 2.5.1 coordinates overlapping reads, authentication, token rotation, site changes and disposal, and protects cache/file publication. No device commands or runtime dependencies are added. Use one client per credential set; token notifications must remain non-blocking. See the release notes for the concurrency contract and validation limits.

## Version 2.5.0

Version 2.5.0 adds `GetBatteryLevelAsync` for validated raw or scaled battery readings, with an optional forced observation. Forced reads bypass measurement caches while preserving authentication, backoff, cancellation and transport exceptions. Missing or invalid measurements remain null; real zero is preserved. Both battery APIs share the 0% floor at the 5% reserve. Legacy `LevelAsync` retains its signature, caching and parsing defaults. Existing assembly identity and runtime dependencies remain compatible.

- [Getting started](articles/intro.md)
- [Choosing a connection](articles/local-access.md#choosing-a-connection)
- [Local access, commands and hardware validation](articles/local-access.md)
- [Compatibility and upgrading](articles/upgrading.md)
- [Release notes](articles/release-notes.md)
- [API reference](api/index.md)
- [Obtain Owner or Fleet credentials](articles/login.md#using-the-setup-app)

The library makes requests when called; consumers control refresh and storage. Calendar history uses Owner or Fleet. TEDAPI runtime stays local; key enrollment is an explicit, separate operation.

Signed LAN has live evidence over Ethernet and home Wi-Fi on one Powerwall 3. Setup-network, installer-bearer and multi-device paths have offline coverage. See the local-access guide for the exact validation boundary.

Protocol reference: [pypowerwall](https://github.com/jasonacox/pypowerwall) by Jason A. Cox. Bundled protocol/query resources retain its MIT notice.
