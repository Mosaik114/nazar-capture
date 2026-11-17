using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace NazarCapture.Engine.Updates;

/// <summary>
/// A release on GitHub: version, page, the ZIP and its SHA-256 (the asset digest, or <c>sidecar:&lt;url&gt;</c>), and
/// since 1.1.6 the setup <c>Nazar-Capture-Setup-&lt;version&gt;.exe</c> with its checksum file (<c>sidecar:&lt;url&gt;</c>).
/// </summary>
internal sealed record Release(string Version, string Page, string? Download, string? Sha256, string? Setup = null, string? SetupSha256 = null);

/// <summary>Why an update could not be installed (said on the card and in the settings row).</summary>
internal enum UpdateProblem
{
    None,
    NoDownload,
    DownloadFailed,
    ChecksumMismatch,
    NoChecksum,
    InstallerMissing,
    StartFailed,
    /// <summary>A download or checksum address outside this repository's releases: never fetched.</summary>
    UntrustedSource,
    /// <summary>A recording (or another capture) is running; installing would end it.</summary>
    Busy,
}

/// <summary>What the manual check found.</summary>
internal enum CheckOutcome
{
    UpToDate,
    Found,
    Failed,
}

/// <summary>A downloaded and checked update, ready to start: the program and its arguments.</summary>
internal sealed record PreparedUpdate(string Program, string Arguments);

/// <summary>
/// What the update check remembers between starts (<c>update-state.json</c> next to settings.ini, written by the engine
/// only; settings.ini stays the host's): when it last asked, the latest version and which version was dismissed or
/// already announced.
/// </summary>
internal sealed class UpdateState
{
    public double LastCheck { get; set; }
    public string Latest { get; set; } = "";
    public string Page { get; set; } = "";
    public string Download { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Setup { get; set; } = "";
    public string SetupSha256 { get; set; } = "";
    public string Dismissed { get; set; } = "";
    public string Notified { get; set; } = "";

    /// <summary>At most once a day; a clock that went backwards counts as due.</summary>
    public bool Due(double now) => LastCheck <= 0 || now - LastCheck >= UpdateCheck.Interval.TotalSeconds || now < LastCheck;   // 0: never asked

    /// <summary>The release to point out: newer than <paramref name="current"/> and not dismissed.</summary>
    public Release? Pending(string current) =>
        UpdateCheck.IsNewer(Latest, current) && Latest != Dismissed ? Remembered() : null;

    /// <summary>The latest release as remembered.</summary>
    internal Release Remembered() =>
        new(Latest, Page, OrNull(Download), OrNull(Sha256), OrNull(Setup), OrNull(SetupSha256));

    internal void Remember(Release release)
    {
        Latest = release.Version;
        Page = release.Page;
        Download = release.Download ?? "";
        Sha256 = release.Sha256 ?? "";
        Setup = release.Setup ?? "";
        SetupSha256 = release.SetupSha256 ?? "";
    }

    static string? OrNull(string text) => text.Length > 0 ? text : null;
}

/// <summary>
/// The update check (as Hams' updates.py): once a day, read-only and without an account, it asks GitHub for the latest
/// release. Any problem (offline, rate limit, broken answer) is logged and means "nothing new".
/// </summary>
internal sealed class UpdateCheck
{
    public const string Repository = "Mosaik114/nazar-capture";
    public const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    /// <summary>The only place downloads and checksums are fetched from: this repository's release assets.</summary>
    public const string DownloadPrefix = ReleasesPage + "/download/";
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    readonly string statePath;
    readonly Func<Release?> fetch;
    readonly Func<double> clock;

