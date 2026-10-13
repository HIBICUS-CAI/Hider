# スパイク結果（設計書 §10）

スパイクのプログラムと手順（`spikes/S-n/`）は PR #1 の終わりに削除した。必要なときはブランチ `archive/pr1-removed`（3bdd9581495d41cb3c20cb0dc30be2f0e4524050）から取り出す（`git fetch origin archive/pr1-removed` の後、`git show FETCH_HEAD:spikes/S-1/README.md`、`git archive FETCH_HEAD spikes | tar -x`）。結果はこの文書に残す。

**状況（2026-10-12）**: 依頼者の判断で、v2.0 では Windows・Linux とも実機での検証・計測を行わない（提案 P-3 承認、docs/manual-tests.md）。以下の実機が必要なスパイク（S-1、S-2、S-4 の実機分、S-5、S-6、S-8、S-3 の GPU）は未実施のまま残し、CI・作業コンテナで測れた値だけを載せている。

## 結果一覧

| # | 検証 | 日付 | 機材 | OS 版 | 結果 | 実測値 |
|---|---|---|---|---|---|---|
| S-1 | Win11: 枠なし・Topmost・非アクティブ・透明窓 + TrayIcon 左右クリック | — | — | — | 未実施（デスクトップが必要。v2.0 では実施しない（P-3）。手順は archive/pr1-removed の spikes/S-1/README.md） | — |
| S-2 | Ubuntu 26.04: 同上 + ARGB visual の GLX 描画 + 帯の透明化 | — | — | — | 未実施（GNOME 50 実機が必要。v2.0 では実施しない（P-3）。手順は archive/pr1-removed の spikes/S-2/README.md） | — |
| S-3 | SKRuntimeEffect のピクセル化（GPU / Software） | 2026-10-11 | GitHub Actions ランナー 3 種（4 コア）、作業コンテナ（Xeon 2.8 GHz 4 コア） | Windows Server 2025（10.0.26100）、Ubuntu 24.04（x64 / arm64） | CPU: **条件付き合格**（全解像度評価は不合格、セル単位評価なら合格）。GPU: 未実施（実機の GPU が必要、v2.0 では実施しない（P-3）） | 下表「S-3 CPU 実測」 |
| S-4 | PrintWindow(PW_RENDERFULLCONTENT) の黒判定 | 2026-10-11（参考） | GitHub Actions windows-latest（GPU なし、1024×768） | Windows 10.0.26100 | 参考実行のみ（実機の Edge / VS Code / UWP / 管理者窓 / 排他全画面は v2.0 では実施しない（P-3）。手順は archive/pr1-removed の spikes/S-4/README.md） | 下表「S-4 参考実行」 |
| S-5 | Screenshot ポータルの許可の記憶 | — | — | — | 未実施（GNOME 50 実機が必要。v2.0 では実施しない（P-3）。手順は archive/pr1-removed の spikes/S-5/README.md） | — |
| S-6 | ScreenCast + PipeWire ヘルパ: restore_token、PTS 判定、帯 1 本のフレーム数、座標対応 | — | — | — | 未実施（GNOME 50 実機が必要。v2.0 では実施しない（P-3）。手順は archive/pr1-removed の spikes/S-6/README.md） | — |
| S-7 | PublishTrimmed: JSON source-gen + Tmds.DBus.Protocol + ColorPicker + SkiaSharp 3 | 2026-10-11 | GitHub Actions ランナー 3 種、作業コンテナ | Windows 10.0.26100、Ubuntu 24.04（x64 / arm64） | **合格**（3 RID とも起動・保存・復元・D-Bus 呼び出し・ColorPicker 描画が通る） | 下表「S-7」 |
| S-8 | linux-arm64 実機で S-3 再測定 | — | — | — | 未実施（実機は v2.0 では測らない（P-3）。CI の arm64 ランナーでの CPU 値は S-3 に記載） | — |

## S-3 CPU 実測（1920×1080、ブロック 12、単一スレッド、30 フレーム平均）

| 環境 | 全解像度シェーダ | セル単位評価 + 最近傍拡大 | 色数テーブル（全解像度） | ぼかし r=8 | 全解像度とセル単位の差 |
|---|---|---|---|---|---|
| windows-latest（x64） | 60.2 ms | **1.9 ms** | 19.2 ms | 2.2 ms | 0/255 |
| ubuntu-24.04（x64） | 118.0 ms | **2.3 ms** | 35.2 ms | 2.0 ms | 0/255 |
| ubuntu-24.04-arm（arm64） | 38.2 ms | **2.4 ms** | 18.1 ms | 4.4 ms | 0/255 |
| 作業コンテナ（x64） | 215.1 ms | **4.6 ms** | 64.6 ms | 4.3 ms | 0/255 |

