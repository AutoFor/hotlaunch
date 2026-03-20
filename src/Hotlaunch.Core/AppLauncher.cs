using Hotlaunch.Core.Config;
using Serilog;

namespace Hotlaunch.Core;

public class AppLauncher(
    IProcessFinder finder,
    IWindowFocuser focuser,
    IProcessStarter starter,
    IEnumerable<IPostActionHandler>? postActionHandlers = null)
{
    private readonly IReadOnlyList<IPostActionHandler> _handlers =
        (postActionHandlers ?? []).ToList();

    public void Launch(HotkeyEntry entry)
    {
        var processName = entry.ProcessName
            ?? Path.GetFileNameWithoutExtension(entry.AppPath.Split('/', '\\').Last());

        var process = finder.FindByName(processName);
        bool isNewlyLaunched = false;

        if (process != null)
        {
            Log.Information("フォーカス: {ProcessName} (最小化={IsMinimized})", processName, process.IsMinimized);
            focuser.Focus(process.MainWindowHandle, restore: process.IsMinimized);
        }
        else if (entry.SkipIfNotRunning)
        {
            Log.Information("未起動のためスキップ: {ProcessName}", processName);
            return;
        }
        else
        {
            Log.Information("新規起動: {AppPath}", entry.AppPath);
            starter.Start(entry.AppPath, entry.Args);
            isNewlyLaunched = true;
        }

        if (entry.PostAction is { } action)
        {
            var handler = _handlers.FirstOrDefault(h => h.CanHandle(action));
            if (handler != null)
                handler.Execute(action, isNewlyLaunched);
            else
                Log.Warning("PostAction未対応: {Action}", action);
        }
    }
}
