using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ACCcom.Helpers;

namespace ACCcom.Controls;

/// <summary>
/// Shared chromeless-window title bar. 22 windows used to copy-paste the same
/// 32px Border + min/max/close markup and the same three Click handlers; this
/// control builds that chrome once and lets windows declare only what differs:
///
///   Title        — localized text, e.g. Title="{Binding [Stats.Title], Source={x:Static local:LanguageManager.Instance}}"
///   ShowMinMax   — false for dialog-style windows (close only), default true
///   ShowClose    — false when the window owns a custom close action, default true
///   ExtraButtons — slot rendered left of the window buttons (e.g. the traffic
///                  window's Clear), typically set as a property element
///
/// Drag-to-move and double-click maximize still come from
/// <see cref="WindowHelper.SetupTitleBar"/>, and the window glyphs are Segoe
/// MDL2 Assets (ChromeMinimize/ChromeMaximize/ChromeRestore/ChromeClose) with
/// the maximize glyph tracking the live window state.
/// </summary>
public class ChromeTitleBar : Border
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ChromeTitleBar), new PropertyMetadata(""));

    public static readonly DependencyProperty ShowMinMaxProperty = DependencyProperty.Register(
        nameof(ShowMinMax), typeof(bool), typeof(ChromeTitleBar),
        new PropertyMetadata(true, (d, _) => ((ChromeTitleBar)d).UpdateButtonVisibility()));

    public static readonly DependencyProperty ShowCloseProperty = DependencyProperty.Register(
        nameof(ShowClose), typeof(bool), typeof(ChromeTitleBar),
        new PropertyMetadata(true, (d, _) => ((ChromeTitleBar)d).UpdateButtonVisibility()));

    public static readonly DependencyProperty ExtraButtonsProperty = DependencyProperty.Register(
        nameof(ExtraButtons), typeof(object), typeof(ChromeTitleBar), new PropertyMetadata(null));

    /// <summary>Localized title text shown at the left edge.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Show the minimize/maximize buttons (default). Dialog windows with
    /// a close-only bar set this to false.</summary>
    public bool ShowMinMax
    {
        get => (bool)GetValue(ShowMinMaxProperty);
        set => SetValue(ShowMinMaxProperty, value);
    }

    /// <summary>Show the built-in close button (default). Windows with a custom
    /// close action pass their button through <see cref="ExtraButtons"/> and
    /// set this to false.</summary>
    public bool ShowClose
    {
        get => (bool)GetValue(ShowCloseProperty);
        set => SetValue(ShowCloseProperty, value);
    }

    /// <summary>Optional extra buttons rendered between the title and the
    /// window buttons (keeps per-window title-bar actions without forking the
    /// chrome itself).</summary>
    public object ExtraButtons
    {
        get => GetValue(ExtraButtonsProperty);
        set => SetValue(ExtraButtonsProperty, value);
    }

    public static readonly DependencyProperty CenterContentProperty = DependencyProperty.Register(
        nameof(CenterContent), typeof(object), typeof(ChromeTitleBar), new PropertyMetadata(null));

    /// <summary>Content rendered centered in the bar (the main window's always-
    /// visible connection badge). Fills the space between title and buttons.</summary>
    public object CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public static readonly DependencyProperty ShowAccentDotProperty = DependencyProperty.Register(
        nameof(ShowAccentDot), typeof(bool), typeof(ChromeTitleBar),
        new PropertyMetadata(false, (d, _) => ((ChromeTitleBar)d).UpdateAccentDot()));

    /// <summary>Shows the glowing accent dot before the title (main window
    /// brand mark; secondary windows keep the plain text title).</summary>
    public bool ShowAccentDot
    {
        get => (bool)GetValue(ShowAccentDotProperty);
        set => SetValue(ShowAccentDotProperty, value);
    }

    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");
    private const string MinGlyph = "\uE921";     // ChromeMinimize
    private const string MaxGlyph = "\uE922";     // ChromeMaximize
    private const string RestoreGlyph = "\uE923"; // ChromeRestore
    private const string CloseGlyph = "\uE8BB";   // ChromeClose

    private const string AccentDotXaml =
        "<Ellipse xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
        " Width=\"8\" Height=\"8\" VerticalAlignment=\"Center\" Margin=\"0,0,8,0\">" +
        "<Ellipse.Effect>" +
        "<DropShadowEffect BlurRadius=\"5\" ShadowDepth=\"0\" Color=\"{DynamicResource Accent}\" Opacity=\"0.55\" />" +
        "</Ellipse.Effect>" +
        "<Ellipse.Fill>" +
        "<LinearGradientBrush StartPoint=\"0,0\" EndPoint=\"1,1\">" +
        "<GradientStop Color=\"{DynamicResource Accent}\" Offset=\"0\" />" +
        "<GradientStop Color=\"{DynamicResource AccentHover}\" Offset=\"1\" />" +
        "</LinearGradientBrush>" +
        "</Ellipse.Fill>" +
        "</Ellipse>";

    private readonly Button _minButton;
    private readonly Button _maxButton;
    private readonly Button _closeButton;
    private readonly Ellipse _accentDot;
    private readonly ContentControl _centerHost;
    private Window? _window;
    private bool _chromeAttached;

    public ChromeTitleBar()
    {
        SetResourceReference(BackgroundProperty, "BgSurfaceBrush");

        var title = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush");
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Title)) { Source = this });

        // Glowing accent dot (ShowAccentDot), matching the main window's brand
        // mark. Built from an inline XAML fragment because DynamicResource on
        // Freezables (GradientStop/Effect colors) needs markup deferral —
        // SetResourceReference only exists on FrameworkElement.
        _accentDot = (Ellipse)System.Windows.Markup.XamlReader.Parse(AccentDotXaml);

        var titleGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleGroup.Children.Add(_accentDot);
        titleGroup.Children.Add(title);
        DockPanel.SetDock(titleGroup, Dock.Left);

        var extraHost = new ContentControl { VerticalAlignment = VerticalAlignment.Stretch, Focusable = false };
        extraHost.SetBinding(ContentControl.ContentProperty,
            new System.Windows.Data.Binding(nameof(ExtraButtons)) { Source = this });

        _centerHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            IsHitTestVisible = true
        };
        _centerHost.SetBinding(ContentControl.ContentProperty,
            new System.Windows.Data.Binding(nameof(CenterContent)) { Source = this });

        _minButton = MakeWindowButton(MinGlyph, "TitleBar.Minimize", "TitleBarButton", OnMinimizeClick);
        _maxButton = MakeWindowButton(MaxGlyph, "TitleBar.Maximize", "TitleBarButton", OnMaximizeClick);
        _closeButton = MakeWindowButton(CloseGlyph, "TitleBar.Close", "TitleBarCloseButton", OnCloseClick);
        UpdateButtonVisibility();
        UpdateAccentDot();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        buttons.Children.Add(extraHost);
        buttons.Children.Add(_minButton);
        buttons.Children.Add(_maxButton);
        buttons.Children.Add(_closeButton);

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleGroup, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(titleGroup);
        panel.Children.Add(buttons);
        panel.Children.Add(_centerHost); // last child fills: the centered content
        Child = panel;

        Loaded += (_, _) => AttachChrome();
    }

    private void UpdateAccentDot()
    {
        if (_accentDot != null)
            _accentDot.Visibility = ShowAccentDot ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button MakeWindowButton(string glyph, string tooltipKey, string styleKey, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Width = 46,
            VerticalAlignment = VerticalAlignment.Stretch,
            FontFamily = IconFont,
            FontSize = 10,
            Content = glyph
        };
        button.SetResourceReference(StyleProperty, styleKey);
        button.SetBinding(Button.ToolTipProperty,
            new System.Windows.Data.Binding($"[{tooltipKey}]") { Source = LanguageManager.Instance });
        button.Click += onClick;
        return button;
    }

    private void UpdateButtonVisibility()
    {
        var minMax = ShowMinMax ? Visibility.Visible : Visibility.Collapsed;
        _minButton.Visibility = minMax;
        _maxButton.Visibility = minMax;
        _closeButton.Visibility = ShowClose ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AttachChrome()
    {
        var win = Window.GetWindow(this);
        if (win == null) return;
        _window = win;
        // Loaded can fire again on re-show; SetupTitleBar would stack a second
        // drag handler each time, so attach exactly once.
        if (_chromeAttached) return;
        _chromeAttached = true;
        win.StateChanged += (_, _) => UpdateMaxGlyph();
        WindowHelper.SetupTitleBar(win, this);
        UpdateMaxGlyph();
    }

    private void UpdateMaxGlyph() =>
        _maxButton.Content = _window?.WindowState == WindowState.Maximized ? RestoreGlyph : MaxGlyph;

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowHelper.Minimize(Window.GetWindow(this));

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowHelper.MaximizeRestore(Window.GetWindow(this));

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        Window.GetWindow(this)?.Close();
}
