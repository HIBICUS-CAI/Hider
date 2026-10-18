using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hider.Core.Model;
using Hider.Platform;
using Hider.Platform.Linux;

namespace Hider.App.Tests.Platform
{
    // X11WindowHost is AvaloniaWindowHost as it is (XWayland, D-1): the client places the window at absolute physical
    // pixels and sizes it in DIP. Avalonia headless stands in for X11.
    [SupportedOSPlatform("linux")]
    public sealed class X11WindowHostTests
    {
        [AvaloniaFact]
        public void GT91_IsAnAvaloniaWindowHost()
        {
            var window = new Window();
            using var host = new X11WindowHost(window);
            Assert.IsAssignableFrom<AvaloniaWindowHost>(host);
            Assert.IsAssignableFrom<IAreaWindowHost>(host);
            window.Close();
        }

        [AvaloniaFact]
        public void GT36_Place_PositionInPhysicalPixels_SizeInDip()
        {
            var window = new Window();
            using var host = new X11WindowHost(window);
            host.Place(new PhysicalRect(-300, 120, 640, 360));
            double scale = host.Scale;
            Assert.Equal(new PixelPoint(-300, 120), window.Position);
            Assert.Equal(AreaGeometry.DipLength(640, scale), window.Width, 6);
            Assert.Equal(AreaGeometry.DipLength(360, scale), window.Height, 6);
            Assert.Equal(host.ScaleFor(new PhysicalRect(-300, 120, 640, 360)), scale);
            window.Close();
        }

        [AvaloniaFact]
        public void GT14_SetTopmost_SetsTheWindowsTopmost()
        {
            var window = new Window();
            using var host = new X11WindowHost(window);
            host.SetTopmost(true);
            Assert.True(window.Topmost);
            host.SetTopmost(false);
            Assert.False(window.Topmost);
            window.Close();
        }

        [AvaloniaFact]
        public void GT65_Dispose_StopsDisplayChanges()
        {
            var window = new Window();
            var host = new X11WindowHost(window);
            int raised = 0;
            host.DisplayChanged += () => raised++;
            host.Dispose();
            host.QueueDisplayChanged(false);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, raised);
            window.Close();
        }
    }
}
