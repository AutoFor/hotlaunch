using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Hotlaunch.Core;
using Hotlaunch.Core.Config;

namespace Hotlaunch;

sealed class TrayApp : Form
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _trayIcon;
    private readonly KeyboardHook _hook;
    private readonly LeaderSequenceTracker _tracker;
    private readonly Icon _normalIcon;
    private readonly Icon _activeIcon;

    public TrayApp()
    {
        // フォームを完全に非表示にする
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;

        // BeginInvoke のためにウィンドウハンドルを事前生成
        _ = Handle;

        _normalIcon = CreateDotIcon(Color.FromArgb(100, 100, 100)); // グレー
        _activeIcon = CreateDotIcon(Color.FromArgb(0, 200, 80));    // グリーン

        var config = ConfigManager.Default.Load();
        (_tracker, _, _hook) = HotlaunchFactory.Create(config);

        _trayIcon = new NotifyIcon
        {
            Icon = _normalIcon,
            Visible = true,
            Text = "hotlaunch",
            ContextMenuStrip = BuildContextMenu(),
        };

        _tracker.LeaderActivated   += () => BeginInvoke(() => _trayIcon.Icon = _activeIcon);
        _tracker.LeaderDeactivated += () => BeginInvoke(() => _trayIcon.Icon = _normalIcon);
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

    // フォームを表示しない
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("終了", null, (_, _) => Application.Exit());
        return menu;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hook.Dispose();
            _tracker.Dispose();
            _trayIcon.Dispose();
            _normalIcon.Dispose();
            _activeIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
