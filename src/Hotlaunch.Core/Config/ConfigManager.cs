using System.Text.Json;
using Serilog;

namespace Hotlaunch.Core.Config;

public class ConfigManager(string configPath) : IConfigManager
{
    private const int CurrentSchemaVersion = 1;

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

        if (config.SchemaVersion < CurrentSchemaVersion)
        {
            config = Migrate(config);
            Save(config);
        }

        return config;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, Options));
    }

    private static AppConfig Migrate(AppConfig config)
    {
        int from = config.SchemaVersion;

        // v0 → v1: LCtrl リマップがなければ追加
        if (config.SchemaVersion < 1)
        {
            if (config.ModifierRemaps.Length == 0)
            {
                config.ModifierRemaps =
                [
                    new ModifierRemapConfig { Source = "LCtrl", Target = "LCtrl", SoloKey = "Muhenkan" },
                ];
                Log.Information("設定マイグレーション v0→v1: LCtrl リマップを追加しました");
            }
            config.SchemaVersion = 1;
        }

        Log.Information("設定マイグレーション完了: v{From} → v{To}", from, config.SchemaVersion);
        return config;
    }

    private AppConfig CreateDefault()
    {
        var config = new AppConfig
        {
            SchemaVersion = CurrentSchemaVersion,
            Leader = new LeaderConfig { Key = "F12", TimeoutMs = 2000, Count = 1 },
            ModifierRemaps =
            [
                new ModifierRemapConfig { Source = "LCtrl", Target = "LCtrl", SoloKey = "Muhenkan" },
            ],
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
