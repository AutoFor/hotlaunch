using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Hotlaunch.Core;
using Hotlaunch.Core.Config;

namespace Hotlaunch;

sealed class TrayApp : IDisposable
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private readonly TaskbarIcon _trayIcon;
    private readonly KeyboardHook _hook;
    private readonly LeaderSequenceTracker _tracker;
    private readonly Icon _normalIcon;
    private readonly Icon _activeIcon;

    public TrayApp()
    {
        _normalIcon = CreateDotIcon(Color.FromArgb(100, 100, 100)); // グレー
        _activeIcon = CreateDotIcon(Color.FromArgb(0, 200, 80));    // グリーン

        var config = ConfigManager.Default.Load();
        (_tracker, _, _hook) = HotlaunchFactory.Create(config);

        var contextMenu = new ContextMenu();
        var exitItem = new MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        contextMenu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            Icon = _normalIcon,
            ToolTipText = "hotlaunch",
            ContextMenu = contextMenu,
        };

        _tracker.LeaderActivated   += () => _trayIcon.Dispatcher.Invoke(() => _trayIcon.Icon = _activeIcon);
        _tracker.LeaderDeactivated += () => _trayIcon.Dispatcher.Invoke(() => _trayIcon.Icon = _normalIcon);
    }

    private static Icon CreateDotIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 2, 2, 12, 12);
        var hicon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hicon).Clone();
        DestroyIcon(hicon);
        return icon;
    }

    public void Dispose()
    {
        _hook.Dispose();
        _tracker.Dispose();
        _trayIcon.Dispose();
        _normalIcon.Dispose();
        _activeIcon.Dispose();
    }
}