    public UpdateCheck(string? statePath = null, Func<Release?>? fetch = null, Func<double>? clock = null, string? current = null)
    {
        this.statePath = statePath ?? Path.Combine(CaptureSettings.Home, "update-state.json");
        this.fetch = fetch ?? FetchLatest;
        this.clock = clock ?? (() => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
        Current = current ?? Settings.AppInfo.Version;
    }

    public string Current { get; }

    /// <summary>"1.2.3" from "v1.2.3"; null if the text is no version.</summary>
    public static int[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string clean = text!.Trim().TrimStart('v', 'V');
        var parts = clean.Split('.');
        if (parts.Length is < 2 or > 4) return null;
        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        return numbers;
    }

    public static bool IsNewer(string? candidate, string current)
    {
        var a = Parse(candidate);
        var b = Parse(current);
        if (a == null || b == null) return false;
        for (int i = 0; i < 4; i++)
            if (a[i] != b[i]) return a[i] > b[i];
        return false;
    }

    /// <summary>The release worth a notice, asking GitHub only when the last attempt is a day old.</summary>
    public Release? Run()
    {
        var state = Load();
        double now = clock();
        if (state.Due(now))
        {
            state.LastCheck = now;   // also on failure: at most one attempt a day
            var release = fetch();
            if (release != null) state.Remember(release);
            Save(state);
        }
        return state.Pending(Current);
    }

    /// <summary>
    /// "Jetzt nach Updates suchen": asks GitHub now, whatever the daily rhythm says, and remembers the answer. A version
    /// found this way is shown even if its notice was hidden before (the user asked).
    /// </summary>
    public (CheckOutcome Outcome, Release? Release) CheckNow()
    {
        var state = Load();
        state.LastCheck = clock();
        var release = fetch();
        if (release == null)
        {
            Save(state);
            return (CheckOutcome.Failed, null);
        }
        state.Remember(release);
        bool newer = IsNewer(release.Version, Current);
        if (newer && state.Dismissed == release.Version) state.Dismissed = "";
        Save(state);
        return newer ? (CheckOutcome.Found, state.Remembered()) : (CheckOutcome.UpToDate, null);
    }

    /// <summary>Whether <paramref name="url"/> is a release asset of this repository (https, this owner and name).</summary>
    public static bool IsTrusted(string? url) =>
        url != null && url.StartsWith(DownloadPrefix, StringComparison.Ordinal) && url.Length > DownloadPrefix.Length
        && url.IndexOf("..", StringComparison.Ordinal) < 0 && url.IndexOfAny(UrlForbidden) < 0;

    static readonly char[] UrlForbidden = { '?', '#', '\\', '@' };

    public void Dismiss(string version)
    {
        var state = Load();
        state.Dismissed = version;
        Save(state);
    }

    /// <summary>True exactly once per version: whether the card may announce it.</summary>
    public bool FirstNotice(string version)
    {
        var state = Load();
        if (state.Notified == version) return false;
        state.Notified = version;
        Save(state);
        return true;
    }

    internal UpdateState Load()
    {
        try
        {
            if (!File.Exists(statePath)) return new UpdateState();
            return new JavaScriptSerializer().Deserialize<UpdateState>(File.ReadAllText(statePath, Encoding.UTF8)) ?? new UpdateState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ErrorLog.Write("Update check: unreadable state, starting fresh", ex);
            return new UpdateState();
        }
    }

    void Save(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, new JavaScriptSerializer().Serialize(state), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Update check: could not write the state", ex);
        }
    }

    /// <summary>Asks GitHub for the latest release; null on any problem.</summary>
    internal static Release? FetchLatest()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(LatestApi);
            request.Accept = "application/vnd.github+json";
            request.UserAgent = "NazarCapture-UpdateCheck";
            request.Timeout = request.ReadWriteTimeout = (int)RequestTimeout.TotalMilliseconds;
            using var response = (HttpWebResponse)request.GetResponse();
            using var reader = new StreamReader(response.GetResponseStream()!, Encoding.UTF8);
            return ParseRelease(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is WebException or IOException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            ErrorLog.Write("Update check failed (" + ex.GetType().Name + ")", null);
            return null;
        }
    }

    /// <summary>The file name of the setup of a release (as build.ps1 -Installer names it).</summary>
    public const string SetupPrefix = "Nazar-Capture-Setup-";

    /// <summary>
    /// Reads the API answer: tag, page (only a page of this repository), the setup and its checksum file
    /// "&lt;setup&gt;.sha256" (both required for the setup way), and the Windows ZIP with its checksum (the asset's
    /// <c>digest</c> "sha256:…", else a sidecar asset "&lt;zip&gt;.sha256" is fetched later). Assets outside this
    /// repository's releases are ignored.
    /// </summary>
    internal static Release? ParseRelease(string json)
    {
        object? parsed;
        try { parsed = new JavaScriptSerializer().DeserializeObject(json); }
        catch (ArgumentException) { return null; }
        if (parsed is not Dictionary<string, object> data || data.TryGetValue("tag_name", out var tag) is false || tag is not string tagText || Parse(tagText) == null) return null;
        string page = data.TryGetValue("html_url", out var url) && url is string text && text.StartsWith(ReleasesPage + "/", StringComparison.Ordinal) ? text : ReleasesPage;
        string? download = null, sha = null, sidecar = null, setup = null, setupSidecar = null;
        if (data.TryGetValue("assets", out var assets) && assets is object[] list)
            foreach (var asset in list.OfType<Dictionary<string, object>>())
            {
                string name = asset.TryGetValue("name", out var n) ? n as string ?? "" : "";
                string link = asset.TryGetValue("browser_download_url", out var l) ? l as string ?? "" : "";
                if (!IsTrusted(link)) continue;
                if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                {
                    download = link;
                    if (asset.TryGetValue("digest", out var d) && d is string digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = digest.Substring(7).ToLowerInvariant();
                }
                else if (name.EndsWith("-win-x64.zip.sha256", StringComparison.OrdinalIgnoreCase)) sidecar = link;
                else if (name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) setup = link;
                else if (name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) setupSidecar = link;
            }
        return new Release(tagText.TrimStart('v', 'V'), page, download, sha ?? (sidecar != null ? UpdateInstaller.Sidecar + sidecar : null),
            setup, setupSidecar != null ? UpdateInstaller.Sidecar + setupSidecar : null);
    }
}

/// <summary>
/// Installs a release after the user said "Jetzt aktualisieren". The setup way (1.1.6 on): downloads
/// <c>Nazar-Capture-Setup-&lt;version&gt;.exe</c>, checks its SHA-256 against the release's
/// <c>&lt;setup&gt;.sha256</c> and starts it silently (<see cref="SetupArguments"/>): it closes the running app through
/// its own quit (refused while a recording runs), installs over it, updates the entry in Windows "Apps" and starts the
/// new version in the tray. Releases without a setup take the ZIP way of 1.1.3 to 1.1.5: the ZIP is checked the same
/// way, unpacked, and its <c>NazarCapture.exe --install</c> started. Nothing is ever started without a matching
/// checksum, and nothing is fetched from outside this repository's releases. Settings stay where they are.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>Marks a checksum that is read from a release asset (a "sidecar" file) instead of given directly.</summary>
    public const string Sidecar = "sidecar:";

