using Avalonia;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests
{
    /// <summary>An <see cref="AreaManager"/> on fake hosts, a fake clock and a temporary areas.json.</summary>
    internal sealed class AreaFixture : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "hider-gui-" + Guid.NewGuid().ToString("N"));

        public AreaFixture(double _scale = 1.0, FakeScreenService? _screens = null, Func<AreaWindow, IAreaWindowHost>? _hostFactory = null,
            IUnderlayCapture? _capture = null)
        {
            Directory.CreateDirectory(dir);
            Scale = _scale;
            Screens = _screens ?? new FakeScreenService(FakeScreenService.Single(_scale));
            Capture = _capture ?? new FakeUnderlayCapture();
            // Everything inline on the test (UI) thread: captures complete synchronously.
            Manager = new AreaManager(new AreaServices(Screens, _hostFactory ?? (w =>
            {
                var host = new FakeAreaWindowHost(w, _scale);
                Hosts.Add(host);
                return host;
            }), AreasPath, Time, a => a(), true, Capture, c => c()));
        }

        public IUnderlayCapture Capture { get; }

        public string AreasPath => Path.Combine(dir, "areas.json");
        public double Scale { get; }
        public FakeTimeProvider Time { get; } = new();
        public FakeScreenService Screens { get; }
        public List<FakeAreaWindowHost> Hosts { get; } = new();
        public AreaManager Manager { get; }

        /// <summary>A new area (edit mode, FR-14) moved to <paramref name="_rect"/>.</summary>
        public AreaController Add(PhysicalRect _rect, PatternKind _pattern = PatternKind.Fill)
        {
            var area = Manager.AddNew();
            area.Model.Rect = _rect;
            area.Model.Pattern = _pattern;
            area.Layout();
            return area;
        }

        /// <summary>Centre of the area in window coordinates (DIP), for headless pointer input.</summary>
        public static Point AreaCenter(AreaController _area)
        {
            var offset = _area.AreaOffsetInWindow;
            double s = _area.Host.Scale;
            return new Point((offset.X + _area.Model.Rect.Width / 2.0) / s, (offset.Y + _area.Model.Rect.Height / 2.0) / s);
        }

        public void Dispose()
        {
            Manager.Dispose();
            Directory.Delete(dir, true);
        }
    }
}
