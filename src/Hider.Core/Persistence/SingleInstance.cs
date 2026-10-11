namespace Hider.Core.Persistence
{
    /// <summary>
    /// Single instance guard (FR-08): keeps instance.lock open with FileShare.None. A second open fails with a sharing
    /// violation on Windows and a flock conflict on Linux, also within the same process. Released by Dispose or process exit.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly FileStream stream;

        private SingleInstance(FileStream _stream)
        {
            stream = _stream;
        }

        /// <summary>The guard, or null when another instance holds the lock.</summary>
        public static SingleInstance? TryAcquire(string _lockPath)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(_lockPath));
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            try
            {
                return new SingleInstance(new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            stream.Dispose();
        }
    }
}
