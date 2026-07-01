# Getting started

Drayton, Wiser, and Schneider Electric are trademarks of Schneider Electric SE, its subsidiaries, or affiliated companies. This project is an independent, unofficial .NET library and is not affiliated with or endorsed by Schneider Electric.

## Installation

```bash
dotnet add package WiserHeatAPIv2
```

## Connect to a hub

```csharp
using WiserHeatApiV2;

var api = new WiserAPI("192.168.1.50", "your-wiser-secret");
await api.InitializeAsync();
```

## Discover hubs on the local network

```csharp
using WiserHeatApiV2;

List<WiserDiscoveredHub> hubs = await WiserDiscovery.DiscoverHubAsync(60, 2);
foreach (var hub in hubs)
{
    Console.WriteLine(hub.Url);
}
```

## Refresh hub state

```csharp
await api.ReadHubDataAsync();
```

## Notes

- The library communicates with a local Wiser hub, not a cloud API.
- A valid Wiser hub IP address or discovered hub endpoint and hub secret are required.
- The .NET implementation is adapted from the Python `wiserHeatAPIv2` project.
