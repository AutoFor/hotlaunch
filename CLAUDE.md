# hotlaunch プロジェクトルール

## ビルド・パブリッシュ時の注意

`dotnet publish` や `dotnet build` で以下のエラーが出た場合、`hotlaunch.exe` が起動中でファイルがロックされている：

```
Access to the path '...\hotlaunch.exe' is denied.
System.UnauthorizedAccessException
```

この場合は `/hotlaunch-kill` を実行してからビルドを再試行すること。

## パブリッシュコマンド

```bash
dotnet publish src/Hotlaunch/Hotlaunch.csproj \
  -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true \
  -o /tmp/hotlaunch-publish && \
cp /tmp/hotlaunch-publish/hotlaunch.exe /mnt/c/Prog/hotlaunch/hotlaunch_new.exe && \
cp /tmp/hotlaunch-publish/*.dll /mnt/c/Prog/hotlaunch/
```

出力先: `C:\Prog\hotlaunch\hotlaunch_new.exe`
（既存の `hotlaunch.exe` を差し替える場合は kill してからリネーム）

> **注意**: WPF のネイティブ DLL（`PresentationNative_cor3.dll`, `wpfgfx_cor3.dll` など）は
> SingleFile に同梱されず、exe と同じディレクトリに置く必要がある。`*.dll` のコピーを忘れると
> `DllNotFoundException` でクラッシュする。

---

## アーキテクチャ上の注意点

### LeaderSequenceTracker の状態遷移

`LeaderActivated` / `LeaderDeactivated` イベント（トレイアイコンの色変化）は **`WaitingForSequence` との境界でのみ発火する**。`PressingLeader` や `HoldingModifier` は中間状態なので発火しない。これを `TransitionTo` の外で勝手に呼ばないこと。

### チャードリーダーモード（無変換+Space など）

`LeaderConfig.ChordKey` を指定するとコードリーダーになる。状態遷移：

```
Idle → HoldingModifier（修飾キー押下）→ WaitingForSequence（チャードキー押下）→ Idle
```

- **HoldingModifier 中に修飾キーが単独解放**された場合（ソロタップ）：`OnKeyUp` は `Inject=[leaderVk↓, leaderVk↑]` を返してシステムに素通しする。こうしないと IME キーが効かなくなる。
- **HoldingModifier 中にチャード以外のキーが押された**場合：`InjectForRemapper=[leaderVk↓, comboKey↓]` を返してリマッパー経由で再注入する（Ctrl+C 等が維持される）。
- `KeyboardHook` の `[2c]` セクションで `_tracker.OnKeyUp()` の**戻り値を使うこと**。無視すると Inject が送られず IME キーが詰まる。

### NonComboKeys（IME キー）

`ModifierRemapper.NonComboKeys` = `{0x1C 変換, 0x1D 無変換, 0x15 カナ}`。これらはリマッパーのコンボ対象外。`DispatchInjectForRemapper` はリストの末尾キーで `IsComboTarget` を確認し、IME キーなら `SendKeys`（素通し）、通常キーなら `SendKeysForRemapper`（Ctrl 等にマップ）に振り分ける。

### リーダーキーと ModifierRemapper の競合

リーダーキーが ModifierRemapper のソースキーでもある場合（例: 無変換→Ctrl のリマップ ＋ 無変換 をリーダーとして使用）、リマッパーがリーダーキーを横取りしてしまう。`HookCallback` の `[2]` セクションでリーダーキーをトラッカーが先に処理し、ブロック時は `SuppressNextKeyUp` でキーアップの誤注入を防ぐ。
