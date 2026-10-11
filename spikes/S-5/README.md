# S-5: Screenshot ポータルの許可の記憶（Ubuntu 26.04）

設計書 §10 S-5。合否: 2 回目以降ダイアログなし（.desktop 経由起動とターミナル起動の両方）。

## 手順

1. ターミナルから: `dotnet run --project spikes/S-5 -- 3`。初回に許可ダイアログが出たら「許可」。
2. もう一度同じコマンドを実行し、ダイアログが出ないことを確認する。
3. .desktop 経由: `dotnet publish spikes/S-5 -c Release -o ~/s5` の後、次の内容で `~/.local/share/applications/hider-s5.desktop` を作り、
   アクティビティ検索から「Hider S5」を起動する（出力は `~/s5.log`）。2 回起動する。

```ini
[Desktop Entry]
Type=Application
Name=Hider S5
Exec=sh -c "$HOME/s5/S5.PortalScreenshot 3 > $HOME/s5.log 2>&1"
Terminal=false
```

4. 終わったら `~/.local/share/applications/hider-s5.desktop` と `~/s5` を削除する。

## 返してほしい情報

- 各実行の出力（`#n: response=... ms image=...`）と、ダイアログが出た回
- `flatpak permission-list screenshot screenshot`（または `flatpak permissions`）の出力
