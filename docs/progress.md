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

## Step 3: App の骨格（トレイ・エリア・塗りつぶし・保存復元）（2026-10-11）

### 1. 結果

未完了（実装と headless テストは完了。残りは実機 PT-01〜08 の人間による確認のみ）。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| headless テスト緑 | 達成 | https://github.com/HIBICUS-CAI/Hider/actions/runs/38163899067 （3 ランナーで GUI 24 件・Core 55 件 pass、publish 3 RID + selftest 成功） |
| Windows 11 / Ubuntu 26.04 実機で PT-01〜08 合格 | 未実施 | 手順は docs/manual-tests.md。作業コンテナの Xvfb で起動・復元（画面外の補正を含む）・ダブルクリック・ツールバー表示・ドラッグ・Esc・保存を確認済み |

### 3. 変更

bcd74ef（ログ）、74f12cc（IAreaWindowHost・画面情報）、038fb93（トレイメニューのモデル）、1ff967e（エリアの窓・編集モード・状態機械・管理）、56424b0（トレイアイコン）、28d76c8（全削除ダイアログ）、2f36b4a（トレイとポップアップ）、1a37bb5（起動・終了の配線）、958a15b（App 側の偽実装）、f5055b6 / f5968cd / 82b1192（GUI テスト）、8948403（manual-tests / wayland-migration）。新規依存なし。全コミットが単体でビルドできることを確認済み。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 55 / 0 / 0 | 24 / 0 / 0 |
| linux-x64 | 55 / 0 / 0 | 24 / 0 / 0 |
| linux-arm64 | 55 / 0 / 0 | 24 / 0 / 0 |

追加: GT-01〜20、GT-36〜38。

### 5. 性能

対象外。

### 6. 提案

なし。判断メモ:

- 移動・リサイズはポインタの画面座標（PointToScreen、物理 px）の差分で計算: 窓が動いても基準がずれず、DIP 変換を経ないため。
- 編集時の余白は D-8 のとおり同一窓を広げる。窓の幅は「エリア + 左右余白」と「ツールバー幅 + 余白」の大きい方（小さいエリアでもツールバーが切れないように）。
- 透明度は、隠しモードでは窓の Opacity（GT-22）、編集モードではパターンだけを薄くする（ツールバーとハンドルを読めるように）。
- 「全削除」以外の削除は確認なし（FR-05）。全削除ダイアログは最前面のエリアより上に出るよう Topmost。
- Linux で新規エリアを出すモニタ: XWayland はポインタが X11 窓の上にある時しか位置を知らないため、主画面に出す（docs/wayland-migration.md に記載）。
- Avalonia の source generator が名前付き要素のフィールドを代入するのは生成された InitializeComponent() だけ（Xvfb での起動確認で発見し修正）。

### 7. 人間への依頼

docs/manual-tests.md の PT-01〜08（Windows 11）と、Ubuntu の PT-02 / 03 / 05 / 06 / 08 / 15（前半）/ 16。返してほしいもの: 各項目の OK / NG、NG のスクリーンショットと `Hider --verbose` のログ。

### 8. 次 Step の前提

Step 4（モザイクパイプラインと描画）へ。S-3 の CPU 結果（P-1）を反映してセル単位評価とキャッシュを入れる。

## Step 4: モザイクパイプラインと描画（2026-10-11）

### 1. 結果

完了（GPU 経路の実測のみ人間待ち）。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| ゴールデン緑 | 達成 | https://github.com/HIBICUS-CAI/Hider/actions/runs/38164936834 （win-x64 / linux-x64 / linux-arm64 の 3 ランナーで、作業コンテナ（linux-x64）で作ったゴールデン 4 枚が許容値 1 / 2 以内で一致） |
| GPU / CPU 両パスで同一出力（許容誤差内） | CPU 側達成、GPU は人間待ち | GT-21: Presenter のオフスクリーン経路（headless の Skia = CPU）と Core の直接描画が差 1/255 以内。GPU は spikes/S-3 `--window` で max diff を出す（人間待ち） |
| S-3 の実測値を docs/perf.md に記録 | 達成（CPU） | docs/perf.md（S-3 の 3 ランナー値、CT-58 の 1080p パイプライン全体） |

