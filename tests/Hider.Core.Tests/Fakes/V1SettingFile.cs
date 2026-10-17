namespace Hider.Core.Tests.Fakes
{
    /// <summary>
    /// The file Hider v1 kept its areas in, at v1's place (work instructions, appendix: the v1 README):
    /// &lt;LocalApplicationData&gt;/hider_setting/setting.txt, for the tests of FR-65 (never read, never imported).
    /// On Linux LocalApplicationData follows XDG_DATA_HOME, which is pointed at a temporary folder (process-wide: the tests
    /// run in a collection without parallelization). On Windows it is the user's real folder (not redirectable): an existing
    /// v1 file is used as it is, a missing one is made and removed afterwards.
    /// </summary>
    internal sealed class V1SettingFile : IDisposable
    {
        public const string Content = "1\n10,20,300,200\n";

        /// <summary>The time given to a file the test makes, so that any write would change it.</summary>
        public static readonly DateTime OldTime = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        private readonly string? temp;
        private readonly string? savedXdg;
        private readonly bool createdFile;
        private readonly bool createdDirectory;

        public V1SettingFile()
        {
            if (OperatingSystem.IsWindows())
            {
                Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hider_setting");
                FilePath = Path.Combine(Directory, "setting.txt");
                if (!File.Exists(FilePath))
                {
                    createdDirectory = !System.IO.Directory.Exists(Directory);
                    System.IO.Directory.CreateDirectory(Directory);
                    Write(FilePath);
                    createdFile = true;
                }
            }
            else
            {
                temp = Path.Combine(Path.GetTempPath(), "hider-v1-" + Guid.NewGuid().ToString("N"));
                savedXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                System.IO.Directory.CreateDirectory(temp);
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", temp);
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (local != temp)
                {
                    Environment.SetEnvironmentVariable("XDG_DATA_HOME", savedXdg);
                    System.IO.Directory.Delete(temp, true);
                    throw new InvalidOperationException($"LocalApplicationData is {local}, not XDG_DATA_HOME ({temp})");
                }
                Directory = Path.Combine(local, "hider_setting");
                FilePath = Path.Combine(Directory, "setting.txt");
                System.IO.Directory.CreateDirectory(Directory);
                Write(FilePath);
                createdFile = true;
            }
            Bytes = File.ReadAllBytes(FilePath);
            WriteTime = File.GetLastWriteTimeUtc(FilePath);
        }

        public string Directory { get; }
        public string FilePath { get; }
        public byte[] Bytes { get; }
        public DateTime WriteTime { get; }

        /// <summary>Writes a v1 file with the old time at <paramref name="_path"/>.</summary>
        public static void Write(string _path)
        {
            File.WriteAllText(_path, Content);
            File.SetLastWriteTimeUtc(_path, OldTime);
        }

        /// <summary>
        /// Opens the file without sharing: while it is held, any read of it (also by .NET on Linux, which locks with flock)
        /// throws IOException.
        /// </summary>
        public static FileStream Hold(string _path) => new(_path, FileMode.Open, FileAccess.Read, FileShare.None);

        public FileStream Hold() => Hold(FilePath);

        /// <summary>The v1 file has its bytes and write time, and (when the test made it) is the only file in its folder.</summary>
        public void AssertUntouched()
        {
            Assert.Equal(Bytes, File.ReadAllBytes(FilePath));
            Assert.Equal(WriteTime, File.GetLastWriteTimeUtc(FilePath));
            if (createdFile)
            {
                Assert.Equal(OldTime, WriteTime);
            }
            if (createdDirectory || temp is not null)
            {
                Assert.Equal(new[] { FilePath }, System.IO.Directory.GetFileSystemEntries(Directory));
            }
        }

        public void Dispose()
        {
            if (temp is not null)
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", savedXdg);
                System.IO.Directory.Delete(temp, true);
                return;
            }
            if (createdFile)
            {
                File.Delete(FilePath);
            }
            if (createdDirectory)
            {
                System.IO.Directory.Delete(Directory);
            }
        }
    }
}
