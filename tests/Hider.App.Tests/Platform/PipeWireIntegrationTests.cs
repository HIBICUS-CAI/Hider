using System.Diagnostics;
using System.Runtime.Versioning;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Platform;
using Hider.Platform.Linux;

namespace Hider.App.Tests.Platform
{
    // libhidercap.so against real frames: a private PipeWire and WirePlumber, a GStreamer solid-colour source as the
    // monitor stream, the connection as OpenPipeWireRemote would hand it out. The last test runs stage 2 as the app does:
    // PipeWireGrabber, ScreenCastPortal (a fake portal on a private bus) and the real helper.
    [Collection(nameof(HidercapGroup))]
    [SupportedOSPlatform("linux")]
    public sealed class PipeWireIntegrationTests : IAsyncLifetime
    {
        private const int Width = 64;
        private const int Height = 48;
        private const int FrameBytes = Width * Height * 4;
        private const uint Argb = 0xFF336699; // R 0x33, G 0x66, B 0x99
        private static readonly byte[] bgra = { 0x99, 0x66, 0x33, 0xFF };
        private readonly Hidercap helper = new();
        private PrivatePipeWire? pw;

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public async ValueTask InitializeAsync()
        {
            if (LinuxTestEnvironment.HasPipeWire)
            {
                pw = await PrivatePipeWire.StartAsync(Ct);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (OperatingSystem.IsLinux() && helper.IsAvailable)
            {
                helper.StopStream();
            }
            pw?.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>CLOCK_MONOTONIC ns, the helper's clock (Stopwatch uses it on Linux).</summary>
        private static long NowNs() => (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));

        /// <summary>Starts a source of <paramref name="_format"/> and the helper on a new connection to it.</summary>
        private async Task<uint> StartAsync(string _format = "BGRx")
        {
            uint node = await pw!.AddSourceAsync("hider-test-" + _format, _format, Width, Height, Argb, Ct);
            Connect(node);
            return node;
        }

        private void Connect(uint _node)
        {
            var socket = pw!.Connect();
            int fd = (int)socket.Handle;
            socket.SafeHandle.SetHandleAsInvalid(); // the helper owns the fd
            Assert.Equal(Hidercap.Ok, helper.Start(fd, _node));
        }

        /// <summary>The first frame (the stream may take a moment to start), into a whole-frame buffer.</summary>
        private (int Code, HidercapFrame Frame) First(byte[] _buffer, int _row0 = 0, int _row1 = int.MaxValue)
        {
            int code = helper.GrabAfter(0, 20000, _row0, _row1, _buffer, out var frame);
            return (code, frame);
        }

        /// <summary>
        /// A frame newer than now, once the stream runs. pipewiresink's first buffer on a new link may not be painted yet
        /// (black): like the app's prepare, the first frame only shows that the stream runs; pixels are checked on later ones.
        /// </summary>
        private (int Code, HidercapFrame Frame) Fresh(byte[] _buffer, int _row0 = 0, int _row1 = int.MaxValue)
        {
            Assert.Equal(Hidercap.Ok, First(new byte[FrameBytes]).Code);
            int code = helper.GrabAfter(NowNs(), 20000, _row0, _row1, _buffer, out var frame);
            return (code, frame);
        }

        private static void AssertRows(byte[] _buffer, int _row0, int _row1, byte _fill)
        {
            for (int y = 0; y < Height; y++)
            {
                bool copied = y >= _row0 && y < _row1;
                for (int x = 0; x < Width; x++)
                {
                    var px = _buffer.AsSpan(y * Width * 4 + x * 4, 4);
                    if (copied)
                    {
                        Assert.True(px.SequenceEqual(bgra), $"pixel ({x}, {y}) is {Convert.ToHexString(px)}, not the source colour");
                    }
                    else
                    {
                        Assert.True(px.SequenceEqual(new[] { _fill, _fill, _fill, _fill }), $"pixel ({x}, {y}) outside rows [{_row0}, {_row1}) was written");
                    }
                }
            }
        }

        [PipeWireTheory]
        [InlineData("BGRx")]
        [InlineData("BGRA")]
        [InlineData("RGBx")]
        [InlineData("RGBA")]
        public async Task Grab_EveryFormat_GivesOpaqueBgra(string _format)
        {
            await StartAsync(_format);
            var buffer = new byte[FrameBytes];
            var (code, frame) = Fresh(buffer);
            Assert.Equal(Hidercap.Ok, code);
            Assert.Equal((Width, Height, Width * 4), (frame.Width, frame.Height, frame.Stride));
            AssertRows(buffer, 0, Height, 0);
            Assert.True(frame.Sequence >= 1);
            Assert.True(helper.LastSequence() >= frame.Sequence);
            // GStreamer's PTS is its running time, not CLOCK_MONOTONIC: the receive time is used.
            Assert.Equal(0, frame.PtsFromMeta);
            Assert.InRange(frame.PtsNs, NowNs() - 20_000_000_000L, NowNs());
        }

        [PipeWireFact]
        public async Task Grab_BufferTooSmall_GivesTheSizeAndCopiesNothing()
        {
            await StartAsync();
            Assert.Equal(Hidercap.BufferTooSmall, helper.GrabAfter(0, 20000, 0, int.MaxValue, Span<byte>.Empty, out var frame));
            Assert.Equal((Width, Height, Width * 4), (frame.Width, frame.Height, frame.Stride));
            Assert.True(frame.Sequence >= 1);
            var shortBuffer = new byte[FrameBytes - 1];
            Array.Fill(shortBuffer, (byte)0x11);
            Assert.Equal(Hidercap.BufferTooSmall, helper.GrabAfter(0, 20000, 0, int.MaxValue, shortBuffer, out frame));
            Assert.All(shortBuffer, _b => Assert.Equal(0x11, _b));
            Assert.Equal(Width, frame.Width);
            // Exactly the size is enough.
            Assert.Equal(Hidercap.Ok, helper.GrabAfter(0, 20000, 0, int.MaxValue, new byte[FrameBytes], out _));
        }

        [PipeWireTheory]
        [InlineData(10, 20, 10, 20)]        // the rows asked for
        [InlineData(-5, 3, 0, 3)]           // row0 clamped to the frame
        [InlineData(40, 1000, 40, Height)]  // row1 clamped to the frame
        [InlineData(30, 10, 30, 30)]        // row1 before row0: nothing
        [InlineData(100, 200, 0, 0)]        // below the frame: nothing
        [InlineData(0, int.MaxValue, 0, Height)]
        public async Task Grab_CopiesOnlyTheRowsAskedFor(int _row0, int _row1, int _from, int _to)
        {
            await StartAsync();
            var buffer = new byte[FrameBytes];
            Array.Fill(buffer, (byte)0x11);
            Assert.Equal(Hidercap.Ok, Fresh(buffer, _row0, _row1).Code);
            AssertRows(buffer, _from, _to, 0x11);
        }

        [PipeWireFact]
        public async Task Grab_NotBeforeLaterThanAnyFrame_TimesOut_WithTheLastSequence()
        {
            await StartAsync();
            Assert.Equal(Hidercap.Ok, First(new byte[FrameBytes]).Code);
            var buffer = new byte[FrameBytes];
            var sw = Stopwatch.StartNew();
            Assert.Equal(Hidercap.Timeout, helper.GrabAfter(NowNs() + 3_600_000_000_000L, 300, 0, int.MaxValue, buffer, out var frame));
            Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(250), $"returned after {sw.Elapsed}");
            Assert.True(frame.Sequence >= 1);
            Assert.Equal((0, 0, 0L), (frame.Width, frame.Height, frame.PtsNs));
            Assert.All(buffer, _b => Assert.Equal(0, _b));
        }

