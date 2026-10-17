# 追加したテスト項目（CT / GT の追加分、ST / NT の新設）

> **このファイルが追加分の正です。作業指示書の「テスト項目」タブには未反映です**（タブへの反映はユーザーの判断）。タブにある CT-01〜CT-92 / GT-01〜GT-38 と、タブに未記載の CT93〜CT98 / GT39 / GT40 の意味は変えていません。

## 規則

- テストメソッド名の先頭に ID を置く（`CT99_FrameBuffer_Defaults`）。番号は既存の最大の次から振り、欠番は再利用しない。
- 既存の項目の説明に含まれる振る舞いを細かく検証するテストは、その既存 ID を付ける（下の「既存 ID の細分化」）。説明にない振る舞い（失敗経路・寿命・境界の扱いなど）は新しい ID にする。同じテスト項目を検証するメソッド群は同じ ID を共有する。
- CT は Core 側の項目、GT は Hider.App.Tests 側の項目（PipeWire 層の CT-89〜CT-91 のように、タブで CT とされた項目は App 側のテストでも CT のまま）。
- **ST-xx（tests/scripts の Python unittest）と NT-xx（native/hidercap の C の単体テスト）は新設**（提案 P-13、承認済み）。Python はテストメソッドを `test_st01_...`、C はテスト関数を `nt01_...` とする。native/hidercap/test_frame_time.c は従来どおり CT-90（main 関数 1 つ）。
- main（43d0337）にあった ID なしのテスト 93 件は今回の対象外（改名していない）。

件数: 新しい ID は CT99〜CT150（52）、GT41〜GT132（92）、ST01〜ST51（51）、NT01〜NT09（9）。C# のメソッド 1,184 件のうち 402 件を既存 ID に、782 件を新しい ID に付けた。Python 336 件、C 17 関数。

## 新しい ID（CT / GT）

