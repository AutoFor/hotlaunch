using System.Diagnostics;
using System.Windows.Automation;
using Hotlaunch.Core;
using Serilog;

namespace Hotlaunch;

public class SpotifyPostActionHandler : IPostActionHandler
{
    public bool CanHandle(string actionName) =>
        string.Equals(actionName, "spotify-play-pause", StringComparison.OrdinalIgnoreCase);

    public void Execute(string actionName, bool isNewlyLaunched)
    {
        // UIA の FindAll はフックコールバックスレッドのタイムアウト（~200ms）を超えるため
        // 常に Task.Run でオフスレッド化する
        if (isNewlyLaunched)
            Task.Run(RetryUntilSuccess);
        else
            Task.Run(() => TryClickPlayPause());
    }

    private void RetryUntilSuccess()
    {
        // 500ms x 30回 = 最大15秒
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(500);
            if (TryClickPlayPause()) return;
        }
        Log.Warning("Spotify再生ボタン: タイムアウト");
    }

    private static bool TryClickPlayPause()
    {
        var process = Process.GetProcessesByName("Spotify")
            .FirstOrDefault(p => p.MainWindowHandle != nint.Zero);
        if (process == null) return false;

        AutomationElement? window;
        try
        {
            window = AutomationElement.FromHandle(process.MainWindowHandle);
        }
        catch (Exception ex)
        {
            Log.Debug("Spotify UIA ウィンドウ取得失敗: {Message}", ex.Message);
            return false;
        }

        // ControlType.Button + Name に再生/一時停止関連文字列を含む要素を検索
        // (AutomationId "play-pause-button" は日本語版では存在しないため使用しない)
        AutomationElement? button = null;
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

        foreach (AutomationElement b in buttons)
        {
            var name = b.GetCurrentPropertyValue(AutomationElement.NameProperty) as string ?? "";
            if (name.Contains("Play", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Pause", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("再生") ||
                name.Contains("一時停止"))
            {
                button = b;
                break;
            }
        }

        if (button == null)
        {
            Log.Debug("Spotify再生ボタン: UI要素が見つからない");
            return false;
        }

        if (button.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern invoke)
        {
            invoke.Invoke();
            Log.Information("Spotify再生/一時停止ボタンを押下");
            return true;
        }

        Log.Debug("Spotify再生ボタン: InvokePattern未対応");
        return false;
    }
}
