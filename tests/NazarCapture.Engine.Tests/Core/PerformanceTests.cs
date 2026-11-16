namespace NazarCapture.Engine.Tests.Core;

/// <summary>Phase 7: what keeps memory flat and the UI thread free.</summary>
public sealed class PerformanceTests : IDisposable
{
    readonly Func<string, bool> network = Ffmpeg.IsNetworkPath;

    public void Dispose() => Ffmpeg.IsNetworkPath = network;

    [Fact]
    public void ACaptureKeepsItsOwnSmallCopy() => WpfThread.Run(() =>
    {
        var pixels = new byte[400 * 300 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
        var desktop = BitmapSource.Create(400, 300, 144, 144, PixelFormats.Bgr32, null, pixels, 400 * 4);
        desktop.Freeze();
        var copy = Screenshots.CopyOf(desktop, new Int32Rect(10, 20, 30, 40));
        Assert.True(copy.IsFrozen);
        Assert.IsNotType<CroppedBitmap>(copy);   // a CroppedBitmap would keep the whole desktop alive
        Assert.Equal(30, copy.PixelWidth);
        Assert.Equal(40, copy.PixelHeight);
        Assert.Equal(144, copy.DpiX);
        var expected = new byte[30 * 40 * 4];
        desktop.CopyPixels(new Int32Rect(10, 20, 30, 40), expected, 30 * 4, 0);
        var actual = new byte[30 * 40 * 4];
        copy.CopyPixels(actual, 30 * 4, 0);
        Assert.Equal(expected, actual);
    });

    [Fact]
    public void MissingFfmpegIsRememberedForHalfAMinute()
    {
        long now = 0;
        int searches = 0;
        IEnumerable<string?> Locations() { searches++; return new[] { "C:\\gibt-es-nicht-1234" }; }
        var locator = new FfmpegLocator(Locations, () => now);
        Assert.Null(locator.Ffmpeg);
        Assert.Null(locator.Ffmpeg);
        Assert.Equal(1, searches);   // not searched again on every click
        now += (long)(FfmpegLocator.MissingFor.TotalSeconds + 1) * Stopwatch.Frequency;
        Assert.Null(locator.Ffmpeg);
        Assert.Equal(2, searches);
    }

    [Fact]
    public void NetworkDrivesAreNotSearched()
    {
        var asked = new List<string>();
        Ffmpeg.IsNetworkPath = path => { asked.Add(path); return path.StartsWith("N:", StringComparison.Ordinal); };
        using var work = new TempDirectory();
        File.WriteAllText(work.File("ffmpeg.exe"), "");
        Assert.Null(Ffmpeg.Find("ffmpeg.exe", new[] { "N:\\tools" }));
        Assert.Equal(work.File("ffmpeg.exe"), Ffmpeg.Find("ffmpeg.exe", new[] { "N:\\tools", work.Path }));
        Assert.Contains("N:\\tools", asked);
    }

    [Fact]
    public void ARecordingNeedsTwoGigabytesFree()
    {
        Assert.True(DiskSpace.Enough(null));
        Assert.True(DiskSpace.Enough(DiskSpace.MinimumBytes));
        Assert.False(DiskSpace.Enough(DiskSpace.MinimumBytes - 1));
        Assert.Equal("1,5 GB", DiskSpace.Gigabytes(1610612736));
        Assert.NotNull(DiskSpace.Free(Path.GetTempPath()));
    }

    [Fact]
    public async Task ALateClipboardSuccessIsReported()
    {
        string? late = null;
        void Handler(string what) => late = what;
        ClipboardWriter.LateSuccess += Handler;
        try
        {
            bool copied = await ClipboardWriter.SetAsync("probe", () => Thread.Sleep(ClipboardWriter.Timeout + TimeSpan.FromMilliseconds(300)));
            Assert.False(copied);
            Assert.True(SpinWait.SpinUntil(() => late != null, TimeSpan.FromSeconds(5)));
            Assert.Equal("probe", late);
        }
        finally { ClipboardWriter.LateSuccess -= Handler; }
    }

    [Fact]
    public void TheMemoryReliefCollectsWithoutAnApplication() => MemoryRelief.CollectNow();
}
