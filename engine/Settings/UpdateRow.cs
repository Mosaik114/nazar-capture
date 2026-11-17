using NazarCapture.Engine.Updates;

namespace NazarCapture.Engine.Settings;

/// <summary>
/// The update row of the settings (Allgemein), below the switch "Über Updates informieren": "Jetzt nach Updates suchen"
/// asks GitHub at once and says the result in the row (up to date, a new version, or that the search failed). While a
/// newer version is known (found now or by the daily check) the row offers "Jetzt aktualisieren" (download, check the
/// SHA-256, install silently, the app starts again), "Ansehen" (the update card with "Was ist neu") and a cross that hides
/// the notice for this version.
/// </summary>
internal sealed class UpdateRow
{
    readonly SettingRow row;
    readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly SecondaryButton search, install, view;
    readonly IconButton dismiss;
    readonly Action<Release> showCard;
    Release? pending;
    bool working;

    public UpdateRow(Func<UpdateCheck> check, Action<Release> showCard)
    {
        NewCheck = check;
        this.showCard = showCard;
        search = new SecondaryButton(UiText.Settings.SearchUpdates, () => SearchNow().LogFailures("Searching for updates"), IconGlyphs.Refresh);
        install = new SecondaryButton(UiText.Update.Now, () => Install().LogFailures("Installing an update from the settings"), IconGlyphs.Refresh)
        {
            Margin = new Thickness(0, 0, UiTokens.Space.Md, 0),
        };
        view = new SecondaryButton(UiText.Settings.View, () => { if (pending != null) this.showCard(pending); }) { MinWidth = SettingsWindow.RowButtonWidth };
        dismiss = IconButton.Stone(LineIcon.Shape.Close, UiText.Settings.UpdateDismiss, Dismiss);
        dismiss.Margin = new Thickness(UiTokens.Space.Xs, 0, 0, 0);
        buttons.Children.Add(search);
        buttons.Children.Add(install);
        buttons.Children.Add(view);
        buttons.Children.Add(dismiss);
        row = new SettingRow(UiText.Settings.UpdatesTitle, Idle, buttons, stacked: true);
        Show(null);
    }

    /// <summary>A fresh check (tests give one with a fake GitHub).</summary>
    internal Func<UpdateCheck> NewCheck { get; set; }

    /// <summary>Download, check and start (tests replace its parts).</summary>
    internal UpdateInstallation Installation { get; set; } = new();

    public SettingRow Row => row;

    internal Release? Pending => pending;

    internal SecondaryButton SearchButton => search;

    internal SecondaryButton InstallButton => install;

    static string Idle => UiText.Settings.UpdatesInstalled(AppInfo.Version);

    /// <summary>The release to offer (null: none), as the daily check or a search remembered it.</summary>
    public void Show(Release? release)
    {
        if (working) return;   // a search or an installation is saying something right now
        pending = release;
        bool found = release != null;
        row.SetTitle(found ? UiText.Settings.UpdateTitle : UiText.Settings.UpdatesTitle);
        row.SetDescription(found ? UiText.Settings.UpdateHint(release!.Version, AppInfo.Version) : Idle);
        search.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
        foreach (var offer in new FrameworkElement[] { install, view, dismiss }) offer.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        search.IsEnabled = install.IsEnabled = true;
    }

    /// <summary>"Jetzt nach Updates suchen": asks now and says what came out.</summary>
    internal async Task SearchNow()
    {
        if (working) return;
        working = true;
        search.IsEnabled = false;
        row.ShowStatus(UiText.Settings.Searching, Status.Processing);
        var check = NewCheck();
        var (outcome, release) = await Task.Run(check.CheckNow);
        working = false;
        search.IsEnabled = true;
        switch (outcome)
        {
            case CheckOutcome.Found:
                Show(release);
                row.ShowStatus(UiText.Settings.UpdateHint(release!.Version, AppInfo.Version), Status.Ready);
                break;
            case CheckOutcome.UpToDate:
                Show(null);
                row.ShowStatus(UiText.Settings.UpToDate(AppInfo.Version), Status.Ready);
                break;
            default:
                row.ShowStatus(UiText.Settings.SearchFailed, Status.Error);
                break;
        }
    }

    /// <summary>"Jetzt aktualisieren" in the row: the same way as on the card, the progress in the row.</summary>
    internal async Task Install()
    {
        if (pending == null || working) return;
        var target = pending;
        working = true;
        install.IsEnabled = false;
        row.ShowStatus(UiText.Update.Downloading, Status.Processing);
        var problem = await Installation.InstallAsync(target);
        working = false;
        if (problem == UpdateProblem.None)
        {
            row.ShowStatus(UiText.Update.Installing, Status.Ready);
            return;   // the setup ends this app and starts the new version
        }
        install.IsEnabled = true;
        row.ShowStatus(UiText.Update.Reason(problem), Status.Error);
    }

    void Dismiss()
    {
        if (pending != null) NewCheck().Dismiss(pending.Version);
        Show(null);
    }
}
