namespace NazarCapture.Engine.NazarUi;

internal static partial class KitSamples
{
    /// <summary>
    /// The sample page of phase 4: every component in its states (buttons, tool buttons, switch, select, key caps,
    /// hotkey field, setting group with all row forms, chips, notice, status dots, menu, card normal and failed).
    /// </summary>
    public static FrameworkElement Components()
    {
        var sheet = Sheet();
        var page = new StackPanel { Margin = new Thickness(UiTokens.Space.Window), Width = 760 };
        sheet.Child = page;

        page.Children.Add(Heading("Knöpfe"));
        var buttons = Line();
        buttons.Children.Add(new PrimaryButton("Screenshot erstellen", icon: IconGlyphs.Edit));
        buttons.Children.Add(new SecondaryButton("Ordner öffnen", icon: IconGlyphs.Folder));
        buttons.Children.Add(new SecondaryButton("Ändern"));
        buttons.Children.Add(new SecondaryButton("Aufnahme starten", role: MuiRole.Recording, icon: IconGlyphs.Video));
        buttons.Children.Add(new SecondaryButton("Text erkennen", role: MuiRole.Ai, icon: IconGlyphs.TextRecognition));
        page.Children.Add(buttons);
        var states = Line();
        states.Children.Add(Focused(new SecondaryButton("Fokus")));
        states.Children.Add(Focused(new PrimaryButton("Fokus")));
        states.Children.Add(new SecondaryButton("Aus") { IsEnabled = false });
        states.Children.Add(new PrimaryButton("Aus") { IsEnabled = false });
        var failed = new SecondaryButton("Fehler");
        Mui.SetIsError(failed, true);
        states.Children.Add(failed);
        states.Children.Add(new IconButton(IconGlyphs.ChromeClose, "Schließen"));
        states.Children.Add(Focused(new IconButton(IconGlyphs.Settings, "Einstellungen")));
        page.Children.Add(states);

        var tools = Line();
        var pen = new ToolButton(IconGlyphs.Pen, "Stift") { IsChecked = true };
        tools.Children.Add(pen);
        tools.Children.Add(new ToolButton(IconGlyphs.Highlight, "Marker"));
        tools.Children.Add(Focused(new ToolButton(IconGlyphs.Text, "Text")));
        tools.Children.Add(new KitSeparator(Orientation.Vertical));
        tools.Children.Add(new ToolButton(IconGlyphs.Undo, "Rückgängig") { IsEnabled = false });
        tools.Children.Add(new KitSeparator(Orientation.Vertical));
        foreach (var (glyph, text) in new[] { (IconGlyphs.Edit, "Bearbeiten"), (IconGlyphs.Pin, "Anpinnen"), (IconGlyphs.Folder, "Ordner") })
            tools.Children.Add(new ActionButton(glyph, text) { Width = 64 });
        var off = new ActionButton(IconGlyphs.Delete, "Löschen") { Width = 64 };
        off.SetAvailable(false, "Noch nicht gespeichert");
        tools.Children.Add(off);
        page.Children.Add(tools);

        page.Children.Add(Heading("Auswahl und Tasten"));
        var controls = Line();
        controls.Children.Add(new ToggleSwitch("An", true));
        controls.Children.Add(new ToggleSwitch("Aus", false));
        controls.Children.Add(Focused(new ToggleSwitch("Fokus", true)));
        controls.Children.Add(new ToggleSwitch("Gesperrt", true) { IsEnabled = false });
        var select = new Select("Format");
        select.Add("PNG (verlustfrei)", 0);
        select.Add("JPEG", 1);
        select.SelectedIndex = 0;
        controls.Children.Add(select);
        controls.Children.Add(new KeyCombo("Shift+Alt+4"));
        controls.Children.Add(new KeyCombo("Ctrl+Print"));
        page.Children.Add(controls);

        page.Children.Add(Heading("Einstellungen"));
        var group = new SettingGroup("Screenshot");
        var checker = new StructuralHotkeyChecker(() => new[] { ("die Aufnahme", "Shift+Alt+5") });
        group.Add(new SettingRow("Kurzbefehl", "Startet die Auswahl auf allen Bildschirmen.", new HotkeyBox("Kurzbefehl Screenshot", "Shift+Alt+4", "Shift+Alt+4", checker)));
        group.Add(new SettingRow("In die Zwischenablage kopieren", null, new ToggleSwitch("In die Zwischenablage kopieren", true)));
        var folder = new SettingRow("Speicherort", @"C:\Users\Beispiel\Pictures\Screenshots", new SecondaryButton("Ändern"));
        group.Add(folder);
        var taken = new SettingRow("Kurzbefehl Aufnahme", null, new KeyCombo("Shift+Alt+5"));
        taken.ShowStatus(UiText.Hotkeys.Taken, Status.Error);
        group.Add(taken);
        group.Add(SettingRow.Display("Version", "1.1.3"));
        page.Children.Add(group);

        page.Children.Add(Heading("Hinweise und Karten"));
        var notes = Line();
        notes.Children.Add(new Chip("1920 × 1080", ChipKind.OnImage));
        notes.Children.Add(new Chip("PNG"));
        notes.Children.Add(new Chip("3", ChipKind.Accent));
        foreach (var status in new[] { Status.Idle, Status.Ready, Status.Processing, Status.Warning, Status.Error, Status.Recording })
            notes.Children.Add(new StatusDot(status));
        page.Children.Add(notes);
        var notice = new Notice("Version 1.1.4 ist da.", Status.Processing, new PrimaryButton("Jetzt aktualisieren"));
        notice.Margin = new Thickness(0, 0, 0, UiTokens.Space.Lg);
        page.Children.Add(notice);

        var cards = new WrapPanel();
        cards.Children.Add(SampleCard(false));
        cards.Children.Add(SampleCard(true));
        var menu = KitMenu.Preview(("Kopieren", IconGlyphs.Copy, "Strg+C"), ("Speichern unter …", IconGlyphs.Save, "Strg+S"), ("", null, null), ("Schließen", IconGlyphs.ChromeClose, "Esc"));
        menu.VerticalAlignment = VerticalAlignment.Top;
        menu.Margin = new Thickness(Elevation.Margin);
        cards.Children.Add(menu);
        page.Children.Add(cards);
        return sheet;
    }

