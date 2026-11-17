using Microsoft.Win32.SafeHandles;

namespace NazarCapture.Engine.Core;

/// <summary>
/// Windows' RedirectionGuard and what Nazar Capture does about it.
/// <para>
/// A process with this mitigation cannot follow a junction or symbolic link that a user without admin rights created:
/// Windows answers with error 448 ("untrusted mount point"). Nazar Capture never turns it on itself, but a process
/// inherits it from the program that started it; Inno Setup 6.7 and later started the app that way after an install
/// or an update (packaging\nazar.iss now switches it off). The host restarts such a copy through the Windows shell
/// without the guard (src\unguard.rs); only where that is impossible (no shell) does a guarded copy run on.
/// </para>
/// Three things keep such a copy working: links are resolved before a program is started through them
/// (<see cref="Resolve"/>, ffmpeg from winget or Scoop), captures fall back to an own folder when theirs cannot be
/// written (<see cref="FileKeeping.FallbackFolder"/>), and the state is written to the log once at start.
/// </summary>
internal static class LinkSafety
{
    /// <summary>ERROR_UNTRUSTED_MOUNT_POINT as an HRESULT (0x800701C0).</summary>
    internal const int UntrustedMountPointHResult = unchecked((int)0x800701C0);

    const int ProcessRedirectionTrustPolicy = 16;
    const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003, IO_REPARSE_TAG_SYMLINK = 0xA000000C;
    const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000, FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    const uint SYMLINK_FLAG_RELATIVE = 1;
    const int MaxLinks = 32;
    static readonly char[] Separators = { '\\', '/' };

    /// <summary>Whether this process runs with RedirectionGuard enforced (null: Windows cannot tell, e.g. before 22H2).</summary>
    public static bool? GuardEnforced
    {
        get
        {
            try
            {
                return GetProcessMitigationPolicy(GetCurrentProcess(), ProcessRedirectionTrustPolicy, out uint flags, (UIntPtr)4)
                    ? (flags & 1) != 0
                    : null;
            }
            catch (EntryPointNotFoundException) { return null; }
        }
    }

    /// <summary>Writes the state to the log if the guard is on, so a report of error 448 can be read at once.</summary>
    public static void LogState()
    {
        if (GuardEnforced == true)
            ErrorLog.Write("RedirectionGuard is enforced for this process (inherited from the program that started it); "
                + "links made without admin rights cannot be followed until Nazar Capture is started again", null);
    }

