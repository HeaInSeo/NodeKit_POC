using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RuntimeEvidence.Tests;

/// <summary>
/// Regressions for inputs that must become "unknown" instead of hanging the collector or
/// producing evidence from unrelated bytes.
/// </summary>
public sealed class HardeningTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("nk-hardening-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [DllImport("libc", SetLastError = true)]
    private static extern int mkfifo(string pathname, uint mode);

    // Offsets inside Fixtures.DynamicElf: e_phoff, then the PT_LOAD and PT_DYNAMIC program headers.
    private const int Phoff = 32, LoadPhdr = 64, DynPhdr = 64 + 2 * 56, POffset = 8, PFilesz = 32;
    private const ulong BaseVaddr = 0x400000;

    private static string[] Values(FileEvidence f, string kind) => f.Items.Where(i => i.Kind == kind).Select(i => i.Value).ToArray();

    private async Task<FileEvidence> CollectSingle(string rel)
    {
        // A blocking open would hang forever; fail the test instead.
        var report = await Task.Run(() => EvidenceCollector.Collect(_root)).WaitAsync(TimeSpan.FromSeconds(10));
        return Assert.Single(report.Files, f => f.Path == rel);
    }

    [Fact]
    public async Task Fifo_IsUnknownAndNeverOpened()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Assert.Equal(0, mkfifo(Path.Combine(_root, "pipe"), Convert.ToUInt32("755", 8)));

        var fifo = await CollectSingle("pipe");
        Assert.Single(Values(fifo, "unknown"), v => v.Contains("not opened", StringComparison.Ordinal));
        Assert.Empty(Values(fifo, "executable-candidate"));
    }

    [Fact]
    public async Task Socket_IsUnknownAndNeverOpened()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(Path.Combine(_root, "sock")));

        var sock = await CollectSingle("sock");
        Assert.Single(Values(sock, "unknown"), v => v.Contains("not opened", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WrappingStringOffset_DoesNotFabricateNeeded()
    {
        var elf = Fixtures.DynamicElf();
        var dyn = (int)BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(DynPhdr + POffset));
        var strtabOff = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(dyn + 3 * 16 + 8)) - BaseVaddr;
        var interpOff = (ulong)(LoadPhdr + 3 * 56);
        // strtab + val wraps past 2^64 onto the PT_INTERP string.
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(dyn + 8), unchecked(0UL - strtabOff + interpOff));
        Write("tool", elf);

        var tool = await CollectSingle("tool");
        Assert.Equal([Fixtures.Needed[1]], Values(tool, "elf-needed"));
        Assert.Single(Values(tool, "unknown"), v => v.StartsWith("elf:DT_NEEDED", StringComparison.Ordinal));
        Assert.Equal([Fixtures.Interpreter], Values(tool, "elf-interpreter"));
    }

    [Fact]
    public async Task WrappingSegmentOffset_IsUnknown()
    {
        var elf = Fixtures.DynamicElf();
        // p_offset + (vaddr - p_vaddr) wraps to a small, in-file but wrong offset.
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(LoadPhdr + POffset), unchecked(0UL - 16));
        Write("tool", elf);

        var tool = await CollectSingle("tool");
        Assert.Empty(Values(tool, "elf-needed"));
        Assert.Empty(Values(tool, "elf-runpath"));
        Assert.Single(Values(tool, "unknown"), v => v.StartsWith("DT_STRTAB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SegmentFileBytesBeyondFile_IsUnknown()
    {
        var elf = Fixtures.DynamicElf();
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(LoadPhdr + PFilesz), (ulong)elf.Length + 4096);
        Write("tool", elf);

        var tool = await CollectSingle("tool");
        Assert.Empty(Values(tool, "elf-needed"));
        Assert.Single(Values(tool, "unknown"), v => v.StartsWith("DT_STRTAB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WrappingProgramHeaderOffset_IsUnknown()
    {
        var elf = Fixtures.DynamicElf();
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(Phoff), ulong.MaxValue - 8);
        Write("tool", elf);

        var tool = await CollectSingle("tool");
        Assert.Single(Values(tool, "unknown"), v => v.StartsWith("ELF not fully analysable", StringComparison.Ordinal));
        Assert.Empty(Values(tool, "elf-interpreter"));
        Assert.Empty(Values(tool, "elf-needed"));
    }

    private void Write(string rel, byte[] bytes)
    {
        var path = Path.Combine(_root, rel);
        File.WriteAllBytes(path, bytes);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
