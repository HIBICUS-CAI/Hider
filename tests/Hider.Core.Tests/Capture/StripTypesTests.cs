using Hider.Core.Capture;
using Hider.Core.Model;

namespace Hider.Core.Tests.Capture
{
    public class StripTypesTests
    {
        [Fact]
        public async Task ScreenGrabber_DefaultPrepare_ExpectsToDeliver()
        {
            // Default interface members are reached through the interface only.
            var task = ((IScreenGrabber)new MinimalGrabber()).PrepareAsync(new PhysicalRect(0, 0, 10, 10), TestContext.Current.CancellationToken);
            Assert.True(task.IsCompletedSuccessfully);
            Assert.True(await task);
            Assert.True(await ((IScreenGrabber)new MinimalGrabber()).PrepareAsync(default, TestContext.Current.CancellationToken)); // whatever the rectangle
        }

        [Fact]
        public void ScreenGrabber_DefaultReadyWithoutPrepare_IsFalse()
        {
            Assert.False(((IScreenGrabber)new MinimalGrabber()).ReadyWithoutPrepare);
        }

        [Fact]
        public async Task ScreenGrabber_DefaultMembers_AsUsedByTheFallback()
        {
            // A grabber without its own PrepareAsync is "ready" for the fallback, and is asked for frames after a prepare.
            using var fallback = new FallbackGrabber(new MinimalGrabber());
            Assert.True(await fallback.PrepareAsync(new PhysicalRect(0, 0, 4, 4), TestContext.Current.CancellationToken));
            Assert.Null(await fallback.GrabAfterAsync(0, new PhysicalRect(0, 0, 4, 4), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void ScreenFrame_Record()
        {
            var frame = FrameBuffer.Allocate(2, 2);
            var a = new ScreenFrame(frame, new PhysicalRect(-10, 0, 2, 2));
            Assert.Same(frame, a.Frame);
            Assert.Equal(new PhysicalRect(-10, 0, 2, 2), a.Covers);
            Assert.Equal(a, new ScreenFrame(frame, new PhysicalRect(-10, 0, 2, 2)));
            Assert.NotEqual(a, new ScreenFrame(FrameBuffer.Allocate(2, 2), a.Covers)); // frames compare by reference
        }

        [Fact]
        public void StripArea_ValueRecord()
        {
            var a = new StripArea("x", new PhysicalRect(1, 2, 3, 4), PatternKind.Mosaic);
            Assert.Equal("x", a.Id);
            Assert.Equal(new PhysicalRect(1, 2, 3, 4), a.Rect);
            Assert.Equal(PatternKind.Mosaic, a.Kind);
            Assert.Equal(a, new StripArea("x", new PhysicalRect(1, 2, 3, 4), PatternKind.Mosaic));
            Assert.NotEqual(a, a with { Kind = PatternKind.Fill });
            Assert.Equal(default, new StripArea().Rect);
        }

        [Fact]
        public void CaptureDeniedException_KeepsTheMessage()
        {
            var e = new CaptureDeniedException("cancelled by the user");
            Assert.Equal("cancelled by the user", e.Message);
            Assert.Null(e.InnerException);
            Assert.IsAssignableFrom<Exception>(e);
        }
    }
}
