using Hider.Core.Tests.Fakes;
using Hider.Platform.Linux;
using SkiaSharp;

namespace Hider.App.Tests.Platform
{
    public class PortalGrabberTests
    {
        [Fact]
        public void ReadPng_GivesPremultipliedBgra()
        {
            string path = Path.Combine(Path.GetTempPath(), $"hider-png-{Guid.NewGuid():N}.png");
            try
            {
                var source = CoordinateFrame.Create(300, 40);
                using (var bitmap = new SKBitmap(new SKImageInfo(300, 40, SKColorType.Bgra8888, SKAlphaType.Premul)))
                {
                    source.Bgra.Span.CopyTo(bitmap.GetPixelSpan());
                    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(path, data.ToArray());
                }
                var frame = PortalScreenshotGrabber.ReadPng(path)!;
                Assert.Equal((300, 40, 1200), (frame.Width, frame.Height, frame.Stride));
                Assert.Equal((299, 39), CoordinateFrame.Decode(frame, 299, 39));
                Assert.Equal((257, 3), CoordinateFrame.Decode(frame, 257, 3));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task NoPortal_ReturnsNullWithoutThrowing()
        {
            // An address nobody listens on stands for "no session bus / no portal" (CI, minimal sessions): FR-52 takes over.
            using var grabber = new PortalScreenshotGrabber(() => new Hider.Core.Model.PhysicalRect(0, 0, 1920, 1080), "unix:path=/nonexistent/hider-test-bus");
            Assert.Null(await grabber.GrabAfterAsync(0, new Hider.Core.Model.PhysicalRect(0, 0, 10, 10), TestContext.Current.CancellationToken));
        }
    }
}
