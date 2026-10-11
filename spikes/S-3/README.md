# S-3: SKRuntimeEffect によるピクセル化（GPU / Software）

設計書 §10 S-3。合否: 両モードで同じ結果。1080p で GPU < 2 ms、CPU < 30 ms。

## 実行

```sh
dotnet run --project spikes/S-3 -c Release              # CPU（実装担当が CI / headless で実行済み）
dotnet run --project spikes/S-3 -c Release -- --window  # Avalonia の窓で GPU（または Software）経路も測る
```

CPU モードは「全解像度でシェーダを評価」と「セル単位で評価して最近傍拡大」の 2 通りと、色数テーブル・ぼかしを測る。
`--window` は Avalonia の描画コンテキスト（ISkiaSharpApiLeaseFeature）上で同じシェーダを 1920×1080 のオフスクリーンに描き、
`gpu=True/False`、1 フレームの時間、CPU 結果との最大差を出す。

## 人間に返してほしい情報（--window、Windows 11 と Ubuntu 26.04）

- 出力の `window: gpu=... ms/frame, max diff ...` の行と、CPU モードの全行
- 機材（CPU / GPU / ドライバ）
