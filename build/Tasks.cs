// Copyright (c) 2026 Neil Colvin. MIT License; see ../TeslaPowerwallLibrary/LICENSE.
using System.IO.Compression;
using System.Xml.Linq;
using Cake.Frosting;

[TaskName("Initialize")]
public sealed class Initialize : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This full-solution pilot includes WPF and .NET Framework tests and requires Windows.");
		if (Directory.Exists(context.Output) || File.Exists(context.Output)) throw new IOException("Preserve the existing run; choose a new --output directory.");
		Directory.CreateDirectory(context.Output);
		context.Record("run", new { version = context.Version, repository = context.Root, output = context.Output, baseline = context.Baseline, startedUtc = DateTimeOffset.UtcNow, liveTests = false, processorTests = false, publication = false });
		Directory.CreateDirectory(context.Results);
		context.DotNet("--version");
	}
}

[TaskName("Restore"), IsDependentOn(typeof(Initialize))]
public sealed class Restore : FrostingTask<BuildContext>
{
	// Preserve the existing workflow's restore evaluation: companion project
	// versions in project.assets.json otherwise change their generated deps files.
	public override void Run(BuildContext context) => context.DotNet("restore", BuildContext.Solution);
}

[TaskName("Build"), IsDependentOn(typeof(Restore))]
public sealed class Build : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context) => context.DotNet(["build", BuildContext.Solution, "--configuration", "Release", "--no-restore", .. context.Properties]);
}

[TaskName("Test"), IsDependentOn(typeof(Build))]
public sealed class Test : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		foreach (var suite in BuildContext.Suites)
			context.DotNet(context.TestArguments(suite, useBuiltBinaries: true));
		var results = BuildContext.Suites.Select(suite => ReadResult(context.Results, suite)).ToArray();
		context.Record("desktop-tests", new { filter = BuildContext.OfflineFilter, suites = results, passed = results.Sum(r => r.passed), processorTests = false });
	}
	public static SuiteResult ReadResult(string results, TestSuite suite)
	{
		var trx = XDocument.Load(Path.Combine(results, suite.Name + ".trx"));
		var counters = trx.Descendants().Single(e => e.Name.LocalName == "Counters");
		int passed = (int)counters.Attribute("passed")!, failed = (int)counters.Attribute("failed")!, executed = (int)counters.Attribute("executed")!;
		if (passed == 0 || failed != 0 || passed != executed) throw new InvalidDataException("Incomplete or unsuccessful test run: " + suite.Name);
		return new(suite.Name, suite.Framework, passed, failed, executed);
	}
}
public sealed record SuiteResult(string suite, string Framework, int passed, int failed, int executed);

[TaskName("Suite"), IsDependentOn(typeof(Initialize))]
public sealed class Suite : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		var selected = context.SelectedSuite ?? throw new ArgumentException("Suite requires --suite.");
		context.DotNet(context.TestArguments(selected, useBuiltBinaries: false));
		context.Record("desktop-tests", new { filter = BuildContext.OfflineFilter, suites = new[] { Test.ReadResult(context.Results, selected) }, processorTests = false });
	}
}

[TaskName("CredentialTools"), IsDependentOn(typeof(Suite))]
public sealed class CredentialTools : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		if (context.SelectedSuite?.Name != "credentials") throw new ArgumentException("CredentialTools requires --suite=credentials.");
		context.DotNet(["build", "TeslaPowerwallLibrary.Setup/TeslaPowerwallLibrary.Setup.csproj", "--configuration", "Release", .. context.Properties]);
	}
}

[TaskName("Pack"), IsDependentOn(typeof(Test))]
public sealed class Pack : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
		=> PackLibrary(context);
	public static void PackLibrary(BuildContext context)
	{
		Directory.CreateDirectory(context.NuGet);
		context.DotNet(["pack", BuildContext.LibraryProject, "--configuration", "Release", "--no-build", "--output", context.NuGet, "-p:PackageVersion=" + context.Version, .. context.Properties]);
	}
}

