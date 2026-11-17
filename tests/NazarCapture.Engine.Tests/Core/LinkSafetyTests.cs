namespace NazarCapture.Engine.Tests.Core;

/// <summary>
/// A copy of Nazar Capture that inherited Windows' RedirectionGuard cannot follow links a user made (error 448):
/// links are read instead of followed, and captures fall back to the app's own folder.
/// </summary>
public sealed class LinkSafetyTests : IDisposable
{
    readonly TempDirectory work = new();

    public void Dispose()
    {
        // junctions first, so deleting the folder never walks into their targets twice
        foreach (string link in links)
        {
            try { Directory.Delete(link); }
            catch (IOException) { }
        }
        work.Dispose();
    }

    readonly List<string> links = new();

    /// <summary>A junction as "mklink /J" makes it (no admin rights needed, which is exactly what the guard distrusts).</summary>
    void Junction(string link, string target)
    {
        links.Insert(0, link);   // removed in reverse order
        var start = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J failed");
    }

    static string Program(string folder, string name = "ffmpeg.exe")
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void APathWithoutLinksStaysAsItIs()
    {
        string exe = Program(work.File("bin"));
        Assert.Equal(exe, LinkSafety.Resolve(exe));
    }

    [Fact]
    public void AJunctionOnTheWayIsReplacedByItsTarget()
    {
        string real = Program(work.File(@"packages\ffmpeg-8\bin"));
        Junction(work.File("current"), work.File(@"packages\ffmpeg-8"));

        Assert.Equal(real, LinkSafety.Resolve(work.File(@"current\bin\ffmpeg.exe")));
        Assert.Equal(work.File(@"packages\ffmpeg-8"), LinkSafety.LinkTarget(work.File("current")));
    }

    [Fact]
    public void JunctionsInsideJunctionsAreResolvedToTheEnd()
    {
        string real = Program(work.File(@"a\b\bin"));
        Junction(work.File("first"), work.File("a"));
        Junction(work.File(@"a\second"), work.File(@"a\b"));

        Assert.Equal(real, LinkSafety.Resolve(work.File(@"first\second\bin\ffmpeg.exe")));
    }

    [Fact]
    public void AScoopShimLeadsToTheProgramBehindItsJunction()
    {
        string real = Program(work.File(@"scoop\apps\ffmpeg\8.0\bin"));
        Junction(work.File(@"scoop\apps\ffmpeg\current"), work.File(@"scoop\apps\ffmpeg\8.0"));
        string shim = Program(work.File(@"scoop\shims"));
        File.WriteAllText(Path.ChangeExtension(shim, ".shim"), "path = \"" + work.File(@"scoop\apps\ffmpeg\current\bin\ffmpeg.exe") + "\"\r\n");

        Assert.Equal(real, LinkSafety.Resolve(shim));
        Assert.Equal(real, Ffmpeg.Find("ffmpeg.exe", new[] { work.File(@"scoop\shims") }));
    }

    [Fact]
    public void FfmpegFoundThroughALinkedFolderIsStartedFromItsRealFolder()
    {
        string real = Program(work.File(@"WinGet\Packages\Gyan.FFmpeg\bin"));
        Junction(work.File("Links"), work.File(@"WinGet\Packages\Gyan.FFmpeg\bin"));

        Assert.Equal(real, Ffmpeg.Find("ffmpeg.exe", new[] { work.File("Links") }));
    }

    [Fact]
    public void AFolderThatDoesNotExistYetIsResolvedThroughItsParent()
    {
        Directory.CreateDirectory(work.File(@"D\Bilder"));
        Junction(work.File("Pictures"), work.File(@"D\Bilder"));

        Assert.Equal(work.File(@"D\Bilder\Nazar-Screenshots"), LinkSafety.ResolveFolder(work.File(@"Pictures\Nazar-Screenshots")));
        Assert.Equal(work.File(@"D\Bilder"), LinkSafety.ResolveFolder(work.File("Pictures")));
    }

