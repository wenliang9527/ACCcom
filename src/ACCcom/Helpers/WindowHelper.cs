using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using ACCcom.Core.Models;

namespace ACCcom.Helpers;

public static class WindowHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MOUSEMENU = 0xF095;

    // The settings object whose WindowStates dictionary persists secondary-window
    // placement. Set once from MainWindow so every window can attach without
    // threading settings through their constructors.
    private static Func<AppSettings>? _settingsProvider;
    public static void SetSettingsProvider(Func<AppSettings> provider) => _settingsProvider = provider;

    /// <summary>
    /// Sets up a chromeless titlebar for a Window: drag-to-move, double-click maximize/restore,
    /// and optional min/max/close buttons via code-behind.
    /// </summary>
    /// <param name="window">The window to set up.</param>
    /// <param name="titleBarBorder">The Border element serving as the titlebar drag area.</param>
    public static void SetupTitleBar(Window window, Border titleBarBorder)
    {
        if (titleBarBorder == null) return;

        // Enable edge/corner resizing with WindowChrome
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            ResizeBorderThickness = new Thickness(4),
            CaptionHeight = 0, // we handle caption ourselves
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        titleBarBorder.MouseLeftButtonDown += (s, e) =>
        {
            // Ignore clicks on buttons
            if (e.OriginalSource is FrameworkElement fe && (fe is Button || fe is ContentControl))
                return;
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                // Detect double-click for maximize/restore
                if (e.ClickCount == 2)
                {
                    if (window.ResizeMode != ResizeMode.NoResize)
                    {
                        window.WindowState = window.WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                        return;
                    }
                }
                window.DragMove();
            }
        };
    }

    /// <summary>
    /// Minimize button click handler.
    /// </summary>
    public static void Minimize(Window window)
    {
        window.WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Toggle maximize/restore button click handler.
    /// </summary>
    public static void MaximizeRestore(Window window)
    {
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <summary>Persists a secondary window's position/size across sessions.
    /// Call once from the window's constructor with a stable key (class name);
    /// placement is restored on Loaded and saved on Closed. Windows with
    /// ResizeMode.NoResize only persist position.</summary>
    public static void AttachWindowState(Window window, string key)
    {
        window.Loaded += (_, _) => RestoreWindowState(window, key);
        window.Closed += (_, _) => SaveWindowState(window, key);
    }

    private static void SaveWindowState(Window window, string key)
    {
        var settings = _settingsProvider?.Invoke();
        if (settings == null) return;

        var rect = window.RestoreBounds;
        if (rect.IsEmpty)
            rect = new Rect(window.Left, window.Top, window.Width, window.Height);
        // NoResize windows keep their XAML size; only their position changes.
        settings.WindowStates[key] = window.ResizeMode == ResizeMode.NoResize
            ? new WindowRect(rect.X, rect.Y, null, null)
            : new WindowRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static void RestoreWindowState(Window window, string key)
    {
        var settings = _settingsProvider?.Invoke();
        if (settings == null) return;
        if (!settings.WindowStates.TryGetValue(key, out var state)) return;

        var width = state.Width ?? window.Width;
        var height = state.Height ?? window.Height;

        // Guard against the window landing off-screen (e.g. a secondary monitor
        // that is no longer attached): keep it on the virtual screen, or fall
        // back to centering on the owner if nothing overlaps.
        var vs = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var restored = new Rect(state.X, state.Y, width, height);
        if (vs.IntersectsWith(restored))
        {
            window.Left = state.X;
            window.Top = state.Y;
            if (window.ResizeMode != ResizeMode.NoResize)
            {
                window.Width = width;
                window.Height = height;
            }
        }
        else if (window.Owner != null)
        {
            window.Left = window.Owner.Left + (window.Owner.Width - width) / 2;
            window.Top = window.Owner.Top + (window.Owner.Height - height) / 2;
        }
    }
}