### 3. 変更

| コミット | 内容 |
|---|---|
| 27e76e8 | 取得の契約（IUnderlayCapture、FrameBuffer、FrameExchange） |
| 880141b | MosaicPipeline 本体（前段ぼかし・ピラミッド・セル単位評価・色数）と GeneratedCache（pending: P-1） |
| e4e5767 / cc58c51 | pixelate / block の本実装 |
| 804ac67 / 49372b2 | 合成画像・ゴールデン比較の基盤、CT-41〜60 とゴールデン 4 枚 |
| a6d0764 | 取得の配線（RefreshScheduler → 取得 → ダブルバッファ、FR-52 の切替） |
| 71c6d1b | MosaicPresenter（pending: P-1） |
| 180a008 | GT-21〜25 |
| 8a489fe | docs/perf.md |

新規依存なし。全コミットが単体でビルドできることを確認済み。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 76 / 0 / 0 | 31 / 0 / 0 |
| linux-x64 | 76 / 0 / 0 | 31 / 0 / 0 |
| linux-arm64 | 76 / 0 / 0 | 31 / 0 / 0 |

追加: CT-41〜49、CT-51〜60、GT-21〜25。ゴールデン: CT42_pixelate_block12、CT48_blur_r8、CT49_blur8_pixelate12、CT51_block_seed1234（作業コンテナの CPU ラスタで生成し目視確認）。

### 5. 性能

1080p の CPU ラスタ（作業コンテナ）: ピクセル化 51.0 ms（うちテスト側 13.8 ms）、前段ぼかし 8 + ピクセル化 89.5 ms、生成ブロック 34.8 ms、目地付き 42.0 ms。支配項はピラミッド作成。詳細は docs/perf.md。

### 6. 提案

なし（P-1 を実装し `pending: P-1` を付けた）。判断メモ:

- 色数は SkSL の色フィルタではなく Skia の表フィルタ（floor(c·L)/(L−1)）: CPU で 2 倍速く、GPU でも同じ結果。設計書 §4.2 の「SkSL ColorFilter」から変えたが、式は同じ。
- 追加ぼかしの半径 r は sigma = r / 2 とした（見た目の広がりが r 程度になる）。
- 生成パターンのハッシュは sin を使わない形にした（GPU / CPU で結果がずれにくい）。seed は 65536 の剰余を float で渡す。
- 目地付きブロックは目地を矩形で塗ることでセル単位評価のまま描く（ICellMethod.DrawJoints）。全画素評価では 1080p で 579 ms だった。
- 取得結果はキャプチャを実行したスレッドで Back に複写して Publish し、UI には再描画だけを投げる（UI スレッドで複写しない）。描画は FrameExchange.Read の中で SKImage.FromPixels により複写なしで参照する。
- 最初のフレームが届く前のモザイクエリアは生成パターンで覆う（NFR-05）。
- 「取得できません」表示時はツールバーが広がる分だけ窓も広げる（Xvfb の確認で見つけた表示崩れを修正）。

### 7. 人間への依頼

spikes/S-3 の `--window`（Windows 11 と Ubuntu 26.04）: GPU 経路の 1 フレーム時間と CPU 結果との最大差。

### 8. 次 Step の前提

Step 5（Windows 取得・ライブ更新・設定ポップアップ）へ。S-4（PrintWindow の黒判定）は人間待ちのため P-3 で仮通過し、UnderlayComposer は偽の窓リストで検証する。

## Step 5: Windows 取得・ライブ更新・設定ポップアップ（2026-10-11）

### 1. 結果

