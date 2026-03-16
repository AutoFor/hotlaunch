using System.Diagnostics;
using Hotlaunch.Core;

namespace Hotlaunch;

public class Win32ProcessStarter : IProcessStarter
{
    public void Start(string appPath, string args)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = appPath,
            Arguments = args,
            UseShellExecute = true,
        });
    }
}
