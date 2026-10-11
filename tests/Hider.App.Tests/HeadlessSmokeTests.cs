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