        [PipeWireFact]
        public async Task Grab_WaitsForAFrameShownAfterNotBefore()
        {
            // CT-90 end to end: a frame received less than RECEIVE_MARGIN after not_before may predate it; the grab waits
            // for one received later.
            await StartAsync();
            var buffer = new byte[FrameBytes];
            var (_, first) = First(buffer);
            long notBefore = NowNs();
            Assert.Equal(Hidercap.Ok, helper.GrabAfter(notBefore, 20000, 0, int.MaxValue, buffer, out var frame));
            long shown = frame.PtsFromMeta != 0 ? frame.PtsNs : frame.PtsNs - 33_333_333; // frame_shown_at
            Assert.True(shown >= notBefore, $"frame shown at {shown}, not before {notBefore}");
            Assert.True(frame.Sequence > first.Sequence);
            AssertRows(buffer, 0, Height, 0);
        }

        [PipeWireFact]
        public async Task StopAndStartAgain_FramesComeAgain()
        {
            uint node = await StartAsync();
            Assert.Equal(Hidercap.Ok, First(new byte[FrameBytes]).Code);
            helper.StopStream();
            Assert.Equal(0ul, helper.LastSequence());
            Assert.Equal(Hidercap.NotStarted, helper.GrabAfter(0, 0, 0, 1, new byte[FrameBytes], out _));
            Connect(node);
            var buffer = new byte[FrameBytes];
            var (code, frame) = Fresh(buffer);
            Assert.Equal(Hidercap.Ok, code);
            Assert.True(frame.Sequence >= 2, "a new stream: its first frame, then a newer one");
            AssertRows(buffer, 0, Height, 0);
        }