    static Card SampleCard(bool error)
    {
        var card = new Card { Width = 260 };
        var preview = new Border { Height = 70 };
        preview.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.ControlSurface);
        card.Body.Children.Add(preview);
        card.Body.Children.Add(card.Header);
        if (error) card.ShowError("Nicht gespeichert", "Der Ordner ist schreibgeschützt.");
        else card.ShowNormal(IconGlyphs.Accept, "Gespeichert", "Screenshot 2026-11-16 10.42.png");
        var actions = Card.ActionRow();
        actions.Children.Add(new ActionButton(IconGlyphs.Edit, "Bearbeiten"));
        actions.Children.Add(new ActionButton(IconGlyphs.Folder, "Ordner"));
        card.Body.Children.Add(actions);
        var bar = new ProgressLine { Margin = new Thickness(UiTokens.Space.Xs, UiTokens.Space.Lg, UiTokens.Space.Xs, 0) };
        bar.Scale.ScaleX = 0.6;
        card.Body.Children.Add(bar);
        return card;
    }

    static T Focused<T>(T element) where T : Control
    {
        Mui.SetShowFocus(element, true);
        return element;
    }

    static TextBlock Heading(string text)
    {
        var heading = TextStyle.Text(text, TextRole.Section);
        heading.Margin = new Thickness(0, UiTokens.Space.Lg, 0, UiTokens.Space.Md);
        return heading;
    }

    static SampleLine Line() => new() { Margin = new Thickness(0, 0, 0, UiTokens.Space.Lg) };

    /// <summary>A wrapping row of samples, each with the control gap after it.</summary>
    sealed class SampleLine : WrapPanel
    {
        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            if (visualAdded is FrameworkElement child && child.Margin == default)
                child.Margin = new Thickness(0, 0, UiTokens.Space.ControlGap, UiTokens.Space.Sm);
        }
    }

    /// <summary><c>--kit-demo</c>: the sample pages in a window (light or dark as set), for looking at the kit.</summary>
    public static void ShowDemo()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var pages = new StackPanel();
        pages.Children.Add(Surfaces());
        pages.Children.Add(Components());
        var window = new Window
        {
            Title = UiText.Titles.KitDemo,
            Width = 840,
            Height = 900,
            Content = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        Palette.Apply(window.Resources);
        window.SetResourceReference(Control.BackgroundProperty, UiTokens.Keys.WindowBg);
        app.Run(window);
    }
}