実装完了。完了条件の 2 つはいずれも Windows 11 実機が必要なため人間待ち。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| Windows 11 実機で 800×450・窓 3 枚・100 ms で CPU が 1 コアの 10% 以下 | 人間待ち | docs/manual-tests.md PT-11、記録先 docs/perf.md「Windows ライブ更新」 |
| S-4 で黒になった窓の種類が FR-52 で正しくフォールバック | 人間待ち（偽の窓リストでは達成） | CT-66〜68（黒判定・タイムアウト・取得不能 → 生成パターン）、docs/manual-tests.md PT-12〜13 |
| CT-61〜70、GT-26〜35 | 達成 | https://github.com/HIBICUS-CAI/Hider/actions/runs/38165635187 |

### 3. 変更

| コミット | 内容 |
|---|---|
| c4be83a | UnderlayComposer（下の窓の合成、純粋関数）と CT-61〜70 |
| 8c3144e | Win32WindowSource（EnumWindows の Z 順、可視・非クローク・交差・Hider 除外、DWM 枠、Progman）、Win32UnderlayCapture（PrintWindow(PW_RENDERFULLCONTENT)、窓ごとタイムアウト、黒検出）と配線 |
| 4ca29a8 | `--selftest` に Windows の取得経路の検査 |
| dc759ea | Avalonia.Controls.ColorPicker（テーマは XAML で読み込み。S-7 のトリミング対策） |
| 2ba5814 | 設定ポップアップ（ParamSpec から行を生成、手法ドロップダウン、カラーピッカー + プリセット 8 色） |
| a3432cb | GT-26〜35、GT-40 |
| 8a81937 | docs/manual-tests.md に PT-09〜14 |

新規依存: Avalonia.Controls.ColorPicker（作業指示書 §2.1 の許可済み一覧内）。全コミットが単体でビルドできることを確認済み。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 86 / 0 / 0 | 42 / 0 / 0 |
| linux-x64 | 86 / 0 / 0 | 42 / 0 / 0 |
| linux-arm64 | 86 / 0 / 0 | 42 / 0 / 0 |

追加: CT-61〜70、GT-26〜35、GT-40（設定ポップアップは headless の GUI テストで確認。ColorPicker の描画はトリミング後の単一ファイルで S-7 として確認済み）。

### 5. 性能

Windows の CPU 負荷は実機待ち（PT-11）。参考: S-4 の GPU なしランナーで PrintWindow は 1 窓約 30 ms。

### 6. 提案

なし。判断メモ:

- スライダー操作中の保存は 500 ms のデバウンス（ドラッグ中に毎フレーム areas.json を書かない）。数値入力の確定・ドロップダウン・プリセットは即保存。
- `CaptureRequest.ExcludeWindows[0]` を「要求したエリア自身の窓」と決めた。他のエリアの窓は合成に含める（エリアが重なった時に下のエリアのパターンが見えるのが自然なため）。全エリアを除外する案は、重なり部分で下のアプリの中身が透けるので却下。
- 黒判定は保守的（全画素が黒に近い時だけ「取得不能」）。暗いアプリを誤って取得不能にしないため。
- EnumWindows はトリミング安全のため関数ポインタ（`delegate* unmanaged`）で呼ぶ。

### 7. 人間への依頼

docs/manual-tests.md の PT-09〜14（Windows 11 実機）。特に PT-11（CPU 負荷）と S-4（spikes/S-4/README.md）の黒判定の結果。

### 8. 次 Step の前提

Step 6（hexcell / blur / noise）へ。

## Step 6: 手法の追加（六角セル平均・ぼかし・ノイズ）（2026-10-11）

### 1. 結果

