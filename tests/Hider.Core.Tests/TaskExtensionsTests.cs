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
    }
}
