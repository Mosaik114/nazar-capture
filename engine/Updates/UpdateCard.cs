namespace NazarCapture.Engine.Updates;

/// <summary>
/// The calm update notice (a card in the bottom right, never a pop-up in the middle of work): with "Jetzt
/// aktualisieren", "Was ist neu" and "Später". Updating downloads the setup, checks its SHA-256 and only then starts it
/// silently (<see cref="UpdateInstallation"/>); the click is the user's confirmation. While a recording runs nothing is
/// started, and the card says so.
/// </summary>
internal sealed class UpdateCard : CardWindow
{
    readonly UpdateCheck check;
    readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, UiTokens.Space.Lg, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
    readonly PrimaryButton update;
    Release? release;

    /// <summary>Download, check and start (tests replace its parts).</summary>
    internal UpdateInstallation Installation { get; set; } = new();

    public UpdateCard(UpdateCheck check) : base(UiText.Update.Title)
    {
        this.check = check;
        update = new PrimaryButton(UiText.Update.Now, () => Install().LogFailures("Installing an update"), IconGlyphs.Refresh);
        actions.Children.Add(new SecondaryButton(UiText.Update.Later, () => { if (release != null) check.Dismiss(release.Version); Dismiss(); }) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) });
        actions.Children.Add(new SecondaryButton(UiText.Update.WhatsNew, () => Shell.Open(release?.Page ?? UpdateCheck.ReleasesPage)) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) });
        actions.Children.Add(update);
        card.Width = double.NaN;
        card.MinWidth = 380;
        card.Body.Children.Add(card.Header);
        card.Body.Children.Add(actions);
        card.Body.Children.Add(bar);
        bar.Visibility = Visibility.Collapsed;
        CreateHandle(NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    internal Release? Shown => release;

    public void Present(Release newer, RECT workArea)
    {
        release = newer;
        Palette.Apply(Resources);
        ShowNormal(IconGlyphs.Refresh, UiText.Update.Available(newer.Version), UiText.Update.Running(check.Current));
        actions.Visibility = Visibility.Visible;
        update.IsEnabled = true;
        bar.Visibility = Visibility.Collapsed;
        SlideIn(workArea);
    }

    protected override void Dismiss() => FadeCard(Hide);

    /// <summary>Download, check and start the installer; the card shows where it is and says why if it cannot.</summary>
    internal async Task Install()
    {
        if (release == null) return;
        var target = release;
        update.IsEnabled = false;
        ShowNormal(IconGlyphs.Refresh, UiText.Update.Downloading, UiText.Update.Available(target.Version));
        bar.Visibility = Visibility.Visible;
        StopBar(0.3);
        var problem = await Installation.InstallAsync(target);
        bar.Visibility = Visibility.Collapsed;
        if (problem == UpdateProblem.None)
        {
            ShowNormal(IconGlyphs.Accept, UiText.Update.Installing, UiText.Update.Available(target.Version));
            actions.Visibility = Visibility.Collapsed;
            return;
        }
        ShowError(UiText.Update.Failed, UiText.Update.Reason(problem));
        update.IsEnabled = true;
    }
}

/// <summary>Runs the daily check in the background and shows the card for a new version.</summary>
internal static class UpdateNotice
{
    static UpdateCard? card;

    /// <summary>
    /// Checks (at most once a day, only if "Über Updates informieren" is on) and only remembers the result: a new version
    /// shows as a calm row in the settings ("Update verfügbar"), as in Hams. Nothing pops up after logon; the card opens
    /// only from that row.
    /// </summary>
    public static void CheckSoon()
    {
        if (!CaptureSettings.Load().CheckUpdates) return;
        var check = new UpdateCheck();
        Task.Run(check.Run).LogFailures("Checking for updates");
    }

    /// <summary>Shows the card for <paramref name="release"/> (from the settings window).</summary>
    public static void Show(Release release)
    {
        card ??= new UpdateCard(new UpdateCheck());
        card.Present(release, NativeMethods.WorkAreaAtCursor());
    }

    /// <summary>The pending release (newer and not dismissed) as remembered, without asking the network.</summary>
    public static Release? Pending() => new UpdateCheck().Load().Pending(Settings.AppInfo.Version);
}