完了。CI: https://github.com/HIBICUS-CAI/Hider/actions/runs/38167416443 （3 ランナーで全テスト・ゴールデン・CPU 経路とシェーダの一致が緑）。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| 3 手法 + 2 生成がツールバー・設定・JSON で選べる | 達成 | `AllMethods_SelectableDrawnAndSaved`（設定ポップアップで各手法を選び、描画し、areas.json に保存されることを確認）。ツールバーはパターン種別（塗り / モザイク / パターン）だけを選び、手法は設定の登録一覧から自動で並ぶ |
| ゴールデン緑 | 達成 | CT-71（hexcell）、CT-75（blur は CT-48 のゴールデンと一致） |
| 追加手順が 5 手順で済む（CT-78） | 達成 | 下表 |

CT-78（手法 1 つの追加で触ったファイル）:

| 手法 | コミット | ファイル数 | 内訳 |
|---|---|---|---|
| hexcell | bbaef7e | 5 | hexcell.sksl、HexcellMethod.cs、MethodRegistry.cs（1 行）、HexcellTests.cs、ゴールデン PNG |
| blur | d375f40 | 4 | blur.sksl、BlurMethod.cs、MethodRegistry.cs（1 行）、BlurTests.cs |
| noise | 7a6fc4e | 4 | noise.sksl、NoiseMethod.cs、MethodRegistry.cs（1 行）、NoiseTests.cs |

表示名（Strings）は Step 2 で 5 手法分を先に入れてあったため、今回は触っていない（手順としては 5 番目）。UI・永続化・パイプラインの変更は不要だった。

ただし基盤側に 3 点手を入れた（§4.3「済まなければ基盤側を直す」）:

1. 手法固有の前段ぼかし（IPreBlurMethod）: blur 手法を「前段ぼかしのみ」で実現するため。共通ぼかしとは sqrt(a²+b²) で 1 回にまとめる（ガウスの合成則。2 回掛けるより速く同じ結果）。
2. 素通し省略とピラミッドの遅延生成: blur 手法を追加したら CPU で 131 ms かかった（使わないピラミッド約 25 ms + 素通しシェーダの全画素評価約 50 ms）。どちらも手法側の追加作業なしで効く。
3. CPU 描画用の任意の高速経路（ICpuRenderer）: hexcell・noise の SkSL は CPU ラスタで 1080p あたり 0.7〜1.1 s（分岐・ハッシュが 1 画素 300〜550 ns）。CPU キャンバスでのみ C# の画素ループで描く。実装は任意なので追加手順は増えない。

### 3. 変更

| コミット | 内容 |
|---|---|
| 6cc3631 / 936ffe6 | CT-13 を一時的に部分集合判定にし、5 手法が揃った時点で完全一致に戻した |
| bcd8c7f | IPreBlurMethod |
| bbaef7e / d375f40 / 7a6fc4e | hexcell / blur / noise |
| f66b99c | 素通しになる値ではシェーダを通さない（SkSL と同じ画素になることをテスト） |
| fbaef45 | ピラミッドを初めて読まれた時に作る |
| 94d0698 | ICpuRenderer と RasterSampler（子シェーダと同じ座標・クランプ・線形補間で読む） |
| 3b954ab / 551fff3 / bb9ac85 | hexcell・noise の CPU 経路、画素ループを最初から最適化コンパイル |
| ea57493 / 522cf7e / 9e9f589 | CT-58 の計測追加、docs/perf.md |
| 974ca3e | 完了条件の GUI テスト |

新規依存なし。全コミットが単体でビルド・テストできることを確認済み。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 115 / 0 / 0 | 44 / 0 / 0 |
| linux-x64 | 115 / 0 / 0 | 44 / 0 / 0 |
| linux-arm64 | 115 / 0 / 0 | 44 / 0 / 0 |

追加: CT-71〜77、素通し・CPU 経路とシェーダの一致（MethodConsistencyTests）、RasterSampler と Skia の差 1 以内、全手法の選択・描画・保存。ゴールデン: CT71_hexcell_radius10（作業コンテナの CPU ラスタで生成し目視確認）。

### 5. 性能

