# Hider v2 — 作業ガイド

画面の一部を覆って隠す常駐ツール（トレイ常駐、複数エリア、塗りつぶし / モザイク / 生成パターン）。.NET 10 + Avalonia 12.1.4、対象は win-x64 / linux-x64 / linux-arm64。配布はネイティブ AOT 版が既定で、通常版（self-contained + trimmed single file の JIT）を `--jit` で選べる。

## 資料（判断の優先順位: 仕様書 > 作業指示書 > 設計書 > UI デザインキャンバス > 現行コード > 一般的な慣習）

| 資料 | 場所 | 内容 |
|---|---|---|
| 仕様書（Hider v2 仕様書） | https://claude.ai/artifact/QT9Dn8x6r69bx6FxD87q8P | 挙動・FR / NFR・露出の意味論・決定履歴 |
| 作業指示書（Hider v2 実装作業指示書） | https://claude.ai/artifact/Cf2T1HM3yg78c6s5N8Crh7 | Step 0〜9 の順序・契約（§3）・完了条件・報告形式。タブ: 設計書 / テスト項目 / GitHub Actions / 付録: 現行コード |
| 設計書 | docs/v2-implementation-plan.md | 方式比較・性能見積・スパイク定義・決定 D-1〜D-12 |
| 追加したテスト項目 | docs/test-items.md | 作業指示書「テスト項目」タブ以降に足した CT / GT と、ST（scripts）・NT（native）の一覧と対応 FR（タブには未反映） |
| UI デザインキャンバス | https://claude.ai/artifact/VKTc6PcAYtSMLHgfw5814y | 画面 8 枚。寸法・配色・項目名の正 |
| 過去の進捗報告・スパイク | ブランチ `archive/pr1-removed`（3bdd9581495d41cb3c20cb0dc30be2f0e4524050） | PR #1 の終わりに main から外した docs/progress.md（Step 0〜9 の報告、提案 P-1〜P-4）、docs/golden-diffs、spikes/。`git fetch origin archive/pr1-removed` の後 `git show FETCH_HEAD:docs/progress.md` |

矛盾を見つけたら勝手に解釈せず、コミット本文に「提案」（問題 / 選択肢 / 推奨 / 影響する FR・決定）を書き、推奨案で実装してコミットに `pending: <提案 ID>` を付ける。

- ID は既存の最大番号 + 1（`git log --grep "pending: P-"` で一覧できる。P-1〜P-4 は archive/pr1-removed の docs/progress.md）。
- ユーザーが承認・却下したら、その後のコミット本文に `resolved: <提案 ID>（承認 | 却下: 理由）` を書く（却下なら同じコミットで直す）。未決の提案 = pending にあって resolved にないもの（`git log --grep "resolved: P-"`）。
- 承認済み: P-1（ソフトウェア描画はセル単位評価と結果のキャッシュ）、P-2（libhidercap の公開関数 4 つ）、P-3（実機検証を待たずに進める。v2.0 では Windows・Linux とも実機検証なし、docs/manual-tests.md）、P-4（CA1707 / CA1725 のみ無効）、P-5（更新間隔を短くしたときは次回を繰り上げる）、P-6（MIT ライセンス）、P-7（矢印キー移動は最後の押下から 500 ms 後に撮り直す）、P-8（CI の作業指示書との差: Xvfb での帯撮影の通し確認、format / python-lint ジョブ、windows-stress.ps1。その後ユーザー指示で installer-windows ジョブを追加）、P-9（リリースは release.yml。書き込み権限は release ジョブだけ）、P-10（トレイメニューの「終了」の上に押せない行「バージョン <版>」。版は `--version` と同じ）、P-11（OS 専用のテスト（Win32 API、Linux の dbus-daemon・PipeWire・libhidercap）は、実行できない OS では理由付きで skip する。対応する OS の CI では必ず実行し、Linux の CI では実環境テストを skip しない）、P-12（areas.json の pattern は仕様の名前を大小文字・前後の空白を問わず読み、未知の文字列は mosaic）、P-13（scripts の unittest は ST-xx、native の C テストは NT-xx。一覧は docs/test-items.md）、P-14（配布はネイティブ AOT 版が既定、通常版（self-contained + trimmed single file の JIT）を `--jit` で選べる。名前は通常版だけ `-jit` 付き。AOT 版はそのマシンの RID だけビルドでき、CI は各 RID で両方を確かめる。仕様書「ランタイム」行と NFR-06 はこれに読み替える）。

