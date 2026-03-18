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

    private static readonly Color NormalColor = Color.FromArgb(100, 100, 100);
    private static readonly Color ActiveColor = Color.FromArgb(0, 200, 80);

    private readonly TaskbarIcon _trayIcon;
    private readonly KeyboardHook _hook;
    private readonly LeaderSequenceTracker _tracker;

    public TrayApp()
    {
        var config = ConfigManager.Default.Load();
        (_tracker, _, _hook) = HotlaunchFactory.Create(config);

        var contextMenu = new ContextMenu();
        var exitItem = new MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        contextMenu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            Icon = CreateDotIcon(NormalColor),
            ToolTipText = "hotlaunch",
            ContextMenu = contextMenu,
        };
        _trayIcon.ForceCreate();

        // H.NotifyIcon は Icon を差し替えるたびに古い Icon を Dispose() する。
        // キャッシュすると 2 回目以降に ObjectDisposedException が発生するため、毎回新規生成する。
        _tracker.LeaderActivated   += () => _trayIcon.Dispatcher.Invoke(() => _trayIcon.Icon = CreateDotIcon(ActiveColor));
        _tracker.LeaderDeactivated += () => _trayIcon.Dispatcher.Invoke(() => _trayIcon.Icon = CreateDotIcon(NormalColor));
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
        _trayIcon.Dispose(); // 現在の Icon も H.NotifyIcon が Dispose する
    }
}