1080p の CPU ラスタ（作業コンテナ）: blur 手法 131〜139 → 58〜66 ms、hexcell 685〜700 → 61〜72 ms、noise 1114〜1147 → 31〜41 ms。詳細は docs/perf.md。

### 6. 提案

なし。判断メモ:

- CPU 経路は「シェーダが基準、C# は CPU 専用の高速版」とし、一致をテストで縛る。却下案: (1) GPU でも幾何（六角形の頂点）や表で描く → SkSL 1 本で済むという追加手順が崩れ、六角形はテクスチャ座標が退化して Skia の DrawVertices で描けない。(2) CPU でも縮小評価で近似 → 六角セルは正方格子に乗らない。
- hexcell の CPU 経路はシェーダと同じ float 演算にしたので、作業コンテナでは全画素一致。テストは CPU の FMA 差に備えて 0.5% まで許容。
- noise は値ノイズ（格子 scale × 4 px、smoothstep 補間）を levels 段に量子化し、ベース色 ±0.15 に割り当てる。格子はエリア原点基準なのでリサイズで模様が崩れない（CT-76）。ブロックと同じハッシュは最後が fract(x·y)（x·y が数千）のため実装間で 1e-3 程度ずれ、段の境目の画素が入れ替わる（作業コンテナで 0.3% 以下）。見た目には影響しないので許容した（テストは 2% まで）。
- noise の「scale」は単位なし（値 × 4 px が格子間隔）。仕様書に単位の定義がなかったため、既定 4 で 16 px の模様になるよう決めた。
- blur 手法の半径は 0〜128（スライダー 0〜32、既定 8、不透明度 0 で素通し）。

### 7. 人間への依頼

なし（GPU 経路の確認は Step 4 の S-3 `--window` に含まれる。実機で hexcell・noise を選んで表示が崩れないかを PT の際に見てもらえると確実）。

### 8. 次 Step の前提

Step 7（Linux 第 1 段階: Screenshot ポータル + 横帯）へ。

## Step 7: Linux 第 1 段階（Screenshot ポータル + 横帯）（2026-10-11）

### 1. 結果

実装完了。完了条件（Ubuntu 26.04 実機での録画と許可ダイアログ）は人間待ち。実機の代わりに、Xvfb + コンポジタ + 偽ポータルで透明化から貼り合わせまでを実際の X11 描画で確認し、CI にも組み込んだ。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| Ubuntu 26.04 実機で 240 px 高のエリアが帯 8 px で撮れ、露出が帯 1 本分（録画） | 人間待ち（Xvfb では達成） | PT-17。Xvfb: 30 回の撮影で下の画像のモザイクになる（セル平均と ±1）。https://github.com/HIBICUS-CAI/Hider/actions/runs/38169013686 の test-gui（linux-x64 / linux-arm64）の `xvfb-strip-smoke.sh` |
| 許可ダイアログは初回のみ | 人間待ち | PT-18、S-5 |
| CT-79〜88 | 達成 | 同上の CI（3 ランナー） |
| Denied / Unavailable → FR-52 | 達成 | CT-85。Xvfb で偽ポータルの拒否（応答 1）とポータル不在の両方で生成パターンへ切り替わることを確認 |
| D-9 のフォールバック窓 | 達成 | D9_* テスト。Xvfb（トレイ無し）で表示とボタン動作を確認 |

### 3. 変更

