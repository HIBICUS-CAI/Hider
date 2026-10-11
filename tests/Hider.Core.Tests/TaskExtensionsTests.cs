namespace Hider.Core.Tests
{
    public class TaskExtensionsTests
    {
        [Fact]
        public async Task Forget_LogsAFailure_AlsoOneThatComesLater()
        {
            var error = Console.Error;
            using var writer = new StringWriter();
            bool enabled = Log.Enabled;
            try
            {
                Console.SetError(writer);
                Log.Enabled = true;
                Task.FromException(new InvalidOperationException("now")).Forget("first");
                var later = new TaskCompletionSource();
                later.Task.Forget("second");
                later.SetException(new IOException("later"));
                await Task.Yield();
                Task.CompletedTask.Forget("fine");
            }
            finally
            {
                Console.SetError(error);
                Log.Enabled = enabled;
            }
            string text = writer.ToString();
            Assert.Contains("first failed: InvalidOperationException: now", text);
            Assert.Contains("second failed: IOException: later", text);
            Assert.DoesNotContain("fine", text);
        }
    }
}
