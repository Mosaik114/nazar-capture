using Microsoft.Win32;
using NazarCapture.Engine.Updates;

namespace NazarCapture.Engine.Tests.Core;

/// <summary>
/// The in-app update end to end with a real setup, but without GitHub and without the real installation: a fake release
/// (the API answer and the downloads come from local files), the real check, download, checksum and silent start, and
/// then the setup really installs into a test folder and starts the app again. Opt-in: it needs a setup built with its
/// own test names (packaging\nazar.iss test defines: Apps entry, shortcut, autostart value, settings folder, mutexes of
/// the test instance below), named in NAZAR_UPDATE_E2E_SETUP, its AppId in NAZAR_UPDATE_E2E_APPID and an empty install
/// folder in NAZAR_UPDATE_E2E_DIR. Every program it starts runs as the isolated test instance
/// <see cref="Instance"/>.
/// </summary>
public sealed class UpdateEndToEndTests
{
    const string Instance = "update-e2e";
    const string Version = "99.0.0";
    const string Assets = UpdateCheck.DownloadPrefix + "v" + Version + "/";
    static readonly string SetupName = UpdateCheck.SetupPrefix + Version + ".exe";

    static string? Variable(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    static Process? Host(string exe) =>
        Process.GetProcessesByName("NazarCapture").FirstOrDefault(p =>
        {
            try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
            catch (System.ComponentModel.Win32Exception) { return false; }
        });

    [Fact]
    public async Task TheSetupFromAFakeReleaseInstallsSilentlyAndTheAppStartsAgain()
    {
        string? setup = Variable("NAZAR_UPDATE_E2E_SETUP"), folder = Variable("NAZAR_UPDATE_E2E_DIR"), appId = Variable("NAZAR_UPDATE_E2E_APPID");
        Assert.SkipWhen(setup == null || folder == null || appId == null, "opt-in: NAZAR_UPDATE_E2E_SETUP, NAZAR_UPDATE_E2E_DIR and NAZAR_UPDATE_E2E_APPID name a test setup");
        Assert.False(Directory.Exists(folder), "the install folder must not exist yet");
        using var work = new TempDirectory();
        string sha = UpdateInstaller.Sha256Of(setup!);

        // The fake GitHub: the API answer, the setup and its checksum file, all at this repository's release addresses.
        string api = "{\"tag_name\":\"v" + Version + "\",\"html_url\":\"" + UpdateCheck.ReleasesPage + "/tag/v" + Version + "\",\"assets\":[" +
            "{\"name\":\"" + SetupName + "\",\"browser_download_url\":\"" + Assets + SetupName + "\"}," +
            "{\"name\":\"" + SetupName + ".sha256\",\"browser_download_url\":\"" + Assets + SetupName + ".sha256\"}]}";
        var asked = new List<string>();
        void DownloadFile(string url, string file)
        {
            asked.Add(url);
            Assert.Equal(Assets + SetupName, url);
            File.Copy(setup!, file, true);
        }
        string DownloadText(string url)
        {
            asked.Add(url);
            Assert.Equal(Assets + SetupName + ".sha256", url);
            return sha + "  " + SetupName + "\n";
        }

        var check = new UpdateCheck(work.File("update-state.json"), () => UpdateCheck.ParseRelease(api), () => 1, "1.1.5");
        var (outcome, release) = check.CheckNow();
        Assert.Equal(CheckOutcome.Found, outcome);

        string? started = null;
        var installation = new UpdateInstallation
        {
            Busy = () => false,
            Prepare = r => UpdateInstaller.Prepare(r, work.File("downloads"), DownloadFile, DownloadText),
            // As in the app, plus only the test folder (the app never passes /DIR: it updates the default folder).
            Start = update => Shell.Open(update.Program, started = update.Arguments + " /DIR=\"" + folder + "\""),
        };
        string exe = Path.Combine(folder!, "NazarCapture.exe");
        string uninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + appId + "}_is1";
        string? outer = Environment.GetEnvironmentVariable("NAZAR_CAPTURE_TEST_INSTANCE");
        Environment.SetEnvironmentVariable("NAZAR_CAPTURE_TEST_INSTANCE", Instance);   // the setup and the app it starts inherit it
        try
        {
            var problem = await installation.InstallAsync(release!);
            Assert.Equal(UpdateProblem.None, problem);
            Assert.StartsWith(UpdateInstaller.SetupArguments, started);
            Assert.Equal(new[] { Assets + SetupName + ".sha256", Assets + SetupName }, asked);

            Assert.True(SpinWait.SpinUntil(() => Host(exe) != null, TimeSpan.FromSeconds(90)), "the app runs again from the test folder");
            // Windows "Apps" now shows the installed version (the ZIP way never updated it).
            using (var key = Registry.CurrentUser.OpenSubKey(uninstallKey))
                Assert.Equal(NazarCapture.Engine.Settings.AppInfo.Version, key?.GetValue("DisplayVersion") as string);
        }
        finally
        {
            // quit the restarted test app through its own --quit, then remove the test installation
            using (var quit = Process.Start(exe, "--quit")) quit?.WaitForExit(30000);
            SpinWait.SpinUntil(() => Host(exe) == null, TimeSpan.FromSeconds(30));
            Environment.SetEnvironmentVariable("NAZAR_CAPTURE_TEST_INSTANCE", outer);
            string uninstaller = Path.Combine(folder!, "unins000.exe");
            if (File.Exists(uninstaller))
                using (var uninstall = Process.Start(uninstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART")) uninstall?.WaitForExit(60000);
            SpinWait.SpinUntil(() => !Directory.Exists(folder), TimeSpan.FromSeconds(60));
        }
        Assert.False(Directory.Exists(folder), "the test installation is gone again");
        using (var gone = Registry.CurrentUser.OpenSubKey(uninstallKey)) Assert.Null(gone);
    }
}
