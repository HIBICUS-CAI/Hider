using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Hider.Core.Model;
using Hider.Platform;

namespace Hider.App.Tests.Platform
{
    public class AvaloniaWindowHostTests
    {
        /// <summary>A host whose screen scale and native step are chosen by the test, as the Win32 host overrides them.</summary>
        private sealed class ProbeHost : AvaloniaWindowHost
        {
            public ProbeHost(Window _window, double? _scale = null, int _extra = 0, double? _dip = null) : base(_window)
            {
                scale = _scale;
                extra = _extra;
                dip = _dip;
            }

            private readonly double? scale;
            private readonly int extra;
            private readonly double? dip;

            public List<PhysicalRect> Native { get; } = new();
            public List<(int Px, double Scale)> LengthsAsked { get; } = new();

            public Window HostedWindow => Window;

            public override double ScaleFor(PhysicalRect _rect) => scale ?? base.ScaleFor(_rect);

            protected override int WindowLength(int _px, double _scale)
            {
                LengthsAsked.Add((_px, _scale));
                return _px + extra;
            }

            protected override double DipLength(int _px, double _scale) => dip ?? base.DipLength(_px, _scale);

            protected override void PlaceNative(PhysicalRect _rect)
            {
                Native.Add(_rect);
            }
        }

        // Avalonia's platform callbacks are hidden from user code (private API in the reference assemblies) but are what the
        // platform calls: they are reached by reflection to raise the events a real DPI or monitor change raises
        // (ITopLevelImpl.ScalingChanged and IScreenImpl.Changed, checked on Avalonia 12.1.4).
        private static void RaiseScalingChanged(Window _window, double _scaling)
        {
            var property = typeof(ITopLevelImpl).GetProperty("ScalingChanged")
                ?? throw new InvalidOperationException("ITopLevelImpl.ScalingChanged was renamed: update RaiseScalingChanged");
            var callback = (Action<double>?)property.GetValue(_window.PlatformImpl);
            Assert.NotNull(callback);
            callback!(_scaling);
        }

        private static Action? ScreensChangedCallback(Window _window)
        {
            object impl = _window.PlatformImpl!.TryGetFeature(typeof(IScreenImpl))
                ?? throw new InvalidOperationException("the headless window no longer has an IScreenImpl feature: update ScreensChangedCallback");
            var property = typeof(IScreenImpl).GetProperty("Changed")
                ?? throw new InvalidOperationException("IScreenImpl.Changed was renamed: update ScreensChangedCallback");
            return (Action?)property.GetValue(impl);
        }

        [AvaloniaFact]
        public void Handle_IsTheWindowsPlatformHandle()
        {
            // Headless windows have a stub handle of 0 (Win32 / X11 give the HWND / window id).
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);

            Assert.Equal(window.TryGetPlatformHandle()!.Handle, host.Handle);
            Assert.Equal(0, host.Handle);
            window.Close();
        }

        [AvaloniaFact]
        public void Scale_BeforePlace_IsTheScaleOfTheScreenAtTheWindowPosition()
        {
            var window = new Window { Position = new PixelPoint(10, 10) };
            using var host = new AvaloniaWindowHost(window);
            var screen = window.Screens.ScreenFromPoint(new PixelPoint(10, 10));

            Assert.NotNull(screen);
            Assert.Equal(screen!.Scaling, host.Scale);
            window.Close();
        }

        [AvaloniaFact]
        public void Scale_BeforePlace_OffEveryScreen_IsThePrimaryScale()
        {
            var window = new Window { Position = new PixelPoint(-100000, -100000) };
            using var host = new AvaloniaWindowHost(window);

            Assert.Null(window.Screens.ScreenFromPoint(new PixelPoint(-100000, -100000)));
            Assert.Equal(window.Screens.Primary!.Scaling, host.Scale);
            window.Close();
        }

