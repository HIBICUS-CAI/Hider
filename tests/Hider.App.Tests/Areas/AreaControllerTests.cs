using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests.Areas
{
    /// <summary>AreaController: the hidden / editing state machine, dragging, arrow keys, patterns and layout (design §6.2).</summary>
    public class AreaControllerTests
    {
        private static readonly PhysicalRect areaRect = new(400, 300, 400, 240);
        private static readonly string[] moveEnd = { "begin-move", "end-drag" };
        private static readonly string[] moveEndExit = { "begin-move", "end-drag", "exit-edit" };

        private static FakeUnderlayCapture Fake(AreaHarness _f) => (FakeUnderlayCapture)_f.Capture;

        /// <summary>A mosaic area without timer captures, so only requested captures are counted.</summary>
        private static AreaController QuietMosaic(AreaHarness _f, PhysicalRect? _rect = null)
        {
            var area = _f.Add(_rect ?? areaRect, PatternKind.Mosaic);
            area.Model.Mosaic.Common.IntervalMs = 0;
            area.SettingsChanged();
            return area;
        }

        private static List<string> Log(AreaController _area)
        {
            var log = new List<string>();
            _area.Transition += log.Add;
            return log;
        }

        [Fact]
        public void Constants_MatchTheDesignCanvas()
        {
            Assert.Equal(8, AreaController.SideMarginDip);
            Assert.Equal(36, AreaController.ToolbarHeightDip);
            Assert.Equal(EditToolbar.HeightDip, AreaController.ToolbarHeightDip);
            Assert.Equal(8, AreaController.ToolbarGapDip);
        }

        [AvaloniaFact]
        public void ToolbarWidthDip_IsWiderOnlyWhileTheCaptureFailed()
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = QuietMosaic(f);
            Assert.False(area.CaptureFailed);
            Assert.Equal(EditToolbar.NormalWidth, area.ToolbarWidthDip);
            capture.Failure = CaptureFailure.Denied;
            area.Refresh();
            Assert.True(area.CaptureFailed);
            Assert.Equal(EditToolbar.WidthWithWarning, area.ToolbarWidthDip);
        }

        [AvaloniaFact]
        public void Time_IsTheServicesClock()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            Assert.Same(f.Time, area.Time);
        }

        [AvaloniaFact]
        public void Constructor_WiresModelWindowHostAndFrames()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            Assert.Same(area.Model, area.Window.PatternView.Model);
            Assert.Same(area.Frames, area.Window.PatternView.Frames);
            Assert.Same(f.Hosts.Single(), area.Host);
            Assert.False(area.IsDragging);
            Assert.False(area.CaptureFailed);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic, 1)]
        [InlineData(PatternKind.Fill, 0)]
        [InlineData(PatternKind.Generated, 0)]
        public void Show_PlacesAndShowsTheWindowHidden_CapturingOnlyForMosaic(PatternKind _pattern, int _captures)
        {
            using var f = new AreaHarness();
            AreasFile.Save(f.AreasPath, new AppState
            {
                Areas = { new AreaState { Id = "0f3b8d2e", X = 100, Y = 120, Width = 300, Height = 200, Pattern = _pattern } },
            });
            f.Manager.Restore();
            var area = f.Manager.Areas.Single();
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.True(area.Window.IsVisible);
            Assert.False(area.Window.IsActive); // FR-11: showing takes no focus
            Assert.Equal(new PhysicalRect(100, 120, 300, 200), f.Hosts.Single().Last);
            Assert.Equal(_captures, Fake(f).Calls);
        }

        /// <summary>The timer's first tick comes exactly one interval from now (the area was just shown), not before.</summary>
        private static void AssertFirstTickAfter(AreaHarness _f, int _intervalMs)
        {
            int calls = Fake(_f).Calls;
            _f.Time.Advance(TimeSpan.FromMilliseconds(_intervalMs - 1));
            Assert.Equal(calls, Fake(_f).Calls);
            _f.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(calls + 1, Fake(_f).Calls);
            _f.Time.Advance(TimeSpan.FromMilliseconds(_intervalMs));
            Assert.Equal(calls + 2, Fake(_f).Calls);
        }

        [AvaloniaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void Show_NewArea_StartsTheTimerAtTheOsDefaultInterval(bool _isWindows)
        {
            // FR-51: the periodic refresh starts when the area is shown, without a settings change.
            using var f = new AreaHarness(_isWindows: _isWindows);
            var area = f.Manager.AddNew();
            int interval = AreaDefaults.IntervalMs(_isWindows);
            Assert.Equal(_isWindows ? 100 : 300000, interval);
            Assert.Equal(interval, area.Model.Mosaic.Common.IntervalMs);
            Assert.Equal(1, Fake(f).Calls); // the creation capture
            AssertFirstTickAfter(f, interval);
        }

        [AvaloniaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void Show_RestoredMosaicArea_StartsTheTimerAtItsStoredInterval(bool _isWindows)
        {
            using var f = new AreaHarness(_isWindows: _isWindows);
            int interval = AreaDefaults.IntervalMs(_isWindows);
            var stored = new AreaState { Id = "0f3b8d2e", X = 100, Y = 120, Width = 300, Height = 200, Pattern = PatternKind.Mosaic };
            stored.Mosaic.Common.IntervalMs = interval;
            AreasFile.Save(f.AreasPath, new AppState { Areas = { stored } });
            f.Manager.Restore();
            var area = f.Manager.Areas.Single();
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal(interval, area.Model.Mosaic.Common.IntervalMs);
            Assert.Equal(1, Fake(f).Calls); // the creation capture
            AssertFirstTickAfter(f, interval);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic, 1)]
        [InlineData(PatternKind.Fill, 0)]
        [InlineData(PatternKind.Generated, 0)]
        public void Refresh_CapturesOnlyForMosaic(PatternKind _pattern, int _captures)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, _pattern);
            area.Model.Mosaic.Common.IntervalMs = 0;
            area.SettingsChanged();
            int calls = Fake(f).Calls;
            area.Refresh();
            Assert.Equal(calls + _captures, Fake(f).Calls);
        }

        [AvaloniaFact]
        public void SettingsChanged_TakesTheNewIntervalFromNow_WithoutCapturing()
        {
            using var f = new AreaHarness();
            var area = QuietMosaic(f);
            int calls = Fake(f).Calls;
            f.Time.Advance(TimeSpan.FromHours(1));
            Assert.Equal(calls, Fake(f).Calls); // 0 = no timer (FR-51)

            area.Model.Mosaic.Common.IntervalMs = 1000;
            area.SettingsChanged();
            Assert.Equal(calls, Fake(f).Calls); // D-10: from the next timer tick
            f.Time.Advance(TimeSpan.FromMilliseconds(999));
            Assert.Equal(calls, Fake(f).Calls);
            f.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(calls + 1, Fake(f).Calls);
            f.Time.Advance(TimeSpan.FromMilliseconds(1000));
            Assert.Equal(calls + 2, Fake(f).Calls);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill)]
        [InlineData(PatternKind.Generated)]
        public void SettingsChanged_NonMosaic_HasNoTimer(PatternKind _pattern)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, _pattern);
            area.Model.Mosaic.Common.IntervalMs = 100;
            area.SettingsChanged();
            int calls = Fake(f).Calls;
            f.Time.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(calls, Fake(f).Calls);
        }

        [AvaloniaFact]
        public void SettingsChanged_RedrawsWithTheNewValues()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Fill);
            area.ExitEdit();
            area.Model.Opacity = 0.5;
            Assert.Equal(1.0, area.Window.Opacity, 6);
            area.SettingsChanged();
            Assert.Equal(0.5, area.Window.Opacity, 6);
        }

        [AvaloniaFact]
        public void SaveSettings_NowWritesAtOnce_OtherwiseAfterTheDebounce()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            int saves = f.Manager.SaveCount;
            area.SaveSettings(true);
            Assert.Equal(saves + 1, f.Manager.SaveCount);

            area.SaveSettings(false);
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            f.Time.Advance(AreaManager.SaveDebounce - TimeSpan.FromTicks(1));
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            f.Time.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(saves + 2, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void EnterEdit_WhenAlreadyEditing_DoesNothing()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var log = Log(area);
            int changed = 0;
            f.Manager.Changed += () => changed++;
            int placements = f.Hosts[0].Placements.Count;
            area.EnterEdit();
            Assert.Empty(log);
            Assert.Equal(0, changed);
            Assert.Equal(placements, f.Hosts[0].Placements.Count);
        }

        [AvaloniaFact]
        public void EnterEdit_FromHidden_LaysOutActivatesAndTellsTheOwner()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            var log = Log(area);
            int changed = 0;
            f.Manager.Changed += () => changed++;
            int saves = f.Manager.SaveCount;
            area.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(AreaMode.Editing, area.Mode);
            Assert.Equal("enter-edit", Assert.Single(log));
            Assert.Equal(1, changed);
            Assert.True(area.Window.IsActive);
            Assert.True(area.Window.ChromeAttached);
            Assert.NotEqual(areaRect, f.Hosts[0].Last);
            Assert.Equal(saves, f.Manager.SaveCount); // entering edit mode saves nothing
        }

        [AvaloniaFact]
        public void EnterEdit_WindowNotVisible_IsNotActivated()
        {
            using var f = new AreaHarness();
            AreasFile.Save(f.AreasPath, new AppState { Areas = { new AreaState { Id = "0f3b8d2e", X = 100, Y = 100, Width = 300, Height = 200, Pattern = PatternKind.Fill } } });
            f.Manager.Restore();
            var area = f.Manager.Areas.Single();
            area.Window.Hide();
            area.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(AreaMode.Editing, area.Mode);
            Assert.False(area.Window.IsVisible);
            Assert.False(area.Window.IsActive);
        }

        [AvaloniaFact]
        public void ExitEdit_WhenHidden_DoesNothing()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            var log = Log(area);
            int saves = f.Manager.SaveCount;
            int placements = f.Hosts[0].Placements.Count;
            area.ExitEdit();
            Assert.Empty(log);
            Assert.Equal(saves, f.Manager.SaveCount);
            Assert.Equal(placements, f.Hosts[0].Placements.Count);
        }

        [AvaloniaFact]
        public void ExitEdit_PlacesTheAreaItself_SavesAndTellsTheOwner()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var log = Log(area);
            int changed = 0;
            f.Manager.Changed += () => changed++;
            int saves = f.Manager.SaveCount;
            area.ExitEdit();
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal("exit-edit", Assert.Single(log));
            Assert.Equal(1, changed);
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Equal(areaRect, f.Hosts[0].Last);
            Assert.Equal(areaRect, area.WindowRect);
            Assert.False(area.Window.ChromeAttached);
        }

        [AvaloniaFact]
        public void ExitEdit_WhileDragging_EndsTheDragFirst()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var log = Log(area);
            int saves = f.Manager.SaveCount;
            area.BeginDrag(ResizeHandle.None, new PixelPoint(0, 0));
            area.DragTo(new PixelPoint(20, 10));
            area.ExitEdit();
            Assert.False(area.IsDragging);
            Assert.Equal(moveEndExit, log);
            Assert.Equal(saves + 2, f.Manager.SaveCount); // the drag's end, then leaving edit mode
            Assert.Equal(areaRect.Offset(20, 10), AreasFile.Load(f.AreasPath, out _).Areas.Single().ToArea(1.0).Rect);
        }

        [AvaloniaFact]
        public void Drag_WhenHidden_IsIgnored()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.ExitEdit();
            var log = Log(area);
            int saves = f.Manager.SaveCount;
            area.BeginDrag(ResizeHandle.None, new PixelPoint(0, 0));
            Assert.False(area.IsDragging);
            area.DragTo(new PixelPoint(50, 50));
            area.EndDrag();
            Assert.Equal(areaRect, area.Model.Rect);
            Assert.Empty(log);
            Assert.Equal(saves, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void DragTo_AndEndDrag_WithoutABeginDrag_DoNothing()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var log = Log(area);
            int saves = f.Manager.SaveCount;
            area.DragTo(new PixelPoint(50, 50));
            area.EndDrag();
            Assert.Equal(areaRect, area.Model.Rect);
            Assert.Empty(log);
            Assert.Equal(saves, f.Manager.SaveCount);
        }

        [AvaloniaTheory]
        [InlineData(ResizeHandle.None, "begin-move")]
        [InlineData(ResizeHandle.Left, "begin-resize:Left")]
        [InlineData(ResizeHandle.Top, "begin-resize:Top")]
        [InlineData(ResizeHandle.Right, "begin-resize:Right")]
        [InlineData(ResizeHandle.Bottom, "begin-resize:Bottom")]
        [InlineData(ResizeHandle.TopLeft, "begin-resize:TopLeft")]
        [InlineData(ResizeHandle.TopRight, "begin-resize:TopRight")]
        [InlineData(ResizeHandle.BottomLeft, "begin-resize:BottomLeft")]
        [InlineData(ResizeHandle.BottomRight, "begin-resize:BottomRight")]
        public void BeginDrag_NamesTheMoveOrTheHandle(ResizeHandle _handle, string _transition)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var log = Log(area);
            area.BeginDrag(_handle, new PixelPoint(10, 10));
            Assert.True(area.IsDragging);
            Assert.Equal(new[] { _transition }, log);
        }

        [AvaloniaFact]
        public void DragTo_FollowsTheStartOfTheDrag_NotThePreviousPoint()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.BeginDrag(ResizeHandle.None, new PixelPoint(100, 100));
            area.DragTo(new PixelPoint(130, 90));
            Assert.Equal(areaRect.Offset(30, -10), area.Model.Rect);
            area.DragTo(new PixelPoint(110, 120));
            Assert.Equal(areaRect.Offset(10, 20), area.Model.Rect);
            Assert.Equal(area.WindowRect, f.Hosts[0].Last); // the window follows at once
        }

        [AvaloniaFact]
        public void DragTo_SameRectangle_PlacesNothing()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.BeginDrag(ResizeHandle.None, new PixelPoint(100, 100));
            int placements = f.Hosts[0].Placements.Count;
            area.DragTo(new PixelPoint(100, 100));
            Assert.Equal(placements, f.Hosts[0].Placements.Count);
            // A resize stopped at the minimum gives the same rectangle again as well.
            area.EndDrag();
            area.BeginDrag(ResizeHandle.Right, new PixelPoint(0, 0));
            area.DragTo(new PixelPoint(-5000, 0));
            placements = f.Hosts[0].Placements.Count;
            area.DragTo(new PixelPoint(-6000, 0));
            Assert.Equal(placements, f.Hosts[0].Placements.Count);
        }

        [AvaloniaTheory]
        [InlineData(1.0, 24)]
        [InlineData(1.25, 30)]
        [InlineData(1.5, 36)]
        [InlineData(1.75, 42)]
        [InlineData(2.0, 48)]
        public void Resize_StopsAtTheMinimumForTheWindowsScale(double _scale, int _min)
        {
            using var f = new AreaHarness(_scale);
            var area = f.Add(areaRect);
            area.BeginDrag(ResizeHandle.TopLeft, new PixelPoint(0, 0));
            area.DragTo(new PixelPoint(5000, 5000));
            area.EndDrag();
            Assert.Equal(new PhysicalRect(areaRect.Right - _min, areaRect.Bottom - _min, _min, _min), area.Model.Rect);

            area.BeginDrag(ResizeHandle.BottomRight, new PixelPoint(0, 0));
            area.DragTo(new PixelPoint(-5000, -5000));
            area.EndDrag();
            Assert.Equal(new PhysicalRect(areaRect.Right - _min, areaRect.Bottom - _min, _min, _min), area.Model.Rect);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void Move_IsInPhysicalPixels_AtEveryScale(double _scale)
        {
            using var f = new AreaHarness(_scale);
            var area = f.Add(areaRect);
            area.BeginDrag(ResizeHandle.None, new PixelPoint(500, 500));
            area.DragTo(new PixelPoint(537, 489));
            area.EndDrag();
            Assert.Equal(areaRect.Offset(37, -11), area.Model.Rect);
            Assert.Equal(areaRect.Width, area.Model.Rect.Width);
        }

        [AvaloniaFact]
        public void EndDrag_SavesAndCapturesTheNewPlace()
        {
            using var f = new AreaHarness();
            var area = QuietMosaic(f);
            var log = Log(area);
            int saves = f.Manager.SaveCount;
            int calls = Fake(f).Calls;
            area.BeginDrag(ResizeHandle.None, new PixelPoint(0, 0));
            area.DragTo(new PixelPoint(5, 6));
            Assert.Equal(saves, f.Manager.SaveCount); // FR-61: saved when the drag ends
            area.EndDrag();
            Assert.False(area.IsDragging);
            Assert.Equal(moveEnd, log);
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Equal(calls + 1, Fake(f).Calls);
            Assert.Equal(areaRect.Offset(5, 6), Fake(f).Requests[^1].Rect);
        }

        [AvaloniaFact]
        public void Drag_PausesTimerCaptures_WhenTheCaptureIsNotLive()
        {
            // FR-38: a capture that cannot follow the drag (Linux) captures when the drag is committed only.
            var capture = new FakeUnderlayCapture { Traits = CaptureTraits.ExposureFree };
            using var f = new AreaHarness(_capture: capture);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Model.Mosaic.Common.IntervalMs = 1000;
            area.SettingsChanged();
            int calls = capture.Calls;
            area.BeginDrag(ResizeHandle.None, new PixelPoint(0, 0));
            f.Time.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(calls, capture.Calls);
            area.EndDrag();
            Assert.Equal(calls + 1, capture.Calls);
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(calls + 2, capture.Calls); // the timer runs again
        }

        [AvaloniaFact]
        public void Drag_KeepsTimerCaptures_WhenTheCaptureIsLive()
        {
            // FR-38: Windows keeps the mosaic following the content during the drag.
            var capture = new FakeUnderlayCapture { Traits = CaptureTraits.Live | CaptureTraits.ExposureFree };
            using var f = new AreaHarness(_capture: capture);
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Model.Mosaic.Common.IntervalMs = 1000;
            area.SettingsChanged();
            int calls = capture.Calls;
            area.BeginDrag(ResizeHandle.None, new PixelPoint(0, 0));
            f.Time.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(calls + 3, capture.Calls);
            area.EndDrag();
        }

        [AvaloniaTheory]
        [InlineData(-1, 0, false, -1, 0)]
        [InlineData(1, 0, false, 1, 0)]
        [InlineData(0, -1, false, 0, -1)]
        [InlineData(0, 1, false, 0, 1)]
        [InlineData(-1, 0, true, -10, 0)]
        [InlineData(1, 0, true, 10, 0)]
        [InlineData(0, -1, true, 0, -10)]
        [InlineData(0, 1, true, 0, 10)]
        public void Nudge_OnePixel_OrTenWithShift(int _dx, int _dy, bool _shift, int _ex, int _ey)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            area.Nudge(_dx, _dy, _shift);
            Assert.Equal(areaRect.Offset(_ex, _ey), area.Model.Rect);
            Assert.Equal(area.WindowRect, f.Hosts[0].Last);
        }

        [AvaloniaFact]
        public void Nudge_WhenHidden_IsIgnored()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.ExitEdit();
            int saves = f.Manager.SaveCount;
            area.Nudge(1, 0, false);
            Assert.Equal(areaRect, area.Model.Rect);
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(saves, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void Nudge_SavesAndCapturesOnce_AfterTheLastKey()
        {
            // FR-61 / P-7: 500 ms after the last key press.
            using var f = new AreaHarness();
            var area = QuietMosaic(f);
            int saves = f.Manager.SaveCount;
            int calls = Fake(f).Calls;
            for (int i = 0; i < 4; i++)
            {
                area.Nudge(0, 1, false);
                f.Time.Advance(TimeSpan.FromMilliseconds(400));
            }
            Assert.Equal(saves, f.Manager.SaveCount);
            Assert.Equal(calls, Fake(f).Calls);
            f.Time.Advance(TimeSpan.FromMilliseconds(100));
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Equal(calls + 1, Fake(f).Calls);
            Assert.Equal(areaRect.Offset(0, 4), Fake(f).Requests[^1].Rect);
            f.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(calls + 1, Fake(f).Calls);
        }

        [AvaloniaFact]
        public void Nudge_CommitAlreadyPostedWhenADragBegins_LeavesTheCaptureToTheDrag()
        {
            using var f = new AreaHarness(_queuePosts: true);
            var area = QuietMosaic(f);
            f.RunPosts();
            int calls = Fake(f).Calls;
            area.Nudge(1, 0, false);
            f.Time.Advance(AreaManager.SaveDebounce); // the commit is posted, not run yet
            Assert.True(f.PendingPosts > 0);
            area.BeginDrag(ResizeHandle.None, new PixelPoint(0, 0));
            f.RunPosts();
            Assert.Equal(calls, Fake(f).Calls);
            area.DragTo(new PixelPoint(4, 0));
            area.EndDrag();
            f.RunPosts();
            Assert.Equal(calls + 1, Fake(f).Calls);
            Assert.Equal(areaRect.Offset(5, 0), Fake(f).Requests[^1].Rect);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill)]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void SetPattern_Same_DoesNothing(PatternKind _pattern)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, _pattern);
            area.Model.Mosaic.Common.IntervalMs = 0;
            area.SettingsChanged();
            var log = Log(area);
            int saves = f.Manager.SaveCount;
            int calls = Fake(f).Calls;
            area.SetPattern(_pattern);
            Assert.Empty(log);
            Assert.Equal(saves, f.Manager.SaveCount);
            Assert.Equal(calls, Fake(f).Calls);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill, PatternKind.Mosaic, 1)]
        [InlineData(PatternKind.Generated, PatternKind.Mosaic, 1)]
        [InlineData(PatternKind.Mosaic, PatternKind.Fill, 0)]
        [InlineData(PatternKind.Mosaic, PatternKind.Generated, 0)]
        [InlineData(PatternKind.Fill, PatternKind.Generated, 0)]
        [InlineData(PatternKind.Generated, PatternKind.Fill, 0)]
        public void SetPattern_SavesRedrawsAndCapturesOnlyForMosaic(PatternKind _from, PatternKind _to, int _captures)
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, _from);
            area.Model.Mosaic.Common.IntervalMs = 0;
            area.SettingsChanged();
            var log = Log(area);
            int saves = f.Manager.SaveCount;
            int calls = Fake(f).Calls;
            area.SetPattern(_to);
            Assert.Equal(_to, area.Model.Pattern);
            Assert.Equal(new[] { $"pattern:{_to}" }, log);
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Equal(_to, AreasFile.Load(f.AreasPath, out _).Areas.Single().Pattern);
            Assert.Equal(calls + _captures, Fake(f).Calls);
            Assert.Equal(_to == PatternKind.Mosaic, area.Window.Toolbar.RefreshButton.IsVisible);
        }

        [AvaloniaFact]
        public void SetPattern_AwayFromMosaic_StopsTheTimer_AndBackStartsIt()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect, PatternKind.Mosaic);
            area.Model.Mosaic.Common.IntervalMs = 1000;
            area.SettingsChanged();
            area.SetPattern(PatternKind.Fill);
            int calls = Fake(f).Calls;
            f.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(calls, Fake(f).Calls);
            area.SetPattern(PatternKind.Mosaic);
            Assert.Equal(calls + 1, Fake(f).Calls);
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(calls + 2, Fake(f).Calls);
        }

        [AvaloniaFact]
        public void Delete_RemovesTheArea_WithOrWithoutATransitionListener()
        {
            using var f = new AreaHarness();
            var a = f.Add(areaRect);
            var b = f.Add(areaRect.Offset(500, 0));
            var log = Log(a);
            a.Delete();
            Assert.Equal("delete", Assert.Single(log));
            b.Delete();
            Assert.Empty(f.Manager.Areas);
            Assert.All(f.Hosts, _h => Assert.True(_h.Disposed));
            Assert.Empty(AreasFile.Load(f.AreasPath, out _).Areas);
        }

        [AvaloniaFact]
        public void Layout_Hidden_IsTheAreaItself_WithoutAskingForAScale()
        {
            using var f = new AreaHarness(1.5);
            var area = f.Add(areaRect);
            area.ExitEdit();
            var host = f.Hosts.Single();
            host.ScaleQueries.Clear();
            area.Layout();
            Assert.Empty(host.ScaleQueries);
            Assert.Equal(areaRect, area.WindowRect);
            Assert.Equal(areaRect, host.Last);
            Assert.False(area.ToolbarInside);
            Assert.Equal(new PixelPoint(0, 0), area.AreaOffsetInWindow);
        }

        [AvaloniaTheory]
        [InlineData(1.0, 8, 44, 436)]
        [InlineData(1.25, 10, 55, 545)]
        [InlineData(1.5, 12, 66, 654)]
        [InlineData(1.75, 14, 77, 763)]
        [InlineData(2.0, 16, 88, 872)]
        [InlineData(25.0 / 11, 19, 100, 991)] // 44 × 25/11 is 100.00000000000001 in doubles: still 100 px
        public void Layout_Editing_MarginsAndToolbarAtTheScale(double _scale, int _side, int _band, int _toolbar)
        {
            using var f = new AreaHarness(_scale);
            var area = f.Add(areaRect);
            var expected = new PhysicalRect(areaRect.X - _side, areaRect.Y - _band, Math.Max(areaRect.Width + 2 * _side, _side + _toolbar), areaRect.Height + _band + _side);
            Assert.Equal(expected, area.WindowRect);
            Assert.Equal(expected, f.Hosts[0].Last);
            Assert.Equal(new PixelPoint(_side, _band), area.AreaOffsetInWindow);
            Assert.False(area.ToolbarInside);
            // The window and its content in DIP (the one conversion, IAreaWindowHost).
            Assert.Equal(expected.Width / _scale, area.Window.Width, 6);
            Assert.Equal(expected.Height / _scale, area.Window.Height, 6);
            var pattern = area.Window.PatternView;
            Assert.Equal(_side / _scale, Canvas.GetLeft(pattern), 6);
            Assert.Equal(_band / _scale, Canvas.GetTop(pattern), 6);
            Assert.Equal(areaRect.Width / _scale, pattern.Width, 6);
            Assert.Equal(areaRect.Height / _scale, pattern.Height, 6);
            Assert.Equal(_band / _scale - AreaController.ToolbarGapDip - AreaController.ToolbarHeightDip, Canvas.GetTop(area.Window.Toolbar), 6);
            Assert.Equal(_side / _scale, Canvas.GetLeft(area.Window.Toolbar), 6);
            Assert.Equal($"{areaRect.Width} × {areaRect.Height} px", area.Window.Toolbar.SizeText.Text);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void Layout_NarrowArea_TheWindowStillHoldsTheToolbar(double _scale)
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaHarness(_scale, _capture: capture);
            var narrow = new PhysicalRect(400, 300, 60, 60);
            var area = f.Add(narrow, PatternKind.Mosaic);
            int side = (int)Math.Ceiling(8 * _scale - 1e-9);
            Assert.Equal(side + (int)Math.Ceiling(EditToolbar.NormalWidth * _scale - 1e-9), area.WindowRect.Width);
            capture.Failure = CaptureFailure.Unavailable;
            area.Refresh();
            Assert.Equal(side + (int)Math.Ceiling(EditToolbar.WidthWithWarning * _scale - 1e-9), area.WindowRect.Width);
        }

        [AvaloniaTheory]
        [InlineData(1.0, 44, false)]
        [InlineData(1.0, 43, true)]
        [InlineData(1.0, 0, true)]
        [InlineData(1.0, 500, false)]
        [InlineData(1.25, 55, false)] // the band above is 55 px at 125%
        [InlineData(1.25, 54, true)]
        [InlineData(2.0, 88, false)]
        [InlineData(2.0, 87, true)]
        public void Layout_ToolbarGoesInside_OnlyWhenTheBandAboveLeavesTheScreen(double _scale, int _y, bool _inside)
        {
            using var f = new AreaHarness(_scale);
            var rect = new PhysicalRect(400, _y, 400, 240);
            var area = f.Add(rect);
            int side = (int)Math.Ceiling(8 * _scale - 1e-9), band = (int)Math.Ceiling(44 * _scale - 1e-9);
            int top = _inside ? side : band;
            Assert.Equal(_inside, area.ToolbarInside);
            Assert.Equal(new PixelPoint(side, top), area.AreaOffsetInWindow);
            Assert.Equal(_y - top, area.WindowRect.Y);
            Assert.Equal(240 + top + side, area.WindowRect.Height);
            double toolbarTop = Canvas.GetTop(area.Window.Toolbar);
            double patternTop = Canvas.GetTop(area.Window.PatternView);
            Assert.Equal(_inside ? patternTop + 4 : patternTop - 8 - 36, toolbarTop, 6);

            // Leaving edit mode puts the window back on the area itself.
            area.ExitEdit();
            Assert.False(area.ToolbarInside);
            Assert.Equal(new PixelPoint(0, 0), area.AreaOffsetInWindow);
            Assert.Equal(rect, area.WindowRect);
        }

        [AvaloniaFact]
        public void Layout_ToolbarInside_UsesTheTopOfTheAreasOwnScreen()
        {
            // A monitor above the primary one: the band above an area on it leaves that monitor's top, not y = 0.
            var upper = new ScreenInfo(new PhysicalRect(0, -1080, 1920, 1080), new PhysicalRect(0, -1080, 1920, 1040), 1.0, false);
            var screens = new FakeScreenService(FakeScreenService.OneScreen(), upper);
            using var f = new AreaHarness(_screens: screens);
            Assert.True(f.Add(new PhysicalRect(100, -1070, 400, 240)).ToolbarInside);
            Assert.False(f.Add(new PhysicalRect(100, -500, 400, 240)).ToolbarInside);
            Assert.True(f.Add(new PhysicalRect(100, 10, 400, 240)).ToolbarInside);
        }

        [AvaloniaFact]
        public void Layout_NoScreens_KeepsTheToolbarAbove()
        {
            var screens = new FakeScreenService();
            using var f = new AreaHarness(_screens: screens);
            var area = f.Add(new PhysicalRect(400, 0, 400, 240));
            screens.Screens = Array.Empty<ScreenInfo>();
            area.Layout();
            Assert.False(area.ToolbarInside);
            Assert.Equal(-44, area.WindowRect.Y);
        }

        [AvaloniaFact]
        public void Layout_ScaleSearch_StopsAfterEightRounds()
        {
            // A host whose scale keeps growing with the window cannot keep the layout going for ever.
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var host = f.Hosts.Single();
            double grow = 1.0;
            host.ScaleAt = _r => _r == areaRect ? 1.0 : (grow += 0.25);
            host.ScaleQueries.Clear();
            area.Layout();
            Assert.Equal(9, host.ScaleQueries.Count); // the area, then eight window rectangles
            Assert.Equal(areaRect, host.ScaleQueries[0]);
            // Margins for the eighth scale asked for (1.0 + 8 × 0.25 = 3.0).
            Assert.Equal(areaRect.X - 24, area.WindowRect.X);
        }

        [AvaloniaFact]
        public void Layout_ScaleSearch_StopsAtTheFirstScaleThatDoesNotGrow()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var host = f.Hosts.Single();
            host.ScaleQueries.Clear();
            area.Layout();
            Assert.Equal(2, host.ScaleQueries.Count); // the area and its window, both 1.0
        }

        [AvaloniaFact]
        public void DisplayChanged_LaysOutAgainInEditMode()
        {
            using var f = new AreaHarness(1.0);
            var area = f.Add(areaRect);
            var host = f.Hosts.Single();
            host.ScaleAt = _ => 2.0; // the window was moved to a 200 % monitor
            host.RaiseDisplayChanged();
            Assert.Equal(areaRect.X - 16, host.Last.X);
            Assert.Equal(areaRect.Y - 88, host.Last.Y);
        }

        [AvaloniaFact]
        public void DisplayChanged_AfterTheAreaIsGone_DoesNothing()
        {
            using var f = new AreaHarness();
            var area = f.Add(areaRect);
            var host = f.Hosts.Single();
            area.Delete();
            int placements = host.Placements.Count;
            host.RaiseDisplayChanged();
            Assert.Equal(placements, host.Placements.Count);
        }

        [AvaloniaFact]
        public void Dispose_StopsTheArrowKeyCommit_ClosesTheWindowAndReleasesTheFrames()
        {
            using var f = new AreaHarness();
            var area = QuietMosaic(f);
            area.Nudge(1, 0, false);
            int calls = Fake(f).Calls;
            Assert.NotEqual(0, area.Frames.Version);
            area.Dispose();
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(calls, Fake(f).Calls);
            Assert.True(f.Hosts.Single().Disposed);
            Assert.False(area.Window.IsVisible);
            Assert.Null(area.Frames.Read((_front, _) => _front));
            area.Refresh(); // a late request (a tray click racing the delete) captures nothing
            Assert.Equal(calls, Fake(f).Calls);
        }
    }
}