## 構成

```
src/Hider.Core     Avalonia 非依存（SkiaSharp と BCL のみ）。Model / Mosaic / Capture / Scheduling / Persistence / Strings
src/Hider          Avalonia アプリ。OS 依存は Platform/Windows, Platform/Linux に閉じる
native/hidercap    PipeWire ヘルパ（C）。make RID=<rid> → out/<rid>/libhidercap.so
tests/             Hider.Core.Tests（CUI, CT-xx）/ Hider.App.Tests（Avalonia headless, GT-xx。Linux では私的な dbus-daemon・PipeWire と本物の libhidercap.so のテストも）/ scripts（scripts/ の unittest）。偽実装は Hider.Core.Tests/Fakes
scripts/           build.py / install.py / make_icons.py / check_wayland_list.py / make_release.py（標準ライブラリのみ、Python 3.10 以上）、release/（install.bat / install.sh / リリースノートの雛形）、xvfb-strip-smoke.sh（Linux の帯撮影の通し確認）、windows-stress.ps1（Windows の負荷下の通し確認。CI の publish）
tools/fakeportal   Xvfb 用の偽 xdg-desktop-portal（Screenshot / ScreenCast）。sln に含めない
```

## コマンド

```sh
dotnet build -c Release -warnaserror         # アナライザー（latest-recommended）とコードスタイル（命名・名前空間・不要な using）も検査
dotnet format Hider.slnx --verify-no-changes # 書式（CI はターゲットによらず ubuntu-24.04 の format ジョブで実行）
dotnet test tests/Hider.Core.Tests
dotnet test tests/Hider.App.Tests             # Linux では先に make -C native/hidercap（ビルドがテスト出力へ .so をコピー）。CI の Linux では実環境テストを skip しない
make -C native/hidercap test                  # libhidercap の単体テスト（CT-90 のフレーム時刻、API の検査とエラー経路。libpipewire のクライアントモジュールが要る、デーモンは不要）
python -m unittest discover -s tests/scripts -t .   # scripts/ の単体テスト（標準ライブラリのみ。install.sh は sh / dash / bash と偽コマンドで実行）
python scripts/build.py <rid>                 # AOT 版 → bin/publish/<rid>/（そのマシンの RID だけ。Ubuntu では python3。scripts/ は直接実行もできる）
python scripts/build.py <rid> --jit           # 通常版 → bin/publish/<rid>-jit/（どのマシンでも全 RID）
bin/publish/<rid>/Hider --selftest            # 終了コード 0。1 行目が runtime: aot / jit（Windows の cmd では start /wait、README 参照）
scripts/xvfb-strip-smoke.sh [--screencast]    # Linux: Xvfb + 偽ポータルで帯撮影（第 1 / 第 2 段階）
python scripts/make_icons.py                  # アイコン（src/Hider/Assets）を作り直す
python scripts/make_release.py version | bump <minor|patch|major> <版> | package <版> --repo <owner/repo> --out-dir <dir>   # リリース用（release.yml が呼ぶ。CI はダミーで通し確認）
```

CI は `.github/workflows/ci.yml`（main への pull_request、workflow_dispatch、release.yml からの workflow_call。ジョブ `summary` が main の必須チェック）。リリースは `.github/workflows/release.yml`: 手動実行 1 回で main の HEAD を、最新のタグ `v<版>` を上げた版（タグがなければ Directory.Build.props の `<Version>`）でリリースする。CI（`build.py --version` で版を指定）・配布物・インストーラの確認・タグ・GitHub Release。main へは push しない（手順は README の「リリース」、提案 P-9 承認済み）。ci.yml への `on: push` / `schedule` の追加、`continue-on-error`、テストの skip / `--filter` による間引きは禁止。

