using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Capture
{
    public class FallbackGrabberTests
    {
        private static readonly PhysicalRect Needed = new(10, 10, 50, 50);

        [Fact]
        public async Task FirstFrameWins_LaterGrabbersUntouched()
        {
            var log = new List<string>();
            var first = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var second = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using var grabber = new FallbackGrabber(first, second);
            Assert.NotNull(await grabber.GrabAfterAsync(7, Needed, TestContext.Current.CancellationToken));
            Assert.Equal((1, 0), (first.Calls, second.Calls));
            Assert.Equal(7, first.NotBefore[0]);
            Assert.Equal(Needed, first.Needed[0]);
        }

        [Fact]
        public async Task CT92_NoFrameFromTheFirst_SecondIsAsked()
        {
            // Stage 2 without its helper (or with the area on another monitor) returns null: stage 1 takes over (D-6).
            var log = new List<string>();
            var first = new FakeScreenGrabber(log, null);
            var second = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using var grabber = new FallbackGrabber(first, second);
            var frame = await grabber.GrabAfterAsync(0, Needed, TestContext.Current.CancellationToken);
            Assert.NotNull(frame);
            Assert.Equal((1, 1), (first.Calls, second.Calls));
        }

        [Fact]
        public async Task Refusal_FallsThrough_ReportedOnlyWhenNothingWorks()
        {
            var log = new List<string>();
            var deniedFirst = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100)) { Deny = true };
            var working = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            using (var grabber = new FallbackGrabber(deniedFirst, working))
            {
                Assert.NotNull(await grabber.GrabAfterAsync(0, Needed, TestContext.Current.CancellationToken));
            }

            var empty = new FakeScreenGrabber(log, null);
            var denied = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100)) { Deny = true };
            using (var grabber = new FallbackGrabber(empty, denied))
            {
                await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.GrabAfterAsync(0, Needed, TestContext.Current.CancellationToken));
            }
            using (var grabber = new FallbackGrabber(new FakeScreenGrabber(log, null), new FakeScreenGrabber(log, null)))
            {
                Assert.Null(await grabber.GrabAfterAsync(0, Needed, TestContext.Current.CancellationToken));
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
            Assert.True(await grabber.PrepareAsync(Needed, TestContext.Current.CancellationToken));
            Assert.Single(log);
            first.Ready = false;
            log.Clear();
            Assert.True(await grabber.PrepareAsync(Needed, TestContext.Current.CancellationToken));
            Assert.Equal(2, log.Count);
            first.DenyPrepare = true;
            second.Ready = false;
            await Assert.ThrowsAsync<CaptureDeniedException>(() => grabber.PrepareAsync(Needed, TestContext.Current.CancellationToken));
            first.DenyPrepare = false;
            first.Ready = false;
            Assert.False(await grabber.PrepareAsync(Needed, TestContext.Current.CancellationToken));
        }
    }
}