| ID | 種別 | 内容 | 対応 FR・NFR・決定 | ファイル / クラス |
|---|---|---|---|---|
| CT99 | CT | 取得の型（CaptureFailure / CaptureTraits / CaptureRequest / CaptureResult、帯撮影の ScreenFrame / StripArea / CaptureDeniedException）の値・既定値・等価性 | FR-52 | Hider.Core.Tests/Capture/CaptureTypesTests.cs（CaptureTypesTests、5 件）<br>Hider.Core.Tests/Capture/StripTypesTests.cs（StripTypesTests、3 件） |
| CT100 | CT | FrameBuffer の確保と再利用（詰めた行・ゼロ埋め、大きくなるときだけ確保し直す、桁あふれは例外でフレームを保つ） | NFR-04 | Hider.Core.Tests/Capture/CaptureTypesTests.cs（CaptureTypesTests、8 件） |
| CT101 | CT | 取得手段がないときの UnavailableCapture は常に Unavailable を返し、露出しない（キャンセル・空の矩形・二重 Dispose でも） | FR-52, NFR-05 | Hider.Core.Tests/Capture/CaptureTypesTests.cs（CaptureTypesTests、2 件） |
| CT102 | CT | FallbackGrabber の準備（Prepare）と破棄: 準備できた最初の手段を選ぶ、拒否はどれも準備できないときだけ投げる、キャンセルで終わる、拒否の後は前の選択を忘れる、Dispose は全手段を順に。IScreenGrabber の既定メンバー | D-6 | Hider.Core.Tests/Capture/FallbackGrabberEdgeTests.cs（FallbackGrabberEdgeTests、6 件）<br>Hider.Core.Tests/Capture/StripTypesTests.cs（StripTypesTests、3 件） |
| CT103 | CT | FrameExchange（取得スレッドと UI の間の 2 枚のバッファ）: Publish で入れ替え、Clear で表を捨てる、Release で両方を手放す | FR-38 | Hider.Core.Tests/Capture/FrameExchangeTests.cs（FrameExchangeTests、6 件） |
| CT104 | CT | FrameExchange の読み取りと並行性: 表と版数を渡す、読み手の例外の後も使える、読み取り中のフレームは差し替えない、並行する Publish を版数がすべて数える | FR-38 | Hider.Core.Tests/Capture/FrameExchangeTests.cs（FrameExchangeTests、4 件） |
| CT105 | CT | StripCoordinator の Dispose: 取得手段も破棄し以降の取得は例外、取得中の Dispose ではその取得は自分の結果で終わり行を戻す | NFR-05 | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、3 件） |
| CT106 | CT | UnderlayComposer の合成先: バッファを再利用し小さければ広げる、半透明の画素も不透明で書く、失敗時はバッファを残してフレームを返さない、LastCaptured は直近に合成した窓 | NFR-04 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、5 件） |
| CT107 | CT | ログ（CLAUDE.md の規約）: 既定は無効、無効なら何も書かない、有効なら時刻付きの 1 行を標準エラーへ | — | Hider.Core.Tests/LogTests.cs（LogTests、3 件） |
| CT108 | CT | ログの捕捉（CaptureTo）: 捕捉中は時刻なしで捕捉先だけへ・無効でも捕捉、入れ子は外側に戻る、開始した非同期処理へ流れる | — | Hider.Core.Tests/LogTests.cs（LogTests、4 件） |
| CT109 | CT | AreaGeometry.Resize: ハンドルなしは不変、ドラッグした辺だけ動く、関係ない方向の差分は無視。ResizeHandle の値と角が 2 辺の組であること | FR-32 | Hider.Core.Tests/Model/AreaGeometryCasesTests.cs（AreaGeometryCasesTests、3 件）<br>Hider.Core.Tests/Model/EnumTests.cs（EnumTests、2 件） |
| CT110 | CT | DIP と物理 px の長さの変換: DipLength は 1/4 px の余裕を足す、StableLength は整数倍率ならそのまま・不正な倍率では探索上限で止まる | FR-13 | Hider.Core.Tests/Model/AreaGeometryCasesTests.cs（AreaGeometryCasesTests、3 件） |
| CT111 | CT | 設定のモデル（Area の矩形、FillSettings、MethodSettings、モザイク / パターンの共通値）の既定値と独立性: エリアごとに別の設定、ParamsFor は一度だけ作り他の手法の値を残す、共通値と手法値は差し替えられる | FR-35 | Hider.Core.Tests/Model/AreaTests.cs（AreaTests、9 件） |
| CT112 | CT | パターン名（PatternKind）の JSON の読み書き: 3 種の順、仕様の名前で書き大小文字を問わず読む、整数は値として読む、未知の名前は mosaic、文字列・整数以外は JSON のエラー | §3.4, FR-64 | Hider.Core.Tests/Model/EnumTests.cs（EnumTests、5 件）<br>Hider.Core.Tests/Persistence/LenientPatternKindConverterTests.cs（LenientPatternKindConverterTests、7 件） |
| CT113 | CT | PhysicalRect の演算: 右端・下端、空の判定、交差判定（接するだけは偽、空は交わらない）、共通部分、和、平行移動、値の等価 | — | Hider.Core.Tests/Model/PhysicalRectTests.cs（PhysicalRectTests、11 件） |
| CT114 | CT | 各手法の識別（Id・ラベル・種類）と ParamSpec の定義（キー・範囲・既定値） | FR-37, D-5 | Hider.Core.Tests/Mosaic/BlockMethodTests.cs（BlockMethodTests、2 件）<br>Hider.Core.Tests/Mosaic/BlurMethodTests.cs（BlurMethodTests、1 件）<br>Hider.Core.Tests/Mosaic/HexcellMethodTests.cs（HexcellMethodTests、1 件）<br>Hider.Core.Tests/Mosaic/NoiseMethodTests.cs（NoiseMethodTests、2 件）<br>Hider.Core.Tests/Mosaic/PixelateMethodTests.cs（PixelateMethodTests、1 件） |
| CT115 | CT | パススルーの判定: 生成手法は常に偽、SkslMethod の既定は偽でパススルーでなければ効果を走らせる | 決定 13 | Hider.Core.Tests/Mosaic/BlockMethodTests.cs（BlockMethodTests、1 件）<br>Hider.Core.Tests/Mosaic/SkslMethodTests.cs（SkslMethodTests、2 件） |
| CT116 | CT | block: セルサイズと目地幅は範囲内の整数 px に丸めてシェーダへ渡す | §4.5 | Hider.Core.Tests/Mosaic/BlockMethodTests.cs（BlockMethodTests、4 件） |
| CT117 | CT | モザイク共通パラメータ（ぼかし・階調・帯の高さ）の定義と設定画面の順序 | FR-37 | Hider.Core.Tests/Mosaic/CommonParamsTests.cs（CommonParamsTests、4 件） |
| CT118 | CT | GeneratedCache の画像の所有と解放: 描画が投げても解放済みの画像を残さない、Detach で呼び出し側へ渡しキーを忘れる、Dispose で解放（繰り返し・空でも可）、破棄後の Get は描き直す | FR-42 | Hider.Core.Tests/Mosaic/GeneratedCacheTests.cs（GeneratedCacheTests、5 件） |
| CT119 | CT | モザイク手法（pixelate / hexcell）の CPU 経路（セル単位評価・TryRender）がシェーダと画素単位で一致する（境界値・小さく細いエリア・ピラミッドの短い入力・行の余白を含む）。扱えない入力（パススルー・CPU 画素なし）は辞退する | P-1 | Hider.Core.Tests/Mosaic/HexcellMethodTests.cs（HexcellMethodTests、6 件）<br>Hider.Core.Tests/Mosaic/MethodBoundaryTests.cs（MethodBoundaryTests、1 件）<br>Hider.Core.Tests/Mosaic/MethodConsistencyTests.cs（MethodConsistencyTests、1 件）<br>Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、4 件）<br>Hider.Core.Tests/Mosaic/PixelateMethodTests.cs（PixelateMethodTests、1 件）<br>Hider.Core.Tests/Mosaic/PixmapRowsTests.cs（PixmapRowsTests、3 件） |
| CT120 | CT | 範囲外・非有限の値: ParamSpec の Clamp は箱の範囲・NaN は既定値、範囲外の値は端の値と同じに描き NaN は既定値で描く（全手法・全パラメータ、モザイク共通値も） | FR-37 | Hider.Core.Tests/Mosaic/HexcellMethodTests.cs（HexcellMethodTests、1 件）<br>Hider.Core.Tests/Mosaic/MethodBoundaryTests.cs（MethodBoundaryTests、5 件）<br>Hider.Core.Tests/Mosaic/NoiseMethodTests.cs（NoiseMethodTests、1 件）<br>Hider.Core.Tests/Mosaic/ParamSpecTests.cs（ParamSpecTests、1 件）<br>Hider.Core.Tests/Mosaic/PixelateMethodTests.cs（PixelateMethodTests、1 件） |
| CT121 | CT | 格子ハッシュ hash(cellX, cellY, seed) が整数の参照実装と一致: 法 251、境界での剰余、置換、シードの 251 進 3 桁、値は 0 と 1 の間で全値に届く、251 セルごとに繰り返さない | FR-40 | Hider.Core.Tests/Mosaic/LatticeHashReferenceTests.cs（LatticeHashReferenceTests、8 件） |
| CT122 | CT | 生成手法 noise の CPU 経路（TryRender）がシェーダと一致する（境界値・小さく細いエリア・行の余白）。エリアを広げると格子が延び、対象の全画素を上書きする | P-1 | Hider.Core.Tests/Mosaic/MethodConsistencyTests.cs（MethodConsistencyTests、1 件）<br>Hider.Core.Tests/Mosaic/NoiseMethodTests.cs（NoiseMethodTests、5 件） |
| CT123 | CT | 手法まわりの型の値: MethodKind、PlatformSupport のフラグと All、ParamUnit と表示文字列、MosaicInputs の既定値と初期化 | FR-70 | Hider.Core.Tests/Mosaic/MethodTypesTests.cs（MethodTypesTests、5 件） |
| CT124 | CT | パイプラインの前段（Prepare）: ぼかしなしは取得画像そのものと遅延ピラミッド、共通ぼかしは新しい画像、半径のしきい値、手法固有の半径とガウスとして合成、負の半径は 0、端はクランプ。定数（σ / 半径、ピラミッドの段数上限） | 設計書 §4.2 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、8 件） |
| CT125 | CT | パイプラインの画像の作成と解放: Release はぼかし画像と作ったピラミッドを解放し取得画像は解放しない（作っていないピラミッドは元画像だけ、各画像は 1 回）、描画面は GPU 文脈なしならラスタ・不可能なサイズは例外 | NFR-04 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、5 件） |
| CT126 | CT | 描画先の矩形（オフセット・拡大）へのクリップ: シェーダ経路・セル経路・CPU 経路とも矩形の外を描かず、矩形内の既存の画素は置き換える | §6.2 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、4 件） |
| CT127 | CT | ParamSpec の省略可能なメンバーの既定値、レコードの等価と with、% だけが分数で表示倍率 100 | FR-37 | Hider.Core.Tests/Mosaic/ParamSpecTests.cs（ParamSpecTests、3 件） |
| CT128 | CT | ParamValues の指紋（キャッシュのキー）: 解決済みの値をキーの序数順に並べ透明度を足す、一度だけ作る、同じ値なら等しく違えば異なる、カルチャによらない往復形式 | FR-42, D-10 | Hider.Core.Tests/Mosaic/ParamValuesTests.cs（ParamValuesTests、4 件） |
| CT129 | CT | 描画に渡す値の組み立て: ForMosaic は手法値・共通値・透明度（同じキーなら共通値が勝つ、保存値がなければ既定値で設定を変えない、保存値は写す）、ForGenerated はシードとベース色を分数で・透明度 1、共通値は ParamSpec がなくても保つ | FR-35, §4.5 | Hider.Core.Tests/Mosaic/ParamValuesTests.cs（ParamValuesTests、6 件） |
| CT130 | CT | pixelate のセルの色: ブロック以下で最大のピラミッド段を 1 回線形サンプル、ピラミッドが短ければ元画像を最近傍で | §4.5, 設計書 §4.2 | Hider.Core.Tests/Mosaic/PixelateMethodTests.cs（PixelateMethodTests、2 件） |
| CT131 | CT | RasterSampler（CPU 経路の標本化）の作成と最近傍: CPU 画素のない画像は null、8 bit premultiplied の BGRA / RGBA だけ、全テクセルを BGRA で、テクセルの境界で Skia と一致、外は端にクランプ、倍率で座標を割る、行の stride、1 px の画像、二重 Dispose | P-1 | Hider.Core.Tests/Mosaic/RasterSamplerTests.cs（RasterSamplerTests、9 件） |
| CT132 | CT | RasterSampler の線形補間が Skia と一致: テクセル中心はそのテクセル、間は補間して丸める、外は Skia と同じクランプ、ピラミッド段の座標、RGBA は補間前に入れ替える、半透明は保存値のまま補間 | P-1 | Hider.Core.Tests/Mosaic/RasterSamplerTests.cs（RasterSamplerTests、6 件） |
| CT133 | CT | 埋め込み SkSL の列挙と読込: Methods フォルダの .sksl だけを序数順に、本文を読む、未知は例外 | §4.3 | Hider.Core.Tests/Mosaic/ShaderSourceTests.cs（ShaderSourceTests、4 件） |
| CT134 | CT | SkslMethod のシェーダの組み立て: 子シェーダは返す前に解放しても描ける、uniform の既定は実効値（丸めない）、エリアサイズを渡す、シードは 251 進の桁、生成手法のベース色とシードが届く、元のシェーダの既定は取得画像の最近傍・クランプ | §4.3, 指示書 §2.3 | Hider.Core.Tests/Mosaic/SkslMethodTests.cs（SkslMethodTests、7 件） |
| CT135 | CT | 数値入力の正規化: 対応表の各文字（全角数字・記号）を半角に、全角スペースは空白として前後を削る、他の文字はそのまま、全角と半角の混在 | FR-37 | Hider.Core.Tests/NumberTextTests.cs（NumberTextTests、4 件） |
| CT136 | CT | 保存形式への変換: 既定値、位置は物理 px・サイズは DIP（小数 2 桁に丸め）で設定は共有、透明度は生の値で保存、復元はサイズ × 倍率を丸め（0.5 は偶数へ）・透明度はエリアのクランプを通す、全 px サイズが往復で戻る | 指示書 §3.4, FR-60 | Hider.Core.Tests/Persistence/AppStateTests.cs（AppStateTests、9 件） |
| CT137 | CT | 読込の寛容さと厳密さ: 未知のキーは無視、キーの大小文字は区別する、UTF-8 の BOM 付き（メモ帳で編集）も読める | §3.4 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、3 件） |
| CT138 | CT | 読めないファイル（ロック中、壊れていて退避できない）は Unreadable としてそのまま残し、理由をログに出す | FR-64 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、2 件） |
| CT139 | CT | areas.json の pattern: 既知は保持し未知の名前は mosaic（ほかのエリアも残る、ファイルは破損扱いにしない）、大小文字違いは同じ名前、前後の空白は無視、int の端の整数は mosaic、名前でも整数でもなければ破損、保存は仕様の小文字の名前 | §3.4, FR-64 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、7 件） |
| CT140 | CT | 読込時の値の正規化: 透明度はクランプし NaN は不透明、シードはハッシュの範囲へ、モザイク共通値はそれぞれの範囲へ（範囲内は保持、巨大な数もクランプ） | §3.4 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、5 件） |
| CT141 | CT | 読込時の位置とサイズの正規化: 座標は ±2^20 に収める、サイズは最小・最大へ、数でないサイズは新規エリアのサイズ、空の状態は何もしない | FR-63 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、5 件） |
| CT142 | CT | 保存・読込の補助: 保存はフォルダを作り UTF-8（BOM なし）で書く、FromJson は null リテラルで null・不正で例外・正規化しない | FR-60 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、5 件） |
| CT143 | CT | HiderJsonContext: 設定はキャメルケース・インデント・寛容な整数、保存する全型にソース生成のメタデータ、プロパティ名はキャメルケース、辞書のキーは与えたまま | NFR-06 | Hider.Core.Tests/Persistence/JsonContextTests.cs（JsonContextTests、4 件） |
| CT144 | CT | 整数欄の寛容な読み取り（LenientInt32）: 整数欄すべてに使う、有限の数は 0 から遠い方へ丸めて int の範囲にクランプ、数でなければ・double を超えれば例外、書くのは素の整数 | FR-64 | Hider.Core.Tests/Persistence/JsonContextTests.cs（JsonContextTests、5 件） |
| CT145 | CT | restore_token の保存の失敗はログに残して続ける（フォルダがない・パスがフォルダ・フォルダを作れない・.tmp がフォルダ） | §6 Linux, FR-60 | Hider.Core.Tests/Persistence/RestoreTokenStoreCasesTests.cs（RestoreTokenStoreCasesTests、4 件） |
| CT146 | CT | ロックが取れない理由の判定: OS ごとのエラーコードで「他のインスタンスが保持」を判定し、名前が長すぎる・作るフォルダがないなどはエラーとして他インスタンス扱いにしない | FR-08 | Hider.Core.Tests/Persistence/StoragePathsSingleInstanceTests.cs（StoragePathsSingleInstanceTests、3 件） |
| CT147 | CT | タイマーのスレッドと呼び出し側のスレッドが同時に tick と要求を出しても、実行が重ならない | D-10 | Hider.Core.Tests/Scheduling/RefreshSchedulerCasesTests.cs（RefreshSchedulerCasesTests、1 件） |
| CT148 | CT | 投げっぱなしのタスク（Forget）: 失敗はちょうど 1 行でログへ、キャンセルは失敗としない、後で成功・未完了なら何も書かない、内側の原因ではなく最初の本当の例外を、複数の例外は集約として | — | Hider.Core.Tests/TaskExtensionsTests.cs（TaskExtensionsTests、6 件） |
| CT149 | CT | 旧 setting.txt を読まない: setting.txt だけのフォルダは空で始まり、setting.txt は読まれず・動かされず・消されない。areas.json と並んでいても無視される | FR-65 | Hider.Core.Tests/RepositoryRulesTests.cs（RepositoryRulesTests、2 件） |
| CT150 | CT | 製品のソースが禁止事項を使わない: キャプチャ除外 API（SetWindowDisplayAffinity / WDA_*）、Avalonia.Wayland / UseWayland、BeginMoveDrag / BeginResizeDrag、DllImport、実行時のアセンブリ読込、setting.txt、HiderJsonContext を通らない JSON。走査がソースを見ていることとコメントを除くことも確認 | NFR-01、NFR-06、D-1、D-12、FR-65 | Hider.Core.Tests/RepositoryRulesTests.cs（RepositoryRulesTests、9 件） |
| GT41 | GT | 版の表示: 版は InformationalVersion からリビジョンを除いたもの・アセンブリの版と一致・一度だけ読む。--version の出力とトレイメニューの版の行（区切り線はその前だけ） | P-10 | Hider.App.Tests/App/AppResourcesTests.cs（AppResourcesTests、3 件）<br>Hider.App.Tests/App/ProgramTests.cs（ProgramTests、1 件）<br>Hider.App.Tests/Tray/TrayMenuModelTests.cs（TrayMenuModelTests、2 件）<br>Hider.App.Tests/Tray/TrayMenuPanelTests.cs（TrayMenuPanelTests、1 件） |
| GT42 | GT | アイコン: トレイ用は 32 px で共有、窓のアイコンは多サイズの .ico と 48 px の PNG で毎回新しく、資産は埋め込み | FR-71 | Hider.App.Tests/App/AppResourcesTests.cs（AppResourcesTests、4 件） |
| GT43 | GT | アプリの資源: Fluent と ColorPicker のテーマを Light で読み込む、等幅フォントは Windows 用を先頭にした代替つき | UI デザインキャンバス | Hider.App.Tests/App/AppResourcesTests.cs（AppResourcesTests、3 件） |
| GT44 | GT | アプリの起動と組み立て: エリアにプラットフォームのサービスを渡す、UI への投函は Dispatcher で後から、取得を渡さなければプラットフォームの取得、Startup は不要、デスクトップのライフタイムがあるときだけ起動 | 指示書 §6 | Hider.App.Tests/App/HiderAppTests.cs（HiderAppTests、6 件） |
| GT45 | GT | Linux の取得経路の結合: 帯はスクリーンショットポータルを通って撮れ、画像のないポータルでは生成パターンへ切り替わる | FR-52, §6 Linux | Hider.App.Tests/App/HiderAppTests.cs（HiderAppTests、2 件） |
| GT46 | GT | 起動時の復元: 保存済みのエリアを隠しモードで復元、ファイルがなければ空で保存は続く、壊れたファイルは退避して保存は続く、変更の通知先がなくても復元する | FR-02, FR-64 | Hider.App.Tests/App/HiderAppTests.cs（HiderAppTests、1 件）<br>Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、3 件） |
| GT47 | GT | 終了: セッション終了の要求で即保存、終了で保存してすべて止める（トレイの「終了」でライフタイムを閉じる）、二重の Stop / Dispose は無害、開始前の Stop は何もしない | FR-06, FR-61 | Hider.App.Tests/App/HiderAppTests.cs（HiderAppTests、6 件）<br>Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、1 件） |
| GT48 | GT | SIGTERM（Windows 以外）: 登録し、受けたらプロセスを殺さずに保存して終了する。シグナルを扱わない指定なら何も登録しない | FR-61 | Hider.App.Tests/App/HiderAppTests.cs（HiderAppTests、3 件） |
| GT49 | GT | --selftest: Core とプラットフォームの検査を走らせて合格、同梱のヘルパが読めない・API が欠けていれば不合格。Windows では窓を列挙してデスクトップの隅を合成できる | 指示書 §2, P-2 | Hider.App.Tests/App/PlatformSelfTestTests.cs（PlatformSelfTestTests、1 件）<br>Hider.App.Tests/App/ProgramTests.cs（ProgramTests、3 件） |
| GT50 | GT | 二重起動: 2 つ目は何もせず終了コード 0（--verbose なら標準エラーにその旨） | FR-08 | Hider.App.Tests/App/ProgramTests.cs（ProgramTests、2 件） |
| GT51 | GT | 起動の失敗: ロックを開けなければ理由を出して終了コード 2（Windows はメッセージボックス）、表示がなければ成功を装わずに失敗しロックを外す | FR-08 | Hider.App.Tests/App/ProgramTests.cs（ProgramTests、3 件） |
| GT52 | GT | Avalonia の構成: 検出したデスクトップのプラットフォーム（X11 / Win32）と Skia、X11 の WM_CLASS はインストーラが書く StartupWMClass と同じ | D-1, FR-72 | Hider.App.Tests/App/ProgramTests.cs（ProgramTests、2 件） |
| GT53 | GT | 取得の要求と結果の受け渡し: 要求は矩形・帯の高さ・自窓を先頭にした除外窓（ネイティブハンドルがなければエリア自身の項目だけ）、フレームは行ごとに元の行の余白を除いて写す。自分の窓の一覧は 0 のハンドルを除く | §6.1 | Hider.App.Tests/Areas/AreaControllerCaptureTests.cs（AreaControllerCaptureTests、3 件）<br>Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、1 件） |
| GT54 | GT | 取得中の要求とエリアの変化: 取得中の要求は 1 回にまとめて後で、投函した開始の前にパターンが変われば撮らずに実行を終える、開始・完了の前にエリアが消えれば何もしない、削除で取得を取り消し生成パターンへ切り替えない | D-10, NFR-05 | Hider.App.Tests/Areas/AreaControllerCaptureTests.cs（AreaControllerCaptureTests、5 件） |
| GT55 | GT | Linux: ポップアップ（設定・パターンメニュー・全削除の確認）が開いている間は帯撮影を止め、閉じたら撮り直す（最初の帯の前なら続ける、2 つでも 1 回、取り消しの直前に終わった取得はそのまま表示）。Windows では止めない。閉じたのが最後のポップアップのときだけ再開 | NFR-03, NFR-05 | Hider.App.Tests/Areas/AreaControllerCaptureTests.cs（AreaControllerCaptureTests、8 件）<br>Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、3 件）<br>Hider.App.Tests/Areas/AreaOwnerContractTests.cs（AreaOwnerContractTests、2 件）<br>Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、3 件） |
| GT56 | GT | Linux: 帯撮影中のモード切替は取り消して 1 回だけ撮り直す（ポップアップが開いていれば閉じてから、取り消し後の切替でも 1 回、撮影が終わった後なら撮らない、このエリアの撮影でない帯は何も変えない） | NFR-03 | Hider.App.Tests/Areas/AreaControllerCaptureTests.cs（AreaControllerCaptureTests、4 件） |
| GT57 | GT | 編集用の寸法（余白・ハンドル・ツールバーの大きさ）がデザインキャンバスどおり、ツールバーは取得失敗中だけ広い | FR-34 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、2 件）<br>Hider.App.Tests/Areas/EditChromeToolbarTests.cs（EditChromeToolbarTests、2 件） |
| GT58 | GT | エリアの部品の配線: 時計はサービスのもの、モデル・窓・ホスト・フレームを結ぶ、サービスは渡されたまま、窓の部品の取得、通知先がなくても動く、モードの既定は隠し | 指示書 §6 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、2 件）<br>Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、1 件）<br>Hider.App.Tests/Areas/AreaStripHostServicesTests.cs（AreaStripHostServicesTests、2 件）<br>Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT59 | GT | 表示の開始: 窓を隠しモードで置いて表示し、モザイクだけ撮る、タイマーは新規なら OS の既定・復元なら保存した間隔で始める | FR-50, FR-51 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、3 件）<br>Hider.App.Tests/Areas/AreaOwnerContractTests.cs（AreaOwnerContractTests、2 件） |
| GT60 | GT | 更新間隔の変更は今からの周期でタイマーへ（撮影はしない）、モザイク以外はタイマーなし | FR-51, P-5 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、2 件） |
| GT61 | GT | 編集モードへの遷移: すでに編集中なら何もしない、隠しモードからは配置し直して窓を活性化し所有者へ知らせる、見えていない窓は活性化しない | FR-30 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、3 件） |
| GT62 | GT | ドラッグの終了（ポインタのキャプチャを失った場合も）で保存し、新しい位置で撮り直す | FR-50, FR-61 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、1 件）<br>Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT63 | GT | ドラッグ中のタイマー撮影: ライブの取得（Windows）なら続け、帯撮影（Linux）なら止める | FR-38, D-10 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、2 件） |
| GT64 | GT | パターンの切替: 同じなら何もしない、保存・再描画しモザイクのときだけ撮る、モザイクから離れればタイマーを止め戻れば始める | FR-34, FR-61 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、3 件）<br>Hider.App.Tests/Areas/AreaOwnerContractTests.cs（AreaOwnerContractTests、1 件） |
| GT65 | GT | 表示の変化（DPI・画面構成の変化）で置き直す: 編集中は配置し直す、エリアが消えた後は何もしない。窓ホストは変化を通知し（置いた倍率と違うとき・画面構成は常に）、購読者がいなければ何もせず、破棄で止める | FR-13 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、2 件）<br>Hider.App.Tests/Platform/AvaloniaWindowHostTests.cs（AvaloniaWindowHostTests、7 件）<br>Hider.App.Tests/Platform/X11WindowHostTests.cs（X11WindowHostTests、1 件） |
| GT66 | GT | エリアの破棄: 保留中の矢印キーの確定を止め、窓を閉じてフレームを手放す | FR-33 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、1 件） |
| GT67 | GT | 保存のタイミング: デバウンスは 500 ms、即時保存は待っている遅延保存を取り消す、遅延保存は静かになってから 1 回で UI スレッドを通す、書き込みに失敗しても動き続け次で再試行。ドラッグ終了は即時・矢印キーは遅延、設定は即時か遅延を選ぶ | FR-61, P-7 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、5 件）<br>Hider.App.Tests/Areas/AreaOwnerContractTests.cs（AreaOwnerContractTests、2 件） |
| GT68 | GT | 保存・復元の DIP ⇔ px の換算: 保存は位置を px・サイズを DIP で（画面がなければ倍率 1）、復元はサイズ（DIP）を画面の倍率で px に（画面がなければ保存した位置を倍率 1 で） | FR-13, 指示書 §3.4 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、4 件） |
| GT69 | GT | 「すべて編集」と「編集を終了」で全エリアを切り替える（エリアがなければ何もしない） | FR-30, FR-31 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、2 件） |
| GT70 | GT | 削除の管理: 全削除は全窓を閉じて空を保存（Linux の帯撮影中なら取り消して生成パターンへ切り替えない、エリア・通知先がなくても）、一覧にないエリアの削除は何もしない、1 つの削除で他は残る。削除するかは所有者が決める | FR-05, FR-33 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、5 件）<br>Hider.App.Tests/Areas/AreaOwnerContractTests.cs（AreaOwnerContractTests、1 件） |
| GT71 | GT | Linux の帯撮影の開始: 最初の撮影の前は状態が空、対象エリアとモニタの和を記録（画面がなければ空）、全エリアのツールチップを閉じる。Windows では状態もツールチップも触らない | NFR-03 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、5 件） |
| GT72 | GT | 読めない areas.json があるとき（Unreadable）は、即時でも遅延でも保存しない | FR-64 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、1 件） |
| GT73 | GT | 帯撮影の窓操作の橋渡し: 接続前はエリアも画面もない、接続後は AreaManager の状態を読む、接続前・消えたエリアへの呼び出しは窓に触れずに終わる、Id でエリアの窓に届く、取得のキャンセルで UI スレッドを待つのをやめる、Dispatcher が仕事を捨てれば呼び出しを取り消す | NFR-03, NFR-05 | Hider.App.Tests/Areas/AreaStripHostServicesTests.cs（AreaStripHostServicesTests、7 件） |
| GT74 | GT | 取得は UI スレッドではなくスレッドプールで走らせ、結果と失敗をそのまま返す（CLAUDE.md: 取得・合成は専用スレッド） | — | Hider.App.Tests/Areas/AreaStripHostServicesTests.cs（AreaStripHostServicesTests、2 件） |
| GT75 | GT | ドラッグを始めない押下: 右ボタン、透明な余白、ツールバーの背景、エリアに重なった設定ボタン（クリックされる）・完了ボタン（編集を出る） | FR-32, FR-34 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、5 件） |
| GT76 | GT | キー入力の対象外: 編集中の他のキー、隠しモードのキー、フォーカスのある部品が処理済みのキーは何もしない | FR-33 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、3 件） |
| GT77 | GT | ツールバーのボタンとメニュー: パターンのメニュー項目で切替、🗑 で削除・✓ で編集を出る、⚙ で設定ポップアップを開閉、パターンメニューの開閉を知らせる、開いた設定はパターンが変われば作り直す、通知先がなければ何も出さない | FR-34 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、6 件）<br>Hider.App.Tests/Areas/EditChromeToolbarTests.cs（EditChromeToolbarTests、3 件） |
| GT78 | GT | Linux: 撮影中の帯だけを透明にする: 帯の定数、キャンセル済みなら何も透明にせずに例外、フレーム待ちのキャンセルはタイムアウトにしない、Restore でクリップをすぐ消す、クリップは帯の上下を 1 px の余裕で覆い、上端の帯では上に負の帯を作らない | NFR-03 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、6 件） |
| GT79 | GT | ツールチップと読み上げ名: アイコンボタンにツールチップと名前、開いているツールチップの検出と一括で閉じる、設定の行のスライダーと入力欄はラベルにちなむ名前 | UI デザインキャンバス | Hider.App.Tests/Areas/EditChromeToolbarTests.cs（EditChromeToolbarTests、2 件）<br>Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、1 件） |
| GT80 | GT | 描画が失敗しても覆う（行列）: 全倍率・全サイズで全物理画素が覆われる、Skia がなくても生成パターンは覆われる、大きい・極端なエリアも完全に、どの手法でもフレームを描けない・キャンバスが失敗すれば覆い描けるときは普通に描く、生成パターンの下にあるモザイクはフレームがなくても覆わない、キャンバスのどんな例外も捕まえる、1 つのエリアの失敗は他に及ばない、モデルのサイズが違えば覆いは境界に従う、窓でも塗りつぶし色だけ・編集中も透ける画素なし | NFR-05 | Hider.App.Tests/Areas/MosaicPresenterCoverMatrixTests.cs（MosaicPresenterCoverMatrixTests、14 件） |
| GT81 | GT | 覆いの色と範囲: その描画の塗りつぶし色（塗りつぶしパターンと同じ読み方）、透明度・パターンによらず不透明、境界の中だけ、他の画素があるキャンバスでは置き換える | NFR-05 | Hider.App.Tests/Areas/MosaicPresenterCoverMatrixTests.cs（MosaicPresenterCoverMatrixTests、2 件）<br>Hider.App.Tests/Areas/MosaicPresenterCoverTests.cs（MosaicPresenterCoverTests、5 件） |
| GT82 | GT | 失敗の後: 失敗のたびに覆い画像を作らない、正常 → 失敗 → 正常でモザイク → 覆い → モザイク（古い画像を出さない）、キャッシュ済みのモザイクの後に Skia が消えても覆い戻ればキャッシュを使う、失敗後のパターン切替は普通に描く、失敗はログに経路の名前を出し正常時は出さない、失敗してもキャッシュは触らない | NFR-05 | Hider.App.Tests/Areas/MosaicPresenterCoverMatrixTests.cs（MosaicPresenterCoverMatrixTests、5 件）<br>Hider.App.Tests/Areas/MosaicPresenterCoverTests.cs（MosaicPresenterCoverTests、2 件）<br>Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、1 件） |
| GT83 | GT | 描画の経路ごとの覆い: 破棄後も（失敗時・正常なリースでは Skia で・塗りつぶしは従来どおり）、TryDraw はリースなしで偽・正常なら描いてリースを解放・1 回の失敗は覆って真・常に失敗なら偽でもリースを解放、描画操作は覆いのブラシを持ち境界で当たり判定、Skia のリースがなければ Avalonia で覆う、削除したエリアの最後のフレームは覆い | NFR-05 | Hider.App.Tests/Areas/MosaicPresenterCoverMatrixTests.cs（MosaicPresenterCoverMatrixTests、8 件）<br>Hider.App.Tests/Areas/MosaicPresenterCoverTests.cs（MosaicPresenterCoverTests、8 件） |
| GT84 | GT | Presenter の再描画の条件: モデルかサイズがなければ描かない、モザイクは新しいフレーム・値・位置のときだけ描き直す、手法を変えれば描き直し戻しても描く（モザイク・生成とも） | D-10 | Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、5 件） |
| GT85 | GT | フレームのないモザイク（最初のフレームの前・FrameExchange なし・フレームが消された）は生成パターンで覆う | NFR-05, FR-52 | Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、3 件） |
| GT86 | GT | 未知の手法・別の種類の手法は既定の手法で描く（設定画面も既定の手法を表示） | §3.4 | Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、3 件）<br>Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、1 件） |
| GT87 | GT | キャッシュの解放と破棄: ReleaseCaches でモザイク画像を捨て次のフレームで描く（描く前でも可）、破棄後は描かず二重破棄も可、GPU 画像は共有の退避リストで次のいずれかのエリアの描画まで待ち、ラスタ画像はすぐ解放、退避リストは各画像を 1 回だけ解放して空にする | NFR-04 | Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、8 件） |
| GT88 | GT | 全削除の確認ダイアログの窓: 小さく枠なしで最前面、表示して活性化、閉じるボタン・Esc・他の閉じ方はすべて偽、最初の答えだけが有効 | FR-05 | Hider.App.Tests/Dialogs/ConfirmDeleteWindowTests.cs（ConfirmDeleteWindowTests、6 件） |
| GT89 | GT | 画面の列挙: 全画面を物理 px の情報に順に変換、TopLevel がなければ空、読むたびに問い合わせる。ScreenInfo は値のレコード | FR-13 | Hider.App.Tests/Platform/AvaloniaScreenServiceTests.cs（AvaloniaScreenServiceTests、3 件）<br>Hider.App.Tests/Platform/ScreenServiceExtensionsTests.cs（ScreenServiceExtensionsTests、1 件） |
| GT90 | GT | カーソルのある画面: ポインタが画面上ならその画面の範囲、どの画面の外・ポインタ不明・ポインタ源なし・TopLevel なしは null（画面を問い合わせない）、Windows 以外はポインタを問わない・Windows はカーソル位置の画面。ScreenAt は点を含む画面、外なら主画面 | FR-14 | Hider.App.Tests/Platform/AvaloniaScreenServiceTests.cs（AvaloniaScreenServiceTests、8 件）<br>Hider.App.Tests/Platform/ScreenServiceExtensionsTests.cs（ScreenServiceExtensionsTests、3 件） |
| GT91 | GT | 窓ホストの基本: ハンドルは窓のプラットフォームハンドル（閉じた窓は 0）、ホストする窓。X11 のホストも AvaloniaWindowHost | 指示書 §6 | Hider.App.Tests/Platform/AvaloniaWindowHostTests.cs（AvaloniaWindowHostTests、3 件）<br>Hider.App.Tests/Platform/X11WindowHostTests.cs（X11WindowHostTests、1 件） |
| GT92 | GT | 窓の倍率: 置く前は窓の位置の画面（どの画面の外なら主画面）、既定の ScaleFor は左上の角の画面か主画面、置いた後は置いた倍率。Windows ではモニタの DPI | FR-13 | Hider.App.Tests/Platform/AvaloniaWindowHostTests.cs（AvaloniaWindowHostTests、4 件）<br>Hider.App.Tests/Platform/Windows/Win32WindowHostTests.cs（Win32WindowHostTests、1 件） |
| GT93 | GT | libhidercap の読込と ABI: 定数が C ヘッダと一致、テストの隣の本物のライブラリを読める | P-2 | Hider.App.Tests/Platform/HidercapNativeTests.cs（HidercapNativeTests、2 件） |
| GT94 | GT | libhidercap のエラー経路（本物の .so）: 未開始ではどの呼び出しも安全、相手が切ればすぐストリームエラー、無応答はタイムアウトで二重の start は拒否、停止後は何度でも再開できる、不正な fd・poll できない fd はストリームエラーで停止のまま | P-2, NFR-05 | Hider.App.Tests/Platform/HidercapNativeTests.cs（HidercapNativeTests、6 件） |
| GT95 | GT | PipeWireGrabber のセッション: 設計の定数、時計がなければシステム時計、準備なしで使えるのはセッションが開いている間だけ、破棄後の Prepare / Grab は何もしない、撮影中の破棄はセッションを残すが使えずアイドルタイマーなし、アイドルで閉じる（失敗はログで続行・開いていなければ何もしない）、破棄で閉じるのが固まれば待ちを打ち切る・失敗はログ、最初の撮影で大きなバッファが要れば頼み直して保つ | §6 Linux | Hider.App.Tests/Platform/PipeWireGrabberTests.cs（PipeWireGrabberTests、10 件） |
| GT96 | GT | 共有されたモニタへの対応付けと読む行: 共有モニタが不明なら準備は偽で撮影は null（ログ）、エリアの一部がモニタの外なら null、画面がなければ対応なし、表示高さ 0 は倍率 1、行はフレームに収める | §6 Linux, §11-8 | Hider.App.Tests/Platform/PipeWireGrabberTests.cs（PipeWireGrabberTests、5 件） |
| GT97 | GT | PipeWire 撮影の結果とログ: フレームの古さと時刻の出所をログ、タイムアウトは最後の番号とログ、ヘルパのエラーで閉じてしばらく再試行しない、ヘルパの待ちの間のキャンセルは例外でセッションを保つ、キャンセル済みなら Prepare / Grab は例外 | NFR-03, FR-52 | Hider.App.Tests/Platform/PipeWireGrabberTests.cs（PipeWireGrabberTests、5 件） |
| GT98 | GT | 実 PipeWire（私的なデーモン）と本物のヘルパでの撮影: 全画素形式を不透明な BGRA に、市松模様の行をその位置へ（一部の行でもセルの境界を保つ）、バッファが小さければ必要なサイズを返して何も写さない、頼んだ行だけ写す。ScreenCast ポータルと本物のヘルパで PipeWireGrabber がストリームを撮れる | §6 Linux, P-11 | Hider.App.Tests/Platform/PipeWireIntegrationTests.cs（PipeWireIntegrationTests、6 件） |
| GT99 | GT | ポータルへの接続: 名前と 2 分の期限の定数、Response の内容、接続は 1 回で再利用・同時でも 1 本、相手がいなければ例外でバスが来たら接続、キャンセルで例外、Reset で次は接続し直す、Dispose で接続を閉じ再接続しない、Entry は文字列の variant を書く | §6 Linux | Hider.App.Tests/Platform/PortalClientTests.cs（PortalClientTests、9 件） |
| GT100 | GT | ポータルの Request / Response: 成功で結果を返しポータルが期待する形で要求を送る、要求ごとに新しいトークン、結果なしは空の辞書、他のコードはそのまま返す、他の要求の応答は無視、返答より先の応答も取りこぼさない | §6 Linux | Hider.App.Tests/Platform/PortalClientTests.cs（PortalClientTests、6 件） |
| GT101 | GT | ポータルの要求の失敗とキャンセル: 応答前のキャンセル・キャンセル済みでも要求を Close して例外、期限切れは Close してタイムアウト（Close の失敗はログ）、ポータルがない・メソッドの失敗はそのエラー、本文の書き込みが投げれば何も送らない、CloseObjectAsync は Close を送り失敗はログだけ、壊れた応答・バスの消失・待ち中の Reset はすぐ失敗 | §6 Linux, NFR-05 | Hider.App.Tests/Platform/PortalClientTests.cs（PortalClientTests、12 件） |
| GT102 | GT | スクリーンショットポータルでの撮影（第 1 段階）: 応答の前は呼ばずに null、準備で 1 回撮り帯ごとに撮る・ファイルを残さない、エスケープされた URI も読んで消す、Dispose で接続を閉じ以後は静かに失敗 | §6 Linux, FR-53 | Hider.App.Tests/Platform/PortalScreenshotGrabberTests.cs（PortalScreenshotGrabberTests、4 件） |
| GT103 | GT | スクリーンショットの失敗: URI なし・file でない URI・別の種類・読めない PNG は null（得たファイルは消す、別の種類の後は新しい接続）、ユーザーの拒否は Denied、他の応答は null でログ、ポータルがなければ null で後から使える、キャンセルは伝えて要求を閉じる、ファイルを消せなければログ | FR-52, FR-53 | Hider.App.Tests/Platform/PortalScreenshotGrabberTests.cs（PortalScreenshotGrabberTests、9 件） |
| GT104 | GT | PNG の読込: ファイルがない・画像でない・画像データが壊れていれば null、途中で切れた PNG は読めた分 | FR-52 | Hider.App.Tests/Platform/PortalScreenshotGrabberTests.cs（PortalScreenshotGrabberTests、4 件） |
| GT105 | GT | テスト用の私的な dbus-daemon: 自分のソケットで待ち受け Dispose でフォルダを消す、待ち受けられなければ標準エラー付きで例外にしてフォルダを消す | P-11 | Hider.App.Tests/Platform/PrivateBusTests.cs（PrivateBusTests、2 件） |
| GT106 | GT | ScreenCast ポータル（第 2 段階）の開始と終了: セッションはポータルが渡したものを持つ、成功は 4 段階（CreateSession / SelectSources / Start / OpenPipeWireRemote）でセッションを返す、位置・サイズのないストリームは 0、2 つのストリームは最初、セッションごとに新しいトークン、Close で fd を手放しセッションを閉じる（失敗はログ、バスがなければ fd だけ）、Dispose で接続を捨て以後は null | §6 Linux | Hider.App.Tests/Platform/ScreenCastPortalTests.cs（ScreenCastPortalTests、9 件） |
| GT107 | GT | ScreenCast の拒否と失敗: 各段階のキャンセルは Denied（開いたセッションは閉じる）、Start の失敗は null でセッションを閉じ次は接続し直す、CreateSession の失敗・ハンドルなしは null、ストリームなし・形の違うストリーム・OpenPipeWireRemote の失敗は null でセッションを閉じる、ダイアログ中のキャンセルは要求とセッションを閉じて例外、ポータル・バスがなければ null | FR-52, NFR-05 | Hider.App.Tests/Platform/ScreenCastPortalTests.cs（ScreenCastPortalTests、12 件） |
| GT108 | GT | 主画面の決め方: 印のある画面（一覧のどこでも、複数なら最初）、印がなければ最初、画面がなければ null。主画面の番号（印なし・画面なしは 0） | FR-63 | Hider.App.Tests/Platform/ScreenServiceExtensionsTests.cs（ScreenServiceExtensionsTests、6 件） |
| GT109 | GT | セッションバスのアドレス: なければポータルのクライアントは接続せず、トレイなし、スクリーンショットは準備できず、ScreenCast は null（Close は fd だけ）。あればそれぞれそのバスを使う | D-9, P-11 | Hider.App.Tests/Platform/SessionBusAddressTests.cs（SessionBusAddressTests、8 件） |
| GT110 | GT | トレイの有無の判定: KDE の StatusNotifierWatcher の名前に所有者がいれば真、いない・いなくなった・バスがなければ偽（ログ） | D-9 | Hider.App.Tests/Platform/StatusNotifierProbeTests.cs（StatusNotifierProbeTests、5 件） |
| GT111 | GT | Windows の取得（UnderlayCapture）: ライブで露出なし、キャンセル済みなら撮る前に例外、二重 Dispose は無害、デスクトップの隅を合成しライブなエリアごとにバッファを 1 つ、自分の窓はエリアの下に合成、除外窓がなければ共有のバッファ 1 つ | NFR-02, §6.2 | Hider.App.Tests/Platform/Windows/Win32CaptureTests.cs（Win32CaptureTests、6 件） |
| GT112 | GT | 窓ごとの取得の期限: 呼び出しの結果を別タスクで返す、固まった窓は期限でタイムアウト、前の呼び出しが固まっていれば呼ばずにすぐタイムアウト、他の窓は撮れる、Trim は固まった呼び出しを戻るまで残す、戻った後は呼び直す、投げる呼び出しは Unavailable で次は走る・合成器を通すと取得の失敗、投げても取得後のキャッシュの整理は走る | 設計書 §5.2 | Hider.App.Tests/Platform/Windows/Win32CaptureTests.cs（Win32CaptureTests、9 件） |
| GT113 | GT | 窓の列挙と PrintWindow での取得: 可視の窓を Z 順（最前面から）に列挙、空の窓は Unavailable、消えた窓は黒でキャッシュしない、自分の窓も一覧にあり上下正しく BGRA で写し後で忘れる、実際の窓のビットマップを写し Trim で忘れる、新しいソースは何も列挙していない・キャッシュがなければ Trim は何もしない | §6.2 | Hider.App.Tests/Platform/Windows/Win32CaptureTests.cs（Win32CaptureTests、8 件） |
| GT114 | GT | Windows: --version / --selftest の出力を親のコンソールへ付け替える（両方リダイレクトなら付けない、親のコンソールがなければ開かない、何もリダイレクトされていなければ空行の後に両方、片方だけならもう片方を、開けない・最初の行が書けなければそのまま、他の失敗は握りつぶさない） | 指示書 §2 | Hider.App.Tests/Platform/Windows/Win32ConsoleTests.cs（Win32ConsoleTests、8 件） |
| GT115 | GT | Win32 の構造体（RECT・BITMAPINFOHEADER）と定数が Windows のヘッダと一致し、呼び出しは窓がなくても例外にならない（画面・メモリ DC、モニタと DPI、カーソル位置） | §6.2 | Hider.App.Tests/Platform/Windows/Win32NativeTests.cs（Win32NativeTests、7 件） |
| GT116 | GT | % の値: 表示は整数の %、入力は分数で保存、刻みは最低 1 % | FR-37 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、3 件） |
| GT117 | GT | 入力欄の確定: 入力値を出す、数でなければ表示を戻す、同じ値なら出さずに表示を整える、Enter で確定して処理済み、他のキーでは確定しない、フォーカスを離れると確定 | FR-37 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、6 件） |
| GT118 | GT | スライダーのキー操作: 矢印は Step と同じ刻みで処理済み、Home / End はスライダーに任せて端へ、他のキーは動かさない。刻みはトラックの 1 %（大きくは 10 %）、表示の 1 単位未満なら 1 単位、端では動かず何も出さない、端の値からは内側へ動く | FR-37 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、7 件） |
| GT119 | GT | モザイク共通の行（ぼかし・階調・更新間隔・帯の高さ）: 全行に対応する欄があり、保存値を表示し自分の欄へ書く（整数は丸める）、更新間隔の入力欄は広い、未知のキーは 0 を表示せず例外 | FR-37 | Hider.App.Tests/Settings/SettingsPanelCommonTests.cs（SettingsPanelCommonTests、4 件）<br>Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、3 件） |
| GT120 | GT | 設定ポップアップの構成: 見出しとパターンはエリアに従う、部品はそのパターンのものだけ、作り直しで行とラベルを置き換える | FR-34, FR-37 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、3 件） |
| GT121 | GT | 透明度の行: 範囲はパターンで変わり、実効の透明度を表示する | §4.5 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、2 件） |
| GT122 | GT | 手法の行: 保存値を表示しなければ既定値（何も保存されていなければ全部既定値）、今の手法の値に書く | FR-35, FR-37 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、3 件） |
| GT123 | GT | 手法の選択欄: その OS の手法を名前付きで並べる、同じ手法・選択なしなら何も変えない、別の手法なら保存してその手法で作り直す | FR-35, D-5 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、3 件） |
| GT124 | GT | 色の選択: 色ボタンは見本・16 進・名前を示す、ピッカーで選んだ色は塗りつぶしに反映して遅れて保存し一致するプリセットを強調、アルファだけ違う同じ色は何もしない、生成パターンではベース色に | §4.5 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、4 件） |
| GT125 | GT | シード欄: Enter で範囲にクランプして保存、数でない・変わらなければ表示を戻して保存しない、Enter は処理済みで他のキーは違う、フォーカスを離れると確定、保存値を表示し名前付き、再抽選は範囲内の新しいシードを表示して保存 | FR-40 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、6 件） |
| GT126 | GT | メニュー項目の実行: NativeMenu のクリックで項目のコマンド、追加は編集モードのエリア、すべて編集の後は編集を終了、終了は終了の処理だけ、版の行・未知のコマンドは何もしない | FR-03, FR-06 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、5 件） |
| GT127 | GT | トレイのクリック: Windows は左クリックでカーソルの左上（近く）にポップアップ（カーソルがなければ位置未定で）、Windows 以外は何も開かずカーソルも問わない。ポップアップ窓は枠なし最前面でタスクバーに出ず、項目を示し、クリックで先に閉じてからコマンド、フォーカスを失えば閉じる | FR-03 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、5 件）<br>Hider.App.Tests/Tray/TrayWindowTests.cs（TrayWindowTests、5 件） |
| GT128 | GT | トレイがないときの代替窓（D-9）: 探索でトレイがあればログだけで窓を出さない、なければログを出し窓がコマンドを実行、探索の失敗はログで窓なし、破棄後の応答では窓を出さない。代替窓は普通の固定サイズの窓で説明の題とアイコン、項目と状態を示し、クリックでコマンドを出して開いたまま、ユーザーが閉じれば最小化・終了では閉じる、セッション・アプリの終了は妨げない、他の閉じ方は拒んで最小化 | D-9 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、4 件）<br>Hider.App.Tests/Tray/TrayWindowTests.cs（TrayWindowTests、7 件） |
| GT129 | GT | トレイの破棄: 全窓を閉じエリアへの追従をやめる、何も開いていなくても投げない | FR-06 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、2 件） |
| GT130 | GT | メニュー窓のデザイン: ボタンなしで始まり間隔はデザインどおり、アイコンのあるコマンドはパスを持ち左の余白が少ない、ボタンの体裁 | UI デザインキャンバス | Hider.App.Tests/Tray/TrayMenuPanelTests.cs（TrayMenuPanelTests、3 件） |
| GT131 | GT | エリアは上限なく作れる: 64 個がそれぞれ別の Id と窓を持ち、すべて保存され、別のマネージャで復元される | FR-10 | Hider.App.Tests/Areas/AreaManagerFrTests.cs（AreaManagerFrTests、1 件） |
| GT132 | GT | 最後に操作したエリアが前: 編集に入ったエリアの窓が順に活性化される（OS がそれを他の最前面窓の前に出す）、すべての窓が最前面 | FR-12 | Hider.App.Tests/Areas/AreaManagerFrTests.cs（AreaManagerFrTests、2 件） |

