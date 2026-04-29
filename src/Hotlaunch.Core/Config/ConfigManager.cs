using System.Text.Json;
using Serilog;

namespace Hotlaunch.Core.Config;

public class ConfigManager(string configPath) : IConfigManager
{
    // 毎起動時にこのリストと比較し、Source が存在しないエントリを自動追加する
    private static readonly ModifierRemapConfig[] DefaultModifierRemaps =
    [
        new ModifierRemapConfig { Source = "LCtrl", Target = "LCtrl", SoloKey = "Muhenkan" },
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static ConfigManager Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".hotlaunch", "config.json"));

    public AppConfig Load()
    {
        if (!File.Exists(configPath))
            return CreateDefault();

        AppConfig config;
        try
        {
            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }

        if (MergeMissingDefaults(config))
            Save(config);

        return config;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, Options));
    }

    private static bool MergeMissingDefaults(AppConfig config)
    {
        bool changed = false;
        foreach (var def in DefaultModifierRemaps)
        {
            if (config.ModifierRemaps.Any(r => string.Equals(r.Source, def.Source, StringComparison.OrdinalIgnoreCase)))
                continue;
            config.ModifierRemaps = [..config.ModifierRemaps, def];
            Log.Information("設定: デフォルトのリマップを追加しました: {Source} → {Target} (SoloKey={SoloKey})",
                def.Source, def.Target, def.SoloKey ?? "なし");
            changed = true;
        }
        return changed;
    }

    private AppConfig CreateDefault()
    {
        var config = new AppConfig
        {
            Leader = new LeaderConfig { Key = "F12", TimeoutMs = 2000, Count = 1 },
            ModifierRemaps = DefaultModifierRemaps.ToArray(),
            Hotkeys =
            [
                new HotkeyEntry
                {
                    Key = "W",
                    AppPath = @"C:\Program Files\WezTerm\wezterm-gui.exe",
                    ProcessName = "wezterm-gui",
                },
            ],
        };

        Save(config);
        return config;
    }
}
