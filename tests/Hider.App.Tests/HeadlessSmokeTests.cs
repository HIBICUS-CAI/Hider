using Avalonia.Controls;
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
            window.Close();
        }
    }
}
