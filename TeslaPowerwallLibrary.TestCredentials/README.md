# Dedicated test credentials

This Windows .NET 10 command-line tool owns credentials issued specifically for testing. It uses the same refresh-token configuration as the library: an Owner API refresh token, or a Fleet API client ID and refresh token. Access tokens are obtained internally. It does not use the test console's saved account or cache, and it never reads credentials from another running application.

| Mode | Initial settings | Current support |
| --- | --- | --- |
| `cloud` — Tesla Owner API | Separately issued refresh token and site ID | Implemented; live authentication, Windows tests and a remote test workflow validated |
| `fleet` — Tesla Fleet API | Separately issued refresh token, client ID and site ID; optional region override | Implemented; read-only Windows and remote live validation passed |
| `local` — gateway access | Will follow the local connection's actual authentication requirements | Reserved; rejected explicitly until the local test integration is implemented |

Owner API authentication has been validated with a dedicated test credential, including three live tests on Windows, three on a remote test host, and normal credential-profile release after both sessions. Dedicated Fleet authentication and three read-only tests on each host have also passed. Region defaults to auto; explicit na/eu/cn overrides remain available. Simulated renewal tests do not establish live Fleet authentication or recovery from an interrupted real token exchange.

## Initial authorization

Use `TeslaPowerwallLibrary.Setup` to issue a **separate test credential**. Keep Owner and Fleet credentials in separate named profiles. Independently issued credentials can rotate separately; copying one refresh token to two clients does not create independent credentials. Do not copy a token from an installed application or initialize multiple profiles with the same token.

For an already registered Fleet application, **Sign in to Tesla** skips PEM verification and partner registration. **Remember application settings** optionally saves the client ID, client secret, domain, redirect URI and region encrypted for the current Windows user. Unchecking it removes that saved application profile. Issued tokens are not saved by the setup app. Complete sign-in/consent in the embedded Tesla window; callback capture, validation and code exchange are automatic. The optional manual browser fallback accepts the complete redirected URL with the same validation.

Create a private copy of [FleetSettings.example.json](FleetSettings.example.json) or [OwnerSettings.example.json](OwnerSettings.example.json) outside the repository, then initialize a profile.

```powershell
dotnet run --project TeslaPowerwallLibrary.TestCredentials -- init fleet-tests "$env:LOCALAPPDATA/TeslaTestSeed.json"
dotnet run --project TeslaPowerwallLibrary.TestCredentials -- status fleet-tests
```

Initialization performs no network request. The store is encrypted with Windows DPAPI for the current user, under `%LOCALAPPDATA%/TeslaPowerwallLibrary/TestCredentials/PROFILE`. An existing store cannot be overwritten with the original seed. After successful initialization, remove the plaintext seed when no longer needed. Do not put real settings, stores or output in Git, release assets or CI artifacts.

## Run tests

For an interactive test runner:

```powershell
dotnet run --project TeslaPowerwallLibrary.TestCredentials -- session fleet-tests
```

The helper renews credentials if necessary and displays the private `RunInputs/LiveTestSettings.json` path. Supply that file to the selected live suite. Keep the session open throughout testing. After all tests stop, type `finished`. Select the freshly generated input file for each new session; a runner's previously cached copy may have expired.

For automation, wrap a **blocking** test command:

```powershell
dotnet run --project TeslaPowerwallLibrary.TestCredentials -- run fleet-tests -- dotnet test path/to/Tests.csproj --filter TestCategory=Live --settings path/to/Live.runsettings
```

Use a local copy of [Live.runsettings.example](Live.runsettings.example) to supply the NUnit enablement parameter. The child inherits `TESLA_LIVE_TEST_DATA_DIRECTORY`, pointing to the generated input directory. Fixtures can use that directory when NUnit's `TestDataDirectory` parameter is absent. For a remote workflow, configure its private input path to the profile's `RunInputs/LiveTestSettings.json` and wrap the complete workflow command. Never wrap a command that launches tests in the background and exits early.

Live tests remain opt-in: generated JSON has `enabled: false`. Use the runner's explicit live-suite selection or the NUnit `EnableLiveTests=true` parameter. Preparing credentials alone does not enable device operations.

## Ownership and recovery

One process holds an exclusive profile lock across authentication and the entire test command/session. Rotated credentials are saved synchronously using encrypted, atomic file replacement. The latest refresh token survives test-package removal, rebuilds and ordinary Windows restarts. Use the same Windows account for subsequent runs; CI service accounts need their own initialized profile and loaded Windows profile. Do not copy a user-encrypted store to another account.

Only the desktop helper renews the dedicated test credential. Remote input files contain a short-lived access token and site settings, never the refresh token. This is internal plumbing: test users configure refresh credentials once and do not manage access tokens manually. An access token can expire during a long test session; stop the session and start another. The helper does not renew it while tests are using the snapshot.

A successful blocking test command removes generated inputs and releases the profile. A failed command or interrupted interactive session retains the profile until the developer verifies that all tests have stopped:

```powershell
dotnet run --project TeslaPowerwallLibrary.TestCredentials -- release fleet-tests --confirm-tests-stopped
```

Release is allowed only after authentication and token persistence completed. An interruption during authentication is held separately: it might have lost a newly issued token before it could be persisted. The tool refuses automatic retry or release in that case. Investigate before replacing the dedicated test authorization; neither delete the held store nor reuse the original seed as an automatic recovery step. There is no automatic recovery from an ambiguous OAuth exchange.

## Validation and limitations

The offline tests exercise Owner/Fleet option selection, simulated rotation through the preparation flow, reopening the latest stored token, competing owners, interruption recovery, encrypted persistence, callback validation and omission of refresh tokens from remote inputs. These tests contact no Tesla endpoints.

This tool manages test authentication. A live fixture receiving a prepared access token tests real API data and application behavior, but does not exercise the application's own refresh-token persistence path. That path needs separate lifecycle tests. Local gateway authentication will have its own settings and tests; it must not fall back to an unrelated cloud mode.