## 既存 ID の細分化

既存の項目の内容はタブのとおり。ここには今回その ID を付けたテストが何を細かく検証しているかを書く。

| ID | 種別 | 細分化した内容 | 対応 FR・NFR・決定 | ファイル / クラス |
|---|---|---|---|---|
| CT01 | CT | 辺からの生成はどの角の順でも正規化、同じ辺なら空、Normalized は正なら不変・負なら原点を反転 | — | Hider.Core.Tests/Model/PhysicalRectTests.cs（PhysicalRectTests、3 件） |
| CT02 | CT | 最小サイズ: 最小未満の矩形から 0 のドラッグで最小へ（片側・両側）、最小は 24 DIP の切り上げ、反対側の辺は固定、倍率に従う、EnsureMinSize は小さい辺だけ広げ原点を保つ・負のサイズは先に正規化 | FR-32 | Hider.Core.Tests/Model/AreaGeometryCasesTests.cs（AreaGeometryCasesTests、7 件） |
| CT03 | CT | 透明度: 0〜1 にクランプし NaN は不透明、実効値はパターンに従う、未定義のパターンはアルファの範囲、下限 10% | §4.5 | Hider.Core.Tests/Model/AreaTests.cs（AreaTests、5 件） |
| CT03 | CT | 0〜1 の外の透明度はクランプして描く | §4.5 | Hider.Core.Tests/Mosaic/MethodBoundaryTests.cs（MethodBoundaryTests、1 件） |
| CT03 | CT | ParamValues の透明度は 0〜1 | §4.5 | Hider.Core.Tests/Mosaic/ParamValuesTests.cs（ParamValuesTests、1 件） |
| CT04 | CT | hexcell の半径は透明度 0 で 1 へ | §4.5 | Hider.Core.Tests/Mosaic/HexcellMethodTests.cs（HexcellMethodTests、1 件） |
| CT04 | CT | Lerp なしは透明度によらずクランプ値、Lerp ありの既定の下限は 1 | §4.5 | Hider.Core.Tests/Mosaic/ParamSpecTests.cs（ParamSpecTests、2 件） |
| CT04 | CT | Lerp を求める ParamSpec だけ補間 | §4.5 | Hider.Core.Tests/Mosaic/ParamValuesTests.cs（ParamValuesTests、1 件） |
| CT04 | CT | pixelate のブロックは透明度 0 で 1 へ | §4.5 | Hider.Core.Tests/Mosaic/PixelateMethodTests.cs（PixelateMethodTests、1 件） |
| CT05 | CT | blur の半径は透明度 0 で 0 へ、透明度 50% で半分 | §4.5 | Hider.Core.Tests/Mosaic/BlurMethodTests.cs（BlurMethodTests、2 件） |
| CT05 | CT | 共通ぼかしは透明度に応じて弱まる | §4.5 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、1 件） |
| CT05 | CT | Lerp の下限が箱の最小より下（0） | §4.5 | Hider.Core.Tests/Mosaic/ParamSpecTests.cs（ParamSpecTests、1 件） |
| CT07 | CT | 更新間隔は 0 に届く対数スライダー | 決定 6 | Hider.Core.Tests/Mosaic/CommonParamsTests.cs（CommonParamsTests、1 件） |
| CT07 | CT | 0 の帯は 2%、線形・0 の帯つき対数・0 の帯なし対数の位置 ⇔ 値、全手法の全 ParamSpec で往復 | 決定 6 | Hider.Core.Tests/Mosaic/ParamSpecTests.cs（ParamSpecTests、8 件） |
| CT08 | CT | 保存値が null なら全 ParamSpec が既定値、同じキーの後の ParamSpec が勝つ、Raw は保存値・なければ既定値か 0 | FR-35 | Hider.Core.Tests/Mosaic/ParamValuesTests.cs（ParamValuesTests、4 件） |
| CT10 | CT | Area・AreaDefaults の既定値、CreateNew の Id・矩形・OS 別の更新間隔 | FR-14, FR-51 | Hider.Core.Tests/Model/AreaTests.cs（AreaTests、4 件） |
| CT10 | CT | 新規エリアは倍率に応じた 400×240 DIP で中央、小さい画面では画面に収める | FR-14 | Hider.Core.Tests/Model/PlacementCasesTests.cs（PlacementCasesTests、2 件） |
| CT11 | CT | AreaId: 8 桁の小文字 16 進、共有の乱数、空いていれば最初の抽選、小さい数は 0 埋め、8 桁の全域、0〜2^32 を求める | — | Hider.Core.Tests/Model/AreaIdColorTextTests.cs（AreaIdColorTextTests、6 件） |
| CT12 | CT | 未知のキーはキーそのもの、単位の文字列（未定義は例外）、固有名詞 | FR-70 | Hider.Core.Tests/StringsTests.cs（StringsTests、4 件） |
| CT13 | CT | 一覧の順序、組み込み手法の Id・ラベル・種類、種類ごとの分割 | D-5 | Hider.Core.Tests/Mosaic/MethodRegistryTests.cs（MethodRegistryTests、3 件） |
| CT14 | CT | 全シェーダがコンパイルでき、毎回新しい効果を返す | §4.3 | Hider.Core.Tests/Mosaic/ShaderSourceTests.cs（ShaderSourceTests、1 件） |
| CT14 | CT | 効果は Id と同名のシェーダから 1 回だけコンパイル | §4.3 | Hider.Core.Tests/Mosaic/SkslMethodTests.cs（SkslMethodTests、1 件） |
| CT15 | CT | 共通パラメータのキー = JSON 名 = uniform 名、種類ごとの固定 uniform、全シェーダが固定 uniform を宣言 | 指示書 §2.3 | Hider.Core.Tests/Mosaic/CommonParamsTests.cs（CommonParamsTests、3 件） |
| CT15 | CT | 先頭コメントの 1 行目が手法名で、宣言した uniform と child を列挙、パラメータの uniform は float | 指示書 §2.3 | Hider.Core.Tests/Mosaic/ShaderSourceTests.cs（ShaderSourceTests、3 件） |
| CT16 | CT | 組み込み手法は全 OS で動く、フィルタは要求したフラグをすべて要し順序を保つ、現在の OS の判定 | — | Hider.Core.Tests/Mosaic/MethodRegistryTests.cs（MethodRegistryTests、3 件） |
| CT16 | CT | SkslMethod の対応 OS の既定は All | — | Hider.Core.Tests/Mosaic/SkslMethodTests.cs（SkslMethodTests、1 件） |
| CT17 | CT | モザイクは取得画像なしで例外、生成は取得画像なしで動く | — | Hider.Core.Tests/Mosaic/SkslMethodTests.cs（SkslMethodTests、2 件） |
| CT18 | CT | Find は登録済みの実体を返す、未知は null、種類を指定するとその種類だけ | — | Hider.Core.Tests/Mosaic/MethodRegistryTests.cs（MethodRegistryTests、3 件） |
| CT20 | CT | 全手法・全パラメータのラベルキーが Strings にある | FR-70 | Hider.Core.Tests/StringsTests.cs（StringsTests、1 件） |
| CT21 | CT | AppState の全フィールドと各型が単独で往復 | FR-60 | Hider.Core.Tests/Persistence/JsonContextTests.cs（JsonContextTests、2 件） |
| CT22 | CT | ToJson はインデント付きのキャメルケース | D-4 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、1 件） |
| CT23 | CT | 正しい種類の既知の手法と、未知のモザイク手法は保持 | §3.4 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、2 件） |
| CT24 | CT | 欠損の補完: 空の areas、areas キーなし、null の設定・共通値・params・手法の params、空の手法 Id は既定、正規化で全登録手法が値を持つ、FillDefaults は欠けを既定・あれば範囲へ・NaN は既定・余分なキーは残す・指定の種類だけ・null の項目は置き換え既存は同じ実体 | §3.4 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、10 件） |
| CT25 | CT | version がなければ 1、version 1 の形でなければ Broken | FR-64 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、2 件） |
| CT26 | CT | 3 つ目の壊れたファイルは古い 2 つを残す、名前付きの浮動小数点リテラル（NaN 等）は破損 | FR-64 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、2 件） |
| CT27 | CT | 残っていた長い .tmp を丸ごと置き換える、ファイル全体を置き換える、.tmp がフォルダ・名前が長すぎ・保存先がフォルダなら例外で既存ファイルを壊さず .tmp を残さない | FR-62 | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、5 件） |
| CT28 | CT | LoadStatus の値、パスがフォルダなら Missing | — | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、2 件） |
| CT29 | CT | 色は大文字に、不正なら既定色 | — | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、1 件） |
| CT30 | CT | なければフォルダを作りロックファイルを残す、既存のロックファイルは中身を変えずに使う、二重 Dispose は無害 | FR-08 | Hider.Core.Tests/Persistence/StoragePathsSingleInstanceTests.cs（StoragePathsSingleInstanceTests、3 件） |
| CT31 | CT | ファイル名、Windows は USERPROFILE だけ・Linux は HOME だけを見る、変数は 1 つだけ問い合わせる、HOME が / ならルートの .hider、現在のプロセスに従う | FR-60 | Hider.Core.Tests/Persistence/StoragePathsSingleInstanceTests.cs（StoragePathsSingleInstanceTests、6 件） |
| CT32 | CT | 画面がなければ正規化だけ、負のサイズは判定の前に正規化、辺が接するだけでは画面上でない | FR-63 | Hider.Core.Tests/Model/PlacementCasesTests.cs（PlacementCasesTests、3 件） |
| CT33 | CT | 主画面の番号が範囲外なら最初 / 最後の画面、中央寄せの端数は原点側へ | FR-63 | Hider.Core.Tests/Model/PlacementCasesTests.cs（PlacementCasesTests、3 件） |
| CT34 | CT | 大きすぎる辺だけ縮める、全画面の外接矩形ちょうどは保つ | FR-63 | Hider.Core.Tests/Model/PlacementCasesTests.cs（PlacementCasesTests、2 件） |
| CT35 | CT | どの画面と交差してもそのまま（2 枚目の画面でも） | FR-13 | Hider.Core.Tests/Model/PlacementCasesTests.cs（PlacementCasesTests、1 件） |
| CT37 | CT | 間隔は 0 から始まり Configure に従う、負は 0 でタイマー停止（停止中なら停止のまま）、停止したタイマーの tick は無視 | FR-51 | Hider.Core.Tests/Scheduling/RefreshSchedulerCasesTests.cs（RefreshSchedulerCasesTests、4 件） |
| CT38 | CT | タイマーの期限はタイムスタンプの周波数で計算する | FR-50 | Hider.Core.Tests/Scheduling/RefreshSchedulerCasesTests.cs（RefreshSchedulerCasesTests、1 件） |
| CT39 | CT | 契機の値、ハンドラがなければ要求で実行しない、全ハンドラが理由を受け取り実行は 1 回 | FR-50 | Hider.Core.Tests/Scheduling/RefreshSchedulerCasesTests.cs（RefreshSchedulerCasesTests、3 件） |
| CT40 | CT | Configure: 同じ間隔ならタイマーを再開しない、0 からは今から 1 周期、長くしたら予定の tick の後に新しい間隔、短くても早まらなければ予定どおり、Dispose 後は無視。Dispose: タイマー停止・冪等・合流中の要求を捨てる、Dispose 後の tick は無視。TimerPaused（ドラッグ中）: 取得・設定、tick を飛ばしてもリズムは保つ、実行中に合流した tick は EndRun で捨てる（その前に再開すれば出す）、要求は止めない。合流: 要求は tick を置き換える・tick は要求を置き換えない・最初の要求を保つ・ハンドラ内の要求は EndRun の後、実行なしの EndRun は何もしない、ハンドラが外れていれば合流した要求を捨てる | D-10, FR-38, P-5 | Hider.Core.Tests/Scheduling/RefreshSchedulerCasesTests.cs（RefreshSchedulerCasesTests、19 件） |
| CT43 | CT | パススルーでは元のシェーダが取得画像 | 決定 13 | Hider.Core.Tests/Mosaic/PixelateMethodTests.cs（PixelateMethodTests、1 件） |
| CT44 | CT | 実効ブロックは切り上げ、ParamSpec なしでも 1 以上 | §4.5 | Hider.Core.Tests/Mosaic/PixelateMethodTests.cs（PixelateMethodTests、2 件） |
| CT45 | CT | エリアの原点を動かしてもセルは動かない | — | Hider.Core.Tests/Mosaic/SkslMethodTests.cs（SkslMethodTests、1 件） |
| CT46 | CT | 階調フィルタは段数ごとにキャッシュ・2 未満は 2、値は floor(c·L/(L−1))・アルファは不変、シェーダ経路とセル経路の両方に掛かる | §4.5 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、3 件） |
| CT47 | CT | 階調 256 以上はフィルタなし | §4.5 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、1 件） |
| CT50 | CT | 遅延ピラミッドの一覧の振る舞い、段数の上限、サイズは切り上げ・1×1 か上限で止まる、奇数サイズは最後の列を繰り返す、2 引数版も同じ | 設計書 §4.2 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、5 件） |
| CT53 | CT | 目地: 幅 0 なら描かない、各セルの右と下をベース色で、エリアに入る最後の目地も描く、セルより広い目地は全面、最大のセルと目地 | §4.5 | Hider.Core.Tests/Mosaic/BlockMethodTests.cs（BlockMethodTests、5 件） |
| CT54 | CT | セルの色はベース色をハッシュでずらし、範囲の端でクランプ | §4.5 | Hider.Core.Tests/Mosaic/BlockMethodTests.cs（BlockMethodTests、2 件） |
| CT55 | CT | 生成画像は不透明な BGRA（premultiplied） | §4.5 | Hider.Core.Tests/Mosaic/GeneratedCacheTests.cs（GeneratedCacheTests、1 件） |
| CT56 | CT | noise もエリアの原点によらない | FR-40 | Hider.Core.Tests/Mosaic/NoiseMethodTests.cs（NoiseMethodTests、1 件） |
| CT57 | CT | 新しいキャッシュは何も描かない、手法をサイズで描く、同じキーなら描かずに同じ画像、手法・サイズ・値・ベース色が変われば描き直して古い画像を解放、キーは要求したサイズ | FR-42 | Hider.Core.Tests/Mosaic/GeneratedCacheTests.cs（GeneratedCacheTests、6 件） |
| CT59 | CT | 空のサイズは 1 px 以上で描く | FR-32 | Hider.Core.Tests/Mosaic/GeneratedCacheTests.cs（GeneratedCacheTests、1 件） |
| CT59 | CT | 空のエリアサイズは 1 px として描く | FR-32 | Hider.Core.Tests/Mosaic/MosaicPipelineTests.cs（MosaicPipelineTests、1 件） |
| CT61 | CT | すでに覆われた部分しかない窓は飛ばし、下の窓が残りを埋める | §6.2 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、1 件） |
| CT62 | CT | 空の矩形は窓を列挙せずに Unavailable | §6.2 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、1 件） |
| CT63 | CT | 除外する窓がなければ最前面の窓から数える | §6.1 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、1 件） |
| CT64 | CT | 矩形が空・フレームと交わらない窓は対象外 | §6.2 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、1 件） |
| CT65 | CT | 自分の窓が一覧になければ最前面から | §6.2 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、1 件） |
| CT66 | CT | 自分が最下層で下に何もなければ不透明な黒 | §6.2 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、1 件） |
| CT67 | CT | 黒判定: 失敗なしで画像もなければ黒、アルファによらず RGB が 0 なら黒、どれかの色があれば黒でない、行の余白は見ない | FR-52 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、4 件） |
| CT69 | CT | タイムアウト: 既定と指定の期限、取得に期限を渡す、除外窓がなくても次回スキップ、他の失敗は取得を失敗させるがスキップしない、タイムアウトで走査を止める、スキップした窓の部分は下から、スキップは次の取得で使い切る（失敗しても）・新しいタイムアウトで置き換わる・空の要求では残る | 設計書 §5.2 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、9 件） |
| CT70 | CT | 窓より小さい画像の欠けた画素は下の窓から | §6.2 | Hider.Core.Tests/Capture/UnderlayComposerEdgeTests.cs（UnderlayComposerEdgeTests、1 件） |
| CT72 | CT | セル中心は最も近い格子点（辺・角を含む）、六角形ごとに 1 色 | — | Hider.Core.Tests/Mosaic/HexcellMethodTests.cs（HexcellMethodTests、3 件） |
| CT73 | CT | 実効半径 1 でパススルー、ParamSpec なしで半径 1 未満ならシェーダは恒等 | 決定 13 | Hider.Core.Tests/Mosaic/HexcellMethodTests.cs（HexcellMethodTests、2 件） |
| CT75 | CT | 前段ぼかしの半径は実効値、シェーダはぼかした入力をそのまま通す（常にパススルー）、半径 0 は恒等、最大半径でほぼ平坦・上限超えはクランプ | §4.5 | Hider.Core.Tests/Mosaic/BlurMethodTests.cs（BlurMethodTests、5 件） |
| CT77 | CT | 段数は整数に丸める、最大の段数、色は範囲の端でクランプ | §4.5 | Hider.Core.Tests/Mosaic/NoiseMethodTests.cs（NoiseMethodTests、3 件） |
| CT79 | CT | 帯の列挙: 行がなければ帯なし、行の切り方、帯がエリアより高ければ 1 回 | §6 Linux | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、3 件） |
| CT81 | CT | 帯の協調が必要な取得であることを Traits で示す | NFR-03 | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、1 件） |
| CT82 | CT | 時計を渡さなければシステム時計で notBefore を決める | 設計書 §5.3 | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、1 件） |
| CT83 | CT | 透明化の対象: 覆うエリアがなければ透明化せずに撮る、帯の途中で終わるエリアは自分の行だけ、要求より上から始まるエリアは自分の上端から数える | §6 Linux | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、3 件） |
| CT84 | CT | 行のコピー（CopyRows）: 空の範囲は倍率 1、指定行だけ、フレーム外は端を繰り返す、右端だけの遅い経路も正確、縮小フレームは最も近い画素中心、広い stride の残りは触らない | — | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、6 件） |
| CT85 | CT | 失敗: 空の矩形は何も頼まず Unavailable、Restore の失敗はログに残し他のエリアは戻す、透明化の失敗は伝えて触ったエリアをすべて戻す、拒否はログ、帯の一部が欠けたフレームは Unavailable | FR-52, NFR-05 | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、5 件） |
| CT86 | CT | 始まる前にキャンセル済みなら何もしない | NFR-05 | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、1 件） |
| CT88 | CT | 座標: 画面が不明なら矩形全体、画面の左上より外のエリアは見える部分をそのオフセットに、Prepare に見える矩形と呼び出し側のトークン | §11-8 | Hider.Core.Tests/Capture/StripCoordinatorEdgeTests.cs（StripCoordinatorEdgeTests、3 件） |
| CT89 | CT | 停止して再開すればフレームがまた来る、2 回目の start は拒否されストリームは続く | §6 Linux | Hider.App.Tests/Platform/PipeWireIntegrationTests.cs（PipeWireIntegrationTests、2 件） |
| CT90 | CT | どのフレームよりも後の notBefore はタイムアウトで最後の番号、notBefore より後に表示されたフレームを待つ | 設計書 §5.3 | Hider.App.Tests/Platform/PipeWireIntegrationTests.cs（PipeWireIntegrationTests、2 件） |
| CT91 | CT | restore_token の読み書き: フォルダがない・パスがフォルダなら null、前後の空白を削る、トークンをそのまま書きフォルダを作る、短いトークンで前の残りがない、空・空白は null と同じく忘れる、ファイルがなければ null の保存は何もしない、2 つのストアが互いのトークンを見る | §6 Linux | Hider.Core.Tests/Persistence/RestoreTokenStoreCasesTests.cs（RestoreTokenStoreCasesTests、8 件） |
| CT91 | CT | restore_token を渡し、新しいトークンがなければ null | §6 Linux | Hider.App.Tests/Platform/ScreenCastPortalTests.cs（ScreenCastPortalTests、1 件） |
| CT92 | CT | 撮影時のフォールバックの順序: 取得手段なし、準備なしで使える手段だけに頼む、引数の受け渡し、拒否は最初のものを投げる、後の手段のフレームが勝つ、他の例外は握りつぶさない、完了が遅れても同じ結果 | D-6 | Hider.Core.Tests/Capture/FallbackGrabberEdgeTests.cs（FallbackGrabberEdgeTests、7 件） |
| CT93 | CT | 判定関数: 往復は同じ状態と矩形なら合格、読めない・JSON が違う・倍率で矩形が戻らなければ不合格、シェーダは全部コンパイルで合格・なければ不合格・欠けていれば例外 | 指示書 §2 | Hider.Core.Tests/SelfTestTests.cs（SelfTestTests、7 件） |
| CT94 | CT | ColorText: # と 6 桁の 16 進、有効なら大文字、無効なら代替、成分の順、無効な文字列は既定色 | — | Hider.Core.Tests/Model/AreaIdColorTextTests.cs（AreaIdColorTextTests、5 件） |
| CT95 | CT | 方向の符号 × 刻み | FR-33 | Hider.Core.Tests/Model/AreaGeometryCasesTests.cs（AreaGeometryCasesTests、1 件） |
| CT97 | CT | 書式付きの文字列: ツールチップの個数、全削除の確認の個数、ツールバーのサイズ表示、版の行、起動時のロック失敗、色の選択、カルチャによらない | FR-70, FR-07 | Hider.Core.Tests/StringsTests.cs（StringsTests、7 件） |
| CT98 | CT | 不正・重複の Id は置き換え（重複の最初は残す）、置き換え先が先行の Id と重なれば引き直す、後の正しい Id と区別、乱数なしなら共有の乱数 | — | Hider.Core.Tests/Persistence/AreasFileCasesTests.cs（AreasFileCasesTests、4 件） |
| GT01 | GT | 見えるトレイアイコンを 1 つ、メニューとツールチップ付きで登録 | FR-01 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、1 件） |
| GT02 | GT | 編集を出ると設定とパターンメニューを閉じ、枠などを取り除く | FR-20 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT03 | GT | 右ボタンのダブルクリックでは編集モードに入らない | FR-21 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT04 | GT | 隠しモードの左クリック（押下・離す）とホイール、編集中のホイールも処理済みにする | FR-21, FR-22 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、3 件） |
| GT04 | GT | ポインタの当たり判定は描いた範囲 | FR-21, FR-22 | Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、1 件） |
| GT05 | GT | 8 つのハンドルとその種類のタグ・リサイズカーソル、外枠と枠はエリアを囲みクリックを取らない、ハンドルは角と辺の中点、空のエリアでは内側の枠も空 | FR-34 | Hider.App.Tests/Areas/EditChromeToolbarTests.cs（EditChromeToolbarTests、5 件） |
| GT06 | GT | 隠しモードのドラッグは無視、BeginDrag なしの DragTo / EndDrag は何もしない、移動かハンドルかを名指し、ドラッグ開始点からの差分で動く、同じ矩形なら置かない、移動は全倍率で物理 px | FR-32 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、6 件） |
| GT06 | GT | 本体の押下で窓にフォーカスしポインタをキャプチャ、ハンドルでないタグの部品は移動、ドラッグなしの移動・離すは触らない | FR-32 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、4 件） |
| GT07 | GT | リサイズは窓の倍率での最小で止まる | FR-32 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、1 件） |
| GT08 | GT | 矢印キーで 1 px・Shift で 10 px、隠しモードでは無視、最後のキーの後に 1 回だけ保存して撮る、ドラッグが始まれば撮影はドラッグに任せる | FR-33, P-7 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、4 件） |
| GT08 | GT | 窓で矢印キーを押すと 1 px（Shift で 10 px）動く | FR-33, P-7 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT09 | GT | 削除は遷移の通知先の有無によらずエリアを消す | FR-33 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、1 件） |
| GT10 | GT | 隠しモードでの ExitEdit は何もしない、エリア自身の矩形に置いて保存し所有者へ知らせる、ドラッグ中なら先にドラッグを終える | FR-31 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、3 件） |
| GT10 | GT | モードの変化を所有者へ知らせ、編集を出るときに保存 | FR-31 | Hider.App.Tests/Areas/AreaOwnerContractTests.cs（AreaOwnerContractTests、1 件） |
| GT10 | GT | Esc で編集を出る（Delete で削除） | FR-31 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT12 | GT | 上の帯が画面から出るときだけツールバーを内側へ、その判定はエリアの画面の上端、画面がなければ上のまま | D-8 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、3 件） |
| GT12 | GT | ツールバーはエリアの上か内側の上部 | D-8 | Hider.App.Tests/Areas/EditChromeToolbarTests.cs（EditChromeToolbarTests、1 件） |
| GT13 | GT | 隠しモードの窓はエリアそのもの（倍率を問わない）、編集中は倍率に応じた余白とツールバー、細いエリアでも窓にツールバーが収まる、倍率の探索は 8 回で・広がらない倍率で止まる | D-8 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、5 件） |
| GT14 | GT | ユーザーによる Close は拒み、コントローラーの Close は通す | FR-11 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT14 | GT | SetTopmost で窓を最前面に | FR-11 | Hider.App.Tests/Platform/AvaloniaWindowHostTests.cs（AvaloniaWindowHostTests、1 件） |
| GT14 | GT | SetTopmost | FR-11 | Hider.App.Tests/Platform/X11WindowHostTests.cs（X11WindowHostTests、1 件） |
| GT15 | GT | カーソルが既知の画面にない・不明なら主画面、画面がなければ 1080p の作業領域で倍率 1、画面の倍率で 400×240 DIP、モザイク / pixelate・編集モード・OS の更新間隔・各既定値、エリアごとに別の Id | FR-14 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、6 件） |
| GT16 | GT | 項目は今のエリアから作るモデル | FR-03, FR-04 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、1 件） |
| GT16 | GT | エリアがあり編集中でなければ版の行以外すべて有効、エリアがなければ「すべて編集」「すべて削除…」が無効、毎回新しい一覧、項目は値のレコード、全コマンドがある | FR-03, FR-04 | Hider.App.Tests/Tray/TrayMenuModelTests.cs（TrayMenuModelTests、5 件） |
| GT17 | GT | モードの変化で Changed を出す（通知先がなくても可） | FR-03 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、2 件） |
| GT17 | GT | 編集中は 2 行目が「編集を終了」、エリアがなくても編集中なら有効（全削除は無効） | FR-03 | Hider.App.Tests/Tray/TrayMenuModelTests.cs（TrayMenuModelTests、2 件） |
| GT18 | GT | 日本語の文言、個数を問いに入れる、「削除」で真・「キャンセル」で偽を返して閉じる | FR-05 | Hider.App.Tests/Dialogs/ConfirmDeleteWindowTests.cs（ConfirmDeleteWindowTests、4 件） |
| GT18 | GT | 確認ダイアログの間は撮影を止める、もう一度求めれば開いているダイアログを置き換える、キャンセルでエリアは残る、閉じた後の更新でダイアログは閉じたまま | FR-05 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、4 件） |
| GT19 | GT | ポップアップ: カーソルがなければ窓の位置で、もう一度で開いているものを閉じる、画面上のカーソルの左上に、どの画面の外でも主画面に収める、コマンドはコントローラーを通す、作業領域に収める | FR-03 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、6 件） |
| GT19 | GT | 項目ごとに 1 つのボタンを順にコマンドをタグに、もう一度で置き換え、クリックで CommandSelected（購読者がなければ何もしない）、Buttons は写し | FR-03 | Hider.App.Tests/Tray/TrayMenuPanelTests.cs（TrayMenuPanelTests、5 件） |
| GT20 | GT | エリアの変化にメニュー・ツールチップ・アイコンが従う | FR-07 | Hider.App.Tests/Tray/TrayControllerTests.cs（TrayControllerTests、1 件） |
| GT22 | GT | 透明度は隠しモードでは窓のアルファ、編集中はパターンのアルファ | §4.5 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT22 | GT | 塗りつぶしは単色で変更に追従 | §4.5 | Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、1 件） |
| GT24 | GT | 取得の開始が投げる・取得が落ちる・自分でキャンセルした・失敗を報告した・成功なのにフレームがない・写せないフレームは、それぞれの名前で生成パターンへ。ユーザーが別のパターンを選んだ後の失敗は選択を保つ | FR-52 | Hider.App.Tests/Areas/AreaControllerCaptureTests.cs（AreaControllerCaptureTests、8 件） |
| GT24 | GT | 失敗で切り替えた生成パターンを保存する | FR-52 | Hider.App.Tests/Areas/AreaOwnerContractTests.cs（AreaOwnerContractTests、1 件） |
| GT24 | GT | 取得失敗中は警告を出して広がり、戻れば狭まる | FR-52 | Hider.App.Tests/Areas/EditChromeToolbarTests.cs（EditChromeToolbarTests、1 件） |
| GT25 | GT | 生成パターンは 1 回描いて写し、サイズが変われば描き直す | FR-42 | Hider.App.Tests/Areas/MosaicPresenterUnitTests.cs（MosaicPresenterUnitTests、1 件） |
| GT26 | GT | 行はラベル・スライダー・入力欄・単位の 4 列 | FR-37 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、1 件） |
| GT26 | GT | パネルの体裁がデザインどおり | FR-37 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、1 件） |
| GT27 | GT | スライダーの移動は丸めた値を出す（入力ではない）、丸めて同じなら出さない、他のプロパティの変化は無視、購読者がいなくても投げない | FR-37 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、4 件） |
| GT28 | GT | トラックの外の入力値はキー操作でも保つ | FR-37 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、1 件） |
| GT29 | GT | SetValue はクランプして表示し、ValueChanged を出さない | FR-37 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、1 件） |
| GT31 | GT | 設定の変更で新しい値で描き直す、保存は即時か遅延（デバウンス）を選ぶ | FR-36 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、2 件） |
| GT31 | GT | スライダーの移動は遅れて保存、入力は即保存 | FR-36 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、1 件） |
| GT33 | GT | 今の色のプリセットだけ強調（大小文字を問わない）、一致しなければなし、プリセットは色名付きの見本ボタン、クリックで強調を付け直す | §4.5 | Hider.App.Tests/Settings/SettingsPanelDetailTests.cs（SettingsPanelDetailTests、4 件） |
| GT34 | GT | バッジと幅を指定するとラベルの後にバッジ | §4.5 | Hider.App.Tests/Settings/ParamRowTests.cs（ParamRowTests、1 件） |
| GT35 | GT | ↻（Refresh）はモザイクのときだけ撮る | FR-34, FR-50 | Hider.App.Tests/Areas/AreaControllerTests.cs（AreaControllerTests、1 件） |
| GT35 | GT | ↻ ボタンで撮り直す | FR-34, FR-50 | Hider.App.Tests/Areas/AreaWindowInputTests.cs（AreaWindowInputTests、1 件） |
| GT35 | GT | パターン名を表示し、↻ はモザイクのときだけ | FR-34, FR-50 | Hider.App.Tests/Areas/EditChromeToolbarTests.cs（EditChromeToolbarTests、1 件） |
| GT36 | GT | 位置は物理 px・サイズは目標倍率の DIP、既定のホストは長さをそのまま、長い窓の長さはネイティブの刻みと DIP サイズに | 設計書 §6.4 | Hider.App.Tests/Platform/AvaloniaWindowHostTests.cs（AvaloniaWindowHostTests、3 件） |
| GT36 | GT | px → DIP はホストの倍率で割る | 設計書 §6.4 | Hider.App.Tests/Platform/ScreenServiceExtensionsTests.cs（ScreenServiceExtensionsTests、1 件） |
| GT36 | GT | 窓の長さは安定する最短の長さ、DIP の長さは WM_SIZE が保存する値そのもの、サイズは Win32 の往復で保たれる | 設計書 §6.4 | Hider.App.Tests/Platform/Windows/Win32WindowHostTests.cs（Win32WindowHostTests、3 件） |
| GT36 | GT | 位置は物理 px・サイズは DIP | 設計書 §6.4 | Hider.App.Tests/Platform/X11WindowHostTests.cs（X11WindowHostTests、1 件） |
| GT37 | GT | 小さすぎれば最小へ、どの画面にもなければ主画面の中央へ | FR-63 | Hider.App.Tests/Areas/AreaManagerTests.cs（AreaManagerTests、2 件） |

