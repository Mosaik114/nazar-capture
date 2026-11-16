namespace MosaikCapture.Engine.Recording;

// Trim window: building the controls from the kit (fields and construction in TrimWindow.cs).
internal sealed partial class TrimWindow
{
    // ---------- Building ----------

    static StoneChrome BuildBigPlayButton()
    {
        var glyph = TextStyle.Icon(IconGlyphs.PlaySolid, 22);
        glyph.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.LightOnDark);
        glyph.Margin = new Thickness(4, 0, 0, 0);
        var chip = new StoneChrome { Width = 60, Height = 60, Radius = 30, Depth = 0, IsSquircle = false, Child = glyph, IsHitTestVisible = false };
        chip.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.PlayBackdrop);
        return chip;
    }

    // The video with a poster until it has loaded; click = play/pause.
    StoneChrome BuildVideo()
    {
        media.MediaOpened += delegate { poster.Visibility = Visibility.Hidden; Seek(playhead); };
        media.MediaEnded += delegate { Pause(); Seek(selectionEnd); };
        var layers = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        layers.Children.Add(poster);
        layers.Children.Add(media);
        layers.Children.Add(bigPlayButton);
        layers.MouseLeftButtonUp += delegate { TogglePlay(); };
        var box = new StoneChrome { Margin = new Thickness(0, 14, 0, 0), Radius = 10, Depth = 0, Child = layers };
        box.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.VideoBackdrop);
        return box;
    }

    // Play button, time and length of the selection.
    DockPanel BuildTransport()
    {
        timeText.Margin = new Thickness(12, 0, 0, 0);
        System.Windows.Documents.Typography.SetNumeralAlignment(timeText, FontNumeralAlignment.Tabular);
        System.Windows.Documents.Typography.SetNumeralAlignment(selectionText, FontNumeralAlignment.Tabular);
        var transport = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(selectionText, Dock.Right);
        transport.Children.Add(selectionText);
        transport.Children.Add(playButton);
        transport.Children.Add(timeText);
        return transport;
    }

    // Film strip, dimmed outside the selection, handles and playhead (in this z-order).
    void BuildTimeline()
    {
        var placeholder = new Shapes.Rectangle { Width = StripWidth, Height = StripHeight };
        placeholder.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.ControlSurface);
        AddToTrack(placeholder, HandleWidth, StripTop);
        AddToTrack(strip, HandleWidth, StripTop);
        AddToTrack(dimBefore, null, StripTop);
        AddToTrack(dimAfter, null, StripTop);
        track.Children.Add(selectionTop);
        track.Children.Add(selectionBottom);
        track.Children.Add(startHandle);
        track.Children.Add(endHandle);
        playheadLine.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.Ink);
        AddToTrack(playheadLine, null, -6);
        playheadKnob.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.Ink);
        playheadKnob.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.WindowBg);
        AddToTrack(playheadKnob, null, -12);
        track.MouseLeftButtonDown += (_, e) => TrackDown(e.GetPosition(track).X);
        track.MouseMove += (_, e) => { if (dragging != DragTarget.None) TrackMove(e.GetPosition(track).X); };
        track.MouseLeftButtonUp += delegate { dragging = DragTarget.None; track.ReleaseMouseCapture(); };
    }

    void AddToTrack(UIElement element, double? left, double top)
    {
        if (left.HasValue) Canvas.SetLeft(element, left.Value);
        Canvas.SetTop(element, top);
        track.Children.Add(element);
    }

    // Key hints and the buttons for sound, GIF and save ("Speichern" is the one primary action).
    DockPanel BuildActions()
    {
        var hint = TextStyle.Text(UiText.Trim.KeyHint, TextRole.Description, UiTokens.Keys.TextSecondary);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        var gifButton = new SecondaryButton(UiText.Trim.AsGif, delegate { Export(gif: true); }, IconGlyphs.Gif) { Margin = new Thickness(UiTokens.Space.Md, 0, 0, 0) };
        var saveButton = new PrimaryButton(UiText.Trim.Save, delegate { Export(gif: false); }, IconGlyphs.Save) { Margin = new Thickness(UiTokens.Space.Md, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(soundButton);
        buttons.Children.Add(gifButton);
        buttons.Children.Add(saveButton);
        var row = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(hint);
        return row;
    }

    // Shown instead of the actions while exporting.
    StackPanel BuildProgress()
    {
        progressLine.Margin = new Thickness(0, UiTokens.Space.Md, 0, 0);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        content.Children.Add(progressText);
        content.Children.Add(progressLine);
        return content;
    }

    Grid BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0), MinHeight = 38 };
        footer.Children.Add(actionsRow);
        footer.Children.Add(progressRow);
        return footer;
    }

    /// <summary>The sound button: a secondary stone whose icon shows whether the export keeps the sound.</summary>
    SecondaryButton BuildSoundButton()
    {
        var button = new SecondaryButton(UiText.Trim.Sound, delegate { withSound = !withSound; media.IsMuted = !withSound; Refresh(); });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        soundGlyph.Margin = new Thickness(0, 0, UiTokens.Space.Md, 0);
        row.Children.Add(soundGlyph);
        row.Children.Add(TextStyle.Apply(new TextBlock { Text = UiText.Trim.Sound, VerticalAlignment = VerticalAlignment.Center }, TextRole.Button));
        button.Content = row;
        return button;
    }

    static Shapes.Rectangle SelectionEdge(double top)
    {
        var edge = new Shapes.Rectangle { Height = StripTop, IsHitTestVisible = false };
        edge.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.AccentSurface);   // same colour as the handles
        Canvas.SetTop(edge, top);
        return edge;
    }

    static StoneChrome TrimHandle()
    {
        var grip = new Shapes.Rectangle
        {
            Width = 2,
            Height = 20,
            RadiusX = 1,
            RadiusY = 1,
            Fill = UiTokens.Fixed.TrimGripBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var handle = new StoneChrome { Width = HandleWidth, Height = TrackHeight, Radius = 5, Depth = 0, Child = grip, Cursor = Cursors.SizeWE };
        handle.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.AccentSurface);   // carries the light grip
        return handle;
    }
}
