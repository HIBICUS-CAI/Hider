# 設計書: Hider v2 実装方針

作業指示書の「設計書」タブを Step 0 でこのパスに保存したもの。§9 の D-1〜D-12 は 2026-10-11 に承認済みで、作業指示書はこの節番号を「設計書 §n」として参照する。入力: 仕様書、UI デザインキャンバス、SelfTools/claude/new-hider の現行コード（付録タブ）。

**保守方針**（2026-10-12〜）: 承認時の内容は作業指示書のタブに残り、こちらはリポジトリの現状に合わせて保守する。

- 実装で変わった事実（関数の数、配置、ランナー名など）は該当箇所を現状に書き換え、末尾の「改訂履歴」に理由のコミットまたは提案 ID を記録する。
- 決定（D-n）の内容は書き換えない。未承認の提案で読み替えているものは、元の決定の後に「提案 P-n・未承認」と明記して併記する。承認されたら決定の本文に取り込み、その旨を改訂履歴に書く。
- 提案の一覧と承認状況は `git log --grep "pending: P-"` / `git log --grep "resolved: P-"`（CLAUDE.md）。

## 0. 結論

1. .NET 10 + Avalonia 12.1 で実装できる。ただし Linux は Avalonia 12.1 で追加された Wayland バックエンドを使わず、現行どおり X11（XWayland）に固定する。GNOME の Wayland ではクライアントが窓の位置を決められず、常に最前面にもできないため。
2. Avalonia が担うのはトレイ・窓・入力・描画だけ。露出を左右する核心（下の内容の取得、帯の制御）は OS API を直接叩く部分で、GUI フレームワークに依存しない。Qt に替えても同じコードを書くことになる。
3. モザイク計算は「取得画像に SkSL（Skia のランタイムシェーダ）を描画時に適用する」方式。Avalonia が既に GPU で描いている（Windows: ANGLE/D3D11、Linux: GLX）ので、専用の GPU 基盤なしで GPU 実行になり、ソフトウェア描画時も同じシェーダが Skia の CPU パスで動く。手法の追加 = SkSL 1 ファイル + パラメータ定義 1 クラス。
4. 着手前に決めること 12 件は §9。すべて承認済み。

## 1. 絶対要件と仕様書の差分

| 項目 | 仕様書 | 絶対要件 | 影響 |
|---|---|---|---|
| Windows | 10 / 11 | 11 のみ | Windows.Graphics.Capture の枠なし取得（Win11 22000+）を昇格先に選べる。Win10 互換の配慮は不要 |
| Linux | Ubuntu GNOME / Wayland | Ubuntu 26.04+（GNOME 50） | GNOME 50 は X11 セッションを削除済み。XWayland は標準搭載。Wayland 専用セッションが前提（§2.2） |
| アーキテクチャ | x64 | x64 + arm64 | ネイティブヘルパ（PipeWire）と SkiaSharp のネイティブライブラリに arm64 版が要る。SkiaSharp 3 は linux-arm64 同梱。GPU は Mesa 経由の GLX |
| 拡張性 | 明記なし | モザイク手法を増やす | 手法を登録制モジュールとして設計する（§4） |
| 効率 | NFR-04: 1 コアの 1 割 | GPU も可 | 描画時シェーダ方式で、CPU の仕事を取得だけにする（§4.4） |

## 2. GUI フレームワーク判定

### 2.1 Avalonia 12.1 で必要な機能の可否

