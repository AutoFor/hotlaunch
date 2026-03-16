namespace Hotlaunch.Core;

public interface IProcessFinder
{
    // 指定プロセス名で起動中かつ MainWindowHandle を持つものを返す
    // 存在しなければ null
    ProcessInfo? FindByName(string processName);
}

public record ProcessInfo(nint MainWindowHandle, bool IsMinimized);
