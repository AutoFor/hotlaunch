using Hotlaunch.Core.Config;
using Serilog;

namespace Hotlaunch.Core;

public class AppLauncher(IProcessFinder finder, IWindowFocuser focuser, IProcessStarter starter)
{
    public void Launch(HotkeyEntry entry)
    {
        var processName = entry.ProcessName
            ?? Path.GetFileNameWithoutExtension(entry.AppPath.Split('/', '\\').Last());

        var process = finder.FindByName(processName);

        if (process != null)
        {
            Log.Information("フォーカス: {ProcessName} (最小化={IsMinimized})", processName, process.IsMinimized);
            focuser.Focus(process.MainWindowHandle, restore: process.IsMinimized);
        }
        else
        {
            Log.Information("新規起動: {AppPath}", entry.AppPath);
            starter.Start(entry.AppPath, entry.Args);
        }
    }
}
