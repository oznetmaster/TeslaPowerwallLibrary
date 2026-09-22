
## Explicit live API validation

The three read-only Live cases validate site selection, typed power/battery readings and refreshed operating configuration. Run this suite separately with dedicated Owner and Fleet credential-helper sessions: the APIs use distinct connections. EnableLiveTests must be true when using the helper-generated private LiveTestSettings.json input. Refresh tokens never enter the test host. The processor live suite is manual-only; the 142 deterministic cases remain credential-free.
