using System.Buffers.Binary;
using System.Text;

namespace RuntimeEvidence;

/// <summary>
/// Reads ELF header, program headers and the dynamic section from bytes. Every offset is
/// bounds-checked; a truncated or inconsistent file yields "unknown" evidence instead of an
/// exception or a guess.
/// </summary>
internal static class ElfReader
{
    private const string Method = "static-read";

    private const uint PtLoad = 1, PtDynamic = 2, PtInterp = 3;
    private const long DtNull = 0, DtNeeded = 1, DtStrtab = 5, DtStrsz = 10, DtRpath = 15, DtRunpath = 29;

    private sealed class FormatException(string message) : Exception(message);

    private readonly record struct Segment(uint Type, ulong Offset, ulong Vaddr, ulong FileSize);

    public static List<Evidence> Read(byte[] b)
    {
        var items = new List<Evidence>();
        try
        {
            ReadInto(b, items);
        }
        catch (FormatException e)
        {
            items.Add(new Evidence("unknown", $"ELF not fully analysable: {e.Message}", "elf", Method));
        }
        return items;
    }

    private static void ReadInto(byte[] b, List<Evidence> items)
    {
        if (b.Length < 16)
        {
            throw new FormatException("truncated e_ident");
        }
        var is64 = b[4] switch
        {
            1 => false,
            2 => true,
            _ => throw new FormatException($"unknown EI_CLASS {b[4]}"),
        };
        var le = b[5] switch
        {
            1 => true,
            2 => false,
            _ => throw new FormatException($"unknown EI_DATA {b[5]}"),
        };
        items.Add(new Evidence("binary-format", $"ELF{(is64 ? 64 : 32)} {(le ? "little" : "big")}-endian", "elf:e_ident", Method));

        var r = new Reader(b, le);
        Need(b, is64 ? 64 : 52, "ELF header");
        var type = r.U16(16);
        var machine = r.U16(18);
        items.Add(new Evidence("elf-type", type switch
        {
            1 => "REL",
            2 => "EXEC",
            3 => "DYN",
            4 => "CORE",
            _ => $"0x{type:x}",
        }, "elf:e_type", Method));
        items.Add(new Evidence("architecture", MachineName(machine), "elf:e_machine", Method));

        var phoff = is64 ? r.U64(32) : r.U32(28);
        var phentsize = r.U16(is64 ? 54 : 42);
        var phnum = r.U16(is64 ? 56 : 44);
        if (phnum == 0)
        {
            items.Add(new Evidence("unknown", "no program headers; linkage and loader cannot be determined", "elf:e_phnum", Method));
            return;
        }
        if (phentsize < (is64 ? 56 : 32))
        {
            throw new FormatException($"program header entry size {phentsize} too small");
        }

        var segments = new List<Segment>();
        for (var i = 0; i < phnum; i++)
        {
            var off = Checked(Add(phoff, (ulong)i * phentsize, "program header"), (ulong)(is64 ? 56 : 32), b, "program header");
            segments.Add(is64
                ? new Segment(r.U32(off), r.U64(off + 8), r.U64(off + 16), r.U64(off + 32))
                : new Segment(r.U32(off), r.U32(off + 4), r.U32(off + 8), r.U32(off + 16)));
        }

        var interp = segments.FirstOrDefault(s => s.Type == PtInterp);
        var hasInterp = interp.Type == PtInterp;
        if (hasInterp)
        {
            var off = Checked(interp.Offset, interp.FileSize, b, "PT_INTERP");
            items.Add(new Evidence("elf-interpreter", CString(b, off, (int)interp.FileSize), "elf:PT_INTERP", Method));
        }

        var dyn = segments.FirstOrDefault(s => s.Type == PtDynamic);
        var hasDynamic = dyn.Type == PtDynamic;
        items.Add(new Evidence("elf-linkage", (hasInterp, hasDynamic) switch
        {
            (true, _) => "dynamic (has PT_INTERP)",
            (false, true) => "PT_DYNAMIC without PT_INTERP (static-pie or shared object)",
            _ => "static (no PT_INTERP, no PT_DYNAMIC)",
        }, "elf:program-headers", Method));
        if (!hasDynamic)
        {
            return;
        }

        var entSize = is64 ? 16 : 8;
        var dynOff = Checked(dyn.Offset, dyn.FileSize, b, "PT_DYNAMIC");
        var entries = new List<(long Tag, ulong Val)>();
        for (var p = dynOff; p + entSize <= dynOff + (int)dyn.FileSize; p += entSize)
        {
            var tag = is64 ? (long)r.U64(p) : r.I32(p);
            var val = is64 ? r.U64(p + 8) : r.U32(p + 4);
            if (tag == DtNull)
            {
                break;
            }
            entries.Add((tag, val));
        }

        var strtab = entries.Where(e => e.Tag == DtStrtab).Select(e => (ulong?)e.Val).FirstOrDefault();
        var strings = entries.Where(e => e.Tag is DtNeeded or DtRunpath or DtRpath).ToList();
        if (strings.Count == 0)
        {
            return;
        }
        if (strtab is null)
        {
            items.Add(new Evidence("unknown", "DT_NEEDED/RPATH present but no DT_STRTAB", "elf:PT_DYNAMIC", Method));
            return;
        }
        if (FileSpan(segments, strtab.Value, b.Length) is null)
        {
            items.Add(new Evidence("unknown", $"DT_STRTAB 0x{strtab.Value:x} is not inside a file-backed PT_LOAD segment", "elf:DT_STRTAB", Method));
            return;
        }
        var strsz = entries.Where(e => e.Tag == DtStrsz).Select(e => (ulong?)e.Val).FirstOrDefault();
        foreach (var (tag, val) in strings)
        {
            var (kind, source) = tag switch
            {
                DtNeeded => ("elf-needed", "elf:DT_NEEDED"),
                DtRunpath => ("elf-runpath", "elf:DT_RUNPATH"),
                _ => ("elf-rpath", "elf:DT_RPATH"),
            };
            if (val >= strsz)
            {
                items.Add(new Evidence("unknown", $"{source} string offset {val} beyond DT_STRSZ {strsz}", source, Method));
                continue;
            }
            // Map strtab+val as one virtual address: adding val to a file offset could wrap or
            // leave the segment and land on unrelated bytes.
            var span = val > ulong.MaxValue - strtab.Value ? null : FileSpan(segments, strtab.Value + val, b.Length);
            if (span is null)
            {
                items.Add(new Evidence("unknown", $"{source} string offset {val} is not inside a file-backed PT_LOAD segment", source, Method));
                continue;
            }
            var (at, end) = span.Value;
            var max = strsz is null ? end - at : (int)Math.Min((ulong)(end - at), strsz.Value - val);
            try
            {
                items.Add(new Evidence(kind, CString(b, at, max), source, Method));
            }
            catch (FormatException e)
            {
                items.Add(new Evidence("unknown", $"{source} {e.Message}", source, Method));
            }
        }
    }

