namespace MosaikCapture.Engine.Recording;

/// <summary>Where ffmpeg and ffprobe are installed.</summary>
internal interface IFfmpegLocator
{
    /// <summary>Full path of ffmpeg.exe, or null if it is not installed.</summary>
    string? Ffmpeg { get; }
    /// <summary>Full path of ffprobe.exe, or null.</summary>
    string? Probe { get; }
}

/// <summary>
/// Finds ffmpeg and ffprobe in the given directories (see <see cref="Ffmpeg.SearchLocations"/>) and
/// remembers them. A remembered file is checked on every use: if it was uninstalled or moved, the search
/// runs again; while nothing is found it runs on every use, so installing ffmpeg needs no restart.
/// Thread-safe (a race only searches twice).
/// </summary>
internal sealed class FfmpegLocator : IFfmpegLocator
{
    /// <summary>"Not installed" is remembered this long, so a long PATH is not searched on every click.</summary>
    internal static readonly TimeSpan MissingFor = TimeSpan.FromSeconds(30);

    readonly Func<IEnumerable<string?>> locations;
    readonly Func<long> now;
    volatile string? ffmpeg, probe;
    long missingSince = long.MinValue;

    public FfmpegLocator(Func<IEnumerable<string?>> locations, Func<long>? clock = null)
    {
        this.locations = locations;
        now = clock ?? Stopwatch.GetTimestamp;
    }

    public string? Ffmpeg
    {
        get
        {
            string? known = ffmpeg;
            if (known != null && File.Exists(known)) return known;
            long since = Interlocked.Read(ref missingSince), at = now();
            if (since != long.MinValue && at - since < MissingFor.TotalSeconds * Stopwatch.Frequency) return null;
            string? found = Recording.Ffmpeg.Find("ffmpeg.exe", locations());
            Interlocked.Exchange(ref missingSince, found == null ? at : long.MinValue);
            return ffmpeg = found;
        }
    }

    public string? Probe
    {
        get
        {
            string? known = probe;
            if (known != null && File.Exists(known)) return known;
            return probe = Recording.Ffmpeg.FindProbe(Ffmpeg, locations());
        }
    }
}