| 必要な機能 | Avalonia での手段 | Win32 | X11 (XWayland) | 備考 |
|---|---|---|---|---|
| トレイアイコン + メニュー | TrayIcon + NativeMenu | ○ 左 = Clicked、右 = メニュー | ○ DBus StatusNotifierItem（AppIndicator 拡張が必要） | Windows の左クリックは自前ポップアップで同じメニューを出す。GNOME では拡張が左右ともメニューを開く |
| 枠なし・最前面・タスクバー非表示・非アクティブ表示 | SystemDecorations=None, Topmost, ShowInTaskbar=false, ShowActivated=false | ○ | ○ _NET_WM_STATE_ABOVE / SKIP_TASKBAR | Mutter は X11 クライアントの ABOVE を尊重する。他の最前面窓との前後は保証しない（仕様どおり） |
| ピクセル単位アルファ（帯の透明化、編集時の余白） | TransparencyLevelHint=Transparent | ○ DWM | ○ ARGB visual + コンポジット（Mutter は常時コンポジット） | GLX + ARGB visual の組合せは要スパイク（§10 S-2） |
| ウィンドウ単位 DPI | RenderScaling / PixelPoint | ○ | △ XWayland の分数スケーリングは要確認 | 座標系ルールを §6.4 に固定 |
| 自前ドラッグ移動・リサイズ | PointerPressed/Moved + Position/Width/Height | ○ | ○ | BeginMoveDrag は使わない（OS 差、ライブプレビューとの相性） |
| 設定ポップアップ | Popup / Flyout | ○ | ○ | ColorPicker は Avalonia.Controls.ColorPicker 12.x（トリミング要確認） |
| GPU 描画 | Skia（既定: Win32 = ANGLE/D3D11、X11 = GLX → Software フォールバック） | ○ | ○ | カスタム描画は ISkiaSharpApiLeaseFeature（unstable 扱いの API） |
| トリミング単一ファイル | 既存 csproj | ○ | ○ | JSON はソースジェネレータ。DBus は Tmds.DBus.Protocol（Avalonia 自身の依存、リフレクション不使用） |

### 2.2 Linux を X11（XWayland）固定にする理由

Avalonia 12.1 からネイティブ Wayland バックエンド（Avalonia.Wayland、UseWayland()）が使えるが、本件では使わない。

- xdg-shell にはクライアントが窓の絶対位置を指定する手段がない。エリアを「画面上の矩形」として置けない。
- 「常に最前面」も xdg-shell にはない。wlr-layer-shell は GNOME が対応しない。
- XWayland 経由なら Mutter が X11 の位置指定と ABOVE を扱う。現行コード（UsePlatformDetect + X11PlatformOptions）のままでよい。
- 画面取得はどちらでもポータル経由になる（XWayland のルートウィンドウには Wayland ネイティブ窓が写らない）。

### 2.3 Avalonia で足りないもの（どのフレームワークでも自前）

| 機能 | 実装手段 | 依存 |
|---|---|---|
| Windows: 下の窓の列挙・取得・実矩形 | P/Invoke: EnumWindows / PrintWindow(PW_RENDERFULLCONTENT) / DwmGetWindowAttribute | user32, dwmapi, gdi32 |
| Linux: スクリーンショット・スクリーンキャスト | DBus（org.freedesktop.portal.*） | Tmds.DBus.Protocol |
| Linux: PipeWire フレーム取得 | C ヘルパ libhidercap.so（start / grab_after） | gcc, libpipewire-0.3-dev |
| 単一インスタンス | ~/.hider/instance.lock を FileShare.None で開いたまま保持（.NET は Unix で flock に変換） | なし |

### 2.4 代替案（Avalonia を捨てる場合）

Qt 6（C++, Widgets）が唯一の現実的な代替。QSystemTrayIcon（SNI）、frameless + WA_TranslucentBackground、WindowStaysOnTopHint、QRhi。ただし同じ理由で QT_QPA_PLATFORM=xcb 固定が要り、取得処理・PipeWire ヘルパ・露出制御はそのまま書く。切替の判断条件は「§10 のスパイク S-1〜S-3 が Avalonia 固有の理由で失敗したとき」に限る。Mutter / XWayland 起因の失敗は Qt でも再現するので切替理由にならない。

## 3. 全体構成

### 3.1 層と依存方向

