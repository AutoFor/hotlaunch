using Hotlaunch.Core;
using Hotlaunch.Core.Config;
using NSubstitute;
using Xunit;

namespace Hotlaunch.Core.Tests;

public class AppLauncherTests
{
    private readonly IProcessFinder _finder = Substitute.For<IProcessFinder>();
    private readonly IWindowFocuser _focuser = Substitute.For<IWindowFocuser>();
    private readonly IProcessStarter _starter = Substitute.For<IProcessStarter>();

    private AppLauncher CreateSut() => new(_finder, _focuser, _starter);

    private static HotkeyEntry WeztermEntry() => new()
    {
        Key = "W",
        AppPath = @"C:\Program Files\WezTerm\wezterm-gui.exe",
        ProcessName = "wezterm-gui",
    };

    [Fact]
    public void 起動済みのとき_フォーカスする()
    {
        var hwnd = (nint)0x1234;
        _finder.FindByName("wezterm-gui").Returns(new ProcessInfo(hwnd, IsMinimized: false));

        CreateSut().Launch(WeztermEntry());

        _focuser.Received(1).Focus(hwnd, restore: false);
        _starter.DidNotReceiveWithAnyArgs().Start(default!, default!);
    }

    [Fact]
    public void 最小化されているとき_復元してフォーカスする()
    {
        var hwnd = (nint)0x1234;
        _finder.FindByName("wezterm-gui").Returns(new ProcessInfo(hwnd, IsMinimized: true));

        CreateSut().Launch(WeztermEntry());

        _focuser.Received(1).Focus(hwnd, restore: true);
        _starter.DidNotReceiveWithAnyArgs().Start(default!, default!);
    }

    [Fact]
    public void 未起動のとき_新規起動する()
    {
        _finder.FindByName("wezterm-gui").Returns((ProcessInfo?)null);

        CreateSut().Launch(WeztermEntry());

        _starter.Received(1).Start(@"C:\Program Files\WezTerm\wezterm-gui.exe", "");
        _focuser.DidNotReceiveWithAnyArgs().Focus(default, default);
    }

    [Fact]
    public void ProcessName未指定のとき_AppPathのファイル名をプロセス名として使う()
    {
        var entry = new HotkeyEntry
        {
            AppPath = @"C:\Program Files\WezTerm\wezterm-gui.exe",
            ProcessName = null, // 未指定
        };
        _finder.FindByName("wezterm-gui").Returns((ProcessInfo?)null);

        CreateSut().Launch(entry);

        _finder.Received(1).FindByName("wezterm-gui");
    }

    [Fact]
    public void Args付きエントリで未起動のとき_ArgsをStartに渡す()
    {
        var entry = new HotkeyEntry
        {
            Key = "W",
            AppPath = @"C:\Program Files\WezTerm\wezterm-gui.exe",
            ProcessName = "wezterm-gui",
            Args = "--new-window",
        };
        _finder.FindByName("wezterm-gui").Returns((ProcessInfo?)null);

        CreateSut().Launch(entry);

        _starter.Received(1).Start(@"C:\Program Files\WezTerm\wezterm-gui.exe", "--new-window");
    }
}
