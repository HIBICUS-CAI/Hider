using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hider.Core.Model;
using Hider.Platform;

namespace Hider.App.Tests.Platform
{
    /// <summary>
    /// Avalonia's screens cannot be faked (IScreenImpl is not implementable by user code), so these run on the headless
    /// platform's screens.
    /// </summary>
    public class AvaloniaScreenServiceTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        private static PhysicalRect Physical(PixelRect _rect) => new(_rect.X, _rect.Y, _rect.Width, _rect.Height);

        [AvaloniaFact]
        public void Screens_ConvertsEveryScreenToPhysicalInfo_InOrder()
        {
            var window = new Window();
            var service = new AvaloniaScreenService(() => window.Screens);

            var screens = service.Screens;
            var all = window.Screens.All;

            Assert.NotEmpty(all);
            Assert.Equal(all.Count, screens.Count);
            for (int i = 0; i < all.Count; i++)
            {
                Assert.Equal(new ScreenInfo(Physical(all[i].Bounds), Physical(all[i].WorkingArea), all[i].Scaling, all[i].IsPrimary), screens[i]);
            }
            Assert.Contains(screens, _s => _s.IsPrimary);
            window.Close();
        }

        [Fact]
        public void Screens_WithoutATopLevel_IsEmpty()
        {
            var service = new AvaloniaScreenService(() => null);

            Assert.Empty(service.Screens);
        }

        [AvaloniaFact]
        public void Screens_AsksTheSourceOnEveryRead()
        {
            // A top level created later (or a monitor plugged in) shows up: nothing is cached.
            var window = new Window();
            Screens? current = null;
            int calls = 0;
            var service = new AvaloniaScreenService(() =>
            {
                calls++;
                return current;
            });

            Assert.Empty(service.Screens);
            current = window.Screens;
            Assert.Equal(window.Screens.All.Count, service.Screens.Count);
            Assert.Equal(2, calls);
            window.Close();
        }

        [AvaloniaFact(Skip = "Windows 以外専用（Windows ではカーソル位置を OS から取る）", SkipWhen = nameof(IsWindows))]
        public void CursorScreen_NotWindows_IsNull_EvenWithScreens()
        {
            // XWayland only knows the pointer over X11 windows: new areas go to the primary screen instead.
            var window = new Window();
            int calls = 0;
            var service = new AvaloniaScreenService(() =>
            {
                calls++;
                return window.Screens;
            });

            Assert.Null(service.CursorScreen);
            Assert.Equal(0, calls);
            window.Close();
        }

        [AvaloniaFact(Skip = "Windows 専用（GetCursorPos）", SkipUnless = nameof(IsWindows))]
        [SupportedOSPlatform("windows")]
        public void CursorScreen_Windows_IsTheBoundsOfTheScreenAtTheCursor_OrNull()
        {
            var window = new Window();
            var service = new AvaloniaScreenService(() => window.Screens);

            // The pointer position comes from the OS (a desktop session always has one).
            Assert.True(Hider.Platform.Windows.Win32Cursor.TryGetPosition(out int x, out int y));
            var expected = window.Screens.ScreenFromPoint(new PixelPoint(x, y));

            Assert.Equal(expected is null ? null : Physical(expected.Bounds), service.CursorScreen);
            window.Close();
        }

        [Fact(Skip = "Windows 専用（GetCursorPos）", SkipUnless = nameof(IsWindows))]
        public void CursorScreen_Windows_WithoutATopLevel_IsNull()
        {
            var service = new AvaloniaScreenService(() => null);

            Assert.Null(service.CursorScreen);
        }
    }
}