        [PipeWireFact]
        public async Task StartTwice_SecondIsRefused_TheStreamGoesOn()
        {
            uint node = await StartAsync();
            var socket = pw!.Connect();
            int fd = (int)socket.Handle;
            socket.SafeHandle.SetHandleAsInvalid();
            Assert.Equal(Hidercap.AlreadyStarted, helper.Start(fd, node));
            Assert.Equal(Hidercap.Ok, First(new byte[FrameBytes]).Code);
        }

        [PipeWireFact]
        public async Task PipeWireGrabber_WithScreenCastPortalAndTheRealHelper_GrabsTheStream()
        {
            // Stage 2 as the app runs it: the portal hands out the stream (node, monitor rectangle, restore token) and a
            // connection to PipeWire; the grabber starts the helper on it and returns frames newer than each strip.
            uint node = await pw!.AddSourceAsync("hider-test-monitor", "BGRx", Width, Height, Argb, Ct);
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _call => _call.Member switch
            {
                "CreateSession" => ScreenCastPortalTests.SessionCreated(_call),
                "Start" => ScreenCastPortalTests.Started((0, 0), (Width, Height), "t1", node),
                _ => FakeDesktopPortal.Answer.Ok(),
            };
            env.Portal.OnOpenPipeWireRemote = () => pw.Connect().SafeHandle;
            string dir = Path.Combine(Path.GetTempPath(), "hider-e2e-" + Guid.NewGuid().ToString("N"));
            try
            {
                var tokens = new RestoreTokenStore(Path.Combine(dir, "restore_token"));
                var screens = new[] { new ScreenInfo(new PhysicalRect(0, 0, Width, Height), new PhysicalRect(0, 0, Width, Height), 1.0, true) };
                var grabber = new PipeWireGrabber(new ScreenCastPortal(env.Address), helper, tokens, () => screens);
                try
                {
                    var strip = new PhysicalRect(0, 8, Width, 8);
                    Assert.True(await grabber.PrepareAsync(strip, Ct));
                    Assert.True(grabber.IsOpen);
                    Assert.True(grabber.ReadyWithoutPrepare);
                    Assert.Equal("t1", tokens.Load());
                    long notBefore = Stopwatch.GetTimestamp();
                    var frame = await grabber.GrabAfterAsync(notBefore, strip, Ct);
                    Assert.NotNull(frame);
                    Assert.Equal(new PhysicalRect(0, 0, Width, Height), frame!.Covers);
                    Assert.Equal((Width, Height, Width * 4), (frame.Frame.Width, frame.Frame.Height, frame.Frame.Stride));
                    // The strip's rows (plus a row of margin) came through.
                    for (int y = 7; y < 17; y++)
                    {
                        Assert.True(frame.Frame.Bgra.Span.Slice(y * Width * 4, 4).SequenceEqual(bgra), $"row {y}");
                    }
                    // An area off the shared monitor is not this grabber's.
                    Assert.Null(await grabber.GrabAfterAsync(Stopwatch.GetTimestamp(), new PhysicalRect(Width, 0, 10, 10), Ct));
                }
                finally
                {
                    grabber.Dispose();
                }
                // The session was closed at dispose (GNOME's sharing indicator goes away), and the stream stopped.
                Assert.Contains(env.Portal.Closed, _c => _c.Interface == FakeDesktopPortal.SessionInterface);
                Assert.Equal(0ul, helper.LastSequence());
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}
