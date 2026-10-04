using System.Text;
using System.Text.Json;

namespace RuntimeEvidence;

/// <summary>
/// Collects runtime-requirement evidence from a local, unpacked Tool root by reading bytes only.
/// It never executes the artifact, never fetches or installs anything, never follows symlinks,
/// and never writes to the root. Anything it cannot establish is reported as "unknown" rather
/// than guessed.
/// </summary>
public static class EvidenceCollector
{
    public const string SchemaNote =
        "NodeKit_POC runtime evidence v0 - feasibility evidence only, not the canonical Tool Runtime Requirements schema";

    private const string ReadMethod = "static-read";
    private const int ShebangMaxBytes = 256;

    public static EvidenceReport Collect(string root)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"not a directory: {root}");
        }

        var files = new List<FileEvidence>();
        Walk(full, full, files);
        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return new EvidenceReport(SchemaNote, files);
    }

    public static string ToJson(EvidenceReport report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    private static void Walk(string root, string dir, List<FileEvidence> files)
    {
        var entries = Directory.GetFileSystemEntries(dir);
        Array.Sort(entries, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var info = new FileInfo(entry);
            var rel = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
            if (info.LinkTarget is not null)
            {
                files.Add(new FileEvidence(rel, [
                    new Evidence("unknown", $"symlink not followed (target \"{info.LinkTarget}\")", "fs:lstat", ReadMethod),
                ]));
                continue;
            }
            if (Directory.Exists(entry))
            {
                Walk(root, entry, files);
                continue;
            }
            var evidence = AnalyseFile(entry);
            if (evidence.Count > 0)
            {
                files.Add(new FileEvidence(rel, evidence));
            }
        }
    }

    private static List<Evidence> AnalyseFile(string path)
    {
        var items = new List<Evidence>();
        var executable = IsExecutable(path);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (executable)
            {
                items.Add(new Evidence("executable-candidate", "mode has an execute bit", "posix:mode", ReadMethod));
                items.Add(new Evidence("unknown", $"unreadable: {e.GetType().Name}", "fs:read", ReadMethod));
            }
            return items;
        }

        var isElf = bytes.Length >= 4 && bytes[0] == 0x7F && bytes[1] == (byte)'E' && bytes[2] == (byte)'L' && bytes[3] == (byte)'F';
        var isScript = bytes.Length >= 2 && bytes[0] == (byte)'#' && bytes[1] == (byte)'!';
        if (!executable && !isElf && !isScript)
        {
            return items; // not an executable candidate
        }

        items.Add(new Evidence("executable-candidate",
            executable ? "mode has an execute bit" : "no execute bit; content looks executable",
            executable ? "posix:mode" : "content:magic", ReadMethod));
        if (isElf)
        {
            items.AddRange(ElfReader.Read(bytes));
        }
        else if (isScript)
        {
            items.AddRange(ReadShebang(bytes));
        }
        else
        {
            items.Add(new Evidence("unknown", "executable bit set but format not recognised (not ELF, no shebang)", "content:magic", ReadMethod));
        }
        return items;
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    private static IEnumerable<Evidence> ReadShebang(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, ShebangMaxBytes);
        var end = Array.IndexOf(bytes, (byte)'\n', 0, limit);
        if (end < 0)
        {
            yield return new Evidence("unknown", $"shebang line not terminated within {ShebangMaxBytes} bytes", "shebang:line1", ReadMethod);
            yield break;
        }
        var line = Encoding.UTF8.GetString(bytes, 2, end - 2).TrimEnd('\r').Trim();
        yield return new Evidence("binary-format", "script (shebang)", "shebang:line1", ReadMethod);
        if (line.Length == 0)
        {
            yield return new Evidence("unknown", "empty shebang interpreter", "shebang:line1", ReadMethod);
            yield break;
        }
        var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        yield return new Evidence("shebang-interpreter", parts[0], "shebang:line1", ReadMethod);
        foreach (var arg in parts.Skip(1))
        {
            yield return new Evidence("shebang-argument", arg, "shebang:line1", ReadMethod);
        }
        if (parts[0].EndsWith("/env", StringComparison.Ordinal))
        {
            yield return new Evidence("unknown",
                "interpreter resolved through env PATH lookup at run time; the concrete interpreter and its version are not determined",
                "shebang:line1", ReadMethod);
        }
        else if (!parts[0].StartsWith('/'))
        {
            yield return new Evidence("unknown", "relative interpreter path; resolution depends on the working directory", "shebang:line1", ReadMethod);
        }
    }
}
