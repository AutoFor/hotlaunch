using Hotlaunch.Core;
using Xunit;

namespace Hotlaunch.Core.Tests;

public class ModifierRemapperTests
{
    private const int MuhenkanVk = 0x1D; // VK_NONCONVERT
    private const int CtrlVk     = 0x11; // VK_CONTROL
    private const int AltVk      = 0x12; // VK_MENU
    private const int CVk        = 0x43;
    private const int ZVk        = 0x5A;

    private static ModifierRemapper Create()
        => new ModifierRemapper([(MuhenkanVk, CtrlVk)]);

    [Fact]
    public void ソースキー押下は素通しで何も注入しない()
    {
        var r = Create();
        var result = r.OnKeyDown(MuhenkanVk);
        Assert.False(result.Block); // 物理キーを素通し（親指シフト等がそのまま受け取れる）
        Assert.Empty(result.Inject);
    }

    [Fact]
    public void ソースキー保持中に別キーでターゲットと元キーが注入される()
    {
        var r = Create();
        r.OnKeyDown(MuhenkanVk);
        var result = r.OnKeyDown(CVk);

        Assert.True(result.Block);
        Assert.Equal(2, result.Inject.Count);
        Assert.Equal((CtrlVk, false), result.Inject[0]);
        Assert.Equal((CVk, false), result.Inject[1]);
    }

    [Fact]
    public void キーアップで元キーのキーアップが注入される()
    {
        var r = Create();
        r.OnKeyDown(MuhenkanVk);
        r.OnKeyDown(CVk);
        var result = r.OnKeyUp(CVk);

        Assert.True(result.Block);
        Assert.Single(result.Inject);
        Assert.Equal((CVk, true), result.Inject[0]);
    }

    [Fact]
    public void ソースキーリリースでターゲットキーアップが注入され物理キーは素通し()
    {
        var r = Create();
        r.OnKeyDown(MuhenkanVk);
        r.OnKeyDown(CVk);
        r.OnKeyUp(CVk);
        var result = r.OnKeyUp(MuhenkanVk);

        Assert.False(result.Block); // 物理↑も素通し
        Assert.Single(result.Inject);
        Assert.Equal((CtrlVk, true), result.Inject[0]);
    }

    [Fact]
    public void 単独押しリリースは素通しで注入しない()
    {
        // OnKeyDown で物理↓が素通し済みなので OnKeyUp も素通し（注入不要）
        var r = Create();
        r.OnKeyDown(MuhenkanVk);
        var result = r.OnKeyUp(MuhenkanVk);

        Assert.False(result.Block);
        Assert.Empty(result.Inject);
    }

    [Fact]
    public void Alt保持中に無変換押下でCtrlAltが注入される()
    {
        var r = Create();
        // Alt は _rules に含まれないが _heldSources がある状態で来る
        r.OnKeyDown(MuhenkanVk);
        var altResult = r.OnKeyDown(AltVk);

        // Ctrl↓ + Alt↓ が注入される
        Assert.True(altResult.Block);
        Assert.Equal(2, altResult.Inject.Count);
        Assert.Equal((CtrlVk, false), altResult.Inject[0]);
        Assert.Equal((AltVk, false), altResult.Inject[1]);

        // 続けて C↓ を押すと Ctrl は再注入されず C のみ
        var cResult = r.OnKeyDown(CVk);
        Assert.True(cResult.Block);
        Assert.Single(cResult.Inject);
        Assert.Equal((CVk, false), cResult.Inject[0]);
    }

    [Fact]
    public void ソースキーを先にリリースしても元キーのキーアップがブロックされ注入される()
    {
        // 無変換↓ → C↓ → 無変換↑ → C↑ の順でリリース
        var r = Create();
        r.OnKeyDown(MuhenkanVk);
        r.OnKeyDown(CVk);
        r.OnKeyUp(MuhenkanVk); // 無変換を先にリリース

        var result = r.OnKeyUp(CVk); // C を後からリリース

        Assert.True(result.Block);
        Assert.Single(result.Inject);
        Assert.Equal((CVk, true), result.Inject[0]);
    }

    [Fact]
    public void ルール外のキーはブロックされない()
    {
        var r = Create();
        var result = r.OnKeyDown(CVk);
        Assert.False(result.Block);
        Assert.Empty(result.Inject);
    }
}