```
┌──────────────────────────── Hider (Avalonia app) ─────────────────────────────┐
│ Tray / AreaWindow / Toolbar / SettingsPopup / Dialogs        (UI, 日本語文字列) │
│ MosaicPresenter: ISkiaSharpApiLeaseFeature で描画 (GPU) / Offscreen (CPU)       │
│ Platform/Windows: Win32UnderlayCapture (PrintWindow)                            │
│ Platform/Linux:   StripCoordinator + PortalScreenshotGrabber / PipeWireGrabber  │
└──────────────┬───────────────────────────────────────────────────────────────┘
               │ 依存（Core は Avalonia を知らない）
┌──────────────▼────────────── Hider.Core (net10.0) ────────────────────────────┐
│ Model: Area, Pattern(Fill / Mosaic / Generated), ParamValues, AppState          │
│ Mosaic: IMosaicMethod, ParamSpec, MethodRegistry, SkSL ソース, パイプライン定義 │
│ Capture: IUnderlayCapture, CaptureRequest / Result, 失敗理由                    │
│ Persistence: AreasFile (System.Text.Json source-gen, tmp → rename)              │
│ Scheduling: RefreshScheduler (更新間隔・契機)                                   │
└─────────────────────────────────────────────────────────────────────────────┘
      Hider.Core は SkiaSharp にのみ依存（シェーダ組立てと CPU テストのため）
┌────── native/hidercap (C) ────────┐   ┌────── tests ────────┐
│ PipeWire ストリーム購読・PTS 判定   │   │ Hider.Core.Tests     │
│ linux-x64 / linux-arm64 .so        │   │ ゴールデン画像比較    │
└───────────────────────────────────┘   └─────────────────────┘
```

### 3.2 リポジトリ構成（案）

```
Hider/
  Hider.slnx
  src/Hider/              Avalonia アプリ（既存 csproj を移動・拡張）
  src/Hider.Core/         モデル・モザイク・取得抽象・永続化（Avalonia 非依存）
  native/hidercap/        C ヘルパ（Makefile、linux-x64 / linux-arm64）
  tests/Hider.Core.Tests/
  scripts/                build.py / install.py（維持。native ビルドとアイコン生成を追加）
  docs/                   本書
```

## 4. モザイク計算（拡張性と効率の中核）

### 4.1 方式の比較

| 方式 | 実装 | GPU | CPU フォールバック | 手法追加コスト | 判定 |
|---|---|---|---|---|---|
| A. C# SIMD カーネル（Vector128/256 + Parallel.For） | 手法ごとに C# | なし | それ自体 | C# 1 クラス | 単純。blur 半径が大きいと NFR-04 を超える。GPU 要件を満たさない |
| B. SkSL ランタイムシェーダ（描画時に適用） | 手法ごとに SkSL + 記述子 | Avalonia の Skia が GPU なら自動で GPU | Skia のラスタパイプラインで同じ SkSL が動く | SkSL 1 ファイル + C# 1 クラス | 採用。パラメータ変更は再計算不要（uniform 更新のみ） |
| C. Vulkan compute（Silk.NET）+ Slang → SPIR-V | 別途デバイス管理・画像転送・Avalonia への取り込み（ICompositionGpuInterop） | ○ | 別実装が必要 | Slang 1 ファイル + 転送コード | 基盤が重い。v2.0 では不要。B で足りなくなった時の拡張先（§4.6） |

B を採る理由: 取得画像はどのみち Avalonia が GPU テクスチャに上げる。そこにシェーダを掛ければ追加の転送も計算基盤も要らない。CPU の仕事は取得だけになる。Linux では撮り直しが稀なので、スライダー操作が再撮影を伴わない点も効く。

### 4.2 パイプライン（固定部分と手法部分）

```
取得画像 (BGRA premul, 物理 px)       ← IUnderlayCapture
  │
  ├─ 前段: 追加ぼかし      SKImageFilter.CreateBlur(r_eff)          固定（共通パラメータ）
  ├─ 前段: ピラミッド      1/2 ずつ箱型縮小した SKImage 列           固定（セル平均系が参照）
  │
  ├─ 手法: SkSL シェーダ   IMosaicMethod.CreateShader(inputs, p)    ← ここだけ手法ごと
  │        pixelate: ピラミッドの level = log2(block_eff) を最近傍で拡大
  │        hexcell : 六角格子の中心座標でピラミッドを 1 回サンプル（セル平均の近似）
  │        blur    : 前段 blur のみ（手法シェーダは恒等）
  │
  ├─ 後段: 色数制限        SkSL ColorFilter: floor(c * levels) / levels   固定（共通パラメータ）
  └─ 描画: DrawRect(paint with shader)   透明度は実効サイズに lerp 済み
```

