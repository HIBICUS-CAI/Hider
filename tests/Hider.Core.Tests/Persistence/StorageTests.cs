using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    public class StorageTests
    {
        [Fact]
        public void CT30_SingleInstance_SecondAcquireFails()
        {
            string path = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"), "instance.lock");
            try
            {
                using (var first = SingleInstance.TryAcquire(path, out string? error))
                {
                    Assert.NotNull(first);
                    Assert.Null(error);
                    Assert.Null(SingleInstance.TryAcquire(path, out error));
                    Assert.Null(error); // another instance, not an error
                }
                using var again = SingleInstance.TryAcquire(path, out _);
                Assert.NotNull(again);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(path)!, true);
            }
        }

        [Fact]
        public void SingleInstance_FolderThatIsAFile_IsAnErrorNotAnotherInstance()
        {
            // ~/.hider existing as a file used to throw out of startup; it must be told apart from "already running".
            string root = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string file = Path.Combine(root, ".hider");
                File.WriteAllText(file, "");
                Assert.Null(SingleInstance.TryAcquire(Path.Combine(file, "instance.lock"), out string? error));
                Assert.NotNull(error);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void SingleInstance_LockPathThatIsAFolder_IsAnErrorNotAnotherInstance()
        {
            // Only a lock held by another process means "already running"; any other failure to open it is an error.
            string root = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
            string lockPath = Path.Combine(root, "instance.lock");
            Directory.CreateDirectory(lockPath);
            try
            {
                Assert.Null(SingleInstance.TryAcquire(lockPath, out string? error));
                Assert.NotNull(error);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CT31_DataDirectory_PerOs()
        {
            var env = new Dictionary<string, string?> { ["USERPROFILE"] = @"C:\Users\me", ["HOME"] = "/home/me" };
            Assert.Equal(@"C:\Users\me\.hider", StoragePaths.DataDirectory(true, _k => env.GetValueOrDefault(_k)));
            Assert.Equal("/home/me/.hider", StoragePaths.DataDirectory(false, _k => env.GetValueOrDefault(_k)));
            Assert.Equal("/home/me/.hider", StoragePaths.DataDirectory(false, _k => _k == "HOME" ? "/home/me/" : null));
            // An empty HOME is unset, not the root directory.
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/', '\\');
            Assert.Equal(profile + "/.hider", StoragePaths.DataDirectory(false, _k => _k == "HOME" ? "" : null));
            Assert.Equal(profile + "\\.hider", StoragePaths.DataDirectory(true, _k => _k == "USERPROFILE" ? "" : null));
            // So is one of white space only: " /.hider" would be a folder named " " in the working directory.
            Assert.Equal(profile + "/.hider", StoragePaths.DataDirectory(false, _k => _k == "HOME" ? " " : null));
            Assert.Equal(profile + "/.hider", StoragePaths.DataDirectory(false, _k => _k == "HOME" ? "\t \n" : null));
            Assert.Equal(profile + "\\.hider", StoragePaths.DataDirectory(true, _k => _k == "USERPROFILE" ? "  " : null));
            // Surrounding white space of a real home is kept (a folder name may contain it).
            Assert.Equal(" /home/me /.hider", StoragePaths.DataDirectory(false, _k => _k == "HOME" ? " /home/me " : null));
        }
    }
}
