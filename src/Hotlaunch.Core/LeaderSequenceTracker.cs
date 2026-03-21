using Hotlaunch.Core.Config;
using Serilog;

namespace Hotlaunch.Core;

public sealed class LeaderSequenceTracker : IDisposable
{
    private enum State { Idle, PressingLeader, HoldingModifier, WaitingForSequence }

    private readonly object _lock = new();
    private State _state = State.Idle;
    private int _pressCount;
    private System.Threading.Timer? _timer;
    private readonly int _leaderVk;
    private readonly int _chordVk; // -1 = single/double-press モード
    private readonly int _timeoutMs;
    private readonly int _leaderPressesNeeded;
    private readonly IReadOnlyDictionary<int, HotkeyEntry> _sequences;

    public event Action<HotkeyEntry>? SequenceMatched;
    public event Action? LeaderActivated;
    public event Action? LeaderDeactivated;

    public bool IsWaitingForSequence { get { lock (_lock) { return _state == State.WaitingForSequence; } } }
    public bool IsHoldingModifier   { get { lock (_lock) { return _state == State.HoldingModifier; } } }
    public bool IsSequenceKey(int vk) => _sequences.ContainsKey(vk);
    public int LeaderVk => _leaderVk;
    public int ChordVk  => _chordVk;

    public LeaderSequenceTracker(int leaderVk, int timeoutMs, IEnumerable<(int Vk, HotkeyEntry Entry)> sequences, int leaderCount = 1, int chordVk = -1)
    {
        _leaderVk = leaderVk;
        _chordVk  = chordVk;
        _timeoutMs = timeoutMs;
        _leaderPressesNeeded = Math.Max(1, leaderCount);
        _sequences = sequences.ToDictionary(x => x.Vk, x => x.Entry);
    }

    // 待機中に無視するModifierキー（Shift/Ctrl/Alt/Win）
    private static readonly HashSet<int> ModifierVkCodes =
    [
        0x10, 0xA0, 0xA1, // Shift, LShift, RShift
        0x11, 0xA2, 0xA3, // Ctrl, LCtrl, RCtrl
        0x12, 0xA4, 0xA5, // Alt, LAlt, RAlt
        0x5B, 0x5C,        // LWin, RWin
    ];

    /// <summary>キー押下を通知する。Block=true の場合はそのキーを抑制する。</summary>
    public RemapResult OnKeyDown(int vkCode)
    {
        lock (_lock)
        {
            if (_chordVk >= 0)
                return OnKeyDownChord(vkCode);
            else
                return OnKeyDownSingle(vkCode);
        }
    }

    // コードモード（無変換+Space など）
    private RemapResult OnKeyDownChord(int vkCode)
    {
        // WaitingForSequence 中にリーダー or チャードキーが再押し → タイマーリフレッシュ
        if (_state == State.WaitingForSequence && (vkCode == _leaderVk || vkCode == _chordVk))
        {
            ResetTimer();
            Log.Information("チャードリーダー再押下 (WaitingForSequence) → タイマーリフレッシュ");
            return new RemapResult(true, []);
        }

        // Idle/HoldingModifier + リーダーキー → HoldingModifier
        if ((_state == State.Idle || _state == State.HoldingModifier) && vkCode == _leaderVk)
        {
            if (_state == State.Idle) TransitionTo(State.HoldingModifier);
            ResetTimer();
            Log.Information("チャードリーダー修飾キー押下 → 修飾待機");
            return new RemapResult(true, []);
        }

        if (_state == State.HoldingModifier)
        {
            if (ModifierVkCodes.Contains(vkCode))
                return new RemapResult(false, []);

            // チャードキー → WaitingForSequence
            if (vkCode == _chordVk)
            {
                CancelTimer();
                TransitionTo(State.WaitingForSequence);
                ResetTimer();
                Log.Information("チャードキー押下 → 待機モード開始 ({TimeoutMs}ms)", _timeoutMs);
                return new RemapResult(true, []);
            }

            // チャード以外のキー → キャンセル。飲み込んだリーダーをリマッパー経由で再注入（Ctrl+key 維持）
            CancelTimer();
            TransitionTo(State.Idle);
            Log.Debug("チャードキャンセル（VkCode={VkCode}）→ Idle + リーダー+キーをリマッパー経由で再注入", vkCode);
            return new RemapResult(true, [], [(_leaderVk, false), (vkCode, false)]);
        }

        return OnKeyDownWaiting(vkCode);
    }