- セル平均を「ピラミッドを 1 回サンプル」で近似するのは、フラグメントシェーダでセル内総和（scatter）ができないため。モザイク用途では見分けがつかず、CPU パスでも 1 px 1 サンプルで済む。
- 実効サイズは C# 側で計算して uniform に渡す（block_eff = lerp(a, 1, block)）。
- 生成パターン（ブロック / ノイズ / 六角 / Voronoi）も同じ IMosaicMethod（入力画像なし）で書く。hash(cellX, cellY, seed) をエリア原点基準で評価すれば FR-40 を満たし、FR-41 の 3×3 近傍探索もそのまま SkSL で書ける。FR-42（CPU でビットマップ生成）は「サイズ・パラメータ変更時に SkSL をオフスクリーン SKSurface へ 1 回描いて SKImage に固める」と読み替える。GPU なら 1 ms 未満、CPU でも 1080p で数十 ms。

### 4.3 拡張点（手法の追加手順）

```csharp
public sealed record ParamSpec(
    string Key, string LabelKey, string Unit,
    double Min, double Max,             // 入力欄が受ける範囲
    double SliderMin, double SliderMax, // スライダーの常用域
    double Default, bool LogScale = false, bool LerpWithOpacity = false);

public interface IMosaicMethod
{
    string Id { get; }                               // JSON のキー。改名禁止
    MethodKind Kind { get; }                         // Mosaic（入力あり） / Generated（入力なし）
    IReadOnlyList<ParamSpec> Params { get; }         // 設定ポップアップはここから自動生成
    PlatformSupport Support { get; }                 // OS 限定の宣言
    SKShader CreateShader(MosaicInputs inputs, ParamValues p);
}
```

追加手順: (1) `Mosaic/Methods/<Name>.sksl` を埋め込みリソースで置く、(2) `<Name>Method : IMosaicMethod` を書く、(3) MethodRegistry に 1 行登録する、(4) 文字列ファイルにラベルを足す、(5) ゴールデン画像テストを 1 枚追加する。UI・JSON・ツールバーの切替は触らない。

- ランタイムプラグイン（DLL 読込）はやらない。PublishTrimmed 単一ファイルと相性が悪く、要件は「リポジトリに手法を足しやすいこと」で足りる。
- 設定ポップアップは ParamSpec から「スライダー + 数値入力」の行を生成する汎用コントロール 1 つで賄う（FR-37）。更新間隔は LogScale=true。

### 4.4 性能見積もり（Windows ライブ 100 ms、エリア 800×450 物理 px、下に窓 3 枚）

| 工程 | 概算 | 備考 |
|---|---|---|
| PrintWindow × 3 | 2〜10 ms | 窓の全サイズ分をコピーするため、交差しない窓は呼ばない。4K 窓は 1 枚 5〜10 ms |
| 交差部分の合成 | < 0.5 ms | 行コピー |
| テクスチャ転送 1.4 MB | < 1 ms | GPU 時。Skia が描画時に実施 |
| 前段 blur r=8 | GPU < 1 ms / CPU 3〜5 ms | Skia 内蔵 |
| 手法シェーダ（1 サンプル） | GPU < 0.5 ms / CPU 5〜10 ms |  |
| 合計 | GPU 時 3〜12 ms（1 コアの 3〜12%） | NFR-04 の境界。支配項は PrintWindow |

GPU 計算を「もっと」使う必要が出るのは、手法が多タップ・多パス（大きな非分離カーネル、ヒストグラム、反復）になった時だけ。その時も B の範囲（多パス = オフスクリーン SKSurface の連鎖）で大半は収まる。

### 4.5 CPU パス（ソフトウェア描画時・テスト）

- Avalonia が Software にフォールバックした場合、同じ SkSL が Skia のラスタパイプラインで動く。Linux では撮り直しが稀なので問題にならない。Windows で GPU が取れない環境（RDP 等）は 100 ms 周期に対し数 ms〜十数 ms で許容範囲。
- テストは SkiaSharp の CPU SKSurface に描いてゴールデン PNG と比較する（Avalonia 不要、CI で全 OS 実行可）。

### 4.6 将来 Slang / compute を使いたくなったら

