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

        /// <summary>Keeps <paramref name="_token"/>, or forgets the old one when null.</summary>
        public void Save(string? _token)
        {
            try
            {
                if (_token is null)
                {
                    File.Delete(path);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, _token);
                if (!OperatingSystem.IsWindows())
                {
                    // It lets anyone with the file start screen capture without asking: owner only.
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                File.Move(temp, path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Verbose($"restore token not saved: {e.Message}");
            }
        }
    }
}
