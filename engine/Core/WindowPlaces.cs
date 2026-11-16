namespace NazarCapture.Engine.Core;

/// <summary>
/// Where the engine's own windows were last (Hams keeps the main window's position and the settings window's size in
/// its config): a small text file of the engine next to settings.ini, one line per entry ("main=x,y",
/// "settings=width,height", logical pixels). The host stays the only writer of settings.ini; this file is the
/// engine's, like update-state.json. A missing or broken file means the defaults.
/// </summary>
internal sealed class WindowPlaces
{
    public const string FileName = "windows.txt";

    readonly string path;

    public WindowPlaces(string path) => this.path = path;

    /// <summary>The user's file in %LOCALAPPDATA%\NazarCapture (or its stand-in under test isolation).</summary>
    public static WindowPlaces User() => new(Path.Combine(CaptureSettings.Home, FileName));

    /// <summary>The two numbers stored under <paramref name="key"/>, or null.</summary>
    public (double A, double B)? Read(string key)
    {
        try
        {
            if (!File.Exists(path)) return null;
            foreach (string line in File.ReadAllLines(path))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0 || line.Substring(0, equals).Trim() != key) continue;
                var parts = line.Substring(equals + 1).Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b)
                    && !double.IsNaN(a) && !double.IsNaN(b) && !double.IsInfinity(a) && !double.IsInfinity(b))
                    return (a, b);
            }
        }
        catch (IOException ex) { ErrorLog.Write("Reading the window places", ex); }
        catch (UnauthorizedAccessException ex) { ErrorLog.Write("Reading the window places", ex); }
        return null;
    }

    /// <summary>Stores two numbers under <paramref name="key"/>, keeping the other entries.</summary>
    public void Write(string key, double a, double b)
    {
        try
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).Where(l => l.IndexOf('=') > 0 && l.Substring(0, l.IndexOf('=')).Trim() != key).ToList() : new List<string>();
            lines.Add(key + "=" + Math.Round(a).ToString(CultureInfo.InvariantCulture) + "," + Math.Round(b).ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllLines(temporary, lines);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (IOException ex) { ErrorLog.Write("Saving the window places", ex); }
        catch (UnauthorizedAccessException ex) { ErrorLog.Write("Saving the window places", ex); }
    }
}
