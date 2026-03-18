namespace Hotlaunch.Core.Config;

public class LeaderConfig
{
    public string Key { get; set; } = "Alt";
    public int TimeoutMs { get; set; } = 2000;
    public int Count { get; set; } = 1;
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
