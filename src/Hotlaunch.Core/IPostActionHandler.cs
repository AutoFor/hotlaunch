namespace Hotlaunch.Core;

public interface IPostActionHandler
{
    bool CanHandle(string actionName);
    void Execute(string actionName, bool isNewlyLaunched);
}
