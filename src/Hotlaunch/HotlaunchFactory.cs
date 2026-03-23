using Hotlaunch.Core;
using Hotlaunch.Core.Config;

namespace Hotlaunch;

static class HotlaunchFactory
{
    // WinForms の Keys 列挙体は VK コードと 1:1 対応しているため、
    // WPF 移行後は直接 VK コードのマッピングで代替する。
    private static readonly Dictionary<string, int> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 修飾キー
        ["Alt"]    = 0x12, // VK_MENU
        ["Ctrl"]   = 0x11, // VK_CONTROL
        ["Shift"]  = 0x10, // VK_SHIFT
        ["Win"]    = 0x5B, // VK_LWIN
        ["Menu"]   = 0x12, // VK_MENU (Alt の別名)
        // 日本語キー
        ["Henkan"]   = 0x1C, // VK_CONVERT（変換）
        ["Muhenkan"] = 0x1D, // VK_NONCONVERT（無変換）
        ["Kana"]     = 0x15, // VK_KANA
        // アルファベット A-Z
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44,
        ["E"] = 0x45, ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48,
        ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C,
        ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50,
        ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58,
        ["Y"] = 0x59, ["Z"] = 0x5A,
        // 数字 0-9
        ["D0"] = 0x30, ["D1"] = 0x31, ["D2"] = 0x32, ["D3"] = 0x33,
        ["D4"] = 0x34, ["D5"] = 0x35, ["D6"] = 0x36, ["D7"] = 0x37,
        ["D8"] = 0x38, ["D9"] = 0x39,
        // ファンクションキー
        ["F1"]  = 0x70, ["F2"]  = 0x71, ["F3"]  = 0x72, ["F4"]  = 0x73,
        ["F5"]  = 0x74, ["F6"]  = 0x75, ["F7"]  = 0x76, ["F8"]  = 0x77,
        ["F9"]  = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["F13"] = 0x7C, ["F14"] = 0x7D, ["F15"] = 0x7E, ["F16"] = 0x7F,
        // 特殊キー
        ["Return"]   = 0x0D, ["Enter"]    = 0x0D,
        ["Space"]    = 0x20, ["Tab"]      = 0x09,
        ["Escape"]   = 0x1B, ["Back"]     = 0x08,
        ["Delete"]   = 0x2E, ["Insert"]   = 0x2D,
        ["Home"]     = 0x24, ["End"]      = 0x23,
        ["PageUp"]   = 0x21, ["PageDown"] = 0x22,
        ["Left"]     = 0x25, ["Up"]       = 0x26,
        ["Right"]    = 0x27, ["Down"]     = 0x28,
    };

    private static int ParseVk(string keyName)
    {
        if (VkMap.TryGetValue(keyName, out var vk))
            return vk;
        throw new ArgumentException($"Unknown key name: '{keyName}'");
    }

    // 未割り当て VK: チョードリーダーの合成キーとして使用
    private const int ChordSyntheticVk = 0xE8;

    public static (LeaderSequenceTracker Tracker, AppLauncher Launcher, KeyboardHook Hook)
        Create(AppConfig config)
    {
        var launcher = new AppLauncher(
            new Win32ProcessFinder(),
            new Win32WindowFocuser(),
            new Win32ProcessStarter(),
            [new SpotifyPostActionHandler(), new TeamsPostActionHandler()]);

        bool isChord = !string.IsNullOrEmpty(config.Leader.ChordKey);
        int leaderVk = isChord ? ChordSyntheticVk : ParseVk(config.Leader.Key);
        int leaderCount = isChord ? 1 : config.Leader.Count;

        var sequences = config.Hotkeys.Select(h => (ParseVk(h.Key), h));
        var directKeys = config.DirectHotkeys.Select(h =>
            (ParseVk(h.Key), h, (IReadOnlySet<int>)h.RequiredModifiers.Select(ParseVk).ToHashSet()));
        var tracker = new LeaderSequenceTracker(leaderVk, config.Leader.TimeoutMs, sequences, leaderCount, directKeys);

        tracker.SequenceMatched += entry => launcher.Launch(entry);

        IEnumerable<(int, int, int)>? chordRules = isChord
            ? [(ParseVk(config.Leader.Key), ParseVk(config.Leader.ChordKey!), ChordSyntheticVk)]
            : null;

        ModifierRemapper? remapper = (config.ModifierRemaps.Length > 0 || chordRules != null)
            ? new ModifierRemapper(
                config.ModifierRemaps.Select(r => (ParseVk(r.Source), ParseVk(r.Target))),
                chordRules)
            : null;

        var hook = new KeyboardHook(tracker, remapper);

        return (tracker, launcher, hook);
    }
}
