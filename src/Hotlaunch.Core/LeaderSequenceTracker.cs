using Hotlaunch.Core.Config;
using Serilog;

namespace Hotlaunch.Core;

public sealed class LeaderSequenceTracker : IDisposable
{
    private enum State { Idle, PressingLeader, WaitingForSequence }

    private readonly object _lock = new();
    private State _state = State.Idle;
    private int _pressCount;
    private System.Threading.Timer? _timer;
    private readonly int _leaderVk;
    private readonly int _timeoutMs;
    private readonly int _leaderPressesNeeded;
    private readonly IReadOnlyDictionary<int, HotkeyEntry> _sequences;
    private readonly IReadOnlyDictionary<int, (HotkeyEntry Entry, IReadOnlySet<int> RequiredMods)> _directKeys;

    public event Action<HotkeyEntry>? SequenceMatched;
    public event Action? LeaderActivated;
    public event Action? LeaderDeactivated;

    public LeaderSequenceTracker(int leaderVk, int timeoutMs, IEnumerable<(int Vk, HotkeyEntry Entry)> sequences, int leaderCount = 1, IEnumerable<(int Vk, HotkeyEntry Entry, IReadOnlySet<int> RequiredMods)>? directKeys = null)
    {
        _leaderVk = leaderVk;
        _timeoutMs = timeoutMs;
        _leaderPressesNeeded = Math.Max(1, leaderCount);
        _sequences = sequences.ToDictionary(x => x.Vk, x => x.Entry);
        _directKeys = directKeys?.ToDictionary(x => x.Vk, x => (x.Entry, x.RequiredMods))
            ?? new Dictionary<int, (HotkeyEntry, IReadOnlySet<int>)>();
    }

    // 後方互換: RequiredMods なし版（既存テスト・空のModsとして扱う）
    public LeaderSequenceTracker(int leaderVk, int timeoutMs, IEnumerable<(int Vk, HotkeyEntry Entry)> sequences, int leaderCount, IEnumerable<(int Vk, HotkeyEntry Entry)> directKeys)
        : this(leaderVk, timeoutMs, sequences, leaderCount,
               directKeys.Select(d => (d.Vk, d.Entry, (IReadOnlySet<int>)new HashSet<int>())))
    {
    }

    // 待機中に無視するModifierキー（Shift/Ctrl/Alt/Win）
    private static readonly HashSet<int> ModifierVkCodes =
    [
        0x10, 0xA0, 0xA1, // Shift, LShift, RShift
        0x11, 0xA2, 0xA3, // Ctrl, LCtrl, RCtrl
        0x12, 0xA4, 0xA5, // Alt, LAlt, RAlt
        0x5B, 0x5C,        // LWin, RWin
    ];

    /// <summary>キー押下を通知する。true を返した場合はそのキーを抑制する。</summary>
    public bool OnKeyDown(int vkCode, IReadOnlySet<int>? heldModifiers = null)
    {
        lock (_lock)
        {
            // Idle 状態でダイレクトキーが押されたら即マッチ（修飾キー条件も確認）
            if (_state == State.Idle && _directKeys.TryGetValue(vkCode, out var directInfo))
            {
                var (directEntry, requiredMods) = directInfo;
                bool modsOk = requiredMods.Count == 0
                    || (heldModifiers != null && requiredMods.IsSubsetOf(heldModifiers));
                if (modsOk)
                {
                    Log.Information("ダイレクトキー {VkCode} → マッチ ({AppPath})", vkCode, directEntry.AppPath);
                    SequenceMatched?.Invoke(directEntry);
                    return true;
                }
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
                return true;
            }

            if (_state == State.PressingLeader)
            {
                // リーダー以外のModifierキーは無視して連打待機継続
                if (ModifierVkCodes.Contains(vkCode))
                    return false;

                // 非修飾キー → キャンセル
                CancelTimer();
                TransitionTo(State.Idle);
                Log.Debug("連打キャンセル（VkCode={VkCode}）→ Idle", vkCode);
                return false;
            }

            if (_state == State.WaitingForSequence)
            {
                // Modifier キーは無視して待機継続
                if (ModifierVkCodes.Contains(vkCode))
                    return false;

                CancelTimer();
                TransitionTo(State.Idle);

                if (_sequences.TryGetValue(vkCode, out var entry))
                {
                    Log.Information("シーケンスキー {VkCode} → マッチ ({AppPath})", vkCode, entry.AppPath);
                    SequenceMatched?.Invoke(entry);
                    return true;
                }

                Log.Debug("シーケンスキー {VkCode} → 未登録、スルー", vkCode);
                return false;
            }

            return false;
        }
    }

    private void TransitionTo(State newState)
    {
        bool wasIdle = _state == State.Idle;
        _state = newState;
        bool isNowIdle = _state == State.Idle;

        if (wasIdle && !isNowIdle)
            LeaderActivated?.Invoke();
        else if (!wasIdle && isNowIdle)
            LeaderDeactivated?.Invoke();
    }

    private void ResetTimer()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ =>
        {
            lock (_lock)
            {
                if (_state == State.PressingLeader || _state == State.WaitingForSequence)
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
