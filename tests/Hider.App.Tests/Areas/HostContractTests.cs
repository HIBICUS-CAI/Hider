using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Hider.Areas;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests.Areas
{
    public class HostContractTests
    {
        [AvaloniaFact]
        public void GT01_Startup_NoMainWindowOnlyTray()
        {
            var app = (HiderApp)Avalonia.Application.Current!;
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            string path = Path.Combine(Path.GetTempPath(), "hider-gui-" + Guid.NewGuid().ToString("N"), "areas.json");
            // A fake capture and tray answer: no session bus, portal or restore token of the machine running the tests.
            app.Start(lifetime, path, w => new FakeAreaWindowHost(w, 1.0), false, new FakeUnderlayCapture(), () => Task.FromResult(true));
            try
            {
                Assert.Null(lifetime.MainWindow);
                Assert.Empty(app.Areas!.Areas);
                Assert.NotNull(app.Tray);
                Assert.Single(Avalonia.Controls.TrayIcon.GetIcons(app)!);
            }
            finally
            {
                app.Stop();
                Directory.Delete(Path.GetDirectoryName(path)!, true);
            }
        }

        [AvaloniaFact]
        public void GT36_FakeHostScale2_PhysicalRectAndDipSize()
        {
            using var f = new AreaFixture(2.0);
            var area = f.Add(new PhysicalRect(100, 200, 800, 480));
            area.ExitEdit();
            var host = f.Hosts.Single();
            Assert.Equal(new PhysicalRect(100, 200, 800, 480), host.Last);
            Assert.Equal(400, area.Window.Width, 6);
            Assert.Equal(240, area.Window.Height, 6);
            Assert.Equal(400, area.Window.PatternView.Width, 6);
            var stored = AreaState.FromArea(area.Model, host.Scale);
            Assert.Equal((100, 200, 400.0, 240.0), (stored.X, stored.Y, stored.Width, stored.Height));
        }

        [AvaloniaFact]
        public void GT37_Restore_PlacesFittedRect()
        {
            using var f = new AreaFixture();
            AreasFile.Save(f.AreasPath, new AppState
            {
                Areas =
                {
                    new AreaState { Id = "0f3b8d2e", X = 100, Y = 100, Width = 300, Height = 200, Pattern = PatternKind.Fill },
                    new AreaState { Id = "1a2b3c4d", X = 9000, Y = 9000, Width = 400, Height = 240, Pattern = PatternKind.Fill },
                },
            });
            Assert.Equal(LoadStatus.Loaded, f.Manager.Restore());
            Assert.Equal(new PhysicalRect(100, 100, 300, 200), f.Hosts[0].Last);
            Assert.Equal(Placement.Fit(new PhysicalRect(9000, 9000, 400, 240), new[] { f.Screens.Screens[0].Bounds }, 0), f.Hosts[1].Last);
            Assert.All(f.Manager.Areas, a => Assert.Equal(AreaMode.Hidden, a.Mode));
        }

        [AvaloniaFact]
        public void GT38_SwappingHosts_SameTransitions()
        {
            var fake = Run(w => new FakeAreaWindowHost(w, 1.0));
            var avalonia = Run(w => new AvaloniaWindowHost(w));
            Assert.Equal(fake, avalonia);
            Assert.Contains("enter-edit", fake);
            Assert.Contains("end-drag", fake);
        }

        private static List<string> Run(Func<AreaWindow, IAreaWindowHost> _factory)
        {
            using var f = new AreaFixture(1.0, null, _factory);
            var log = new List<string>();
            var area = f.Manager.AddNew();
            area.Transition += log.Add;
            area.ExitEdit();
            area.EnterEdit();
            area.BeginDrag(ResizeHandle.None, new Avalonia.PixelPoint(0, 0));
            area.DragTo(new Avalonia.PixelPoint(10, 5));
            area.EndDrag();
            area.BeginDrag(ResizeHandle.BottomRight, new Avalonia.PixelPoint(0, 0));
            area.DragTo(new Avalonia.PixelPoint(-1000, -1000));
            area.EndDrag();
            area.SetPattern(PatternKind.Generated);
            area.ExitEdit();
            log.Add(area.Model.Rect.ToString());
            return log;
        }

        [AvaloniaFact]
        public void UnreadableAreasFile_IsNeverOverwritten()
        {
            using var f = new AreaFixture();
            const string original = "{ \"version\": 1, \"areas\": [] }";
            File.WriteAllText(f.AreasPath, original);
            using (new FileStream(f.AreasPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(LoadStatus.Unreadable, f.Manager.Restore());
            }
            Assert.True(f.Manager.SaveBlocked);
            f.Manager.AddNew();
            f.Manager.SaveNow();
            Assert.Equal(original, File.ReadAllText(f.AreasPath));
        }

        [AvaloniaFact]
        public void SaveAndRestore_UseTheSameScale_OnMixedDpi()
        {
            // Primary at 100 %, the area on a 150 % monitor; the fake window host reports 1.0 (as a closed window would).
            var screens = new FakeScreenService(
                new ScreenInfo(new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), 1.0, true),
                new ScreenInfo(new PhysicalRect(1920, 0, 2880, 1620), new PhysicalRect(1920, 0, 2880, 1580), 1.5, false));
            var rect = new PhysicalRect(2000, 100, 600, 300);
            string? json = null;
            for (int round = 0; round < 3; round++)
            {
                using var f = new AreaFixture(1.0, screens);
                if (json is null)
                {
                    f.Add(rect).ExitEdit();
                }
                else
                {
                    // Exit and start again: the area keeps its size.
                    File.WriteAllText(f.AreasPath, json);
                    f.Manager.Restore();
                    Assert.Equal(rect, f.Manager.Areas.Single().Model.Rect);
                }
                f.Manager.SaveNow();
                var stored = AreasFile.Load(f.AreasPath, out _).Areas.Single();
                Assert.Equal((400.0, 200.0), (stored.Width, stored.Height));
                json = File.ReadAllText(f.AreasPath);
            }
        }
    }
}
