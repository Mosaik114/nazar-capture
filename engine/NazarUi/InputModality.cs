namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// What was used last, keyboard or pointer, for all windows of the engine (Hams' InputModality in states.py).
/// The focus edge is only for keyboard use, like <c>:focus-visible</c> in a browser: whoever clicked already knows
/// where they are. Every key switches to keyboard, every mouse or pen press to pointer. When an element gets the
/// keyboard focus it shows the edge (<see cref="Mui.ShowFocusProperty"/>) only after keyboard use, also when the
/// focus comes back because its window is activated again. A click takes the edge away from the focused element.
/// FocusVisualStyle stays null everywhere: this edge replaces WPF's dotted rectangle.
/// </summary>
internal static class InputModality
{
    [ThreadStatic] static bool installed;
    static bool classHandlers;
    static readonly object gate = new();

    /// <summary>True after a key, false after a pointer press. Starts true, as in Hams (the first Tab shows the edge).</summary>
    public static bool Keyboard { get; private set; } = true;

    /// <summary>Hooks the input of the calling (UI) thread once and the focus events of all elements once per process.</summary>
    public static void Install()
    {
        lock (gate)
        {
            if (!classHandlers)
            {
                classHandlers = true;
                EventManager.RegisterClassHandler(typeof(UIElement), System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
                    new KeyboardFocusChangedEventHandler((sender, e) => { if (ReferenceEquals(e.NewFocus, sender)) OnFocusIn(sender as DependencyObject); }), true);
                EventManager.RegisterClassHandler(typeof(UIElement), System.Windows.Input.Keyboard.LostKeyboardFocusEvent,
                    new KeyboardFocusChangedEventHandler((sender, e) => { if (ReferenceEquals(e.OldFocus, sender)) OnFocusOut(sender as DependencyObject); }), true);
            }
        }
        if (installed) return;
        installed = true;
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            switch (e.StagingItem.Input)
            {
                case KeyEventArgs { RoutedEvent: var routed } when routed == System.Windows.Input.Keyboard.PreviewKeyDownEvent:
                    NoteKey();
                    break;
                case MouseButtonEventArgs { RoutedEvent: var routed } when routed == Mouse.PreviewMouseDownEvent:
                    NotePointer();
                    break;
                case StylusDownEventArgs:
                    NotePointer();
                    break;
            }
        };
    }

    /// <summary>A key was pressed.</summary>
    internal static void NoteKey() => Keyboard = true;

    /// <summary>A pointer was pressed: the focused element loses its visible edge (it keeps the focus itself).</summary>
    internal static void NotePointer()
    {
        Keyboard = false;
        if (System.Windows.Input.Keyboard.FocusedElement is DependencyObject focused) Mui.SetShowFocus(focused, false);
    }

    internal static void OnFocusIn(DependencyObject? element)
    {
        if (element != null && Keyboard) Mui.SetShowFocus(element, true);
    }

    internal static void OnFocusOut(DependencyObject? element)
    {
        if (element != null && Mui.GetShowFocus(element)) Mui.SetShowFocus(element, false);
    }

    /// <summary>Tests only: start from a known modality.</summary>
    internal static void Reset(bool keyboard) => Keyboard = keyboard;
}
