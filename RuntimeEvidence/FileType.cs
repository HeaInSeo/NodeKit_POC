using System.Runtime.InteropServices;

namespace RuntimeEvidence;

/// <summary>
/// Classifies a directory entry with lstat semantics before anything opens it. Opening a FIFO
/// blocks until a writer appears and reading a device can be endless, so only regular files may
/// be read. Uses statx(2), whose struct layout is identical on every Linux architecture.
/// </summary>
internal static class FileType
{
    private const int AtFdcwd = -100, AtSymlinkNoFollow = 0x100;
    private const uint StatxType = 0x1;
    private const int StatxBufferSize = 256, StxModeOffset = 28;

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int dirfd, string pathname, int flags, uint mask, [Out] byte[] statxbuf);

    /// <summary>Returns null for a regular file, otherwise why the entry must not be opened.</summary>
    public static string? NonRegular(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return null; // FIFOs, sockets and devices do not appear in the Windows directory tree walked here
        }
        if (!OperatingSystem.IsLinux())
        {
            return "file type not determinable on this OS";
        }
        var buf = new byte[StatxBufferSize];
        if (statx(AtFdcwd, path, AtSymlinkNoFollow, StatxType, buf) != 0)
        {
            return $"lstat failed (errno {Marshal.GetLastPInvokeError()})";
        }
        return (BitConverter.ToUInt16(buf, StxModeOffset) & 0xF000) switch
        {
            0x8000 => null,
            0x1000 => "FIFO",
            0x2000 => "character device",
            0x6000 => "block device",
            0xC000 => "socket",
            0x4000 => "directory",
            0xA000 => "symlink",
            var t => $"file type 0x{t:x}",
        };
    }
}
