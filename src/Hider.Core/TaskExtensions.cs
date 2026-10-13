namespace Hider.Core
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Lets a task run without awaiting it, logging (with --verbose) a failure that would otherwise vanish unobserved.
        /// For event handlers that start async work (a click that silently does nothing is worse than a logged error).
        /// </summary>
        public static void Forget(this Task _task, string _what)
        {
            if (_task.IsCompleted)
            {
                Report(_task, _what);
                return;
            }
            _task.ContinueWith(_t => Report(_t, _what), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static void Report(Task _task, string _what)
        {
            if (_task.IsFaulted)
            {
                var e = _task.Exception!.GetBaseException();
                Log.Verbose($"{_what} failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