| コミット | 内容 |
|---|---|
| 7168513 | IScreenGrabber、IStripHost、StripArea、CaptureDeniedException |
| 72b3052 | StripCoordinator と CT-79〜88（偽の Grabber / Host） |
| 2df07b2 | AreaWindow の帯の透明化（Root のクリップ、描画完了待ち） |
| 5828729 | AreaStripHost（取得スレッド → UI スレッド、エリア一覧のスナップショット） |
| f8377f3 | Tmds.DBus.Protocol を直接参照（これまで推移的依存） |
| 8386ec1 | PortalScreenshotGrabber |
| 3f24842 | Linux の取得を StripCoordinator + ポータルに配線 |
| c80d0d2 / e2f34d7 | TrayMenuPanel の切り出し、D-9 の常駐窓（StatusNotifierWatcher の有無で判定） |
| ed22d8a / 7c618ca | 偽ポータル（tools/fakeportal）と scripts/xvfb-strip-smoke.sh、CI の Linux GUI ジョブで実行 |
| 8ae2059 / 813d14f | PT-15〜20 の手順、XWAYLAND-ASSUMPTION と移行メモ |

新規依存なし（Tmds.DBus.Protocol は Avalonia と同じ版を明示しただけ）。CI に apt の xvfb / xcompmgr / imagemagick / dbus を追加（Linux GUI ジョブのみ）。全コミットが単体でビルド・テストできることを確認済み。X11WindowHost は Step 3 で実装済みのため変更なし。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 129 / 0 / 0 | 52 / 0 / 0 |
| linux-x64 | 129 / 0 / 0 | 52 / 0 / 0（+ Xvfb 帯撮影 合格） |
| linux-arm64 | 129 / 0 / 0 | 52 / 0 / 0（+ Xvfb 帯撮影 合格） |

追加: CT-79〜88、帯の透明化（隠し / 編集モードで該当行のアルファが 0、戻すと 255）、StripCoordinator → AreaStripHost → AreaWindow の結合、PNG の読み込み、ポータル不在で null、D-9 の 3 件。トリミング後の単一ファイル（linux-x64）でも Xvfb の帯撮影が合格。

### 5. 性能

実機の値は PT-17 待ち。Xvfb の偽ポータル（ImageMagick の import）は 1 回約 200 ms で、設計書 §5.3 の 0.1〜0.3 s と同程度。240 px / 帯 8 px で 30 回撮るため、第 1 段階の撮り直しは数秒かかる（第 2 段階で短縮する前提どおり）。

### 6. 提案

なし。判断メモ:

- 帯の透明化は MosaicPresenter ではなく窓の Root をクリップして行う。編集枠やエリア内に置いたツールバーもまとめて消えるので撮影に写り込まない。クリップの境目が画素の途中に来ても覆われた行が半透明で残らないよう、上下 1 物理 px 広く消す（貼り合わせるのは帯の行だけ）。
- 描画完了は RequestAnimationFrame を 3 回待つ（描く・表示・余裕 1。設計書の「描画完了 + 1 フレーム」）。フレームが来ない窓（最小化など）で止まらないよう 250 ms で打ち切る。
- 帯で透明にするのは、要求したエリアと重なるモザイクのエリアだけ。塗りつぶし・生成パターンのエリアは撮影に写したまま（人が画面を見た時と同じ。CT-83）。透明にする行はエリアの全幅（契約が行だけのため）。
- StripCoordinator のコンストラクタに「仮想画面の範囲」を足した。ポータルのフレームが X11 座標と異なる倍率でも、画素中心を写して最近傍で読む（CT-88、分数スケーリングの検証は PT-20 / S-6）。
- 取得スレッドが読むエリア一覧と仮想画面は、取得開始時に UI スレッドで作るスナップショット。取得スレッドから UI スレッドへ同期で問い合わせる案は、取得が UI 待ちで止まるので却下。
- ポータルの応答 1（ユーザーが取消）は Denied、それ以外の失敗・バス不在・ポータル不在は Unavailable。どちらも FR-52 で生成パターンに切り替わる。
- D-9 の窓は × で最小化する（メニューへの唯一の入口のため）。終了はメニューの「終了」。
- 偽ポータルによる Xvfb 確認は GNOME 実機の代わりではない（Mutter の合成タイミング、XWayland の座標、許可の記憶は実機でしか分からない）が、配線の退行は CI で捕まえられる。

