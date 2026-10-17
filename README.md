# Hider

画面の一部を覆って隠す常駐ツールです。トレイから「エリア」を追加し、塗りつぶし・モザイク（下の内容をぼかす）・生成パターンのどれかで覆います。配信・画面共有・録画で映したくない部分を隠すことを想定しています。エリアは画面上に常に表示されるので、画面共有や録画にも覆った状態で映ります。

- エリアは何枚でも置けます。位置・大きさ・設定は自動で保存され、次回起動時に復元されます。
- モザイクの手法: ピクセル化 / 六角セル平均 / ガウスぼかし（ぼかしと色数の制限を重ねがけ可）。
- 生成パターン: ブロック / ノイズ（下の内容を使わないので取得できない窓の上でも使えます）。

## 動作環境

| OS | アーキテクチャ | 備考 |
|---|---|---|
| Windows 11 | x64 | |
| Ubuntu 26.04（GNOME） | x64 / arm64 | XWayland 上で動作。トレイには AppIndicator 拡張（既定で有効）が必要。無い場合はメニューの小窓を出します |

配布物は .NET ランタイム不要のフォルダです（実行ファイルの隣にネイティブライブラリが並ぶので、フォルダごとコピーして使います）。

## 使い方

1. 起動するとトレイにモザイク柄のアイコンが出ます（メイン窓はありません）。
2. トレイのメニュー「新しいエリアを追加」で、カーソルのある画面（Linux では主画面）の中央にエリアが編集モードで出ます。
3. 編集モードの操作:
   - 本体をドラッグで移動、四隅と四辺のハンドルで大きさを変更（最小 24×24）。
   - 矢印キーで 1 px、Shift + 矢印で 10 px 移動。Delete でそのエリアを削除。
   - ツールバーで覆い方（塗りつぶし / モザイク / パターン）を切り替え、⚙ で詳細設定（透明度、手法、ブロックの大きさ、ぼかし、色数、更新間隔、色など）。
   - ✓ または Esc で編集を終わります。
4. 編集していないエリアはクリックを下に通しません。ダブルクリックで再び編集モードになります。トレイの「すべてのエリアを編集」でまとめて編集できます。

モザイクの中身は、Windows では一定間隔（既定 100 ms）で下の窓から取り直します。Linux では作成時・移動の確定時・↻ ボタン・更新間隔（既定 5 分）で撮り直します。下の内容を取得できない場合は自動で生成パターンに切り替わり、ツールバーに「取得できません」と出ます。

## インストール

インストーラを 1 つ実行するだけです。最新のリリースのビルド済みファイル（.NET ランタイム込み）をダウンロードし、SHA-256 を確かめてからインストールし、Hider を起動します。.NET SDK や Python、リポジトリのクローンは要りません。