- SkSL は GLSL 系の小さな言語で、手法 1 つは 20〜60 行。Slang からの移植は手作業で十分。
- 本格的な compute が要るなら Silk.NET.Vulkan で別デバイスを持ち、結果画像を ICompositionGpuInterop で Avalonia の合成面に取り込む（Linux: dmabuf / opaque fd、Windows: D3D11 共有ハンドル。ANGLE との相互運用は要検証）。IMosaicMethod に Backend を増やして共存させる前提で、インターフェースは今から Skia 型を直接露出せず MosaicInputs 経由にしておく。

## 5. 下の内容の取得

### 5.1 抽象

```csharp
public interface IUnderlayCapture : IDisposable
{
    CaptureTraits Traits { get; }   // Live or Snapshot / ExposureFree / NeedsStripCooperation
    Task<CaptureResult> CaptureAsync(CaptureRequest req, CancellationToken ct);
}
// CaptureRequest: 物理 px の矩形、除外する Hider 窓のハンドル一覧、帯の高さ
// CaptureResult : BGRA premul バッファ、失敗理由（Black / Denied / Unavailable）
```

失敗は FR-52 のとおり AreaController が生成パターンへ切り替える。

### 5.2 Windows

| 段階 | 方式 | 採用時期 |
|---|---|---|
| 1 | EnumWindows を Z 順に走査 → 自分より下・可視・非クローク・交差ありの窓だけ PrintWindow(PW_RENDERFULLCONTENT) → DWMWA_EXTENDED_FRAME_BOUNDS で切り出し合成。最下層は Progman | v2.0 |
| 2 | Windows.Graphics.Capture のウィンドウ取得（IsBorderRequired=false は Win11 限定）。内容が変わった時だけフレームが来るため CPU 負荷が下がる。CsWinRT + トリミングの相性が課題 | 段階 1 で NFR-04 を満たせない場合 |

- 専用スレッドで実行する。PrintWindow は対象窓が応答停止だとブロックし得るため、窓ごとにタイムアウトを持ち、失敗した窓は次回まで黒扱いにする。
- 他の Hider 窓は列挙時に除外する（HWND 一覧を CaptureRequest で渡す）。

### 5.3 Linux（Wayland）

```
StripCoordinator（全モザイクエリア共有、1 本ずつ直列）
  for each 帯 y in 対象エリア群:
     1. 帯と交差する全モザイクエリア窓の該当行を透明化（アルファ 0）→ 描画完了を待つ
     2. IScreenGrabber.GrabAfter(t)   透明化より後のフレームを 1 枚
     3. 帯の行を貼り合わせ → 透明化を戻す
```

| 段階 | IScreenGrabber | 帯 1 本の所要 |
|---|---|---|
| 1 | PortalScreenshotGrabber: org.freedesktop.portal.Screenshot（PNG 書き出し待ち） | 0.1〜0.3 s |
| 2 | PipeWireGrabber: org.freedesktop.portal.ScreenCast（persist_mode=2、restore_token を ~/.hider に保存し毎回更新）+ libhidercap.so | 2〜3 フレーム |

- 「透明化より後」の判定は、透明化の描画完了時刻（描画完了コールバック + 1 フレーム余裕）と PipeWire の PTS を比べる。取れない場合は固定待ち（2 フレーム）にフォールバック。
- 分数スケーリング時の座標対応（XWayland 座標とポータルのフレーム座標）は §10 S-6 で確認する。

### 5.4 スレッドとデータの流れ

- 取得スレッド → CaptureResult（ダブルバッファ）→ UI スレッドへ通知 → MosaicPresenter が次の描画で SKImage.FromPixels（コピーなし）で参照する。描画スレッドが読んでいる間は書き換えない。
- 更新間隔タイマーは Core の RefreshScheduler が持ち、Windows でも Linux でも同じ契機（生成時 / 確定時 / ↻ / タイマー）で CaptureAsync を呼ぶだけにする。

## 6. ウィンドウと UI

### 6.1 エリア = 1 ウィンドウ（仕様どおり）と編集モードの余白

編集モードではツールバーをエリア上辺の外側に出す必要がある。別ウィンドウで追従させると移動時にずれ、前後関係も崩れるので、同じウィンドウを余白付きに広げる方式を採る（上: ツールバー高さ + 8、他 3 辺: ハンドル分）。余白は透明にする（ピクセル単位アルファは Linux の帯で既に前提）。画面上端で余白が外に出る場合はツールバーをエリア内側上部に置く。

