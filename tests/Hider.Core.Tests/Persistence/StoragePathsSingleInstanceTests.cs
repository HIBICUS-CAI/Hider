using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    public class StoragePathsSingleInstanceTests
    {
        private static readonly string[] variablesAsked = { "HOME", "USERPROFILE" };

        private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/', '\\');

        [Fact]
        public void StoragePaths_Names()
        {
            Assert.Equal(".hider", StoragePaths.DirectoryName);
            Assert.Equal("areas.json", StoragePaths.AreasFileName);
            Assert.Equal("instance.lock", StoragePaths.InstanceLockFileName);
            Assert.Equal("restore_token", StoragePaths.RestoreTokenFileName);
        }

        [Fact]
        public void DataDirectory_Windows_UsesUserProfileOnly()
        {
            Assert.Equal(@"C:\Users\me\.hider", StoragePaths.DataDirectory(true, _k => _k == "USERPROFILE" ? @"C:\Users\me\" : "/home/other"));
            // HOME is not looked at on Windows: without USERPROFILE the system's profile folder is used.
            Assert.Equal(Profile + @"\.hider", StoragePaths.DataDirectory(true, _k => _k == "HOME" ? "/home/me" : null));
        }

        [Fact]
        public void DataDirectory_Linux_UsesHomeOnly()
        {
            Assert.Equal("/home/me/.hider", StoragePaths.DataDirectory(false, _k => _k == "HOME" ? "/home/me//" : @"C:\Users\other"));
            Assert.Equal(Profile + "/.hider", StoragePaths.DataDirectory(false, _k => _k == "USERPROFILE" ? @"C:\Users\me" : null));
        }

        [Fact]
        public void DataDirectory_AsksForExactlyOneVariable()
        {
            var asked = new List<string>();
            StoragePaths.DataDirectory(false, _k =>
            {
                asked.Add(_k);
                return "/home/me";
            });
            StoragePaths.DataDirectory(true, _k =>
            {
                asked.Add(_k);
                return @"C:\Users\me";
            });
            Assert.Equal(variablesAsked, asked);
        }

        [Fact]
        public void DataDirectory_RootHome_IsTheHiderFolderAtTheRoot()
        {
            // A home of "/" (some service accounts) keeps one separator.
            Assert.Equal("/.hider", StoragePaths.DataDirectory(false, _ => "/"));
        }

        [Fact]
        public void CurrentPaths_FollowThisProcess()
        {
            string expected = StoragePaths.DataDirectory(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable);
            Assert.Equal(expected, StoragePaths.CurrentDataDirectory);
            Assert.EndsWith(Path.DirectorySeparatorChar + ".hider", StoragePaths.CurrentDataDirectory, StringComparison.Ordinal);
            Assert.Equal(Path.Combine(expected, "areas.json"), StoragePaths.AreasFile);
            Assert.Equal(Path.Combine(expected, "instance.lock"), StoragePaths.InstanceLock);
            Assert.Equal(Path.Combine(expected, "restore_token"), StoragePaths.RestoreToken);
        }

        [Fact]
        public void SingleInstance_CreatesMissingFolders_KeepsTheLockFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "a", "b", "instance.lock");
            try
            {
                using (var guard = SingleInstance.TryAcquire(path, out string? error))
                {
                    Assert.NotNull(guard);
                    Assert.Null(error);
                    Assert.True(File.Exists(path));
                }
                Assert.True(File.Exists(path)); // released, not deleted
                using var again = SingleInstance.TryAcquire(path, out string? error2);
                Assert.NotNull(again);
                Assert.Null(error2);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void SingleInstance_ExistingLockFile_IsReusedAndUnchanged()
        {
            string root = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "instance.lock");
            File.WriteAllText(path, "left by a crash");
            try
            {
                using (var guard = SingleInstance.TryAcquire(path, out _))
                {
                    Assert.NotNull(guard);
                }
                Assert.Equal("left by a crash", File.ReadAllText(path));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void SingleInstance_DisposeTwice_IsHarmless()
        {
            string root = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "instance.lock");
            try
            {
                var guard = SingleInstance.TryAcquire(path, out _)!;
                guard.Dispose();
                guard.Dispose();
                using var again = SingleInstance.TryAcquire(path, out _);
                Assert.NotNull(again);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void SingleInstance_NameTooLong_IsAnErrorNotAnotherInstance()
        {
            // An I/O error other than a held lock (here ENAMETOOLONG) must not read as "already running".
            string root = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Assert.Null(SingleInstance.TryAcquire(Path.Combine(root, new string('l', 300)), out string? error));
                Assert.False(string.IsNullOrEmpty(error));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Theory]
        [InlineData(true, unchecked((int)0x80070020), true)] // ERROR_SHARING_VIOLATION as an HRESULT
        [InlineData(true, unchecked((int)0x80070021), true)] // ERROR_LOCK_VIOLATION
        [InlineData(true, 32, true)] // the bare Win32 code: only the low word is compared
        [InlineData(true, 33, true)]
        [InlineData(true, unchecked((int)0x8007000B), false)] // ERROR_BAD_FORMAT: some other I/O error
        [InlineData(true, unchecked((int)0x80070005), false)] // ERROR_ACCESS_DENIED
        [InlineData(true, unchecked((int)0x80070034), false)] // 52: not 32 / 33
        [InlineData(true, 11, false)] // EWOULDBLOCK means nothing on Windows
        [InlineData(true, 0, false)]
        [InlineData(false, 11, true)] // EWOULDBLOCK: flock conflict
        [InlineData(false, 32, false)]
        [InlineData(false, 33, false)]
        [InlineData(false, unchecked((int)0x80070020), false)] // a Windows HRESULT is no flock conflict
        [InlineData(false, 0x1000B, false)] // the low word is 11, but Linux compares the whole value
        [InlineData(false, 36, false)] // ENAMETOOLONG
        [InlineData(false, 21, false)] // EISDIR
        [InlineData(false, 0, false)]
        public void SingleInstance_IsHeldByAnother_PerOsCodes(bool _isWindows, int _hresult, bool _held)
        {
            Assert.Equal(_held, SingleInstance.IsHeldByAnother(_isWindows, _hresult));
        }

        [Fact]
        public void SingleInstance_RootPath_HasNoFolderToCreate_IsAnError()
        {
            // The root has no parent folder (GetDirectoryName gives null) and cannot be opened as a file.
            Assert.Null(SingleInstance.TryAcquire(Path.GetPathRoot(Path.GetTempPath())!, out string? error));
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
