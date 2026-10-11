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
                using (var first = SingleInstance.TryAcquire(path))
                {
                    Assert.NotNull(first);
                    Assert.Null(SingleInstance.TryAcquire(path));
                }
                using var again = SingleInstance.TryAcquire(path);
                Assert.NotNull(again);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(path)!, true);
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
        }
    }
}
