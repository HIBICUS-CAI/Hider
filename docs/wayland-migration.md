# 純正 Wayland 移行への備え（作業指示書 §6）

v2 は XWayland（X11 クライアント）で動く（D-1）。将来 Avalonia.Wayland へ移る時に変わるのは「窓をどこに置き、どう最前面にするか」だけになるよう、X11 前提のコードを下表の箇所に閉じ込めている。コード上は `// XWAYLAND-ASSUMPTION:` コメントで印を付ける（`grep -rn XWAYLAND-ASSUMPTION src` で一覧できる）。

## XWayland 前提の箇所

| ファイル | 関数 / 型 | 前提 | 移行時の扱い |
|---|---|---|---|
| src/Hider/Platform/Linux/X11WindowHost.cs | X11WindowHost | クライアントが窓の絶対位置を決められ、Mutter が `_NET_WM_STATE_ABOVE` を尊重する | WaylandExtensionHost（GNOME Shell 拡張の DBus を呼ぶ）に差し替える |
| src/Hider.Core/Model/Placement.cs | Placement.Fit | 全モニタの絶対座標（物理 px）を知っている | 拡張の GetPlacement / モニタ情報で置き換える |
| src/Hider/Platform/AvaloniaScreenService.cs | AvaloniaScreenService.CursorScreen | ポインタ位置は XWayland の窓の上にある時しか分からない（そのため Linux は新規エリアを主画面に出す） | 拡張にポインタのモニタを問い合わせる |
| src/Hider/Areas/AreaManager.cs | AreaManager.CaptureStarting | 帯方式に渡すエリア矩形と仮想画面が X11 の絶対座標で、ポータルのフレームがその範囲を写す（StripCoordinator.CopyRows が倍率とオフセットで対応付け） | 拡張の GetPlacement で得た配置（物理 px）とモニタ情報に置き換える。帯の透明化と撮影の流れは変わらない |
| src/Hider/Program.cs | BuildAvaloniaApp | `UsePlatformDetect()` + `X11PlatformOptions`（Avalonia.Wayland は参照しない） | プラットフォーム選択はここ 1 箇所だけ変える |

帯の透明化は窓の描画内容（AreaWindow のクリップでアルファ 0 にした行）で行い、X11 のシェイプやリージョンは使わない（Step 7）。取得はポータル（Screenshot / ScreenCast）のみで、X11 の XGetImage 等は使わない。トレイは DBus StatusNotifierItem（Avalonia 標準）。いずれも移行で変わらない。

## 移行時に必要になるもの（今回はやらない）

| 課題 | 状況 | 現実的な解 |
|---|---|---|
| 絶対位置への配置と最前面 | xdg-shell にない。wlr-layer-shell は GNOME 未対応 | GNOME Shell 拡張を自作し、DBus で「このアプリの窓を (x, y) に置き常に上にする」を提供する。仕様書 §12.2 の「露出ゼロ化」と同じ拡張にまとめられる |
| クライアントが自分の画面座標を知らない | Wayland の設計。ポータルのフレーム座標との対応が取れない | 同じ拡張が配置結果（物理 px）を返す。IAreaWindowHost に WaylandExtensionHost を足すだけで Core は変わらない |
| Avalonia.Wayland の成熟度 | 12.1 で実験的。UsePlatformDetect は選ばない | ポインタキャプチャ、Popup、ピクセル単位アルファ、EGL 描画を S-2 相当のスパイクで再検証する |
| 分数スケーリング | Wayland ネイティブなら fractional-scale-v1 で正確になる | 物理 px を Core の単位にしてあるので、IAreaWindowHost.Scale の実装差し替えで済む |

## 拡張に求める DBus インターフェース案

```
interface io.github.hibicus.Hider.Shell1   (object /io/github/hibicus/Hider/Shell1)
  method Place(s window_id, i x, i y, i width, i height)   物理 px で配置
  method SetAbove(s window_id, b above)                     常に最前面
  method GetPlacement(s window_id) -> (i x, i y, i width, i height, d scale)
  signal PlacementChanged(s window_id, i x, i y, i width, i height, d scale)
```

`window_id` は窓の `xdg_toplevel` に付けるアプリ ID + エリア ID（例 `hider:0f3b8d2e`）。拡張は Meta.Window を app id とタイトルで特定する。
