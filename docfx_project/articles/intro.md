# Getting started

Tesla and Powerwall are trademarks of Tesla, Inc. This project is an independent, unofficial .NET library
and is not affiliated with or endorsed by Tesla.

## Installation

```bash
dotnet add package TeslaPowerwallLibrary
```

## Connect to a local gateway

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
    Host = "10.0.1.99",
    Password = "your-customer-password"
};

using var powerwall = new Powerwall(options);
await powerwall.ConnectAsync();
```

## Connect using the Tesla Owners cloud API

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

After the first successful cloud connect, the library persists the (possibly rotated) tokens internally,
keyed by `Email`, so later runs can omit `AccessToken` and `RefreshToken` entirely. `AccessToken` is optional
even on a first connect: when omitted (or stale), the library silently derives a new one from `RefreshToken`.

## Read status and history

```csharp
var status = await powerwall.StatusAsync();
var systemStatus = await powerwall.SystemStatusAsync();

// Strongly typed calendar-history convenience methods (cloud mode only) deserialize the raw JSON for you,
// directly into records with a few computed convenience properties layered on top (for example
// EnergyHistoryPoint.SolarKwh, which sums and converts the underlying raw watt-hour fields):
IReadOnlyList<EnergyHistoryPoint> energyHistory = await powerwall.GetEnergyCalendarHistoryAsync(HistoryPeriod.Day);
IReadOnlyList<PowerHistoryPoint> powerHistory = await powerwall.GetPowerCalendarHistoryAsync(HistoryPeriod.Day);
IReadOnlyList<StateOfEnergyHistoryPoint> soeHistory = await powerwall.GetStateOfEnergyCalendarHistoryAsync(HistoryPeriod.Day);
IReadOnlyList<SelfConsumptionHistoryPoint> selfConsumptionHistory = await powerwall.GetSelfConsumptionCalendarHistoryAsync(HistoryPeriod.Day);
BackupHistory backupHistory = await powerwall.GetBackupCalendarHistoryAsync(HistoryPeriod.Day);

// Or call GetCalendarHistoryAsync directly for the raw JSON body (any kind, including time_of_use_energy and savings):
string? rawEnergyHistory = await powerwall.GetCalendarHistoryAsync("energy", period: "day");
```

## Notes

- The library supports both direct local network access to the Powerwall gateway and Tesla Owners (cloud) API access; select the mode using `PowerwallOptions`.
- Local mode requires the gateway host/IP address and the customer password configured on the gateway.
- Local mode targets the Gateway 2 / Powerwall+ local REST API. Powerwall 3 gateways use a different local protocol (TEDAPI) that is not yet implemented in this library and reject the plain REST endpoints with a `403` error; Powerwall 3 owners should use Cloud mode for now.
- Cloud mode requires a Tesla account email and OAuth tokens obtained via the Tesla login flow.
- The .NET implementation is adapted from the Python `pypowerwall` project.