        [AvaloniaFact]
        public void ScaleFor_Default_IsTheScreenAtTheTopLeftCorner_OrThePrimary()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);

            Assert.Equal(window.Screens.ScreenFromPoint(new PixelPoint(5, 5))!.Scaling, host.ScaleFor(new PhysicalRect(5, 5, 10, 10)));
            Assert.Equal(window.Screens.Primary!.Scaling, host.ScaleFor(new PhysicalRect(-90000, -90000, 10, 10)));
            window.Close();
        }

        [AvaloniaFact]
        public void Scale_AfterPlace_IsTheScaleItWasPlacedWith()
        {
            var window = new Window();
            using var host = new ProbeHost(window, 2.0);

            host.Place(new PhysicalRect(100, 50, 300, 200));

            Assert.Equal(2.0, host.Scale);
            Assert.Equal(1.0, window.RenderScaling); // not the window's own scaling
            window.Close();
        }

        [AvaloniaFact]
        public void Place_PositionInPhysicalPixels_SizeInDipOfTheTargetScale()
        {
            var window = new Window();
            using var host = new ProbeHost(window, 2.0);

            host.Place(new PhysicalRect(100, 50, 301, 200));

            Assert.Equal(new PixelPoint(100, 50), window.Position);
            Assert.Equal(AreaGeometry.DipLength(301, 2.0), window.Width, 12);
            Assert.Equal(AreaGeometry.DipLength(200, 2.0), window.Height, 12);
            Assert.Equal(new[] { (301, 2.0), (200, 2.0) }, host.LengthsAsked);
            Assert.Equal(new[] { new PhysicalRect(100, 50, 301, 200) }, host.Native);
            window.Close();
        }

        [AvaloniaFact]
        public void Place_DefaultHost_KeepsTheExactLength()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);
            double scale = host.ScaleFor(new PhysicalRect(20, 30, 1, 1));

            host.Place(new PhysicalRect(20, 30, 640, 360));

            Assert.Equal(new PixelPoint(20, 30), window.Position);
            Assert.Equal(AreaGeometry.DipLength(640, scale), window.Width, 12);
            Assert.Equal(AreaGeometry.DipLength(360, scale), window.Height, 12);
            Assert.Equal(scale, host.Scale);
            window.Close();
        }

        [AvaloniaFact]
        public void Place_LongerWindowLength_IsWhatTheNativeStepAndTheDipSizeGet()
        {
            // A platform that cannot keep the exact length makes the window longer, never shorter.
            var window = new Window();
            using var host = new ProbeHost(window, 1.75, 1, 12.5);

            host.Place(new PhysicalRect(-10, -20, 61, 40));

            Assert.Equal(new PhysicalRect(-10, -20, 62, 41), host.Native.Single());
            Assert.Equal(new PixelPoint(-10, -20), window.Position);
            Assert.Equal(12.5, window.Width);
            Assert.Equal(12.5, window.Height);
            window.Close();
        }

        [AvaloniaFact]
        public void SetTopmost_SetsTheWindowsTopmost()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);

            host.SetTopmost(true);
            Assert.True(window.Topmost);
            host.SetTopmost(false);
            Assert.False(window.Topmost);
            window.Close();
        }

        [AvaloniaFact]
        public void Window_IsTheHostedWindow()
        {
            var window = new Window();
            using var host = new ProbeHost(window);

            Assert.Same(window, host.HostedWindow);
            window.Close();
        }

        [AvaloniaFact]
        public void ScalingChange_BeforeAnyPlacement_IsReported()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);
            int raised = 0;
            host.DisplayChanged += () => raised++;

            host.QueueDisplayChanged(true);
            Assert.Equal(0, raised);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, raised);
            window.Close();
        }

        [AvaloniaFact]
        public void ScalingChange_ToAnotherScaleThanThePlacedOne_IsReported()
        {
            var window = new Window();
            using var host = new ProbeHost(window, 2.0);
            int raised = 0;
            host.DisplayChanged += () => raised++;
            host.Place(new PhysicalRect(0, 0, 100, 100));

            host.QueueDisplayChanged(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, raised); // placed at 2.0, the window renders at 1.0
            window.Close();
        }

        [AvaloniaFact]
        public void ScreensChange_IsReportedEvenAtThePlacedScale()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);
            int raised = 0;
            host.DisplayChanged += () => raised++;
            host.Place(new PhysicalRect(0, 0, 100, 100));

            host.QueueDisplayChanged(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, raised);

            // The flags are cleared after each report: a later scaling change at the placed scale reports nothing.
            host.QueueDisplayChanged(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, raised);
            window.Close();
        }

        [AvaloniaFact]
        public void DisplayChange_WithoutSubscribers_DoesNothing()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);

            host.QueueDisplayChanged(false);
            host.QueueDisplayChanged(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1.0, host.Scale);
            window.Close();
        }

        [AvaloniaFact]
        public void WindowScalingChanged_QueuesADisplayChange()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);
            int raised = 0;
            host.DisplayChanged += () => raised++;

            RaiseScalingChanged(window, 1.0);
            Assert.Equal(0, raised); // after the current message, not inside it
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, raised);
            window.Close();
        }

        [AvaloniaFact]
        public void ScreensChanged_QueuesADisplayChange()
        {
            var window = new Window();
            using var host = new AvaloniaWindowHost(window);
            int raised = 0;
            host.DisplayChanged += () => raised++;
            host.Place(new PhysicalRect(0, 0, 100, 100));

            var changed = ScreensChangedCallback(window);
            Assert.NotNull(changed);
            changed!();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, raised);
            window.Close();
        }

        [AvaloniaFact]
        public void Dispose_StopsListening_AndDropsSubscribers()
        {
            var window = new Window();
            var host = new AvaloniaWindowHost(window);
            int raised = 0;
            host.DisplayChanged += () => raised++;
            host.QueueDisplayChanged(false); // already posted: still dropped

            host.Dispose();
            RaiseScalingChanged(window, 1.0);
            ScreensChangedCallback(window)?.Invoke();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, raised);
            window.Close();
        }
    }
}