[TaskName("Applications"), IsDependentOn(typeof(Build))]
public sealed class Applications : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		foreach (var app in BuildContext.Applications)
			context.DotNet(["publish", app.Project, "--configuration", "Release", "--no-restore", "--no-build", "--framework", app.Framework,
				"--output", Path.Combine(context.Output, "publish", app.Name + "-" + app.Framework), .. context.Properties]);
	}
}

[TaskName("Package"), IsDependentOn(typeof(Pack)), IsDependentOn(typeof(Applications))]
public sealed class Package : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
		=> Collect(context);
	public static void Collect(BuildContext context)
	{
		Directory.CreateDirectory(context.Release);
		foreach (string document in BuildContext.Documents) File.Copy(Path.Combine(context.Root, document), Path.Combine(context.Release, document));
		File.Copy(Path.Combine(context.Root, "TeslaPowerwallLibrary/Tedapi/Protocol/UPSTREAM-LICENSE.txt"), Path.Combine(context.Release, "pypowerwall-LICENSE.txt"));
		foreach (var (project, frameworks) in new[] { ("TeslaPowerwallLibrary", new[] { "net472", "net10.0" }), ("TeslaPowerwallLibrary.Login", new[] { "net472", "net10.0-windows" }) })
			foreach (string framework in frameworks)
				foreach (string extension in new[] { "dll", "xml" })
				{
					string source = Path.Combine(context.Root, project, "bin", "Release", framework, project + "." + extension);
					if (File.Exists(source)) File.Copy(source, Path.Combine(context.Release, project + "." + framework + "." + extension));
					else if (extension == "dll" || project == "TeslaPowerwallLibrary") throw new FileNotFoundException("Required release file missing.", source);
				}
		foreach (var app in BuildContext.Applications)
		{
			string name = app.Name + "-" + app.Framework;
			ZipFile.CreateFromDirectory(Path.Combine(context.Output, "publish", name), Path.Combine(context.Release, name + ".zip"));
		}
	}
}

[TaskName("Verify"), IsDependentOn(typeof(Package))]
public sealed class Verify : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
		=> Complete(context, desktopTests: true);
	public static void Complete(BuildContext context, bool desktopTests)
	{
		string package = Path.Combine(context.NuGet, "TeslaPowerwallLibrary." + context.Version + ".nupkg");
		ArtifactVerification.VerifyPackage(package, context.Version, context.Root);
		var current = ArtifactVerification.Inventory(context.Output);
		context.Record("artifact-manifest", current);
		if (context.Baseline != null)
		{
			var comparison = ArtifactVerification.Compare(ArtifactVerification.Inventory(context.Baseline), current);
			context.Record("comparison", new { baseline = context.Baseline, differences = comparison, equivalent = comparison.Length == 0, zipContainerBytesCompared = false, payloadBytesCompared = true });
			if (comparison.Length != 0) throw new InvalidDataException("Artifact comparison failed: " + string.Join(", ", comparison));
		}
		context.Record("completed", new { version = context.Version, completedUtc = DateTimeOffset.UtcNow, packageValidation = true, desktopTests, applicationBundles = 5, comparisonPerformed = context.Baseline != null, publication = false });
		Console.WriteLine("Verified pilot artifacts: " + context.Output);
	}
}

[TaskName("ReleasePack"), IsDependentOn(typeof(Build))]
public sealed class ReleasePack : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context) => Pack.PackLibrary(context);
}

// Produces files only. Public publication still requires the YAML validation gate,
// collision checks, permissions and publication lock; this target sends nothing.
[TaskName("ReleaseArtifacts"), IsDependentOn(typeof(ReleasePack)), IsDependentOn(typeof(Applications))]
public sealed class ReleaseArtifacts : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		Package.Collect(context);
		Verify.Complete(context, desktopTests: false);
	}
}

[TaskName("Default"), IsDependentOn(typeof(Verify))]
public sealed class Default : FrostingTask;
