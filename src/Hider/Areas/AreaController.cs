using Avalonia;
using Hider.Core;
using Hider.Core.Model;
using Hider.Platform;

namespace Hider.Areas
{
    /// <summary>
    /// One area: its model, its window and the hidden / editing state machine (design §6.2). Geometry is physical pixels; the
    /// window is placed through <see cref="IAreaWindowHost"/> only.
    /// </summary>
    public sealed class AreaController : IDisposable
    {
        /// <summary>Space around the area in edit mode for the frame and the handles (DIP).</summary>
        public const double SideMarginDip = 8;

        /// <summary>Edit toolbar size (design canvas 3) and its gap to the area (D-8: toolbar height + 8).</summary>
        public const double ToolbarHeightDip = 36;
        public const double ToolbarWidthDip = 436;
        public const double ToolbarGapDip = 8;

        private readonly IAreaOwner owner;
        private ResizeHandle dragHandle;
        private PixelPoint dragStartPointer;
        private PhysicalRect dragStartRect;

        public AreaController(Area _model, IAreaOwner _owner, Func<AreaWindow, IAreaWindowHost> _hostFactory)
        {
            Model = _model;
            owner = _owner;
            Window = new AreaWindow();
            Window.Attach(this);
            Host = _hostFactory(Window);
        }

        public Area Model { get; }
        public AreaWindow Window { get; }
        public IAreaWindowHost Host { get; }
        public AreaMode Mode { get; private set; } = AreaMode.Hidden;
        public bool IsDragging { get; private set; }

        /// <summary>In edit mode, true when the toolbar sits inside the area because there is no room above it (D-8).</summary>
        public bool ToolbarInside { get; private set; }

        /// <summary>Window rectangle: the area itself when hidden, the area plus margins when editing (D-8).</summary>
        public PhysicalRect WindowRect { get; private set; }

        /// <summary>State transitions, for checking that hosts do not change behaviour (GT-38).</summary>
        public event Action<string>? Transition;

        public void Show()
        {
            Layout();
            Window.Show();
            Log.Verbose($"area {Model.Id}: shown at {Model.Rect}");
        }

        public void EnterEdit()
        {
            if (Mode == AreaMode.Editing)
            {
                return;
            }
            Mode = AreaMode.Editing;
            Transition?.Invoke("enter-edit");
            Layout();
            owner.ModeChanged(this);
        }

        /// <summary>Leaves edit mode (✓, Esc, tray "end editing") and saves (FR-31).</summary>
        public void ExitEdit()
        {
            if (Mode == AreaMode.Hidden)
            {
                return;
            }
            if (IsDragging)
            {
                EndDrag();
            }
            Mode = AreaMode.Hidden;
            Transition?.Invoke("exit-edit");
            Layout();
            owner.ModeChanged(this);
            owner.SaveNow();
        }

        /// <summary>Starts moving (<see cref="ResizeHandle.None"/>) or resizing from a pointer position in physical pixels.</summary>
        public void BeginDrag(ResizeHandle _handle, PixelPoint _screenPointer)
        {
            if (Mode != AreaMode.Editing)
            {
                return;
            }
            IsDragging = true;
            dragHandle = _handle;
            dragStartPointer = _screenPointer;
            dragStartRect = Model.Rect;
            Transition?.Invoke(_handle == ResizeHandle.None ? "begin-move" : $"begin-resize:{_handle}");
        }

        public void DragTo(PixelPoint _screenPointer)
        {
            if (!IsDragging)
            {
                return;
            }
            int dx = _screenPointer.X - dragStartPointer.X;
            int dy = _screenPointer.Y - dragStartPointer.Y;
            var rect = dragHandle == ResizeHandle.None
                ? dragStartRect.Offset(dx, dy)
                : AreaGeometry.Resize(dragStartRect, dragHandle, dx, dy, Host.Scale);
            if (rect != Model.Rect)
            {
                Model.Rect = rect;
                Layout();
            }
        }

        /// <summary>Ends a drag and saves (FR-61: move / resize are saved when the drag ends).</summary>
        public void EndDrag()
        {
            if (!IsDragging)
            {
                return;
            }
            IsDragging = false;
            Transition?.Invoke("end-drag");
            owner.SaveNow();
        }

        /// <summary>Arrow keys: 1 px, Shift 10 px (FR-33). Saved 500 ms after the last key press (FR-61).</summary>
        public void Nudge(int _dx, int _dy, bool _shift)
        {
            if (Mode != AreaMode.Editing)
            {
                return;
            }
            Model.Rect = AreaGeometry.Nudge(Model.Rect, _dx, _dy, _shift);
            Layout();
            owner.SaveSoon();
        }

        /// <summary>Switches the pattern; the other kinds keep their settings (FR-35). Saved immediately (FR-61).</summary>
        public void SetPattern(PatternKind _pattern)
        {
            if (Model.Pattern == _pattern)
            {
                return;
            }
            Model.Pattern = _pattern;
            Transition?.Invoke($"pattern:{_pattern}");
            Window.RefreshPattern();
            owner.SaveNow();
        }

        /// <summary>Delete key or 🗑: removes this area without confirmation (FR-05, FR-33).</summary>
        public void Delete()
        {
            Transition?.Invoke("delete");
            owner.Delete(this);
        }

        /// <summary>Recomputes the window rectangle for the current mode and places the window.</summary>
        public void Layout()
        {
            var area = Model.Rect;
            if (Mode == AreaMode.Hidden)
            {
                ToolbarInside = false;
                WindowRect = area;
            }
            else
            {
                int side = Host.DipToPx(SideMarginDip);
                int toolbarBand = Host.DipToPx(ToolbarHeightDip + ToolbarGapDip);
                int toolbarWidth = Host.DipToPx(ToolbarWidthDip);
                var screen = owner.Screens.ScreenAt(area.X, area.Y);
                int screenTop = screen?.Bounds.Y ?? int.MinValue;
                ToolbarInside = area.Y - toolbarBand < screenTop;
                int top = ToolbarInside ? side : toolbarBand;
                WindowRect = new PhysicalRect(area.X - side, area.Y - top, Math.Max(area.Width + 2 * side, side + toolbarWidth), area.Height + top + side);
            }
            Host.Place(WindowRect);
            Window.ApplyLayout();
        }

        /// <summary>Area origin inside the window, in physical pixels (0 when hidden).</summary>
        public PixelPoint AreaOffsetInWindow => new(Model.Rect.X - WindowRect.X, Model.Rect.Y - WindowRect.Y);

        public void Dispose()
        {
            Window.CloseFromController();
        }
    }
}
