namespace Hotlaunch.Core;

public interface IProcessStarter
{
    void Start(string appPath, string args);
}
