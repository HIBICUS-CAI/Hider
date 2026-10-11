# 進捗報告

作業指示書 §5 の形式（結果 / 完了条件の達成表 / 変更 / テスト / 性能 / 提案 / 人間への依頼 / 次 Step の前提）で Step ごとに追記する。

## Step 0: リポジトリ初期化（2026-10-11）

### 1. 結果

完了。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| `dotnet build -warnaserror` が通る | 達成 | CI 3 ランナーの test-core / test-gui で `dotnet build -c Release --no-restore -warnaserror` 成功 |
| 各テストプロジェクトに 1 件以上のテストがあり緑 | 達成 | CT93_SelfTest_Passes、GT39_HeadlessSkia_RendersPixels が 3 OS で pass |
| `python scripts/build.py` がホスト RID の実行ファイルを出し、`--selftest` が 0 | 達成 | ローカル linux-x64、CI publish（win-x64 / linux-x64 / linux-arm64）で `--version` → `Hider 2.0.0`、`--selftest` → `selftest passed` |
| CI を 1 回手動実行して全ジョブ緑 | 達成 | https://github.com/HIBICUS-CAI/Hider/actions/runs/38160619753 （12 ジョブ全 success、約 3 分） |

### 3. 変更

| コミット | 内容 |
|---|---|
| df80614 | .github/workflows/ci.yml（main にも同一コミットを配置） |
| 3d78375 / 366e01f | .gitignore / .editorconfig |
| 0fc11c3 / 243a344 / f09095b | global.json / Directory.Build.props（+ src/Directory.Build.props）/ Directory.Packages.props |
| 93e0d28 / c586a42 / a034443 / 456dfad | src/Hider.Core、src/Hider（現行コードを移植、MainWindow 削除）、`--version` / `--selftest`、App → HiderApp 改名 |
| 7a87bf0 / 968910e | tests/Hider.Core.Tests、tests/Hider.App.Tests |
| 166fd1e / 95f1428 / 7d1fa4e / 8b95a06 | RID 指定 publish の locked mode 対応、Hider.sln、packages.lock.json、library-packs 無効化 |
| 80ca8ab / 0d9902f / f7608e6 | native/hidercap、scripts/、build.py のヘルパビルド組込み |
| 9ef8eb6 / 57bb4a6 | docs/v2-implementation-plan.md、CLAUDE.md |

新規依存（すべて許可リスト内）: Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent / Avalonia.Skia / Avalonia.Headless.XUnit 12.1.4、SkiaSharp 3.119.4、SkiaSharp.NativeAssets.Linux 3.119.4（テストのみ。Hider.Core はマネージド SkiaSharp だけを参照するため）、xunit.v3 3.2.2、xunit.runner.visualstudio 3.1.5、Microsoft.NET.Test.Sdk 18.10.1。Avalonia.Controls.ColorPicker と Tmds.DBus.Protocol は版のみ登録（使用は Step 5 / 7）。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 1 / 0 / 0 | 1 / 0 / 0 |
| linux-x64 | 1 / 0 / 0 | 1 / 0 / 0 |
| linux-arm64 | 1 / 0 / 0 | 1 / 0 / 0 |

追加: CT93_SelfTest_Passes、GT39_HeadlessSkia_RendersPixels（テストタブの既存 ID と重ならないよう末尾番号を使用）。

### 5. 性能

対象外。

### 6. 提案

なし（判断した事項は下記「判断メモ」）。

判断メモ:

- workflow_dispatch は既定ブランチにワークフローが必要なため、ci.yml を追加する 1 コミットだけ main に置いた（許可済み）。作業ブランチはその上に積んでいる。
- アクションは実装時点の最新メジャー（checkout@v7、setup-dotnet@v6、setup-python@v7、upload-artifact@v7）に上げた。Actions タブの「実装時点の最新を確認してから固定する」に従った。
- `App` クラスを `HiderApp` に改名: テスト名前空間 `Hider.App.Tests`（指示書 §3.2）が型 `Hider.App` と衝突するため、契約外のクラス名の方を変えた。
- SelfContained のみ RID 指定時に限定し、PublishTrimmed は無条件のまま: RID なしの `dotnet build` / `dotnet test` を通しつつ、ロックファイルが RID で変わらないようにするため。RuntimeIdentifiers は src/ 全体に設定（publish -r が参照先にも RID を渡すため）。
- ネイティブヘルパは「実行ファイルの隣」に同梱（設計書 §8 の runtimes/<rid>/native/ は RID 別資産の意と解釈）。クロスツールチェーンが無い場合は警告して省略（実行時に任意、D-6）。
- Fakes は Core.Tests から App.Tests へソースリンクで共有（テスト exe 同士の参照は xunit.v3 のエントリポイントが衝突するため）。
- 作業コンテナの .NET SDK は Ubuntu 版 10.0.112（Microsoft の配布元がプロキシで遮断されていたため）。CI は Microsoft 版 10.0.401。両者でロックファイルが一致するよう `DisableImplicitLibraryPacksFolder` を設定した。

### 7. 人間への依頼

なし。

### 8. 次 Step の前提

Step 1（スパイク）に進む。S-3（Skia シェーダ）と S-7（トリミング）は実装担当が CI / headless で実行し、S-1・S-2・S-4・S-5・S-6・S-8 はプログラムと手順を用意して人間に依頼する。
