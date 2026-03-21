namespace Hotlaunch.Core.Config;

public class LeaderConfig
{
    public string Key { get; set; } = "Alt";
    public int TimeoutMs { get; set; } = 2000;
    public int Count { get; set; } = 1;
    /// <summary>null = single/double-press モード。キー名を指定すると Key+ChordKey のコードリーダーになる。</summary>
    public string? ChordKey { get; set; } = null;
    /// <summary>
    /// チャードリーダーモード時に、修飾キー押下からチャードキー押下までの最低待機時間（ms）。
    /// これより短い同時押しは親指シフト等の通常入力として素通しする。デフォルト 200ms。
    /// </summary>
    public int ChordDelayMs { get; set; } = 200;
}

public class HotkeyEntry
{
    public string Key { get; set; } = "";
    public string AppPath { get; set; } = "";
    public string Args { get; set; } = "";
    public string? ProcessName { get; set; }
}

public class ModifierRemapConfig
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
}

public class AppConfig
{
    public LeaderConfig Leader { get; set; } = new();
    public ModifierRemapConfig[] ModifierRemaps { get; set; } = [];
    public HotkeyEntry[] Hotkeys { get; set; } = [];
}
