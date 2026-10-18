using Hider.Core.Capture;

namespace Hider.Core.Tests.Capture
{
    /// <summary>Every member of <see cref="FrameExchange"/>, the capture / render double buffer (work instructions §3.5).</summary>
    public class FrameExchangeTests
    {
        private static (FrameBuffer? Front, long Version) Front(FrameExchange _exchange)
        {
            return _exchange.Read((_front, _version) => (_front, _version));
        }

        [Fact]
        public void CT103_New_NoFrontFrame_VersionZero_EmptyBack()
        {
            var exchange = new FrameExchange();
            Assert.Equal(0, exchange.Version);
            Assert.NotNull(exchange.Back);
            Assert.Same(exchange.Back, exchange.Back);
            Assert.Equal((null, 0L), Front(exchange));
        }

        [Fact]
        public void CT103_Publish_BackBecomesTheFront_FirstTimeANewBack()
        {
            var exchange = new FrameExchange();
            var first = exchange.Back;
            first.Resize(2, 1);
            exchange.Publish();
            Assert.Equal(1, exchange.Version);
            Assert.Same(first, Front(exchange).Front);
            Assert.NotSame(first, exchange.Back);
            Assert.Equal(0, exchange.Back.Width); // a fresh buffer
        }

        [Fact]
        public void CT103_Publish_SwapsTheTwoBuffers()
        {
            // After the first publish the two buffers alternate: no allocation per frame.
            var exchange = new FrameExchange();
            var a = exchange.Back;
            exchange.Publish();
            var b = exchange.Back;
            exchange.Publish();
            Assert.Same(b, Front(exchange).Front);
            Assert.Same(a, exchange.Back);
            exchange.Publish();
            Assert.Same(a, Front(exchange).Front);
            Assert.Same(b, exchange.Back);
            Assert.Equal(3, exchange.Version);
        }

        [Fact]
        public void CT103_Clear_DropsTheFront_KeepsTheBack()
        {
            var exchange = new FrameExchange();
            exchange.Publish();
            var back = exchange.Back;
            exchange.Clear();
            Assert.Equal((null, 2L), Front(exchange));
            Assert.Same(back, exchange.Back);
            exchange.Clear(); // again: still counts as a change
            Assert.Equal(3, exchange.Version);
        }

        [Fact]
        public void CT103_Clear_ThenPublish_AFreshBackAgain()
        {
            var exchange = new FrameExchange();
            exchange.Publish();
            exchange.Clear();
            var back = exchange.Back;
            exchange.Publish();
            Assert.Same(back, Front(exchange).Front);
            Assert.NotSame(back, exchange.Back);
        }

        [Fact]
        public void CT103_Release_LetsBothBuffersGo()
        {
            var exchange = new FrameExchange();
            var a = exchange.Back;
            a.Resize(100, 100);
            exchange.Publish();
            var b = exchange.Back;
            b.Resize(100, 100);
            exchange.Release();
            Assert.Equal((null, 2L), Front(exchange));
            Assert.NotSame(a, exchange.Back);
            Assert.NotSame(b, exchange.Back);
            Assert.True(exchange.Back.Bgra.IsEmpty);
        }

        [Fact]
        public void CT104_Read_PassesTheFrontAndTheVersion_ReturnsTheReadersResult()
        {
            var exchange = new FrameExchange();
            exchange.Back.Resize(7, 3);
            exchange.Publish();
            int width = exchange.Read((_front, _version) => _front!.Width * 10 + (int)_version);
            Assert.Equal(71, width);
            Assert.Equal("none", new FrameExchange().Read((_front, _) => _front is null ? "none" : "frame"));
        }

        [Fact]
        public void CT104_Read_ExceptionInTheReader_LeavesTheExchangeUsable()
        {
            var exchange = new FrameExchange();
            Assert.Throws<InvalidOperationException>(() => exchange.Read<int>((_, _) => throw new InvalidOperationException()));
            exchange.Publish(); // the lock was released
            Assert.Equal(1, exchange.Version);
        }

        [Fact]
        public async Task CT104_Publish_WaitsForARead_TheFrameBeingReadIsNeverReplaced()
        {
            var exchange = new FrameExchange();
            exchange.Publish();
            var reading = Front(exchange).Front;
            Task? publish = null;
            bool finishedDuringRead = exchange.Read((_front, _) =>
            {
                publish = Task.Run(exchange.Publish, TestContext.Current.CancellationToken);
                // The publisher blocks on the lock held here; it cannot complete inside the read.
                bool done = publish.Wait(TimeSpan.FromMilliseconds(100));
                Assert.Same(reading, _front);
                return done;
            });
            Assert.False(finishedDuringRead);
            await publish!;
            Assert.Equal(2, exchange.Version);
            Assert.NotSame(reading, Front(exchange).Front);
        }

        [Fact]
        public async Task CT104_ConcurrentPublishAndRead_VersionCountsEveryPublish()
        {
            var exchange = new FrameExchange();
            var writer = Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    exchange.Back.Resize(1, 1);
                    exchange.Publish();
                }
            }, TestContext.Current.CancellationToken);
            long last = -1;
            while (!writer.IsCompleted)
            {
                long version = exchange.Read((_front, _version) =>
                {
                    Assert.True(_version == 0 || _front is not null);
                    return _version;
                });
                Assert.True(version >= last);
                last = version;
            }
            await writer;
            Assert.Equal(1000, exchange.Version);
        }
    }
}
