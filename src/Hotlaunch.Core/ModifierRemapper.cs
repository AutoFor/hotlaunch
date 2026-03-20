using Serilog;

namespace Hotlaunch.Core;

public readonly record struct RemapResult(bool Block, IReadOnlyList<(int Vk, bool KeyUp)> Inject, int? LeaderTriggerVk = null);

public sealed class ModifierRemapper
{
    private readonly record struct ChordRule(int SourceVk, int TriggerVk, int SyntheticVk);

    private readonly IReadOnlyDictionary<int, int> _rules;
    private readonly IReadOnlyList<ChordRule> _chordRules;
    private readonly HashSet<int> _heldSources = new();
    private readonly HashSet<int> _usedAsModifier = new();
    private readonly HashSet<int> _usedAsChordSource = new();
    private readonly HashSet<int> _heldNonSourceKeys = new();
    private readonly HashSet<int> _heldChordTriggers = new();

    public ModifierRemapper(
        IEnumerable<(int sourceVk, int targetVk)> rules,
        IEnumerable<(int sourceVk, int triggerVk, int syntheticVk)>? chords = null)
    {
        _rules = rules.ToDictionary(r => r.sourceVk, r => r.targetVk);
        _chordRules = chords is null
            ? Array.Empty<ChordRule>()
            : chords.Select(c => new ChordRule(c.sourceVk, c.triggerVk, c.syntheticVk)).ToList();
    }

    public bool HasPendingState =>
        _heldSources.Count > 0 || _heldNonSourceKeys.Count > 0 || _heldChordTriggers.Count > 0;

    public string StateDescription =>
        $"heldSources=[{string.Join(",", _heldSources.Select(v => $"0x{v:X2}"))}] " +
        $"usedAsModifier=[{string.Join(",", _usedAsModifier.Select(v => $"0x{v:X2}"))}] " +
        $"heldNonSourceKeys=[{string.Join(",", _heldNonSourceKeys.Select(v => $"0x{v:X2}"))}]";

    /// <summary>スタック状態をリセットする。無変換+C が効かなくなったときにトレイから呼ぶ。</summary>
    public void Reset()
    {
        _heldSources.Clear();
        _usedAsModifier.Clear();
        _usedAsChordSource.Clear();
        _heldNonSourceKeys.Clear();
        _heldChordTriggers.Clear();
        Log.Information("リマッパー: 状態をリセットしました");
    }

    public RemapResult OnKeyDown(int vkCode)
    {
        // チョード検出（ソースキー保持中にトリガーキーが押された、かつソースはまだ修飾キーとして使っていない）
        foreach (var chord in _chordRules)
        {
            if (chord.TriggerVk == vkCode
                && _heldSources.Contains(chord.SourceVk)
                && !_usedAsModifier.Contains(chord.SourceVk))
            {
                _heldChordTriggers.Add(vkCode);
                _usedAsChordSource.Add(chord.SourceVk);
                Log.Information("チョード検出: 0x{SrcHex}+0x{TrgHex} → 合成VK 0x{SynHex}",
                    chord.SourceVk.ToString("X2"), vkCode.ToString("X2"), chord.SyntheticVk.ToString("X2"));
                return new RemapResult(true, [], chord.SyntheticVk);
            }
        }

        // ソースキー押下 → 追跡開始
        if (_rules.ContainsKey(vkCode))
        {
            _heldSources.Add(vkCode);
            Log.Information("リマッパー: 0x{VkHex} 押下 → ソースキー追跡開始", vkCode.ToString("X2"));
            return new RemapResult(true, []);
        }
        // ソースキー保持中 → ターゲット+元キーを注入
        if (_heldSources.Count > 0)
        {
            var inject = new List<(int, bool)>();
            foreach (var src in _heldSources)
                if (_usedAsModifier.Add(src))
                    inject.Add((_rules[src], false));
            inject.Add((vkCode, false));
            _heldNonSourceKeys.Add(vkCode);
            Log.Information("リマッパー: コンボ検出 0x{VkHex} → {Count}キー注入 [{Keys}]",
                vkCode.ToString("X2"), inject.Count,
                string.Join(",", inject.Select(k => $"0x{k.Item1:X2}{(k.Item2 ? "↑" : "↓")}")));
            return new RemapResult(true, inject);
        }
        return new RemapResult(false, []);
    }

    public RemapResult OnKeyUp(int vkCode)
    {
        // チョードトリガーリリース → 抑制するだけ
        if (_heldChordTriggers.Remove(vkCode))
        {
            Log.Information("リマッパー: チョードトリガー 0x{VkHex} リリース → 抑制", vkCode.ToString("X2"));
            return new RemapResult(true, []);
        }

        // ソースキーリリース
        if (_rules.TryGetValue(vkCode, out int targetVk))
        {
            _heldSources.Remove(vkCode);
            if (_usedAsChordSource.Remove(vkCode))
            {
                Log.Information("リマッパー: 0x{VkHex} リリース (チョードソースとして使用済み → 抑制)", vkCode.ToString("X2"));
                return new RemapResult(true, []);
            }
            bool wasUsed = _usedAsModifier.Remove(vkCode);
            if (wasUsed)
            {
                Log.Information("リマッパー: 0x{VkHex} リリース (修飾キーとして使用済み → 0x{TargetHex}↑注入)",
                    vkCode.ToString("X2"), targetVk.ToString("X2"));
                return new RemapResult(true, [(targetVk, true)]);
            }
            else
            {
                Log.Information("リマッパー: 0x{VkHex} リリース (単独押し → 元キー注入)", vkCode.ToString("X2"));
                return new RemapResult(true, [(vkCode, false), (vkCode, true)]);
            }
        }
        // コンボ中に押された非ソースキーのリリース → キーアップを注入（ソース先行リリース時も対応）
        if (_heldNonSourceKeys.Remove(vkCode))
        {
            Log.Debug("リマッパー: 0x{VkHex} コンボキーリリース → キーアップ注入", vkCode.ToString("X2"));
            return new RemapResult(true, [(vkCode, true)]);
        }
        // ソースキー保持中の非ソースキーリリース（_heldNonSourceKeys 未登録のケース）
        if (_heldSources.Count > 0 && _usedAsModifier.Count > 0)
        {
            Log.Warning("リマッパー: 0x{VkHex} リリース (未追跡キー、ソース保持中) → キーアップ注入 {State}",
                vkCode.ToString("X2"), StateDescription);
            return new RemapResult(true, [(vkCode, true)]);
        }
        return new RemapResult(false, []);
    }
}