| OS | 操作 | インストール先 | 作るもの |
|---|---|---|---|
| Windows | [install.bat](https://github.com/HIBICUS-CAI/Hider/releases/latest/download/install.bat) をダウンロードしてダブルクリック | `%LOCALAPPDATA%\Programs\Hider` | デスクトップのショートカット |
| Linux | 端末で `curl -fsSL https://github.com/HIBICUS-CAI/Hider/releases/latest/download/install.sh \| sh` | `~/.local/share/Hider` | `~/.local/share/applications/Hider.desktop`（アクティビティから検索・ドックに固定できる） |

- 更新も同じ操作です（既存のファイルを上書きします）。Hider を起動している場合は、トレイの「終了」で終了してから実行してください（起動中はインストーラが止まります）。
- インストール先は `install.bat <フォルダ>` / `sh install.sh -d <フォルダ>` で変えられます。成功すると install.bat の窓は自動で閉じ、失敗したときだけ理由を表示して止まります。
- インストーラが通信するのは、そのリリースのアーカイブ（`Hider-<版>-<rid>.zip` / `.tar.gz`）のダウンロードだけです。アーカイブをインストーラと同じフォルダ（`curl … | sh` の場合は実行したディレクトリ）に置いておくと、ダウンロードせずにそれを使います。Hider 本体は通信しません。
- Windows では、ダウンロードや実行の時にセキュリティの警告（SmartScreen など）が出ることがあります（署名していないため）。「保存」「詳細情報」→「実行」で続けます。
- アンインストールはインストール先のフォルダと、ショートカット（Hider.lnk）またはデスクトップエントリ（Hider.desktop）を削除するだけです（設定は `~/.hider` に残ります）。

## ビルド

必要なもの: .NET 10 SDK（global.json は 10.0.100 以上の 10.0 系を受け付けます。動作を確かめた版は 10.0.112 で、CI は 10.0 系の最新を使います。SDK が暗黙に加える Microsoft.NET.ILLink.Tasks の版は Directory.Build.targets で固定しているので、SDK を更新してもロックファイルは変わりません。アナライザーは AnalysisLevel=latest-recommended で SDK に合わせて規則を追い、新しい SDK で `dotnet format`、アナライザー、コードスタイルの結果が変わったときは、その時点でコードを直します）、Python 3.10 以上（標準ライブラリのみ。Windows は `python`、Linux は `python3` か `./scripts/build.py` のように直接実行）。Linux 版を Linux 上でビルドする場合は `build-essential` と `libpipewire-0.3-dev` も必要です（PipeWire ヘルパ libhidercap.so をビルドするため）。

```sh
python scripts/build.py              # このマシン向け
python scripts/build.py linux-x64    # 対象を指定（win-x64 / linux-x64 / linux-arm64 / all）
bin/publish/linux-x64/Hider --selftest
```

Windows の Hider.exe は GUI アプリなので、cmd は終了を待たずに次のプロンプトを出します（出力は起動元のコンソールに出ます）。終了コードまで見るときは `start /wait bin\publish\win-x64\Hider.exe --selftest` の後に `echo %ERRORLEVEL%`、PowerShell では `(Start-Process bin\publish\win-x64\Hider.exe --selftest -Wait -PassThru -NoNewWindow).ExitCode` を使います。

出力は `bin/publish/<rid>/` です。別アーキテクチャの Linux 向けにクロスビルドする場合、クロスコンパイラが無ければ libhidercap.so 抜き（ScreenCast を使わず Screenshot ポータルで撮る）でビルドされます。

アイコンを作り直す場合は `python scripts/make_icons.py`（生成物はリポジトリに含まれています）。

### ビルドしたものをインストール

```sh
python scripts/install.py            # 既定の場所へ
python scripts/install.py -d <folder>
```

`bin/publish/<rid>/` を、リリースのインストーラと同じ場所に同じ形で入れます（上の「インストール」の表）。既存のファイルは上書きします。Hider を終了してから実行してください。

## 設定ファイル

`~/.hider`（Windows は `%USERPROFILE%\.hider`）に置きます。

| ファイル | 内容 |
|---|---|
| `areas.json` | エリアの一覧（位置は物理 px、大きさは DIP）と各エリアの設定。変更のたびに保存（一時ファイル + 置き換え）。壊れている場合は `areas.json.broken` に名前を変えて残し（以前の `.broken` は日時付きの名前で残します）、空の状態で起動します。ほかのプログラムが開いていて読めない場合はファイルに触れず空の状態で起動し、その回は保存しません |
| `restore_token` | Linux の画面共有（ScreenCast）の許可を次回起動時も使うための値。消すと次回ダイアログが出ます |
| `instance.lock` | 二重起動の防止 |

`--verbose` を付けて起動すると、取得・保存などのログを標準エラー出力に出します（Windows の Hider.exe は GUI アプリなので、ログを見るときは `Hider.exe --verbose 2> hider.log` のようにリダイレクトします。常駐する起動は起動元のコンソールにつながらないので、そのコンソールで Ctrl+C を押したり閉じたりしても Hider は終了しません）。

## 画面の取得と既知の制約

- **Windows**: エリアの下にある窓を 1 枚ずつ取得して重ね合わせます（PrintWindow）。そのエリア自身の窓は除きます（ほかのエリアが下にあれば、それも写ります）。一部の窓（排他全画面のゲームや保護された動画など）は真っ黒にしか取れず、その場合は生成パターンに切り替わります。
- **Linux**: 自分の窓を除いて画面を撮る手段がないため、エリアを横の帯（既定 8 px）に分け、その帯だけを一瞬透明にして撮ることを繰り返します。撮り直しの間、帯 1 本分だけ下の内容が見えます（帯の高さ 0 で一括にすると速くなる代わりに全体が一瞬見えます）。
  - 画面共有（ScreenCast）ポータルと同梱の libhidercap.so で撮ります。初回は画面共有のダイアログでモニタを選びます（2 回目以降は不要）。撮影中は GNOME の画面共有表示が出て、数秒で消えます。
  - 画面共有は選んだ 1 枚のモニタだけを写します。ほかのモニタのエリアや、libhidercap.so・PipeWire が無い環境では、スクリーンショットポータルで撮ります（1 帯ごとに 0.1〜0.3 秒かかります）。
  - Wayland ネイティブではなく XWayland のクライアントとして動きます。分数スケーリング（125% など）での位置合わせは、実機では確認していません（v2.0 は実機検証なし）。
- GPU が使えない環境（リモートデスクトップなど）ではモザイクの描画を CPU で行うため、大きなエリアでは更新が重くなります。
- 実機で確認する項目（v2.0 では未実施）は docs/manual-tests.md にあります。

## 開発

```sh
dotnet build -c Release -warnaserror
dotnet test tests/Hider.Core.Tests
dotnet test tests/Hider.App.Tests
scripts/xvfb-strip-smoke.sh [--screencast]   # Linux の帯撮影を Xvfb と偽ポータルで通し確認
```

設計は docs/v2-implementation-plan.md、作業の進め方は CLAUDE.md、CI は `.github/workflows/ci.yml`、リリースは `.github/workflows/release.yml` を参照してください。

### CI と main の保護

CI は main への PR ごとに動き（同じ PR に push すると古い実行は取り消されます）、Actions タブから手動でも実行できます。最後のジョブ `summary` は、どれかのジョブが失敗・取り消しになると赤になるので、これを main の必須チェックにします（リポジトリの設定で、ワークフローからは設定できません）。

- Settings → Rules → Rulesets で main（Default branch）に「Require status checks to pass」を付け、チェックに `summary`（GitHub Actions）を追加する。main に入るコミットは `summary` が緑であることが必要になり、main への直接 push も、そのコミットが他のブランチで CI を通っていない限り拒否されます（直接 push を残したい場合は Bypass list に自分を入れる）。「Require a pull request before merging」は任意です（リリースは PR を作らないので、付けても付けなくても動きます）。

### リリース

オーナーの操作は、Actions タブの「Release」→「Run workflow」→「Run workflow」だけです（ブランチは main のまま。main 以外では失敗します）。あとは `.github/workflows/release.yml` が行います。リリースするのは main の HEAD で、main へは何も push しません。

1. prepare: 版を決める。版の正はタグ（`v<MAJOR.MINOR.PATCH>`）です。
   - タグがまだない（初回）: `Directory.Build.props` の `<Version>`（2.0.0）をそのまま使う。
   - タグがある: 最新のタグを `bump` で上げる（既定は minor で `2.x.0` → `2.(x+1).0`。patch / major も選べます）。このとき `Directory.Build.props` は見ません（`<Version>` はソースからビルドしたときの版で、更新は任意です）。
   - main の HEAD に既にタグがある（リリース済み。誤って 2 回実行した場合など）は失敗します。
2. ci: そのコミットで CI（`ci.yml` を呼び出し、テスト・format・python-lint・native・publish と `--selftest`）。publish は決めた版でビルドし（`build.py --version`）、`--version` の表示が一致することを確かめます。
3. package: publish 出力から `Hider-<版>-<rid>.zip` / `.tar.gz`、`install.bat`、`install.sh`、`SHA256SUMS.txt` を作る（`scripts/make_release.py`。インストーラにはリリースの URL と各アーカイブの SHA-256 が入ります）。
4. verify: 3 RID のランナーで、インストーラをアーカイブと並べて実行し、入ったものの `--version` と `--selftest` を確かめる。
5. release: タグ `v<版>` を付けて GitHub Release を作り、アセットを添付する（書き込み権限を持つのはこのジョブだけ）。
6. smoke: 公開されたリリースからインストーラをダウンロードして実行し、同じ確認をする。

途中で失敗した場合は、その実行の「Re-run failed jobs」でやり直せます（タグは手順 5 まで付かないので、手順 1 からやり直しても同じ版になります）。

## ライセンス

MIT License（LICENSE）。配布物に同梱している .NET ランタイム、Avalonia、SkiaSharp / HarfBuzzSharp（Skia、HarfBuzz ほか）、Tmds.DBus.Protocol、ANGLE などの著作権表示とライセンスは THIRD-PARTY-NOTICES.txt にあり、配布物のフォルダにも LICENSE.txt とあわせて入っています。
