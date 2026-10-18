using Avalonia;
using Avalonia.Headless.XUnit;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests.Areas
{
    /// <summary>What an AreaController asks of its owner (IAreaOwner), checked with an owner that only records the calls.</summary>
    public class AreaOwnerContractTests
    {
        private static readonly string[] captureCalls = { "capture-starting", "own-windows" };
        private static readonly string[] modeCalls = { "mode:Editing", "mode:Hidden", "save-now" };
        private static readonly string[] nowThenSoon = { "save-now", "save-soon" };
        private static readonly string[] popupCalls = { "popup:True", "popup:False" };
        private static readonly string[] failureCalls = { "capture-starting", "own-windows", "save-now" };

        private sealed class RecordingOwner : IAreaOwner
        {
            public RecordingOwner(IUnderlayCapture _capture)
            {
                Screens = new FakeScreenService();
                Services = new AreaServices(Screens, _w => new FakeAreaWindowHost(_w, 1.0), "unused", Time, _a => _a(), true, _capture, _c => _c());
            }

            public FakeTimeProvider Time { get; } = new();
            public List<string> Calls { get; } = new();
            public List<nint> Windows { get; } = new();
            public bool PopupOpen { get; set; }

            public IScreenService Screens { get; }
            public AreaServices Services { get; }

            public IReadOnlyList<nint> OwnWindows
            {
                get
                {
                    Calls.Add("own-windows");
                    return Windows;
                }
            }

            public bool AnyPopupOpen => PopupOpen;

            public void SaveNow() => Calls.Add("save-now");
            public void SaveSoon() => Calls.Add("save-soon");
            public void Delete(AreaController _area) => Calls.Add($"delete:{_area.Model.Id}");
            public void CaptureStarting() => Calls.Add("capture-starting");
            public void PopupOpenChanged(bool _open) => Calls.Add($"popup:{_open}");
            public void ModeChanged(AreaController _area) => Calls.Add($"mode:{_area.Mode}");
        }

        private static (RecordingOwner Owner, AreaController Area) Create(PatternKind _pattern = PatternKind.Fill, IUnderlayCapture? _capture = null)
        {
            var owner = new RecordingOwner(_capture ?? new FakeUnderlayCapture());
            var model = Area.CreateNew("0f3b8d2e", new PhysicalRect(100, 200, 400, 240), true);
            model.Pattern = _pattern;
            model.Mosaic.Common.IntervalMs = 0;
            var area = new AreaController(model, owner, owner.Services.HostFactory);
            return (owner, area);
        }

        [AvaloniaFact]
        public void GT59_Show_CapturesMosaic_ThroughTheOwner()
        {
            var (owner, area) = Create(PatternKind.Mosaic);
            owner.Windows.Add(7);
            area.Show();
            Assert.Equal(captureCalls, owner.Calls);
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT59_Show_FillCapturesNothing()
        {
            var (owner, area) = Create();
            area.Show();
            Assert.Empty(owner.Calls);
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT10_ModeChanges_AreReported_AndLeavingSaves()
        {
            var (owner, area) = Create();
            area.Show();
            area.EnterEdit();
            area.ExitEdit();
            Assert.Equal(modeCalls, owner.Calls);
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT67_DragEnd_SavesNow_ArrowKeysSaveSoon()
        {
            var (owner, area) = Create();
            area.Show();
            area.EnterEdit();
            owner.Calls.Clear();
            area.BeginDrag(ResizeHandle.None, new PixelPoint(0, 0));
            area.DragTo(new PixelPoint(3, 3));
            area.EndDrag();
            area.Nudge(1, 0, false);
            Assert.Equal(nowThenSoon, owner.Calls);
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT67_SaveSettings_ChoosesNowOrSoon()
        {
            var (owner, area) = Create();
            area.SaveSettings(true);
            area.SaveSettings(false);
            Assert.Equal(nowThenSoon, owner.Calls);
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT70_Delete_AsksTheOwner_WhichDecides()
        {
            var (owner, area) = Create();
            area.Delete();
            Assert.Equal("delete:0f3b8d2e", Assert.Single(owner.Calls));
            Assert.False(area.Host is FakeAreaWindowHost { Disposed: true }); // the owner disposes it, not Delete
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT64_SetPattern_SavesNow()
        {
            var (owner, area) = Create();
            area.SetPattern(PatternKind.Generated);
            Assert.Equal("save-now", Assert.Single(owner.Calls));
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT55_SettingsPopup_OpenAndClose_AreReported()
        {
            var (owner, area) = Create();
            area.Show();
            area.EnterEdit();
            owner.Calls.Clear();
            area.Window.ToggleSettings();
            area.Window.ToggleSettings();
            Assert.Equal(popupCalls, owner.Calls);
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT55_OwnersPopups_HoldAStripCapture_UntilResumed()
        {
            var capture = new FakeUnderlayCapture { Traits = CaptureTraits.NeedsStripCooperation };
            var (owner, area) = Create(PatternKind.Fill, capture);
            area.Show();
            area.SetPattern(PatternKind.Mosaic);
            int calls = capture.Calls;
            owner.PopupOpen = true;
            area.Refresh();
            Assert.Equal(calls, capture.Calls);
            owner.PopupOpen = false;
            area.ResumeAfterPopups();
            Assert.Equal(calls + 1, capture.Calls);
            area.ResumeAfterPopups(); // nothing held any more
            Assert.Equal(calls + 1, capture.Calls);
            area.Dispose();
        }

        [AvaloniaFact]
        public void GT24_Failure_SavesTheFallback()
        {
            var (owner, area) = Create(PatternKind.Mosaic, new FakeUnderlayCapture { Failure = CaptureFailure.Denied });
            area.Show();
            Assert.Equal(failureCalls, owner.Calls);
            Assert.Equal(PatternKind.Generated, area.Model.Pattern);
            area.Dispose();
        }
    }
}
