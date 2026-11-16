using NazarCapture.Engine.Updates;

namespace NazarCapture.Engine.Tests.Core;

/// <summary>Phase 7b: the daily update check, the checksum and the confirmed installation.</summary>
public sealed class UpdateTests : IDisposable
{
    readonly TempDirectory work = new();

    public void Dispose() => work.Dispose();

    string State => work.File("update-state.json");

    [Theory]
    [InlineData("1.1.4", "1.1.3", true)]
    [InlineData("v1.2.0", "1.1.3", true)]
    [InlineData("1.1.3", "1.1.3", false)]
    [InlineData("1.1.2", "1.1.3", false)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("kaputt", "1.1.3", false)]
    [InlineData("", "1.1.3", false)]
    public void NewerSameOrOlder(string candidate, string current, bool newer) => Assert.Equal(newer, UpdateCheck.IsNewer(candidate, current));

    [Fact]
    public void AsksAtMostOnceADayAndRemembersTheAnswer()
    {
        double now = 1000;
        int asked = 0;
        var check = new UpdateCheck(State, () => { asked++; return new Release("1.1.4", "p", "d", "abc"); }, () => now, "1.1.3");
        Assert.Equal("1.1.4", check.Run()!.Version);
        Assert.Equal("1.1.4", check.Run()!.Version);   // from the state, without asking
        Assert.Equal(1, asked);
        now += TimeSpan.FromDays(1).TotalSeconds;
        check.Run();
        Assert.Equal(2, asked);
    }

    [Fact]
    public void NoNetworkOrARateLimitMeansNothingNewAndNoSecondAttemptToday()
    {
        int asked = 0;
        var check = new UpdateCheck(State, () => { asked++; return null; }, () => 50, "1.1.3");
        Assert.Null(check.Run());
        Assert.Null(check.Run());
        Assert.Equal(1, asked);
    }

    [Fact]
    public void ADismissedVersionIsQuietAndAnnouncedOnlyOnce()
    {
        var check = new UpdateCheck(State, () => new Release("1.1.4", "p", "d", "abc"), () => 1, "1.1.3");
        var release = check.Run()!;
        Assert.True(check.FirstNotice(release.Version));
        Assert.False(check.FirstNotice(release.Version));
        check.Dismiss("1.1.4");
        Assert.Null(check.Run());
        Assert.Null(new UpdateCheck(State, () => null, () => 1, "1.1.4").Run());   // installed meanwhile: nothing to say
    }

    [Fact]
    public void TheApiAnswerGivesVersionPageZipAndDigest()
    {
        string json = "{\"tag_name\":\"v1.1.4\",\"html_url\":\"https://github.com/" + UpdateCheck.Repository + "/releases/tag/v1.1.4\",\"assets\":[" +
            "{\"name\":\"nazar-capture-1.1.4-win-x64.zip\",\"browser_download_url\":\"https://github.com/" + UpdateCheck.Repository + "/releases/download/v1.1.4/nazar-capture-1.1.4-win-x64.zip\",\"digest\":\"sha256:ABCDEF\"}]}";
        var release = UpdateCheck.ParseRelease(json)!;
        Assert.Equal("1.1.4", release.Version);
        Assert.EndsWith("/releases/tag/v1.1.4", release.Page);
        Assert.EndsWith("-win-x64.zip", release.Download);
        Assert.Equal("abcdef", release.Sha256);
        Assert.Null(UpdateCheck.ParseRelease("{\"message\":\"API rate limit exceeded\"}"));
        Assert.Null(UpdateCheck.ParseRelease("nicht json"));
        var foreign = UpdateCheck.ParseRelease("{\"tag_name\":\"1.1.4\",\"html_url\":\"https://example.com/x\",\"assets\":[{\"name\":\"a-win-x64.zip\",\"browser_download_url\":\"https://example.com/a-win-x64.zip\"}]}")!;
        Assert.Equal(UpdateCheck.ReleasesPage, foreign.Page);   // only pages and downloads of this repository
        Assert.Null(foreign.Download);
    }

    static string Zip(TempDirectory work, string content)
    {
        string folder = work.File("payload");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "NazarCapture.exe"), content);
        string zip = work.File("release.zip");
        if (File.Exists(zip)) File.Delete(zip);
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);
        return zip;
    }

    [Fact]
    public void AMatchingChecksumUnpacksTheInstaller()
    {
        string zip = Zip(work, "neu");
        var release = new Release("1.1.4", "p", "https://x/zip", UpdateInstaller.Sha256Of(zip));
        var (installer, problem) = UpdateInstaller.Prepare(release, work.File("updates"), (_, file) => File.Copy(zip, file, true), _ => "");
        Assert.Equal(UpdateProblem.None, problem);
        Assert.Equal("neu", File.ReadAllText(installer!));
    }

    [Fact]
    public void ABrokenDownloadIsNeverInstalled()
    {
        string zip = Zip(work, "neu");
        var release = new Release("1.1.4", "p", "https://x/zip", new string('0', 64));
        var (installer, problem) = UpdateInstaller.Prepare(release, work.File("updates"), (_, file) => File.Copy(zip, file, true), _ => "");
        Assert.Equal(UpdateProblem.ChecksumMismatch, problem);
        Assert.Null(installer);
        Assert.False(File.Exists(work.File("updates\\update-1.1.4.zip")));
        Assert.Equal(UpdateProblem.NoChecksum, UpdateInstaller.Prepare(release with { Sha256 = null }, work.File("updates"), (_, _) => { }, _ => "").Problem);
        Assert.Equal(UpdateProblem.DownloadFailed, UpdateInstaller.Prepare(release, work.File("updates"), (_, _) => throw new System.Net.WebException("offline"), _ => "").Problem);
    }

    [Fact]
    public void TheChecksumMayComeFromASidecarFile()
    {
        string zip = Zip(work, "neu");
        string sha = UpdateInstaller.Sha256Of(zip);
        var release = new Release("1.1.4", "p", "https://x/zip", "sidecar:https://x/zip.sha256");
        Assert.Equal(sha, UpdateInstaller.ExpectedSha(release, _ => sha.ToUpperInvariant() + "  nazar-capture-1.1.4-win-x64.zip\n"));
        Assert.Null(UpdateInstaller.ExpectedSha(release, _ => "kaputt"));
    }

    [Fact]
    public void TheCardInstallsOnlyAfterTheClickAndSaysWhyNot() => WpfThread.Run(() =>
    {
        var check = new UpdateCheck(State, () => null, () => 1, "1.1.3");
        var started = new List<string>();
        var card = new UpdateCard(check) { Offscreen = true, StartInstaller = exe => { started.Add(exe); return true; } };
        card.Prepare = _ => (null, UpdateProblem.ChecksumMismatch);
        card.Present(new Release("1.1.4", "p", "d", "s"), new RECT { right = 1920, bottom = 1040 });
        Assert.Empty(started);   // showing the notice installs nothing
        var install = card.Install();
        Assert.True(WpfThread.PumpUntil(() => install.IsCompleted, 5000));
        Assert.Empty(started);
        Assert.True(card.Plate.IsError);
        Assert.Equal(UiText.Update.Reason(UpdateProblem.ChecksumMismatch), card.Plate.Subtitle.Text);

        card.Prepare = _ => ("C:\\x\\NazarCapture.exe", UpdateProblem.None);
        install = card.Install();
        Assert.True(WpfThread.PumpUntil(() => install.IsCompleted, 5000));
        Assert.Equal(new[] { "C:\\x\\NazarCapture.exe" }, started);
        card.Close();
    });
}
