namespace Hider.Core.Persistence
{
    /// <summary>Where Hider keeps its files (FR-60): %USERPROFILE%\.hider on Windows, ~/.hider on Linux.</summary>
    public static class StoragePaths
    {
        public const string DirectoryName = ".hider";
        public const string AreasFileName = "areas.json";
        public const string InstanceLockFileName = "instance.lock";
        public const string RestoreTokenFileName = "restore_token";

        /// <summary>The data directory for an OS, given how to read environment variables (pure, for tests).</summary>
        public static string DataDirectory(bool _isWindows, Func<string, string?> _environment)
        {
            // An empty or blank variable counts as unset: "" + "/.hider" would put the files at the file system root, " " +
            // "/.hider" in a folder named " " of the working directory.
            string? home = _isWindows ? _environment("USERPROFILE") : _environment("HOME");
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            char separator = _isWindows ? '\\' : '/';
            return home.TrimEnd('\\', '/') + separator + DirectoryName;
        }

        public static string CurrentDataDirectory => DataDirectory(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable);

        public static string AreasFile => Path.Combine(CurrentDataDirectory, AreasFileName);
        public static string InstanceLock => Path.Combine(CurrentDataDirectory, InstanceLockFileName);
        public static string RestoreToken => Path.Combine(CurrentDataDirectory, RestoreTokenFileName);
    }
}
