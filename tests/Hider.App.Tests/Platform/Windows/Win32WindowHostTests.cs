using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hider.Core.Model;
using Hider.Platform.Windows;

namespace Hider.App.Tests.Platform.Windows
{
    /// <summary>Win32WindowHost on a headless window (no HWND: the one-step SetWindowPos is skipped, the rest runs).</summary>
    [SupportedOSPlatform("windows")]
    public class Win32WindowHostTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        private static double MonitorScale(PhysicalRect _rect)
        {
            var r = new Win32Native.Rect { Left = _rect.X, Top = _rect.Y, Right = _rect.Right, Bottom = _rect.Bottom };
            nint monitor = Win32Native.MonitorFromRect(r, Win32Native.MONITOR_DEFAULTTONEAREST);
            Assert.Equal(0, Win32Native.GetDpiForMonitor(monitor, Win32Native.MDT_EFFECTIVE_DPI, out uint dpi, out _));
            return dpi / 96.0;
        }

        [AvaloniaFact(Skip = "Windows 専用（MonitorFromRect / GetDpiForMonitor）", SkipUnless = nameof(IsWindows))]
        public void ScaleFor_IsTheDpiOfTheMonitorWindowsPicks()
        {
            var window = new Window();
            using var host = new Win32WindowHost(window);

            foreach (var rect in new[] { new PhysicalRect(0, 0, 100, 100), new PhysicalRect(-90000, -90000, 10, 10) })
            {
                Assert.Equal(MonitorScale(rect), host.ScaleFor(rect));
            }
            window.Close();
        }

        [AvaloniaFact(Skip = "Windows 専用（MonitorFromRect / GetDpiForMonitor）", SkipUnless = nameof(IsWindows))]
        public void Place_SizeSurvivesTheWin32RoundTrip()
        {
            // The window takes the shortest length not shorter than the area that Avalonia's WM_SIZE / Resize keep (px / scale).
            var window = new Window();
            using var host = new Win32WindowHost(window);
            var rect = new PhysicalRect(40, 30, 61, 37);
            double scale = MonitorScale(rect);

            host.Place(rect);

            int width = AreaGeometry.StableLength(61, scale), height = AreaGeometry.StableLength(37, scale);
            Assert.Equal(new PixelPoint(40, 30), window.Position);
            Assert.Equal(width / scale, window.Width, 12);
            Assert.Equal(height / scale, window.Height, 12);
            Assert.True(width >= 61 && height >= 37);
            Assert.Equal(scale, host.Scale);
            Assert.Equal(0, host.Handle); // headless: no HWND, so no SetWindowPos
            window.Close();
        }
    }
}