    /// <summary>
    /// The setup's command line: no window, no message boxes, no reboot, and <c>/RESTARTAPP=1</c>, which makes
    /// packaging/nazar.iss start the app again (in the tray) after a silent install.
    /// </summary>
    public const string SetupArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTAPP=1";

    static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

    /// <summary>The SHA-256 of a file in lower-case hex.</summary>
    public static string Sha256Of(string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The expected checksum: the digest itself, or the first word of the sidecar file ("&lt;64 hex&gt;  &lt;name&gt;", as
    /// build.ps1 writes it; a name, if given, must be <paramref name="fileName"/>). Null if there is none or it is no
    /// SHA-256. A sidecar outside this repository's releases is never fetched (null).
    /// </summary>
    internal static string? ExpectedSha(string? sha256, Func<string, string> download, string? fileName = null)
    {
        if (sha256 == null) return null;
        if (!sha256.StartsWith(Sidecar, StringComparison.Ordinal)) return IsSha256(sha256) ? sha256.ToLowerInvariant() : null;
        string url = sha256.Substring(Sidecar.Length);
        if (!UpdateCheck.IsTrusted(url)) return null;
        var words = download(url).Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        string first = words.FirstOrDefault() ?? "";
        if (!IsSha256(first)) return null;
        if (fileName != null && words.Length > 1 && !string.Equals(words[1].TrimStart('*'), fileName, StringComparison.OrdinalIgnoreCase)) return null;
        return first.ToLowerInvariant();
    }

    internal static string? ExpectedSha(Release release, Func<string, string> download) => ExpectedSha(release.Sha256, download);

    static bool IsSha256(string text) => text.Length == 64 && text.All(Uri.IsHexDigit);

    /// <summary>Downloads and checks the setup (or, without one, the ZIP); what to start, or the problem.</summary>
    public static (PreparedUpdate? Update, UpdateProblem Problem) Prepare(Release release, string workRoot, Action<string, string> downloadFile, Func<string, string> downloadText) =>
        release.Setup != null ? PrepareSetup(release, workRoot, downloadFile, downloadText) : PrepareZip(release, workRoot, downloadFile, downloadText);

    static (PreparedUpdate?, UpdateProblem) PrepareSetup(Release release, string workRoot, Action<string, string> downloadFile, Func<string, string> downloadText)
    {
        string url = release.Setup!;
        string name = url.Substring(url.LastIndexOf('/') + 1);
        if (!UpdateCheck.IsTrusted(url) || !name.StartsWith(UpdateCheck.SetupPrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return (null, UpdateProblem.UntrustedSource);
        var (expected, problem) = Expected(release.SetupSha256, downloadText, name);
        if (expected == null) return (null, problem);
        string setup = Path.Combine(workRoot, name);
        problem = Download(url, setup, expected, downloadFile);
        // The setup's log lands next to it, for "why did the update not happen".
        string arguments = SetupArguments + " /LOG=\"" + Path.Combine(workRoot, "update-setup.log") + "\"";
        return problem == UpdateProblem.None ? (new PreparedUpdate(setup, arguments), UpdateProblem.None) : (null, problem);
    }

    static (PreparedUpdate?, UpdateProblem) PrepareZip(Release release, string workRoot, Action<string, string> downloadFile, Func<string, string> downloadText)
    {
        if (release.Download == null) return (null, UpdateProblem.NoDownload);
        if (!UpdateCheck.IsTrusted(release.Download)) return (null, UpdateProblem.UntrustedSource);
        var (expected, problem) = Expected(release.Sha256, downloadText, null);
        if (expected == null) return (null, problem);
        string work = Path.Combine(workRoot, "update-" + release.Version);
        string zip = work + ".zip";
        problem = Download(release.Download, zip, expected, downloadFile);
        if (problem != UpdateProblem.None) return (null, problem);
        try
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);   // an earlier attempt of ours
            ZipFile.ExtractToDirectory(zip, work);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Update: unpacking failed", ex);
            return (null, UpdateProblem.DownloadFailed);
        }
        string? exe = Directory.EnumerateFiles(work, "NazarCapture.exe", SearchOption.AllDirectories).FirstOrDefault();
        return exe == null ? (null, UpdateProblem.InstallerMissing) : (new PreparedUpdate(exe, "--install"), UpdateProblem.None);
    }

    static (string? Expected, UpdateProblem Problem) Expected(string? sha256, Func<string, string> downloadText, string? fileName)
    {
        if (sha256 != null && sha256.StartsWith(Sidecar, StringComparison.Ordinal) && !UpdateCheck.IsTrusted(sha256.Substring(Sidecar.Length)))
            return (null, UpdateProblem.UntrustedSource);
        try
        {
            string? expected = ExpectedSha(sha256, downloadText, fileName);
            return (expected, expected == null ? UpdateProblem.NoChecksum : UpdateProblem.None);
        }
        catch (Exception ex) when (ex is WebException or IOException or NotSupportedException)
        {
            ErrorLog.Write("Update: checksum not readable", ex);
            return (null, UpdateProblem.NoChecksum);
        }
    }

    /// <summary>Downloads <paramref name="url"/> to <paramref name="file"/> and keeps it only if its SHA-256 matches.</summary>
    static UpdateProblem Download(string url, string file, string expected, Action<string, string> downloadFile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (File.Exists(file)) File.Delete(file);   // an earlier attempt of ours
            downloadFile(url, file);
            if (string.Equals(Sha256Of(file), expected, StringComparison.OrdinalIgnoreCase)) return UpdateProblem.None;
        }
        catch (Exception ex) when (ex is WebException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorLog.Write("Update: download failed", ex);
            TryDelete(file);
            return UpdateProblem.DownloadFailed;
        }
        TryDelete(file);   // our own download, never kept when it does not match
        return UpdateProblem.ChecksumMismatch;
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write("Update: could not remove a failed download", ex); }
    }

