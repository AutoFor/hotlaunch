using System.Windows.Forms;
using Hotlaunch.Core;
using Hotlaunch.Core.Config;

namespace Hotlaunch;

static class HotlaunchFactory
{
    private static readonly Dictionary<string, int> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alt"]    = (int)Keys.Menu,
        ["Henkan"] = 0x1C, // VK_CONVERT（変換キー）
    };

    private static int ParseVk(string keyName) =>
        KeyAliases.TryGetValue(keyName, out var vk) ? vk : (int)Enum.Parse<Keys>(keyName, ignoreCase: true);

    public static (LeaderSequenceTracker Tracker, AppLauncher Launcher, KeyboardHook Hook)
        Create(AppConfig config)
    {
        var launcher = new AppLauncher(new Win32ProcessFinder(), new Win32WindowFocuser(), new Win32ProcessStarter());

        int leaderVk = ParseVk(config.Leader.Key);
        var sequences = config.Hotkeys.Select(h => (ParseVk(h.Key), h));
        var tracker = new LeaderSequenceTracker(leaderVk, config.Leader.TimeoutMs, sequences, config.Leader.Count);

        tracker.SequenceMatched += entry => launcher.Launch(entry);

        var hook = new KeyboardHook(tracker);

        return (tracker, launcher, hook);
    }
}
