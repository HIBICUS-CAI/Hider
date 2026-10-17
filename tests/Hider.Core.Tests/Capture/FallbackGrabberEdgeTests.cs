using Hider.Core.Capture;
using Hider.Core.Model;

namespace Hider.Core.Tests.Capture
{
    /// <summary>The branches of <see cref="FallbackGrabber"/> that FallbackGrabberTests does not reach.</summary>
    public class FallbackGrabberEdgeTests
    {
        private static readonly PhysicalRect needed = new(0, 0, 8, 8);
        private static readonly string[] disposeOrder = { "first", "second", "third" };

        private static ScreenFrame Frame()
        {
            return new ScreenFrame(FrameBuffer.Allocate(8, 8), needed);
        }

        [Fact]
        public async Task NoGrabbers_NothingReadyNoFrame()
        {
            using var grabber = new FallbackGrabber();
            Assert.False(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            Assert.Null(await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            Assert.False(((IScreenGrabber)grabber).ReadyWithoutPrepare); // the interface default
        }

        [Fact]
        public async Task WithoutPrepare_OnlyGrabbersReadyWithoutPrepareAreAsked()
        {
            var frame = Frame();
            int askedFirst = 0, askedSecond = 0;
            var first = new ScriptedGrabber { Grab = (_, _, _) => { askedFirst++; return Task.FromResult<ScreenFrame?>(null); } };
            var second = new ScriptedGrabber { Ready = true, Grab = (_, _, _) => { askedSecond++; return Task.FromResult<ScreenFrame?>(frame); } };
            using var grabber = new FallbackGrabber(first, second);
            Assert.Same(frame, await grabber.GrabAfterAsync(5, needed, TestContext.Current.CancellationToken));
            Assert.Equal((0, 1), (askedFirst, askedSecond));
        }

        [Fact]
        public async Task GrabArguments_PassedThrough()
        {
            long seenTimestamp = 0;
            PhysicalRect seenRect = default;
            using var cancel = new CancellationTokenSource();
            CancellationToken seenToken = default;
            var only = new ScriptedGrabber
            {
                Grab = (_t, _r, _c) =>
                {
                    (seenTimestamp, seenRect, seenToken) = (_t, _r, _c);
                    return Task.FromResult<ScreenFrame?>(Frame());
                },
            };
            using var grabber = new FallbackGrabber(only);
            Assert.True(await grabber.PrepareAsync(needed, cancel.Token));
            await grabber.GrabAfterAsync(123456, new PhysicalRect(1, 2, 3, 4), cancel.Token);
            Assert.Equal((123456L, new PhysicalRect(1, 2, 3, 4), cancel.Token), (seenTimestamp, seenRect, seenToken));
        }

        [Fact]
        public async Task GrabRefusals_TheFirstOneIsThrown()
        {
            var firstRefusal = new CaptureDeniedException("first");
            var first = new ScriptedGrabber { Grab = (_, _, _) => Task.FromException<ScreenFrame?>(firstRefusal) };
            var second = new ScriptedGrabber { Grab = (_, _, _) => Task.FromException<ScreenFrame?>(new CaptureDeniedException("second")) };
            using var grabber = new FallbackGrabber(first, second);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            second.Ready = true;
            var e = await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            Assert.Same(firstRefusal, e);
        }

        [Fact]
        public async Task GrabRefusalThenFrame_TheFrameWins()
        {
            var frame = Frame();
            var first = new ScriptedGrabber { Grab = (_, _, _) => Task.FromException<ScreenFrame?>(new CaptureDeniedException("no")) };
            var second = new ScriptedGrabber { Ready = true, Grab = (_, _, _) => Task.FromResult<ScreenFrame?>(frame) };
            using var grabber = new FallbackGrabber(first, second);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            Assert.Same(frame, await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task OtherExceptions_AreNotSwallowed()
        {
            var first = new ScriptedGrabber { Grab = (_, _, _) => Task.FromException<ScreenFrame?>(new IOException("pipe")) };
            var second = new ScriptedGrabber { Ready = true, Grab = (_, _, _) => Task.FromResult<ScreenFrame?>(Frame()) };
            using var grabber = new FallbackGrabber(first, second);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<IOException>(() => grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            var preparing = new FallbackGrabber(new ScriptedGrabber { Prepare = (_, _) => Task.FromException<bool>(new OperationCanceledException()) });
            using (preparing)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => preparing.PrepareAsync(needed, TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task PrepareRefusals_FirstThrownOnlyWhenNoneIsReady()
        {
            var firstRefusal = new CaptureDeniedException("first");
            var deniedFirst = new ScriptedGrabber { Prepare = (_, _) => Task.FromException<bool>(firstRefusal) };
            var deniedSecond = new ScriptedGrabber { Prepare = (_, _) => Task.FromException<bool>(new CaptureDeniedException("second")) };
            using (var grabber = new FallbackGrabber(deniedFirst, deniedSecond))
            {
                Assert.Same(firstRefusal, await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.PrepareAsync(needed, TestContext.Current.CancellationToken)));
            }
            var ready = new ScriptedGrabber();
            using (var grabber = new FallbackGrabber(new ScriptedGrabber { Prepare = (_, _) => Task.FromException<bool>(firstRefusal) }, ready))
            {
                Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken)); // the refusal is not reported
            }
            var notReady = new ScriptedGrabber { Prepare = (_, _) => Task.FromResult(false) };
            using (var grabber = new FallbackGrabber(new ScriptedGrabber { Prepare = (_, _) => Task.FromException<bool>(firstRefusal) }, notReady))
            {
                Assert.Same(firstRefusal, await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.PrepareAsync(needed, TestContext.Current.CancellationToken)));
            }
        }

        [Fact]
        public async Task PrepareArguments_PassedThrough()
        {
            // The token reaches the grabber: a prepare may wait for the portal's dialog and must end on cancellation (NFR-05).
            PhysicalRect seen = default;
            CancellationToken seenToken = default;
            using var cancel = new CancellationTokenSource();
            var only = new ScriptedGrabber { Prepare = (_r, _c) => { (seen, seenToken) = (_r, _c); return Task.FromResult(true); } };
            using var grabber = new FallbackGrabber(only);
            Assert.True(await grabber.PrepareAsync(new PhysicalRect(-5, 6, 7, 8), cancel.Token));
            Assert.Equal((new PhysicalRect(-5, 6, 7, 8), cancel.Token), (seen, seenToken));
        }

        [Fact]
        public async Task PrepareArguments_PassedToEveryGrabberAskedInTurn()
        {
            var seen = new List<(string, PhysicalRect, CancellationToken)>();
            using var cancel = new CancellationTokenSource();
            ScriptedGrabber Recording(string _name, bool _ready) => new()
            {
                Prepare = (_r, _c) =>
                {
                    seen.Add((_name, _r, _c));
                    return _ready ? Task.FromResult(true) : Task.FromException<bool>(new CaptureDeniedException(_name));
                },
            };
            using var grabber = new FallbackGrabber(Recording("first", false), Recording("second", true), Recording("third", true));
            Assert.True(await grabber.PrepareAsync(needed, cancel.Token));
            Assert.Equal(new[] { ("first", needed, cancel.Token), ("second", needed, cancel.Token) }, seen);
        }

        [Fact]
        public async Task Prepare_CancelledWhileWaiting_EndsWithTheCancellation()
        {
            // A prepare that only ends through its token (the portal's dialog left open) ends when the caller cancels.
            using var cancel = new CancellationTokenSource();
            var waiting = new ScriptedGrabber { Prepare = async (_, _c) => { await Task.Delay(Timeout.Infinite, _c); return true; } };
            using var grabber = new FallbackGrabber(waiting);
            var prepare = grabber.PrepareAsync(needed, cancel.Token);
            Assert.False(prepare.IsCompleted);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepare);
        }

        [Fact]
        public async Task ARefusedPrepare_ForgetsTheEarlierReadyGrabber()
        {
            // A prepare that ends in a refusal leaves nothing prepared: strips go to no grabber that needs a prepare.
            int asked = 0;
            bool deny = false;
            var only = new ScriptedGrabber
            {
                Prepare = (_, _) => deny ? Task.FromException<bool>(new CaptureDeniedException("no")) : Task.FromResult(true),
                Grab = (_, _, _) => { asked++; return Task.FromResult<ScreenFrame?>(Frame()); },
            };
            using var grabber = new FallbackGrabber(only);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            Assert.NotNull(await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            deny = true;
            await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            Assert.Null(await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            Assert.Equal(1, asked);
        }

        [Fact]
        public async Task GrabbersCompletingLater_SameOutcome()
        {
            // Real grabbers finish asynchronously (portal answers, PipeWire frames): the fallback resumes after each await.
            var frame = Frame();
            static async Task<T> Later<T>(T _value)
            {
                await Task.Yield();
                return _value;
            }
            static async Task<T> LaterThrow<T>(Exception _e)
            {
                await Task.Yield();
                throw _e;
            }
            var refusing = new ScriptedGrabber
            {
                Prepare = (_, _) => LaterThrow<bool>(new CaptureDeniedException("no")),
                Grab = (_, _, _) => LaterThrow<ScreenFrame?>(new CaptureDeniedException("no")),
            };
            var empty = new ScriptedGrabber { Prepare = (_, _) => Later(false), Grab = (_, _, _) => Later<ScreenFrame?>(null) };
            var working = new ScriptedGrabber { Prepare = (_, _) => Later(true), Grab = (_, _, _) => Later<ScreenFrame?>(frame) };
            using var grabber = new FallbackGrabber(refusing, empty, working);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            refusing.Ready = true;
            Assert.Same(frame, await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void Dispose_EveryGrabberInOrder()
        {
            var log = new List<string>();
            var grabbers = disposeOrder.Select(_n => new ScriptedGrabber { Name = _n, DisposeLog = log }).ToArray();
            var fallback = new FallbackGrabber(grabbers);
            fallback.Dispose();
            Assert.Equal(disposeOrder, log);
            Assert.All(grabbers, _g => Assert.Equal(1, _g.Disposed));
        }
    }
}
