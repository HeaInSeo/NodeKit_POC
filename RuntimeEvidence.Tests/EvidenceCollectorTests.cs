using System.Security.Cryptography;

namespace RuntimeEvidence.Tests;

public sealed class EvidenceCollectorTests : IDisposable
{
    private readonly string _root = Fixtures.CreateRoot();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static FileEvidence File(EvidenceReport r, string path) => Assert.Single(r.Files, f => f.Path == path);

    private static string[] Values(FileEvidence f, string kind) => f.Items.Where(i => i.Kind == kind).Select(i => i.Value).ToArray();

    [Fact]
    public void DynamicElf_ReportsArchitectureInterpreterAndNeeded()
    {
        var tool = File(EvidenceCollector.Collect(_root), "bin/tool");
        Assert.Equal(["ELF64 little-endian"], Values(tool, "binary-format"));
        Assert.Equal(["x86_64 (EM_X86_64)"], Values(tool, "architecture"));
        Assert.Equal(["DYN"], Values(tool, "elf-type"));
        Assert.Equal([Fixtures.Interpreter], Values(tool, "elf-interpreter"));
        Assert.Equal(Fixtures.Needed, Values(tool, "elf-needed"));
        Assert.Equal([Fixtures.RunPath], Values(tool, "elf-runpath"));
        Assert.Equal(["dynamic (has PT_INTERP)"], Values(tool, "elf-linkage"));
        Assert.Empty(Values(tool, "unknown"));
        Assert.Contains(tool.Items, i => i.Kind == "elf-needed" && i.Source == "elf:DT_NEEDED");
    }

    [Fact]
    public void ShebangScripts_ReportInterpreterAndKeepEnvResolutionUnknown()
    {
        var report = EvidenceCollector.Collect(_root);
        var env = File(report, "bin/run.sh");
        Assert.Equal(["/usr/bin/env"], Values(env, "shebang-interpreter"));
        Assert.Equal(["python3"], Values(env, "shebang-argument"));
        Assert.Single(Values(env, "unknown"), v => v.Contains("env PATH lookup", StringComparison.Ordinal));

        var bash = File(report, "bin/wrapper");
        Assert.Equal(["/bin/bash"], Values(bash, "shebang-interpreter"));
        Assert.Equal(["-eu"], Values(bash, "shebang-argument"));
        Assert.Empty(Values(bash, "unknown"));
    }

    [Fact]
    public void CorruptAndUnrecognised_AreUnknownNotGuessed()
    {
        var report = EvidenceCollector.Collect(_root);
        var broken = File(report, "bin/broken");
        Assert.Single(Values(broken, "unknown"), v => v.StartsWith("ELF not fully analysable", StringComparison.Ordinal));
        Assert.Empty(Values(broken, "elf-needed"));
        Assert.Empty(Values(broken, "elf-interpreter"));

        var mystery = File(report, "bin/mystery");
        Assert.Equal(["mode has an execute bit"], Values(mystery, "executable-candidate"));
        Assert.Single(Values(mystery, "unknown"));

        var link = File(report, "bin/escape");
        Assert.Single(Values(link, "unknown"), v => v.StartsWith("symlink not followed", StringComparison.Ordinal));

        Assert.DoesNotContain(report.Files, f => f.Path == "share/README.txt");
    }

    [Fact]
    public void SameArtifact_GivesIdenticalReport_RegardlessOfLocationAndCreationOrder()
    {
        var first = EvidenceCollector.ToJson(EvidenceCollector.Collect(_root));
        Assert.Equal(first, EvidenceCollector.ToJson(EvidenceCollector.Collect(_root)));

        var other = Fixtures.CreateRoot(reverse: true);
        try
        {
            Assert.Equal(first, EvidenceCollector.ToJson(EvidenceCollector.Collect(other)));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
        Assert.DoesNotContain(_root, first, StringComparison.Ordinal);
    }

    [Fact]
    public void Collection_DoesNotModifyInput()
    {
        var before = Snapshot(_root);
        EvidenceCollector.Collect(_root);
        Assert.Equal(before, Snapshot(_root));
    }

    private static List<string> Snapshot(string root)
    {
        var entries = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
                     .Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                entries.Add($"{path} -> {info.LinkTarget}");
            }
            else if (System.IO.File.Exists(path))
            {
                entries.Add($"{path} {Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)))} {System.IO.File.GetUnixFileMode(path)} {info.LastWriteTimeUtc.Ticks}");
            }
        }
        return entries;
    }
}
