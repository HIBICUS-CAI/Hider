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
        /// taken at all (<paramref name="_error"/> is the system's message: the folder is a file, no permission). The caller
        /// words it for the user (Strings) and for standard error.
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
                _error = e.Message;
                return null;
            }
            try
            {
                return new SingleInstance(new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException e) when (IsHeldByAnother(OperatingSystem.IsWindows(), e.HResult))
            {
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // No permission, a read-only or full file system, an I/O error: not "already running".
                _error = e.Message;
                return null;
            }
        }

        /// <summary>
        /// The lock is held by another process: a sharing or lock violation on Windows (the Win32 code in the low word of
        /// the HRESULT), a flock conflict (EWOULDBLOCK) on Linux. Any other code is an error, not "already running".
        /// Takes the OS as an argument so that both rules run on every OS in the tests.
        /// </summary>
        internal static bool IsHeldByAnother(bool _isWindows, int _hresult)
        {
            const int errorSharingViolation = 32, errorLockViolation = 33, ewouldblock = 11;
            return _isWindows
                ? (_hresult & 0xFFFF) is errorSharingViolation or errorLockViolation
                : _hresult == ewouldblock;
        }

        public void Dispose()
        {
            stream.Dispose();
        }
    }
}
