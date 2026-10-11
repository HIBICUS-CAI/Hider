namespace Hider.Core
{
    /// <summary>Diagnostics on standard error, only with --verbose (work instructions §2.3). No telemetry, no files.</summary>
    public static class Log
    {
        public static bool Enabled { get; set; }

        public static void Verbose(string _message)
        {
            if (Enabled)
            {
                Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {_message}");
            }
        }
    }
}
