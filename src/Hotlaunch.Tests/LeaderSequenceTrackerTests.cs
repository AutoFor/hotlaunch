using Hotlaunch.Core;
using Hotlaunch.Core.Config;
using Xunit;

namespace Hotlaunch.Core.Tests;

public class LeaderSequenceTrackerTests
{
    private const int AltVk = 18; // Keys.Menu (VK_MENU)
    private const int WVk   = 87; // Keys.W
    private const int AVk   = 65; // Keys.A（未登録キー）

    private static LeaderSequenceTracker CreateTracker(int timeoutMs = 2000, int leaderCount = 1)
    {
        var entry = new HotkeyEntry { Key = "W", AppPath = @"C:\wezterm-gui.exe", ProcessName = "wezterm-gui" };
        return new LeaderSequenceTracker(AltVk, timeoutMs, [(WVk, entry)], leaderCount);
    }

    [Fact]
    public void Alt押下はキーを抑制する()
    {
        using var tracker = CreateTracker();
        Assert.True(tracker.OnKeyDown(AltVk));
    }

    [Fact]
    public void Alt後にW押下でイベントが発火し抑制する()
    {
        using var tracker = CreateTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk);
        var suppressed = tracker.OnKeyDown(WVk);

        Assert.NotNull(fired);
        Assert.True(suppressed);
    }

    [Fact]
    public void Alt後に未登録キーはイベントを発火せず通過する()
    {
        using var tracker = CreateTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk);
        var suppressed = tracker.OnKeyDown(AVk);

        Assert.Null(fired);
        Assert.False(suppressed);
    }

    [Fact]
    public void リーダーなしにWを押してもイベントは発火しない()
    {
        using var tracker = CreateTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(WVk);

        Assert.Null(fired);
    }

    [Fact]
    public async Task タイムアウト後にWを押してもイベントが発火しない()
    {
        using var tracker = CreateTracker(timeoutMs: 50);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk);
        await Task.Delay(150); // タイムアウト待ち
        tracker.OnKeyDown(WVk);

        Assert.Null(fired);
    }

    [Fact]
    public async Task タイムアウト後は再びリーダーモードに入れる()
    {
        using var tracker = CreateTracker(timeoutMs: 50);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk);
        await Task.Delay(150);

        tracker.OnKeyDown(AltVk); // 再度リーダー
        tracker.OnKeyDown(WVk);

        Assert.NotNull(fired);
    }

    [Fact]
    public void 待機中にModifierキーを押しても状態が維持される()
    {
        using var tracker = CreateTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk);

        // Modifier キーを押しても待機解除されない
        Assert.False(tracker.OnKeyDown(0xA0)); // Shift
        Assert.False(tracker.OnKeyDown(0xA2)); // Ctrl
        Assert.False(tracker.OnKeyDown(0x5B)); // Win

        // その後 W を押すとマッチする
        tracker.OnKeyDown(WVk);

        Assert.NotNull(fired);
    }

    [Fact]
    public void ダブルプレス設定で1回だけ押してもイベントは発火しない()
    {
        using var tracker = CreateTracker(leaderCount: 2);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk); // 1回だけ → PressingLeader 状態
        tracker.OnKeyDown(WVk);   // W → シーケンスキーではなくキャンセル

        Assert.Null(fired);
    }

    [Fact]
    public void ダブルプレス設定で2回押すとシーケンス待機になる()
    {
        using var tracker = CreateTracker(leaderCount: 2);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk); // 1回目
        tracker.OnKeyDown(AltVk); // 2回目 → WaitingForSequence
        tracker.OnKeyDown(WVk);   // W → マッチ

        Assert.NotNull(fired);
    }

    [Fact]
    public void ダイレクトキーはリーダーなしで即マッチする()
    {
        var directEntry = new HotkeyEntry { Key = "F12", AppPath = @"C:\Spotify.exe" };
        var tracker = new LeaderSequenceTracker(AltVk, 2000, [(WVk, new HotkeyEntry())], directKeys: [(0x7B, directEntry)]);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        var suppressed = tracker.OnKeyDown(0x7B); // F12

        Assert.Same(directEntry, fired);
        Assert.True(suppressed);
    }

    [Fact]
    public void ダイレクトキーはリーダー待機中は発火しない()
    {
        var directEntry = new HotkeyEntry { Key = "F12", AppPath = @"C:\Spotify.exe" };
        var tracker = new LeaderSequenceTracker(AltVk, 2000, [(WVk, new HotkeyEntry())], directKeys: [(0x7B, directEntry)]);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk); // リーダー押下 → WaitingForSequence
        tracker.OnKeyDown(0x7B);  // F12 → シーケンスキーとして処理、未登録なのでスルー

        Assert.Null(fired);
    }

    [Fact]
    public void リーダーキー連打でタイマーがリセットされる()
    {
        // タイムアウトを短くして、1回目のAltから待っても2回目のAltから
        // タイムアウト以内ならWでマッチすることを確認
        using var tracker = CreateTracker(timeoutMs: 200);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk); // 1回目: 待機開始
        tracker.OnKeyDown(AltVk); // 2回目: タイマーリセット
        tracker.OnKeyDown(WVk);   // タイムアウト前のW → マッチするはず

        Assert.NotNull(fired);
    }
}
