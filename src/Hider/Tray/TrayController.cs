using Avalonia;
using Avalonia.Controls;
using Hider.Areas;
using Hider.Core;
using Hider.Dialogs;
using Hider.Platform;

namespace Hider.Tray
{
    /// <summary>
    /// The tray icon and its menu (FR-01, FR-03 .. FR-07). Right click (and both clicks on GNOME's AppIndicator) opens the
    /// native menu; a left click on Windows opens <see cref="TrayPopupWindow"/> with the same items. On Linux without a tray
    /// the same items live in <see cref="TrayFallbackWindow"/> (D-9).
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        private readonly AreaManager areas;
        private readonly Action exit;
        private readonly TrayIcon icon;
        private readonly NativeMenu menu = new();
        private TrayPopupWindow? popup;
        private ConfirmDeleteWindow? confirm;
        private TrayFallbackWindow? fallback;
        private bool disposed;

        /// <param name="_trayAvailable">Linux only: whether a tray exists (null = never show the D-9 window).</param>
        public TrayController(Application _application, AreaManager _areas, Action _exit, Func<Task<bool>>? _trayAvailable = null)
        {
            areas = _areas;
            exit = _exit;
            icon = new TrayIcon { Icon = AppIcons.CreateWindowIcon(), Menu = menu, IsVisible = true };
            icon.Clicked += (_, _) => OnClicked();
            TrayIcon.SetIcons(_application, new TrayIcons { icon });
            areas.Changed += Update;
            Update();
            if (_trayAvailable is not null)
            {
                _ = ShowFallbackUnlessTrayAsync(_trayAvailable);
            }
        }

        /// <summary>The D-9 window while it is shown.</summary>
        public TrayFallbackWindow? FallbackWindow => fallback;

        private async Task ShowFallbackUnlessTrayAsync(Func<Task<bool>> _trayAvailable)
        {
            bool tray = await _trayAvailable();
            Log.Verbose(tray ? "tray: StatusNotifierWatcher found" : "tray: no StatusNotifierWatcher, showing the menu window (D-9)");
            if (tray || disposed)
            {
                return;
            }
            fallback = new TrayFallbackWindow();
            fallback.CommandSelected += c => _ = ExecuteAsync(c);
            Update();
            fallback.Show();
        }

        public IReadOnlyList<TrayMenuItem> Items => TrayMenuModel.Build(areas.Areas.Count, areas.AnyEditing);

        public string ToolTip => Strings.TrayTooltip(areas.Areas.Count);

        public NativeMenu Menu => menu;

        /// <summary>Rebuilds the menu labels / enabled states and the tooltip (FR-03, FR-04, FR-07).</summary>
        public void Update()
        {
            menu.Items.Clear();
            foreach (var item in Items)
            {
                if (item.SeparatorBefore)
                {
                    menu.Items.Add(new NativeMenuItemSeparator());
                }
                var native = new NativeMenuItem(item.Label) { IsEnabled = item.IsEnabled };
                var command = item.Command;
                native.Click += (_, _) => _ = ExecuteAsync(command);
                menu.Items.Add(native);
            }
            icon.ToolTipText = ToolTip;
            fallback?.SetItems(Items, ToolTip);
        }

        public async Task ExecuteAsync(TrayCommand _command)
        {
            switch (_command)
            {
                case TrayCommand.AddArea:
                    areas.AddNew();
                    break;
                case TrayCommand.EditAll:
                    areas.EditAll();
                    break;
                case TrayCommand.EndEdit:
                    areas.EndEditAll();
                    break;
                case TrayCommand.DeleteAll:
                    if (await ConfirmDeleteAllAsync())
                    {
                        areas.DeleteAll();
                    }
                    break;
                case TrayCommand.Exit:
                    exit();
                    break;
            }
        }

        /// <summary>Asks before deleting every area (FR-05). Exposed for tests through <see cref="ConfirmWindow"/>.</summary>
        public Task<bool> ConfirmDeleteAllAsync()
        {
            confirm?.Close();
            confirm = new ConfirmDeleteWindow();
            confirm.SetCount(areas.Areas.Count);
            return confirm.AskAsync();
        }

        public ConfirmDeleteWindow? ConfirmWindow => confirm;

        public TrayPopupWindow? PopupWindow => popup;

        /// <summary>
        /// Opens the custom popup with the native menu's items (Windows left click). With a cursor position it opens above-left
        /// of the cursor, like the native menu near the taskbar, kept inside the working area of the cursor's monitor.
        /// </summary>
        public void ShowPopup(PixelPoint? _cursor = null)
        {
            popup?.Close();
            var window = new TrayPopupWindow();
            popup = window;
            window.SetItems(Items);
            window.CommandSelected += c => _ = ExecuteAsync(c);
            if (_cursor is { } cursor)
            {
                // The window's size is in DIP: convert it with the cursor monitor's scale before placing it in physical px.
                window.Measure(Size.Infinity);
                var screen = window.Screens.ScreenFromPoint(cursor) ?? window.Screens.Primary;
                window.Position = screen is null
                    ? cursor
                    : PlaceAboveLeft(cursor, PixelSize.FromSize(window.DesiredSize, screen.Scaling), screen.WorkingArea);
            }
            window.Show();
            window.Activate();
        }

        /// <summary>Top-left of a window of <paramref name="_size"/> above-left of the cursor, clamped into the working area.</summary>
        public static PixelPoint PlaceAboveLeft(PixelPoint _cursor, PixelSize _size, PixelRect _workingArea)
        {
            int x = Math.Clamp(_cursor.X - _size.Width, _workingArea.X, Math.Max(_workingArea.X, _workingArea.Right - _size.Width));
            int y = Math.Clamp(_cursor.Y - _size.Height, _workingArea.Y, Math.Max(_workingArea.Y, _workingArea.Bottom - _size.Height));
            return new PixelPoint(x, y);
        }

        private void OnClicked()
        {
            // Linux AppIndicator opens the native menu for both buttons; only Windows needs the custom popup.
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            ShowPopup(Platform.Windows.Win32Cursor.TryGetPosition(out int x, out int y) ? new PixelPoint(x, y) : null);
        }

        public void Dispose()
        {
            disposed = true;
            areas.Changed -= Update;
            fallback?.CloseForExit();
            popup?.Close();
            confirm?.Close();
            icon.Dispose();
        }
    }
}
