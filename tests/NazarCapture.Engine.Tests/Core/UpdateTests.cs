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

    const string Assets = UpdateCheck.DownloadPrefix + "v1.1.6/";
    const string ZipUrl = Assets + "nazar-capture-1.1.6-win-x64.zip";
    const string SetupName = "Nazar-Capture-Setup-1.1.6.exe";
    const string SetupUrl = Assets + SetupName;

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

    /// <summary>A fake release on a fake GitHub: what each address delivers (a missing address fails like the network).</summary>
    sealed class FakeGitHub
    {
        public readonly Dictionary<string, string> Files = new(), Texts = new();
        public readonly List<string> Asked = new();

        public void DownloadFile(string url, string file)
        {
            Asked.Add(url);
            if (!Files.TryGetValue(url, out var source)) throw new System.Net.WebException("404 " + url);
            File.Copy(source, file, true);
        }

        public string DownloadText(string url)
        {
            Asked.Add(url);
            return Texts.TryGetValue(url, out var text) ? text : throw new System.Net.WebException("404 " + url);
        }
    }

    (string Setup, string Sha) FakeSetup(string content = "setup")
    {
        string setup = work.File("built\\" + SetupName);
        Directory.CreateDirectory(Path.GetDirectoryName(setup)!);
        File.WriteAllText(setup, content);
        return (setup, UpdateInstaller.Sha256Of(setup));
    }

    static Release SetupRelease => new("1.1.6", "p", ZipUrl, UpdateInstaller.Sidecar + ZipUrl + ".sha256", SetupUrl, UpdateInstaller.Sidecar + SetupUrl + ".sha256");

    [Fact]
    public void TheApiAnswerGivesTheSetupAndItsChecksumFile()
    {
        string json = "{\"tag_name\":\"v1.1.6\",\"html_url\":\"" + UpdateCheck.ReleasesPage + "/tag/v1.1.6\",\"assets\":[" +
            "{\"name\":\"" + SetupName + "\",\"browser_download_url\":\"" + SetupUrl + "\"}," +
            "{\"name\":\"" + SetupName + ".sha256\",\"browser_download_url\":\"" + SetupUrl + ".sha256\"}," +
            "{\"name\":\"nazar-capture-1.1.6-win-x64.zip\",\"browser_download_url\":\"" + ZipUrl + "\"}," +
            "{\"name\":\"nazar-capture-1.1.6-win-x64.zip.sha256\",\"browser_download_url\":\"" + ZipUrl + ".sha256\"}]}";
        var release = UpdateCheck.ParseRelease(json)!;
        Assert.Equal(SetupUrl, release.Setup);
        Assert.Equal(UpdateInstaller.Sidecar + SetupUrl + ".sha256", release.SetupSha256);
        Assert.Equal(ZipUrl, release.Download);
        Assert.Equal(UpdateInstaller.Sidecar + ZipUrl + ".sha256", release.Sha256);

        // A setup from somewhere else is not taken, and then the ZIP is the way.
        string foreign = json.Replace(SetupUrl, "https://example.com/" + SetupName);
        Assert.Null(UpdateCheck.ParseRelease(foreign)!.Setup);
        Assert.Equal(ZipUrl, UpdateCheck.ParseRelease(foreign)!.Download);
    }

    [Theory]
    [InlineData(UpdateCheck.DownloadPrefix + "v1.1.6/a.exe", true)]
    [InlineData("https://github.com/Mosaik114/nazar-capture/releases/download/", false)]
    [InlineData("http://github.com/Mosaik114/nazar-capture/releases/download/v1/a.exe", false)]
    [InlineData("https://github.com/Mosaik114/nazar-capture-fork/releases/download/v1/a.exe", false)]
    [InlineData("https://github.com/Mosaik114/nazar-capture/releases/download/../../../evil/a.exe", false)]
    [InlineData("https://github.com/Mosaik114/nazar-capture/releases/download/v1/a.exe?x=https://evil", false)]
    [InlineData("https://github.com.evil.com/Mosaik114/nazar-capture/releases/download/v1/a.exe", false)]
    [InlineData("https://github.com/Mosaik114/nazar-capture/releases/download@evil.com/a.exe", false)]
    [InlineData(null, false)]
    public void OnlyThisRepositorysReleaseAssetsAreTrusted(string? url, bool trusted) => Assert.Equal(trusted, UpdateCheck.IsTrusted(url));

    [Fact]
    public void TheSetupIsInstalledWhenItMatchesItsChecksumFile()
    {
        var (setup, sha) = FakeSetup();
        var github = new FakeGitHub();
        github.Files[SetupUrl] = setup;
        github.Texts[SetupUrl + ".sha256"] = sha + "  " + SetupName + "\r\n";
        var (update, problem) = UpdateInstaller.Prepare(SetupRelease, work.File("updates"), github.DownloadFile, github.DownloadText);
        Assert.Equal(UpdateProblem.None, problem);
        Assert.Equal(work.File("updates\\" + SetupName), update!.Program);
        Assert.StartsWith("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTAPP=1 /LOG=", update.Arguments);
        Assert.Equal(sha, UpdateInstaller.Sha256Of(update.Program));
        Assert.DoesNotContain(ZipUrl, github.Asked);   // the ZIP is not needed
    }

    [Fact]
    public void ASetupWithoutAMatchingChecksumIsNeverStarted()
    {
        var (setup, sha) = FakeSetup();
        string folder = work.File("updates");
        var github = new FakeGitHub();
        github.Files[SetupUrl] = setup;
        github.Files[ZipUrl] = Zip(work, "zip");

        // the checksum file is missing: nothing is downloaded, and no way around it (not even the ZIP)
        var (update, problem) = UpdateInstaller.Prepare(SetupRelease, folder, github.DownloadFile, github.DownloadText);
        Assert.Equal((null, UpdateProblem.NoChecksum), (update, problem));
        Assert.DoesNotContain(SetupUrl, github.Asked);
        Assert.Equal(UpdateProblem.NoChecksum, UpdateInstaller.Prepare(SetupRelease with { SetupSha256 = null }, folder, github.DownloadFile, github.DownloadText).Problem);

        // a checksum file that is no checksum, or names another file
        github.Texts[SetupUrl + ".sha256"] = "kaputt";
        Assert.Equal(UpdateProblem.NoChecksum, UpdateInstaller.Prepare(SetupRelease, folder, github.DownloadFile, github.DownloadText).Problem);
        github.Texts[SetupUrl + ".sha256"] = sha + "  Nazar-Capture-Setup-1.1.5.exe";
        Assert.Equal(UpdateProblem.NoChecksum, UpdateInstaller.Prepare(SetupRelease, folder, github.DownloadFile, github.DownloadText).Problem);

        // the download does not match: deleted, never started
        github.Texts[SetupUrl + ".sha256"] = new string('0', 64) + "  " + SetupName;
        Assert.Equal(UpdateProblem.ChecksumMismatch, UpdateInstaller.Prepare(SetupRelease, folder, github.DownloadFile, github.DownloadText).Problem);
        Assert.False(File.Exists(Path.Combine(folder, SetupName)));

        // the download fails
        github.Texts[SetupUrl + ".sha256"] = sha;
        github.Files.Remove(SetupUrl);
        Assert.Equal(UpdateProblem.DownloadFailed, UpdateInstaller.Prepare(SetupRelease, folder, github.DownloadFile, github.DownloadText).Problem);
        Assert.False(File.Exists(Path.Combine(folder, SetupName)));
    }

    [Fact]
    public void AddressesOutsideTheReleasesAreNeverFetched()
    {
        var github = new FakeGitHub();
        string folder = work.File("updates");
        var foreignSetup = SetupRelease with { Setup = "https://example.com/" + SetupName };
        var foreignSidecar = SetupRelease with { SetupSha256 = UpdateInstaller.Sidecar + "https://example.com/x.sha256" };
        var notASetup = SetupRelease with { Setup = Assets + "evil.exe" };
        var foreignZip = new Release("1.1.6", "p", "https://example.com/a-win-x64.zip", new string('0', 64));
        foreach (var release in new[] { foreignSetup, foreignSidecar, notASetup, foreignZip })
            Assert.Equal(UpdateProblem.UntrustedSource, UpdateInstaller.Prepare(release, folder, github.DownloadFile, github.DownloadText).Problem);
        Assert.Empty(github.Asked);
    }

    [Fact]
    public void WithoutASetupTheZipOfEarlierReleasesIsTheWay()
    {
        string zip = Zip(work, "neu");
        var github = new FakeGitHub();
        github.Files[ZipUrl] = zip;
        github.Texts[ZipUrl + ".sha256"] = UpdateInstaller.Sha256Of(zip).ToUpperInvariant() + "  nazar-capture-1.1.6-win-x64.zip\n";
        var release = SetupRelease with { Setup = null, SetupSha256 = null };
        var (update, problem) = UpdateInstaller.Prepare(release, work.File("updates"), github.DownloadFile, github.DownloadText);
        Assert.Equal(UpdateProblem.None, problem);
        Assert.Equal("neu", File.ReadAllText(update!.Program));
        Assert.Equal("--install", update.Arguments);
    }

    [Fact]
    public void ABrokenZipIsNeverInstalled()
    {
        string zip = Zip(work, "neu");
        var github = new FakeGitHub();
        github.Files[ZipUrl] = zip;
        var release = new Release("1.1.4", "p", ZipUrl, new string('0', 64));
        var (update, problem) = UpdateInstaller.Prepare(release, work.File("updates"), github.DownloadFile, github.DownloadText);
        Assert.Equal(UpdateProblem.ChecksumMismatch, problem);
        Assert.Null(update);
        Assert.False(File.Exists(work.File("updates\\update-1.1.4.zip")));
        Assert.Equal(UpdateProblem.NoChecksum, UpdateInstaller.Prepare(release with { Sha256 = null }, work.File("updates"), github.DownloadFile, github.DownloadText).Problem);
        Assert.Equal(UpdateProblem.NoDownload, UpdateInstaller.Prepare(release with { Download = null }, work.File("updates"), github.DownloadFile, github.DownloadText).Problem);
        github.Files.Clear();
        Assert.Equal(UpdateProblem.DownloadFailed, UpdateInstaller.Prepare(release with { Sha256 = UpdateInstaller.Sha256Of(zip) }, work.File("updates"), github.DownloadFile, github.DownloadText).Problem);
    }

    [Fact]
    public void TheChecksumMayComeFromASidecarFile()
    {
        string zip = Zip(work, "neu");
        string sha = UpdateInstaller.Sha256Of(zip);
        var release = new Release("1.1.4", "p", ZipUrl, UpdateInstaller.Sidecar + ZipUrl + ".sha256");
        Assert.Equal(sha, UpdateInstaller.ExpectedSha(release, _ => sha.ToUpperInvariant() + "  nazar-capture-1.1.4-win-x64.zip\n"));
        Assert.Null(UpdateInstaller.ExpectedSha(release, _ => "kaputt"));
        Assert.Null(UpdateInstaller.ExpectedSha(release, _ => new string('g', 64)));   // not hex
    }

    [Fact]
    public void ASearchNowAsksAtOnceAndSaysWhatItFound()
    {
        Release? answer = null;
        int asked = 0;
        var check = new UpdateCheck(State, () => { asked++; return answer; }, () => 100, "1.1.6");
        Assert.Equal(CheckOutcome.Failed, check.CheckNow().Outcome);
        answer = new Release("1.1.6", "p", null, null);
        Assert.Equal(CheckOutcome.UpToDate, check.CheckNow().Outcome);
        answer = SetupRelease with { Version = "1.1.7" };
        check.Dismiss("1.1.7");
        var (outcome, found) = check.CheckNow();   // asked on purpose: a hidden notice comes back
        Assert.Equal(CheckOutcome.Found, outcome);
        Assert.Equal(SetupUrl, found!.Setup);
        Assert.Equal(UpdateInstaller.Sidecar + SetupUrl + ".sha256", found.SetupSha256);
        Assert.Equal("1.1.7", check.Run()!.Version);   // remembered, including the setup
        Assert.Equal(SetupUrl, check.Run()!.Setup);
        Assert.Equal(3, asked);   // the daily check does not ask again the same day
    }

    [Fact]
    public void NothingIsStartedWhileARecordingRuns() => WpfThread.Run(() =>
    {
        bool busy = true;
        var started = new List<PreparedUpdate>();
        int prepared = 0;
        var installation = new UpdateInstallation
        {
            Busy = () => busy,
            Prepare = _ => { prepared++; return (new PreparedUpdate("C:\\x\\" + SetupName, UpdateInstaller.SetupArguments), UpdateProblem.None); },
            Start = update => { started.Add(update); return true; },
        };
        var run = installation.InstallAsync(SetupRelease);
        Assert.True(WpfThread.PumpUntil(() => run.IsCompleted, 5000));
        Assert.Equal(UpdateProblem.Busy, run.Result);
        Assert.Equal(0, prepared);

        installation.Prepare = _ => { prepared++; busy = true; return (new PreparedUpdate("C:\\x\\" + SetupName, ""), UpdateProblem.None); };   // a recording starts meanwhile
        busy = false;
        run = installation.InstallAsync(SetupRelease);
        Assert.True(WpfThread.PumpUntil(() => run.IsCompleted, 5000));
        Assert.Equal(UpdateProblem.Busy, run.Result);
        Assert.Empty(started);

        busy = false;
        installation.Prepare = _ => (new PreparedUpdate("C:\\x\\" + SetupName, UpdateInstaller.SetupArguments), UpdateProblem.None);
        run = installation.InstallAsync(SetupRelease);
        Assert.True(WpfThread.PumpUntil(() => run.IsCompleted, 5000));
        Assert.Equal(UpdateProblem.None, run.Result);
        Assert.Single(started);
        installation.Start = _ => false;
        run = installation.InstallAsync(SetupRelease);
        Assert.True(WpfThread.PumpUntil(() => run.IsCompleted, 5000));
        Assert.Equal(UpdateProblem.StartFailed, run.Result);
    });

    [Fact]
    public void TheCardInstallsOnlyAfterTheClickAndSaysWhyNot() => WpfThread.Run(() =>
    {
        var check = new UpdateCheck(State, () => null, () => 1, "1.1.3");
        var started = new List<string>();
        var card = new UpdateCard(check) { Offscreen = true };
        card.Installation = new UpdateInstallation
        {
            Busy = () => false,
            Prepare = _ => (null, UpdateProblem.ChecksumMismatch),
            Start = update => { started.Add(update.Program); return true; },
        };
        card.Present(new Release("1.1.4", "p", "d", "s"), new RECT { right = 1920, bottom = 1040 });
        Assert.Empty(started);   // showing the notice installs nothing
        var install = card.Install();
        Assert.True(WpfThread.PumpUntil(() => install.IsCompleted, 5000));
        Assert.Empty(started);
        Assert.True(card.Plate.IsError);
        Assert.Equal(UiText.Update.Reason(UpdateProblem.ChecksumMismatch), card.Plate.Subtitle.Text);

        card.Installation.Prepare = _ => (new PreparedUpdate("C:\\x\\" + SetupName, UpdateInstaller.SetupArguments), UpdateProblem.None);
        install = card.Install();
        Assert.True(WpfThread.PumpUntil(() => install.IsCompleted, 5000));
        Assert.Equal(new[] { "C:\\x\\" + SetupName }, started);
        Assert.False(card.Plate.IsError);
        card.Close();
    });
}
