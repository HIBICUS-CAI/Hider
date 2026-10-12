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

        /// <summary>
        /// The guard, or null when another instance holds the lock (<paramref name="_error"/> null) or the lock cannot be
        /// taken at all (<paramref name="_error"/> says why: the folder is a file, no permission).
        /// </summary>
        public static SingleInstance? TryAcquire(string _lockPath, out string? _error)
        {
            _error = null;
            try
            {
                string? dir = Path.GetDirectoryName(Path.GetFullPath(_lockPath));
                if (dir is not null)
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _error = $"cannot create the folder of {_lockPath}: {e.Message}";
                return null;
            }
            try
            {
                return new SingleInstance(new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (UnauthorizedAccessException e)
            {
                _error = $"cannot open {_lockPath}: {e.Message}";
                return null;
            }
            catch (IOException)
            {
                return null; // sharing violation (Windows) or flock conflict (Linux): another instance
            }
        }

        public void Dispose()
        {
            stream.Dispose();
        }
    }
}
