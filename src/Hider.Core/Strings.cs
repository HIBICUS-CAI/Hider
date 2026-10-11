using System.Globalization;
using System.Text;
using Hider.Core.Mosaic;

namespace Hider.Core
{
    /// <summary>
    /// Every UI string (FR-70: Japanese only, one file). Fixed texts are properties; texts with numbers are methods. Keys are
    /// also used as <c>LabelKey</c> by mosaic methods and parameters, so the settings popup can look labels up by key.
    /// </summary>
    public static class Strings
    {
        private static readonly Dictionary<string, string> texts = new()
        {
            // Tray menu (FR-03, FR-07)
            ["tray.add"] = "新しいエリアを追加",
            ["tray.editAll"] = "すべてのエリアを編集",
            ["tray.endEdit"] = "編集を終了",
            ["tray.deleteAll"] = "すべてのエリアを削除…",
            ["tray.exit"] = "終了",
            ["tray.tooltip"] = "Hider — エリア {0} 個",

            // Delete all confirmation (FR-05)
            ["confirm.deleteAll"] = "{0} 個のエリアをすべて削除します。",
            ["confirm.cannotUndo"] = "この操作は元に戻せません。",
            ["confirm.cancel"] = "キャンセル",
            ["confirm.delete"] = "削除",
            ["confirm.close"] = "閉じる",

            // Edit toolbar (FR-34, FR-52)
            ["pattern.fill"] = "塗りつぶし",
            ["pattern.mosaic"] = "モザイク",
            ["pattern.generated"] = "パターン",
            ["toolbar.settings"] = "設定",
            ["toolbar.refresh"] = "今すぐ更新（下の内容を撮り直す）",
            ["toolbar.delete"] = "このエリアを削除",
            ["toolbar.done"] = "完了",
            ["toolbar.captureFailed"] = "取得できません",

            // Settings popup (FR-35 .. FR-37)
            ["settings.opacity"] = "透明度",
            ["settings.method"] = "手法",
            ["settings.fillSection"] = "塗りつぶし",
            ["settings.mosaicSection"] = "モザイク（画面の内容を加工）",
            ["settings.generatedSection"] = "パターン（生成・下の内容に依存しない）",
            ["settings.color"] = "色",
            ["settings.presets"] = "プリセット",
            ["settings.baseColor"] = "ベース色",
            ["settings.seed"] = "シード",
            ["settings.reseed"] = "シードを振り直す",
            ["settings.pickColor"] = "色を選ぶ",
            ["settings.pickColorWithValue"] = "色を選ぶ: {0}",

            // Mosaic methods and their parameters (D-5)
            ["method.pixelate"] = "ピクセル化",
            ["method.hexcell"] = "六角セル平均",
            ["method.blur"] = "ガウスぼかし",
            ["param.block"] = "ブロック",
            ["param.radius"] = "半径",
            ["param.blur"] = "ぼかし",
            ["param.levels"] = "色数",
            ["param.intervalMs"] = "更新間隔",
            ["param.stripPx"] = "帯の高さ",

            // Generated methods and their parameters (D-5)
            ["method.block"] = "ブロック",
            ["method.noise"] = "ノイズ",
            ["param.cellSize"] = "セルサイズ",
            ["param.variance"] = "色のばらつき",
            ["param.gap"] = "目地幅",
            ["param.noiseScale"] = "ノイズスケール",
            ["param.noiseLevels"] = "段階数",

            // Fallback resident window when there is no tray (D-9)
            ["fallback.title"] = "Hider（トレイが使えないため窓で表示しています）",
        };

        /// <summary>Units after a number box. Symbols such as "px" are not Japanese words, so they are kept apart from texts.</summary>
        private static readonly Dictionary<ParamUnit, string> units = new()
        {
            [ParamUnit.None] = "",
            [ParamUnit.Px] = "px",
            [ParamUnit.Colors] = "色",
            [ParamUnit.Steps] = "段",
            [ParamUnit.Percent] = "%",
            [ParamUnit.Ms] = "ms",
        };

