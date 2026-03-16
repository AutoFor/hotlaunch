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
cp /tmp/hotlaunch-publish/hotlaunch.exe /mnt/c/tools/hotlaunch/hotlaunch_new.exe
```

出力先: `C:\tools\hotlaunch\hotlaunch_new.exe`
（既存の `hotlaunch.exe` を差し替える場合は kill してからリネーム）
