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
cp /tmp/hotlaunch-publish/hotlaunch.exe /mnt/c/tools/hotlaunch/hotlaunch_new.exe && \
cp /tmp/hotlaunch-publish/*.dll /mnt/c/tools/hotlaunch/
```

出力先: `C:\tools\hotlaunch\hotlaunch_new.exe`
（既存の `hotlaunch.exe` を差し替える場合は kill してからリネーム）

> **注意**: WPF のネイティブ DLL（`PresentationNative_cor3.dll`, `wpfgfx_cor3.dll` など）は
> SingleFile に同梱されず、exe と同じディレクトリに置く必要がある。`*.dll` のコピーを忘れると
> `DllNotFoundException` でクラッシュする。
