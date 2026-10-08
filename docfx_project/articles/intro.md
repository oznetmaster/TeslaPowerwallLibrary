# Getting started

Tesla and Powerwall are trademarks of Tesla, Inc. This independent, unofficial library is not affiliated with or endorsed by Tesla.

## Installation

```text
dotnet add package TeslaPowerwallLibrary
```

The library targets .NET Framework 4.7.2 and .NET 10. Restore and deploy all NuGet dependencies.

Compare [connection methods and their tradeoffs](local-access.md#choosing-a-connection) before selecting a backend.

## Classic local gateway

```csharp
using TeslaPowerwallLibrary;

using var powerwall = new Powerwall(new PowerwallOptions
{
    Host = "powerwall.local",
    Password = customerPassword,
    AllowLocalControl = false,
    NoLocalSessionPersistence = true
});
if (!await powerwall.ConnectAsync())
    throw new InvalidOperationException("Unable to connect to the gateway.");

var power = await powerwall.GetPowerReadingsAsync();
// Values are watts. Missing readings are null; measured zero is zero.
```

Existing callers can keep `PowerAsync()` with its non-nullable `PowerSnapshot` and released zero defaults. See [compatibility](upgrading.md).

## Signed Powerwall 3 LAN over Ethernet or home Wi-Fi

Select `PowerwallLocalProtocol.TedapiSigned`, a local customer password and an already verified RSA-4096 key. The [local-access guide](local-access.md) covers key preparation/enrollment, a complete connection example, discovery, typed reads and guarded commands. Local runtime has no cloud dependency or automatic cloud fallback.

## Owner or Fleet

Use `CloudMode = true` with Owner OAuth credentials, or `FleetApi = true` with a registered client ID and Fleet credentials. Initial authorization is handled by the [companion login tools](login.md). Existing token persistence and refresh options remain available; host-managed token storage can opt out of library persistence.

Both cloud modes provide typed calendar-history methods such as `GetEnergyCalendarHistoryAsync(HistoryPeriod.Day)`. Local telemetry does not provide that cloud archive. The Windows desktop app can associate a local device with its cloud site and maintain history itself; the library performs no automatic polling or database writes for history.

## Reads and controls

`CacheExpireSeconds` controls response reuse. The caller decides when to read. New TEDAPI connections are read-only unless signed local control is explicitly enabled. Classic gateway defaults remain compatible with earlier releases; the example above opts into read-only, memory-only behavior.

Firmware and diagnostic queries read status; they do not start updates, self-tests or phase-detection procedures. Hardware and firmware coverage is recorded in the local-access guide.
