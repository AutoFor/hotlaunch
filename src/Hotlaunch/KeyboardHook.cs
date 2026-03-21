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
    // リマッパーだけ通過させる注入イベントのマーカー（dwExtraInfo に設定）
    private static readonly IntPtr HOTLAUNCH_REMAP_MARKER = new(0x484C4A01);

    private readonly LowLevelKeyboardProc _proc; // GC されないよう保持
    private readonly LeaderSequenceTracker _tracker;
    private readonly ModifierRemapper? _remapper;
    private IntPtr _hookId;
    private uint _hookThreadId;
    private long _lastEventTickMs;
    private System.Threading.Timer? _healthTimer;

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

        if (_hookId == IntPtr.Zero)
            Log.Warning("キーボードフック登録失敗！ hotlaunch はキーを受信できません");

        _lastEventTickMs = Environment.TickCount64;
        // 30秒ごとにフック状態を確認
        _healthTimer = new System.Threading.Timer(_ =>
        {
            var idleSec = (Environment.TickCount64 - Interlocked.Read(ref _lastEventTickMs)) / 1000;
            if (_remapper != null && _remapper.HasPendingState)
                Log.Warning("リマッパー: 状態が残留しています ({IdleSec}秒間イベントなし) {State}",
                    idleSec, _remapper.StateDescription);
            else
                Log.Debug("フック稼働確認: hookId={HookId}, {IdleSec}秒間イベントなし", _hookId, idleSec);
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hookId, nCode, wParam, lParam);

        Interlocked.Exchange(ref _lastEventTickMs, Environment.TickCount64);
        var kbs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int vk          = (int)kbs.vkCode;
        bool isInjected = (kbs.flags & LLKHF_INJECTED) != 0;
        int msg         = (int)wParam;
        bool isDown     = msg == WM_KEYDOWN  || msg == WM_SYSKEYDOWN;
        bool isUp       = msg == WM_KEYUP    || msg == WM_SYSKEYUP;

        Log.Debug("フック受信: VK=0x{VkHex} isDown={IsDown} isUp={IsUp} isInjected={IsInjected}",
            vk.ToString("X2"), isDown, isUp, isInjected);

        // REMAP_ONLY 注入: リマッパーのみ通過（PressingLeader キャンセル時のリーダー+コンボキー再注入）
        bool isRemapOnly = isInjected && kbs.dwExtraInfo == HOTLAUNCH_REMAP_MARKER;
        // その他のすべての注入イベント（hotlaunch 自身の通常注入・外部アプリの注入）はスキップ
        if (isInjected && !isRemapOnly) return CallNextHookEx(_hookId, nCode, wParam, lParam);

        // [1] シーケンス待機中はシーケンスキーをリマッパーより優先
        bool trackerEarlyHandled = false;
        if (!isRemapOnly && isDown && _tracker.IsWaitingForSequence && _tracker.IsSequenceKey(vk))
        {
            Log.Debug("トラッカー優先: WaitingForSequence + シーケンスキー 0x{VkHex}", vk.ToString("X2"));
            var trackerResult = _tracker.OnKeyDown(vk);
            if (trackerResult.Inject.Count > 0) SendKeys(trackerResult.Inject);
            if (trackerResult.Block)
            {
                _remapper?.Reset();
                return (IntPtr)1;
            }
            trackerEarlyHandled = true;
        }

        // [2] リーダーキーはトラッカーを優先（リマッパーがソースキーとして横取りする前に処理）
        bool leaderHandled = false;
        if (!isRemapOnly && isDown && vk == _tracker.LeaderVk)
        {
            Log.Debug("リーダーキー優先処理: 0x{VkHex}", vk.ToString("X2"));
            var trackerResult = _tracker.OnKeyDown(vk);
            if (trackerResult.Block)
            {
                // リマッパーソースキー（無変換等）のみ ghost 抑制。Ctrl 等の通常キーは不要。
                if (_remapper?.IsSourceKey(vk) == true)
                    _remapper.SuppressNextKeyUp(vk);
                if (trackerResult.InjectForRemapper.Count > 0) DispatchInjectForRemapper(trackerResult.InjectForRemapper);
                if (trackerResult.Inject.Count > 0) SendKeys(trackerResult.Inject);
                return (IntPtr)1;
            }
            leaderHandled = true;
        }

        // [2b] チャードキー: HoldingModifier 状態でチャードキーを検出（リマッパーより優先）
        if (!isRemapOnly && isDown && _tracker.ChordVk >= 0 && vk == _tracker.ChordVk && _tracker.IsHoldingModifier)
        {
            Log.Debug("チャードキー優先処理: 0x{VkHex}", vk.ToString("X2"));
            var trackerResult = _tracker.OnKeyDown(vk);
            if (trackerResult.Block)
            {
                if (trackerResult.Inject.Count > 0)
                {
                    // 同時押しキャンセル: 素通し再注入（Space↑は抑制しない）
                    SendKeys(trackerResult.Inject);
                }
                else
                {
                    // 通常チャード起動: チャードキーの↑を抑制
                    _remapper?.SuppressNextKeyUp(vk);
                }
                return (IntPtr)1;
            }
        }

        // [2c] キーアップ: チャードモードでリーダー修飾キー解放時の状態更新（リマッパーより前に実行）
        if (!isRemapOnly && isUp && _tracker.ChordVk >= 0)
        {
            bool wasHolding = _tracker.IsHoldingModifier;
            bool leaderReleased = _tracker.OnKeyUp(vk);

            if (leaderReleased && _remapper?.IsSourceKey(vk) == true)
            {
                // IMEキー（無変換等）のソロタップ: 再注入して物理↑をブロック
                SendKeys([(vk, false), (vk, true)]);
                return (IntPtr)1;
            }
            // 通常キー（Ctrl等）: 物理↑をそのまま通す → 以降の処理へ
        }

        // [3] リマッパー処理（物理キーと REMAP_ONLY 注入の両方）
        if (_remapper != null)
        {
            var result = isDown ? _remapper.OnKeyDown(vk)
                       : isUp   ? _remapper.OnKeyUp(vk)
                       : default;
            Log.Debug("リマッパー結果: Block={Block} InjectCount={InjectCount}", result.Block, result.Inject.Count);
            if (result.Inject.Count > 0) SendKeys(result.Inject);
            if (result.Block) return (IntPtr)1;
        }

        // [4] トラッカー処理（物理キー・リーダー/シーケンス処理済みを除く）
        if (!isRemapOnly && isDown && !trackerEarlyHandled && !leaderHandled)
        {
            Log.Debug("キー押下: VK={VkCode} (0x{VkHex})", vk, vk.ToString("X2"));
            var trackerResult = _tracker.OnKeyDown(vk);
            if (trackerResult.InjectForRemapper.Count > 0) DispatchInjectForRemapper(trackerResult.InjectForRemapper);
            if (trackerResult.Inject.Count > 0) SendKeys(trackerResult.Inject);
            if (trackerResult.Block) return (IntPtr)1;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void SendKeys(IReadOnlyList<(int Vk, bool KeyUp)> keys)
        => SendInputInternal(keys, IntPtr.Zero);

    /// <summary>リマッパーだけ通過させる注入（PressingLeader キャンセル時のリーダー+コンボキー）</summary>
    private void SendKeysForRemapper(IReadOnlyList<(int Vk, bool KeyUp)> keys)
        => SendInputInternal(keys, HOTLAUNCH_REMAP_MARKER);

    /// <summary>
    /// InjectForRemapper を処理する。コンボ対象キーならリマッパー経由、
    /// IMEキー等の非コンボ対象なら素通し注入（変換+無変換が Ctrl+変換 になるのを防ぐ）。
    /// </summary>
    private void DispatchInjectForRemapper(IReadOnlyList<(int Vk, bool KeyUp)> keys)
    {
        if (keys.Count == 0) return;
        // 末尾がコンボキー（リーダー×N + コンボキー1個 の構成）
        var comboKey = keys[^1];
        if (_remapper?.IsComboTarget(comboKey.Vk) ?? true)
            SendKeysForRemapper(keys);   // Ctrl+C など: リマッパー経由
        else
            SendKeys(keys);              // 変換など IME キー: 素通し注入
    }

    private void SendInputInternal(IReadOnlyList<(int Vk, bool KeyUp)> keys, IntPtr extraInfo)
    {
        var inputs = new INPUT[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            inputs[i] = new INPUT { Type = 1, ki = new KEYBDINPUT { wVk = (ushort)keys[i].Vk, dwFlags = keys[i].KeyUp ? 0x0002u : 0u, dwExtraInfo = extraInfo } };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
            Log.Warning("SendInput 失敗: sent={Sent}/{Total} err={Err}", sent, inputs.Length, Marshal.GetLastWin32Error());
    }

    /// <summary>リマッパーの状態をリセットする。無変換+C が効かなくなったときにトレイから呼ぶ。</summary>
    public void ResetRemapper()
    {
        _remapper?.Reset();
        if (_remapper == null)
            Log.Information("リマッパーが無効のためリセット不要");
    }

    public void Dispose()
    {
        _healthTimer?.Dispose();
        UnhookWindowsHookEx(_hookId);
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
