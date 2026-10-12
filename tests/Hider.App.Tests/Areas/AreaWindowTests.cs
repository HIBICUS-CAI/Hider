using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hider.Areas;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;

namespace Hider.App.Tests.Areas
{
    public class AreaWindowTests
    {
        private static readonly PhysicalRect areaRect = new(400, 300, 400, 240);

        private static void DoubleClick(AreaController _area, Point _p)
        {
            for (int i = 0; i < 2; i++)
            {
                _area.Window.MouseDown(_p, MouseButton.Left);
                _area.Window.MouseUp(_p, MouseButton.Left);
            }
        }

        [AvaloniaFact]
        public void GT02_HiddenMode_HasNoChrome()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            area.ExitEdit();
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.False(area.Window.ChromeAttached);
            Assert.Empty(area.Window.GetVisualDescendants().OfType<EditToolbar>());
            Assert.Equal(areaRect, f.Hosts[0].Last);
        }

        [AvaloniaFact]
        public void GT03_DoubleClick_EntersEditMode()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            area.ExitEdit();
            DoubleClick(area, AreaFixture.AreaCenter(area));
            Assert.Equal(AreaMode.Editing, area.Mode);
            Assert.True(area.Window.ChromeAttached);
        }

        [AvaloniaFact]
        public void GT04_SingleClickAndWheel_AreSwallowed()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            area.ExitEdit();
            var p = AreaFixture.AreaCenter(area);
            bool reachedBehind = false;
            area.Window.AddHandler(InputElement.PointerPressedEvent, (_, _e) => reachedBehind |= !_e.Handled, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            area.Window.MouseDown(p, MouseButton.Left);
            area.Window.MouseUp(p, MouseButton.Left);
            area.Window.MouseWheel(p, new Vector(0, -1));
            Assert.False(reachedBehind);
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal(areaRect, area.Model.Rect);
        }

        [AvaloniaFact]
        public void GT05_EditMode_HandlesToolbarAndPhysicalSize()
        {
            using var f = new AreaFixture(2.0);
            var area = f.Add(new PhysicalRect(400, 300, 800, 480));
            Assert.Equal(8, area.Window.Chrome.Handles.Count);
            Assert.All(area.Window.Chrome.Handles, _h => Assert.True(_h.Control.IsVisible));
            Assert.True(area.Window.Toolbar.IsVisible);
            Assert.Equal("800 × 480 px", area.Window.Toolbar.SizeText.Text);
            Assert.Equal(400, area.Window.PatternView.Width, 6); // DIP = physical / 2
        }

        [AvaloniaFact]
        public void GT06_BodyDrag_MovesByTheDelta()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            var p = AreaFixture.AreaCenter(area);
            var screenStart = area.Window.PointToScreen(p);
            // Like a real mouse: the target is a screen position, delivered relative to wherever the window is now.
            Point InWindow(int _dx, int _dy) => area.Window.PointToClient(screenStart + new PixelVector(_dx, _dy));
            area.Window.MouseDown(p, MouseButton.Left);
            area.Window.MouseMove(InWindow(30, 20));
            area.Window.MouseMove(InWindow(50, -10));
            area.Window.MouseUp(InWindow(50, -10), MouseButton.Left);
            Assert.Equal(areaRect.Offset(50, -10), area.Model.Rect);
            Assert.False(area.IsDragging);
            var host = f.Hosts[0];
            Assert.Equal(area.WindowRect, host.Last);
            Assert.Equal(areaRect.X + 50, host.Last.X + area.AreaOffsetInWindow.X);
        }

        [AvaloniaTheory]
        [InlineData(ResizeHandle.TopLeft, true, true, false, false)]
        [InlineData(ResizeHandle.Top, false, true, false, false)]
        [InlineData(ResizeHandle.TopRight, false, true, true, false)]
        [InlineData(ResizeHandle.Left, true, false, false, false)]
        [InlineData(ResizeHandle.Right, false, false, true, false)]
        [InlineData(ResizeHandle.BottomLeft, true, false, false, true)]
        [InlineData(ResizeHandle.Bottom, false, false, false, true)]
        [InlineData(ResizeHandle.BottomRight, false, false, true, true)]
        public void GT07_EachHandle_MovesOnlyItsEdges(ResizeHandle _kind, bool _left, bool _top, bool _right, bool _bottom)
        {
            // Through the visible handle (EditChrome's name-to-handle table included), not through AreaGeometry directly.
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            var center = HandleCenter(area, _kind);
            // The window moves with a left or top edge: pointer positions are given on the screen, as the user moves it.
            var screenStart = area.Window.PointToScreen(center);
            Point InWindow() => area.Window.PointToClient(screenStart + new PixelVector(15, 10));
            area.Window.MouseDown(center, MouseButton.Left);
            area.Window.MouseMove(InWindow());
            area.Window.MouseUp(InWindow(), MouseButton.Left);
            var r = area.Model.Rect;
            Assert.Equal(areaRect.X + (_left ? 15 : 0), r.X);
            Assert.Equal(areaRect.Y + (_top ? 10 : 0), r.Y);
            Assert.Equal(areaRect.Right + (_right ? 15 : 0), r.Right);
            Assert.Equal(areaRect.Bottom + (_bottom ? 10 : 0), r.Bottom);
        }

        private static Point HandleCenter(AreaController _area, ResizeHandle _kind)
        {
            var h = _area.Window.Chrome.Handles.Single(_x => _x.Kind == _kind).Control;
            return new Point(Canvas.GetLeft(h) + h.Width / 2, Canvas.GetTop(h) + h.Height / 2);
        }

        [AvaloniaFact]
        public void GT07_HandleDrag_MovesOneEdgeAndStopsAtMinimum()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            Point Center(ResizeHandle _kind) => HandleCenter(area, _kind);

            var right = Center(ResizeHandle.Right);
            area.Window.MouseDown(right, MouseButton.Left);
            area.Window.MouseMove(right + new Vector(40, 25));
            area.Window.MouseUp(right + new Vector(40, 25), MouseButton.Left);
            Assert.Equal(areaRect with { Width = areaRect.Width + 40 }, area.Model.Rect);

            var start = area.Model.Rect;
            var topLeft = Center(ResizeHandle.TopLeft);
            area.Window.MouseDown(topLeft, MouseButton.Left);
            area.Window.MouseMove(topLeft + new Vector(5000, 5000));
            area.Window.MouseUp(topLeft + new Vector(5000, 5000), MouseButton.Left);
            Assert.Equal(24, area.Model.Rect.Width);
            Assert.Equal(24, area.Model.Rect.Height);
            Assert.Equal(start.Right, area.Model.Rect.Right);
            Assert.Equal(start.Bottom, area.Model.Rect.Bottom);
        }

        [AvaloniaFact]
        public void GT08_ArrowKeys_OneAndTenPixels()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            area.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            area.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            Assert.Equal(areaRect.Offset(1, 10), area.Model.Rect);
            int saves = f.Manager.SaveCount;
            f.Time.Advance(TimeSpan.FromMilliseconds(499));
            Assert.Equal(saves, f.Manager.SaveCount);
            f.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(saves + 1, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void GT08_ArrowKeys_CaptureTheNewPlaceWhenCommitted()
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaFixture(_capture: capture);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Model.Mosaic.Common.IntervalMs = 0; // no timer captures in between
            area.SettingsChanged();
            int calls = capture.Calls;
            area.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            f.Time.Advance(TimeSpan.FromMilliseconds(300));
            area.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            f.Time.Advance(TimeSpan.FromMilliseconds(499));
            Assert.Equal(calls, capture.Calls);
            f.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(calls + 1, capture.Calls);
            Assert.Equal(areaRect.Offset(2, 0), capture.Requests[^1].Rect);
        }

        [AvaloniaFact]
        public void GT09_DeleteKey_RemovesWithoutConfirmation()
        {
            using var f = new AreaFixture();
            f.Add(areaRect);
            var area = f.Add(areaRect.Offset(500, 0));
            area.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Single(f.Manager.Areas);
            Assert.DoesNotContain(area, f.Manager.Areas);
        }

        [AvaloniaFact]
        public void GT10_EscapeAndDone_ReturnToHiddenAndSave()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            int saves = f.Manager.SaveCount;
            area.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal(saves + 1, f.Manager.SaveCount);

            area.EnterEdit();
            area.Window.Toolbar.DoneButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal(saves + 2, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void GT11_FocusLoss_KeepsEditMode()
        {
            // The headless platform does not deactivate a window when another is activated: the platform's deactivation
            // callback (private in Avalonia 12.1.4, hence reflection in this test) is run as Windows / X11 would on a click
            // elsewhere. IsActive turning false shows it took effect; the Deactivated event is raised with it.
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            area.Window.Show();
            area.Window.Activate();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(area.Window.IsActive);
            typeof(WindowBase).GetMethod("HandleDeactivated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(area.Window, null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(area.Window.IsActive);
            Assert.Equal(AreaMode.Editing, area.Mode);
        }

        [AvaloniaFact]
        public void GT12_AtScreenTop_ToolbarMovesInside()
        {
            using var f = new AreaFixture();
            var below = f.Add(new PhysicalRect(400, 300, 400, 240));
            Assert.False(below.ToolbarInside);
            Assert.True(Canvas.GetTop(below.Window.Toolbar) < Canvas.GetTop(below.Window.PatternView));

            var top = f.Add(new PhysicalRect(400, 10, 400, 240));
            Assert.True(top.ToolbarInside);
            Assert.True(Canvas.GetTop(top.Window.Toolbar) > Canvas.GetTop(top.Window.PatternView));
            Assert.True(top.WindowRect.Y >= 0);
        }

        [AvaloniaFact]
        public void GT13_EditMargins_RoundTripToStoredRect()
        {
            using var f = new AreaFixture(1.5);
            var area = f.Add(new PhysicalRect(401, 333, 451, 217));
            var edit = area.WindowRect;
            Assert.True(edit.X < 401 && edit.Y < 333 && edit.Right > 401 + 451 && edit.Bottom > 333 + 217);
            Assert.Equal(new PixelPoint(401 - edit.X, 333 - edit.Y), area.AreaOffsetInWindow);
            area.ExitEdit();
            Assert.Equal(new PhysicalRect(401, 333, 451, 217), area.WindowRect);
            Assert.Equal(new PhysicalRect(401, 333, 451, 217), area.Model.Rect);
            var saved = Hider.Core.Persistence.AreasFile.Load(f.AreasPath, out _).Areas.Single();
            Assert.Equal(new PhysicalRect(401, 333, 451, 217), saved.ToArea(1.5).Rect);
        }

        [AvaloniaFact]
        public void GT14_WindowProperties()
        {
            using var f = new AreaFixture();
            var w = f.Add(areaRect).Window;
            Assert.True(w.Topmost);
            Assert.False(w.ShowInTaskbar);
            Assert.Equal(WindowDecorations.None, w.WindowDecorations);
            Assert.False(w.ShowActivated);
            Assert.False(w.CanResize);
        }

        [AvaloniaFact]
        public void GT15_NewArea_CentredOnCursorMonitor()
        {
            var left = new Hider.Platform.ScreenInfo(new PhysicalRect(-1920, 0, 1920, 1080), new PhysicalRect(-1920, 0, 1920, 1040), 1.0, false);
            var right = new Hider.Platform.ScreenInfo(new PhysicalRect(0, 0, 2880, 1620), new PhysicalRect(0, 0, 2880, 1560), 1.5, true);
            var screens = new FakeScreenService(left, right) { CursorScreen = left.Bounds };
            using var f = new AreaFixture(1.0, screens);
            var area = f.Manager.AddNew();
            Assert.Equal(new PhysicalRect(-1920 + (1920 - 400) / 2, (1040 - 240) / 2, 400, 240), area.Model.Rect);
            Assert.Equal(AreaMode.Editing, area.Mode);
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
            Assert.Equal("pixelate", area.Model.Mosaic.Method);

            screens.CursorScreen = right.Bounds;
            var second = f.Manager.AddNew();
            Assert.Equal(new PhysicalRect((2880 - 600) / 2, (1560 - 360) / 2, 600, 360), second.Model.Rect);
        }

        [Theory]
        [InlineData(false, WindowCloseReason.WindowClosing, true)]
        [InlineData(false, WindowCloseReason.Undefined, true)]
        [InlineData(false, WindowCloseReason.ApplicationShutdown, false)]
        [InlineData(false, WindowCloseReason.OSShutdown, false)]
        [InlineData(true, WindowCloseReason.WindowClosing, false)]
        public void Close_RefusedOnlyForUserAttempts(bool _allowed, WindowCloseReason _reason, bool _keepOpen)
        {
            Assert.Equal(_keepOpen, AreaWindow.KeepOpen(_allowed, _reason));
        }

        [AvaloniaFact]
        public void SaveAfterDispose_DoesNotEmptyTheFile()
        {
            // A debounced save already posted to the UI thread may run after Dispose cleared the list.
            using var f = new AreaFixture();
            f.Add(new PhysicalRect(100, 100, 400, 240));
            f.Add(new PhysicalRect(600, 100, 400, 240));
            f.Manager.SaveSoon();
            f.Manager.Dispose();
            f.Manager.SaveNow();
            f.Manager.SaveSoon();
            f.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Hider.Core.Persistence.AreasFile.Load(f.AreasPath, out _).Areas.Count);
        }

        [AvaloniaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void SettingsOpen_LinuxCaptureWaitsForItToClose(bool _strips)
        {
            // The popup is its own top-level window, outside the strip transparency: on Linux it would show in the mosaic.
            var capture = new FakeUnderlayCapture { Traits = _strips ? Hider.Core.Capture.CaptureTraits.NeedsStripCooperation : Hider.Core.Capture.CaptureTraits.ExposureFree };
            using var f = new AreaFixture(_capture: capture, _isWindows: !_strips);
            var area = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            area.Window.ToggleSettings();
            int calls = capture.Calls;
            area.Refresh();
            Assert.Equal(_strips ? calls : calls + 1, capture.Calls);
            area.Window.ToggleSettings();
            Assert.Equal(calls + 1, capture.Calls);
        }

        /// <summary>A strip capture (Linux) that can hang until it is cancelled.</summary>
        private sealed class HangingStripCapture : Hider.Core.Capture.IUnderlayCapture
        {
            private readonly FakeUnderlayCapture inner = new();

            public Hider.Core.Capture.CaptureTraits Traits => Hider.Core.Capture.CaptureTraits.NeedsStripCooperation;
            public bool Hang { get; set; }
            public int Calls { get; private set; }

            /// <summary>Called before hanging with the capture's token: lets a test start a strip (or not, a "prepare").</summary>
            public Action<CancellationToken>? BeforeHang { get; set; }

            public async Task<Hider.Core.Capture.CaptureResult> CaptureAsync(Hider.Core.Capture.CaptureRequest _request, CancellationToken _ct)
            {
                Calls++;
                if (Hang)
                {
                    BeforeHang?.Invoke(_ct);
                    await Task.Delay(Timeout.Infinite, _ct);
                }
                return await inner.CaptureAsync(_request, _ct);
            }

            public void Dispose()
            {
            }
        }

        [AvaloniaFact]
        public void SettingsOfAnotherArea_HoldsAndCancelsStripCaptures_WithoutFallingBack()
        {
            // Area A's settings panel may lie over area B: B's capture must not run, and one running when the popup opens is
            // cancelled (not a capture failure) and taken again when the popup closes.
            var capture = new HangingStripCapture();
            using var f = new AreaFixture(_capture: capture, _isWindows: false);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            var b = f.Add(new PhysicalRect(600, 200, 400, 240), PatternKind.Mosaic);
            capture.Hang = true;
            capture.BeforeHang = _ct => StartStrip(b, _ct); // the capture is in its strips
            b.Refresh();
            int calls = capture.Calls;

            a.Window.ToggleSettings();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the cancelled capture finishes on the UI thread
            Assert.Equal(PatternKind.Mosaic, b.Model.Pattern);
            Assert.False(b.CaptureFailed);
            capture.Hang = false;
            b.Refresh();
            Assert.Equal(calls, capture.Calls);

            a.Window.ToggleSettings();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(calls + 1, capture.Calls);
            Assert.Equal(PatternKind.Mosaic, b.Model.Pattern);
        }

        /// <summary>The first strip of a capture, as StripCoordinator starts it (its failure on cancel is the coordinator's).</summary>
        private static void StartStrip(AreaController _area, CancellationToken _ct)
        {
            _area.Window.SetTransparentRowsAsync(0, 8, _ct).ContinueWith(_ => _area.Window.RestoreRowsAsync(), TaskScheduler.Default);
        }

        [AvaloniaFact]
        public void BeforeTheFirstStrip_SettingsAndModeChangesLeaveThePermissionDialogAlone()
        {
            // Before its first strip a capture may be showing a portal permission dialog (Prepare): cancelling it would close
            // the dialog the user is answering. A mode change lets it go on (the clip follows the new layout); a settings
            // popup holds the cancel until the first strip, or drops it if the popup closes first.
            var capture = new HangingStripCapture();
            using var f = new AreaFixture(_capture: capture, _isWindows: false);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            var b = f.Add(new PhysicalRect(600, 200, 400, 240), PatternKind.Mosaic);
            b.ExitEdit(); // a stays in edit mode: its settings popup opens only there
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            CancellationToken running = default;
            capture.Hang = true;
            capture.BeforeHang = _ct => running = running.CanBeCanceled ? running : _ct; // B's, not a later one's
            b.Refresh();
            int calls = capture.Calls;

            b.EnterEdit();
            b.ExitEdit();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(running.IsCancellationRequested, "a mode change before the strips cancelled the capture");

            a.Window.ToggleSettings();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(running.IsCancellationRequested, "a settings popup before the strips cancelled the capture");

            capture.Hang = false;
            StartStrip(b, running); // the dialog was answered, the strips begin: now the held cancel happens
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(running.IsCancellationRequested);
            Assert.Equal(PatternKind.Mosaic, b.Model.Pattern);
            Assert.False(b.CaptureFailed);
            Assert.Equal(calls, capture.Calls);

            a.Window.ToggleSettings();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(calls + 1, capture.Calls); // taken again once the popup closed
        }

        [AvaloniaFact]
        public void StripsOfAnOverlappingAreasCapture_DoNotCountAsThisAreasOwn()
        {
            // StripCoordinator turns every overlapping mosaic window see-through. While A's capture waits for its turn (or
            // prepares, a dialog open), B's strips come to A's window: they must not make A's capture "in its strips".
            var capture = new HangingStripCapture();
            using var f = new AreaFixture(_capture: capture, _isWindows: false);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            var b = f.Add(new PhysicalRect(300, 200, 400, 240), PatternKind.Mosaic);
            a.ExitEdit();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            CancellationToken running = default;
            capture.Hang = true;
            capture.BeforeHang = _ct => running = running.CanBeCanceled ? running : _ct; // A's, not a later one's
            a.Refresh();
            Assert.True(running.CanBeCanceled, "A's capture is running");
            using var other = new CancellationTokenSource(); // B's capture
            StartStrip(a, other.Token); // B's strip reaches A's window
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            a.EnterEdit();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(running.IsCancellationRequested, "B's strip made A's prepare count as strips");
            other.Cancel();
        }

        [AvaloniaFact]
        public void ModeChangeDuringStrips_CancelsAndRetakes_WithoutFallingBack()
        {
            // Entering or leaving edit mode moves the area inside its window: strips taken through the old layout are not
            // used; the capture is cancelled (not a failure) and taken again right after.
            var capture = new HangingStripCapture();
            using var f = new AreaFixture(_capture: capture, _isWindows: false);
            var area = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            area.ExitEdit();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            capture.Hang = true;
            capture.BeforeHang = _ct => StartStrip(area, _ct);
            area.Refresh();
            int calls = capture.Calls;
            capture.Hang = false;
            area.EnterEdit();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
            Assert.False(area.CaptureFailed);
            Assert.Equal(calls + 1, capture.Calls); // taken again once
        }

        [AvaloniaFact]
        public void ModeChangeDuringStrips_RebuildsTheClipForTheNewLayout()
        {
            // The clip is made from the area's place in the window; edit mode moves it down by the toolbar band.
            using var f = new AreaFixture();
            var area = f.Add(new PhysicalRect(100, 200, 120, 80), PatternKind.Generated);
            area.ExitEdit();
            _ = area.Window.SetTransparentRowsAsync(16, 24, TestContext.Current.CancellationToken);
            area.EnterEdit();
            Assert.True(area.AreaOffsetInWindow.Y > 0);
            var group = (Avalonia.Media.GeometryGroup)area.Window.Root.Clip!;
            var above = ((Avalonia.Media.RectangleGeometry)group.Children[0]).Rect;
            var below = ((Avalonia.Media.RectangleGeometry)group.Children[1]).Rect;
            double scale = area.Host.Scale;
            Assert.Equal((area.AreaOffsetInWindow.Y + 16 - 1) / scale, above.Bottom, 3); // the strip moved with the area
            Assert.Equal((area.AreaOffsetInWindow.Y + 24 + 1) / scale, below.Top, 3);
            Assert.True(below.Bottom >= area.WindowRect.Height / scale, "the clip reaches the bottom of the larger window");
        }

        [AvaloniaFact]
        public void KeysPressedInTheSettingsPopup_DoNotMoveOrDeleteTheArea()
        {
            // Popup events bubble on to the window that owns the popup: arrows in a number box or Delete on a slider must
            // stay in the panel.
            using var f = new AreaFixture();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Window.Show(); // the popup opens (as its own top level) only on a shown window
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            area.Window.ToggleSettings();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var panel = area.Window.Settings!;
            Assert.NotNull(TopLevel.GetTopLevel(panel)); // open: headless hosts it as an overlay, a desktop as a PopupRoot
            var row = panel.ParamRows[0];
            foreach (var (target, key) in new (Control, Key)[] { (row.Input, Key.Up), (row.Slider, Key.Left), (row.Slider, Key.Delete), (row.Input, Key.Escape) })
            {
                target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
            }
            Assert.Equal(areaRect, area.Model.Rect);
            Assert.Contains(area, f.Manager.Areas);
            Assert.Equal(AreaMode.Editing, area.Mode);
        }

        [AvaloniaFact]
        public void DisplayChange_PlacesTheWindowAgain()
        {
            // After a DPI or monitor change the system resized the window; it is placed again at the area's rectangle.
            using var f = new AreaFixture();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.ExitEdit();
            var host = f.Hosts.Single();
            int before = host.Placements.Count;
            host.RaiseDisplayChanged();
            Assert.Equal(before + 1, host.Placements.Count);
            Assert.Equal(areaRect, host.Last);
        }

        [AvaloniaFact]
        public void EditMargins_FollowTheWindowsScale_WhenItIsLarger()
        {
            // The area is on a 100% monitor, but with its margins and toolbar the window is mostly on a 150% one: the margins
            // and the toolbar width are worked out at 150%, or the toolbar would be cut off.
            using var f = new AreaFixture(1.5);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            var host = f.Hosts.Single();
            host.ScaleAt = _r => _r == areaRect ? 1.0 : 1.5;
            area.Layout();
            Assert.Equal(areaRect.X - 12, host.Last.X); // 8 DIP at 150%
            Assert.True(host.Last.Width >= 12 + (int)Math.Ceiling(area.ToolbarWidthDip * 1.5), "the toolbar fits");
            int count = host.Placements.Count;
            area.Layout();
            Assert.Equal(host.Placements[count - 1], host.Last); // stable
        }

        [AvaloniaFact]
        public void EditMargins_StayAtTheAreasScale_WhenTheWindowsIsSmaller()
        {
            // Margins made at 200% would put the window on the 100% monitor: it keeps the larger margins (room to spare)
            // instead of flipping between the two scales.
            using var f = new AreaFixture(1.0);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            var host = f.Hosts.Single();
            host.ScaleAt = _r => _r == areaRect ? 2.0 : 1.0;
            area.Layout();
            Assert.Equal(areaRect.X - 16, host.Last.X); // 8 DIP at 200%
            int count = host.Placements.Count;
            area.Layout();
            Assert.Equal(host.Placements[count - 1], host.Last);
        }

        [AvaloniaFact]
        public void EnteringEditMode_ActivatesTheWindow_ShowingDoesNot()
        {
            // Arrows, Delete and Esc (FR-33) must work right after "add" or "edit all" from the tray, without a click first;
            // restoring hidden areas at startup must not take the focus from the user's window (FR-11).
            using var f = new AreaFixture();
            AreasFile.Save(f.AreasPath, new AppState
            {
                Areas =
                {
                    new AreaState { Id = "0f3b8d2e", X = 100, Y = 100, Width = 300, Height = 200, Pattern = PatternKind.Fill },
                    new AreaState { Id = "1a2b3c4d", X = 700, Y = 100, Width = 300, Height = 200, Pattern = PatternKind.Fill },
                },
            });
            var user = new Window { Width = 200, Height = 100 };
            user.Show();
            user.Activate();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(user.IsActive);

            f.Manager.Restore();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.All(f.Manager.Areas, _a => Assert.False(_a.Window.IsActive));
            Assert.True(user.IsActive);

            // (The headless platform does not deactivate the other window; that the areas were inactive before is the check.)
            f.Manager.EditAll();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Contains(f.Manager.Areas, _a => _a.Window.IsActive);

            f.Manager.EndEditAll();
            var added = f.Manager.AddNew();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(added.Window.IsActive);
            user.Close();
        }
    }
}
