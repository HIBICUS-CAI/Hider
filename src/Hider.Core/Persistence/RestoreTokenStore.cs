namespace Hider.Core.Persistence
{
    /// <summary>
    /// The ScreenCast portal's restore_token (~/.hider/restore_token, spec §6 Linux). With it the portal starts without the
    /// dialog. Tokens are single use: every successful start hands out a new one, which replaces the old; a start without a
    /// new token leaves none, so the next start shows the dialog again (CT-91).
    /// </summary>
    public sealed class RestoreTokenStore
    {
        private readonly string path;

        public RestoreTokenStore(string _path)
        {
            path = _path;
        }

        public string? Load()
        {
            try
            {
                string token = File.ReadAllText(path).Trim();
                return token.Length > 0 ? token : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Keeps <paramref name="_token"/>, or forgets the old one when null, empty or white space only (<see cref="Load"/>
        /// would read such a file as no token anyway).
        /// </summary>
        public void Save(string? _token)
        {
            string temp = path + ".tmp";
            try
            {
                if (string.IsNullOrWhiteSpace(_token))
                {
                    File.Delete(path);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // A left-over temp file would keep its old mode: start from none.
                File.Delete(temp);
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows())
                {
                    // It lets anyone with the file start screen capture without asking: owner only, from the moment it exists.
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }
                using (var stream = new FileStream(temp, options))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(_token);
                }
                File.Move(temp, path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Verbose($"restore token not saved: {e.Message}");
                TryDelete(temp);
            }
        }

        private static void TryDelete(string _path)
        {
            try
            {
                File.Delete(_path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Verbose($"could not delete {_path}: {e.Message}");
            }
        }
    }
}
