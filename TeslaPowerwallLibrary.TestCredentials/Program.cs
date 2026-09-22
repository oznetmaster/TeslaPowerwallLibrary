// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See TeslaPowerwallLibrary/LICENSE.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

using TeslaPowerwallLibrary.TestCredentials;

if (args.Length == 0 || args[0] is "help" or "--help")
	{
	Console.WriteLine ("Tesla dedicated test credentials (Windows; Owner and Fleet APIs)");
	Console.WriteLine ("  init PROFILE PRIVATE_JSON    Save an independently issued test credential without contacting Tesla.");
	Console.WriteLine ("  status PROFILE               Show profile readiness; never print tokens.");
	Console.WriteLine ("  run PROFILE -- PROGRAM ARGS  Prepare private inputs and hold ownership until the test command exits.");
	Console.WriteLine ("  session PROFILE              Prepare inputs for an interactive runner; keep this window open until all tests stop.");
	Console.WriteLine ("  release PROFILE --confirm-tests-stopped  Release a held, authenticated run after verifying no tests remain active. Never retries uncertain authentication.");
	Console.WriteLine ("Use separate cloud/fleet profiles with dedicated credentials. Local access is not supported by this test tool yet.");
	return 0;
	}

try
	{
	if (args.Length < 2 || !Regex.IsMatch (args[1], "\\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,47}\\z")
		|| args[0] is not ("init" or "status" or "run" or "session" or "release")
		|| (args[0] == "init" && args.Length != 3)
		|| (args[0] is "status" or "session" && args.Length != 2)
		|| (args[0] == "release" && (args.Length != 3 || args[2] != "--confirm-tests-stopped"))
		|| (args[0] == "run" && (args.Length < 4 || args[2] != "--")))
		throw new ArgumentException ("Invalid command. Use --help.");
	string directory = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "TeslaPowerwallLibrary", "TestCredentials", args[1]);
	using var store = new CredentialStore (directory);
	if (args[0] == "init")
		{
		using var input = File.OpenRead (args[2]);
		store.Initialize (JsonSerializer.Deserialize<CredentialState> (input, CredentialStore.JsonOptions) ?? throw new InvalidDataException ());
		Console.WriteLine ("Dedicated test credential saved with Windows user encryption. This profile must remain its sole refresh owner. Remove the plaintext seed file when no longer needed.");
		return 0;
		}
	if (args[0] == "status")
		{
		Console.WriteLine (store.Current is null ? "Not initialized." : store.Current.InProgress
			? store.Current.Prepared ? "Held after preparation. Confirm all tests stopped before release. No authentication attempted." : "Held after uncertain authentication. Do not retry the saved refresh token automatically."
			: "Ready. No authentication attempted.");
		return 0;
		}
	if (args[0] == "release")
		{
		store.ReleaseStoppedRun ();
		Console.WriteLine ("Stopped test session released. No authentication attempted.");
		return 0;
		}
	using var diagnostics = new AuthenticationDiagnostics ();
	using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (90));
	await TestPreparation.PrepareAsync (store, deadline.Token, logger: diagnostics);
	store.MarkPrepared ();
	Console.WriteLine ("Private live inputs: " + Path.Combine (store.InputsDirectory, "LiveTestSettings.json"));
	int result = 0;
	if (args[0] == "session")
		{
		Console.WriteLine ("Run the selected live tests. After all tests have stopped, type finished to release this profile.");
		if (Console.ReadLine () != "finished")
			throw new InvalidOperationException ("Session completion was not confirmed.");
		}
	else
		{
		var start = new ProcessStartInfo (args[3]) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
		foreach (string argument in args.Skip (4))
			start.ArgumentList.Add (argument);
		start.Environment["TESLA_LIVE_TEST_DATA_DIRECTORY"] = store.InputsDirectory;
		using var process = Process.Start (start) ?? throw new InvalidOperationException ("Test command could not start.");
		await process.WaitForExitAsync ();
		result = process.ExitCode;
		}
	if (result == 0)
		store.Complete ();
	else
		Console.Error.WriteLine ("The test command failed. Profile retained until all local and remote tests are confirmed stopped; then use release.");
	return result;
	}
catch (NotSupportedException)
	{
	Console.Error.WriteLine ("This connection mode is not supported by the test credential tool yet.");
	return 2;
	}
catch (Exception exception)
	{
	// Exceptions may contain private paths, parameters or server responses.
	Console.Error.WriteLine (AuthenticationDiagnostics.DescribeFailure (exception));
	Console.Error.WriteLine ("Credential operation failed. Check profile status and private settings. A held profile requires investigation before reuse; no automatic retry was made. Credential details withheld.");
	return 1;
	}