所見:

- SkSL を CPU（Skia のラスタパイプライン）で全画素評価すると 1 画素あたり約 20〜100 ns（1080p の 38〜215 ms を画素数で割った値）かかり、設計書 §4.4 の見積（CPU 5〜10 ms / 800×450）を大きく超える。定数色を返すだけのシェーダでも 1080p で 64 ms（作業コンテナ）。
- 出力がセル単位で一定の手法（ピクセル化）は、セル数ぶんの縮小面でシェーダを評価して最近傍で拡大すると全解像度評価と画素単位で一致し、2 ms 前後になる。
- 色数制限もセル単位の縮小面に掛ければ同じ結果になる（出力がセル単位で一定のため）。
- 提案 P-1（archive/pr1-removed の docs/progress.md、Step 1）として、パイプラインに「セル単位評価」と「結果のキャッシュ」を入れる。

## S-4 参考実行（windows-latest、管理者、GPU なし）

| z | class | process | black | ms |
|---|---|---|---|---|
| 5 | Shell_TrayWnd | explorer | no | 32.9 |
| 10 | CASCADIA_HOSTING_WINDOW_CLASS | WindowsTerminal | no | 29.5 |
| 47 | Progman | explorer | no | 30.7 |

所見: GPU なしの仮想環境では 1 窓あたり約 30 ms（窓の全サイズを複写するため）。実機（GPU あり）の値が NFR-04 の判定材料になるが、v2.0 では測らない（P-3）。実機で 1 窓 10 ms を超えるなら D-7（Windows.Graphics.Capture への昇格）の条件に近づく。

## S-7（トリミング後の単一ファイル）

| RID | JSON 保存・復元 | SkSL | D-Bus GetId | ColorPicker 描画（headless Skia） |
|---|---|---|---|---|
| win-x64 | ok | ok | 対象外（Windows は D-Bus を使わない） | ok（1684 色） |
| linux-x64 | ok | ok | ok | ok（1684 色） |
| linux-arm64 | ok | ok | ok | ok（1691 色） |

所見: `StyleInclude` をコードで生成すると IL2026（トリミング非安全）になる。XAML に書けばコンパイル時に解決され安全。本体は ColorPicker のテーマを App の XAML で読み込む。

## 仕様書 §11（要検証項目）との対応

| §11 | 項目 | スパイク | 状態 |
|---|---|---|---|
| 1 | PrintWindow(PW_RENDERFULLCONTENT) | S-4 | 参考実行のみ（実機は v2.0 では確認しない、P-3） |
| 2 | Avalonia TrayIcon のクリック | S-1 | 未実施 |
| 3 | BeginMoveDrag / BeginResizeDrag | — | 使用しない（指示書 §2.2 で禁止、自前ドラッグ） |
| 4 | ピクセル単位アルファ（XWayland） | S-2 | 実機未実施。Xvfb + xcompmgr では帯の行がアルファ 0 になり下が写ることを確認（scripts/xvfb-strip-smoke.sh、CI） |
| 5 | 最前面（Mutter / XWayland） | S-1, S-2 | 未実施 |
| 6 | ポータル（Screenshot の許可、ScreenCast の restore_token） | S-5, S-6 | 実装済み（Step 7、8）。偽ポータルで呼び出し・restore_token の受け渡しを確認。GNOME の許可の記憶は実機で確認していない（P-3） |
| 7 | PipeWire の PTS 判定と帯 1 本のフレーム数 | S-6 | 実装済み（Step 8）。専用 PipeWire + GStreamer で通し確認（PTS 無しの受信時刻判定で 2 フレーム）。Mutter の PTS は実機で確認していない（P-3） |
| 8 | 座標系（分数スケーリング） | S-2, S-6 | 倍率・オフセットの写像は実装・テスト済み（CT-88、MapStream）。実機の座標は未確認 |
| 9 | トリミング（System.Text.Json source-gen、ColorPicker） | S-7 | 検証済み |
| 10 | 性能（PrintWindow、生成パターンの再生成時間） | S-3, S-4, S-8 | CPU 側のみ測定済み（GPU・実機は v2.0 では測らない、P-3） |
| 11 | セッション終了時の保存 | PT-08 | 都度保存（FR-61）と終了時保存を実装済み（Step 3）。実機の PT-08 は v2.0 では実施しない（P-3） |
