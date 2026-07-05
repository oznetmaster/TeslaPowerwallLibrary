# Tesla cloud login (TeslaPowerwallLibrary.Login)

`TeslaPowerwallLibrary.Login` is a small, Windows-only helper library that performs an interactive Tesla™
OAuth 2.0 (PKCE) browser login and returns the resulting cloud tokens. It is a separate assembly from the
main `TeslaPowerwallLibrary` package: it is **not published to NuGet**, is not required for Cloud mode
(you can obtain and supply tokens by any means you like), and is distributed only as a build artifact
(DLL) attached to each [GitHub release](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases).

Tesla and Powerwall are trademarks of Tesla, Inc. This project is an independent, unofficial .NET library
and is not affiliated with or endorsed by Tesla.

## Why a separate library?

`TeslaPowerwallLibrary` targets both .NET Framework 4.7.2 and .NET 10, and works on any platform those
targets support. Interactive login, however, needs a browser control to host the Tesla sign-in page, which
pulls in the Windows-only WebView2 runtime. Keeping that dependency out of the core library means
`TeslaPowerwallLibrary` stays lightweight and cross-platform for consumers who already have tokens or use
Local mode, while `TeslaPowerwallLibrary.Login` remains available for Windows apps that want an in-process,
interactive sign-in experience.

`TeslaPowerwallLibrary.Login` hosts the login page in an embedded WebView2 control on a bare Win32 window
(no WPF or WinForms dependency), so it can be referenced directly by any .NET application — console or
desktop — without requiring a UI framework or launching a separate process.

This library is used internally by the repository's `TeslaPowerwallLibrary.App`, `TeslaPowerwallLibrary.Setup`,
and `TeslaPowerwallLibrary.TestConsole` projects; it is documented here for anyone who wants to reuse it in
their own application.

## Usage

```csharp
using TeslaPowerwallLibrary.Login;

var result = await TeslaCloudLogin.SignInAsync (
	region: "us",
	timeout: TimeSpan.FromMinutes (5));

if (result.Status == TeslaCloudLoginStatus.Success)
	{
	var tokens = result.Tokens!;
	// tokens.Email is the account email Tesla returned in the id_token. Cloud-mode token
	// caching is keyed by email, so pass all three values to PowerwallOptions:
	//   Email = tokens.Email, RefreshToken = tokens.RefreshToken, AccessToken = tokens.AccessToken
	}
```

If a likely email is already known (for example, from a previous sign-in or saved settings), pass it as
the optional `email` parameter to prefill Tesla's sign-in page:

```csharp
var result = await TeslaCloudLogin.SignInAsync (
	region: "us",
	timeout: TimeSpan.FromMinutes (5),
	email: previouslyKnownEmail);
```

`SignInAsync` opens the sign-in window and returns once the user completes authentication, cancels, or the
login fails or times out. The login runs on its own dedicated thread with an independent Win32 message
loop, so it does not require and will not interfere with a caller's existing UI thread or message loop.

- `region` — the Tesla region to authenticate against (`us` or `cn`).
- `timeout` — the maximum time to wait for the user to complete the login.
- `email` — an optional address used only to prefill the sign-in page (sent as Tesla's `login_hint`). This
  is a convenience hint, not a constraint: the user can still complete login with a different account, and
  the returned `tokens.Email` always reflects whichever account actually signed in, which may differ from
  this hint.
- `cancellationToken` — an optional token used to abandon the login early.

The returned `TeslaCloudLoginResult` reports a `Status` of `Success`, `Cancelled`, or `Failed`, along with
the captured `TeslaCloudLoginTokens` (`RefreshToken`, `AccessToken`, `Email`) on success. `Email` is the
account email Tesla returned in the id_token — capture it along with the tokens, because
`TeslaPowerwallLibrary`'s cloud-mode token cache is keyed by email; without it, `PowerwallOptions.Email`
would have to be known ahead of time by some other means for the library to find the cached tokens again on
a later run. The caller is responsible for persisting the returned tokens; `TeslaCloudLogin` performs no
persistence of its own. All three values can then be supplied to `PowerwallOptions` for Cloud mode — see
[Connect using the Tesla Owners cloud API](intro.md#connect-using-the-tesla-owners-cloud-api).

## API reference

See the [Login Library API Reference](../api-login/index.md) for the full type listing.