    /// <summary>
    /// Maps a virtual address through the PT_LOAD segment containing it in its file-backed part.
    /// Returns the file offset and the end of that segment's file bytes, or null when no such
    /// segment exists or the segment's file bytes are not entirely inside the file.
    /// </summary>
    private static (int At, int End)? FileSpan(List<Segment> segments, ulong vaddr, int fileLength)
    {
        foreach (var s in segments.Where(s => s.Type == PtLoad))
        {
            if (vaddr < s.Vaddr || vaddr - s.Vaddr >= s.FileSize)
            {
                continue;
            }
            if (s.Offset > (ulong)fileLength || s.FileSize > (ulong)fileLength - s.Offset)
            {
                continue;
            }
            return ((int)(s.Offset + (vaddr - s.Vaddr)), (int)(s.Offset + s.FileSize));
        }
        return null;
    }

    private static ulong Add(ulong a, ulong b, string what) =>
        a > ulong.MaxValue - b ? throw new FormatException($"{what} offset overflows") : a + b;

    private static string MachineName(ushort m) => m switch
    {
        3 => "x86 (EM_386)",
        40 => "arm (EM_ARM)",
        62 => "x86_64 (EM_X86_64)",
        183 => "aarch64 (EM_AARCH64)",
        243 => "riscv (EM_RISCV)",
        21 => "ppc64 (EM_PPC64)",
        22 => "s390 (EM_S390)",
        _ => $"unknown e_machine {m}",
    };

    private static void Need(byte[] b, int len, string what)
    {
        if (b.Length < len)
        {
            throw new FormatException($"truncated {what}");
        }
    }

    private static int Checked(ulong off, ulong len, byte[] b, string what)
    {
        if (off > (ulong)b.Length || len > (ulong)b.Length - off)
        {
            throw new FormatException($"{what} outside file (offset {off}, length {len}, file {b.Length})");
        }
        return (int)off;
    }

    private static string CString(byte[] b, int off, int max)
    {
        var end = Array.IndexOf(b, (byte)0, off, Math.Min(max, b.Length - off));
        if (end < 0)
        {
            throw new FormatException($"unterminated string at offset {off}");
        }
        return Encoding.UTF8.GetString(b, off, end - off);
    }

    private readonly struct Reader(byte[] b, bool le)
    {
        public ushort U16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));
        public uint U32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
        public int I32(int o) => le ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(o));
        public ulong U64(int o) => le ? BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(o));
    }
}
