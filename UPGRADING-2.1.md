# Upgrading to 2.1

Version 2.1 adds local TEDAPI access and preserves the 2.0 public API on .NET Framework 4.7.2 and .NET 10. Existing Owner and Fleet connection options remain valid. The assembly identity remains `2.0.0.0`; the NuGet and file versions are `2.1.0` and `2.1.0.0`. Update the package and deploy its resolved dependencies together.

## Power readings

`PowerAsync()` still returns `PowerSnapshot`, whose `Site`, `Solar`, `Battery` and `Load` properties are non-nullable `double` values. Unavailable flows retain their previous zero defaults. This is a compatibility contract, not confirmation of a measured zero. Existing single-flow helpers retain their behavior too.

New code should use `GetPowerReadingsAsync()`, available on `Powerwall` and `PowerwallClientBase`. It returns `PowerReadings` with nullable `double?` properties using the same JSON wire names and watt units. Reported zero remains zero; missing values remain null. Existing derived clients do not need to implement a new abstract member.

```csharp
var power = await powerwall.GetPowerReadingsAsync();
string solar = power.Solar is double watts ? $"{watts:N0} W" : "Unavailable";
```

The desktop app and console use this new method. They do not replace missing telemetry with zero. The new `EnergyHistoryPoint.BatteryToHomeKwh` property separately exposes reported battery contribution to home loads; it is null when unreported and excludes battery export to the grid.

## Classic gateway defaults

For `LocalProtocol = PowerwallLocalProtocol.Gateway` (the existing default), omitted options preserve session persistence and permission to issue explicit control calls. The original positional `PowerwallLocalClient` constructor retains these defaults too. Connecting never sends a control command.

To request a read-only, memory-only classic gateway connection, use the options constructor:

```csharp
var options = new PowerwallOptions
{
    Host = "powerwall.local",
    Password = customerPassword,
    AllowLocalControl = false,
    NoLocalSessionPersistence = true
};
```

Explicit values override protocol defaults regardless of initializer order. Record copies preserve explicitly chosen values. If no value was chosen, changing `LocalProtocol` selects that protocol's default.

Classic session files retain their existing format. Applications that enable persistence should keep the cache in private storage. Local requests now reject invalid host authorities and redirects away from the configured device, preserve cancellation and invalidate cached readings after session changes. Unsupported endpoints and missing authentication tokens are reported as failures or unavailable data rather than successful reads.

## New TEDAPI connections

TEDAPI protocols are opt-in and do not replace the classic gateway selection. They default to read-only and use memory-only sessions. Signed LAN controls require `AllowLocalControl = true`. The desktop app and console explicitly choose read-only, memory-only local sessions until the user enables controls for that session.

Signed LAN works over Ethernet or home Wi-Fi and requires a local customer password and a verified, caller-owned RSA-4096 key. Cloud-assisted key enrollment is explicit and separate from runtime access. Local runtime never falls back to Owner or Fleet. See [local access](LOCAL-ACCESS.md) for setup, supported calls, discovery, cache behavior, query versions and hardware validation limits.

## Dependencies and earlier upgrades

Local protocol messages use the existing `Google.Protobuf` runtime dependency. Restore the complete NuGet dependency graph; do not copy only the main library DLL. Protocol generation tools are build-only dependencies. SQLite is confined to the Windows desktop app and is not a library dependency.

For upgrades from 1.x, also follow the [2.0 migration guide](MIGRATION-SystemTextJson.md). The 2.1 additions do not undo those earlier breaking changes.
