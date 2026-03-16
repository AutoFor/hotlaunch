namespace Hotlaunch.Core;

public interface IWindowFocuser
{
    void Focus(nint hwnd, bool restore);
}
