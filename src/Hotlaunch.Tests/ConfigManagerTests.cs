using System.Text.Json;
using Hotlaunch.Core.Config;
using Xunit;

namespace Hotlaunch.Core.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private ConfigManager CreateManager(string? fileName = null)
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, fileName ?? "config.json");
        return new ConfigManager(path);
    }

    [Fact]
    public void 設定ファイルが存在しないときデフォルト設定を返す()
    {
        var manager = CreateManager();
        var config = manager.Load();

        Assert.Equal("F12", config.Leader.Key);
        Assert.Equal(2000, config.Leader.TimeoutMs);
        Assert.Equal(1, config.Leader.Count);
        Assert.Single(config.Hotkeys);
        Assert.Equal("W", config.Hotkeys[0].Key);
        Assert.Equal("wezterm-gui", config.Hotkeys[0].ProcessName);
    }

    [Fact]
    public void 保存した設定を読み込むとラウンドトリップできる()
    {
        var manager = CreateManager();
        var original = new AppConfig
        {
            Leader = new LeaderConfig { Key = "Alt", TimeoutMs = 1500 },
            Hotkeys =
            [
                new HotkeyEntry { Key = "T", AppPath = @"C:\term.exe", ProcessName = "term", Args = "--here" },
            ],
        };

        manager.Save(original);
        var loaded = manager.Load();

        Assert.Equal("Alt", loaded.Leader.Key);
        Assert.Equal(1500, loaded.Leader.TimeoutMs);
        Assert.Single(loaded.Hotkeys);
        Assert.Equal("T", loaded.Hotkeys[0].Key);
        Assert.Equal(@"C:\term.exe", loaded.Hotkeys[0].AppPath);
        Assert.Equal("term", loaded.Hotkeys[0].ProcessName);
        Assert.Equal("--here", loaded.Hotkeys[0].Args);
    }

    [Fact]
    public void 不正なJSONのときデフォルト設定にフォールバックする()
    {
        var path = Path.Combine(_tempDir, "config.json");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(path, "{ this is not valid json !!!");

        var manager = new ConfigManager(path);
        var config = manager.Load();

        Assert.Equal("F12", config.Leader.Key);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
