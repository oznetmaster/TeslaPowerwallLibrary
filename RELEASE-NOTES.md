# TeslaPowerwallLibrary 2.3.1

Fix signed LAN requests expiring during RSA-4096 signature generation on slower supported runtimes. The signed-request validity window is now 60 seconds from the start of signature generation, allowing slower signing to finish before delivery. The HTTP request timeout remains independently configured.

The correction uses the existing RSA key and SHA-512/PKCS#1 v1.5 protocol. The public API, runtime dependencies and assembly identity remain compatible. Requests receive fresh identifiers and finite expirations; control commands are not automatically replayed.

Validation covers both TEDAPI query sets, offline tests on .NET 10 and .NET Framework 4.7.2, and read-only live comparisons with a controlled 20-second signing delay. The former 13-second window reproduced `RrorTimeout`; the corrected window returned current battery data. Native embedded-runtime validation also confirmed the correction with real RSA-4096 signing.

NuGet/file versions are `2.3.1`/`2.3.1.0`; assembly identity remains `2.0.0.0`. Package validation compares both targets with released 2.3.0 without compatibility suppressions. See the [changelog](CHANGELOG.md), [local-access guide](LOCAL-ACCESS.md) and [development history](DEVELOPMENT-HISTORY.md).
