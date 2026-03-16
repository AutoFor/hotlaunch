using System.Runtime.InteropServices;
using Hotlaunch.Core;

namespace Hotlaunch;

public class Win32WindowFocuser : IWindowFocuser
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int SW_RESTORE = 9;

    public void Focus(nint hwnd, bool restore)
    {
        if (restore)
            ShowWindow(hwnd, SW_RESTORE);

        // SetForegroundWindow はフォアグラウンドプロセス以外からだと
        // タスクバー点滅になる。AttachThreadInput で入力キューを繋いでから呼ぶ。
        WithAttachedInput(hwnd, () => SetForegroundWindow(hwnd));
    }

    private static void WithAttachedInput(IntPtr hwnd, Action action)
    {
        var foregroundHwnd = GetForegroundWindow();
        var foregroundTid  = GetWindowThreadProcessId(foregroundHwnd, out _);
        var currentTid     = GetCurrentThreadId();
        var targetTid      = GetWindowThreadProcessId(hwnd, out _);

        if (foregroundTid != currentTid)
            AttachThreadInput(currentTid, foregroundTid, true);
        if (targetTid != currentTid)
            AttachThreadInput(currentTid, targetTid, true);

        action();

        if (foregroundTid != currentTid)
            AttachThreadInput(currentTid, foregroundTid, false);
        if (targetTid != currentTid)
            AttachThreadInput(currentTid, targetTid, false);
    }
}
