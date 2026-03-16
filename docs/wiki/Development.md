# 開発者ガイド

## ビルド方法

```bash
# WSL から Windows 向けにビルド（テスト付き）
dotnet test src/Hotlaunch.Tests/Hotlaunch.Tests.csproj

# リリースビルド & パブリッシュ
dotnet publish src/Hotlaunch/Hotlaunch.csproj \
  -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o /tmp/hotlaunch-publish
```

## デプロイ方法

1. `hotlaunch.exe` が起動中なら先に停止する（`/hotlaunch-kill` スキル）
2. `hotlaunch.exe` を `C:\tools\hotlaunch\` へコピー
3. `C:\tools\hotlaunch\hotlaunch.exe` を起動

---

## テスト戦略

### テスト不可ゾーン / テスト可能ゾーン

```
┌──────────────────────────────────────────────────────┐
│  テスト可能ゾーン（Hotlaunch.Core）                    │
│                                                      │
│  LeaderSequenceTracker  純粋ロジック、直接テスト        │
│  AppLauncher            インターフェース経由でモック     │
│  ConfigManager          インスタンス化・IConfigManager化済み │
└──────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────┐
│  テスト不可ゾーン（Win32 / WinForms）                  │
│                                                      │
│  Win32WindowFocuser  AttachThreadInput など           │
│  Win32ProcessFinder  Process.GetProcessesByName      │
│  KeyboardHook        WH_KEYBOARD_LL                  │
│  TrayApp             WinForms                        │
└──────────────────────────────────────────────────────┘
```

Win32 層は **インターフェースで抽象化**することでテスト可能ゾーンから切り離しています。

### 現在のテスト一覧

#### `AppLauncherTests`（5テスト）

| テスト名 | 検証内容 |
|---------|---------|
| 起動済みのとき_フォーカスする | `IWindowFocuser.Focus(restore: false)` が呼ばれる |
| 最小化されているとき_復元してフォーカスする | `IWindowFocuser.Focus(restore: true)` が呼ばれる |
| 未起動のとき_新規起動する | `IProcessStarter.Start()` が呼ばれる |
| ProcessName未指定のとき_AppPathのファイル名を使う | `IProcessFinder.FindByName("wezterm-gui")` が呼ばれる |
| Args付きエントリで未起動のとき_ArgsをStartに渡す | `Start(path, "--new-window")` が呼ばれる |

#### `LeaderSequenceTrackerTests`（10テスト）

| テスト名 | 検証内容 |
|---------|---------|
| Alt押下はキーを抑制する | リーダーキー押下で `true` を返す |
| Alt後にW押下でイベントが発火し抑制する | シーケンスマッチで `SequenceMatched` が発火し `true` を返す |
| Alt後に未登録キーはイベントを発火せず通過する | 未登録キーで `false` を返す |
| リーダーなしにWを押してもイベントは発火しない | Idle 状態では発火しない |
| タイムアウト後にWを押してもイベントが発火しない | タイムアウト後は Idle に戻る |
| タイムアウト後は再びリーダーモードに入れる | タイムアウト後に再シーケンス可能 |
| 待機中にModifierキーを押しても状態が維持される | Shift/Ctrl/Win を押しても待機継続 |
| リーダーキー連打でタイマーがリセットされる | 連打後のW でマッチする |
| ダブルプレス設定で1回だけ押してもイベントは発火しない | `count=2` で1回押しはシーケンス移行しない |
| ダブルプレス設定で2回押すとシーケンス待機になる | `count=2` で2回押し後にW でマッチ |

#### `ConfigManagerTests`（3テスト）

| テスト名 | 検証内容 |
|---------|---------|
| 設定ファイルが存在しないときデフォルト設定を返す | F12 + W→WezTerm のデフォルトが返る |
| 保存した設定を読み込むとラウンドトリップできる | Save → Load で内容が一致する |
| 不正なJSONのときデフォルト設定にフォールバックする | 壊れた JSON でもクラッシュしない |

### テストカバレッジ

現在 **18テスト**（全てグリーン）。主要なビジネスロジックはカバー済み。

---

## リファクタリング戦略

### 原則

**テストがグリーンな状態を維持しながら少しずつ変える。**

```
1. dotnet test → グリーン確認（ベースライン）
2. テストを追加して網羅を上げる
3. リファクタリング実施
4. dotnet test → グリーン確認
```

### 優先度別タスク

#### 優先度 高：動作に影響するリスクがある

| タスク | 理由 |
|-------|------|
| `ConfigManager` にテストを追加 | ファイルI/O処理は変更時に壊れやすい |
| `LeaderSequenceTracker` の Modifier キーテスト追加 | 今後キー追加時の回帰を防ぐ |

#### 優先度 中：コードの明確化（完了済み）

| タスク | 内容 | 状態 |
|-------|------|------|
| `TrayApp` の組み立て責務を分離 | `HotlaunchFactory` に切り出し済み | ✅ |
| `Win32WindowFocuser` の `AttachThreadInput` ロジックをメソッド分割 | `WithAttachedInput()` として切り出し済み | ✅ |
| `ConfigManager` を `IConfigManager` インターフェース化 | インスタンスクラス + インターフェース化済み | ✅ |

#### 優先度 低：将来の拡張に備える

| タスク | 内容 |
|-------|------|
| ファイルシステム抽象化（`IFileSystem`） | `ConfigManager` テストの簡略化 |
| `AppLauncher` のロガー注入 | テストでログ出力を検証可能にする |

### リファクタリング禁止事項

- `Hotlaunch.Core` と `Hotlaunch`（Win32層）の境界を崩さない
- テストなしで Win32 P/Invoke の実装を変えない（手動確認が困難）
- `LeaderSequenceTracker` のロック処理を不用意に変更しない（競合状態のリスク）

---

## トラブルシューティング

| 症状 | 原因 | 対処 |
|------|------|------|
| ビルドが `Access denied` で失敗 | `hotlaunch.exe` が実行中 | `/hotlaunch-kill` スキルで停止 |
| キーが反応しない | フック未登録 | ログで `キーボードフック登録完了` を確認 |
| タスクバーが点滅してフォーカスされない | `SetForegroundWindow` の制限 | `AttachThreadInput` で回避済み（`Win32WindowFocuser`） |
| F6〜F10 が反応しない | 日本語 IME が横取り | リーダーキーを F12 に変更（`config.json` の `leader.key`） |
