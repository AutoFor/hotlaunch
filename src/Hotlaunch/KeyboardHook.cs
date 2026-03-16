using System.Runtime.InteropServices;
using Hotlaunch.Core;
using Serilog;

namespace Hotlaunch;

// WH_KEYBOARD_LL を専用スレッドで動かす。
// WinForms のメッセージループとは独立した GetMessage ループを持つことで
// フック配信を確実にする。
sealed class KeyboardHook : IDisposable
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX, ptY;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const uint WM_QUIT       = 0x0012;

    private readonly LowLevelKeyboardProc _proc; // GC されないよう保持
    private readonly LeaderSequenceTracker _tracker;
    private IntPtr _hookId;
    private uint _hookThreadId;

    public KeyboardHook(LeaderSequenceTracker tracker)
    {
        _tracker = tracker;
        _proc = HookCallback;

        var ready = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
            _hookThreadId = GetCurrentThreadId();
            Log.Information("キーボードフック登録完了 (handle={HookId}, threadId={ThreadId})", _hookId, _hookThreadId);
            ready.Set();

            // このスレッド専用の GetMessage ループ
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        })
        {
            IsBackground = true,
            Name = "KeyboardHookThread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        ready.Wait(); // フック登録完了まで待つ
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Log.Debug("キー押下: VK={VkCode} (0x{VkHex})", vkCode, vkCode.ToString("X2"));
                if (_tracker.OnKeyDown(vkCode))
                    return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        UnhookWindowsHookEx(_hookId);
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
