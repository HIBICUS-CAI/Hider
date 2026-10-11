using Avalonia;
using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Scheduling;
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
        public const double ToolbarGapDip = 8;

        /// <summary>The toolbar grows to show "取得できません" (FR-52); the window must be wide enough for it.</summary>
        public double ToolbarWidthDip => CaptureFailed ? EditToolbar.WidthWithWarning : EditToolbar.NormalWidth;

        private readonly IAreaOwner owner;
        private readonly RefreshScheduler scheduler;
        private CancellationTokenSource? captureCancel;
        private RefreshReason? heldForSettings;
        private bool disposed;
        private ResizeHandle dragHandle;
        private PixelPoint dragStartPointer;
        private PhysicalRect dragStartRect;

        public AreaController(Area _model, IAreaOwner _owner, Func<AreaWindow, IAreaWindowHost> _hostFactory)
        {
            Model = _model;
            owner = _owner;
            scheduler = new RefreshScheduler(_owner.Services.Time);
            scheduler.Due += reason => owner.Services.UiPost(() => StartCapture(reason));
            Window = new AreaWindow();
            Window.Attach(this);
            Host = _hostFactory(Window);
            Window.RefreshRequested += Refresh;
            Window.SettingsClosed += () =>
            {
                if (heldForSettings is { } reason)
                {
                    heldForSettings = null;
                    RequestCapture(reason);
                }
            };
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

        /// <summary>Latest captured underlay (double buffer shared with the presenter, work instructions §3.5).</summary>
        public FrameExchange Frames { get; } = new();

        /// <summary>The last capture failed and the area fell back to a generated pattern (FR-52).</summary>
        public bool CaptureFailed { get; private set; }

        public void Show()
        {
            Layout();
            Window.Show();
            Log.Verbose($"area {Model.Id}: shown at {Model.Rect}");
            ConfigureRefresh();
            RequestCapture(RefreshReason.Created);
        }

        /// <summary>↻ in the toolbar: capture again now (FR-50).</summary>
        public void Refresh()
        {
            RequestCapture(RefreshReason.Manual);
        }

        /// <summary>
        /// A setting changed (FR-36): redraw from the current capture without capturing again; a new refresh interval or strip
        /// height applies from the next timer tick (D-10).
        /// </summary>
        public void SettingsChanged()
        {
            ConfigureRefresh();
            Window.RefreshPattern();
        }

        /// <summary>Saves a settings change (FR-36): now for a typed or picked value, after 500 ms while a slider moves.</summary>
        public void SaveSettings(bool _now)
        {
            if (_now)
            {
                owner.SaveNow();
            }
            else
            {
                owner.SaveSoon();
            }
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
            // FR-38: a live capture (Windows) keeps following the content during the drag; others capture when it ends.
            scheduler.TimerPaused = !owner.Services.Capture.Traits.HasFlag(CaptureTraits.Live);
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
            scheduler.TimerPaused = false;
            Transition?.Invoke("end-drag");
            owner.SaveNow();
            RequestCapture(RefreshReason.Committed);
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
            if (_pattern == PatternKind.Mosaic)
            {
                CaptureFailed = false;
            }
            ConfigureRefresh();
            Layout();
            owner.SaveNow();
            RequestCapture(RefreshReason.Manual);
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

        private void ConfigureRefresh()
        {
            scheduler.Configure(Model.Pattern == PatternKind.Mosaic ? Model.Mosaic.Common.IntervalMs : 0);
        }

        private void RequestCapture(RefreshReason _reason)
        {
            if (Model.Pattern == PatternKind.Mosaic && !disposed)
            {
                scheduler.Request(_reason);
            }
        }

        /// <summary>Starts one capture off the UI thread; the scheduler merges requests that come in meanwhile.</summary>
        private void StartCapture(RefreshReason _reason)
        {
            if (disposed || Model.Pattern != PatternKind.Mosaic)
            {
                scheduler.EndRun(); // the run began when Due was raised
                return;
            }
            if (Window.Settings is not null && owner.Services.Capture.Traits.HasFlag(CaptureTraits.NeedsStripCooperation))
            {
                // Linux: the settings popup is a top-level window of its own, outside the strip transparency, and may lie over
                // the area: a screenshot now would put the panel into the mosaic. Capture once it closes.
                heldForSettings = _reason;
                scheduler.EndRun();
                return;
            }
            owner.CaptureStarting();
            // ExcludeWindows[0] is this area's own window: the capture only looks at what lies below it.
            var others = owner.OwnWindows;
            var exclude = new List<nint>(others.Count + 1) { Host.Handle };
            foreach (var handle in others)
            {
                if (handle != Host.Handle)
                {
                    exclude.Add(handle);
                }
            }
            var request = new CaptureRequest(Model.Rect, exclude, Model.Mosaic.Common.StripPx);
            var cancel = new CancellationTokenSource();
            captureCancel = cancel;
            var capture = owner.Services.Capture;
            Task<CaptureResult> task;
            try
            {
                task = owner.Services.RunCapture(() => capture.CaptureAsync(request, cancel.Token));
            }
            catch (Exception e)
            {
                task = Task.FromException<CaptureResult>(e);
            }
            // Runs right where the capture finished (a capture thread in the app): the frame is copied into the back buffer
            // there, and only the UI work is posted.
            task.ContinueWith(t =>
            {
                var result = t.IsCompletedSuccessfully ? t.Result : CaptureResult.Failed(t.IsCanceled ? CaptureFailure.Timeout : CaptureFailure.Unavailable);
                bool ok = false;
                try
                {
                    ok = result.Failure == CaptureFailure.None && result.Frame is not null && !cancel.IsCancellationRequested;
                    if (ok)
                    {
                        CopyToBack(result.Frame!);
                        Frames.Publish();
                    }
                }
                catch (Exception e)
                {
                    Log.Verbose($"area {Model.Id}: taking the frame failed: {e.GetType().Name}: {e.Message}");
                    ok = false;
                    result = CaptureResult.Failed(CaptureFailure.Unavailable);
                }
                finally
                {
                    // Always reaches FinishCapture: it ends the scheduler's run, without which this area never refreshes again.
                    owner.Services.UiPost(() => FinishCapture(_reason, cancel, result, ok));
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void FinishCapture(RefreshReason _reason, CancellationTokenSource _cancel, CaptureResult _result, bool _ok)
        {
            try
            {
                if (disposed)
                {
                    return;
                }
                if (_ok)
                {
                    Window.PatternView.InvalidateVisual();
                    return;
                }
                Log.Verbose($"area {Model.Id}: capture failed ({_result.Failure}) on {_reason}, switching to a generated pattern");
                if (Model.Pattern == PatternKind.Mosaic)
                {
                    // FR-52: cover with a generated pattern; the mosaic settings stay as they are.
                    CaptureFailed = true;
                    Frames.Clear();
                    Model.Pattern = PatternKind.Generated;
                    Transition?.Invoke($"capture-failed:{_result.Failure}");
                    ConfigureRefresh();
                    Layout();
                    owner.SaveNow();
                }
            }
            finally
            {
                // Both run on the UI thread, so Dispose never cancels a disposed source.
                if (captureCancel == _cancel)
                {
                    captureCancel = null;
                }
                _cancel.Dispose();
                scheduler.EndRun();
            }
        }

        private void CopyToBack(FrameBuffer _frame)
        {
            var back = Frames.Back;
            back.Resize(_frame.Width, _frame.Height);
            var src = _frame.Bgra.Span;
            var dst = back.Bgra.Span;
            int row = _frame.Width * 4;
            for (int y = 0; y < _frame.Height; y++)
            {
                src.Slice(y * _frame.Stride, row).CopyTo(dst.Slice(y * back.Stride, row));
            }
        }

        public void Dispose()
        {
            disposed = true;
            captureCancel?.Cancel();
            scheduler.Dispose();
            Window.CloseFromController();
        }
    }
}
