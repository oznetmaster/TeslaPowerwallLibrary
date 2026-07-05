# TeslaPowerwallLibrary

`TeslaPowerwallLibrary` is a .NET client library for interacting with a Tesla™ Powerwall™ system, either
over the local gateway network API or the Tesla Owners cloud API.

Tesla and Powerwall are trademarks of Tesla, Inc. This project is an independent, unofficial .NET library
and is not affiliated with or endorsed by Tesla.

## What this library provides

- Local network access to the Powerwall gateway (status, power flow, history, and control)
- Tesla Owners (cloud) API access, including interactive OAuth login and token persistence
- Access to gateway status, system status, grid status, and energy history
- Control helpers such as backup reserve level, operating mode, grid charging, and grid export
- Multi-target support for .NET Framework 4.7.2 and .NET 10

## Gateway hardware compatibility

Local mode's plain HTTPS/JSON REST API is the original Gateway 2 / Powerwall+ local interface and is well established on that hardware. On Powerwall 3, Tesla replaced this local REST API with TEDAPI (protobuf-encoded, RSA-signed); the plain REST endpoints return a `403` error on a Powerwall 3 gateway. Powerwall 3 owners need TEDAPI support (not yet implemented in this library) for local access; Cloud mode works today regardless of gateway generation.

## Origins

This project is adapted from the Python project [pypowerwall](https://pypi.org/project/pypowerwall/) by Jason A. Cox.

## Documentation sections

- [Getting started](articles/intro.md)
- [API reference](api/index.md)

