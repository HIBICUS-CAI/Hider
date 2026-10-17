using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Hider.Platform.Linux;

namespace Hider.App.Tests.Platform
{
    /// <summary>libhidercap keeps one stream per process: the tests of the real library run one at a time.</summary>
    [CollectionDefinition(nameof(HidercapGroup))]
    public sealed class HidercapGroup
    {
    }

    /// <summary>A unix socket pair (listener, client, accepted server end) standing in for a PipeWire remote.</summary>
    [SupportedOSPlatform("linux")]
    public sealed class FakeRemote : IDisposable
    {
        private readonly string path = Path.Combine("/tmp", "hider-hc-" + Guid.NewGuid().ToString("N")[..12] + ".sock");
        private readonly Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        public FakeRemote()
        {
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            client.Connect(new UnixDomainSocketEndPoint(path));
            Server = listener.Accept();
        }

        /// <summary>The remote's end: what the helper writes arrives here; disposing it hangs up.</summary>
        public Socket Server { get; }

        /// <summary>The client end's fd, handed over: the socket object no longer closes it (the helper owns it).</summary>
        public int TakeFd()
        {
            int fd = (int)client.Handle;
            client.SafeHandle.SetHandleAsInvalid();
            return fd;
        }

        /// <summary>Whether the helper closed its end: the remote reads end of stream (after what it was sent).</summary>
        public bool PeerClosed(TimeSpan _within)
        {
            var buffer = new byte[4096];
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < _within)
            {
                if (!Server.Poll(TimeSpan.FromMilliseconds(100), SelectMode.SelectRead))
                {
                    continue;
                }
                if (Server.Receive(buffer) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            Server.Dispose();
            listener.Dispose();
            File.Delete(path);
        }
    }

    // The real libhidercap.so (copied next to the tests) without a PipeWire daemon: the API before start and after stop,
    // a second start, a remote that hangs up or never answers, a bad fd. The C side of the same is native/hidercap/test_hidercap.c.
    [Collection(nameof(HidercapGroup))]
    [SupportedOSPlatform("linux")]
    public sealed class HidercapNativeTests : IDisposable
    {
        private readonly Hidercap helper = new();

        public HidercapNativeTests()
        {
            if (OperatingSystem.IsLinux() && helper.IsAvailable)
            {
                helper.StopStream(); // whatever an earlier test left
            }
        }

        public void Dispose()
        {
            if (helper.IsAvailable)
            {
                helper.StopStream();
            }
        }

        private static HidercapFrame Dirty() => new() { Width = 7, Height = 7, Stride = 7, PtsFromMeta = 7, PtsNs = 7, Sequence = 7 };

        [Fact]
        public void GT93_Constants_MatchTheCHeader()
        {
            Assert.Equal((0, 1, -1, -2, -3, -4), (Hidercap.Ok, Hidercap.BufferTooSmall, Hidercap.Timeout, Hidercap.NotStarted, Hidercap.StreamError, Hidercap.AlreadyStarted));
        }

        [HidercapFact]
        public void GT93_IsAvailable_LoadsTheLibraryNextToTheTests()
        {
            Assert.True(File.Exists(LinuxTestEnvironment.HidercapPath), "libhidercap.so is not next to the tests: make -C native/hidercap before dotnet build");
            Assert.True(helper.IsAvailable);
            Assert.True(new Hidercap().IsAvailable); // loaded once per process, shared by every instance
        }

        [HidercapFact]
        public void GT94_NotStarted_EveryCallIsSafe()
        {
            var frame = Dirty();
            var buffer = new byte[64];
            Assert.Equal(Hidercap.NotStarted, helper.GrabAfter(0, 0, 0, 1, buffer, out frame));
            Assert.Equal(default, frame);
            Assert.Equal(Hidercap.NotStarted, helper.GrabAfter(0, 1000, 0, 1, Span<byte>.Empty, out frame));
            Assert.Equal(0ul, helper.LastSequence());
            helper.StopStream();
            helper.StopStream();
            Assert.Equal(0ul, helper.LastSequence());
        }

