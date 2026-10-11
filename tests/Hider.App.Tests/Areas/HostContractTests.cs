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
            app.Start(lifetime, path, w => new FakeAreaWindowHost(w, 1.0), false);
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
    }
}