## 作業ルール（指示書 §2 の要約）

- Step は順に進め、完了条件 + テスト緑 + 報告で閉じる。確認順: 実装 → テスト → `dotnet build -warnaserror` と `dotnet test` → `python scripts/build.py <rid>` → `--selftest`。
- コミットは 1 コミット 1 つの内容。1 行目 `step<N>: <要約>`、本文に対応 FR と確認した RID（日本語）。
- 生成物（bin/、obj/、publish 出力）はコミットしない。src/Hider のロックファイルは 2 つ（AOT 版の packages.lock.json、通常版の packages.jit.lock.json）。パッケージや Directory.Build.targets の `HiderPublishPackVersion` を変えたら両方を作り直す（README の「ビルド」）。ゴールデン画像を更新したら、理由と差分の要約をコミット本文に書く（`HIDER_UPDATE_GOLDEN=1` で再生成。差分画像はコミットしない）。

### 禁止

- 自ウィンドウをキャプチャから除外する API（`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 等）。NFR-01
- リフレクション依存の JSON（型引数なしの `JsonSerializer` 呼び出し等）。必ず `HiderJsonContext`。NFR-06
- ランタイムのアセンブリ読込（プラグイン）。D-12
- `Avalonia.Wayland` の参照、`UseWayland()`。D-1
- `BeginMoveDrag` / `BeginResizeDrag`（ドラッグは自前実装）
- スナップショット画像のディスク保存、旧 `setting.txt` の読込。FR-53、FR-65
- テストの無効化・スキップで CI を緑にすること
- 許可リスト外の NuGet: Avalonia 12.1.x 系、SkiaSharp と SkiaSharp.NativeAssets.*（Avalonia と同版）、Tmds.DBus.Protocol、xunit 系と Microsoft.NET.Test.Sdk（テストのみ）、Avalonia.Headless.XUnit、BenchmarkDotNet（bench を作る場合のみ。現在 bench プロジェクトはない）。版は Directory.Packages.props で一元管理
- 日本語以外の UI 文字列、Strings 以外に散らばる UI 文字列。FR-70

### コーディング規約

- Nullable / ImplicitUsings 有効、TreatWarningsAsErrors。src/ は IsTrimmable + EnableTrimAnalyzer。警告は抑制せず直す。
- 命名: 引数は `_name`、private フィールドは接頭辞なし camelCase、他は .NET 標準。名前空間はブロック形式。
- コード内コメントは英語、ドキュメントとコミット本文は日本語。
- P/Invoke は `LibraryImport`（`DllImport` 不可）。OS 依存コードは `[SupportedOSPlatform]` と `OperatingSystem.IsWindows()` / `IsLinux()` で囲む。
- SkSL は `src/Hider.Core/Mosaic/Methods/<id>.sksl`（埋め込みリソース）。先頭コメントに uniform と child の一覧。uniform 名 = `ParamSpec.Key`。
- 取得・合成は専用スレッド、UI 更新は Dispatcher 経由。`async void` はイベントハンドラのみ。
- ログは `--verbose` 時のみ標準エラーへ。テレメトリ・ネットワークアクセスなし。

### 座標・露出

- Core・取得・モザイクは物理 px のみ。DIP が現れるのは AreaWindow の Width / Height とポインタ座標だけで、変換は `IAreaWindowHost` の実装 1 箇所に閉じる。
- 保存形式は x, y = 物理 px、width, height = DIP（指示書 §3.4）。
- Windows はエリアを透明化しない。Linux は撮影中の帯だけ透明化する。
- X11 前提のコードには `// XWAYLAND-ASSUMPTION:` を付け、docs/wayland-migration.md に一覧を維持する（`python scripts/check_wayland_list.py` で照合。CI でも実行）。
