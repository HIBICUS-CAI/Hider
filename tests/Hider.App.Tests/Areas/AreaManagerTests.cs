using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests.Areas
{
    /// <summary>AreaManager: restore, add, delete, edit all, popups, the strip capture's area list and saving (FR-02 .. FR-14, FR-61 .. FR-63).</summary>
    public class AreaManagerTests
    {
        private static AreaState Stored(string _id, int _x, int _y, double _w, double _h, PatternKind _pattern = PatternKind.Fill)
        {
            return new AreaState { Id = _id, X = _x, Y = _y, Width = _w, Height = _h, Pattern = _pattern };
        }

        [Fact]
        public void SaveDebounce_Is500Milliseconds()
        {
            Assert.Equal(TimeSpan.FromMilliseconds(500), AreaManager.SaveDebounce);
        }

        [AvaloniaFact]
        public void Services_AreThoseItWasMadeWith()
        {
            using var f = new AreaHarness();
            Assert.Same(f.Services, f.Manager.Services);
            Assert.Same(f.Screens, f.Manager.Screens);
            Assert.Empty(f.Manager.Areas);
            Assert.False(f.Manager.AnyEditing);
            Assert.False(f.Manager.AnyPopupOpen);
            Assert.False(f.Manager.SaveBlocked);
            Assert.Equal(0, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void Restore_NoFile_IsMissing_EmptyAndStillSaves()
        {
            using var f = new AreaHarness();
            int changed = 0;
            f.Manager.Changed += () => changed++;
            Assert.Equal(LoadStatus.Missing, f.Manager.Restore());
            Assert.Empty(f.Manager.Areas);
            Assert.Equal(1, changed);
            Assert.False(f.Manager.SaveBlocked);
            f.Manager.SaveNow();
            Assert.True(File.Exists(f.AreasPath));
        }

        [AvaloniaFact]
        public void Restore_BrokenFile_IsMovedAside_AndSavingGoesOn()
        {
            using var f = new AreaHarness();
            File.WriteAllText(f.AreasPath, "{ not json");
            Assert.Equal(LoadStatus.Broken, f.Manager.Restore());
            Assert.True(File.Exists(f.AreasPath + ".broken"));
            Assert.False(f.Manager.SaveBlocked);
            f.Manager.AddNew();
            Assert.Single(AreasFile.Load(f.AreasPath, out _).Areas);
        }

        [AvaloniaFact]
        public void Restore_WithoutAChangedListener_StillRestores()
        {
            using var f = new AreaHarness();
            AreasFile.Save(f.AreasPath, new AppState { Areas = { Stored("0f3b8d2e", 100, 100, 300, 200) } });
            Assert.Equal(LoadStatus.Loaded, f.Manager.Restore());
            Assert.Single(f.Manager.Areas);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void Restore_SizeInDipIsTurnedIntoPixelsWithTheScreensScale(double _scale)
        {
            using var f = new AreaHarness(_scale);
            AreasFile.Save(f.AreasPath, new AppState { Areas = { Stored("0f3b8d2e", 100, 120, 300, 200) } });
            f.Manager.Restore();
            var area = f.Manager.Areas.Single();
            Assert.Equal(new PhysicalRect(100, 120, (int)Math.Round(300 * _scale), (int)Math.Round(200 * _scale)), area.Model.Rect);
            Assert.Equal(AreaMode.Hidden, area.Mode);
            Assert.Equal("0f3b8d2e", area.Model.Id);
        }

        [AvaloniaTheory]
        [InlineData(1.0, 24)]
        [InlineData(1.5, 36)]
        [InlineData(2.0, 48)]
        public void Restore_TooSmall_GrowsToTheMinimum(double _scale, int _min)
        {
            using var f = new AreaHarness(_scale);
            AreasFile.Save(f.AreasPath, new AppState { Areas = { Stored("0f3b8d2e", 100, 120, 1, 1) } });
            f.Manager.Restore();
            var rect = f.Manager.Areas.Single().Model.Rect;
            Assert.Equal((100, 120, _min, _min), (rect.X, rect.Y, rect.Width, rect.Height));
        }

        [AvaloniaFact]
        public void Restore_OffEveryScreen_GoesToTheCentreOfThePrimary()
        {
            var secondary = new ScreenInfo(new PhysicalRect(-1920, 0, 1920, 1080), new PhysicalRect(-1920, 0, 1920, 1040), 1.0, false);
            var primary = new ScreenInfo(new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), 1.0, true);
            using var f = new AreaHarness(_screens: new FakeScreenService(secondary, primary));
            AreasFile.Save(f.AreasPath, new AppState { Areas = { Stored("0f3b8d2e", 9000, 9000, 400, 240) } });
            f.Manager.Restore();
            Assert.Equal(new PhysicalRect((1920 - 400) / 2, (1080 - 240) / 2, 400, 240), f.Manager.Areas.Single().Model.Rect);
        }

        [AvaloniaFact]
        public void Restore_NoScreens_KeepsTheStoredPlaceAtScale1()
        {
            var screens = new FakeScreenService { Screens = Array.Empty<ScreenInfo>() };
            using var f = new AreaHarness(_screens: screens);
            AreasFile.Save(f.AreasPath, new AppState { Areas = { Stored("0f3b8d2e", 9000, 9000, 300, 200) } });
            f.Manager.Restore();
            Assert.Equal(new PhysicalRect(9000, 9000, 300, 200), f.Manager.Areas.Single().Model.Rect);
        }

        [AvaloniaFact]
        public void AddNew_CursorOnNoKnownScreen_UsesThePrimary()
        {
            var screens = new FakeScreenService(FakeScreenService.OneScreen()) { CursorScreen = new PhysicalRect(5000, 0, 100, 100) };
            using var f = new AreaHarness(_screens: screens);
            var area = f.Manager.AddNew();
            Assert.Equal(new PhysicalRect((1920 - 400) / 2, (1040 - 240) / 2, 400, 240), area.Model.Rect);
        }

        [AvaloniaFact]
        public void AddNew_CursorUnknown_UsesThePrimary()
        {
            var other = new ScreenInfo(new PhysicalRect(1920, 0, 1920, 1080), new PhysicalRect(1920, 0, 1920, 1040), 1.0, false);
            var primary = new ScreenInfo(new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), 1.0, true);
            using var f = new AreaHarness(_screens: new FakeScreenService(other, primary));
            var area = f.Manager.AddNew();
            Assert.Equal(new PhysicalRect((1920 - 400) / 2, (1040 - 240) / 2, 400, 240), area.Model.Rect);
        }

        [AvaloniaFact]
        public void AddNew_NoScreens_UsesA1080pWorkAreaAtScale1()
        {
            var screens = new FakeScreenService { Screens = Array.Empty<ScreenInfo>() };
            using var f = new AreaHarness(_screens: screens);
            var area = f.Manager.AddNew();
            Assert.Equal(new PhysicalRect((1920 - 400) / 2, (1080 - 240) / 2, 400, 240), area.Model.Rect);
        }

        [AvaloniaTheory]
        [InlineData(1.0, 400, 240)]
        [InlineData(1.25, 500, 300)]
        [InlineData(1.5, 600, 360)]
        [InlineData(2.0, 800, 480)]
        public void AddNew_400x240Dip_AtTheScreensScale(double _scale, int _w, int _h)
        {
            using var f = new AreaHarness(_scale);
            var area = f.Manager.AddNew();
            Assert.Equal((_w, _h), (area.Model.Rect.Width, area.Model.Rect.Height));
        }

        [AvaloniaTheory]
        [InlineData(true, AreaDefaults.IntervalMsWindows)]
        [InlineData(false, AreaDefaults.IntervalMsLinux)]
        public void AddNew_MosaicPixelateInEditMode_WithTheOsInterval_AndEveryDefault(bool _isWindows, int _interval)
        {
            using var f = new AreaHarness(_isWindows: _isWindows);
            int changed = 0;
            f.Manager.Changed += () => changed++;
            var area = f.Manager.AddNew();
            Assert.Equal(AreaMode.Editing, area.Mode);
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
            Assert.Equal(AreaDefaults.MosaicMethod, area.Model.Mosaic.Method);
            Assert.Equal(_interval, area.Model.Mosaic.Common.IntervalMs);
            foreach (var method in MethodRegistry.All)
            {
                var store = method.Kind == MethodKind.Mosaic ? area.Model.Mosaic.Params : area.Model.Generated.Params;
                Assert.True(store.ContainsKey(method.Id), method.Id);
                Assert.All(method.Params, _p => Assert.Equal(_p.Default, store[method.Id][_p.Key]));
            }
            Assert.True(f.Manager.AnyEditing);
            Assert.Equal(1, f.Manager.SaveCount);
            Assert.Equal(2, changed); // once entering edit mode, once for the new area
            Assert.Single(AreasFile.Load(f.AreasPath, out _).Areas);
        }

        [AvaloniaFact]
        public void AddNew_GivesEveryAreaItsOwnId()
        {
            using var f = new AreaHarness();
            var ids = Enumerable.Range(0, 20).Select(_ => f.Manager.AddNew().Model.Id).ToList();
            Assert.Equal(20, ids.Distinct().Count());
        }

        [AvaloniaFact]
        public void EditAll_AndEndEditAll_SwitchEveryArea()
        {
            using var f = new AreaHarness();
            var areas = new[] { f.Manager.AddNew(), f.Manager.AddNew(), f.Manager.AddNew() };
            f.Manager.EndEditAll();
            Assert.All(areas, _a => Assert.Equal(AreaMode.Hidden, _a.Mode));
            Assert.False(f.Manager.AnyEditing);
            areas[1].EnterEdit();
            Assert.True(f.Manager.AnyEditing);
            f.Manager.EditAll();
            Assert.All(areas, _a => Assert.Equal(AreaMode.Editing, _a.Mode));
            int saves = f.Manager.SaveCount;
            f.Manager.EndEditAll();
            Assert.All(areas, _a => Assert.Equal(AreaMode.Hidden, _a.Mode));
            Assert.Equal(saves + 3, f.Manager.SaveCount); // each area saves as it leaves edit mode (FR-31)
        }

        [AvaloniaFact]
        public void EditAll_AndEndEditAll_WithoutAreas_DoNothing()
        {
            using var f = new AreaHarness();
            f.Manager.EditAll();
            f.Manager.EndEditAll();
            Assert.Equal(0, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void DeleteAll_ClosesEveryWindow_AndSavesNothingLeft()
        {
            using var f = new AreaHarness();
            var areas = new[] { f.Manager.AddNew(), f.Manager.AddNew() };
            int changed = 0;
            f.Manager.Changed += () => changed++;
            f.Manager.DeleteAll();
            Assert.Empty(f.Manager.Areas);
            Assert.All(areas, _a => Assert.False(_a.Window.IsVisible));
            Assert.All(f.Hosts, _h => Assert.True(_h.Disposed));
            Assert.Equal(1, changed);
            Assert.Empty(AreasFile.Load(f.AreasPath, out _).Areas);
        }

        [AvaloniaFact]
        public void DeleteAll_DuringALinuxStripCapture_CancelsIt_WithoutFallingBack()
        {
            // FR-05 from the tray while area A's capture is in its strips: the capture is cancelled, and its finish, posted
            // to the UI thread after the controllers are gone, neither falls back (FR-52) nor saves nor captures again.
            var capture = new ScriptedCapture { Traits = CaptureTraits.NeedsStripCooperation };
            using var f = new AreaHarness(_capture: capture, _isWindows: false, _queuePosts: true);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            var b = f.Add(new PhysicalRect(600, 200, 400, 240), PatternKind.Mosaic);
            f.RunPosts(); // the creation captures
            var log = new List<string>();
            a.Transition += log.Add;
            b.Transition += log.Add;
            capture.Next = null;
            a.Refresh();
            f.RunPosts();
            Assert.True(capture.IsWaiting);
            var token = capture.Tokens[^1];
            var strip = a.Window.SetTransparentRowsAsync(0, 8, token);
            Assert.Equal((0, 8), a.Window.TransparentRows);
            int calls = capture.Calls;
            int saves = f.Manager.SaveCount;

            f.Manager.DeleteAll();
            Assert.True(token.IsCancellationRequested);
            Assert.Empty(f.Manager.Areas);
            Assert.All(f.Hosts, _h => Assert.True(_h.Disposed));
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Empty(AreasFile.Load(f.AreasPath, out _).Areas);
            Assert.Equal(1, f.PendingPosts); // the cancelled capture's finish

            f.RunPosts();
            Assert.Empty(log);
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Equal(calls, capture.Calls);
            Assert.All(new[] { a, b }, _a =>
            {
                Assert.Equal(PatternKind.Mosaic, _a.Model.Pattern);
                Assert.False(_a.CaptureFailed);
            });
            UiJobs.Complete(strip);
            Assert.True(strip.IsCanceled);
        }

        [AvaloniaFact]
        public void DeleteAll_WithoutAreasOrListener_SavesAnEmptyList()
        {
            using var f = new AreaHarness();
            f.Manager.DeleteAll();
            Assert.Equal(1, f.Manager.SaveCount);
            Assert.Empty(AreasFile.Load(f.AreasPath, out var status).Areas);
            Assert.Equal(LoadStatus.Loaded, status);
        }

        [AvaloniaFact]
        public void Delete_AnAreaNotInTheList_DoesNothing()
        {
            using var f = new AreaHarness();
            var area = f.Manager.AddNew();
            f.Manager.Delete(area);
            int changed = 0;
            f.Manager.Changed += () => changed++;
            int saves = f.Manager.SaveCount;
            f.Manager.Delete(area); // twice: a tray "delete all" racing the area's own delete
            Assert.Equal(saves, f.Manager.SaveCount);
            Assert.Equal(0, changed);
        }

        [AvaloniaFact]
        public void Delete_OneArea_KeepsTheOthers()
        {
            using var f = new AreaHarness();
            var a = f.Manager.AddNew();
            var b = f.Manager.AddNew();
            int changed = 0;
            f.Manager.Changed += () => changed++;
            f.Manager.Delete(a);
            Assert.Equal(b, Assert.Single(f.Manager.Areas));
            Assert.Equal(1, changed);
            Assert.Equal(b.Model.Id, AreasFile.Load(f.AreasPath, out _).Areas.Single().Id);
        }

        [AvaloniaFact]
        public void HoldCapturesForDialog_CountsAsAPopupUntilDisposed_Once()
        {
            using var f = new AreaHarness();
            var first = f.Manager.HoldCapturesForDialog();
            var second = f.Manager.HoldCapturesForDialog();
            Assert.True(f.Manager.AnyPopupOpen);
            first.Dispose();
            first.Dispose(); // a second dispose does not release the other hold
            Assert.True(f.Manager.AnyPopupOpen);
            second.Dispose();
            Assert.False(f.Manager.AnyPopupOpen);
        }

        [AvaloniaFact]
        public void AnyPopupOpen_SeesEveryAreasPopups()
        {
            using var f = new AreaHarness();
            var a = f.Manager.AddNew();
            var b = f.Manager.AddNew();
            Assert.False(f.Manager.AnyPopupOpen);
            b.Window.ToggleSettings();
            Assert.True(f.Manager.AnyPopupOpen);
            b.Window.ToggleSettings();
            a.Window.Toolbar.PatternMenu.ShowAt(a.Window.Toolbar.PatternButton);
            Assert.True(f.Manager.AnyPopupOpen);
            a.Window.Toolbar.PatternMenu.Hide();
            Assert.False(f.Manager.AnyPopupOpen);
        }

        [AvaloniaFact]
        public void PopupClosed_WhileAnotherIsOpen_HeldCapturesStillWait()
        {
            var capture = new FakeUnderlayCapture { Traits = CaptureTraits.NeedsStripCooperation };
            using var f = new AreaHarness(_capture: capture, _isWindows: false);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            using var dialog = f.Manager.HoldCapturesForDialog();
            a.Window.ToggleSettings();
            int calls = capture.Calls;
            a.Refresh();
            a.Window.ToggleSettings(); // the dialog is still open
            Assert.Equal(calls, capture.Calls);
            dialog.Dispose();
            Assert.Equal(calls + 1, capture.Calls);
        }

        [AvaloniaFact]
        public void ModeChanged_RaisesChanged()
        {
            using var f = new AreaHarness();
            var area = f.Manager.AddNew();
            int changed = 0;
            f.Manager.Changed += () => changed++;
            f.Manager.ModeChanged(area);
            Assert.Equal(1, changed);
        }

        [AvaloniaFact]
        public void ModeChanged_WithoutListener_DoesNothing()
        {
            using var f = new AreaHarness();
            var area = f.Manager.AddNew();
            f.Manager.ModeChanged(area);
            Assert.Single(f.Manager.Areas);
        }

        [AvaloniaFact]
        public void OwnWindows_ListsNativeHandles_SkippingZero()
        {
            using var f = new AreaHarness(_firstHandle: 0x10);
            f.Manager.AddNew();
            f.Manager.AddNew();
            Assert.Equal(new nint[] { 0x10, 0x11 }, f.Manager.OwnWindows);
            using var g = new AreaHarness();
            g.Manager.AddNew();
            Assert.Empty(g.Manager.OwnWindows);
        }

        [AvaloniaFact]
        public void StripState_IsEmptyBeforeTheFirstCapture()
        {
            using var f = new AreaHarness(_isWindows: false);
            Assert.Empty(f.Manager.StripAreas);
            Assert.Equal(default, f.Manager.StripScreen);
            Assert.Empty(f.Manager.StripScreens);
        }

        [AvaloniaFact]
        public void CaptureStarting_OnLinux_RecordsAreasAndTheMonitorUnion()
        {
            var left = new ScreenInfo(new PhysicalRect(-1280, 200, 1280, 1024), new PhysicalRect(-1280, 200, 1280, 1000), 1.0, false);
            var main = new ScreenInfo(new PhysicalRect(0, 0, 2560, 1440), new PhysicalRect(0, 0, 2560, 1400), 1.25, true);
            using var f = new AreaHarness(_screens: new FakeScreenService(left, main), _isWindows: false);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            var b = f.Add(new PhysicalRect(-600, 300, 200, 100), PatternKind.Fill);
            f.Manager.CaptureStarting();
            Assert.Equal(new[] { new StripArea(a.Model.Id, a.Model.Rect, PatternKind.Mosaic), new StripArea(b.Model.Id, b.Model.Rect, PatternKind.Fill) }, f.Manager.StripAreas);
            Assert.Equal(PhysicalRect.FromEdges(-1280, 0, 2560, 1440), f.Manager.StripScreen);
            Assert.Equal(new[] { left, main }, f.Manager.StripScreens);
        }

        [AvaloniaFact]
        public void CaptureStarting_OnLinuxWithoutScreens_HasAnEmptyUnion()
        {
            var screens = new FakeScreenService();
            using var f = new AreaHarness(_screens: screens, _isWindows: false);
            f.Add(new PhysicalRect(100, 200, 400, 240));
            screens.Screens = Array.Empty<ScreenInfo>();
            f.Manager.CaptureStarting();
            Assert.Equal(default, f.Manager.StripScreen);
            Assert.Empty(f.Manager.StripScreens);
            Assert.Single(f.Manager.StripAreas);
        }

        [AvaloniaFact]
        public void CaptureStarting_OnWindows_LeavesTheStripStateAndTheTooltips()
        {
            using var f = new AreaHarness(_isWindows: true);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Fill);
            ToolTip.SetIsOpen(a.Window.Toolbar.SettingsButton, true);
            f.Manager.CaptureStarting();
            Assert.Empty(f.Manager.StripAreas);
            Assert.Equal(default, f.Manager.StripScreen);
            Assert.True(a.Window.Toolbar.AnyTipOpen); // nothing is captured in strips on Windows
            a.Window.Toolbar.CloseTips();
        }

        [AvaloniaFact]
        public void CaptureStarting_OnLinux_ClosesEveryAreasTooltips()
        {
            using var f = new AreaHarness(_isWindows: false);
            var a = f.Add(new PhysicalRect(100, 200, 400, 240));
            var b = f.Add(new PhysicalRect(600, 200, 400, 240));
            ToolTip.SetIsOpen(a.Window.Toolbar.RefreshButton, true);
            ToolTip.SetIsOpen(b.Window.Toolbar.DeleteButton, true);
            f.Manager.CaptureStarting();
            Assert.False(a.Window.Toolbar.AnyTipOpen);
            Assert.False(b.Window.Toolbar.AnyTipOpen);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void SaveNow_StoresPositionInPixelsAndSizeInDip(double _scale)
        {
            using var f = new AreaHarness(_scale);
            var area = f.Add(new PhysicalRect(101, 203, 600, 300));
            area.Model.Opacity = 0.75;
            f.Manager.SaveNow();
            var stored = AreasFile.Load(f.AreasPath, out _).Areas.Single();
            Assert.Equal((101, 203), (stored.X, stored.Y));
            Assert.Equal(600 / _scale, stored.Width, 6);
            Assert.Equal(300 / _scale, stored.Height, 6);
            Assert.Equal(0.75, stored.Opacity, 6);
            Assert.Equal(area.Model.Id, stored.Id);
        }

        [AvaloniaFact]
        public void SaveNow_NoScreens_StoresAtScale1()
        {
            var screens = new FakeScreenService();
            using var f = new AreaHarness(_screens: screens);
            f.Add(new PhysicalRect(100, 100, 300, 200));
            screens.Screens = Array.Empty<ScreenInfo>();
            f.Manager.SaveNow();
            var stored = AreasFile.Load(f.AreasPath, out _).Areas.Single();
            Assert.Equal((300.0, 200.0), (stored.Width, stored.Height));
        }

        [AvaloniaFact]
        public void SaveNow_WriteFails_KeepsRunning_AndTriesAgainNextTime()
        {
            using var f = new AreaHarness();
            f.Add(new PhysicalRect(100, 100, 300, 200));
            int saves = f.Manager.SaveCount;
            File.Delete(f.AreasPath);
            Directory.CreateDirectory(f.AreasPath); // the rename over it fails
            f.Manager.SaveNow();
            Assert.Equal(saves, f.Manager.SaveCount);
            Directory.Delete(f.AreasPath);
            f.Manager.SaveNow();
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Single(AreasFile.Load(f.AreasPath, out _).Areas);
        }

        [AvaloniaFact]
        public void SaveNow_CancelsAPendingDebouncedSave()
        {
            using var f = new AreaHarness();
            f.Add(new PhysicalRect(100, 100, 300, 200));
            f.Manager.SaveSoon();
            f.Manager.SaveNow();
            int saves = f.Manager.SaveCount;
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(saves, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void SaveSoon_WaitsForQuiet_ThenSavesOnce()
        {
            using var f = new AreaHarness();
            f.Add(new PhysicalRect(100, 100, 300, 200));
            int saves = f.Manager.SaveCount;
            f.Manager.SaveSoon();
            f.Time.Advance(TimeSpan.FromMilliseconds(300));
            f.Manager.SaveSoon(); // restarts the wait
            f.Time.Advance(TimeSpan.FromMilliseconds(499));
            Assert.Equal(saves, f.Manager.SaveCount);
            f.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            f.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(saves + 1, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void SaveSoon_RunsThroughTheUiThreadPost()
        {
            using var f = new AreaHarness(_queuePosts: true);
            f.Manager.SaveSoon();
            f.Time.Advance(AreaManager.SaveDebounce);
            Assert.Equal(0, f.Manager.SaveCount);
            f.RunPosts();
            Assert.Equal(1, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void Blocked_SavesNothing_NowOrLater()
        {
            using var f = new AreaHarness();
            File.WriteAllText(f.AreasPath, "{ \"version\": 1, \"areas\": [] }");
            using (new FileStream(f.AreasPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(LoadStatus.Unreadable, f.Manager.Restore());
            }
            f.Manager.SaveSoon();
            f.Time.Advance(TimeSpan.FromSeconds(1));
            f.Manager.Dispose();
            Assert.Equal(0, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void Dispose_SavesClosesEveryWindow_AndIsDoneOnce()
        {
            using var f = new AreaHarness();
            var a = f.Add(new PhysicalRect(100, 100, 300, 200));
            a.Nudge(1, 0, false); // a debounced save is pending
            int saves = f.Manager.SaveCount;
            f.Manager.Dispose();
            Assert.Equal(saves + 1, f.Manager.SaveCount);
            Assert.Empty(f.Manager.Areas);
            Assert.False(a.Window.IsVisible);
            Assert.True(f.Hosts.Single().Disposed);
            Assert.Equal(new PhysicalRect(101, 100, 300, 200), AreasFile.Load(f.AreasPath, out _).Areas.Single().ToArea(1.0).Rect);
            f.Manager.Dispose();
            f.Manager.SaveSoon();
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(saves + 1, f.Manager.SaveCount);
        }
    }
}
