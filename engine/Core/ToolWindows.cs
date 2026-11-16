namespace MosaikCapture.Engine.Core;

/// <summary>Helpers of the tool windows that are no part of the look (handles, scale, capture exclusion, images).</summary>
internal static class ToolWindows
{
    /// <summary>Invisible in the video (and in screenshots of other apps), visible on screen as usual.</summary>
    public static void ExcludeFromCapture(IntPtr hwnd) => NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

    /// <summary>Device pixels per WPF unit of the monitor the window is on.</summary>
    public static double Scale(IntPtr hwnd)
    {
        var source = HwndSource.FromHwnd(hwnd);
        return source?.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1;
    }

    public static bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>Loads an image completely into memory, so the file can be deleted right away.</summary>
    public static BitmapSource LoadImage(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(File.ReadAllBytes(file));
        image.EndInit();
        image.Freeze();
        return image;
    }
}

/// <summary>How times and sizes are shown to the user (German number format).</summary>
internal static class Formats
{
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>0:07 or 1:02:07</summary>
    public static string Clock(double seconds)
    {
        int total = (int)Math.Floor(Math.Max(0, seconds));
        string minutesAndSeconds = (total / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        return total >= 3600 ? total / 3600 + ":" + minutesAndSeconds : total / 60 + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>0:07,4 (tenths of a second)</summary>
    public static string ClockWithTenths(double seconds)
    {
        int tenths = (int)Math.Round(Math.Max(0, seconds) * 10);
        return Clock(tenths / 10) + "," + tenths % 10;
    }

    /// <summary>KB below 1 MB, above that MB with a German decimal comma.</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 1024 * 1024) return Math.Max(1, bytes / 1024) + " KB";
        double megabytes = bytes / 1048576.0;
        return megabytes.ToString(megabytes < 100 ? "0.0" : "0", German) + " MB";
    }
}