- 隠しモードに戻る時に窓を元の矩形へ縮める。保存するのは常にエリア矩形（余白抜き）。
- 設定ポップアップは Avalonia の Popup（Linux では別 X11 窓になるが追従は Avalonia 任せ）。

### 6.2 状態機械

```
Hidden ──ダブルクリック / トレイ「すべて編集」/ 新規作成──▶ Editing
Editing ──✓ / Esc / トレイ「編集を終了」──▶ Hidden（保存）
Editing.Dragging(Move | Resize handle):
    Windows = タイマー継続（下の内容に追従、FR-38）/ Linux = 確定時のみ撮り直し
Mosaic 取得失敗 ──▶ Pattern = Generated（ツールバーに「取得できません」）
```

### 6.3 トレイ

- Windows: TrayIcon.Clicked（左）で、カーソル位置に枠なし小窓 + 同じ項目のメニューを出す。右は NativeMenu。
- Linux: DBus SNI。AppIndicator 拡張が左右ともメニューを開く前提。SNI Watcher が無い環境では、同じメニューを持つ小さな常駐窓にフォールバックする（§9 D-9）。

### 6.4 座標系ルール

- Core・取得・モザイクは物理 px のみ。DIP が出るのは Avalonia の Width / Height / ポインタ座標だけで、変換は AreaWindow の 1 箇所に閉じ込める。
- 保存形式は仕様どおり（x, y 物理 / width, height DIP）。復元時は Screens.ScreenFromPoint のスケールで物理サイズを求める。

## 7. 永続化・単一インスタンス

### 7.1 JSON の変更提案（仕様 §4.7 version 1 の修正。D-4 で確定）

FR-35（手法を切り替えても他の設定値を保持）と手法追加を両立するため、手法固有値を手法 ID で引く辞書にする。

```json
"mosaic": {
  "method": "pixelate",
  "common": { "blur": 0, "levels": 256, "intervalMs": 100, "stripPx": 8 },
  "params": { "pixelate": { "block": 12 }, "hexcell": { "radius": 10 }, "blur": { "radius": 8 } }
},
"generated": {
  "method": "block",
  "common": { "baseColor": "#68768A", "seed": 1234 },
  "params": { "block": { "cellSize": 12, "variance": 0.2, "gap": 0 }, "noise": { "scale": 4, "levels": 6 } }
}
```

- 未知の手法 ID は読み飛ばさず保持する（古いビルドで開いても消えない）。
- System.Text.Json ソースジェネレータ。辞書は `Dictionary<string, Dictionary<string, double>>` に固定する（トリミング安全）。
- 書き込みは tmp → rename、読み込み失敗は .broken（仕様どおり）。

### 7.2 単一インスタンス

~/.hider/instance.lock を FileShare.None で開いたまま保持する。Windows は共有違反、Linux は flock で 2 つ目が失敗して即終了（FR-08）。OS 別コードは不要。

## 8. ビルド・配布・CI

| 項目 | 方針 |
|---|---|
| csproj | 既存（net10.0, self-contained, single-file, trimmed, InvariantGlobalization）を踏襲し、Hider.Core を ProjectReference |
| バージョン固定 | Avalonia 12.1.4。SkiaSharp と Tmds.DBus.Protocol は Avalonia が参照する版に合わせる |
| ネイティブヘルパ | native/hidercap: C、公開関数 4 つ（hidercap_start / grab_after / last_sequence / stop。提案 P-2）、-fvisibility=hidden。linux-x64 / linux-arm64 を scripts/build.py が make（gcc）で native/hidercap/out/<rid>/ に作り、Hider.csproj が実行ファイルと同じディレクトリに同梱（single-file の外。既定の探索で読み込まれる） |
| CI | GitHub Actions（workflow_dispatch のみ）: windows-latest（win-x64）/ ubuntu-24.04（linux-x64）/ ubuntu-24.04-arm（linux-arm64。public リポジトリなら無料）。dotnet test は Core と App（headless）。ヘルパは Linux ランナーで libpipewire-0.3-dev を apt して実ビルド。作業指示書の GitHub Actions タブとの差（Xvfb での帯撮影の通し確認、format / python-lint ジョブ、windows-stress.ps1）は提案 P-8 で承認済み |
| 24.04 ランナーで 26.04 向けに作る | glibc は後方互換なので可。PipeWire は 0.3 系 ABI（libpipewire-0.3.so.0）で互換 |
| アイコン | Windows .ico（16/20/24/32/48/256）、Linux PNG + svg。生成スクリプトを scripts に追加 |

