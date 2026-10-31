// Copyright (c) 2026 Neil Colvin. MIT License; see ../../TeslaPowerwallLibrary/LICENSE.
using System.IO.Compression;
using System.Reflection;
using Cake.Frosting;
using NUnit.Framework;

public sealed class ArtifactTests
{
	private string _directory = null!;
	[SetUp] public void CreateDirectory() => _directory = Path.Combine(Path.GetTempPath(), "powerwall-cake-" + Guid.NewGuid().ToString("N"));
	[TearDown] public void RemoveDirectory() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
	private string Archive(string name, DateTimeOffset timestamp, params (string Name, string Content)[] entries)
	{
		Directory.CreateDirectory(_directory); string path = Path.Combine(_directory, name);
		using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
		foreach (var (entryName, content) in entries)
		{
			var entry = zip.CreateEntry(entryName); entry.LastWriteTime = timestamp;
			using var writer = new StreamWriter(entry.Open()); writer.Write(content);
		}
		return path;
	}
	[Test] public void ChangedRuntimePayloadFailsComparison()
	{
		string before = Archive("before.zip", DateTimeOffset.Now, ("library.dll", "original"));
		string after = Archive("after.zip", DateTimeOffset.Now, ("library.dll", "changed"));
		Assert.That(ArtifactVerification.Compare(ArtifactVerification.Archive(before), ArtifactVerification.Archive(after)), Is.EqualTo(new[] { "library.dll" }));
	}
	[Test] public void MissingNoticeFailsComparison()
	{
		string before = Archive("before.zip", DateTimeOffset.Now, ("library.dll", "same"), ("LICENSE.txt", "notice"));
		string after = Archive("after.zip", DateTimeOffset.Now, ("library.dll", "same"));
		Assert.That(ArtifactVerification.Compare(ArtifactVerification.Archive(before), ArtifactVerification.Archive(after)), Is.EqualTo(new[] { "LICENSE.txt" }));
	}
	[Test] public void DifferentZipTimestampsAndOrderRetainEquivalentPayload()
	{
		string before = Archive("before.zip", DateTimeOffset.Now, ("library.dll", "same"), ("LICENSE.txt", "notice"));
		string after = Archive("after.zip", DateTimeOffset.Now.AddDays(-5), ("LICENSE.txt", "notice"), ("library.dll", "same"));
		Assert.That(ArtifactVerification.Compare(ArtifactVerification.Archive(before), ArtifactVerification.Archive(after)), Is.Empty);
	}
	[Test] public void DuplicateArchiveNamesAreRejected()
	{
		string path = Archive("duplicate.zip", DateTimeOffset.Now, ("library.dll", "first"), ("library.dll", "second"));
		Assert.Throws<ArgumentException>(() => ArtifactVerification.Archive(path));
	}
	[TestCase("2.3.2.0"), TestCase("v2.3.2"), TestCase("2.3"), TestCase("2.03.2"), TestCase("2.3.2 -p:Other=true")]
	public void InvalidReleaseVersionIsRejected(string version) => Assert.Throws<ArgumentException>(() => BuildContext.ValidateVersion(version));
	[Test] public void NuGetRelationshipIdsDoNotMaskChangedTargets()
	{
		const string part = "package/services/metadata/core-properties/nuget.psmdcp";
		string Make(string name, string id, string target) => Archive(name, DateTimeOffset.Now,
			(part, "same"), ("_rels/.rels", $"<Relationships><Relationship Id='{id}' Type='manifest' Target='{target}'/></Relationships>"));
		var before = ArtifactVerification.Archive(Make("before.nupkg", "first", "/library.nuspec"));
		var same = ArtifactVerification.Archive(Make("same.nupkg", "second", "/library.nuspec"));
		var changed = ArtifactVerification.Archive(Make("changed.nupkg", "third", "/wrong.nuspec"));
		Assert.That(ArtifactVerification.Compare(before, same), Is.Empty);
		Assert.That(ArtifactVerification.Compare(before, changed), Is.EqualTo(new[] { "_rels/.rels" }));
	}
	[TestCase("powerwall", "net472", "library-net472")]
	[TestCase("powerwall", "net10.0", "library-net10")]
	[TestCase("console-net10", "net10.0-windows", "console-net10")]
	public void CiSelectorsResolveTheExpectedSuite(string name, string framework, string expected) => Assert.That(BuildContext.ResolveSuite(name, framework).Name, Is.EqualTo(expected));
	[TestCase("powerwall", null), TestCase("powerwall", "net8.0"), TestCase("console-net10", "net472"), TestCase("unrecognised", null)]
	public void AmbiguousOrMismatchedCiSelectorsFail(string name, string? framework) => Assert.Throws<ArgumentException>(() => BuildContext.ResolveSuite(name, framework));
	[Test] public void ArtifactPreparationDoesNotRerunDesktopTests()
	{
		static HashSet<Type> Dependencies(Type task)
		{
			var found = new HashSet<Type>();
			void Visit(Type current)
			{
				foreach (var dependency in current.GetCustomAttributes<IsDependentOnAttribute>()) if (found.Add(dependency.Task)) Visit(dependency.Task);
			}
			Visit(task); return found;
		}
		var artifacts = Dependencies(typeof(ReleaseArtifacts));
		Assert.That(artifacts, Does.Contain(typeof(Build)).And.Contain(typeof(Applications)).And.Contain(typeof(ReleasePack)));
		Assert.That(artifacts, Does.Not.Contain(typeof(Test)).And.Not.Contain(typeof(Suite)).And.Not.Contain(typeof(Pack)));
		Assert.That(Dependencies(typeof(Verify)), Does.Contain(typeof(Test)));
	}
	[Test] public void EmptyTestRunCannotBeReportedAsPassed()
	{
		Directory.CreateDirectory(_directory);
		var suite = BuildContext.Suites[0];
		File.WriteAllText(Path.Combine(_directory, suite.Name + ".trx"), "<TestRun><ResultSummary><Counters passed='0' failed='0' executed='0'/></ResultSummary></TestRun>");
		Assert.Throws<InvalidDataException>(() => Test.ReadResult(_directory, suite));
	}
}
