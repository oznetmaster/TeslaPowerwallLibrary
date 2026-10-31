// Copyright (c) 2026 Neil Colvin. MIT License; see ../TeslaPowerwallLibrary/LICENSE.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

public static class ArtifactVerification
{
	private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
	public static SortedDictionary<string, string> Inventory(string root)
	{
		var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
		foreach (string folder in new[] { "nuget", "release" })
		{
			string directory = Path.Combine(root, folder);
			if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
			var files = Directory.GetFiles(directory);
			if (files.Length == 0) throw new InvalidDataException("Empty artifact directory: " + directory);
			foreach (string file in files.Order(StringComparer.Ordinal))
			{
				string key = folder + "/" + Path.GetFileName(file);
				if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
					foreach (var entry in Archive(file)) result.Add(key + "!/" + entry.Key, entry.Value);
				else result.Add(key, Hash(File.ReadAllBytes(file)));
			}
		}
		return result;
	}
	public static SortedDictionary<string, string> Archive(string path)
	{
		using var archive = ZipFile.OpenRead(path);
		var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
		var metadata = archive.Entries.Where(e => e.FullName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) && e.FullName.EndsWith(".psmdcp", StringComparison.Ordinal)).ToArray();
		bool nuget = path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
		if (nuget && metadata.Length != 1) throw new InvalidDataException("Expected one NuGet core-properties part.");
		foreach (var entry in archive.Entries.Where(e => e.Name != ""))
		{
			string name = entry.FullName.Replace('\\', '/');
			using var input = entry.Open(); using var buffer = new MemoryStream(); input.CopyTo(buffer);
			byte[] bytes = buffer.ToArray();
			// NuGet generates OPC relationship IDs and sometimes the metadata filename.
			// Retain every relationship's type/target and every core-properties byte.
			if (nuget && name == "_rels/.rels")
			{
				var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes));
				foreach (var relation in xml.Root!.Elements())
				{
					relation.Attribute("Id")?.Remove();
					var target = relation.Attribute("Target");
					if (target?.Value.TrimStart('/') == metadata[0].FullName) target.Value = "/package/services/metadata/core-properties/nuget.psmdcp";
				}
				bytes = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
			}
			if (nuget && name == metadata[0].FullName) name = "package/services/metadata/core-properties/nuget.psmdcp";
			result.Add(name, Hash(bytes));
		}
		return result;
	}
	public static string[] Compare(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> candidate) =>
		baseline.Keys.Union(candidate.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
			.Where(key => !baseline.TryGetValue(key, out string? before) || !candidate.TryGetValue(key, out string? after) || before != after).ToArray();
	public static void VerifyPackage(string path, string version, string root)
	{
		using var archive = ZipFile.OpenRead(path);
		var nuspecEntry = archive.GetEntry("TeslaPowerwallLibrary.nuspec") ?? throw new InvalidDataException("Missing nuspec.");
		using var input = nuspecEntry.Open(); var nuspec = XDocument.Load(input);
		var metadata = nuspec.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
		if (metadata.Elements().Single(e => e.Name.LocalName == "id").Value != "TeslaPowerwallLibrary" || metadata.Elements().Single(e => e.Name.LocalName == "version").Value != version)
			throw new InvalidDataException("Unexpected package identity.");
		if (metadata.Descendants().Where(e => e.Name.LocalName == "dependency").Any(e => ((string?)e.Attribute("id"))?.StartsWith("Cake.", StringComparison.OrdinalIgnoreCase) == true))
			throw new InvalidDataException("Build dependency leaked into the runtime package.");
		void Exact(string entryName, string source)
		{
			using var stream = (archive.GetEntry(entryName) ?? throw new InvalidDataException("Missing " + entryName)).Open();
			using var buffer = new MemoryStream(); stream.CopyTo(buffer);
			if (!buffer.ToArray().SequenceEqual(File.ReadAllBytes(source))) throw new InvalidDataException("Package bytes differ: " + entryName);
		}
		foreach (string framework in new[] { "net472", "net10.0" })
			foreach (string extension in new[] { "dll", "xml" })
				Exact($"lib/{framework}/TeslaPowerwallLibrary.{extension}", Path.Combine(root, "TeslaPowerwallLibrary/bin/Release", framework, "TeslaPowerwallLibrary." + extension));
		foreach (string document in BuildContext.Documents.Concat(["MIGRATION-SystemTextJson.md"])) Exact(document, Path.Combine(root, document));
		Exact("licenses/pypowerwall.txt", Path.Combine(root, "TeslaPowerwallLibrary/Tedapi/Protocol/UPSTREAM-LICENSE.txt"));
		// SDK package/API validation remains enabled in the unchanged library project.
	}
}
