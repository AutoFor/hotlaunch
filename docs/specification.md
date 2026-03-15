# hotlaunch 仕様書

## 概要

Windows 常駐型のグローバルホットキーランチャー。
任意のショートカットキーを登録しておくと、どのウィンドウがフォーカスされていても指定アプリを起動する。

---

## 技術スタック

| 項目 | 選択 |
|------|------|
| 言語 | C# (.NET 8) |
| UI | WinForms |
| 常駐方式 | システムトレイ (NotifyIcon) |
| ホットキー | Win32 API `RegisterHotKey` (P/Invoke) |
| 設定ファイル | JSON (`~/.hotlaunch/config.json`) |

---

## 機能一覧

### MVP（最初に作る）

- [ ] システムトレイに常駐
- [ ] グローバルホットキーの登録・解除
- [ ] ホットキーに対してアプリのパスを紐づけ
- [ ] ホットキー押下でアプリ起動
- [ ] 設定の JSON 読み込み・保存
- [ ] トレイアイコン右クリックメニュー（終了 / 設定を開く）

### 将来的な拡張（やるかも）

- [ ] GUI での設定編集画面
- [ ] アプリが既に起動中の場合はウィンドウをフォアグラウンドに持ってくる
- [ ] 起動引数の指定
- [ ] 複数プロファイル切り替え
- [ ] Windows スタートアップ登録

---

## 設定ファイル仕様

パス: `%USERPROFILE%\.hotlaunch\config.json`

```json
{
  "hotkeys": [
    {
      "modifiers": ["Ctrl", "Alt"],
      "key": "B",
      "appPath": "C:\\Users\\you\\AppData\\Local\\Programs\\cursor\\Cursor.exe",
      "args": ""
    },
    {
      "modifiers": ["Ctrl", "Alt"],
      "key": "T",
      "appPath": "wt.exe",
      "args": ""
    }
  ]
}
```

### modifiers に指定できる値

| 値 | Win32定数 |
|----|----------|
| `"Alt"` | `MOD_ALT (0x0001)` |
| `"Ctrl"` | `MOD_CONTROL (0x0002)` |
| `"Shift"` | `MOD_SHIFT (0x0004)` |
| `"Win"` | `MOD_WIN (0x0008)` |

---

## アーキテクチャ

```
hotlaunch/
├── src/
│   └── Hotlaunch/
│       ├── Program.cs              # エントリーポイント
│       ├── TrayApp.cs              # システムトレイ・メインループ
│       ├── HotkeyManager.cs        # RegisterHotKey / UnregisterHotKey 管理
│       ├── AppLauncher.cs          # アプリ起動ロジック
│       └── Config/
│           ├── ConfigManager.cs    # JSON読み書き
│           └── HotkeyConfig.cs     # 設定モデル
├── docs/
│   └── specification.md           # この仕様書
└── README.md
```

---

## 主要実装メモ

### グローバルホットキー登録 (P/Invoke)

```csharp
[DllImport("user32.dll")]
private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll")]
private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
```

- `WM_HOTKEY` メッセージ (`0x0312`) を `WndProc` でキャッチして起動処理を呼ぶ
- 同じキーの二重登録はエラーになるため、起動時に既存登録を解除してから再登録する

### WinForms での常駐（ウィンドウ非表示）

```csharp
// Program.cs
Application.Run(new TrayApp()); // Form を表示しない ApplicationContext を使う
```

`ApplicationContext` を継承したクラスを使い、メインウィンドウを持たずにトレイだけで動かす。

---

## 開発ロードマップ

1. プロジェクト初期化 (`dotnet new winforms`)
2. システムトレイ常駐の骨格実装
3. JSON 設定読み込み
4. `RegisterHotKey` でホットキー登録
5. ホットキー押下でアプリ起動
6. トレイメニュー（終了）
7. README 整備・リリース
