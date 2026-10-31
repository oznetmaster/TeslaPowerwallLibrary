// Copyright (c) 2026 Neil Colvin. MIT License; see ../TeslaPowerwallLibrary/LICENSE.
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cake.Common;
using Cake.Core;
using Cake.Frosting;

public sealed record TestSuite(string Name, string Project, string Framework);
public sealed record Application(string Name, string Project, string Framework);

public sealed class BuildContext : FrostingContext
{
	public const string LibraryProject = "TeslaPowerwallLibrary/TeslaPowerwallLibrary.csproj";
	public const string Solution = "TeslaPowerwallLibrary.slnx";
	public const string OfflineFilter = "TestCategory!=Live";
	public static readonly TestSuite[] Suites = [
		new("library-net472", "TeslaPowerwallLibrary.Tests/TeslaPowerwallLibrary.Tests.csproj", "net472"),
		new("library-net10", "TeslaPowerwallLibrary.Tests/TeslaPowerwallLibrary.Tests.csproj", "net10.0"),
		new("console-net472", "TeslaPowerwallLibrary.TestConsole.Tests/TeslaPowerwallLibrary.TestConsole.Tests.csproj", "net472"),
		new("console-net10", "TeslaPowerwallLibrary.TestConsole.Tests/TeslaPowerwallLibrary.TestConsole.Tests.csproj", "net10.0-windows"),
		new("desktop", "TeslaPowerwallLibrary.App.Tests/TeslaPowerwallLibrary.App.Tests.csproj", "net10.0-windows"),
		new("credentials", "TeslaPowerwallLibrary.TestCredentials.Tests/TeslaPowerwallLibrary.TestCredentials.Tests.csproj", "net10.0-windows")];
	public static readonly Application[] Applications = [
		new("TeslaPowerwallTestCredentials", "TeslaPowerwallLibrary.TestCredentials/TeslaPowerwallLibrary.TestCredentials.csproj", "net10.0-windows"),
		new("TeslaPowerwallApp", "TeslaPowerwallLibrary.App/TeslaPowerwallLibrary.App.csproj", "net10.0-windows"),
		new("TeslaPowerwallSetup", "TeslaPowerwallLibrary.Setup/TeslaPowerwallLibrary.Setup.csproj", "net10.0-windows"),
		new("TeslaPowerwallTestConsole", "TeslaPowerwallLibrary.TestConsole/TeslaPowerwallLibrary.TestConsole.csproj", "net472"),
		new("TeslaPowerwallTestConsole", "TeslaPowerwallLibrary.TestConsole/TeslaPowerwallLibrary.TestConsole.csproj", "net10.0-windows")];
	public static readonly string[] Documents = ["README.md", "CHANGELOG.md", "LOCAL-ACCESS.md", "UPGRADING-2.1.md", "RELEASE-NOTES.md"];
	public string Root { get; }
	public string Version { get; }
	public string Output { get; }
	public string? Baseline { get; }
	public TestSuite? SelectedSuite { get; }
	public string Results => Path.Combine(Output, "test-results");
	public string Release => Path.Combine(Output, "release");
	public string NuGet => Path.Combine(Output, "nuget");
	private int _command;
	public BuildContext(ICakeContext context) : base(context)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, Solution))) directory = directory.Parent;
		Root = directory?.FullName ?? throw new InvalidOperationException("Cannot find the repository solution above the build application.");
		var project = XDocument.Load(Path.Combine(Root, LibraryProject));
		Version = context.Argument("version", project.Descendants("Version").Single().Value);
		ValidateVersion(Version);
		string run = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ") + "-" + Guid.NewGuid().ToString("N")[..8];
		Output = Path.GetFullPath(context.Argument("output", Path.Combine(Root, "artifacts", "cake-pilot", run)), Root);
		string allowed = Path.Combine(Root, "artifacts", "cake-pilot") + Path.DirectorySeparatorChar;
		if (!Output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must be a new directory beneath artifacts/cake-pilot/.");
		Baseline = context.HasArgument("baseline") ? Path.GetFullPath(context.Argument<string>("baseline"), Root) : null;
		SelectedSuite = context.HasArgument("suite") ? ResolveSuite(context.Argument<string>("suite"), context.Argument<string?>("framework", null)) : null;
	}
	public static TestSuite ResolveSuite(string name, string? framework = null)
	{
		var matches = Suites.Where(s => (s.Name == name || name == "powerwall" && s.Name.StartsWith("library-", StringComparison.Ordinal)) && (framework == null || s.Framework == framework)).ToArray();
		return matches.Length == 1 ? matches[0] : throw new ArgumentException("Select a known suite and matching framework from build/README.md.");
	}
	public string[] TestArguments(TestSuite suite, bool useBuiltBinaries) =>
		["test", suite.Project, "--configuration", "Release", "--framework", suite.Framework,
			.. (useBuiltBinaries ? new[] { "--no-build", "--no-restore" } : Array.Empty<string>()),
			"--filter", OfflineFilter, "--logger", "trx;LogFileName=" + suite.Name + ".trx", "--results-directory", Results, .. Properties];
	public static void ValidateVersion(string version)
	{
		if (!Regex.IsMatch(version, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z", RegexOptions.CultureInvariant))
			throw new ArgumentException("Use a three-part numeric version, for example 2.3.2.");
	}
	public string[] Properties => ["-p:GenerateDocfxDocumentation=false", "-p:ContinuousIntegrationBuild=true", "-p:Version=" + Version];
	public void Record(string name, object data)
	{
		using var stream = new FileStream(Path.Combine(Output, name + ".json"), FileMode.CreateNew, FileAccess.Write);
		JsonSerializer.Serialize(stream, data, new JsonSerializerOptions { WriteIndented = true });
	}
	public void DotNet(params string[] arguments) => Execute("dotnet", arguments);
	public void Execute(string executable, params string[] arguments)
	{
		int sequence = ++_command;
		string prefix = Path.Combine(Output, $"command-{sequence:D2}");
		var start = new ProcessStartInfo(executable) { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (string argument in arguments) start.ArgumentList.Add(argument);
		DateTimeOffset begun = DateTimeOffset.UtcNow;
		Record($"command-{sequence:D2}-intent", new { executable, arguments, workingDirectory = Root, startedUtc = begun });
		Console.WriteLine($"> {executable} {string.Join(' ', arguments)}");
		using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + executable);
		using var log = TextWriter.Synchronized(new StreamWriter(prefix + ".log", append: false));
		async Task Copy(StreamReader reader)
		{
			while (await reader.ReadLineAsync() is { } line) { log.WriteLine(line); Console.WriteLine(line); }
		}
		Task stdout = Copy(process.StandardOutput), stderr = Copy(process.StandardError);
		if (!process.WaitForExit((int)TimeSpan.FromMinutes(15).TotalMilliseconds))
		{
			process.Kill(entireProcessTree: true); process.WaitForExit();
			Task.WaitAll(stdout, stderr);
			throw new TimeoutException("Build command exceeded 15 minutes.");
		}
		Task.WaitAll(stdout, stderr);
		Record($"command-{sequence:D2}-result", new { exitCode = process.ExitCode, completedUtc = DateTimeOffset.UtcNow, elapsedSeconds = (DateTimeOffset.UtcNow - begun).TotalSeconds });
		if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} failed with exit code {process.ExitCode}; see {prefix}.log");
	}
}