        /// <summary>The toolbar's size in physical pixels (FR-34). Symbols and digits only, so kept apart from texts.</summary>
        private const string SizeFormat = "{0} × {1} px";

        // Parsed once (after the tables above: static fields initialise in order).
        private static readonly CompositeFormat trayTooltip = CompositeFormat.Parse(Get("tray.tooltip"));
        private static readonly CompositeFormat toolbarSize = CompositeFormat.Parse(SizeFormat);
        private static readonly CompositeFormat pickColorWithValue = CompositeFormat.Parse(Get("settings.pickColorWithValue"));
        private static readonly CompositeFormat confirmDeleteAll = CompositeFormat.Parse(Get("confirm.deleteAll"));

        /// <summary>Platform name shown as a badge next to Linux-only settings. A proper noun, not translated.</summary>
        public const string LinuxBadge = "Linux";

        /// <summary>Product name used in window titles. A proper noun, not translated.</summary>
        public const string AppName = "Hider";

        public static IReadOnlyDictionary<string, string> All => texts;

        public static string Unit(ParamUnit _unit)
        {
            return units[_unit];
        }

        public static bool Contains(string _key)
        {
            return texts.ContainsKey(_key);
        }

        /// <summary>The text for a key; the key itself when it is unknown so a missing label shows up instead of crashing.</summary>
        public static string Get(string _key)
        {
            return texts.TryGetValue(_key, out string? text) ? text : _key;
        }

        public static string TrayAdd => Get("tray.add");
        public static string TrayEditAll => Get("tray.editAll");
        public static string TrayEndEdit => Get("tray.endEdit");
        public static string TrayDeleteAll => Get("tray.deleteAll");
        public static string TrayExit => Get("tray.exit");
        public static string ConfirmCannotUndo => Get("confirm.cannotUndo");
        public static string ConfirmCancel => Get("confirm.cancel");
        public static string ConfirmDelete => Get("confirm.delete");
        public static string ConfirmClose => Get("confirm.close");
        public static string PatternFill => Get("pattern.fill");
        public static string PatternMosaic => Get("pattern.mosaic");
        public static string PatternGenerated => Get("pattern.generated");
        public static string ToolbarSettings => Get("toolbar.settings");
        public static string ToolbarRefresh => Get("toolbar.refresh");
        public static string ToolbarDelete => Get("toolbar.delete");
        public static string ToolbarDone => Get("toolbar.done");
        public static string ToolbarCaptureFailed => Get("toolbar.captureFailed");
        public static string SettingsOpacity => Get("settings.opacity");
        public static string SettingsMethod => Get("settings.method");
        public static string SettingsFillSection => Get("settings.fillSection");
        public static string SettingsMosaicSection => Get("settings.mosaicSection");
        public static string SettingsGeneratedSection => Get("settings.generatedSection");
        public static string SettingsColor => Get("settings.color");
        public static string SettingsPresets => Get("settings.presets");
        public static string SettingsBaseColor => Get("settings.baseColor");
        public static string SettingsSeed => Get("settings.seed");
        public static string SettingsReseed => Get("settings.reseed");
        public static string SettingsPickColor => Get("settings.pickColor");
        public static string FallbackTitle => Get("fallback.title");

        /// <summary>"Hider — エリア N 個" (FR-07).</summary>
        public static string TrayTooltip(int _count)
        {
            return string.Format(CultureInfo.InvariantCulture, trayTooltip, _count);
        }

        /// <summary>The size shown on the edit toolbar, in physical pixels (FR-34).</summary>
        public static string ToolbarSize(int _width, int _height)
        {
            return string.Format(CultureInfo.InvariantCulture, toolbarSize, _width, _height);
        }

        /// <summary>Accessible name of the color button, with the current color.</summary>
        public static string SettingsPickColorWithValue(string _color)
        {
            return string.Format(CultureInfo.InvariantCulture, pickColorWithValue, _color);
        }

        /// <summary>"N 個のエリアをすべて削除します。" (FR-05).</summary>
        public static string ConfirmDeleteAll(int _count)
        {
            return string.Format(CultureInfo.InvariantCulture, confirmDeleteAll, _count);
        }
    }
}
