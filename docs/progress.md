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

## Step 1: スパイク（2026-10-11）

### 1. 結果

完了（完成条件「docs/spikes.md に 8 件全ての結果」を満たす）。実装担当が実行できる S-3（CPU）と S-7 は実施済み。S-1・S-2・S-4（実機）・S-5・S-6・S-8 はプログラムと手順を用意し、人間の実行待ち。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| spikes/<S-n>/ に独立した最小プロジェクトと実行手順 | 達成 | spikes/S-1〜S-8（S-2 は S-1 のプログラムを `--strip` で使う、S-8 は S-3 を使う） |
| S-3・S-7 を実装担当が CI・headless で実行 | 達成 | 一時ワークフロー（実行後に削除）の run 38161677618、3 ランナーとも success |
| docs/spikes.md に 8 件全ての結果（未実施は理由） | 達成 | docs/spikes.md |

### 3. 変更

cb3de1f（spikes 共通設定）、0c70d64（S-1/S-2）、599229a（S-3）、661f846（S-4）、826a44b（S-5）、a4c6613（S-7）、7589d93（S-8）、f897cc5（.gitignore）、7f6e46b（S-6）、本報告と docs/spikes.md。新規依存なし（S-7 のみスパイク内で Avalonia.Headless 12.1.4 を VersionOverride で参照）。

### 4. テスト

本体のテストは変更なし。スパイク実行結果は docs/spikes.md。

### 5. 性能

S-3 CPU（1080p、ブロック 12）: 全解像度シェーダ 38〜215 ms、セル単位評価 1.9〜4.6 ms（全解像度と画素一致）。S-4 参考: GPU なしの仮想環境で PrintWindow 1 窓約 30 ms。詳細は docs/spikes.md。

### 6. 提案

**P-1: CPU 描画経路の「セル単位評価」と「描画結果のキャッシュ」**

- 問題: SkSL を CPU で全画素評価すると 1080p で 38〜215 ms（S-3 の CPU 基準 30 ms を満たさない）。設計書 §4.4 の CPU 見積（5〜10 ms / 800×450）も成り立たない。Software 描画（RDP、GLX 不可の Linux）と生成パターンのオフスクリーン生成（FR-42）に効く。
- 選択肢: (a) 設計どおり全画素評価のまま。(b) 出力がセル単位で一定の手法はセル数の縮小面で評価して最近傍拡大する（色数制限も縮小面に掛ける）。加えて、取得画像・パラメータ・サイズが変わらない限り描画結果（SKImage）を使い回す。(c) CPU 経路だけ C# SIMD カーネル（方式 A）を別実装する。
- 推奨: (b)。SkSL 1 ソースで GPU / CPU 共通という D-3 を保ったまま、ピクセル化は全解像度と画素一致で 2 ms 前後になる。六角セル平均・ぼかし手法は全画素評価のままだが、キャッシュにより再描画は取得・設定変更時だけになる。契約の変更は IMosaicMethod に手を入れず、任意実装のインターフェース（セルサイズを返す）を Core に追加するだけ。
- 影響: D-3（方式は維持、実装の細部）、NFR-04、FR-42、設計書 §4.2 / §4.4。該当コミットに `pending: P-1` を付ける。

**P-2: libhidercap の公開関数を 4 つにする**

- 問題: 指示書 §3.1 は `hidercap_start(fd, node_id)` / `hidercap_grab_after(pts, out)` の 2 関数だが、(1) 停止手段がないと第 1 段階へのフォールバックやポータルのセッション失効でストリームとスレッドが残る（テスト CT-89 も stop を前提にしている）、(2) 待ち時間の上限がないと GNOME が画面に変化のない間フレームを送らないため無期限に待つ、(3) フレームの複写先の大きさを呼び出し側が知る手段がない。
- 選択肢: (a) 2 関数のまま（停止は dlclose 任せ、タイムアウトなし）。(b) `hidercap_start(fd, node_id)`、`hidercap_grab_after(not_before_ns, timeout_ms, dst, dst_size, out)`、`hidercap_last_sequence()`、`hidercap_stop()` の 4 関数。
- 推奨: (b)。S-6 の原型（spikes/S-6/native）はこの形で実装済みで、Step 8 で native/hidercap に移す。
- 影響: 指示書 §3.1、Step 8、CT-89〜92。

**P-3: 人間待ちのゲートを仮通過として先へ進む**

- 問題: Step 3・4 のゲート（S-1・S-2・S-3 合格）、Step 5（S-4）、Step 7・8（S-5・S-6）が人間の実機検証待ち。
- 選択肢: (a) 結果が返るまで止まる。(b) headless・CI で確認できる範囲（Core、GUI の headless テスト、ビルド・publish・selftest）を先に実装し、実機依存の挙動は「未検証」と明記して PT の手順を docs/manual-tests.md に用意する。
- 推奨: (b)（依頼者から「判断は任せる、最後にまとめて確認」の指示あり）。実機結果で設計が変わる箇所（S-4 の結果次第で D-7 の WGC 昇格、S-6 の結果次第で帯のタイミング）は Step 5・8 の報告で再確認する。
- 影響: 指示書 §4 Step 1 のゲート。

