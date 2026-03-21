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
        Assert.True(tracker.OnKeyDown(AltVk).Block);
    }

    [Fact]
    public void Alt後にW押下でイベントが発火し抑制する()
    {
        using var tracker = CreateTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk);
        var result = tracker.OnKeyDown(WVk);

        Assert.NotNull(fired);
        Assert.True(result.Block);
    }

    [Fact]
    public void Alt後に未登録キーはイベントを発火せずリーダーとキーを再注入する()
    {
        using var tracker = CreateTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(AltVk);
        var result = tracker.OnKeyDown(AVk);

        Assert.Null(fired);
        // 元キーはブロックし、リーダー→元キーの順で再注入する
        Assert.True(result.Block);
        Assert.Equal(2, result.Inject.Count);
        Assert.Equal((AltVk, false), result.Inject[0]);
        Assert.Equal((AVk, false), result.Inject[1]);
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
        Assert.False(tracker.OnKeyDown(0xA0).Block); // Shift
        Assert.False(tracker.OnKeyDown(0xA2).Block); // Ctrl
        Assert.False(tracker.OnKeyDown(0x5B).Block); // Win

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

    // ---- チャードリーダーモード (無変換+Space) のテスト ----

    private const int SpaceVk    = 0x20;
    private const int MuhenkanVk = 0x1D;

    // テストは瞬時実行なので chordDelayMs=0 をデフォルトにする
    private static LeaderSequenceTracker CreateChordTracker(int timeoutMs = 2000, int chordDelayMs = 0)
    {
        var entry = new HotkeyEntry { Key = "W", AppPath = @"C:\wezterm-gui.exe", ProcessName = "wezterm-gui" };
        return new LeaderSequenceTracker(MuhenkanVk, timeoutMs, [(WVk, entry)], chordVk: SpaceVk, chordDelayMs: chordDelayMs);
    }

    [Fact]
    public void チャード_無変換押下はブロックしIsHoldingModifierになる()
    {
        using var tracker = CreateChordTracker();
        var result = tracker.OnKeyDown(MuhenkanVk);
        Assert.True(result.Block);
        Assert.True(tracker.IsHoldingModifier);
    }

    [Fact]
    public void チャード_無変換Space押下でWaitingForSequenceになる()
    {
        using var tracker = CreateChordTracker();
        tracker.OnKeyDown(MuhenkanVk);
        var result = tracker.OnKeyDown(SpaceVk);
        Assert.True(result.Block);
        Assert.True(tracker.IsWaitingForSequence);
    }

    [Fact]
    public void チャード_同時押しはリマッパー経由で素通しする()
    {
        // ChordDelayMs=200 で、即座に Space を押した場合は親指シフト扱い
        using var tracker = CreateChordTracker(chordDelayMs: 200);
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(MuhenkanVk);
        // すぐに Space → 同時押し扱い（経過時間ほぼ 0ms < 200ms）
        var result = tracker.OnKeyDown(SpaceVk);

        Assert.Null(fired);
        Assert.True(result.Block);
        Assert.False(tracker.IsWaitingForSequence);
        // 両キーをリマッパーを通さず素通し注入（Ctrl+Space にならないよう）
        Assert.Equal(2, result.Inject.Count);
        Assert.Equal((MuhenkanVk, false), result.Inject[0]);
        Assert.Equal((SpaceVk,    false), result.Inject[1]);
    }

    [Fact]
    public void チャード_無変換Space後にWでイベント発火する()
    {
        using var tracker = CreateChordTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(MuhenkanVk);
        tracker.OnKeyDown(SpaceVk);
        var result = tracker.OnKeyDown(WVk);

        Assert.NotNull(fired);
        Assert.True(result.Block);
    }

    [Fact]
    public void チャード_無変換後に別キーでキャンセルしリマッパー経由再注入する()
    {
        using var tracker = CreateChordTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(MuhenkanVk);
        var result = tracker.OnKeyDown(0x43); // C キー

        Assert.Null(fired);
        Assert.True(result.Block);
        // 無変換+C をリマッパー経由で再注入（Ctrl+C になる）
        Assert.Equal(2, result.InjectForRemapper.Count);
        Assert.Equal((MuhenkanVk, false), result.InjectForRemapper[0]);
        Assert.Equal((0x43, false), result.InjectForRemapper[1]);
    }

    [Fact]
    public void チャード_無変換解放でHoldingModifierからIdleに戻る()
    {
        using var tracker = CreateChordTracker();
        tracker.OnKeyDown(MuhenkanVk);
        Assert.True(tracker.IsHoldingModifier);

        bool detected = tracker.OnKeyUp(MuhenkanVk);
        Assert.True(detected); // リーダー解放を検出
        Assert.False(tracker.IsHoldingModifier);
        Assert.False(tracker.IsWaitingForSequence);
        // ソロタップ再注入は KeyboardHook 側（IsSourceKey で判断）が担当
    }

    [Fact]
    public void チャード_WaitingForSequence中の未登録キーはリーダーチャード元キーを再注入する()
    {
        using var tracker = CreateChordTracker();
        HotkeyEntry? fired = null;
        tracker.SequenceMatched += e => fired = e;

        tracker.OnKeyDown(MuhenkanVk);
        tracker.OnKeyDown(SpaceVk);
        var result = tracker.OnKeyDown(AVk); // 未登録キー

        Assert.Null(fired);
        Assert.True(result.Block);
        Assert.Equal(3, result.Inject.Count);
        Assert.Equal((MuhenkanVk, false), result.Inject[0]);
        Assert.Equal((SpaceVk,    false), result.Inject[1]);
        Assert.Equal((AVk,        false), result.Inject[2]);
    }
}
