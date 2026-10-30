# TeslaPowerwallLibrary

A typed .NET client for Tesla™ Powerwall™ classic gateway, local TEDAPI, Owner and Fleet APIs, targeting .NET Framework 4.7.2 and .NET 10.

Tesla and Powerwall are trademarks of Tesla, Inc. This independent, unofficial project is not affiliated with or endorsed by Tesla.

## Version 2.3.2

Version 2.3.2 fixes false solar-production dips during brief TEDAPI `SolarMeterComms` dropouts. Complete configured wired/Wi-Fi remote-meter samples within 60 seconds of gateway time supply retained solar power, with load corrected by the same delta. Nullable `MeterReading.PowerHoldAgeSeconds` identifies corrected readings and their age. Raw telemetry, measured currents and energy counters retain their reported values. Existing public signatures, assembly identity, dependencies and classic gateway defaults remain compatible. Both TEDAPI query formats are supported; June 2024 remains the default. Dropout behavior has offline regression coverage; a live remote-meter dropout has not been reproduced locally.

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