### 7. 人間への依頼

spikes/S-1、S-2、S-4、S-5、S-6、S-8 の README の手順を実行し、「返してほしい情報」を返してほしい。優先度は S-1・S-2（Step 3 の前提）> S-4（Step 5）> S-5・S-6（Step 7・8）> S-3 の `--window`（GPU）> S-8。

### 8. 次 Step の前提

P-3 により Step 2（Core）へ進む。Step 2 は実機に依存しない。

## Step 2: Core（モデル・登録・永続化・スケジューラ）（2026-10-11）

### 1. 結果

完了。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| 3 ランナーで Core テスト緑 | 達成 | https://github.com/HIBICUS-CAI/Hider/actions/runs/38162760316 （全 12 ジョブ success。実行後に履歴を整えるため 1 コミットだけ書き換え: テストファイルの未使用 using を 1 行削除したのみ） |
| トリミング警告 0 | 達成 | `python scripts/build.py linux-x64`（CI=true）で IL 警告なし、CI の publish 3 RID も成功 |
| `--selftest` が JSON 往復を含む | 達成 | AppState（全フィールド・全登録手法・未知手法 ID）をファイル経由で Save → Load して一致を確認、全 .sksl をコンパイル。CI publish 3 RID で `selftest passed` |

### 3. 変更

| コミット | 内容 |
|---|---|
| c54c1e5 | モデル（Area、MethodSettings、既定値、色、ID） |
| 5566d63 | 移動・リサイズの幾何計算 |
| dcabc2d | 配置計算（Placement.Fit、新規エリア） |
| 18769d2 | Strings |
| 93a4d76 | 手法の登録基盤（ParamSpec、ParamValues、IMosaicMethod、MethodRegistry、SkslMethod）と pixelate / block スタブ |
| 0c9c8e0 | MosaicPipeline の骨格と BuildPyramid |
| 175789e | RefreshScheduler |
| a232474 | AppState / HiderJsonContext / AreasFile |
| 289c6fd | 保存先・単一インスタンス |
| a478a07 | 拡張性の回帰テスト（CT-19） |
| ead9c9b | --selftest の更新 |

新規依存なし。全コミットが単体でビルドできることを確認済み（worktree で各コミットをビルド）。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 55 / 0 / 0 | 1 / 0 / 0 |
| linux-x64 | 55 / 0 / 0 | 1 / 0 / 0 |
| linux-arm64 | 55 / 0 / 0 | 1 / 0 / 0 |

追加: CT-01〜18、CT-20〜40（CT-19 を含む Step 2 範囲すべて）、CT-50（BuildPyramid、Step 4 の ID だが Step 2 で実装したため前倒し）、CT-94〜98（色・Nudge・スライダー端・文字列書式・ID 補正）。

### 5. 性能

対象外。

### 6. 提案

なし（新規提案はなし。判断した事項は下記）。

判断メモ:

- 透明度は 0..1 で保持し、パターン別の下限（塗りつぶし・生成 0.10）は EffectiveOpacity で適用: パターンを切り替えても値を失わないため（FR-35）。
- 生成シェーダの固定 uniform に baseColor と seed を含めた: 生成パターンに色とシードが必須なため。CT-15 の「ParamSpec.Key + 固定 uniform」を「種類ごとの固定 uniform」と解釈。
- 単位 "%" は保存値が比率（JSON 例の variance 0.2 に合わせ、UI で ×100 表示）。ParamSpec に表示係数の項目を足さずに済ませた。
- 対数スライダーは先頭 2% を 0..SliderMin の線形域にした（更新間隔 0 を扱うため、CT-07）。
- RefreshScheduler に TimerPaused を追加（契約への追加）: ドラッグ中に Live でない取得のタイマーを止めるため（CT-40、FR-38）。
- Placement.Fit の縮小は「どの画面とも交差せず移動する時」だけ: 交差しているエリアを縮めると複数モニタにまたがるエリアが壊れるため。
- MethodSettings の Common / Params は init ではなく set: JSON ソース生成が欠けた init プロパティに null を代入するため（CT-29 で発見）。
- CT-13 の既知 ID 集合は現時点の {pixelate, block}。Step 6 で 5 手法に更新する。

### 7. 人間への依頼

なし（Step 1 の依頼が未回答のまま）。

### 8. 次 Step の前提

Step 3（App の骨格）へ。ゲート（S-1・S-2）は P-3 により仮通過。Xvfb（コンポジタなし）で起動・復元・編集モード・ドラッグ・Esc・保存の動作を確認しながら進める。
