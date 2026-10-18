using System.Runtime.Versioning;
using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Platform.Linux;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace Hider.App.Tests.Platform
{
    // PortalScreenshotGrabber (stage 1) against a private dbus-daemon and the scriptable portal.
    [SupportedOSPlatform("linux")]
    public sealed class PortalScreenshotGrabberTests : IDisposable
    {
        private static readonly PhysicalRect needed = new(0, 0, 10, 10);
        private readonly string dir = Path.Combine(Path.GetTempPath(), "hider-shot-" + Guid.NewGuid().ToString("N"));
        private PhysicalRect screen = new(0, 0, 1920, 1080);
        private int screenCalls;

        public PortalScreenshotGrabberTests()
        {
            Directory.CreateDirectory(dir);
        }

        public void Dispose()
        {
            Directory.Delete(dir, true);
        }

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private PortalScreenshotGrabber Create(string _address)
        {
            return new PortalScreenshotGrabber(() =>
            {
                screenCalls++;
                return screen;
            }, _address);
        }

        /// <summary>A coordinate PNG (pixel (x, y) encodes x and y) of the given size at <paramref name="_path"/>.</summary>
        internal static void WritePng(string _path, int _width, int _height)
        {
            var source = CoordinateFrame.Create(_width, _height);
            using var bitmap = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul));
            source.Bgra.Span.CopyTo(bitmap.GetPixelSpan());
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(_path, data.ToArray());
        }

        /// <summary>The portal writes a PNG for each screenshot and answers its file URI, as GNOME's portal does.</summary>
        private FakeDesktopPortal.Answer.Respond Shoot(int _width = 64, int _height = 48, uint _code = 0, string _name = "Screenshot.png")
        {
            string path = Path.Combine(dir, _name);
            WritePng(path, _width, _height);
            return new FakeDesktopPortal.Answer.Respond(_code, (ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "uri", new Uri(path).AbsoluteUri));
        }

        private static FakeDesktopPortal.Answer.Respond UriAnswer(string _uri, uint _code = 0)
        {
            return new FakeDesktopPortal.Answer.Respond(_code, (ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "uri", _uri));
        }

        [DbusFact]
        public async Task GT102_GrabAfterAsync_BeforeAnyAnswer_IsNullWithoutAPortalCall()
        {
            using var env = await PortalEnv.StartAsync();
            using var grabber = Create(env.Address);
            Assert.False(grabber.ReadyWithoutPrepare);
            Assert.Null(await grabber.GrabAfterAsync(0, needed, Ct));
            Assert.Empty(env.Portal.Calls);
        }

        [DbusFact]
        public async Task GT102_Prepare_ShootsOnce_ThenGrabsShootEachTime_AndNoFileStays()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => Shoot();
            using var grabber = Create(env.Address);
            Assert.True(await grabber.PrepareAsync(needed, Ct));
            Assert.True(grabber.ReadyWithoutPrepare);
            Assert.Empty(Directory.GetFiles(dir)); // FR-53: read and deleted at once

            // The request as GNOME's portal wants it: no parent window, interactive=false, a handle_token.
            var call = Assert.Single(env.Portal.Calls);
            Assert.Equal(("org.freedesktop.portal.Screenshot", "Screenshot"), (call.Interface, call.Member));
            Assert.Equal("", Assert.Single(call.Arguments));
            Assert.False(call.Options["interactive"].GetBool());
            Assert.NotNull(call.Option("handle_token"));
            Assert.Equal(2, call.Options.Count);

            // Prepared: a second prepare needs no screenshot.
            Assert.True(await grabber.PrepareAsync(needed, Ct));
            Assert.Single(env.Portal.Calls);

            // Each grab is a new screenshot; Covers is the virtual screen at the time of the shot.
            screen = new PhysicalRect(-1920, 0, 3840, 1080);
            var frame = await grabber.GrabAfterAsync(12345, needed, Ct);
            Assert.NotNull(frame);
            Assert.Equal(screen, frame!.Covers);
            Assert.Equal((64, 48, 256), (frame.Frame.Width, frame.Frame.Height, frame.Frame.Stride));
            Assert.Equal((63, 47), CoordinateFrame.Decode(frame.Frame, 63, 47));
            Assert.Equal((5, 9), CoordinateFrame.Decode(frame.Frame, 5, 9));
            Assert.Equal(2, env.Portal.Calls.Count);
            Assert.Equal(2, screenCalls);
            Assert.Empty(Directory.GetFiles(dir));
        }

        [DbusFact]
        public async Task GT102_Shot_UriWithEscapedCharacters_IsReadAndDeleted()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => Shoot(8, 4, 0, "Screenshot from 2026 #1 100%.png");
            using var grabber = Create(env.Address);
            Assert.True(await grabber.PrepareAsync(needed, Ct));
            Assert.Empty(Directory.GetFiles(dir));
        }

        [DbusFact]
        public async Task GT103_Shot_OkWithoutUri_IsNull_AndNotPrepared()
        {
            using var env = await PortalEnv.StartAsync();
            using var grabber = Create(env.Address);
            Assert.False(await grabber.PrepareAsync(needed, Ct));
            Assert.False(grabber.ReadyWithoutPrepare);
            Assert.Null(await grabber.GrabAfterAsync(0, needed, Ct));
            Assert.Single(env.Portal.Calls);
            Assert.Equal(0, screenCalls);
        }

        [DbusTheory]
        [InlineData("https://example.org/Screenshot.png")]
        [InlineData("FILE:///tmp/Screenshot.png")]
        [InlineData("")]
        public async Task GT103_Shot_UriThatIsNotAFileUri_IsNull(string _uri)
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => UriAnswer(_uri);
            using var grabber = Create(env.Address);
            Assert.False(await grabber.PrepareAsync(needed, Ct));
            Assert.False(grabber.ReadyWithoutPrepare);
        }

        [DbusFact]
        public async Task GT103_Shot_UriOfAnotherType_IsNull_AndTheNextShotUsesANewConnection()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => FakeDesktopPortal.Answer.Ok((ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "uri", 7u));
            using var grabber = Create(env.Address);
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                Assert.False(await grabber.PrepareAsync(needed, Ct));
            }
            Assert.Contains("screenshot portal: ", log.ToString(), StringComparison.Ordinal);
            // The error reset the connection: the next request comes from another unique name.
            env.Portal.OnRequest = _ => Shoot();
            Assert.True(await grabber.PrepareAsync(needed, Ct));
            var calls = env.Portal.Calls;
            Assert.Equal(2, calls.Count);
            Assert.NotEqual(calls[0].Sender, calls[1].Sender);
        }

        [DbusFact]
        public async Task GT103_Shot_UnreadablePng_IsNull_AndTheFileIsDeleted()
        {
            using var env = await PortalEnv.StartAsync();
            string path = Path.Combine(dir, "broken.png");
            env.Portal.OnRequest = _ =>
            {
                File.WriteAllText(path, "not a png");
                return UriAnswer(new Uri(path).AbsoluteUri);
            };
            using var grabber = Create(env.Address);
            Assert.False(await grabber.PrepareAsync(needed, Ct));
            Assert.False(File.Exists(path));
            Assert.Equal(0, screenCalls);
        }

        [DbusFact]
        public async Task GT103_Shot_CancelledByTheUser_ThrowsDenied_AndDeletesAFileItGot()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => Shoot(8, 8, 1);
            using var grabber = Create(env.Address);
            await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.PrepareAsync(needed, Ct));
            Assert.Empty(Directory.GetFiles(dir));
            Assert.False(grabber.ReadyWithoutPrepare);
            // Without a file too; the connection is kept (a refusal is no connection error).
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Respond(1);
            await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.PrepareAsync(needed, Ct));
            var calls = env.Portal.Calls;
            Assert.Equal(calls[0].Sender, calls[1].Sender);
        }

        [DbusTheory]
        [InlineData(2u)]
        [InlineData(3u)]
        public async Task GT103_Shot_OtherResponse_IsNull_AndLogged(uint _code)
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => Shoot(8, 8, _code);
            using var grabber = Create(env.Address);
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                Assert.False(await grabber.PrepareAsync(needed, Ct));
            }
            Assert.Contains($"screenshot portal: response {_code}", log.ToString(), StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(dir)); // a file that came with it is deleted too
        }

        [DbusFact]
        public async Task GT103_Shot_NoPortalOnTheBus_IsNull_ThenWorksOnceThePortalIsThere()
        {
            using var env = await PortalEnv.StartAsync(false);
            using var grabber = Create(env.Address);
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                Assert.False(await grabber.PrepareAsync(needed, Ct));
            }
            Assert.Contains("screenshot portal: DBusErrorReplyException", log.ToString(), StringComparison.Ordinal);
            using var portal = await FakeDesktopPortal.StartAsync(env.Address);
            portal.OnRequest = _ => Shoot();
            Assert.True(await grabber.PrepareAsync(needed, Ct));
        }

        [DbusFact]
        public async Task GT103_Shot_Cancelled_PropagatesAndClosesTheRequest()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Silent();
            using var grabber = Create(env.Address);
            using var cts = new CancellationTokenSource();
            env.Portal.Replied = _ => cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grabber.PrepareAsync(needed, cts.Token));
            Assert.Equal(FakeDesktopPortal.RequestInterface, Assert.Single(env.Portal.Closed).Interface);
        }

        [DbusFact]
        public async Task GT103_Shot_FileCannotBeDeleted_IsLogged()
        {
            using var env = await PortalEnv.StartAsync();
            // A path in a directory that does not exist (DirectoryNotFoundException) and a directory (UnauthorizedAccessException).
            string missing = Path.Combine(dir, "gone", "Screenshot.png");
            string directory = Path.Combine(dir, "a-directory");
            Directory.CreateDirectory(directory);
            using var grabber = Create(env.Address);
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                env.Portal.OnRequest = _ => UriAnswer(new Uri(missing).AbsoluteUri);
                Assert.False(await grabber.PrepareAsync(needed, Ct));
                env.Portal.OnRequest = _ => UriAnswer(new Uri(directory).AbsoluteUri);
                Assert.False(await grabber.PrepareAsync(needed, Ct));
            }
            string text = log.ToString();
            Assert.Contains($"could not delete the portal screenshot {missing}", text, StringComparison.Ordinal);
            Assert.Contains($"could not delete the portal screenshot {directory}", text, StringComparison.Ordinal);
            Assert.True(Directory.Exists(directory));
        }

        [DbusFact]
        public async Task GT102_Dispose_ClosesTheConnection_LaterCallsFailQuietly()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => Shoot();
            var grabber = Create(env.Address);
            Assert.True(await grabber.PrepareAsync(needed, Ct));
            grabber.Dispose();
            Assert.True(await grabber.PrepareAsync(needed, Ct)); // already answered: nothing to do
            Assert.Null(await grabber.GrabAfterAsync(0, needed, Ct));
            Assert.Single(env.Portal.Calls);
        }

        [Fact]
        public void GT104_ReadPng_MissingFile_IsNull()
        {
            Assert.Null(PortalScreenshotGrabber.ReadPng(Path.Combine(dir, "none.png")));
        }

        [Fact]
        public void GT104_ReadPng_NotAnImage_IsNull()
        {
            string path = Path.Combine(dir, "text.png");
            File.WriteAllText(path, "hello");
            Assert.Null(PortalScreenshotGrabber.ReadPng(path));
        }

        [Fact]
        public void GT104_ReadPng_TruncatedPng_GivesWhatWasDecoded()
        {
            // A PNG cut off in its image data: Skia reports IncompleteInput and fills what it decoded (the top rows).
            string whole = Path.Combine(dir, "whole.png");
            WritePng(whole, 200, 100);
            byte[] bytes = File.ReadAllBytes(whole);
            string cut = Path.Combine(dir, "cut.png");
            File.WriteAllBytes(cut, bytes[..(bytes.Length * 2 / 3)]);
            var frame = PortalScreenshotGrabber.ReadPng(cut);
            Assert.NotNull(frame);
            Assert.Equal((200, 100), (frame!.Width, frame.Height));
            Assert.Equal((7, 3), CoordinateFrame.Decode(frame, 7, 3));
        }

        [Fact]
        public void GT104_ReadPng_CorruptImageData_IsNull()
        {
            // Valid header and size, garbage compressed data: the decoder fails with an error other than IncompleteInput.
            string whole = Path.Combine(dir, "whole.png");
            WritePng(whole, 64, 64);
            byte[] bytes = File.ReadAllBytes(whole);
            int idat = IndexOf(bytes, "IDAT"u8) + 4;
            for (int i = idat; i < Math.Min(bytes.Length - 16, idat + 64); i++)
            {
                bytes[i] = 0xFF;
            }
            string bad = Path.Combine(dir, "bad.png");
            File.WriteAllBytes(bad, bytes);
            Assert.Null(PortalScreenshotGrabber.ReadPng(bad));
        }

        private static int IndexOf(byte[] _haystack, ReadOnlySpan<byte> _needle)
        {
            return _haystack.AsSpan().IndexOf(_needle);
        }
    }
}