### 7. 人間への依頼

docs/manual-tests.md の PT-15〜20（Ubuntu 26.04 実機）と spikes/S-5。特に PT-17（録画で露出が帯 1 本分か）と PT-20（125% / 150% でのずれ）。

確認したいこと（最後にまとめて確認予定）: FR-52 の切り替えは Step 4 から「エリアのパターンを生成に変えて保存する」実装になっている。一時的な失敗（ログイン直後でポータルがまだ無い等）でも次回起動時は生成パターンのまま（ツールバーで戻せる）。「表示だけ生成に切り替え、保存はモザイクのまま」にする案もある。

### 8. 次 Step の前提

Step 8（ScreenCast + PipeWire ヘルパ）へ。spikes/S-6 の libhidercap 原型（P-2 の 4 関数 API）を native/hidercap に移す。

## Step 8: Linux 第 2 段階（ScreenCast + PipeWire ヘルパ）（2026-10-11）

### 1. 結果

実装完了。完了条件（Ubuntu 26.04 / arm64 実機での帯 1 本 3 フレーム以内、再起動後ダイアログなし）は人間待ち。実機の代わりに、作業コンテナと CI（x64 / arm64）で専用の PipeWire を立て、偽ポータルの ScreenCast → libhidercap → 帯の貼り合わせを通しで確認した。

### 2. 完了条件の達成表

| 条件 | 結果 | 証跡 |
|---|---|---|
| 帯 1 本あたり 3 フレーム以内 | 人間待ち | PT-22。Xvfb（30 fps、PTS なし）では約 2 フレーム |
| 再起動後にダイアログなしで撮れる | 人間待ち（Xvfb では達成） | PT-21。偽ポータルで restore_token が「無し → token-1 → token-2」と渡り、置き換わることを確認。CT-91 |
| linux-arm64 実機でも同様 | 人間待ち（CI の arm64 ランナーの Xvfb では達成） | PT-24、https://github.com/HIBICUS-CAI/Hider/actions/runs/38170573443 の test-gui (linux-arm64) |
| CT-89〜92 | 達成 | 同上の CI（3 ランナー） |
| ヘルパ不在で第 1 段階 | 達成 | CT-92（FallbackGrabber、PipeWireGrabber）。Xvfb: ヘルパなしの dll とヘルパ同梱の単一ファイル（偽ポータルが ScreenCast 非対応）の両方で第 1 段階で撮れた |
| CI の native ジョブ | 達成 | native (linux-x64 / linux-arm64) が実ヘルパをビルド |

### 3. 変更

| コミット | 内容 |
|---|---|
| 93d1e21 | IScreenGrabber が必要な範囲を受け取り、写した範囲（ScreenFrame.Covers）を返す形に変更 |
| c3751ec | FallbackGrabber（第 2 段階 → 第 1 段階） |
| 0a614f4 / 564f6cb | native/hidercap（pending: P-2）、CLOCK_MONOTONIC でない PTS の扱い |
| 38e5059 | P/Invoke 層（LibraryImport、IHidercap） |
| 3dc6b19 | RestoreTokenStore |
| f628f62 / 6a45ea5 | PortalClient の切り出し、ScreenCastPortal |
| 82be6c7 / ce5a2cf | PipeWireGrabber、失敗・拒否後の抑止 |
| c4d5fd7 | Linux の配線（ScreenCast + ヘルパ → Screenshot） |
| c7cc7e4 / 0d2134a | Xvfb 確認の第 2 段階モード（偽ポータルの ScreenCast、専用 PipeWire）、CI で実行 |
| 69d4947 | 帯の描画完了待ちを Rendered + 1 フレームに |
| 7849879 / 最新 | PT-21〜27、docs/perf.md |

新規 NuGet 依存なし。CI の Linux GUI ジョブに apt の pipewire / wireplumber / gstreamer1.0-pipewire / gstreamer1.0-plugins-good / gstreamer1.0-tools / libpipewire-0.3-dev を追加。全コミットが単体でビルド・テストでき、ヘルパもビルドできることを確認済み。

