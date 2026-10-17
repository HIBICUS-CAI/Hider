namespace Hider.Core.Tests
{
    public class TaskExtensionsTests
    {
        [Fact]
        public void Forget_LogsAFailure_AlsoOneThatComesLater()
        {
            using var writer = new StringWriter();
            using (Log.CaptureTo(writer))
            {
                Task.FromException(new InvalidOperationException("now")).Forget("first");
                var later = new TaskCompletionSource();
                later.Task.Forget("second");
                later.SetException(new IOException("later")); // the report runs synchronously, in this flow
                Task.CompletedTask.Forget("fine");
            }
            string text = writer.ToString();
            Assert.Contains("first failed: InvalidOperationException: now", text);
            Assert.Contains("second failed: IOException: later", text);
            Assert.DoesNotContain("fine", text);
        }

        [Fact]
        public async Task LogCapture_StaysInItsOwnFlow()
        {
            using var mine = new StringWriter();
            using var other = new StringWriter();
            using (Log.CaptureTo(mine))
            {
                // A capture set inside work this flow starts stays there: this one keeps only its own messages.
                await Task.Run(() =>
                {
                    using (Log.CaptureTo(other))
                    {
                        Log.Verbose("other");
                    }
                }, TestContext.Current.CancellationToken);
                Log.Verbose("mine");
            }
            Assert.Equal("mine" + Environment.NewLine, mine.ToString());
            Assert.Equal("other" + Environment.NewLine, other.ToString());
        }

        [Fact]
        public void Forget_FailureMessage_IsExactlyOneLine()
        {
            using var writer = new StringWriter();
            using (Log.CaptureTo(writer))
            {
                Task.FromException(new ArgumentException("bad value")).Forget("saving");
            }
            Assert.Equal("saving failed: ArgumentException: bad value" + Environment.NewLine, writer.ToString());
        }

        [Fact]
        public void Forget_Cancelled_IsNotAFailure()
        {
            using var writer = new StringWriter();
            using (Log.CaptureTo(writer))
            {
                Task.FromCanceled(new CancellationToken(true)).Forget("cancelled now");
                var later = new TaskCompletionSource();
                later.Task.Forget("cancelled later");
                later.SetCanceled(TestContext.Current.CancellationToken);
            }
            Assert.Equal("", writer.ToString());
        }

        [Fact]
        public void Forget_SucceedingLater_LogsNothing()
        {
            using var writer = new StringWriter();
            using (Log.CaptureTo(writer))
            {
                var later = new TaskCompletionSource<int>();
                later.Task.Forget("later");
                later.SetResult(5);
            }
            Assert.Equal("", writer.ToString());
        }

        [Fact]
        public void Forget_NotCompleted_LogsNothingYet()
        {
            using var writer = new StringWriter();
            var never = new TaskCompletionSource();
            using (Log.CaptureTo(writer))
            {
                never.Task.Forget("pending");
            }
            Assert.Equal("", writer.ToString());
            never.SetResult(); // finishes outside the capture: nothing to see here either
        }

        [Fact]
        public void Forget_ReportsTheFirstRealException_NotItsInnerCause()
        {
            using var writer = new StringWriter();
            using (Log.CaptureTo(writer))
            {
                Task.FromException(new InvalidOperationException("outer", new IOException("cause"))).Forget("wrapped");
            }
            Assert.Equal("wrapped failed: InvalidOperationException: outer" + Environment.NewLine, writer.ToString());
        }

        [Fact]
        public void Forget_SeveralExceptions_ReportedAsTheAggregate()
        {
            using var writer = new StringWriter();
            using (Log.CaptureTo(writer))
            {
                var source = new TaskCompletionSource();
                source.SetException(new Exception[] { new IOException("one"), new TimeoutException("two") });
                source.Task.Forget("both");
            }
            string text = writer.ToString();
            Assert.StartsWith("both failed: AggregateException: ", text, StringComparison.Ordinal);
            Assert.Contains("one", text, StringComparison.Ordinal);
            Assert.Contains("two", text, StringComparison.Ordinal);
        }
    }
}