## ST（scripts の単体テスト、新設）

| ID | 種別 | 内容 | 対応 FR・NFR・決定 | ファイル / クラス |
|---|---|---|---|---|
| ST01 | ST | build.py の対象 RID の解釈: 各対象・all は全対象を順に（他の指定より優先）、指定の順を保ち重複を除く、指定なしはホスト（未対応のホストなら終了、指定があればホストを問わない）、未知の対象は全部名指しで終了、大小文字を区別 | FR-72 | scripts/test_build.py（ParseArgsTest、11 件）<br>scripts/test_build.py（ParseTargetsTest、3 件） |
| ST02 | ST | build.py --version: 版を受け取り、不正な版（非 ASCII を含む）は終了、対象より先に検査、値が必要 | FR-72, P-9 | scripts/test_build.py（ParseArgsTest、4 件） |
| ST03 | ST | build.py のコマンドライン: 未知のオプション、ヘルプ、警告は接頭辞付きで標準エラーへ、スクリプトとして直接実行できる | FR-72 | scripts/test_build.py（ParseArgsTest、2 件）<br>scripts/test_build.py（WarnTest、1 件）<br>scripts/test_build.py（ScriptTest、2 件） |
| ST04 | ST | build.py の libhidercap のビルド: 対象の make を走らせ古いヘルパを先に消す、make がない・失敗すればホストの対象なら終了・他のアーキテクチャや未対応のホストなら警告、負の終了コードも失敗 | FR-72, D-6 | scripts/test_build.py（BuildNativeHelperTest、9 件） |
| ST05 | ST | build.py の publish: 全対象、版はグローバルプロパティで渡す、指定なしはホスト、Windows はヘルパを作らない、古い出力を置き換える、dotnet がなければビルド前に終了、引数は dotnet より先に検査、publish の失敗で止まる | FR-72 | scripts/test_build.py（MainTest、8 件） |
| ST06 | ST | XWAYLAND-ASSUMPTION の数え方: ファイルごと・1 行に複数も数える、なければ載せない、src/ の .cs だけ（bin / obj を除く）、UTF-8、大小文字を区別、src/ がなくてもよい | D-1 | scripts/test_check_wayland_list.py（MarkersTest、9 件） |
| ST07 | ST | docs/wayland-migration.md の表の数え方: ファイルごとの行数、その節だけ（節が再び出てもよい、見出しの末尾の空白は可、見出しは一致が必要）、見出し行・区切り・他の行は無視、1 列目は空白を除く、閉じる \| がない行、節がない、文書がなければ例外 | D-1 | scripts/test_check_wayland_list.py（TableRowsTest、10 件） |
| ST08 | ST | 照合の結果: すべて載っていれば合格・どこにもなければ合格、不一致は並べて報告、表の行が多すぎても不合格、パスの定数、このリポジトリ自体が一致（直接実行・失敗時の終了コードも） | D-1 | scripts/test_check_wayland_list.py（MainTest、4 件）<br>scripts/test_check_wayland_list.py（ConstantsTest、1 件）<br>scripts/test_check_wayland_list.py（RepositoryTest、3 件） |
| ST09 | ST | scripts の共通のパス: リポジトリのルート、アプリのプロジェクト、ヘルパのフォルダに Makefile、対象は 3 つの RID を順に、publish の出力は bin/publish の下（任意の名前） | FR-72 | scripts/test_common.py（PathsTest、6 件） |
| ST10 | ST | ホストの RID の判定: OS とマシンの対応、結果は対象か None、CI のランナーでは期待どおり | FR-72 | scripts/test_common.py（HostTargetTest、3 件） |
| ST11 | ST | scripts の形: 入口は実行可能、common はモジュールだけ、標準ライブラリだけ、Python 3.10 で解釈できる | FR-72 | scripts/test_common.py（ScriptFilesTest、4 件） |
| ST12 | ST | install.py の OS の判定: Windows、Linux、それ以外は Linux 扱い、CI のランナーでは期待どおり | FR-72 | scripts/test_install.py（ModuleTest、4 件） |
| ST13 | ST | install.py のコピー: ツリーを写し既存のフォルダへ上書き、元がない・OS エラー・shutil のエラーは終了、他の例外は捕まえない | FR-72 | scripts/test_install.py（CopyFilesTest、6 件） |
| ST14 | ST | install.py の Windows のショートカット: エンコードしたスクリプトで PowerShell を実行、パスは環境変数で渡す、ファイルを写す、ショートカットの失敗で終了、コピーの失敗では PowerShell を走らせない | FR-72 | scripts/test_install.py（InstallWindowsTest、5 件） |
| ST15 | ST | .desktop の文字列のエスケープ（バックスラッシュ、Exec の引数） | FR-72 | scripts/test_install.py（DesktopStringTest、2 件）<br>scripts/test_install.py（DesktopExecArgTest、1 件） |
| ST16 | ST | install.py の Linux のインストール: 写して .desktop を書く（LF・UTF-8、データの場所とインストール先は別でもよい、既存のインストールとエントリを置き換える、パスをエスケープ）、実行ファイルがなければ失敗、通しの実行 | FR-72, FR-71 | scripts/test_install.py（InstallLinuxTest、6 件）<br>scripts/test_install.py（EndToEndTest、1 件） |
| ST17 | ST | install.py のビルドの検査: 未対応のマシン・ビルドがない・他の対象のビルド・実行ファイルがファイルでない・Windows で exe がないなら終了 | FR-72 | scripts/test_install.py（MainTest、5 件） |
| ST18 | ST | install.py のインストール先: Linux は XDG_DATA_HOME（なし・空なら ~/.local/share）、--dest（相対は絶対に）、Windows は LOCALAPPDATA（なければ終了）か --dest、空の --dest は既定 | FR-72 | scripts/test_install.py（MainTest、9 件） |
| ST19 | ST | install.py のコマンドライン: 未知の引数、ヘルプ、スクリプトとして直接実行 | FR-72 | scripts/test_install.py（MainTest、2 件）<br>scripts/test_install.py（ScriptTest、1 件） |
| ST20 | ST | install.bat の検査で止まる: 改ざんされたアーカイブ、実行ファイルのないアーカイブ、Hider の実行中、フォルダの指定も LOCALAPPDATA もない、コピーの失敗。ダブルクリック時の失敗は一時停止して終了コードを保つ、フォルダ指定時は止めない | P-9 | scripts/test_install_bat.py（InstallBatTest、7 件） |
| ST21 | ST | install.bat のインストール先: 指定のフォルダ、既定は LOCALAPPDATA（空の引数も既定）、相対は絶対に、既存のインストールを上書き | P-9 | scripts/test_install_bat.py（InstallBatTest、5 件） |
| ST22 | ST | install.bat の取得と起動: 隣にアーカイブがなければダウンロード（見つからなければ失敗）、指定がなければ Hider を起動・HIDER_NO_LAUNCH なら起動しない | P-9 | scripts/test_install_bat.py（InstallBatTest、4 件） |
| ST23 | ST | install.sh の引数: ヘルプ、未知の引数、-d はフォルダが必要、-d（相対は絶対に、空は既定）、--dest=x は未知の引数、-d の後のヘルプ、最後の -d が勝つ | P-9 | scripts/test_install_sh.py（OptionsTest、9 件） |
| ST24 | ST | install.sh の既定の場所: XDG_DATA_HOME、なければ HOME の下、HOME がなくても XDG_DATA_HOME、どちらもなければ理由を示して終了 | P-9 | scripts/test_install_sh.py（DefaultFolderTest、4 件） |
| ST25 | ST | install.sh の環境の検査: アーキテクチャの対応（未対応は終了）、Linux 以外は終了、Hider の実行中は止める（pgrep がなければ調べない）、sha256sum を優先し shasum でもよい・どちらもなければ終了 | P-9 | scripts/test_install_sh.py（MachineTest、9 件） |
| ST26 | ST | install.sh の取得: curl（失敗で終了）、wget（失敗で終了）、curl を優先、道具がなければ終了、サーバーにアーカイブがなければ終了、スクリプトの隣のアーカイブを使う（他アーキのものは使わない、相対パスでの起動も）、sh へのパイプでは今のフォルダ・ダウンロード、途中で切れたスクリプトは何も実行しない | P-9 | scripts/test_install_sh.py（DownloadTest、13 件） |
| ST27 | ST | install.sh のチェックサムと中身: ダウンロード・手元のアーカイブの不一致で終了、アーキテクチャごとのチェックサム、実行ファイルのない・別の版の・壊れたアーカイブ（チェックサムが一致しても）は終了 | P-9 | scripts/test_install_sh.py（ChecksumTest、6 件） |
| ST28 | ST | install.sh の既存のインストール: 置き換える、2 回入れても同じ、実行属性を付ける、コピーの失敗・インストール先がファイルなら終了 | P-9 | scripts/test_install_sh.py（ExistingInstallTest、5 件） |
| ST29 | ST | install.sh の .desktop: パスのエスケープは install.py と同じ、エントリの行 | P-9, FR-72 | scripts/test_install_sh.py（DesktopEntryTest、2 件） |
| ST30 | ST | install.sh の起動と中断: 表示があれば起動、空の HIDER_NO_LAUNCH でも起動、HIDER_NO_LAUNCH なら起動しない、表示がなければ起動しない、ダウンロード中に終了させても後始末する | P-9 | scripts/test_install_sh.py（LaunchTest、4 件）<br>scripts/test_install_sh.py（InterruptTest、1 件） |
| ST31 | ST | install.sh のシェルの違い: 求めたシェル（dash / bash / busybox 等）で動く、何かが残ればハングではなく失敗、変わったフォルダ名と起動 | P-9 | scripts/test_install_sh.py（ShellsTest、3 件） |
| ST32 | ST | スクリプトの構文: install.sh と xvfb-strip-smoke.sh はシェルとして、windows-stress.ps1 は PowerShell として | P-8, P-9 | scripts/test_install_sh.py（SyntaxTest、3 件） |
| ST33 | ST | Windows の実機での install.py: ショートカットを作る・既存を置き換える、LOCALAPPDATA へのインストール | FR-72 | scripts/test_install_windows.py（InstallWindowsRealTest、3 件） |
| ST34 | ST | アイコンの定数とモザイク柄: 資産のフォルダ、アプリの灰青のパレット、サイズ、配置はパレットの番号の格子、セルの色は配置に従う（角、格子の外は例外） | FR-71 | scripts/test_make_icons.py（ConstantsTest、4 件）<br>scripts/test_make_icons.py（CellColorTest、3 件） |
| ST35 | ST | 角丸の判定: 半径はサイズの 1/8、中央と辺は内側、角は切る、円の境界は内側、対称 | FR-71 | scripts/test_make_icons.py（CornerRadiusTest、1 件）<br>scripts/test_make_icons.py（InsideRoundedTest、4 件） |
| ST36 | ST | アイコンの描画: 形、セルは整数 px に揃う、均等なセル、内側は不透明、角はアンチエイリアス、アルファは対称でサンプル数を数える | FR-71 | scripts/test_make_icons.py（RenderTest、7 件） |
| ST37 | ST | PNG と ICO の書き出し: PNG は描いた画素に戻る・決定的・最大圧縮、ICO の配置・256 以上は 0 と書く・画像なし・項目は PNG | FR-71 | scripts/test_make_icons.py（PngTest、3 件）<br>scripts/test_make_icons.py（IcoTest、4 件） |
| ST38 | ST | SVG の書き出し: 構造、セル、文字列。色は大文字の 2 桁ずつ | FR-71 | scripts/test_make_icons.py（HexColorTest、1 件）<br>scripts/test_make_icons.py（SvgTest、3 件） |
| ST39 | ST | make_icons.py の実行: 全ファイルを書きコミット済みの資産と一致、既存のフォルダへ上書き、直接実行、資産のパス | FR-71 | scripts/test_make_icons.py（MainTest、4 件） |
| ST40 | ST | リリースの命名と定数: パス、繰り上げの種類、プレースホルダの形、タグは v を付ける、Windows は zip・Linux は tar.gz | P-9 | scripts/test_make_release.py（ConstantsTest、3 件）<br>scripts/test_make_release.py（TagNameTest、1 件）<br>scripts/test_make_release.py（ArchiveNameTest、2 件） |
| ST41 | ST | 版の解釈: 正しい版、不正な版・ASCII 以外の数字は終了。Directory.Build.props の版を読む（なし・2 つ・不正・空は終了、Version で終わる他の要素は数えない、ファイルがなければ例外）、リポジトリの版 | P-9 | scripts/test_make_release.py（ParseVersionTest、3 件）<br>scripts/test_make_release.py（PropsVersionTest、7 件）<br>scripts/test_make_release.py（RepositoryPropsTest、1 件） |
| ST42 | ST | 版の繰り上げ: 各部分、下位を 0 に、9 を超えて数える、先頭の 0 を落とす、0 から、他の指定は patch、不正な版は終了 | P-9 | scripts/test_make_release.py（BumpTest、7 件） |
| ST43 | ST | 次の版の決定（タグ）: タグがなければ props の版（既定は Directory.Build.props、タグがあれば読まない）、各部分の繰り上げ、軽量・注釈付きタグ（peeled の行だけでも）、数として比較（先頭の 0 も）、他のタグは無視 | P-9 | scripts/test_make_release.py（ChooseVersionTest、9 件） |
| ST44 | ST | HEAD がリリース済みかの判定と ls-remote の出力: リリース済みなら止めてその最初のタグを名指し、ブランチの ref はリリースでない、オブジェクト名は完全一致、SHA-256 のリポジトリ、CRLF、想定外の行・不正な HEAD は終了、本物の git ls-remote の出力 | P-9 | scripts/test_make_release.py（ChooseVersionTest、9 件） |
| ST45 | ST | publish 出力の探し方: 指定のフォルダ（bin/publish より優先）、既定は bin/publish、フォルダがなければ終了、Windows は exe・Linux は拡張子なしの実行ファイルが必要（ファイルであること） | P-9 | scripts/test_make_release.py（FindInputTest、7 件） |
| ST46 | ST | アーカイブとチェックサム: tar の所有者を消しモードを揃える、zip（再現可能）・tar.gz、他の名前は tar.gz、元のフォルダは変えない、SHA-256（既知の値、1 チャンクより大きいファイル）、SHA256SUMS は sha256sum -c の形 | P-9 | scripts/test_make_release.py（TarFilterTest、3 件）<br>scripts/test_make_release.py（MakeArchiveTest、5 件）<br>scripts/test_make_release.py（Sha256Test、2 件）<br>scripts/test_make_release.py（ShaSumsFormatTest、1 件） |
| ST47 | ST | テンプレートの置換: 全出現を置換、使わない値は可、残ったプレースホルダ・プレースホルダに見える値は終了、プレースホルダでないものは残る、行末（値にも）、UTF-8、テンプレートがなければ例外 | P-9 | scripts/test_make_release.py（RenderTest、9 件） |
| ST48 | ST | リリースのテンプレート: 全テンプレートが描ける、install.sh・install.bat の値と install.bat の目印、リリースノートのリンク | P-9 | scripts/test_make_release.py（TemplatesTest、5 件） |
| ST49 | ST | 配布物一式（package）: 資産の一覧、チェックサム、インストーラがアーカイブのチェックサムを持つ、行末とモード、アーカイブは版付きのフォルダに入力を収める、リリースノート、既定の入力は bin/publish、入力がなければインストーラの前に止まる、同じフォルダへ再実行できる | P-9 | scripts/test_make_release.py（PackageTest、9 件） |
| ST50 | ST | make_release.py のコマンド（version / bump / next-version）: version は引数を取らない、bump は 2 つの引数・他の部分や不正な版は拒否、next-version は標準入力を読む（タグなし・リリース済み・引数の検査）、コマンドは必須、ヘルプの体裁、直接実行 | P-9 | scripts/test_make_release.py（MainTest、13 件） |
| ST51 | ST | make_release.py package のコマンド: 入力フォルダは省略可、--repo と --out-dir は必須、不正な版・リポジトリ名は拒否、通しの実行 | P-9 | scripts/test_make_release.py（MainTest、6 件） |