### 4. テスト

| OS | Core pass / fail / skip | GUI pass / fail / skip |
|---|---|---|
| win-x64 | 135 / 0 / 0 | 65 / 0 / 0 |
| linux-x64 | 135 / 0 / 0 | 65 / 0 / 0（+ Xvfb 第 1 段階・第 2 段階 合格） |
| linux-arm64 | 135 / 0 / 0 | 65 / 0 / 0（+ Xvfb 第 1 段階・第 2 段階 合格） |

追加: CT-89〜92、FallbackGrabber、フレームが範囲外なら Unavailable、ScreenCast のストリーム位置と X11 モニタの対応付け、失敗・拒否後の抑止、hidercap_frame の構造体配置。

### 5. 性能

Xvfb の参考値は docs/perf.md「Linux 撮り直し」（第 2 段階で 30 帯 約 2.1 s、30 fps の映像ノードで PTS が無いため）。実機の PT-22〜23 待ち。

### 6. 提案

P-2（libhidercap の公開関数を 4 つ）を実装し、コミットに `pending: P-2` を付けた。作業指示書の「関数 2 つ」に `hidercap_last_sequence`（PT-22 の「帯 1 本のフレーム数」をログに出すため）と `hidercap_stop` を足したもの。

判断メモ:

- ヘルパは毎フレーム複写せず、最新バッファを dequeue したまま保持し、grab_after の時だけ BGRA に変換して複写する。原型は動画再生中など画面が動く間ずっと 1 フレームごとに全画面を複写していた。
- ScreenCast のセッションは撮影時に開き、最後の帯から 3 s で閉じる。常時開くと GNOME の画面共有表示が出続ける。却下案: 常時開いて PipeWire だけ止める → 表示は消えない。毎帯開閉 → 遅い。
- ScreenCast はダイアログで選んだ 1 モニタだけを写すため、Grabber の契約を「必要な範囲を受け取り、写した範囲を返す」に変えた。共有モニタ外のエリアは第 1 段階で撮る（PT-27 を追加）。複数モニタを同時に共有する案（multiple=true）はヘルパが複数ストリームを扱う必要があり見送り。
- ポータルのストリーム位置（コンポジタの論理座標）と X11 のモニタは「原点が同じ、または倍率を掛けると同じ」で対応付ける（XWAYLAND-ASSUMPTION、PT-20 / S-6 で確認）。
- Mutter のフレーム時刻（PTS）は CLOCK_MONOTONIC の前提。直近 500 ms に入らない PTS は信用せず受信時刻（−2 フレーム）で判定する。Xvfb の通し確認で GStreamer が別の時計を使っていて見つかった（全フレームが古いと判定され、帯ごとに 1 s 待っていた）。
- ScreenCast の失敗後 1 分は第 1 段階に任せ、ダイアログを取り消した場合はその起動中は二度と尋ねない（帯の数だけダイアログが出るのを防ぐ）。
- restore_token は ~/.hider/restore_token に所有者のみ読み書き可で保存（持っていれば無許可で画面を撮れるため）。
- 偽ポータルによる確認は GNOME の代わりではない（Mutter の PTS、DMA-BUF を選ばないか、XWayland の座標は実機でしか分からない）が、ポータル呼び出し・fd の受け渡し・PipeWire の購読・PTS 判定・貼り合わせの配線は CI で毎回確かめられる。

### 7. 人間への依頼

docs/manual-tests.md の PT-21〜27（Ubuntu 26.04、arm64 実機）と spikes/S-6。特に PT-22 のログが `(pts)` になっているか（`(receive time)` なら Mutter の PTS の扱いを見直す）。

### 8. 次 Step の前提

Step 9（アイコン、README、ドキュメント、CI 全緑）へ。
