using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// AreaWindow: pointer and key input in both modes (FR-21, FR-22, FR-32, FR-33), the toolbar's buttons and menu, the
    /// settings popup, opacity, closing, and the strip transparency calls.
    /// </summary>
    public class AreaWindowInputTests
    {
        private static readonly PhysicalRect areaRect = new(400, 300, 400, 240);
        private static readonly bool[] openThenClosed = { true, false };
        private static readonly string[] exitEditOnly = { "exit-edit" };

        private static FakeUnderlayCapture Fake(AreaHarness _f) => (FakeUnderlayCapture)_f.Capture;

        /// <summary>A point inside the window at the area's top-left plus (dx, dy) DIP (scale 1).</summary>
        private static Point AtArea(AreaController _area, double _dx, double _dy)
        {
            var offset = _area.AreaOffsetInWindow;
            double s = _area.Host.Scale;
            return new Point(offset.X / s + _dx, offset.Y / s + _dy);
        }

        private static Point Center(AreaController _area) => AtArea(_area, _area.Model.Rect.Width / 2.0 / _area.Host.Scale, _area.Model.Rect.Height / 2.0 / _area.Host.Scale);

        private static void Click(Control _control)
        {
            _control.RaiseEvent(new RoutedEventArgs(_control is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));
        }

        private static void Press(AreaController _area, Key _key, RawInputModifiers _modifiers = RawInputModifiers.None)
        {
            _area.Window.KeyPress(_key, _modifiers, PhysicalKey.None, null);
        }

        [AvaloniaFact]
        public void GT03_Hidden_RightDoubleClick_DoesNotEnterEditMode()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            var p = Center(area);
            for (int i = 0; i < 2; i++)
            {
                area.Window.MouseDown(p, MouseButton.Right);
                area.Window.MouseUp(p, MouseButton.Right);
            }
            Assert.Equal(AreaMode.Hidden, area.Mode);
        }

        [AvaloniaFact]
        public void GT04_Hidden_SingleLeftClick_StaysHidden_AndBothPressAndReleaseAreHandled()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            bool? pressHandled = null, releaseHandled = null;
            area.Window.AddHandler(InputElement.PointerPressedEvent, (_, _e) => pressHandled = _e.Handled, RoutingStrategies.Bubble, true);
            area.Window.AddHandler(InputElement.PointerReleasedEvent, (_, _e) => releaseHandled = _e.Handled, RoutingStrategies.Bubble, true);
            var p = Center(area);
            area.Window.MouseDown(p, MouseButton.Left);
            area.Window.MouseUp(p, MouseButton.Left);
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.True(pressHandled);
            Assert.True(releaseHandled);
        }

        [AvaloniaFact]
        public void GT04_Hidden_Wheel_IsHandled()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            bool? handled = null;
            area.Window.AddHandler(InputElement.PointerWheelChangedEvent, (_, _e) => handled = _e.Handled, RoutingStrategies.Bubble, true);
            area.Window.MouseWheel(Center(area), new Vector(0, 1));
            Assert.True(handled);
        }

        [AvaloniaFact]
        public void GT04_Editing_Wheel_IsHandledToo()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            bool? handled = null;
            area.Window.AddHandler(InputElement.PointerWheelChangedEvent, (_, _e) => handled = _e.Handled, RoutingStrategies.Bubble, true);
            area.Window.MouseWheel(Center(area), new Vector(0, -1));
            Assert.True(handled);
            Assert.Equal(areaRect, area.Model.Rect);
        }

        [AvaloniaFact]
        public void GT75_Editing_RightPress_StartsNoDrag()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var p = Center(area);
            area.Window.MouseDown(p, MouseButton.Right);
            Assert.False(area.IsDragging);
            area.Window.MouseMove(p + new Vector(30, 30));
            area.Window.MouseUp(p + new Vector(30, 30), MouseButton.Right);
            Assert.Equal(areaRect, area.Model.Rect);
        }

        [AvaloniaTheory]
        [InlineData(-7, 60)] // left margin
        [InlineData(407, 60)] // right margin
        [InlineData(100, 247)] // bottom margin
        [InlineData(100, -7)] // between the toolbar and the area
        public void GT75_Editing_PressInTheTransparentMargin_StartsNoDrag(double _dx, double _dy)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var p = AtArea(area, _dx, _dy);
            Assert.True(p.X >= 0 && p.Y >= 0 && p.X < area.Window.Width && p.Y < area.Window.Height, "inside the window");
            area.Window.MouseDown(p, MouseButton.Left);
            Assert.False(area.IsDragging);
            area.Window.MouseMove(p + new Vector(30, 30));
            area.Window.MouseUp(p + new Vector(30, 30), MouseButton.Left);
            Assert.Equal(areaRect, area.Model.Rect);
        }

        [AvaloniaFact]
        public void GT75_Editing_PressOnTheToolbarBackground_StartsNoDrag()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var toolbar = area.Window.Toolbar;
            // On the toolbar's border, above its buttons.
            var p = new Point(Canvas.GetLeft(toolbar) + 20, Canvas.GetTop(toolbar) + 2);
            area.Window.MouseDown(p, MouseButton.Left);
            Assert.False(area.IsDragging);
            area.Window.MouseUp(p, MouseButton.Left);
        }

        /// <summary>The centre of a toolbar button in window coordinates (laid out first).</summary>
        private static Point ButtonCenter(AreaController _area, Button _button)
        {
            _area.Window.UpdateLayout();
            var p = _button.TranslatePoint(new Point(_button.Bounds.Width / 2, _button.Bounds.Height / 2), _area.Window);
            Assert.NotNull(p);
            return p.Value;
        }

        [AvaloniaFact]
        public void GT75_Editing_PressOnASettingsButtonOverTheArea_ClicksItWithoutADrag()
        {
            // At the screen's top the toolbar lies inside the area: the button has handled the press, which must not also
            // start moving the area below it.
            using var f = new AreaHarness();
            var area = f.Add(new PhysicalRect(400, 10, 400, 240));
            Assert.True(area.ToolbarInside);
            var p = ButtonCenter(area, area.Window.Toolbar.SettingsButton);
            var inPattern = area.Window.TranslatePoint(p, area.Window.PatternView);
            Assert.True(inPattern is { } q && new Rect(area.Window.PatternView.Bounds.Size).Contains(q)); // over the area
            area.Window.MouseDown(p, MouseButton.Left);
            Assert.False(area.IsDragging);
            area.Window.MouseUp(p, MouseButton.Left);
            Assert.True(area.Window.PopupOpen); // the click went to ⚙
            Assert.Equal(new PhysicalRect(400, 10, 400, 240), area.Model.Rect);
        }

        [AvaloniaFact]
        public void GT75_Editing_PressOnTheDoneButtonOverTheArea_LeavesEditModeWithoutADrag()
        {
            using var f = new AreaHarness();
            var area = f.Add(new PhysicalRect(400, 10, 400, 240));
            var log = new List<string>();
            area.Transition += log.Add;
            var p = ButtonCenter(area, area.Window.Toolbar.DoneButton);
            area.Window.MouseDown(p, MouseButton.Left);
            Assert.False(area.IsDragging);
            area.Window.MouseUp(p, MouseButton.Left);
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal(exitEditOnly, log);
        }

        [AvaloniaFact]
        public void GT06_Editing_PressOnTheBody_FocusesTheWindow_AndCapturesThePointer()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.Window.Activate();
            area.Window.UpdateLayout();
            Assert.True(area.Window.Toolbar.DoneButton.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(area.Window.Toolbar.DoneButton, area.Window.FocusManager!.GetFocusedElement());
            IPointer? pointer = null;
            area.Window.AddHandler(InputElement.PointerPressedEvent, (_, _e) => pointer = _e.Pointer, RoutingStrategies.Bubble, true);
            var p = Center(area);
            area.Window.MouseDown(p, MouseButton.Left);
            Assert.True(area.IsDragging);
            Assert.Same(area.Window, area.Window.FocusManager!.GetFocusedElement());
            Assert.Same(area.Window, pointer!.Captured);
            area.Window.MouseUp(p, MouseButton.Left);
            Assert.Null(pointer.Captured);
            Assert.False(area.IsDragging);
        }

        [AvaloniaFact]
        public void GT06_Editing_ControlWithAHandleTagThatIsNoHandle_Moves()
        {
            // Only the chrome's own eight handles resize; a Tag alone does not make a handle.
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.Window.PatternView.Tag = ResizeHandle.Left;
            var p = Center(area);
            var screenStart = area.Window.PointToScreen(p);
            area.Window.MouseDown(p, MouseButton.Left);
            var to = area.Window.PointToClient(screenStart + new PixelVector(10, 0));
            area.Window.MouseMove(to);
            area.Window.MouseUp(area.Window.PointToClient(screenStart + new PixelVector(10, 0)), MouseButton.Left);
            Assert.Equal(areaRect.Offset(10, 0), area.Model.Rect);
        }

        [AvaloniaFact]
        public void GT06_PointerMove_WithoutADrag_IsLeftAlone()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            bool? handled = null;
            area.Window.AddHandler(InputElement.PointerMovedEvent, (_, _e) => handled = _e.Handled, RoutingStrategies.Bubble, true);
            area.Window.MouseMove(Center(area));
            Assert.False(handled);
            Assert.Equal(areaRect, area.Model.Rect);
        }

        [AvaloniaFact]
        public void GT06_PointerRelease_InEditModeWithoutADrag_IsLeftAlone()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            bool? handled = null;
            area.Window.AddHandler(InputElement.PointerReleasedEvent, (_, _e) => handled = _e.Handled, RoutingStrategies.Bubble, true);
            var p = AtArea(area, -7, 60);
            area.Window.MouseDown(p, MouseButton.Left);
            area.Window.MouseUp(p, MouseButton.Left);
            Assert.False(handled);
        }

        [AvaloniaFact]
        public void GT62_PointerCaptureLost_EndsTheDrag_AndSaves()
        {
            // Another window or the system took the pointer (a modal, Alt+Tab): the drag ends where it is.
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            IPointer? pointer = null;
            area.Window.AddHandler(InputElement.PointerPressedEvent, (_, _e) => pointer = _e.Pointer, RoutingStrategies.Bubble, true);
            var p = Center(area);
            var screenStart = area.Window.PointToScreen(p);
            area.Window.MouseDown(p, MouseButton.Left);
            area.Window.MouseMove(area.Window.PointToClient(screenStart + new PixelVector(12, 7)));
            int saves = f.Manager.SaveCount;
            pointer!.Capture(null);
            Assert.False(area.IsDragging);
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Equal(areaRect.Offset(12, 7), area.Model.Rect);
        }

        [AvaloniaTheory]
        [InlineData(Key.Left, RawInputModifiers.None, -1, 0)]
        [InlineData(Key.Right, RawInputModifiers.None, 1, 0)]
        [InlineData(Key.Up, RawInputModifiers.None, 0, -1)]
        [InlineData(Key.Down, RawInputModifiers.None, 0, 1)]
        [InlineData(Key.Left, RawInputModifiers.Shift, -10, 0)]
        [InlineData(Key.Right, RawInputModifiers.Shift, 10, 0)]
        [InlineData(Key.Up, RawInputModifiers.Shift, 0, -10)]
        [InlineData(Key.Down, RawInputModifiers.Shift, 0, 10)]
        [InlineData(Key.Down, RawInputModifiers.Control, 0, 1)] // only Shift changes the step
        public void GT08_ArrowKeys_MoveOneOrTenPixels(Key _key, RawInputModifiers _modifiers, int _dx, int _dy)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            bool? handled = null;
            area.Window.AddHandler(InputElement.KeyDownEvent, (_, _e) => handled = _e.Handled, RoutingStrategies.Bubble, true);
            Press(area, _key, _modifiers);
            Assert.Equal(areaRect.Offset(_dx, _dy), area.Model.Rect);
            Assert.True(handled);
        }

        [AvaloniaTheory]
        [InlineData(Key.A)]
        [InlineData(Key.Back)]
        [InlineData(Key.F5)]
        public void GT76_OtherKeys_InEditMode_AreLeftAlone(Key _key)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            bool? handled = null;
            area.Window.AddHandler(InputElement.KeyDownEvent, (_, _e) => handled = _e.Handled, RoutingStrategies.Bubble, true);
            Press(area, _key);
            Assert.False(handled);
            Assert.Equal(areaRect, area.Model.Rect);
            Assert.Equal(AreaMode.Editing, area.Mode);
            Assert.Single(f.Manager.Areas);
        }

        [AvaloniaTheory]
        [InlineData(Key.Left)]
        [InlineData(Key.Delete)]
        [InlineData(Key.Escape)]
        public void GT76_Keys_InHiddenMode_DoNothing(Key _key)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            int saves = f.Manager.SaveCount;
            Press(area, _key);
            Assert.Equal(areaRect, area.Model.Rect);
            Assert.Single(f.Manager.Areas);
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal(saves, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void GT76_Keys_AlreadyHandledByTheFocusedControl_DoNothing()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete, Source = area.Window, Handled = true };
            area.Window.RaiseEvent(e);
            Assert.Single(f.Manager.Areas);
        }

        [AvaloniaFact]
        public void GT10_Escape_LeavesEditMode_AndDelete_RemovesTheArea()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            Press(area, Key.Escape);
            Assert.Equal(AreaMode.Hidden, area.Mode);
            area.EnterEdit();
            Press(area, Key.Delete);
            Assert.Empty(f.Manager.Areas);
        }

        [AvaloniaTheory]
        [InlineData("FillItem", PatternKind.Fill)]
        [InlineData("GeneratedItem", PatternKind.Generated)]
        public void GT77_PatternMenuItems_SwitchThePattern(string _item, PatternKind _pattern)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            var toolbar = area.Window.Toolbar;
            var item = _item == "FillItem" ? toolbar.FillItem : toolbar.GeneratedItem;
            Click(item);
            Assert.Equal(_pattern, area.Model.Pattern);
            Click(toolbar.MosaicItem);
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
        }

        [AvaloniaFact]
        public void GT35_RefreshButton_CapturesAgain()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            int calls = Fake(f).Calls;
            Click(area.Window.Toolbar.RefreshButton);
            Assert.Equal(calls + 1, Fake(f).Calls);
        }

        [AvaloniaFact]
        public void GT77_DeleteButton_RemovesTheArea_AndDoneButton_LeavesEditMode()
        {
            using var f = new AreaHarness();
            var a = f.Add(areaRect);
            var b = f.Add(areaRect.Offset(500, 0));
            Click(a.Window.Toolbar.DoneButton);
            Assert.Equal(AreaMode.Hidden, a.Mode);
            Click(b.Window.Toolbar.DeleteButton);
            Assert.Equal(a, Assert.Single(f.Manager.Areas));
        }

        [AvaloniaFact]
        public void GT77_SettingsButton_OpensAndClosesTheSettingsPopup()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            var seen = new List<bool>();
            area.Window.PopupOpenChanged += seen.Add;
            Assert.False(area.Window.PopupOpen);
            Assert.Null(area.Window.Settings);
            Click(area.Window.Toolbar.SettingsButton);
            Assert.True(area.Window.PopupOpen);
            var panel = area.Window.Settings;
            Assert.NotNull(panel);
            Assert.Equal(PatternKind.Mosaic, panel.Pattern);
            Assert.True(f.Manager.AnyPopupOpen);
            Click(area.Window.Toolbar.SettingsButton);
            Assert.False(area.Window.PopupOpen);
            Assert.Null(area.Window.Settings);
            Assert.Equal(openThenClosed, seen);

            // Opened again: a new panel for the current values.
            area.Window.ToggleSettings();
            Assert.NotSame(panel, area.Window.Settings);
            area.Window.ToggleSettings();
        }

        [AvaloniaFact]
        public void GT77_PatternMenu_OpenAndClose_AreReported()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            var seen = new List<bool>();
            area.Window.PopupOpenChanged += seen.Add;
            var menu = area.Window.Toolbar.PatternMenu;
            menu.ShowAt(area.Window.Toolbar.PatternButton);
            Assert.True(area.Window.PopupOpen);
            menu.Hide();
            Assert.False(area.Window.PopupOpen);
            Assert.Equal(openThenClosed, seen);
        }

        [AvaloniaFact]
        public void GT77_OpenSettings_AreRebuiltWhenThePatternChanges_AndKeptOtherwise()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Window.ToggleSettings();
            var panel = area.Window.Settings!;
            var rows = panel.ParamRows;
            area.SettingsChanged();
            Assert.Same(rows, panel.ParamRows); // same pattern: the rows (and the control being dragged) stay
            area.SetPattern(PatternKind.Generated);
            Assert.Same(panel, area.Window.Settings);
            Assert.Equal(PatternKind.Generated, panel.Pattern);
            Assert.NotSame(rows, panel.ParamRows);
        }

        [AvaloniaFact]
        public void GT02_LeavingEditMode_ClosesTheSettingsAndThePatternMenu_AndRemovesTheChrome()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Window.ToggleSettings();
            Assert.True(area.Window.ChromeAttached);
            area.ExitEdit();
            Assert.False(area.Window.PopupOpen);
            Assert.False(area.Window.ChromeAttached);
            Assert.Equal(0, Canvas.GetLeft(area.Window.PatternView));
            Assert.Equal(0, Canvas.GetTop(area.Window.PatternView));
            Assert.Equal(areaRect.Width, area.Window.PatternView.Width, 6);
            area.EnterEdit();
            Assert.True(area.Window.ChromeAttached);
            Assert.Single(area.Window.Root.Children.OfType<EditChrome>());
            area.Layout(); // laid out again in edit mode: the chrome is not added twice
            Assert.Single(area.Window.Root.Children.OfType<EditChrome>());
        }

        [AvaloniaFact]
        public void GT55_ExitEdit_ClosingTheSettings_RunsTheHeldLinuxCapture()
        {
            // The popup closed by leaving edit mode (ApplyLayout), not by ⚙: the capture held for it runs then. ExitEdit
            // itself asks for no capture (it only saves), so this is the one place that takes it.
            var capture = new FakeUnderlayCapture { Traits = CaptureTraits.NeedsStripCooperation };
            using var f = new AreaHarness(_capture: capture, _isWindows: false);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Window.ToggleSettings();
            int calls = capture.Calls;
            area.Refresh();
            Assert.Equal(calls, capture.Calls); // held while the popup is open
            area.ExitEdit();
            Assert.False(area.Window.PopupOpen);
            Assert.False(f.Manager.AnyPopupOpen);
            Assert.Equal(calls + 1, capture.Calls);
            Assert.Equal(AreaMode.Hidden, area.Mode);
        }

        [AvaloniaFact]
        public void GT55_ExitEdit_ClosingThePatternMenu_RunsTheHeldLinuxCapture()
        {
            var capture = new FakeUnderlayCapture { Traits = CaptureTraits.NeedsStripCooperation };
            using var f = new AreaHarness(_capture: capture, _isWindows: false);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Window.Toolbar.PatternMenu.ShowAt(area.Window.Toolbar.PatternButton);
            int calls = capture.Calls;
            area.Refresh();
            Assert.Equal(calls, capture.Calls);
            area.ExitEdit();
            Assert.False(area.Window.Toolbar.PatternMenu.IsOpen);
            Assert.False(f.Manager.AnyPopupOpen);
            Assert.Equal(calls + 1, capture.Calls);
        }

        [AvaloniaFact]
        public void GT55_ExitEdit_ClosingBothPopups_RunsTheHeldLinuxCaptureOnce_AfterTheLastOne()
        {
            // The settings popup closes first while the menu is still open: the capture waits for the menu too.
            var capture = new FakeUnderlayCapture { Traits = CaptureTraits.NeedsStripCooperation };
            using var f = new AreaHarness(_capture: capture, _isWindows: false);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Window.ToggleSettings();
            area.Window.Toolbar.PatternMenu.ShowAt(area.Window.Toolbar.PatternButton);
            int calls = capture.Calls;
            area.Refresh();
            // Runs after the controller's own handler: the captures made by the time each popup has closed.
            var atClose = new List<int>();
            area.Window.PopupOpenChanged += _open =>
            {
                if (!_open)
                {
                    atClose.Add(capture.Calls);
                }
            };
            area.ExitEdit();
            Assert.False(f.Manager.AnyPopupOpen);
            Assert.Equal(new[] { calls, calls + 1 }, atClose); // none when the first one closed, one with the last
            Assert.Equal(calls + 1, capture.Calls);
        }

        [AvaloniaFact]
        public void GT58_Accessors_AreTheWindowsOwnParts()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            Assert.Same(area.Window.Chrome.Toolbar, area.Window.Toolbar);
            Assert.Same(area.Window.Pattern, area.Window.PatternView);
            Assert.Contains(area.Window.Chrome, area.Window.Root.Children);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill, 0.5, 0.5)]
        [InlineData(PatternKind.Generated, 0.3, 0.3)]
        [InlineData(PatternKind.Generated, 0.05, 0.10)] // alpha opacity has a floor (spec §4.5)
        [InlineData(PatternKind.Fill, 0.0, 0.10)]
        [InlineData(PatternKind.Mosaic, 0.3, 1.0)] // mosaic opacity is the fineness, not alpha
        public void GT22_Opacity_IsWindowAlphaWhenHidden_PatternAlphaWhenEditing(PatternKind _pattern, double _opacity, double _alpha)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, _pattern);
            area.Model.Opacity = _opacity;
            area.SettingsChanged();
            Assert.Equal(1.0, area.Window.Opacity, 6); // the toolbar stays readable
            Assert.Equal(_alpha, area.Window.PatternView.Opacity, 6);
            area.ExitEdit();
            Assert.Equal(_alpha, area.Window.Opacity, 6);
            Assert.Equal(1.0, area.Window.PatternView.Opacity, 6);
        }

        [AvaloniaFact]
        public void GT14_UsersClose_IsRefused_ControllersCloseIsNot()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.Window.Close(); // Alt+F4 must not lose an area
            Assert.True(area.Window.IsVisible);
            Assert.Single(f.Manager.Areas);
            area.Delete();
            Assert.False(area.Window.IsVisible);
        }

        [AvaloniaFact]
        public void GT78_Strip_Constants()
        {
            Assert.Equal(1, AreaWindow.StripSpareFrames);
            Assert.Equal(TimeSpan.FromMilliseconds(250), AreaWindow.StripFrameTimeout);
        }

        [AvaloniaFact]
        public void GT78_SetTransparentRows_AlreadyCancelled_ThrowsBeforeAnythingTurnsSeeThrough()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var seen = new List<CancellationToken>();
            area.Window.StripStarting += seen.Add;
            var task = area.Window.SetTransparentRowsAsync(0, 8, cts.Token);
            Assert.True(task.IsCanceled);
            Assert.Null(area.Window.TransparentRows);
            Assert.Null(area.Window.Root.Clip);
            Assert.Equal(cts.Token, Assert.Single(seen)); // the controller is told first: a held cancel happens there
        }

        [AvaloniaFact]
        public void GT78_SetTransparentRows_CancelledWhileWaitingForTheFrame_IsNoTimeout()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            using var cts = new CancellationTokenSource();
            var task = area.Window.SetTransparentRowsAsync(0, 8, cts.Token);
            Assert.False(task.IsCompleted);
            Assert.Equal((0, 8), area.Window.TransparentRows);
            Assert.NotNull(area.Window.Root.Clip);
            cts.Cancel();
            UiJobs.Complete(task);
            Assert.True(task.IsCanceled);
            Assert.Equal(0, area.Window.StripFrameTimeouts);
        }

        [AvaloniaFact]
        public void GT78_RestoreRows_ClearsTheClip_AtOnce()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            _ = area.Window.SetTransparentRowsAsync(4, 12, TestContext.Current.CancellationToken);
            var restore = area.Window.RestoreRowsAsync();
            Assert.True(restore.IsCompletedSuccessfully);
            Assert.Null(area.Window.TransparentRows);
            Assert.Null(area.Window.Root.Clip);
            area.Layout(); // no rows: the layout leaves the window unclipped
            Assert.Null(area.Window.Root.Clip);
        }

        [AvaloniaFact]
        public void GT78_Clip_CoversTheRowsAboveAndBelow_WithOnePixelOfSlack()
        {
            using var f = new AreaHarness(2.0);
            var area = f.Add(new PhysicalRect(100, 200, 120, 80), PatternKind.Generated);
            area.ExitEdit();
            _ = area.Window.SetTransparentRowsAsync(16, 24, TestContext.Current.CancellationToken);
            var group = (Avalonia.Media.GeometryGroup)area.Window.Root.Clip!;
            Assert.Equal(2, group.Children.Count);
            var above = ((Avalonia.Media.RectangleGeometry)group.Children[0]).Rect;
            var below = ((Avalonia.Media.RectangleGeometry)group.Children[1]).Rect;
            Assert.Equal(-1, above.X);
            Assert.Equal(-1, above.Y);
            Assert.Equal((16 - 1) / 2.0, above.Bottom, 6);
            Assert.Equal((24 + 1) / 2.0, below.Top, 6);
            Assert.True(above.Width >= 120 / 2.0 + 2);
            Assert.True(below.Bottom >= 80 / 2.0);
        }

        [AvaloniaFact]
        public void GT78_Clip_StripAtTheTop_LeavesNoNegativeBandAbove()
        {
            using var f = new AreaHarness();
            var area = f.Add(new PhysicalRect(100, 200, 120, 80), PatternKind.Generated);
            area.ExitEdit();
            _ = area.Window.SetTransparentRowsAsync(0, 8, TestContext.Current.CancellationToken);
            var group = (Avalonia.Media.GeometryGroup)area.Window.Root.Clip!;
            var above = ((Avalonia.Media.RectangleGeometry)group.Children[0]).Rect;
            Assert.Equal(0, above.Height); // from -1 to -1: nothing above the strip is drawn
        }

        [AvaloniaFact]
        public void GT77_WindowWithoutListeners_RaisesNothing()
        {
            // The events have no listener on a window the controller did not make (its own window always has them):
            // ↻, the pattern menu, the settings popup and a strip still work.
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            var window = new AreaWindow();
            window.Attach(area);
            Click(window.Toolbar.RefreshButton);
            Click(window.Toolbar.SettingsButton);
            Assert.True(window.PopupOpen);
            window.ToggleSettings();
            Assert.False(window.PopupOpen);
            window.Toolbar.PatternMenu.ShowAt(window.Toolbar.PatternButton);
            window.Toolbar.PatternMenu.Hide();
            // Never shown, so no frame comes: the wait gives up after the timeout on the app's clock.
            var strip = window.SetTransparentRowsAsync(0, 8, TestContext.Current.CancellationToken);
            Assert.False(strip.IsCompleted);
            f.Time.Advance(AreaWindow.StripFrameTimeout);
            UiJobs.Complete(strip);
            Assert.True(strip.IsCompletedSuccessfully);
            Assert.Equal(1, window.StripFrameTimeouts);
            window.CloseFromController();
        }
    }
}
