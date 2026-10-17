using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    public sealed class RestoreTokenStoreCasesTests : IDisposable
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
        public void CT91_Load_NoFolder_IsNull()
        {
            Assert.Null(new RestoreTokenStore(FilePath).Load());
        }

        [Fact]
        public void CT91_Load_PathIsAFolder_IsNull()
        {
            Directory.CreateDirectory(FilePath);
            Assert.Null(new RestoreTokenStore(FilePath).Load());
        }

        [Fact]
        public void CT91_Load_TrimsSurroundingWhitespace()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "  abc-123 \r\n");
            Assert.Equal("abc-123", new RestoreTokenStore(FilePath).Load());
        }

        [Fact]
        public void CT91_Save_WritesTheTokenExactly_CreatingTheFolder()
        {
            var store = new RestoreTokenStore(FilePath);
            store.Save("tok en");
            Assert.Equal("tok en", File.ReadAllText(FilePath));
            Assert.False(File.Exists(FilePath + ".tmp"));
        }

        [Fact]
        public void CT91_Save_ShorterToken_LeavesNoRestOfTheLongerOne()
        {
            var store = new RestoreTokenStore(FilePath);
            store.Save(new string('x', 1000));
            store.Save("a");
            Assert.Equal("a", File.ReadAllText(FilePath));
            Assert.Equal("a", store.Load());
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\r\n\t")]
        public void CT91_Save_EmptyOrBlankToken_ForgetsTheOldOne_LikeNull(string _token)
        {
            // Load reads such a file as no token: writing one would only leave an empty file behind. The old token is single
            // use and gone either way (CT-91), so it is forgotten as for null.
            var store = new RestoreTokenStore(FilePath);
            store.Save("old");
            store.Save(_token);
            Assert.False(File.Exists(FilePath));
            Assert.False(File.Exists(FilePath + ".tmp"));
            Assert.Null(store.Load());
        }

        [Fact]
        public void CT91_Save_Null_WithoutAFile_DoesNothing()
        {
            var store = new RestoreTokenStore(FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            store.Save(null);
            Assert.False(File.Exists(FilePath));
            Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(FilePath)!));
        }

        [Fact]
        public void CT145_Save_Null_WithoutTheFolder_DoesNotThrow()
        {
            // The folder does not exist: forgetting the token neither throws nor creates the folder.
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                new RestoreTokenStore(FilePath).Save(null);
            }
            Assert.False(Directory.Exists(dir));
        }

        [Fact]
        public void CT145_Save_Null_PathIsAFolder_IsLogged()
        {
            Directory.CreateDirectory(FilePath);
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                new RestoreTokenStore(FilePath).Save(null);
            }
            Assert.True(Directory.Exists(FilePath));
            Assert.Contains("restore token not saved", log.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void CT145_Save_FolderCannotBeCreated_IsLogged_NothingLeft()
        {
            // ~/.hider is a file: the token cannot be kept; the next start shows the dialog again.
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ".hider"), "file");
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                new RestoreTokenStore(FilePath).Save("token");
            }
            Assert.Equal("file", File.ReadAllText(Path.Combine(dir, ".hider")));
            Assert.Contains("restore token not saved", log.ToString(), StringComparison.Ordinal);
            Assert.Null(new RestoreTokenStore(FilePath).Load());
        }

        [Fact]
        public void CT145_Save_TempIsAFolder_TokenNotSaved_LoggedTwice()
        {
            // A folder named restore_token.tmp: the temp file cannot be made, nor removed.
            Directory.CreateDirectory(Path.Combine(FilePath + ".tmp", "inside"));
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                new RestoreTokenStore(FilePath).Save("token");
            }
            string text = log.ToString();
            Assert.Contains("restore token not saved", text, StringComparison.Ordinal);
            Assert.Contains("could not delete", text, StringComparison.Ordinal);
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void CT91_TwoStores_OnOneFile_SeeEachOthersToken()
        {
            var a = new RestoreTokenStore(FilePath);
            var b = new RestoreTokenStore(FilePath);
            a.Save("from-a");
            Assert.Equal("from-a", b.Load());
            b.Save(null);
            Assert.Null(a.Load());
        }
    }
}