    [Fact]
    public void ALinkThatLeadsNowhereIsNotResolved()
    {
        Directory.CreateDirectory(work.File("gone"));
        Junction(work.File("dangling"), work.File("gone"));
        Directory.Delete(work.File("gone"));

        Assert.Null(LinkSafety.Resolve(work.File(@"dangling\ffmpeg.exe")));
        Assert.Null(LinkSafety.Resolve(work.File("missing.exe")));
    }

    [Fact]
    public void Error448IsRecognized()
    {
        Assert.True(LinkSafety.IsUntrustedMountPoint(new IOException("untrusted mount point", LinkSafety.UntrustedMountPointHResult)));
        Assert.True(LinkSafety.IsUntrustedMountPoint(new InvalidOperationException("wrapped", new System.ComponentModel.Win32Exception(448))));
        Assert.False(LinkSafety.IsUntrustedMountPoint(new IOException("disk full", unchecked((int)0x80070070))));
        Assert.False(LinkSafety.IsUntrustedMountPoint(null));
    }

    [Fact]
    public void TheGuardStateCanBeRead()
    {
        // The test run is never started by an installer; on Windows 10 22H2 and later the answer is known.
        Assert.NotEqual(true, LinkSafety.GuardEnforced);
    }

    [Fact]
    public void AnUnwritableFolderFallsBackToTheAppsOwnFolder()
    {
        File.WriteAllText(work.File("file"), "");
        string unwritable = work.File(@"file\Bilder");   // below a file: can never be created

        Assert.Equal(work.File("Ersatzordner"), FileKeeping.WritableOrFallback(unwritable, work.File("Ersatzordner")));
        Assert.True(Directory.Exists(work.File("Ersatzordner")));
        string writable = work.File("Bilder");
        Assert.Equal(writable, FileKeeping.WritableOrFallback(writable, work.File("Ersatzordner")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(writable));   // the check leaves nothing behind
    }

    [Fact]
    public void KeepingFallsBackInsteadOfLeavingTheCaptureToTheTidying()
    {
        string capture = Program(work.File("unsaved"), "Nazar-Screenshot 2026-11-17 14-03-12.png");
        File.WriteAllText(work.File("file"), "");

        string kept = FileKeeping.KeepOrFallback(capture, work.File(@"file\Bilder"), work.File("Ersatzordner"));

        Assert.Equal(work.File(@"Ersatzordner\Nazar-Screenshot 2026-11-17 14-03-12.png"), kept);
        Assert.True(FileKeeping.IsUnsaved(kept, work.File("Ersatzordner")));   // the same folder test as IsInFallback
        Assert.False(File.Exists(capture));
    }

    [Fact]
    public void TheFallbackFolderIsNamedInTheInterfaceLanguageAndNeverSplit()
    {
        using (new LanguageScope("en")) Assert.Equal(work.File("Backup folder"), FileKeeping.FallbackFolderIn(work.Path));
        using (new LanguageScope("ar")) Assert.Equal(work.File("مجلد بديل"), FileKeeping.FallbackFolderIn(work.Path));

        // A folder that exists under another language's name (the German one of earlier 1.1.7 builds) stays in use.
        Directory.CreateDirectory(work.File("Ersatzordner"));
        using (new LanguageScope("en")) Assert.Equal(work.File("Ersatzordner"), FileKeeping.FallbackFolderIn(work.Path));
        using (new LanguageScope("tr")) Assert.Equal(work.File("Ersatzordner"), FileKeeping.FallbackFolderIn(work.Path));

        // Every name is a valid folder name, one per language, and a capture in any of them counts as rescued.
        var names = FileKeeping.FallbackFolderNames.ToList();
        Assert.Equal(7, names.Count);
        Assert.All(names, name => Assert.True(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, name));
        Assert.True(FileKeeping.IsInFallback(Path.Combine(CaptureSettings.Home, "Ersatzordner", "a.png")));
        Assert.True(FileKeeping.IsInFallback(Path.Combine(CaptureSettings.Home, "Backup folder", "a.png")));
        Assert.False(FileKeeping.IsInFallback(Path.Combine(CaptureSettings.Home, "unsaved", "a.png")));
    }
}