## 9. 着手前に決めること（2026-10-11 承認済み）

| # | 項目 | 決定 | 理由 / 決めないと止まるもの |
|---|---|---|---|
| D-1 | Linux バックエンド | X11（XWayland）固定。Avalonia.Wayland は参照しない | 位置指定・最前面が Wayland では不可。全 Linux 実装の前提 |
| D-2 | Windows の最低バージョン | 11（22000+） | WGC 枠なし・DWM 前提を使える。仕様書 §2 の「10 / 11」を更新 |
| D-3 | モザイク計算方式 | §4 の B（SkSL を描画時に適用）。GPU は Avalonia 既定の描画モードに乗る | IMosaicMethod の形・テスト方法・生成パターンの実装（FR-42 の読み替え）が決まる |
| D-4 | JSON 形式 | §7.1 の手法別辞書に変更（仕様 §4.7 を更新） | 永続化・設定 UI・手法登録の 3 箇所に効く。後から変えると version 2 が要る |
| D-5 | v2.0 の手法セット | モザイク: ピクセル化・ぼかし・色数・六角セル平均（ピラミッド近似、全 OS）。生成: ブロック・ノイズ。六角・Voronoi 生成は v2.1 | 実装量と検証画像の枚数。仕様 §10-2 の回答 |
| D-6 | Linux 第 2 段階（PipeWire）を v2.0 に含めるか | 含める。ただし第 1 段階を先に動かし、同じ IScreenGrabber で差し替える | 帯方式は第 2 段階が前提（240 px で 3〜9 s は実用外）。C ヘルパの CI 整備が必要。仕様 §10-1 の回答 |
| D-7 | Windows 取得の昇格条件 | PrintWindow で開始。実測で 1 コア 10% を超える、または常用アプリで黒になる窓が出たら WGC へ | §4.4 の支配項 |
| D-8 | 編集モードのツールバー配置 | 同一ウィンドウ余白方式（§6.1） | AreaWindow の構造 |
| D-9 | トレイが無い Linux 環境の扱い | 最小の常駐窓にフォールバック | 仕様は拡張前提のみ。何もしないと操作手段がなくなる |
| D-10 | 設定変更の即時反映の粒度 | uniform 更新のみ（再取得しない）。更新間隔・帯の高さの変更は次回タイマーから（変更そのものでは撮らない）。間隔を短くしたときは、予約済みの次回を「残り時間」と「変更時点から新しい間隔」の早い方に繰り上げる（提案 P-5、2026-10-12 承認） | FR-36 と Linux の露出回数の両立 |
| D-11 | プロジェクト分割 | Hider / Hider.Core / tests + native | テストと CI の形 |
| D-12 | ランタイムプラグイン | しない（コンパイル時登録） | 拡張性要件の解釈。トリミングとの整合 |

## 10. 技術リスクと検証スパイク（実装前に 1〜2 日で潰す順）

