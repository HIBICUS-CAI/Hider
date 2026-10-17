using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Capture
{
    /// <summary>The branches of <see cref="StripCoordinator"/> that the CT-79 .. CT-88 tests do not reach.</summary>
    public class StripCoordinatorEdgeTests
    {
        private static readonly string[] restoredBoth = { "restore a", "restore b" };

        /// <summary>A host whose calls can fail.</summary>
        private sealed class FailingHost : IStripHost
        {
            public List<string> Log { get; } = new();
            public string? FailSetFor { get; init; }
            public string? FailRestoreFor { get; init; }

            public Task SetTransparentRowsAsync(string _areaId, int _y0, int _y1, CancellationToken _ct)
            {
                Log.Add($"set {_areaId}");
                return _areaId == FailSetFor ? Task.FromException(new InvalidOperationException("set failed")) : Task.CompletedTask;
            }

            public Task RestoreAsync(string _areaId)
            {
                Log.Add($"restore {_areaId}");
                return _areaId == FailRestoreFor ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
            }
        }

        private static CaptureRequest Request(PhysicalRect _rect, int _strip = 8)
        {
            return new CaptureRequest(_rect, Array.Empty<nint>(), _strip);
        }

        [Fact]
        public void Traits_NeedStripCooperation()
        {
            using var coordinator = new StripCoordinator(new FakeScreenGrabber(new List<string>(), null), new FakeStripHost(new List<string>()), Array.Empty<StripArea>);
            Assert.Equal(CaptureTraits.NeedsStripCooperation, coordinator.Traits);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(10, 0)]
        [InlineData(-5, 10)]
        public async Task EmptyRect_UnavailableWithoutAskingAnything(int _w, int _h)
        {
            var log = new List<string>();
            int areaCalls = 0;
            using var coordinator = new StripCoordinator(new FakeScreenGrabber(log, CoordinateFrame.Create(10, 10)), new FakeStripHost(log),
                () => { areaCalls++; return Array.Empty<StripArea>(); });
            var result = await coordinator.CaptureAsync(Request(new PhysicalRect(0, 0, _w, _h)), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.Unavailable, result.Failure);
            Assert.Null(result.Frame);
            Assert.Empty(log);
            Assert.Equal(0, areaCalls);
        }

        [Theory]
        [InlineData(0, 8)]
        [InlineData(-3, 8)]
        public void Strips_NoRows_NoStrips(int _height, int _strip)
        {
            Assert.Empty(StripCoordinator.Strips(_height, _strip));
        }

        [Theory]
        [InlineData(5, 8, new[] { 0, 5 })]               // one short strip
        [InlineData(8, 8, new[] { 0, 8 })]
        [InlineData(9, 8, new[] { 0, 8, 8, 9 })]
        [InlineData(3, 1, new[] { 0, 1, 1, 2, 2, 3 })]
        [InlineData(20, -1, new[] { 0, 20 })]            // negative: the whole area, like 0
        [InlineData(1, 0, new[] { 0, 1 })]
        public void Strips_Rows(int _height, int _strip, int[] _edges)
        {
            Assert.Equal(_edges, StripCoordinator.Strips(_height, _strip).SelectMany(_s => new[] { _s.Y0, _s.Y1 }));
        }

        [Fact]
        public async Task WithoutClock_TheSystemClockGivesTheTimestamps()
        {
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var area = new StripArea("a", new PhysicalRect(0, 0, 10, 10), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { area });
            long before = TimeProvider.System.GetTimestamp();
            var result = await coordinator.CaptureAsync(Request(area.Rect, 0), TestContext.Current.CancellationToken);
            long after = TimeProvider.System.GetTimestamp();
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.InRange(grabber.NotBefore.Single(), before, after);
        }

        [Fact]
        public async Task EmptyScreen_MeansUnknown_TheWholeRectIsCaptured()
        {
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(200, 100));
            var area = new StripArea("a", new PhysicalRect(150, 10, 40, 8), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { area }, new FakeTimeProvider(), () => default);
            var result = await coordinator.CaptureAsync(Request(area.Rect), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal(new PhysicalRect(150, 10, 40, 8), grabber.Needed.Single());
            Assert.Equal((189, 17), CoordinateFrame.Decode(result.Frame!, 39, 7));
        }

        [Fact]
        public async Task AreaPastTheTopLeftOfTheScreens_VisiblePartPlacedAtItsOffset()
        {
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var area = new StripArea("a", new PhysicalRect(-20, -10, 60, 30), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { area }, new FakeTimeProvider(),
                () => new PhysicalRect(0, 0, 100, 100));
            var result = await coordinator.CaptureAsync(Request(area.Rect, 0), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.None, result.Failure);
            var frame = result.Frame!;
            Assert.Equal((60, 30, 240), (frame.Width, frame.Height, frame.Stride));
            Assert.Equal((0, 0), CoordinateFrame.Decode(frame, 20, 10));
            Assert.Equal((39, 19), CoordinateFrame.Decode(frame, 59, 29));
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Bgra.Span.Slice(0, 4).ToArray());                       // above-left: black
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Bgra.Span.Slice(29 * 240 + 19 * 4, 4).ToArray());     // left of the screen
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Bgra.Span.Slice(9 * 240 + 59 * 4, 4).ToArray());      // above the screen
            Assert.Equal(new PhysicalRect(0, 0, 40, 20), grabber.Needed.Single());
            Assert.Contains("set a 10-30", log); // the area's own rows of the visible part
        }

        [Fact]
        public async Task StripTallerThanTheArea_OneGrab()
        {
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var area = new StripArea("a", new PhysicalRect(0, 0, 10, 5), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { area }, new FakeTimeProvider());
            var result = await coordinator.CaptureAsync(Request(area.Rect, 64), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal(1, grabber.Calls);
            Assert.Contains("set a 0-5", log);
        }

        [Fact]
        public async Task NoAreaCoversTheRect_GrabsWithoutMakingAnythingTransparent()
        {
            // The requester is not a mosaic area any more (or not listed): nothing to make transparent, the screen is grabbed.
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100));
            var fill = new StripArea("f", new PhysicalRect(0, 0, 10, 10), PatternKind.Fill);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { fill }, new FakeTimeProvider());
            var result = await coordinator.CaptureAsync(Request(new PhysicalRect(0, 0, 10, 10)), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.DoesNotContain(log, _e => _e.StartsWith("set", StringComparison.Ordinal) || _e.StartsWith("restore", StringComparison.Ordinal));
            Assert.Equal(2, grabber.Calls);
        }

        [Fact]
        public async Task CancelledBeforeStarting_NothingHappens()
        {
            var log = new List<string>();
            using var coordinator = new StripCoordinator(new FakeScreenGrabber(log, CoordinateFrame.Create(10, 10)), new FakeStripHost(log),
                () => new[] { new StripArea("a", new PhysicalRect(0, 0, 10, 10), PatternKind.Mosaic) });
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.CaptureAsync(Request(new PhysicalRect(0, 0, 10, 10)), cancel.Token));
            Assert.Empty(log);
        }

        [Fact]
        public async Task RestoreFailure_IsLogged_OtherAreasStillRestored_ResultKept()
        {
            var host = new FailingHost { FailRestoreFor = "a" };
            var grabber = new FakeScreenGrabber(new List<string>(), CoordinateFrame.Create(100, 100));
            var areas = new[]
            {
                new StripArea("a", new PhysicalRect(0, 0, 20, 8), PatternKind.Mosaic),
                new StripArea("b", new PhysicalRect(10, 0, 20, 8), PatternKind.Mosaic),
            };
            using var coordinator = new StripCoordinator(grabber, host, () => areas, new FakeTimeProvider());
            using var messages = new StringWriter();
            CaptureResult result;
            using (Log.CaptureTo(messages))
            {
                result = await coordinator.CaptureAsync(Request(areas[0].Rect), TestContext.Current.CancellationToken);
            }
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal(restoredBoth, host.Log.Where(_e => _e.StartsWith("restore", StringComparison.Ordinal)));
            Assert.Contains("restoring area a failed: boom", messages.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task SetFailure_Propagates_EveryTouchedAreaRestored()
        {
            // The area whose set threw may be half transparent: it is restored too, and the grabber is never asked.
            var host = new FailingHost { FailSetFor = "b" };
            var grabber = new FakeScreenGrabber(new List<string>(), CoordinateFrame.Create(100, 100));
            var areas = new[]
            {
                new StripArea("a", new PhysicalRect(0, 0, 20, 8), PatternKind.Mosaic),
                new StripArea("b", new PhysicalRect(10, 0, 20, 8), PatternKind.Mosaic),
            };
            using var coordinator = new StripCoordinator(grabber, host, () => areas, new FakeTimeProvider());
            var e = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CaptureAsync(Request(areas[0].Rect), TestContext.Current.CancellationToken));
            Assert.Equal("set failed", e.Message);
            Assert.Equal(restoredBoth, host.Log.Where(_e => _e.StartsWith("restore", StringComparison.Ordinal)));
            Assert.Equal(0, grabber.Calls);
            // The serial lock was released: the next capture runs (and fails the same way).
            var again = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CaptureAsync(Request(areas[0].Rect), TestContext.Current.CancellationToken));
            Assert.Equal("set failed", again.Message);
        }

        [Fact]
        public async Task DeniedWhilePreparingOrGrabbing_IsLogged()
        {
            var log = new List<string>();
            var area = new StripArea("a", new PhysicalRect(0, 0, 10, 10), PatternKind.Mosaic);
            var denyPrepare = new FakeScreenGrabber(log, CoordinateFrame.Create(10, 10)) { DenyPrepare = true };
            var denyGrab = new FakeScreenGrabber(log, CoordinateFrame.Create(10, 10)) { Deny = true };
            using var first = new StripCoordinator(denyPrepare, new FakeStripHost(log), () => new[] { area }, new FakeTimeProvider());
            using var second = new StripCoordinator(denyGrab, new FakeStripHost(log), () => new[] { area }, new FakeTimeProvider());
            using var messages = new StringWriter();
            using (Log.CaptureTo(messages))
            {
                Assert.Equal(CaptureFailure.Denied, (await first.CaptureAsync(Request(area.Rect), TestContext.Current.CancellationToken)).Failure);
                Assert.Equal(CaptureFailure.Denied, (await second.CaptureAsync(Request(area.Rect), TestContext.Current.CancellationToken)).Failure);
            }
            Assert.Equal(2, messages.ToString().Split('\n').Count(_l => _l.Contains("strip capture denied: test", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task FrameMissingPartOfTheStrip_Unavailable()
        {
            // Covers overlaps the strip but not all of it.
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(100, 100)) { Covers = new PhysicalRect(0, 0, 100, 4) };
            var area = new StripArea("a", new PhysicalRect(0, 0, 10, 8), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { area }, new FakeTimeProvider());
            Assert.Equal(CaptureFailure.Unavailable, (await coordinator.CaptureAsync(Request(area.Rect), TestContext.Current.CancellationToken)).Failure);
            Assert.Equal("restore a", log[^1]);
        }

        [Fact]
        public async Task Dispose_DisposesTheGrabber_LaterCapturesThrow()
        {
            var grabber = new ScriptedGrabber();
            var coordinator = new StripCoordinator(grabber, new FakeStripHost(new List<string>()), Array.Empty<StripArea>);
            coordinator.Dispose();
            Assert.Equal(1, grabber.Disposed);
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => coordinator.CaptureAsync(Request(new PhysicalRect(0, 0, 4, 4)), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task CoveringAreaEndingInsideAStrip_OnlyItsOwnRowsAreMadeTransparent()
        {
            // "b" ends at y = 40, inside the third strip [36, 44) of "a": its rows stop at its bottom (10), not at the
            // strip's (14). The host is asked only for rows the area has (NFR-03: only the strip's rows of each area).
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(200, 100));
            var a = new StripArea("a", new PhysicalRect(10, 20, 100, 24), PatternKind.Mosaic);
            var b = new StripArea("b", new PhysicalRect(10, 30, 50, 10), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { a, b }, new FakeTimeProvider());
            var result = await coordinator.CaptureAsync(Request(a.Rect), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal(
                "prepare, set a 0-8, grab, restore a, set a 8-16, set b 0-6, grab, restore a, restore b, set a 16-24, set b 6-10, grab, restore a, restore b",
                string.Join(", ", log));
            Assert.Equal((109, 43), CoordinateFrame.Decode(result.Frame!, 99, 23));
        }

        [Fact]
        public async Task CoveringAreaStartingAboveTheRequest_RowsCountFromItsOwnTop()
        {
            // "c" starts 5 rows above "a" and ends inside its first strip: rows [5, 9) of c are the strip's [20, 24).
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(200, 100));
            var a = new StripArea("a", new PhysicalRect(10, 20, 100, 16), PatternKind.Mosaic);
            var c = new StripArea("c", new PhysicalRect(50, 15, 80, 9), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(log), () => new[] { a, c }, new FakeTimeProvider());
            var result = await coordinator.CaptureAsync(Request(a.Rect), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal("prepare, set a 0-8, set c 5-9, grab, restore a, restore c, set a 8-16, grab, restore a", string.Join(", ", log));
        }

        [Fact]
        public async Task PrepareGetsTheVisibleRectAndTheCallersToken()
        {
            PhysicalRect seen = default;
            CancellationToken seenToken = default;
            using var cancel = new CancellationTokenSource();
            var grabber = new ScriptedGrabber { Prepare = (_r, _c) => { (seen, seenToken) = (_r, _c); return Task.FromResult(false); } };
            var area = new StripArea("a", new PhysicalRect(-20, 10, 60, 30), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(grabber, new FakeStripHost(new List<string>()), () => new[] { area }, new FakeTimeProvider(),
                () => new PhysicalRect(0, 0, 100, 100));
            Assert.Equal(CaptureFailure.Unavailable, (await coordinator.CaptureAsync(Request(area.Rect), cancel.Token)).Failure);
            Assert.Equal((new PhysicalRect(0, 10, 40, 30), cancel.Token), (seen, seenToken));
        }

        [Fact]
        public void CopyRows_EmptyBounds_TakenAsScaleOne()
        {
            var frame = CoordinateFrame.Create(50, 40);
            var rect = new PhysicalRect(5, 6, 10, 4);
            var dst = FrameBuffer.Allocate(10, 4);
            StripCoordinator.CopyRows(frame, new PhysicalRect(0, 0, 0, 0), rect, 0, 4, dst);
            Assert.Equal((5, 6), CoordinateFrame.Decode(dst, 0, 0));
            Assert.Equal((14, 9), CoordinateFrame.Decode(dst, 9, 3));
        }

        [Fact]
        public void CopyRows_OnlyTheRequestedRows()
        {
            var frame = CoordinateFrame.Create(50, 40);
            var rect = new PhysicalRect(0, 0, 10, 6);
            var dst = FrameBuffer.Allocate(10, 6);
            StripCoordinator.CopyRows(frame, new PhysicalRect(0, 0, 50, 40), rect, 2, 4, dst);
            for (int y = 0; y < 6; y++)
            {
                bool copied = y is >= 2 and < 4;
                Assert.Equal(copied, dst.Bgra.Span.Slice(y * dst.Stride, dst.Stride).IndexOfAnyExcept((byte)0) >= 0);
            }
            Assert.Equal((3, 3), CoordinateFrame.Decode(dst, 3, 3));
        }

        [Fact]
        public void CopyRows_PastTheFrame_RepeatsTheEdge()
        {
            // A rectangle reaching past the frame on every side reads the nearest edge pixel (the slow per-pixel path).
            var frame = CoordinateFrame.Create(20, 10);
            var bounds = new PhysicalRect(0, 0, 20, 10);
            var rect = new PhysicalRect(-3, -2, 26, 14);
            var dst = FrameBuffer.Allocate(26, 14);
            StripCoordinator.CopyRows(frame, bounds, rect, 0, 14, dst);
            Assert.Equal((0, 0), CoordinateFrame.Decode(dst, 0, 0));
            Assert.Equal((0, 0), CoordinateFrame.Decode(dst, 3, 2));
            Assert.Equal((5, 4), CoordinateFrame.Decode(dst, 8, 6));
            Assert.Equal((19, 9), CoordinateFrame.Decode(dst, 25, 13));
            Assert.Equal((19, 0), CoordinateFrame.Decode(dst, 24, 0));
        }

        [Fact]
        public void CopyRows_RightEdgeOnly_SlowPathStillExact()
        {
            var frame = CoordinateFrame.Create(20, 10);
            var rect = new PhysicalRect(15, 0, 10, 2);
            var dst = FrameBuffer.Allocate(10, 2);
            StripCoordinator.CopyRows(frame, new PhysicalRect(0, 0, 20, 10), rect, 0, 2, dst);
            for (int x = 0; x < 10; x++)
            {
                Assert.Equal((Math.Min(15 + x, 19), 1), CoordinateFrame.Decode(dst, x, 1));
            }
        }

        [Fact]
        public void CopyRows_DownscaledFrame_NearestPixelCentres()
        {
            // The frame shows bounds at half the resolution: area pixel x maps to floor((x + 0.5) / 2).
            var frame = CoordinateFrame.Create(50, 25);
            var rect = new PhysicalRect(10, 4, 8, 4);
            var dst = FrameBuffer.Allocate(8, 4);
            StripCoordinator.CopyRows(frame, new PhysicalRect(0, 0, 100, 50), rect, 0, 4, dst);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Assert.Equal(((10 + x) / 2, (4 + y) / 2), CoordinateFrame.Decode(dst, x, y));
                }
            }
        }

        [Fact]
        public void CopyRows_WiderDestinationStride_LeavesTheRestOfTheRow()
        {
            var frame = CoordinateFrame.Create(20, 10);
            var rect = new PhysicalRect(2, 3, 4, 2);
            var dst = new FrameBuffer { Width = 4, Height = 2, Stride = 32, Bgra = Enumerable.Repeat((byte)0xAB, 64).ToArray() };
            StripCoordinator.CopyRows(frame, new PhysicalRect(0, 0, 20, 10), rect, 0, 2, dst);
            Assert.Equal((2, 3), CoordinateFrame.Decode(dst, 0, 0));
            Assert.Equal((5, 4), CoordinateFrame.Decode(dst, 3, 1));
            Assert.All(dst.Bgra.Span.Slice(16, 16).ToArray(), _b => Assert.Equal(0xAB, _b));
            Assert.All(dst.Bgra.Span.Slice(48, 16).ToArray(), _b => Assert.Equal(0xAB, _b));
        }
    }
}
