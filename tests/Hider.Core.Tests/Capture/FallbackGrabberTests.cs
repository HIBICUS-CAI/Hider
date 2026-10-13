using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Capture
{
    public class FallbackGrabberTests
    {
        private static readonly PhysicalRect needed = new(10, 10, 50, 50);

        [Fact]
        public async Task FirstFrameWins_LaterGrabbersUntouched()
        {
            var log = new List<string>();
            var first = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var second = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using var grabber = new FallbackGrabber(first, second);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            Assert.NotNull(await grabber.GrabAfterAsync(7, needed, TestContext.Current.CancellationToken));
            Assert.Equal((1, 0), (first.Calls, second.Calls));
            Assert.Equal(7, first.NotBefore[0]);
            Assert.Equal(needed, first.Needed[0]);
        }

        [Fact]
        public async Task CT92_NoFrameFromTheFirst_SecondIsAsked()
        {
            // Stage 2 without its helper (or with the area on another monitor) returns null: stage 1 takes over (D-6).
            var log = new List<string>();
            var first = new FakeScreenGrabber(log, null) { Ready = false };
            var second = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using var grabber = new FallbackGrabber(first, second);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            var frame = await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken);
            Assert.NotNull(frame);
            Assert.Equal((1, 1), (first.Calls, second.Calls));
        }

        [Fact]
        public async Task Refusal_FallsThrough_ReportedOnlyWhenNothingWorks()
        {
            var log = new List<string>();
            var deniedFirst = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100)) { Deny = true, Ready = false };
            var working = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using (var grabber = new FallbackGrabber(deniedFirst, working))
            {
                Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
                Assert.NotNull(await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            }

            var empty = new FakeScreenGrabber(log, null) { Ready = false };
            var denied = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100)) { Deny = true };
            using (var grabber = new FallbackGrabber(empty, denied))
            {
                Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            }
            using (var grabber = new FallbackGrabber(new FakeScreenGrabber(log, null) { Ready = false }, new FakeScreenGrabber(log, null)))
            {
                Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
                Assert.Null(await grabber.GrabAfterAsync(0, needed, TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task Prepare_StopsAtTheFirstReadyGrabber()
        {
            // Stage 1 must not open its own dialog when stage 2 is ready; it is prepared when stage 2 is not.
            var log = new List<string>();
            var first = new FakeScreenGrabber(log, null);
            var second = new FakeScreenGrabber(log, null);
            using var grabber = new FallbackGrabber(first, second);
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            Assert.Single(log);
            first.Ready = false;
            log.Clear();
            Assert.True(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            Assert.Equal(2, log.Count);
            first.DenyPrepare = true;
            second.Ready = false;
            await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
            first.DenyPrepare = false;
            first.Ready = false;
            Assert.False(await grabber.PrepareAsync(needed, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Strips_NeverReachAGrabberAfterTheReadyOne()
        {
            // Stage 2 was prepared; when it times out in the middle of a capture, stage 1 (unprepared: it may open its
            // permission dialog) is not asked while the strip is transparent. Without a prepare nothing is asked.
            var log = new List<string>();
            var first = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var second = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using var grabber = new FallbackGrabber(first, second);
            var ct = TestContext.Current.CancellationToken;
            Assert.Null(await grabber.GrabAfterAsync(0, needed, ct));
            Assert.True(await grabber.PrepareAsync(needed, ct));
            first.Frame = null;
            Assert.Null(await grabber.GrabAfterAsync(0, needed, ct));
            Assert.Equal((1, 0), (first.Calls, second.Calls));

            // Stage 1 prepared (stage 2 not ready): stage 2 is still asked first, stage 1 answers.
            first.Ready = false;
            first.Frame = CoordinateFrame.Create(100, 100);
            Assert.True(await grabber.PrepareAsync(needed, ct));
            first.FailOnCall = 2;
            Assert.NotNull(await grabber.GrabAfterAsync(0, needed, ct));
            Assert.Equal((2, 1), (first.Calls, second.Calls));

            // A failed prepare leaves nothing to ask.
            second.Ready = false;
            Assert.False(await grabber.PrepareAsync(needed, ct));
            Assert.Null(await grabber.GrabAfterAsync(0, needed, ct));
            Assert.Equal((2, 1), (first.Calls, second.Calls));
        }

        [Fact]
        public async Task Strips_ReachALaterGrabberThatIsReadyWithoutPrepare()
        {
            // Stage 2 prepared but without a frame in the middle of a capture (nothing on screen changed): a Screenshot portal
            // that already answered stands in; one that never answered (its dialog would open) is not asked.
            var log = new List<string>();
            var first = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var second = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using var grabber = new FallbackGrabber(first, second);
            var ct = TestContext.Current.CancellationToken;
            Assert.True(await grabber.PrepareAsync(needed, ct));
            first.Frame = null;
            Assert.Null(await grabber.GrabAfterAsync(0, needed, ct));
            second.ReadyWithoutPrepare = true;
            Assert.NotNull(await grabber.GrabAfterAsync(0, needed, ct));
            Assert.Equal((2, 1), (first.Calls, second.Calls));
        }
    }
}
