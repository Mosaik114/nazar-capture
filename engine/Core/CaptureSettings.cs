using System.ComponentModel;
namespace MosaikCapture.Engine.Core;

/// <summary>The settings as they were when a flow (a screenshot, a recording) started; immutable.</summary>
internal sealed class CaptureSettingsSnapshot
{
    public string ScreenshotFolder { get; init; } = "";
    public string RecordingFolder { get; init; } = "";
    public bool Microphone { get; init; }
    public bool SystemAudio { get; init; } = true;
    public bool Clicks { get; init; } = true;
    public int Fps { get; init; } = 60;
    public int Countdown { get; init; } = 3;
    /// <summary>Light, dark or like Windows (the windows follow it from the next time they open).</summary>
    public Appearance Appearance { get; init; } = Appearance.System;

    public AudioSettings Audio => new(Microphone, SystemAudio, Clicks);
}

/// <summary>
/// Settings shared with the host: a Unicode INI file, section [capture], written by the host. Each flow
/// reads it once (<see cref="Load()"/>) and works with that snapshot. <c>MOSAIK_CAPTURE_TEST_CONFIG</c>
/// points to a different file; an isolated host (see <see cref="TestIsolation"/>) sets it, and it is read once
/// at startup. The keys, defaults and normalisation follow contract/contract.json, as on the host.
/// </summary>
internal static class CaptureSettings
{
    internal const string TestConfigVariable = "MOSAIK_CAPTURE_TEST_CONFIG";
    internal static readonly string HostWindowClass = TestIsolation.Isolate("MosaikCapture.Host");
    internal const int SaveAudioMessage = 0x8014;
    internal const string AppFolderName = "MosaikCapture";

    /// <summary>%LOCALAPPDATA%, or its stand-in under test isolation.</summary>
    static readonly string LocalAppData = TestIsolation.LocalAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%LOCALAPPDATA%\MosaikCapture: settings and temporary recording files.</summary>
    public static readonly string Home = Path.Combine(LocalAppData, AppFolderName);

    /// <summary>Work folders of running recordings (video segments and audio tracks).</summary>
    public static readonly string RecordingWorkRoot = Path.Combine(Home, "recording");

    /// <summary>Where versions before 0.2 kept their work folders; cleaned up once.</summary>
    public static readonly string LegacyRecordingWorkRoot = Path.Combine(LocalAppData, LegacyHomeName, LegacyWorkFolderName);

    // Folder names of older versions (which used German names); only read to clean up their leftovers.
    const string LegacyHomeName = "Mosaiks-Aufnahme", LegacyWorkFolderName = "Aufnahme";

    static volatile string fileName = ResolveFileName(Environment.GetEnvironmentVariable(TestConfigVariable));

    /// <summary>The INI file, decided once at startup.</summary>
    public static string FileName => fileName;

    /// <summary>The override path if set, otherwise %LOCALAPPDATA%\MosaikCapture\settings.ini.</summary>
    internal static string ResolveFileName(string? overridePath) =>
        string.IsNullOrWhiteSpace(overridePath) ? Path.Combine(Home, "settings.ini") : overridePath!;

    /// <summary>Points the engine at another settings file (self-test, live test, unit tests).</summary>
    internal static void UseFile(string path) => fileName = path;

    static int appearance;

    /// <summary>
    /// The appearance as of the latest <see cref="Load()"/> (every capture and recording reads the settings once),
    /// so opening a window costs no file access; like Windows until the settings were read.
    /// </summary>
    public static Appearance Appearance => (Appearance)Volatile.Read(ref appearance);

    /// <summary>Reads all settings at once from <see cref="FileName"/>.</summary>
    public static CaptureSettingsSnapshot Load()
    {
        var snapshot = Load(FileName);
        Volatile.Write(ref appearance, (int)snapshot.Appearance);
        return snapshot;
    }

    internal static CaptureSettingsSnapshot Load(string file) => new()
    {
        ScreenshotFolder = Read(file, "screenshot_folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), UiText.Files.ScreenshotFolder)),
        RecordingFolder = Read(file, "recording_folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), UiText.Files.RecordingFolder)),
        Microphone = Number(file, "microphone", 0) != 0,
        SystemAudio = Number(file, "system_audio", 1) != 0,
        Clicks = Number(file, "show_clicks", 1) != 0,
        Fps = NormalizeFps(Number(file, "fps", 60)),
        Countdown = NormalizeCountdown(Number(file, "countdown", 3)),
        Appearance = NormalizeAppearance(Number(file, "appearance", 0)),
    };

    /// <summary>0 = like Windows, 1 = light, 2 = dark; anything else means like Windows.</summary>
    internal static Appearance NormalizeAppearance(int value) => value is 1 or 2 ? (Appearance)value : Appearance.System;

    /// <summary>Only 30 and 60 fps are supported; anything else means 60.</summary>
    internal static int NormalizeFps(int fps) => fps == 30 ? 30 : 60;

    /// <summary>Only 0, 3 and 5 seconds are supported; anything else means 3.</summary>
    internal static int NormalizeCountdown(int seconds) => seconds is 0 or 5 ? seconds : 3;

    /// <summary>
    /// The host owns the INI file: audio toggles changed in the toolbar are sent to it as bit flags
    /// (see <see cref="AudioFlags"/>) and it persists them.
    /// </summary>
    public static void SaveAudio(bool microphone, bool sound, bool clicks)
    {
        IntPtr host = NativeMethods.FindWindow(HostWindowClass, null);
        if (host == IntPtr.Zero) return;   // no host (tests, or started by hand): nothing to persist
        if (!NativeMethods.PostMessage(host, SaveAudioMessage, new IntPtr(AudioFlags(microphone, sound, clicks)), IntPtr.Zero))
            ErrorLog.Write("Sending the audio settings to the host", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    /// <summary>1 = microphone, 2 = system sound, 4 = show clicks.</summary>
    internal static int AudioFlags(bool microphone, bool sound, bool clicks) => (microphone ? 1 : 0) | (sound ? 2 : 0) | (clicks ? 4 : 0);

    /// <summary>Longest value read (UTF-16 units), as on the host side; anything longer is cut off.</summary>
    internal const int MaxValueLength = 64 * 1024;

    // The host allows long paths, so the buffer grows until the value fits (a cut value comes back as size - 1).
    static string Read(string file, string key, string fallback)
    {
        for (int size = 2048; ; size *= 2)
        {
            var value = new StringBuilder(size);
            uint length = NativeMethods.GetPrivateProfileString("capture", key, fallback, value, size, file);
            if (length < size - 1 || size >= MaxValueLength) return value.ToString();
        }
    }

    static int Number(string file, string key, int fallback) =>
        int.TryParse(Read(file, key, fallback.ToString(CultureInfo.InvariantCulture)), out int value) ? value : fallback;
}