    /// <summary>Whether <paramref name="error"/> is Windows refusing a link (error 448).</summary>
    public static bool IsUntrustedMountPoint(Exception? error)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            if (e.HResult == UntrustedMountPointHResult) return true;
            if (e is System.ComponentModel.Win32Exception { NativeErrorCode: 448 }) return true;
        }
        return false;
    }

    /// <summary>
    /// <paramref name="path"/> without junctions and symbolic links: each one on the way is replaced by its target,
    /// read from the link itself, so nothing has to be traversed. A Scoop shim (an .exe next to a .shim file that names
    /// the real program) is replaced by that program. Returns <paramref name="path"/> unchanged when nothing needs
    /// resolving, and null when a link cannot be read or points somewhere that does not exist.
    /// </summary>
    public static string? Resolve(string path)
    {
        string? resolved = ResolveLinks(path);
        if (resolved == null) return null;
        string? shim = ScoopShimTarget(resolved);
        return shim == null ? resolved : ResolveLinks(shim);
    }

    /// <summary>
    /// A folder's real path like <see cref="Resolve"/>, also for a folder that does not exist yet: the nearest existing
    /// parent is resolved and the rest appended. Null if nothing on the way can be resolved.
    /// </summary>
    public static string? ResolveFolder(string folder)
    {
        string? existing;
        try { existing = Path.GetFullPath(folder).TrimEnd('\\'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        var missing = new Stack<string>();
        while (existing != null)
        {
            string? real = ResolveLinks(existing);
            if (real != null) return missing.Aggregate(real, Path.Combine);
            string? parent = Path.GetDirectoryName(existing);
            if (parent == null) return null;
            missing.Push(Path.GetFileName(existing));
            existing = parent;
        }
        return null;
    }

    static string? ResolveLinks(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return null;
        string current = root!;
        var rest = new Queue<string>(full.Substring(root!.Length).Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        int links = 0;
        while (rest.Count > 0)
        {
            string next = Path.Combine(current, rest.Dequeue());
            FileAttributes attributes;
            try { attributes = File.GetAttributes(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                current = next;
                continue;
            }
            string? target = LinkTarget(next);
            if (target == null)
            {
                current = next;   // another kind of reparse point (OneDrive, deduplication ...): Windows reads it as a file
                continue;
            }
            if (++links > MaxLinks) return null;
            string combined = Path.IsPathRooted(target) ? target : Path.Combine(current, target);
            // the target replaces this part; what is left of the path follows it
            var remaining = rest.ToList();
            string targetFull;
            try { targetFull = Path.GetFullPath(combined); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
            string? targetRoot = Path.GetPathRoot(targetFull);
            if (string.IsNullOrEmpty(targetRoot)) return null;
            current = targetRoot!;
            rest = new Queue<string>(targetFull.Substring(targetRoot!.Length).Split(Separators, StringSplitOptions.RemoveEmptyEntries).Concat(remaining));
        }
        return File.Exists(current) || Directory.Exists(current) ? current : null;
    }

    /// <summary>The target of a junction or symbolic link, read without following it; null for any other reparse point.</summary>
    internal static string? LinkTarget(string link)
    {
        using SafeFileHandle handle = CreateFileW(link, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new byte[16 * 1024];
        if (!DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, buffer, buffer.Length, out int returned, IntPtr.Zero) || returned < 8)
            return null;
        uint tag = BitConverter.ToUInt32(buffer, 0);
        // REPARSE_DATA_BUFFER: tag, data length, reserved, then substitute/print name offsets and lengths
        int substituteOffset = BitConverter.ToUInt16(buffer, 8), substituteLength = BitConverter.ToUInt16(buffer, 10);
        int printOffset = BitConverter.ToUInt16(buffer, 12), printLength = BitConverter.ToUInt16(buffer, 14);
        int pathBuffer;
        bool relative = false;
        if (tag == IO_REPARSE_TAG_MOUNT_POINT) pathBuffer = 16;
        else if (tag == IO_REPARSE_TAG_SYMLINK)
        {
            relative = (BitConverter.ToUInt32(buffer, 16) & SYMLINK_FLAG_RELATIVE) != 0;
            pathBuffer = 20;
        }
        else return null;
        string Read(int offset, int length) =>
            pathBuffer + offset + length <= returned ? Encoding.Unicode.GetString(buffer, pathBuffer + offset, length) : "";
        string name = Read(printOffset, printLength);
        if (name.Length == 0) name = Read(substituteOffset, substituteLength);
        if (name.StartsWith(@"\??\", StringComparison.Ordinal)) name = name.Substring(4);
        if (name.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) name = @"\\" + name.Substring(4);
        if (name.Length == 0) return null;
        return relative ? name : (Path.IsPathRooted(name) ? name : null);
    }

    /// <summary>A Scoop shim "...\scoop\shims\ffmpeg.exe" names its program in "ffmpeg.shim" (path = "...").</summary>
    internal static string? ScoopShimTarget(string exe)
    {
        try
        {
            string shim = Path.ChangeExtension(exe, ".shim");
            if (!File.Exists(shim)) return null;
            foreach (string line in File.ReadLines(shim))
            {
                int equals = line.IndexOf('=');
                if (equals < 0 || !string.Equals(line.Substring(0, equals).Trim(), "path", StringComparison.OrdinalIgnoreCase)) continue;
                string target = line.Substring(equals + 1).Trim().Trim('"');
                return Path.IsPathRooted(target) ? target : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint flags, UIntPtr length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
