using System.Diagnostics;
using System.Runtime.InteropServices;
using Hotlaunch.Core;

namespace Hotlaunch;

public class Win32ProcessFinder : IProcessFinder
{
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    public ProcessInfo? FindByName(string processName)
    {
        var process = Process.GetProcessesByName(processName)
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        if (process == null)
            return null;

        return new ProcessInfo(
            MainWindowHandle: process.MainWindowHandle,
            IsMinimized: IsIconic(process.MainWindowHandle));
    }
}
