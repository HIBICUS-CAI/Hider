using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    public sealed class RestoreTokenStoreTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "hider-token-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(dir, ".hider", "restore_token");

        public void Dispose()
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void CT91_SaveReplaceAndForget()
        {
            var store = new RestoreTokenStore(FilePath);
            Assert.Null(store.Load()); // absent: the portal shows its dialog
            store.Save("first");
            Assert.Equal("first", store.Load());
            store.Save("second"); // single use: the newest replaces the old
            Assert.Equal("second", store.Load());
            Assert.Equal("second", new RestoreTokenStore(FilePath).Load());
            store.Save(null); // a start without a new token: the used one is worthless
            Assert.Null(store.Load());
            Assert.False(File.Exists(FilePath));
            if (!OperatingSystem.IsWindows())
            {
                store.Save("third");
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
            }
        }

        [Fact]
        public void CT91_TempFile_NewWithOwnerOnlyMode_NeverLeftBehind()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, "old");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
            var store = new RestoreTokenStore(FilePath);
            store.Save("token");
            Assert.Equal("token", store.Load());
            Assert.False(File.Exists(temp));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
            }

            // The rename fails (a directory is in the way): the token does not stay behind in the temp file.
            File.Delete(FilePath);
            Directory.CreateDirectory(FilePath);
            Directory.CreateDirectory(Path.Combine(FilePath, "x"));
            store.Save("second");
            Assert.False(File.Exists(temp));
        }

        [Fact]
        public void CT91_EmptyFile_IsNoToken()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "  \n");
            Assert.Null(new RestoreTokenStore(FilePath).Load());
        }
    }
}
