using System.Text;

namespace RuntimeEvidence.Tests;

/// <summary>
/// Builds the fixture Tool roots in a temp directory. The dynamic ELF is synthesised byte by
/// byte (ELF64 LE x86_64 with PT_LOAD, PT_INTERP and PT_DYNAMIC), so no third-party binary is
/// committed and the bytes are identical on every host.
/// </summary>
internal static class Fixtures
{
    public const string Interpreter = "/lib64/ld-linux-x86-64.so.2";
    public static readonly string[] Needed = ["libz.so.1", "libc.so.6"];
    public const string RunPath = "$ORIGIN/../lib";

    private const UnixFileMode Exec = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode Plain = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>Creates the fixture root. <paramref name="reverse"/> creates entries in the opposite order.</summary>
    public static string CreateRoot(bool reverse = false)
    {
        var root = Directory.CreateTempSubdirectory("nk-evidence-").FullName;
        var actions = new List<Action>
        {
            () => Write(root, "bin/tool", DynamicElf(), Exec),
            () => Write(root, "bin/run.sh", Encoding.UTF8.GetBytes("#!/usr/bin/env python3\nprint('hi')\n"), Exec),
            () => Write(root, "bin/wrapper", Encoding.UTF8.GetBytes("#!/bin/bash -eu\nexec \"$@\"\n"), Exec),
            () => Write(root, "bin/multi", Encoding.UTF8.GetBytes("#!/bin/tool first\t second  \n"), Exec),
            () => Write(root, "bin/broken", [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0], Exec),
            () => Write(root, "bin/mystery", Encoding.UTF8.GetBytes("not a known executable format\n"), Exec),
            () => Write(root, "share/README.txt", Encoding.UTF8.GetBytes("documentation\n"), Plain),
            () => File.CreateSymbolicLink(Path.Combine(root, "bin", "escape"), "../../outside-root/secret"),
        };
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "share"));
        if (reverse)
        {
            actions.Reverse();
        }
        foreach (var a in actions)
        {
            a();
        }
        return root;
    }

    private static void Write(string root, string rel, byte[] bytes, UnixFileMode mode)
    {
        var path = Path.Combine(root, rel);
        File.WriteAllBytes(path, bytes);
        File.SetUnixFileMode(path, mode);
    }

    public static byte[] DynamicElf()
    {
        const ulong baseVaddr = 0x400000;
        const int ehsize = 64, phentsize = 56, phnum = 3;
        var interp = Encoding.ASCII.GetBytes(Interpreter + "\0");
        var strtab = new MemoryStream();
        strtab.WriteByte(0);
        var nameOffsets = new List<ulong>();
        foreach (var s in Needed.Append(RunPath))
        {
            nameOffsets.Add((ulong)strtab.Length);
            strtab.Write(Encoding.ASCII.GetBytes(s + "\0"));
        }
        var strBytes = strtab.ToArray();

        var interpOff = ehsize + phentsize * phnum;
        var strOff = interpOff + interp.Length;
        var dynOff = (strOff + strBytes.Length + 7) & ~7;
        var dynamic = new List<(long, ulong)>
        {
            (1, nameOffsets[0]), (1, nameOffsets[1]), (29, nameOffsets[2]), (5, baseVaddr + (ulong)strOff), (0, 0),
        };
        var total = dynOff + dynamic.Count * 16;

        var buf = new byte[total];
        var w = new BinaryWriter(new MemoryStream(buf));
        w.Write([0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        w.Write((ushort)3);  // e_type DYN
        w.Write((ushort)62); // e_machine x86_64
        w.Write(1u);         // e_version
        w.Write(baseVaddr);  // e_entry
        w.Write((ulong)ehsize); // e_phoff
        w.Write(0UL);        // e_shoff
        w.Write(0u);         // e_flags
        w.Write((ushort)ehsize);
        w.Write((ushort)phentsize);
        w.Write((ushort)phnum);
        w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);

        void Phdr(uint type, ulong off, ulong size)
        {
            w.Write(type); w.Write(4u); w.Write(off); w.Write(baseVaddr + off); w.Write(baseVaddr + off);
            w.Write(size); w.Write(size); w.Write(8UL);
        }
        Phdr(1, 0, (ulong)total);                                  // PT_LOAD
        Phdr(3, (ulong)interpOff, (ulong)interp.Length);           // PT_INTERP
        Phdr(2, (ulong)dynOff, (ulong)(dynamic.Count * 16));       // PT_DYNAMIC

        Array.Copy(interp, 0, buf, interpOff, interp.Length);
        Array.Copy(strBytes, 0, buf, strOff, strBytes.Length);
        w.Seek(dynOff, SeekOrigin.Begin);
        foreach (var (tag, val) in dynamic)
        {
            w.Write(tag); w.Write(val);
        }
        w.Flush();
        return buf;
    }
}
