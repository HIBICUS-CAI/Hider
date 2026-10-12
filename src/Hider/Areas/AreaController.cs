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
        public const double ToolbarHeightDip = EditToolbar.HeightDip;
        public const double ToolbarGapDip = 8;

        /// <summary>The toolbar grows to show "取得できません" (FR-52); the window must be wide enough for it.</summary>
        public double ToolbarWidthDip => CaptureFailed ? EditToolbar.WidthWithWarning : EditToolbar.NormalWidth;

        private readonly IAreaOwner owner;
        private readonly RefreshScheduler scheduler;
        private CancellationTokenSource? captureCancel;
        private ITimer? nudgeTimer;
        private RefreshReason? heldForSettings;
        private RefreshReason? retakeAfterCancel;
        private RefreshReason runningReason;
        private bool cancelledToRetake;
        private bool stripsStarted;
        private bool cancelAtFirstStrip;
        private bool disposed;
        private ResizeHandle dragHandle;
        private PixelPoint dragStartPointer;
        private PhysicalRect dragStartRect;

        public AreaController(Area _model, IAreaOwner _owner, Func<AreaWindow, IAreaWindowHost> _hostFactory)
        {
            Model = _model;
            owner = _owner;
            scheduler = new RefreshScheduler(_owner.Services.Time);
            scheduler.Due += _reason => owner.Services.UiPost(() => StartCapture(_reason));
            Window = new AreaWindow();
            Window.Attach(this);
            Host = _hostFactory(Window);
            Window.RefreshRequested += Refresh;
            Window.StripStarting += OnStripStarting;
            // A DPI change (another monitor's scale, a changed scale setting) or a monitor change resizes the window by the
            // system's rules; placing it again restores the area's physical rectangle.
            Host.DisplayChanged += () =>
            {
                if (!disposed)
                {
                    Layout();
                }
            };
            Window.SettingsOpenChanged += _open => owner.SettingsOpenChanged(this, _open);
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
            RetakeRunningStrips();
            Layout();
            // Editing takes the keyboard (arrows, Delete, Esc: FR-33): the window is activated now. Showing an area does not
            // take the focus (FR-11, ShowActivated is false); entering edit mode is the user's request to work on it.
            if (Window.IsVisible)
            {
                Window.Activate();
            }
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
            RetakeRunningStrips();
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

        /// <summary>
        /// Arrow keys: 1 px, Shift 10 px (FR-33). Saved 500 ms after the last key press (FR-61); the move is committed then,
        /// and the new place is captured (FR-50), as at the end of a drag.
        /// </summary>
        public void Nudge(int _dx, int _dy, bool _shift)
        {
            if (Mode != AreaMode.Editing)
            {
                return;
            }
            Model.Rect = AreaGeometry.Nudge(Model.Rect, _dx, _dy, _shift);
            Layout();
            owner.SaveSoon();
            nudgeTimer?.Dispose();
            nudgeTimer = owner.Services.Time.CreateTimer(_ => owner.Services.UiPost(() => RequestCapture(RefreshReason.Committed)), null,
                AreaManager.SaveDebounce, Timeout.InfiniteTimeSpan);
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
            // The user chose: back to mosaic tries capturing again, fill and pattern have nothing to do with capturing.
            CaptureFailed = false;
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
                // The window's scale is decided by the window with its margins, and the margins (DIP) by the scale. Starting
                // from the area's own scale, the margins are worked out again only while the window would get a larger scale:
                // a window at a smaller scale than its margins were made for just has room to spare (its content is laid out
                // from the placed pixels, Host.Scale), while a larger one would cut the toolbar off. The scale only grows, so
                // this ends, and an area across a 100% / 200% border no longer flips between the two.
                double scale = Host.ScaleFor(area);
                WindowRect = EditRect(area, scale);
                for (int i = 0; i < 8; i++)
                {
                    double windowScale = Host.ScaleFor(WindowRect);
                    if (windowScale <= scale)
                    {
                        break;
                    }
                    scale = windowScale;
                    WindowRect = EditRect(area, scale);
                }
            }
            Host.Place(WindowRect);
            Window.ApplyLayout();
        }

        /// <summary>The window rectangle in edit mode: margins, handles and the toolbar around the area at a scale.</summary>
        private PhysicalRect EditRect(PhysicalRect _area, double _scale)
        {
            int side = DipToPx(SideMarginDip, _scale);
            int toolbarBand = DipToPx(ToolbarHeightDip + ToolbarGapDip, _scale);
            int toolbarWidth = DipToPx(ToolbarWidthDip, _scale);
            var screen = owner.Screens.ScreenAt(_area.X, _area.Y);
            int screenTop = screen?.Bounds.Y ?? int.MinValue;
            ToolbarInside = _area.Y - toolbarBand < screenTop;
            int top = ToolbarInside ? side : toolbarBand;
            return new PhysicalRect(_area.X - side, _area.Y - top, Math.Max(_area.Width + 2 * side, side + toolbarWidth), _area.Height + top + side);
        }

        private static int DipToPx(double _dip, double _scale)
        {
            return (int)Math.Ceiling(_dip * _scale - 1e-9);
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
            if (owner.AnySettingsOpen && NeedsStrips)
            {
                // Linux: a settings popup is a top-level window of its own, outside the strip transparency, and may lie over
                // this area (its own popup or another area's): a screenshot now would put the panel into the mosaic. Capture
                // once every popup is closed.
                heldForSettings = _reason;
                scheduler.EndRun();
                return;
            }
            runningReason = _reason;
            stripsStarted = false;
            cancelAtFirstStrip = false;
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
            task.ContinueWith(_t =>
            {
                var result = _t.IsCompletedSuccessfully ? _t.Result : CaptureResult.Failed(_t.IsCanceled ? CaptureFailure.Timeout : CaptureFailure.Unavailable);
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

        private bool NeedsStrips => owner.Services.Capture.Traits.HasFlag(CaptureTraits.NeedsStripCooperation);

        /// <summary>
        /// A settings popup opened (UI thread): a strip capture running now would put it into its remaining strips, so it is
        /// cancelled and taken again once the popups are closed (<see cref="ResumeAfterSettings"/>). Before its first strip
        /// the capture may be showing a portal permission dialog the user is answering: cancelling would close it, so the
        /// cancel waits for the first strip (and is dropped if the popups close before that).
        /// </summary>
        internal void HoldForSettings()
        {
            if (captureCancel is not { IsCancellationRequested: false } || !NeedsStrips)
            {
                return;
            }
            heldForSettings ??= runningReason;
            if (stripsStarted)
            {
                CancelRunningStrips();
            }
            else
            {
                cancelAtFirstStrip = true;
            }
        }

        /// <summary>
        /// The window is laid out anew (edit mode entered or left) while a strip capture may run: strips already taken were
        /// seen through the old layout, so the capture is cancelled and taken again once it has ended. Before the first strip
        /// nothing was taken yet and the clip follows the new layout (AreaWindow.ApplyLayout): the capture goes on, and a
        /// permission dialog it may be showing stays open.
        /// </summary>
        private void RetakeRunningStrips()
        {
            if (stripsStarted && CancelRunningStrips())
            {
                retakeAfterCancel ??= runningReason;
            }
        }

        private void OnStripStarting(CancellationToken _capture)
        {
            if (captureCancel is not { } running || _capture != running.Token)
            {
                return; // another area's capture covering this window (this one may still wait for its turn)
            }
            stripsStarted = true;
            if (cancelAtFirstStrip)
            {
                cancelAtFirstStrip = false;
                CancelRunningStrips();
            }
        }

        /// <summary>Cancels a running strip capture without it counting as a failure (no FR-52 fallback).</summary>
        private bool CancelRunningStrips()
        {
            if (captureCancel is { } running && NeedsStrips && !running.IsCancellationRequested)
            {
                cancelledToRetake = true;
                running.Cancel();
                return true;
            }
            return false;
        }

        /// <summary>Every settings popup is closed (UI thread): a capture held meanwhile runs now.</summary>
        internal void ResumeAfterSettings()
        {
            if (cancelAtFirstStrip)
            {
                // The popups closed before the capture's first strip: nothing has to be cancelled or taken again.
                cancelAtFirstStrip = false;
                heldForSettings = null;
                return;
            }
            if (heldForSettings is { } reason && captureCancel is null)
            {
                heldForSettings = null;
                RequestCapture(reason);
            }
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
                    // Finished before a cancel came: the frame is good and is shown; nothing needs taking again.
                    if (cancelledToRetake)
                    {
                        heldForSettings = null;
                        retakeAfterCancel = null;
                    }
                    Window.PatternView.InvalidateVisual();
                    return;
                }
                if (cancelledToRetake && _cancel.IsCancellationRequested)
                {
                    // Not a failure (no FR-52 fallback): taken again after the layout change or when the popups are closed.
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
                    cancelledToRetake = false;
                    if (cancelAtFirstStrip)
                    {
                        // Ended before any strip (a refusal, no grabber ready): the popups were never in it.
                        cancelAtFirstStrip = false;
                        heldForSettings = null;
                    }
                }
                _cancel.Dispose();
                scheduler.EndRun();
                if (retakeAfterCancel is { } retake && captureCancel is null)
                {
                    retakeAfterCancel = null;
                    RequestCapture(retake); // held instead if a settings popup is open (StartCapture)
                }
                if (!owner.AnySettingsOpen)
                {
                    ResumeAfterSettings(); // the popups closed while the cancelled capture was finishing
                }
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
            nudgeTimer?.Dispose();
            captureCancel?.Cancel();
            scheduler.Dispose();
            Host.Dispose();
            Window.CloseFromController();
            Frames.Release();
        }
    }
}