## NT（native/hidercap の単体テスト、新設）

| ID | 種別 | 内容 | 対応 FR・NFR・決定 | ファイル / 関数 |
|---|---|---|---|---|
| NT01 | NT | 未開始の API はどれも安全にエラーを返し、不正な fd はストリームエラーで停止のまま | P-2, NFR-05 | native/hidercap/test_hidercap.c（`nt01_not_started`、`nt01_invalid_fd`） |
| NT02 | NT | 相手が切ればストリームエラー、無応答ならタイムアウト（二重の start は拒否） | P-2, NFR-05 | native/hidercap/test_hidercap.c（`nt02_remote_hangs_up`、`nt02_silent_remote_times_out`） |
| NT03 | NT | 停止の後に再び start できる | P-2 | native/hidercap/test_hidercap.c（`nt03_restart`） |
| NT04 | NT | 使えるバッファの判定（データ・大きさ・形式・破損の印） | §6 Linux | native/hidercap/test_hidercap_internal.c（`nt04_usable`） |
| NT05 | NT | process コールバック: 使えるフレームがなければ何もせず、最新の 1 枚を時刻（ヘッダの PTS か受信時刻）付きで保持する | 設計書 §5.3, CT-90 | native/hidercap/test_hidercap_internal.c（`nt05_process_nothing_usable`、`nt05_process_keeps_the_newest`、`nt05_process_frame_time`） |
| NT06 | NT | バッファの削除・ストリームの状態変化・保持中のフレームの解放 | NFR-05 | native/hidercap/test_hidercap_internal.c（`nt06_remove_buffer`、`nt06_state_changed`、`nt06_release_held`） |
| NT07 | NT | 形式の変更（param_changed）: 対象外は無視し、使える形式を取り込む | §6 Linux | native/hidercap/test_hidercap_internal.c（`nt07_param_changed_ignored`、`nt07_param_changed_takes_the_format`） |
| NT08 | NT | 保持中のフレームから行を写す（BGRx / RGBx の入れ替え、行の範囲）、保持中のフレームでの grab_after | §6 Linux | native/hidercap/test_hidercap_internal.c（`nt08_copy_held`、`nt08_grab_after_with_a_held_frame`） |
| NT09 | NT | start の各段階（文脈・ループ・ストリーム・接続）の失敗で、作ったものを戻し fd を正しく扱う | NFR-05 | native/hidercap/test_hidercap_internal.c（`nt09_start_failures`） |
## FR・NFR との照合（タブの項目 + 上の追加分）

仕様書の FR-01〜FR-72 と NFR-01〜NFR-06 のうち、CT / GT が 1 件もないもの:

| FR・NFR | 内容 | 現状 |
|---|---|---|
| FR-41 | Voronoi（Worley 方式、3×3 近傍） | 本 PR の対象外。Voronoi の生成パターンは v2.0 では実装せず、別途対応（オーナー指示 2026-10-17。設計書 D-5）。組み込み手法は pixelate / hexcell / blur / block / noise |
| NFR-01 | 録画・画面共有に映る（除外 API を使わない） | 映ることそのものは PT-10 / PT-26（実機）。除外 API を使わないことは CT150 でソースを検査 |

以前この表にあった FR-10（GT131）、FR-12（GT132）、FR-65（CT149、CT150）はテストを足した。

FR-71（アイコン）と FR-72（build.py / install.py）は GT42 / GT52 のほか ST で検証している。FR-02 と FR-06 はタブでは PT だけだったが、GT46 / GT47 で CT / GT が付いた。
