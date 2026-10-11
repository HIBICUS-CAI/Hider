# S-7: PublishTrimmed（System.Text.Json source-gen + Tmds.DBus.Protocol + ColorPicker + SkiaSharp 3）

設計書 §10 S-7。合否: 3 ターゲットで起動・保存・復元が通る。実装担当が CI で実行する。

```sh
dotnet publish spikes/S-7 -c Release -r <rid> -o out/s7
dbus-run-session -- out/s7/S7.Trim     # Linux（セッションバスが必要）
out/s7/S7.Trim.exe                     # Windows（D-Bus の検査は行わない）
```

検査: JSON ソース生成でファイルへ保存・復元、SkSL のコンパイル、セッションバスへの GetId 呼び出し（Linux）、
FluentTheme + ColorPicker を Avalonia headless（Skia 描画）で描いて画素が空でないこと。
