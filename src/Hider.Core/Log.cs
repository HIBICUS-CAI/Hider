namespace Hider.Core
{
    /// <summary>Diagnostics on standard error, only with --verbose (work instructions §2.3). No telemetry, no files.</summary>
    public static class Log
    {
        private static readonly AsyncLocal<TextWriter?> capture = new();

        public static bool Enabled { get; set; }

        public static void Verbose(string _message)
        {
            var writer = capture.Value;
            if (writer is not null)
            {
                writer.WriteLine(_message);
            }
            else if (Enabled)
            {
                Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {_message}");
            }
        }

        /// <summary>
        /// Sends the messages of this flow (and of the work it starts, which inherits the execution context) to
        /// <paramref name="_writer"/> until disposed, whatever <see cref="Enabled"/> says. The process-wide standard error and
        /// <see cref="Enabled"/> are left alone, so tests running in parallel do not see each other's messages.
        /// </summary>
        public static IDisposable CaptureTo(TextWriter _writer)
        {
            var previous = capture.Value;
            capture.Value = _writer;
            return new Restore(previous);
        }

        private sealed class Restore(TextWriter? _previous) : IDisposable
        {
            public void Dispose()
            {
                capture.Value = _previous;
            }
        }
    }
}
