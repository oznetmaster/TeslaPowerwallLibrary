# TeslaPowerwallLibrary 1.2.5

Patch release correcting Fleet authentication and account-region selection. Existing public connection methods remain available.

- Fleet token operations use the documented Fleet authentication endpoint; refresh requests are form encoded.
- Fleet connections discover the account's regional API endpoint automatically. Explicit `na`, `eu` and `cn` settings remain supported; China requires its separate registration and explicit region.
- Add a Windows helper that owns independently issued test credentials, persists refresh-token rotation securely and prepares short-lived inputs for remote tests.
- Simplify Setup sign-in for registered Fleet applications, with optional encrypted application settings and a manual browser fallback.

Validation: 118 library tests passed on each of net472 and .NET 10, and 47 credential/callback/privacy tests passed. Dedicated Owner and Fleet read-only tests passed on Windows and a remote host. Live automatic-region validation used a North America/Asia-Pacific account; other routing is tested offline. Embedded Fleet sign-in still awaits end-to-end interactive validation.

GitHub assets include the applications, login assemblies and the dedicated test-credential helper. Only the main library is published to NuGet. Private credentials and local deployment settings are not included.

See [README](README.md), [changelog](CHANGELOG.md) and [test-credential guide](TeslaPowerwallLibrary.TestCredentials/README.md).
