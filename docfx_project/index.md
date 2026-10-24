# TeslaPowerwallLibrary

A typed .NET client for Tesla™ Powerwall™ classic gateway, local TEDAPI, Owner and Fleet APIs, targeting .NET Framework 4.7.2 and .NET 10.

Tesla and Powerwall are trademarks of Tesla, Inc. This independent, unofficial project is not affiliated with or endorsed by Tesla.

## Version 2.3.1

Version 2.3.1 fixes signed requests expiring during slower RSA-4096 signature generation by allowing a finite 60-second validity window. The HTTP request timeout is independent. Version 2.3 added fourteen PW2 MSA signals, conservative per-battery counter pairing and TEDAPI maximum-power ratings that respect inverter derating. The new nullable `SystemStatus.InverterNominalUsablePower` reports the usable inverter sum. Existing public signatures, assembly identity and classic gateway defaults remain compatible. Both supported TEDAPI query formats remain available; June 2024 is still the default and can request the added signals, while the June 2026 captures remain unchanged.

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
