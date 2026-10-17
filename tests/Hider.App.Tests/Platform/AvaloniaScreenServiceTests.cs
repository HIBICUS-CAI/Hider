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

        [AvaloniaFact]
        public void CursorScreen_PointerOnAScreen_IsThatScreensBounds()
        {
            var window = new Window();
            var all = window.Screens.All;
            PixelPoint? pointer = null;
            var service = new AvaloniaScreenService(() => window.Screens, () => pointer);

            foreach (var screen in all)
            {
                pointer = screen.Bounds.Center;
                Assert.Equal(Physical(screen.Bounds), service.CursorScreen);
                pointer = screen.Bounds.TopLeft; // the edges belong to the screen
                Assert.Equal(Physical(screen.Bounds), service.CursorScreen);
            }
            window.Close();
        }

        [AvaloniaFact]
        public void CursorScreen_PointerOffEveryScreen_IsNull()
        {
            var window = new Window();
            var service = new AvaloniaScreenService(() => window.Screens, () => new PixelPoint(-100000, -100000));

            Assert.Null(service.CursorScreen);
            window.Close();
        }

        [AvaloniaFact]
        public void CursorScreen_PointerNotKnownNow_IsNull_WithoutAskingTheScreens()
        {
            // GetCursorPos can fail (a locked session): then the primary screen is used, as without a pointer.
            var window = new Window();
            int calls = 0, asked = 0;
            var service = new AvaloniaScreenService(() =>
            {
                calls++;
                return window.Screens;
            }, () =>
            {
                asked++;
                return null;
            });

            Assert.Null(service.CursorScreen);
            Assert.Equal(0, calls);
            Assert.Equal(1, asked); // asked on every read: the pointer moves
            Assert.Null(service.CursorScreen);
            Assert.Equal(2, asked);
            window.Close();
        }

        [AvaloniaFact]
        public void CursorScreen_NoPointerSource_IsNull_WithoutAskingTheScreens()
        {
            // Linux: XWayland only knows the pointer over X11 windows, so none is asked and new areas go to the primary screen.
            var window = new Window();
            int calls = 0;
            var service = new AvaloniaScreenService(() =>
            {
                calls++;
                return window.Screens;
            }, null);

            Assert.Null(service.CursorScreen);
            Assert.Equal(0, calls);
            window.Close();
        }

        [Fact]
        public void CursorScreen_PointerButNoTopLevel_IsNull()
        {
            var service = new AvaloniaScreenService(() => null, () => new PixelPoint(10, 10));

            Assert.Null(service.CursorScreen);
        }

        [AvaloniaFact(Skip = OsOnly.NotWindowsCursor, SkipWhen = nameof(IsWindows))]
        public void CursorScreen_NotWindows_AsksNoPointer()
        {
            // The public constructor gives no pointer source off Windows.
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

        [AvaloniaFact(Skip = OsOnly.Cursor, SkipUnless = nameof(IsWindows))]
        [SupportedOSPlatform("windows")]
        public void CursorScreen_Windows_IsTheBoundsOfTheScreenAtTheCursor()
        {
            // The public constructor asks GetCursorPos (a desktop session always has a cursor; it does not move here).
            var window = new Window();
            var service = new AvaloniaScreenService(() => window.Screens);

            Assert.True(Hider.Platform.Windows.Win32Cursor.TryGetPosition(out int x, out int y));
            Assert.Equal(new PixelPoint(x, y), Hider.Platform.Windows.Win32Cursor.Position());
            var expected = window.Screens.ScreenFromPoint(new PixelPoint(x, y));

            Assert.Equal(expected is null ? null : Physical(expected.Bounds), service.CursorScreen);
            window.Close();
        }

        [Fact(Skip = OsOnly.Cursor, SkipUnless = nameof(IsWindows))]
        public void CursorScreen_Windows_WithoutATopLevel_IsNull()
        {
            var service = new AvaloniaScreenService(() => null);

            Assert.Null(service.CursorScreen);
        }
    }
}
