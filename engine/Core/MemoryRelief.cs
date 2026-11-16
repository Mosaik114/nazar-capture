using System.Runtime;

namespace NazarCapture.Engine.Core;

/// <summary>
/// After an overlay, an editor, a card or a pin let go of a large image, the memory is given back once the engine is
/// idle: a full collection with the large object heap compacted once. Frozen desktops are tens of megabytes; without
/// compaction the heap keeps the holes and the private memory only grows over a working day.
/// </summary>
internal static class MemoryRelief
{
    /// <summary>How long to wait after the last release (more releases in between: one collection).</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    static DispatcherTimer? timer;

    /// <summary>Asks for a collection soon (UI thread). Nothing happens while a capture or recording is busy.</summary>
    public static void Soon()
    {
        if (Application.Current == null) return;
        timer ??= CreateTimer();
        timer.Stop();
        timer.Start();
    }

    static DispatcherTimer CreateTimer()
    {
        var created = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = Delay };
        created.Tick += delegate
        {
            created.Stop();
            if (EngineActivity.Busy) return;
            CollectNow();
        };
        return created;
    }

    /// <summary>The collection itself (also used by the measuring run).</summary>
    public static void CollectNow()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

/// <summary>Free space checks before a recording writes raw audio and video segments.</summary>
internal static class DiskSpace
{
    /// <summary>Below this a recording does not start: an hour of raw sound alone takes about 1.4 GB per track.</summary>
    public const long MinimumBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Free bytes on the drive of <paramref name="path"/>; null if unknown (then the recording may start).</summary>
    public static long? Free(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Whether a recording may start with <paramref name="free"/> bytes left.</summary>
    public static bool Enough(long? free) => free is null || free.Value >= MinimumBytes;

    /// <summary>"3,2 GB" in German notation.</summary>
    public static string Gigabytes(long bytes) => (bytes / 1073741824.0).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB";
}