| # | 検証 | 合否基準 | 仕様 §11 対応 |
|---|---|---|---|
| S-1 | Win11: 枠なし・Topmost・非アクティブ・透明窓 + TrayIcon 左右クリック | 左クリックで自前メニューが出る。窓がフォーカスを奪わない | 2, 5 |
| S-2 | Ubuntu 26.04（GNOME 50, XWayland）: 同上 + ARGB visual で GLX 描画 + 帯の透明化が画面に反映 | 透明帯の下が実際に見える。Topmost が Wayland ネイティブ窓より上 | 4, 5 |
| S-3 | ISkiaSharpApiLeaseFeature + SKRuntimeEffect で取得画像にピクセル化を掛ける（GPU / Software 両方） | 両モードで同じ結果。1080p で GPU < 2 ms、CPU < 30 ms | 10 |
| S-4 | PrintWindow(PW_RENDERFULLCONTENT): Edge / VS Code / UWP / 管理者窓 / Progman / 排他全画面 | 黒になる種類の一覧と、黒検出（全画素 0）の信頼性 | 1 |
| S-5 | ポータル Screenshot の許可の記憶（.desktop 経由起動とターミナル起動の両方） | 2 回目以降ダイアログなし | 6 |
| S-6 | ScreenCast + PipeWire ヘルパ: restore_token の再利用、PTS で「透明化後」判定、帯 1 本のフレーム数、分数スケーリング時の座標対応 | 帯 1 本 ≤ 3 フレーム。座標ずれ 0 px | 6, 7, 8 |
| S-7 | PublishTrimmed: System.Text.Json source-gen + Tmds.DBus.Protocol + ColorPicker + SkiaSharp 3 | 3 ターゲットで起動・保存・復元が通る | 9 |
| S-8 | linux-arm64 実機（想定ハードは要確認）: GLX 描画と Software の両方で S-3 を再測定 | CPU パスでも 100 ms 内に収まる | 10 |

## 11. 実装順（スパイク後。作業指示書 §4 が正）

1. Core: モデル・JSON（§7.1）・RefreshScheduler・MethodRegistry と ParamSpec。テスト基盤（ゴールデン画像）。
2. App: トレイ、AreaWindow（隠し / 編集、自前ドラッグ、余白ツールバー）、塗りつぶし、保存・復元、単一インスタンス。
3. Mosaic: パイプライン（§4.2）+ ピクセル化 + ぼかし + 色数。MosaicPresenter の GPU / CPU 両パス。
4. Windows 取得（PrintWindow）+ ライブ更新 + 設定ポップアップ（ParamSpec 自動生成）。
5. 六角セル平均、生成パターン（ブロック・ノイズ）。
6. Linux 第 1 段階（Screenshot ポータル + StripCoordinator）。
7. Linux 第 2 段階（ScreenCast + libhidercap）。
8. 仕上げ: 確認ダイアログ、キーボード操作、画面外復元、アイコン、CI、README。

## 12. 参照（確認済みの外部情報）2026-10-11 時点

- Avalonia 12 は .NET 10 / SkiaSharp 3 対応、12.1（2026-07）で実験的 Wayland バックエンド追加（UsePlatformDetect では選ばれない）: [What's new in Avalonia 12.1](https://avaloniaui.net/blog/release-12-1)、[Breaking changes in Avalonia 12](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
- X11PlatformOptions.RenderingMode の既定は Glx → Software: [RenderingMode Property](https://api-docs.avaloniaui.net/docs/P_Avalonia_X11PlatformOptions_RenderingMode)
- ISkiaSharpApiLeaseFeature（unstable）: [Custom rendering](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering)
- GNOME 50 で X11 セッション削除、XWayland は残る（Ubuntu 26.04）: [GNOME 50 debuts with X11 axed](https://www.theregister.com/2026/03/19/gnome_50/)
- ScreenCast の restore_token（persist_mode、トークンは使い切り）: [org.freedesktop.portal.ScreenCast](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html)

## 改訂履歴（承認後）

| 日付 | 節 | 内容 | 根拠 |
|---|---|---|---|
| 2026-10-12 | 冒頭 | 保守方針を追加 | PR #1 の指摘 |
| 2026-10-12 | §8 | ネイティブヘルパの関数数（2 → 4）と同梱先（runtimes/<rid>/native/ → 実行ファイルと同じディレクトリ）を現状に合わせた | 提案 P-2 |
| 2026-10-12 | §9 | D-10 に未承認の提案 P-5 を併記していたのを、元の決定と区別して明記 | 提案 P-5 |
| 2026-10-12 | §8 | Windows ランナーを windows-2025 の固定から windows-latest に戻した（作業指示書どおり） | ユーザー指示 |
| 2026-10-12 | §8 | CI の作業指示書との差を承認済みとした | 提案 P-8（承認） |
| 2026-10-12 | §9 | D-10 に提案 P-5 の承認を取り込んだ | 提案 P-5（承認） |
