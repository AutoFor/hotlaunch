namespace Hotlaunch.Core;

public readonly record struct RemapResult(bool Block, IReadOnlyList<(int Vk, bool KeyUp)> Inject);

public sealed class ModifierRemapper
{
    private readonly IReadOnlyDictionary<int, int> _rules;
    private readonly HashSet<int> _heldSources = new();
    private readonly HashSet<int> _usedAsModifier = new();
    private readonly HashSet<int> _heldNonSourceKeys = new();

    public ModifierRemapper(IEnumerable<(int sourceVk, int targetVk)> rules)
        => _rules = rules.ToDictionary(r => r.sourceVk, r => r.targetVk);

    public RemapResult OnKeyDown(int vkCode)
    {
        // ソースキー押下 → 追跡開始
        if (_rules.ContainsKey(vkCode))
        {
            _heldSources.Add(vkCode);
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
            return new RemapResult(true, inject);
        }
        return new RemapResult(false, []);
    }

    public RemapResult OnKeyUp(int vkCode)
    {
        // ソースキーリリース
        if (_rules.TryGetValue(vkCode, out int targetVk))
        {
            _heldSources.Remove(vkCode);
            bool wasUsed = _usedAsModifier.Remove(vkCode);
            return wasUsed
                ? new RemapResult(true, [(targetVk, true)])
                : new RemapResult(true, [(vkCode, false), (vkCode, true)]);  // 単独押し: 元キーを注入
        }
        // コンボ中に押された非ソースキーのリリース → キーアップを注入（ソース先行リリース時も対応）
        if (_heldNonSourceKeys.Remove(vkCode))
            return new RemapResult(true, [(vkCode, true)]);
        // ソースキー保持中の非ソースキーリリース（_heldNonSourceKeys 未登録のケース）
        if (_heldSources.Count > 0 && _usedAsModifier.Count > 0)
            return new RemapResult(true, [(vkCode, true)]);
        return new RemapResult(false, []);
    }
}
