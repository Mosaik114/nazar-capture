namespace MosaikCapture.Engine.MosaikUi;

/// <summary>
/// The control templates of the kit, built once per UI thread: a <see cref="RoleChrome"/> around the content. The
/// chrome's radius, depth and padding come from the control (<see cref="Mui.RadiusProperty"/>,
/// <see cref="Mui.DepthProperty"/>, Padding), its colours from <see cref="Roles"/>.
/// </summary>
internal static class KitTemplates
{
    [ThreadStatic] static ControlTemplate? stone;

    /// <summary>Template of every kit button and toggle.</summary>
    public static ControlTemplate Stone => stone ??= BuildStone();

    static ControlTemplate BuildStone()
    {
        var chrome = new FrameworkElementFactory(typeof(RoleChrome), "Chrome");
        chrome.SetValue(StoneChrome.RadiusProperty, new TemplateBindingExtension(Mui.RadiusProperty));
        chrome.SetValue(StoneChrome.DepthProperty, new TemplateBindingExtension(Mui.DepthProperty));
        chrome.SetValue(StoneChrome.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        chrome.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        content.SetValue(UIElement.FocusableProperty, false);
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        chrome.AppendChild(content);
        var template = new ControlTemplate(typeof(Control)) { VisualTree = chrome };
        template.Seal();
        return template;
    }
}
