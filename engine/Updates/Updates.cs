using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace NazarCapture.Engine.Updates;

/// <summary>A release on GitHub: version, page, the installer ZIP and its SHA-256.</summary>
internal sealed record Release(string Version, string Page, string? Download, string? Sha256);

/// <summary>Why an update could not be installed (said on the card).</summary>
internal enum UpdateProblem
{
    None,
    NoDownload,
    DownloadFailed,
    ChecksumMismatch,
    NoChecksum,
    InstallerMissing,
    StartFailed,
}

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
    public string Dismissed { get; set; } = "";
    public string Notified { get; set; } = "";

    /// <summary>At most once a day; a clock that went backwards counts as due.</summary>
    public bool Due(double now) => LastCheck <= 0 || now - LastCheck >= UpdateCheck.Interval.TotalSeconds || now < LastCheck;   // 0: never asked

    /// <summary>The release to point out: newer than <paramref name="current"/> and not dismissed.</summary>
    public Release? Pending(string current) =>
        UpdateCheck.IsNewer(Latest, current) && Latest != Dismissed
            ? new Release(Latest, Page, Download.Length > 0 ? Download : null, Sha256.Length > 0 ? Sha256 : null)
            : null;
}

/// <summary>
/// The update check (as Hams' updates.py): once a day, read-only and without an account, it asks GitHub for the latest
/// release. Any problem (offline, rate limit, broken answer) is logged and means "nothing new".
/// </summary>
internal sealed class UpdateCheck
{
    public const string Repository = "Mosaik114/mosaik-capture";
    public const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
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
            if (release != null)
            {
                state.Latest = release.Version;
                state.Page = release.Page;
                state.Download = release.Download ?? "";
                state.Sha256 = release.Sha256 ?? "";
            }
            Save(state);
        }
        return state.Pending(Current);
    }

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

    /// <summary>
    /// Reads the API answer: tag, page (only a page of this repository), the Windows ZIP and its checksum (the asset's
    /// <c>digest</c> "sha256:…", else a sidecar asset "&lt;zip&gt;.sha256" is fetched later).
    /// </summary>
    internal static Release? ParseRelease(string json)
    {
        object? parsed;
        try { parsed = new JavaScriptSerializer().DeserializeObject(json); }
        catch (ArgumentException) { return null; }
        if (parsed is not Dictionary<string, object> data || data.TryGetValue("tag_name", out var tag) is false || tag is not string tagText || Parse(tagText) == null) return null;
        string page = data.TryGetValue("html_url", out var url) && url is string text && text.StartsWith(ReleasesPage + "/", StringComparison.Ordinal) ? text : ReleasesPage;
        string? download = null, sha = null, sidecar = null;
        if (data.TryGetValue("assets", out var assets) && assets is object[] list)
            foreach (var asset in list.OfType<Dictionary<string, object>>())
            {
                string name = asset.TryGetValue("name", out var n) ? n as string ?? "" : "";
                string link = asset.TryGetValue("browser_download_url", out var l) ? l as string ?? "" : "";
                if (!link.StartsWith("https://github.com/" + Repository + "/releases/download/", StringComparison.Ordinal)) continue;
                if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                {
                    download = link;
                    if (asset.TryGetValue("digest", out var d) && d is string digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = digest.Substring(7).ToLowerInvariant();
                }
                else if (name.EndsWith("-win-x64.zip.sha256", StringComparison.OrdinalIgnoreCase)) sidecar = link;
            }
        return new Release(tagText.TrimStart('v', 'V'), page, download, sha ?? (sidecar != null ? "sidecar:" + sidecar : null));
    }
}

/// <summary>
/// Installs a release after the user said "Jetzt aktualisieren": downloads the ZIP, checks its SHA-256 against the
/// release (never installs without a matching checksum), unpacks it and starts its <c>NazarCapture.exe --install</c>,
/// which stops the running app cleanly, copies the files and starts the new version. Settings stay where they are.
/// </summary>
internal static class UpdateInstaller
{
    static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

    /// <summary>The SHA-256 of a file in lower-case hex.</summary>
    public static string Sha256Of(string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>The expected checksum: the digest itself, or the first word of the sidecar file.</summary>
    internal static string? ExpectedSha(Release release, Func<string, string> download)
    {
        if (release.Sha256 == null) return null;
        if (!release.Sha256.StartsWith("sidecar:", StringComparison.Ordinal)) return release.Sha256;
        string text = download(release.Sha256.Substring(8));
        string first = text.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 ? first.ToLowerInvariant() : null;
    }

    /// <summary>Downloads, checks, unpacks; the installer exe to start, or the problem.</summary>
    public static (string? Installer, UpdateProblem Problem) Prepare(Release release, string workRoot, Action<string, string> downloadFile, Func<string, string> downloadText)
    {
        if (release.Download == null) return (null, UpdateProblem.NoDownload);
        string? expected;
        try { expected = ExpectedSha(release, downloadText); }
        catch (Exception ex) when (ex is WebException or IOException) { ErrorLog.Write("Update: checksum not readable", ex); return (null, UpdateProblem.NoChecksum); }
        if (expected == null) return (null, UpdateProblem.NoChecksum);
        string work = Path.Combine(workRoot, "update-" + release.Version);
        string zip = work + ".zip";
        try
        {
            Directory.CreateDirectory(workRoot);
            downloadFile(release.Download, zip);
        }
        catch (Exception ex) when (ex is WebException or IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Update: download failed", ex);
            return (null, UpdateProblem.DownloadFailed);
        }
        if (!string.Equals(Sha256Of(zip), expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zip);   // our own download, never kept when it does not match
            return (null, UpdateProblem.ChecksumMismatch);
        }
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
        return exe == null ? (null, UpdateProblem.InstallerMissing) : (exe, UpdateProblem.None);
    }

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