    // シングル/ダブルプレスモード
    private RemapResult OnKeyDownSingle(int vkCode)
    {
        // WaitingForSequence 中にリーダーキーが再押し → タイマーリフレッシュ
        if (_state == State.WaitingForSequence && vkCode == _leaderVk)
        {
            ResetTimer();
            Log.Information("リーダーキー再押下 (WaitingForSequence) → タイマーリフレッシュ");
            return new RemapResult(true, []);
        }

        if ((_state == State.Idle || _state == State.PressingLeader) && vkCode == _leaderVk)
        {
            _pressCount = _state == State.PressingLeader ? _pressCount + 1 : 1;
            if (_pressCount >= _leaderPressesNeeded)
            {
                TransitionTo(State.WaitingForSequence);
                Log.Information("リーダーキー {Count}回押下 → 待機モード開始 ({TimeoutMs}ms)", _pressCount, _timeoutMs);
            }
            else
            {
                TransitionTo(State.PressingLeader);
                Log.Information("リーダーキー {Count}/{Needed}回押下 → 連打待機", _pressCount, _leaderPressesNeeded);
            }
            ResetTimer();
            return new RemapResult(true, []);
        }

        if (_state == State.PressingLeader)
        {
            if (ModifierVkCodes.Contains(vkCode))
                return new RemapResult(false, []);

            var injectForRemapper = new List<(int, bool)>();
            for (int i = 0; i < _pressCount; i++)
                injectForRemapper.Add((_leaderVk, false));
            injectForRemapper.Add((vkCode, false));
            CancelTimer();
            TransitionTo(State.Idle);
            Log.Debug("連打キャンセル（VkCode={VkCode}）→ Idle + リーダー×{Count}+キーをリマッパー経由で再注入", vkCode, _pressCount);
            return new RemapResult(true, [], injectForRemapper);
        }

        return OnKeyDownWaiting(vkCode);
    }

    // WaitingForSequence 共通処理
    private RemapResult OnKeyDownWaiting(int vkCode)
    {
        if (_state != State.WaitingForSequence)
            return new RemapResult(false, []);

        if (ModifierVkCodes.Contains(vkCode))
            return new RemapResult(false, []);

        CancelTimer();
        TransitionTo(State.Idle);

        if (_sequences.TryGetValue(vkCode, out var entry))
        {
            Log.Information("シーケンスキー {VkCode} → マッチ ({AppPath})", vkCode, entry.AppPath);
            SequenceMatched?.Invoke(entry);
            return new RemapResult(true, []);
        }

        // 未登録キー → 飲み込んだキーを再注入（親指シフト等の組み合わせを壊さない）
        Log.Debug("シーケンスキー {VkCode} → 未登録、リーダー+キー再注入", vkCode);
        if (_chordVk >= 0)
            return new RemapResult(true, [(_leaderVk, false), (_chordVk, false), (vkCode, false)]);
        else
            return new RemapResult(true, [(_leaderVk, false), (vkCode, false)]);
    }

    /// <summary>キー解放を通知する。コードモードでリーダー修飾キーが解放された場合に状態を戻す。</summary>
    public RemapResult OnKeyUp(int vkCode)
    {
        lock (_lock)
        {
            // コードモード: HoldingModifier 中にリーダー修飾キーが解放 → Idle へ
            // ソロタップ扱いで元キーを再注入（IME キーとして機能させる）
            if (_chordVk >= 0 && _state == State.HoldingModifier && vkCode == _leaderVk)
            {
                CancelTimer();
                TransitionTo(State.Idle);
                Log.Debug("チャードリーダー修飾キー単独解放 → Idle + ソロタップ再注入");
                return new RemapResult(true, [(_leaderVk, false), (_leaderVk, true)]);
            }
            return new RemapResult(false, []);
        }
    }

    private void TransitionTo(State newState)
    {
        // LeaderActivated/Deactivated は WaitingForSequence との境界でのみ発火する。
        // HoldingModifier や PressingLeader は中間状態のためアイコンを変えない。
        bool wasWaiting = _state == State.WaitingForSequence;
        _state = newState;
        bool isNowWaiting = _state == State.WaitingForSequence;

        if (!wasWaiting && isNowWaiting)
            LeaderActivated?.Invoke();
        else if (wasWaiting && !isNowWaiting)
            LeaderDeactivated?.Invoke();
    }

    private void ResetTimer()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ =>
        {
            lock (_lock)
            {
                if (_state == State.PressingLeader || _state == State.HoldingModifier || _state == State.WaitingForSequence)
                {
                    Log.Debug("タイムアウト → 待機モード解除");
                    TransitionTo(State.Idle);
                }
            }
        }, null, _timeoutMs, Timeout.Infinite);
    }

    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => CancelTimer();
}
