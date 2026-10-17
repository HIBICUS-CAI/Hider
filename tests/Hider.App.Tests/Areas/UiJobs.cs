using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// Runs the headless UI thread's jobs until a condition holds: for work that completes through dispatcher posts or
    /// rendered frames (strip transparency, cancelled waits). Bounded by a wall-clock limit and by the test's own
    /// cancellation, so a regression shows as a failed assertion instead of a hung run.
    /// </summary>
    internal static class UiJobs
    {
        /// <summary>Upper bound for one wait: far above what the jobs need (they are all queued or due at once).</summary>
        public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Runs jobs (and, with <paramref name="_renderFrames"/>, render timer ticks) until <paramref name="_done"/> is true;
        /// returns whether it became true.
        /// </summary>
        public static bool RunUntil(Func<bool> _done, bool _renderFrames = false)
        {
            var clock = Stopwatch.StartNew();
            var cancel = TestContext.Current.CancellationToken;
            while (!_done())
            {
                if (clock.Elapsed >= Limit || cancel.IsCancellationRequested)
                {
                    return false;
                }
                if (_renderFrames)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
                Dispatcher.UIThread.RunJobs();
            }
            return true;
        }

        /// <summary>Runs jobs until <paramref name="_task"/> has completed, and fails the test if it does not.</summary>
        public static void Complete(Task _task, bool _renderFrames = false)
        {
            Assert.True(RunUntil(() => _task.IsCompleted, _renderFrames), "the task did not complete on the UI thread");
        }
    }
}
