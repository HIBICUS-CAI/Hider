using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Hider.Core.Model;
using Hider.Platform;

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
        private bool closingAllowed;

        public AreaWindow()
        {
            InitializeComponent();
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
            AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);
            AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
            AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble);
            AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);

            chrome.Toolbar.PatternSelected += p => controller.SetPattern(p);
            chrome.Toolbar.DoneClicked += () => controller.ExitEdit();
            chrome.Toolbar.DeleteClicked += () => controller.Delete();
            chrome.Toolbar.RefreshClicked += () => RefreshRequested?.Invoke();
            chrome.Toolbar.SettingsClicked += () => SettingsRequested?.Invoke();
            Closing += (_, e) => e.Cancel = !closingAllowed;
        }

        /// <summary>Connects the window to its controller (called once, right after construction).</summary>
        public void Attach(AreaController _controller)
        {
            controller = _controller;
            Pattern.Model = _controller.Model;
        }

        /// <summary>↻ pressed (wired to the refresh scheduler from Step 5).</summary>
        public event Action? RefreshRequested;

        /// <summary>⚙ pressed (settings popup from Step 5).</summary>
        public event Action? SettingsRequested;

        public EditChrome Chrome => chrome;
        public EditToolbar Toolbar => chrome.Toolbar;
        public PatternView PatternView => Pattern;

        /// <summary>True when the edit chrome is part of the visual tree (GT-02).</summary>
        public bool ChromeAttached => Root.Children.Contains(chrome);

        /// <summary>Set by the mosaic presenter when the underlay cannot be captured (FR-52).</summary>
        public bool CaptureFailed { get; set; }

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
            chrome.Toolbar.Update(model.Pattern, model.Rect, CaptureFailed);
        }

        public void CloseFromController()
        {
            closingAllowed = true;
            Close();
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
            if (controller.Mode != AreaMode.Editing || _e.Handled)
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

        private ResizeHandle? FindHandle(Control? _source)
        {
            for (var c = _source; c is not null && c != this; c = c.Parent as Control)
            {
                if (c.Tag is ResizeHandle handle && chrome.Handles.Any(h => h.Control == c))
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