    /// <summary>Where downloads are kept until they are installed.</summary>
    public static string WorkRoot => Path.Combine(Path.GetTempPath(), CaptureSettings.AppFolderName);

    public static void DownloadFile(string url, string file)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var client = new WebClient();
        client.Headers[HttpRequestHeader.UserAgent] = "NazarCapture-Update";
        client.DownloadFile(url, file);
    }

    public static string DownloadText(string url)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var client = new WebClient();
        client.Headers[HttpRequestHeader.UserAgent] = "NazarCapture-Update";
        return client.DownloadString(url);
    }
}

/// <summary>
/// "Jetzt aktualisieren", shared by the update card and the settings row: never while a capture runs (the setup would
/// end the app, so it is not even started then), download and check off the UI thread, then start the checked program.
/// </summary>
internal sealed class UpdateInstallation
{
    /// <summary>Download and check (tests replace it).</summary>
    public Func<Release, (PreparedUpdate? Update, UpdateProblem Problem)> Prepare { get; set; } =
        release => UpdateInstaller.Prepare(release, UpdateInstaller.WorkRoot, UpdateInstaller.DownloadFile, UpdateInstaller.DownloadText);

    /// <summary>Starts the checked program (tests record it instead).</summary>
    public Func<PreparedUpdate, bool> Start { get; set; } = update => Shell.Open(update.Program, update.Arguments);

    /// <summary>Whether a recording or another capture runs now.</summary>
    public Func<bool> Busy { get; set; } = () => EngineActivity.Busy;

    /// <summary>Downloads, checks and starts the update; <see cref="UpdateProblem.None"/> once the installer runs.</summary>
    public async Task<UpdateProblem> InstallAsync(Release release)
    {
        if (Busy()) return UpdateProblem.Busy;
        var (update, problem) = await Task.Run(() => Prepare(release));
        if (problem != UpdateProblem.None) return problem;
        if (update == null) return UpdateProblem.InstallerMissing;
        if (Busy()) return UpdateProblem.Busy;   // a recording started while the download ran
        return Start(update) ? UpdateProblem.None : UpdateProblem.StartFailed;
    }
}