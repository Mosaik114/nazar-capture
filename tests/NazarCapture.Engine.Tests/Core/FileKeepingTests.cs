namespace NazarCapture.Engine.Tests.Core;

/// <summary>
/// "Aufbewahren" and "… speichern" off: only files Nazar Capture named itself are ever deleted, only directly in the
/// configured folder, never another program's file, never a subfolder; a locked file or a missing folder is skipped.
/// </summary>
public sealed class FileKeepingTests : IDisposable
{
    static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Local);
    readonly TempDirectory work = new();

    public void Dispose() => work.Dispose();

    string Make(string name, int daysOld, string? folder = null)
    {
        string path = Path.Combine(folder ?? work.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, name);
        File.SetLastWriteTime(path, Now - TimeSpan.FromDays(daysOld) - TimeSpan.FromMinutes(1));
        return path;
    }

    [Theory]
    [InlineData("Nazar-Screenshot 2026-11-17 14-03-12.png", true)]
    [InlineData("Nazar-Screenshot 2026-11-17 14-03-12 (2).png", true)]
    [InlineData("nazar-screenshot 2026-11-17 14-03-12.JPG", true)]
    [InlineData("Nazar-Screenshot 2026-11-17 14-03-12.png.txt", false)]
    [InlineData("Nazar-Screenshot 2026-11-17 14-03-12 Kopie.png", false)]
    [InlineData("Nazar-Screenshot meine Auswahl.png", false)]
    [InlineData("Mosaiks-Screenshot 2026-09-26 14-03-12.png", false)]
    [InlineData("Screenshot 2026-11-17 140312.png", false)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12.mp4", false)]
    public void OwnScreenshotNames(string name, bool own) => Assert.Equal(own, FileKeeping.ScreenshotName.IsMatch(name));

    [Theory]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12.mp4", true)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12 (3).mp4", true)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12 gekürzt.mp4", true)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12 gekürzt (2).mp4", true)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12 gekürzt gekürzt.mp4", true)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12.gif", true)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12 (Rohdaten)", false)]
    [InlineData("Nazar-Aufnahme 2026-11-17 14-03-12.mkv", false)]
    [InlineData("Mosaiks-Aufnahme 2026-09-26 14-03-12.mp4", false)]
    [InlineData("Mosaiks-Aufnahme 2026-09-26 14-03-12 gekürzt.mp4", false)]
    [InlineData("Urlaub.mp4", false)]
    [InlineData("Nazar-Aufnahme Urlaub.mp4", false)]
    public void OwnRecordingNames(string name, bool own) => Assert.Equal(own, FileKeeping.RecordingName.IsMatch(name));

    [Fact]
    public void OnlyOwnOldFilesDirectlyInTheFolderGo()
    {
        string folder = work.File("Eigener Ordner");
        string oldOwn = Make("Nazar-Aufnahme 2026-10-01 10-00-00.mp4", 40, folder);
        string oldGif = Make("Nazar-Aufnahme 2026-10-01 10-00-00.gif", 40, folder);
        string fresh = Make("Nazar-Aufnahme 2026-11-16 10-00-00.mp4", 1, folder);
        string foreign = Make("Mosaiks-Aufnahme 2026-09-26 14-03-12.mp4", 400, folder);
        string mine = Make("Urlaub.mp4", 400, folder);
        string nested = Make("Nazar-Aufnahme 2026-10-01 10-00-00.mp4", 400, Path.Combine(folder, "Archiv"));
        string rawData = Path.Combine(folder, "Nazar-Aufnahme 2026-09-01 10-00-00 (Rohdaten)");
        Directory.CreateDirectory(rawData);
        Directory.SetLastWriteTime(rawData, Now.AddDays(-400));
        Make("segment.mp4", 400, rawData);

        Assert.Equal(0, FileKeeping.Prune(folder, 0, FileKeeping.RecordingName, Now));   // unlimited: nothing
        Assert.True(File.Exists(oldOwn));
        Assert.Equal(2, FileKeeping.Prune(folder, 30, FileKeeping.RecordingName, Now));
        Assert.False(File.Exists(oldOwn));
        Assert.False(File.Exists(oldGif));
        foreach (string kept in new[] { fresh, foreign, mine, nested, Path.Combine(rawData, "segment.mp4") })
            Assert.True(File.Exists(kept), kept);
        Assert.True(Directory.Exists(rawData));
        Assert.Equal(0, FileKeeping.Prune(folder, 30, FileKeeping.ScreenshotName, Now));   // the screenshot rule takes no videos
    }

    [Fact]
    public void ALockedFileOrAMissingFolderIsSkipped()
    {
        string locked = Make("Nazar-Screenshot 2026-01-01 10-00-00.png", 300);
        string other = Make("Nazar-Screenshot 2026-01-02 10-00-00.png", 300);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(1, FileKeeping.Prune(work.Path, 7, FileKeeping.ScreenshotName, Now));
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(other));
        Assert.Equal(0, FileKeeping.Prune(work.File("gibt es nicht"), 7, FileKeeping.ScreenshotName, Now));
        Assert.Equal(0, FileKeeping.Prune("", 7, FileKeeping.ScreenshotName, Now));
        Assert.Equal(0, FileKeeping.Prune("Z:\\\0kaputt", 7, FileKeeping.ScreenshotName, Now));
        string readOnly = Make("Nazar-Screenshot 2026-01-03 10-00-00.png", 300);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try { FileKeeping.Prune(work.Path, 7, FileKeeping.ScreenshotName, Now); }
        finally { File.SetAttributes(readOnly, FileAttributes.Normal); }
        Assert.True(File.Exists(readOnly));   // no access: skipped and logged, never forced
    }

    [Fact]
    public void UnsavedCapturesWaitADayAndAreKeptOnRequest()
    {
        string unsaved = work.File("unsaved");
        string old = Make("Nazar-Aufnahme 2026-11-15 10-00-00.mp4", 2, unsaved);
        string recent = Make("Nazar-Screenshot 2026-11-17 11-00-00.png", 0, unsaved);
        Assert.Equal(1, FileKeeping.PruneUnsaved(unsaved, Now));
        Assert.False(File.Exists(old));
        Assert.True(FileKeeping.IsUnsaved(recent, unsaved));
        Assert.False(FileKeeping.IsUnsaved(work.File("Nazar-Screenshot x.png"), unsaved));
        Assert.False(FileKeeping.IsUnsaved(Path.Combine(unsaved, "sub", "a.png"), unsaved));
        Assert.False(FileKeeping.IsUnsaved("", unsaved));

        string folder = work.File("Bilder");
        Make("Nazar-Screenshot 2026-11-17 11-00-00.png", 0, folder);   // the name is taken there
        string kept = FileKeeping.Keep(recent, folder);
        Assert.Equal(Path.Combine(folder, "Nazar-Screenshot 2026-11-17 11-00-00 (2).png"), kept);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(recent));
    }

    [Fact]
    public void TidyingFollowsTheSettings()
    {
        string shots = work.File("shots"), takes = work.File("takes");
        string shot = Make("Nazar-Screenshot 2026-08-01 10-00-00.png", 100, shots);
        string take = Make("Nazar-Aufnahme 2026-08-01 10-00-00.mp4", 100, takes);
        FileKeeping.TidyNow(new CaptureSettingsSnapshot { ScreenshotFolder = shots, RecordingFolder = takes, ScreenshotKeepDays = 90, RecordingKeepDays = 0 }, Now);
        Assert.False(File.Exists(shot));
        Assert.True(File.Exists(take));
        Assert.Equal(0, CaptureSettings.NormalizeKeepDays(31));
        Assert.Equal(365, CaptureSettings.NormalizeKeepDays(365));
    }

    [Fact]
    public void TheCardsOfferSpeichernForACaptureThatWasNotKept() => WpfThread.Run(() =>
    {
        string unsaved = work.File("unsaved"), pictures = work.File("Bilder"), videos = work.File("Videos");
        FileKeeping.UnsavedFolderOverride = unsaved;
        try
        {
            string shot = Make("Nazar-Screenshot 2026-11-17 11-00-00.png", 0, unsaved);
            var image = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16);
            var capture = new ScreenCapture { Image = image, Path = shot, WorkArea = new RECT { right = 1920, bottom = 1040 } };
            var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true, KeepFolder = () => pictures };
            card.Present(capture, UiText.Common.CopiedToClipboard);
            card.Saved();
            Assert.Equal(UiText.Common.OnlyInClipboard, card.Plate.Subtitle.Text);
            card.KeepInFolder();
            Assert.Equal(Path.Combine(pictures, Path.GetFileName(shot)), capture.Path);
            Assert.True(File.Exists(capture.Path));
            Assert.Equal(UiText.Common.Kept, card.Plate.Title.Text);
            Assert.Equal(Path.GetFileName(shot), card.Plate.Subtitle.Text);
            card.Close();

            string take = Make("Nazar-Aufnahme 2026-11-17 11-00-00.mp4", 0, unsaved);
            var recordingCard = new RecordingCard { Offscreen = true };
            recordingCard.Present(new RecordedVideo { Path = take, Duration = 3, Bytes = 1000 }, UiText.Recording.Copied, new RECT { right = 1920, bottom = 1040 });
            Assert.StartsWith(UiText.Common.OnlyInClipboard, recordingCard.Plate.Subtitle.Text);
            recordingCard.KeepInFolder(videos);
            Assert.Equal(Path.Combine(videos, Path.GetFileName(take)), recordingCard.Shown!.Path);
            Assert.True(File.Exists(recordingCard.Shown.Path));
            Assert.False(File.Exists(take));
            recordingCard.Close();
        }
        finally { FileKeeping.UnsavedFolderOverride = null; }
    });

    [Fact]
    public void WithSavingOffAScreenshotGoesToTheAppsOwnFolder()
    {
        Assert.Equal(work.Path, Screenshots.TargetFolder(work.Path, save: true));
        Assert.Equal(FileKeeping.UnsavedFolder, Screenshots.TargetFolder(work.Path, save: false));
        Assert.StartsWith(CaptureSettings.Home, FileKeeping.UnsavedFolder, StringComparison.OrdinalIgnoreCase);
    }
}
