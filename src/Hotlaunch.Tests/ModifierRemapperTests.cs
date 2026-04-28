using Hotlaunch.Core;
using Xunit;

namespace Hotlaunch.Core.Tests;

public class ModifierRemapperTests
{
    private const int MuhenkanVk  = 0x1D; // VK_NONCONVERT
    private const int HenkanVk    = 0x1C; // VK_CONVERT
    private const int CtrlVk      = 0x11; // VK_CONTROL
    private const int AltVk       = 0x12; // VK_MENU
    private const int CVk         = 0x43;
    private const int ZVk         = 0x5A;
    private const int SyntheticVk = 0xE8;

    private static ModifierRemapper Create()
        => new ModifierRemapper([(MuhenkanVk, CtrlVk, (int?)null)]);

    private static ModifierRemapper CreateWithChord()
        => new ModifierRemapper([(MuhenkanVk, CtrlVk, (int?)null)], [(MuhenkanVk, HenkanVk, SyntheticVk)]);

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

    [Fact]
    public void チョード_無変換押下後に変換を押すと合成VKが返る()
    {
        var r = CreateWithChord();
        r.OnKeyDown(MuhenkanVk);
        var result = r.OnKeyDown(HenkanVk);

        Assert.True(result.Block);
        Assert.Empty(result.Inject);
        Assert.Equal(SyntheticVk, result.LeaderTriggerVk);
    }

    [Fact]
    public void チョード_変換リリースは抑制される()
    {
        var r = CreateWithChord();
        r.OnKeyDown(MuhenkanVk);
        r.OnKeyDown(HenkanVk);
        var result = r.OnKeyUp(HenkanVk);

        Assert.True(result.Block);
        Assert.Empty(result.Inject);
    }

    [Fact]
    public void チョード_無変換リリースはCtrl注入せず抑制される()
    {
        var r = CreateWithChord();
        r.OnKeyDown(MuhenkanVk);
        r.OnKeyDown(HenkanVk);
        r.OnKeyUp(HenkanVk);
        var result = r.OnKeyUp(MuhenkanVk);

        Assert.True(result.Block);
        Assert.Empty(result.Inject); // Ctrl↑ が注入されないことを確認
    }

    [Fact]
    public void チョード_無変換単独押しは素通しで注入しない()
    {
        // 物理キーが OnKeyDown で素通し済みなので OnKeyUp も素通し
        var r = CreateWithChord();
        r.OnKeyDown(MuhenkanVk);
        var result = r.OnKeyUp(MuhenkanVk);

        Assert.False(result.Block);
        Assert.Empty(result.Inject);
    }

    [Fact]
    public void チョード_無変換がCtrl修飾として使用済みなら変換押下でチョードにならない()
    {
        var r = CreateWithChord();
        r.OnKeyDown(MuhenkanVk);
        r.OnKeyDown(CVk);            // 無変換をCtrlとして使用
        var result = r.OnKeyDown(HenkanVk); // この時点ではチョードにならない

        Assert.Null(result.LeaderTriggerVk);
    }


    // Ctrl → Muhenkan マッピング用テスト
    private const int LCtrlVk = 0xA2; // VK_LCONTROL

    private static ModifierRemapper CreateCtrlToMuhenkan()
        => new ModifierRemapper([(LCtrlVk, LCtrlVk, MuhenkanVk)]);

    [Fact]
    public void Ctrl単独押しは無変換DOWN_UPが注入される()
    {
        var r = CreateCtrlToMuhenkan();
        r.OnKeyDown(LCtrlVk);
        var result = r.OnKeyUp(LCtrlVk);

        Assert.True(result.Block);
        Assert.Equal(2, result.Inject.Count);
        Assert.Equal((MuhenkanVk, false), result.Inject[0]); // 無変換↓
        Assert.Equal((MuhenkanVk, true),  result.Inject[1]); // 無変換↑
    }

    [Fact]
    public void CtrlコンボはLCtrl注入される()
    {
        var r = CreateCtrlToMuhenkan();
        r.OnKeyDown(LCtrlVk);
        var result = r.OnKeyDown(CVk);

        Assert.True(result.Block);
        Assert.Equal(2, result.Inject.Count);
        Assert.Equal((LCtrlVk, false), result.Inject[0]); // LCtrl↓
        Assert.Equal((CVk, false), result.Inject[1]);
    }

    [Fact]
    public void Ctrlコンボ後のCtrlリリースはLCtrl上が注入される()
    {
        var r = CreateCtrlToMuhenkan();
        r.OnKeyDown(LCtrlVk);
        r.OnKeyDown(CVk);
        r.OnKeyUp(CVk);
        var result = r.OnKeyUp(LCtrlVk);

        Assert.False(result.Block);
        Assert.Single(result.Inject);
        Assert.Equal((LCtrlVk, true), result.Inject[0]); // LCtrl↑ のみ（無変換は注入しない）
    }
}
