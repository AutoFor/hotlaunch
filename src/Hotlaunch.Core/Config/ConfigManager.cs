using System.Text.Json;

namespace Hotlaunch.Core.Config;

public class ConfigManager(string configPath) : IConfigManager
{
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

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, Options));
    }

    private AppConfig CreateDefault()
    {
        var config = new AppConfig
        {
            Leader = new LeaderConfig { Key = "F12", TimeoutMs = 2000, Count = 1 },
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
