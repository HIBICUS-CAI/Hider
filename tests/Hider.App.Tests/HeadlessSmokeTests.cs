using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace Hider.App.Tests
{
    public class HeadlessSmokeTests
    {
        [AvaloniaFact]
        public void GT39_HeadlessSkia_RendersPixels()
        {
            var window = new Window { Width = 40, Height = 30, Background = Brushes.Red };
            window.Show();

            using var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Equal(40, frame!.PixelSize.Width);
            Assert.Equal(30, frame.PixelSize.Height);
            // The pixels are really drawn: every one is the window's red background.
            using (var buffer = frame.Lock())
            {
                bool rgba = buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888;
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                for (int y = 0; y < buffer.Size.Height; y++)
                {
                    for (int x = 0; x < buffer.Size.Width; x++)
                    {
                        int i = y * buffer.RowBytes + x * 4;
                        var (r, g, b, a) = rgba ? (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]) : (bytes[i + 2], bytes[i + 1], bytes[i], bytes[i + 3]);
                        Assert.Equal((255, 0, 0, 255), (r, g, b, a));
                    }
                }
            }
            window.Close();
        }

        [AvaloniaFact]
        public void Theme_IsLightWhateverTheOsUses()
        {
            // The colours are fixed light ones (UI canvas): Fluent must not follow a dark OS theme and turn the text white.
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            var text = new TextBlock { Text = "x" };
            var window = new Window { Content = text };
            window.Show();
            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            window.Close();
        }
    }
}
