# Test credential tooling — unreleased

- Add independent Owner and Fleet test profiles configured with refresh credentials, with access tokens managed internally.
- Persist rotation using Windows user encryption and atomic replacement; retain exclusive ownership throughout interactive or automated test sessions.
- Produce private remote-test inputs without refresh credentials. Test package removal does not remove the Windows credential store.
- Block uncertain authentication retries and provide explicit release for a prepared session only after its tests have stopped.
- Simplify Fleet authorization for registered applications in the Setup app, with optional encrypted application settings, embedded sign-in, automatic callback validation/code exchange and a manual browser fallback.
- Recognize local access as a separate future testing mode; do not fall back to cloud authentication.

Validated with 47 offline credential, simulated-renewal, callback and privacy tests, plus 118 library tests on both net472 and .NET 10. Dedicated Owner and Fleet credentials each passed three read-only live tests on Windows and three on a remote test host, with normal profile release and private-input cleanup. A wrong explicit Fleet region was corrected during validation. Automatic account-region discovery also passed a live Windows connection and three read-only live tests on the remote test host. The Australian account resolved to the North America/Asia-Pacific endpoint; other regional routing is covered by offline tests. The Setup app builds with embedded Fleet sign-in and a manual fallback.

Library 1.2.5 corrects Fleet token endpoints/form encoding and defaults Fleet connections to automatic account-region discovery. Explicit regional overrides remain available.