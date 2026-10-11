# スパイク（設計書 §10）

本体の実装前に、方式の成否を確かめるために書いた使い捨てのプログラムです。Hider.slnx にも CI にも含めません（`dotnet build spikes/S-n/...` で個別にビルドします）。結果は docs/spikes.md、実機で行う手順は各フォルダの README.md にあります。

| # | 内容 | 状態 | 本体での後継 |
|---|---|---|---|
| S-1 | 枠なし・最前面・非アクティブ・透明窓 + トレイ（Windows 11） | 人間待ち | AreaWindow、TrayController |
| S-2 | S-1 + ARGB visual + 帯の透明化（Ubuntu 26.04）。S-1 のプログラムを Linux で動かす | 人間待ち | AreaWindow.SetTransparentRowsAsync |
| S-3 | SkSL のピクセル化（CPU は CI で測定済み、GPU は `--window` で人間待ち） | CPU 済み | MosaicPipeline（セル単位評価、P-1） |
| S-4 | PrintWindow の黒判定（Windows 11） | 参考実行のみ、人間待ち | Win32UnderlayCapture、UnderlayComposer |
| S-5 | Screenshot ポータルの許可の記憶 | 人間待ち | PortalScreenshotGrabber |
| S-6 | ScreenCast + PipeWire ヘルパ（native/ はヘルパの原型） | 人間待ち | native/hidercap、PipeWireGrabber、ScreenCastPortal |
| S-7 | PublishTrimmed（JSON source-gen、Tmds.DBus、ColorPicker、SkiaSharp） | 合格 | 本体の publish（CI の selftest） |
| S-8 | linux-arm64 実機で S-3（S-3 のプログラムを使う） | 人間待ち | — |

本体の実装が進んだ今も、実機での確認（S-1、S-2、S-4〜S-6、S-8、S-3 の GPU）はこれらのプログラムで行えます。全スパイクがビルドできることを 2026-10-11 に確認しました。
