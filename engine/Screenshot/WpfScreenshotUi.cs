namespace NazarCapture.Engine.Screenshot;

/// <summary>
/// The real screenshot windows. Each window is created on first use, and anew after the interface language changed
/// (<see cref="PerLanguage{T}"/>).
/// </summary>
internal sealed class WpfScreenshotUi : IScreenshotUi
{
    IScreenshotFlow? flow;
    readonly PerLanguage<SelectionOverlay> overlays;
    readonly PerLanguage<ScreenshotCard> cards;
    readonly PerLanguage<ScreenshotEditor> editors;

    IScreenshotFlow Flow => flow ?? throw new InvalidOperationException("The screenshot windows are not bound to a flow.");
    ScreenshotCard Card => cards.Value;
    ScreenshotEditor Editor => editors.Value;

    public WpfScreenshotUi()
    {
        overlays = new(() => new SelectionOverlay(c => Flow.Captured(c)));
        cards = new(() => new ScreenshotCard(() => Flow.Edit(), () => Flow.Pin(), () => Flow.CardClosed()));
        editors = new(() => new ScreenshotEditor((c, i) => Flow.Edited(c, i), c => Flow.EditCancelled(c), (c, i) => Flow.EditedAndPinned(c, i)));
        Current = this;
    }

    /// <summary>The screenshot windows of this engine (the warm-up prepares them).</summary>
    internal static WpfScreenshotUi? Current { get; private set; }

    /// <summary>Creates the overlay and the card and renders them once, invisibly, ahead of the first hotkey.</summary>
    internal void Prepare()
    {
        overlays.Value.Prepare();
        _ = Card;
    }

    public void Bind(IScreenshotFlow screenshotFlow)
    {
        flow = screenshotFlow;
        var dispatcher = Dispatcher.CurrentDispatcher;
        ClipboardWriter.LateSuccess += what =>
        {
            if (what == "image") dispatcher.BeginInvoke(new Action(() => cards.Existing?.CopiedAfterAll()));
        };
    }

    public bool EditorOpen => editors.Existing is { IsVisible: true };

    public void OpenOverlay()
    {
        overlays.Value.Open();
    }

    public void OpenEditor(ScreenCapture capture) => Editor.Open(capture);
    public void ShowCard(ScreenCapture capture, string heading) => Card.Present(capture, heading);
    public void CardSaved() => Card.Saved();
    public void CardSavedNotCopied() => Card.SavedNotCopied();
    public void CardFailed() => Card.Failed();
    public void CardSavedInFallback(bool copied) => Card.SavedInFallback(copied);
    public void HideCard() => Card.HideNow();

    public void PinInPlace(ScreenCapture capture) =>
        PinnedScreenshot.Open(capture.Image, capture.VirtualLeft + capture.Crop.X, capture.VirtualTop + capture.Crop.Y, capture.Path);

    public void CloseAll()
    {
        overlays.Existing?.Cancel();
        editors.Existing?.Abandon();
        cards.Existing?.HideNow();
    }

    public void ShowProblem(string heading, string detail) => Card.ShowProblem(heading, detail, NativeMethods.WorkAreaAtCursor());
}

/// <summary>Screenshot files below the configured folder and the clipboard.</summary>
internal sealed class ScreenshotStore : IScreenshotStore
{
    public string NewPath(DateTime when) => Screenshots.NewPath(Screenshots.TargetFolder(), when);

    public Task<byte[]> SaveAsync(BitmapSource image, string path) => Task.Run(() =>
    {
        byte[] png = Screenshots.Encode(image, false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, png);
        return png;
    });

    public string FallbackPath(string path) =>
        UniqueFile.Next(Path.Combine(FileKeeping.WritableOrFallback(Path.GetDirectoryName(path)!), Path.GetFileName(path)));

    public Task<byte[]> EncodeAsync(BitmapSource image) => Task.Run(() => Screenshots.Encode(image, false));

    public bool IsInFallback(string path) => FileKeeping.IsInFallback(path);

    public Task<bool> CopyAsync(BitmapSource image, byte[] png) => ClipboardWriter.SetImageAsync(image, png);
}
