using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace ACCcom.Helpers;

public static class WindowHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MOUSEMENU = 0xF095;

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
}
