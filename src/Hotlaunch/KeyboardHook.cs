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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint n, INPUT[] inputs, int size);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public int Type;      // INPUT_KEYBOARD = 1
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;            // KEYEVENTF_KEYUP = 0x0002
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL  = 13;
    private const int WM_KEYDOWN      = 0x0100;
    private const int WM_SYSKEYDOWN   = 0x0104;
    private const int WM_KEYUP        = 0x0101;
    private const int WM_SYSKEYUP     = 0x0105;
    private const uint WM_QUIT        = 0x0012;
    private const uint LLKHF_INJECTED = 0x10;

    private readonly LowLevelKeyboardProc _proc; // GC されないよう保持
    private readonly LeaderSequenceTracker _tracker;
    private readonly ModifierRemapper? _remapper;
    private IntPtr _hookId;
    private uint _hookThreadId;

    public KeyboardHook(LeaderSequenceTracker tracker, ModifierRemapper? remapper = null)
    {
        _tracker  = tracker;
        _remapper = remapper;
        _proc     = HookCallback;

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
        if (nCode < 0) return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var kbs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int vk          = (int)kbs.vkCode;
        bool isInjected = (kbs.flags & LLKHF_INJECTED) != 0;
        int msg         = (int)wParam;
        bool isDown     = msg == WM_KEYDOWN  || msg == WM_SYSKEYDOWN;
        bool isUp       = msg == WM_KEYUP    || msg == WM_SYSKEYUP;

        Log.Debug("フック受信: VK=0x{VkHex} isDown={IsDown} isUp={IsUp} isInjected={IsInjected}",
            vk.ToString("X2"), isDown, isUp, isInjected);

        if (isInjected) return CallNextHookEx(_hookId, nCode, wParam, lParam);

        if (_remapper != null)
        {
            var result = isDown ? _remapper.OnKeyDown(vk)
                       : isUp   ? _remapper.OnKeyUp(vk)
                       : default;
            Log.Debug("リマッパー結果: Block={Block} InjectCount={InjectCount}", result.Block, result.Inject.Count);
            if (result.Inject.Count > 0) SendKeys(result.Inject);
            if (result.Block) return (IntPtr)1;
        }

        if (isDown)
        {
            Log.Debug("キー押下: VK={VkCode} (0x{VkHex})", vk, vk.ToString("X2"));
            if (_tracker.OnKeyDown(vk))
                return (IntPtr)1;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void SendKeys(IReadOnlyList<(int Vk, bool KeyUp)> keys)
    {
        var inputs = new INPUT[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            inputs[i] = new INPUT { Type = 1, ki = new KEYBDINPUT { wVk = (ushort)keys[i].Vk, dwFlags = keys[i].KeyUp ? 0x0002u : 0u } };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
            Log.Warning("SendInput 失敗: sent={Sent}/{Total} err={Err}", sent, inputs.Length, Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        UnhookWindowsHookEx(_hookId);
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
