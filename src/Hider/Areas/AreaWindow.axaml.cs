using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Hider.Core.Model;
using Hider.Platform;
using Hider.Settings;

namespace Hider.Areas
{
    /// <summary>
    /// The window of one area (1 area = 1 window, spec §3). Hidden mode shows only the pattern and swallows clicks and the
    /// wheel; a double click enters edit mode (FR-20 .. FR-22). Edit mode adds the frame, handles and toolbar around the area
    /// inside the same, enlarged window (D-8). Moving and resizing are implemented here with pointer capture, never with
    /// BeginMoveDrag / BeginResizeDrag (work instructions §2.2).
    /// </summary>
    public partial class AreaWindow : Window
    {
        private AreaController controller = null!;
        private readonly EditChrome chrome = new();
        private readonly Popup settingsPopup = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            VerticalOffset = 4,
        };
        private SettingsPanel? settings;
        private bool closingAllowed;

        public AreaWindow()
        {
            InitializeComponent();
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
            AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);
            AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
            AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble);
            AddHandler(PointerWheelChangedEvent, (_, _e) => _e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);

            chrome.Toolbar.PatternSelected += _p => controller.SetPattern(_p);
            chrome.Toolbar.DoneClicked += () => controller.ExitEdit();
            chrome.Toolbar.DeleteClicked += () => controller.Delete();
            chrome.Toolbar.RefreshClicked += () => RefreshRequested?.Invoke();
            chrome.Toolbar.SettingsClicked += ToggleSettings;
            settingsPopup.PlacementTarget = chrome.Toolbar.SettingsButton;
            Root.Children.Add(settingsPopup);
            // IsOpen rather than Closed: Closed only comes when the popup window really opened.
            settingsPopup.PropertyChanged += (_, _e) =>
            {
                if (_e.Property == Popup.IsOpenProperty)
                {
                    PopupOpenChanged?.Invoke(settingsPopup.IsOpen);
                }
            };
            // The pattern menu is a top-level window too (Linux: outside the strip transparency, like the settings popup).
            var patternMenu = chrome.Toolbar.PatternMenu;
            patternMenu.Opened += (_, _) => PopupOpenChanged?.Invoke(true);
            patternMenu.Closed += (_, _) => PopupOpenChanged?.Invoke(false);
            Closing += (_, _e) => _e.Cancel = KeepOpen(closingAllowed, _e.CloseReason);
        }

        /// <summary>
        /// Whether a close is refused: only the user's or another window's attempt is (Alt+F4 must not lose an area). A sign-out
        /// or shutdown must never be held up — cancelling it would make Windows report Hider as blocking the shutdown.
        /// </summary>
        public static bool KeepOpen(bool _closingAllowed, WindowCloseReason _reason)
        {
            return !_closingAllowed && _reason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown);
        }

        /// <summary>Connects the window to its controller (called once, right after construction).</summary>
        public void Attach(AreaController _controller)
        {
            controller = _controller;
            Pattern.Model = _controller.Model;
            Pattern.Frames = _controller.Frames;
        }

        /// <summary>↻ pressed.</summary>
        public event Action? RefreshRequested;

        /// <summary>
        /// The settings popup or the pattern menu opened (true) or closed (light dismiss, ⚙ again, a choice, or leaving edit
        /// mode).
        /// </summary>
        public event Action<bool>? PopupOpenChanged;

        /// <summary>Whether this window's settings popup or pattern menu is open.</summary>
        public bool PopupOpen => settingsPopup.IsOpen || chrome.Toolbar.PatternMenu.IsOpen;

        /// <summary>The settings popup content while it is open (FR-35 .. FR-37).</summary>
        public SettingsPanel? Settings => settingsPopup.IsOpen ? settings : null;

        /// <summary>Opens or closes the settings popup under ⚙.</summary>
        public void ToggleSettings()
        {
            if (settingsPopup.IsOpen)
            {
                settingsPopup.IsOpen = false;
                return;
            }
            settings = new SettingsPanel(controller);
            settingsPopup.Child = settings;
            settingsPopup.IsOpen = true;
        }

        public EditChrome Chrome => chrome;
        public EditToolbar Toolbar => chrome.Toolbar;
        public MosaicPresenter PatternView => Pattern;

        /// <summary>True when the edit chrome is part of the visual tree (GT-02).</summary>
        public bool ChromeAttached => Root.Children.Contains(chrome);

        /// <summary>Lays the content out for the controller's mode and window rectangle (all DIP).</summary>
        public void ApplyLayout()
        {
            var host = controller.Host;
            var offset = controller.AreaOffsetInWindow;
            var area = new Rect(host.PxToDip(offset.X), host.PxToDip(offset.Y), host.PxToDip(controller.Model.Rect.Width), host.PxToDip(controller.Model.Rect.Height));
            Canvas.SetLeft(Pattern, area.X);
            Canvas.SetTop(Pattern, area.Y);
            Pattern.Width = area.Width;
            Pattern.Height = area.Height;

            bool editing = controller.Mode == AreaMode.Editing;
            if (!editing)
            {
                settingsPopup.IsOpen = false;
                chrome.Toolbar.PatternMenu.Hide();
            }
            if (editing && !ChromeAttached)
            {
                Root.Children.Add(chrome);
            }
            else if (!editing && ChromeAttached)
            {
                Root.Children.Remove(chrome);
            }
            if (editing)
            {
                chrome.Arrange(area, controller.ToolbarInside, AreaController.ToolbarGapDip);
            }
            if (TransparentRows is not null)
            {
                ApplyClip();
            }
            RefreshPattern();
        }

        /// <summary>Applies the pattern and opacity: window alpha when hidden (GT-22); in edit mode only the pattern fades so the
        /// toolbar stays readable. Mosaic opacity is the fineness value, not alpha (spec §4.5).</summary>
        public void RefreshPattern()
        {
            var model = controller.Model;
            double alpha = model.Pattern == PatternKind.Mosaic ? 1.0 : model.EffectiveOpacity;
            bool editing = controller.Mode == AreaMode.Editing;
            Opacity = editing ? 1.0 : alpha;
            Pattern.Opacity = editing ? alpha : 1.0;
            Pattern.InvalidateVisual();
            chrome.Toolbar.Update(model.Pattern, model.Rect, controller.CaptureFailed);
            if (settings is not null && settingsPopup.IsOpen && settings.Pattern != model.Pattern)
            {
                settings.Rebuild();
            }
        }

        /// <summary>Frames to wait after the transparent rows have been rendered: one spare frame for the display server.</summary>
        public const int StripSpareFrames = 1;

        /// <summary>Upper bound for that wait, for windows that render no frames (minimised, not mapped yet).</summary>
        public static readonly TimeSpan StripFrameTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>Rows that the strip capture currently needs see-through (area-local physical px), or null.</summary>
        public (int Y0, int Y1)? TransparentRows { get; private set; }

        /// <summary>
        /// Linux strip capture (IStripHost, design §5.3): everything in the window — pattern and edit chrome alike — is clipped
        /// away in rows [y0, y1) of the area, so those window pixels have alpha 0. Completes once the change has been shown.
        /// </summary>
        public async Task SetTransparentRowsAsync(int _y0, int _y1, CancellationToken _ct)
        {
            // The grabber is prepared by now (any permission dialog answered): a cancel held back until the strips start
            // happens here, before anything turns see-through.
            StripStarting?.Invoke(_ct);
            _ct.ThrowIfCancellationRequested();
            TransparentRows = (_y0, _y1);
            ApplyClip();
            await WaitFramesAsync(_ct);
        }

        /// <summary>
        /// Clips <see cref="TransparentRows"/> away for the current layout. Also run by <see cref="ApplyLayout"/>: a mode change
        /// during a strip moves the area inside the window and resizes it, and a clip made for the old layout would show part
        /// of the area's surroundings or keep rows opaque (the controller retakes such a capture).
        /// </summary>
        private void ApplyClip()
        {
            if (TransparentRows is not { } rows)
            {
                Root.Clip = null;
                return;
            }
            var host = controller.Host;
            int offset = controller.AreaOffsetInWindow.Y;
            // One physical px more on each side: a clip edge that falls between pixels never leaves a covered row half drawn.
            double top = host.PxToDip(offset + rows.Y0 - 1), bottom = host.PxToDip(offset + rows.Y1 + 1);
            // The window's new size may not have reached Bounds yet (a layout pass follows): the placed rectangle counts too.
            double width = Math.Max(Math.Max(Bounds.Width, ClientSize.Width), host.PxToDip(controller.WindowRect.Width)) + 1;
            double height = Math.Max(Math.Max(Bounds.Height, ClientSize.Height), host.PxToDip(controller.WindowRect.Height)) + 1;
            Root.Clip = new GeometryGroup
            {
                Children =
                {
                    new RectangleGeometry(new Rect(-1, -1, width + 2, Math.Max(0, top + 1))),
                    new RectangleGeometry(new Rect(-1, bottom, width + 2, Math.Max(0, height - bottom + 1))),
                },
            };
        }

        /// <summary>Shows the whole window again (does not wait for the frame).</summary>
        public Task RestoreRowsAsync()
        {
            TransparentRows = null;
            Root.Clip = null;
            return Task.CompletedTask;
        }

        /// <summary>Design §5.3: the composition batch that carries the change has been rendered, plus one spare frame.</summary>
        private async Task WaitFramesAsync(CancellationToken _ct)
        {
            // The app's clock (a fake one in tests: the timeout then comes exactly when a test says, not on a timer thread).
            using var giveUp = new CancellationTokenSource(StripFrameTimeout, controller.Time);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_ct, giveUp.Token);
            try
            {
                if (ElementComposition.GetElementVisual(this)?.Compositor is { } compositor)
                {
                    await compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(timeout.Token);
                }
                for (int i = 0; i < StripSpareFrames; i++)
                {
                    var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    RequestAnimationFrame(_ => frame.TrySetResult());
                    await frame.Task.WaitAsync(timeout.Token);
                }
            }
            catch (OperationCanceledException) when (!_ct.IsCancellationRequested)
            {
                StripFrameTimeouts++;
                Hider.Core.Log.Verbose($"area {controller.Model.Id}: no frame within {StripFrameTimeout.TotalMilliseconds} ms, capturing anyway");
            }
        }

        /// <summary>
        /// A strip is about to turn rows of this window see-through (UI thread). The token is the capture's: the strips of
        /// another area's capture also come here when the areas overlap.
        /// </summary>
        internal event Action<CancellationToken>? StripStarting;

        /// <summary>Strip changes captured after <see cref="StripFrameTimeout"/> instead of a rendered frame (tests).</summary>
        internal int StripFrameTimeouts { get; private set; }

        public void CloseFromController()
        {
            closingAllowed = true;
            Close();
            Pattern.Dispose();
        }

        private PixelPoint ScreenPoint(PointerEventArgs _e)
        {
            return this.PointToScreen(_e.GetPosition(this));
        }

        private void OnPointerPressed(object? _sender, PointerPressedEventArgs _e)
        {
            if (controller.Mode == AreaMode.Hidden)
            {
                // Clicks never reach the windows below (FR-22); a double click enters edit mode (FR-21).
                _e.Handled = true;
                if (_e.ClickCount >= 2 && _e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    controller.EnterEdit();
                }
                return;
            }
            if (_e.Handled || !_e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }
            var handle = FindHandle(_e.Source as Control);
            if (handle is null && !IsOver(Pattern, _e))
            {
                return; // transparent margin
            }
            Focus();
            controller.BeginDrag(handle ?? ResizeHandle.None, ScreenPoint(_e));
            _e.Pointer.Capture(this);
            _e.Handled = true;
        }

        private void OnPointerMoved(object? _sender, PointerEventArgs _e)
        {
            if (controller.IsDragging)
            {
                controller.DragTo(ScreenPoint(_e));
                _e.Handled = true;
            }
        }

        private void OnPointerReleased(object? _sender, PointerReleasedEventArgs _e)
        {
            if (controller.IsDragging)
            {
                controller.DragTo(ScreenPoint(_e));
                controller.EndDrag();
                _e.Pointer.Capture(null);
                _e.Handled = true;
            }
            else if (controller.Mode == AreaMode.Hidden)
            {
                _e.Handled = true;
            }
        }

        private void OnPointerCaptureLost(object? _sender, PointerCaptureLostEventArgs _e)
        {
            controller.EndDrag();
        }

        protected override void OnKeyDown(KeyEventArgs _e)
        {
            base.OnKeyDown(_e);
            if (controller.Mode != AreaMode.Editing || _e.Handled || IsInSettings(_e.Source))
            {
                return;
            }
            bool shift = _e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            switch (_e.Key)
            {
                case Key.Left: controller.Nudge(-1, 0, shift); break;
                case Key.Right: controller.Nudge(1, 0, shift); break;
                case Key.Up: controller.Nudge(0, -1, shift); break;
                case Key.Down: controller.Nudge(0, 1, shift); break;
                case Key.Delete: controller.Delete(); break;
                case Key.Escape: controller.ExitEdit(); break;
                default: return;
            }
            _e.Handled = true;
        }

        /// <summary>
        /// Whether an event comes from the settings popup. Popup events bubble on to the window that owns the popup, so
        /// without this an arrow in a number box would move the area and Delete on a slider would delete it.
        /// </summary>
        private bool IsInSettings(object? _source)
        {
            for (var e = _source as StyledElement; e is not null; e = e.Parent)
            {
                if (ReferenceEquals(e, settingsPopup))
                {
                    return true;
                }
            }
            return false;
        }

        private ResizeHandle? FindHandle(Control? _source)
        {
            for (var c = _source; c is not null && c != this; c = c.Parent as Control)
            {
                if (c.Tag is ResizeHandle handle && chrome.Handles.Any(_h => _h.Control == c))
                {
                    return handle;
                }
            }
            return null;
        }

        private static bool IsOver(Control _control, PointerEventArgs _e)
        {
            var p = _e.GetPosition(_control);
            return p.X >= 0 && p.Y >= 0 && p.X < _control.Bounds.Width && p.Y < _control.Bounds.Height;
        }
    }
}
