using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hider.Areas;
using Hider.Core.Model;
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

        [AvaloniaFact]
        public void GT07_HandleDrag_MovesOneEdgeAndStopsAtMinimum()
        {
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            Point Center(ResizeHandle _kind)
            {
                var h = area.Window.Chrome.Handles.Single(_x => _x.Kind == _kind).Control;
                return new Point(Canvas.GetLeft(h) + h.Width / 2, Canvas.GetTop(h) + h.Height / 2);
            }

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
            using var f = new AreaFixture();
            var area = f.Add(areaRect);
            area.Window.Activate();
            var other = new Window();
            other.Show();
            other.Activate();
            Assert.Equal(AreaMode.Editing, area.Mode);
            other.Close();
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

            public async Task<Hider.Core.Capture.CaptureResult> CaptureAsync(Hider.Core.Capture.CaptureRequest _request, CancellationToken _ct)
            {
                Calls++;
                if (Hang)
                {
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
    }
}
