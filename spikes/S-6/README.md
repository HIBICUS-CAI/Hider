# S-6: ScreenCast + PipeWire ヘルパ（Ubuntu 26.04）

設計書 §10 S-6。合否: 帯 1 本 ≤ 3 フレーム。座標ずれ 0 px。restore_token で 2 回目以降ダイアログなし。

ヘルパは Hider が同梱する `native/hidercap`（libhidercap.so）をそのまま使う（原型は Step 8 で本番に移し、削除した）。
プログラムは最前面の 240×240 窓の色を 20 回切り替え、描画完了の直後に「PTS がそれより後のフレーム」をヘルパに要求して、
(1) 何フレーム待ったか、(2) フレームに新しい色が写っているか、(3) 写った位置と窓の位置のずれ、を出す。
これは Linux の帯方式（帯を透明化 → 描画完了 → それより後のフレームを 1 枚取る）と同じ手順。

## 準備

```sh
sudo apt install build-essential libpipewire-0.3-dev
make -C native/hidercap
```

## 手順

1. `dotnet run --project spikes/S-6` → 画面共有の許可ダイアログでモニタを選び「共有」。窓が出て 20 回色が変わり、自動で閉じる。
2. もう一度実行し、ダイアログが出ないことを確認する（`~/.hider-spike-s6/restore_token` を使う）。
3. 設定 → ディスプレイで 125% と 150% にして 1 と同じことを行う。
4. 終わったら `~/.hider-spike-s6/` を削除する。

## 返してほしい情報

- 各実行の出力全文（`portal:`、`x11 screen:`、`#n:`、`summary:` の行）
- 2 回目にダイアログが出たかどうか