        [HidercapFact]
        public void GT94_RemoteHangsUp_GrabReportsAStreamErrorAtOnce()
        {
            using var remote = new FakeRemote();
            remote.Server.Dispose();
            Assert.Equal(Hidercap.Ok, helper.Start(remote.TakeFd(), 42));
            var sw = Stopwatch.StartNew();
            Assert.Equal(Hidercap.StreamError, helper.GrabAfter(0, 20000, 0, int.MaxValue, new byte[16], out var frame));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"waited {sw.Elapsed}");
            Assert.Equal(default, frame);
            // The error stays until stop; then the helper is not started.
            Assert.Equal(Hidercap.StreamError, helper.GrabAfter(0, 0, 0, 1, new byte[16], out _));
            helper.StopStream();
            Assert.Equal(Hidercap.NotStarted, helper.GrabAfter(0, 0, 0, 1, new byte[16], out _));
        }

        [HidercapFact]
        public void GT94_SilentRemote_GrabTimesOut_AndASecondStartIsRefused()
        {
            using var remote = new FakeRemote();
            Assert.Equal(Hidercap.Ok, helper.Start(remote.TakeFd(), 42));

            // A second start is refused and closes the fd it was given (ownership passes also on failure).
            using var other = new FakeRemote();
            Assert.Equal(Hidercap.AlreadyStarted, helper.Start(other.TakeFd(), 43));
            Assert.True(other.PeerClosed(TimeSpan.FromSeconds(10)));

            var sw = Stopwatch.StartNew();
            var frame = Dirty();
            Assert.Equal(Hidercap.Timeout, helper.GrabAfter(0, 200, 0, int.MaxValue, new byte[16], out frame));
            Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(150), $"returned after {sw.Elapsed}");
            Assert.Equal(default, frame); // no frame yet: sequence 0 too
            Assert.Equal(Hidercap.Timeout, helper.GrabAfter(0, 0, 0, 1, Span<byte>.Empty, out _));
            Assert.Equal(Hidercap.Timeout, helper.GrabAfter(long.MinValue, -1, 0, 1, Span<byte>.Empty, out _));
            Assert.Equal(0ul, helper.LastSequence());

            // Stop closes the connection: the remote sees its end of stream.
            helper.StopStream();
            Assert.True(remote.PeerClosed(TimeSpan.FromSeconds(10)));
        }

        [HidercapFact]
        public void GT94_StartAfterStop_Works_Repeatedly()
        {
            for (int i = 0; i < 3; i++)
            {
                using var remote = new FakeRemote();
                Assert.Equal(Hidercap.Ok, helper.Start(remote.TakeFd(), (uint)i));
                Assert.Equal(Hidercap.Timeout, helper.GrabAfter(0, 0, 0, 1, Span<byte>.Empty, out _));
                helper.StopStream();
            }
        }

        [HidercapFact]
        public void GT94_InvalidFd_IsAStreamError_AndLeavesTheHelperStopped()
        {
            Assert.Equal(Hidercap.StreamError, helper.Start(-1, 42));
            Assert.Equal(Hidercap.NotStarted, helper.GrabAfter(0, 0, 0, 1, Span<byte>.Empty, out _));
            using var remote = new FakeRemote();
            Assert.Equal(Hidercap.Ok, helper.Start(remote.TakeFd(), 42));
        }

        [HidercapFact]
        public void GT94_FdThatCannotBePolled_IsAStreamError()
        {
            // /dev/null is no PipeWire socket: the start itself goes through (the connection is made asynchronously), the
            // connection then fails and the grab reports it instead of waiting for its timeout.
            using var devNull = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.ReadWrite);
            int fd = (int)devNull.DangerousGetHandle();
            devNull.SetHandleAsInvalid();
            Assert.Equal(Hidercap.Ok, helper.Start(fd, 42));
            var sw = Stopwatch.StartNew();
            Assert.Equal(Hidercap.StreamError, helper.GrabAfter(0, 20000, 0, 1, Span<byte>.Empty, out _));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"waited {sw.Elapsed}");
        }
    }
}
