using System.Text;

namespace Hider.Core.Tests.Fakes
{
    /// <summary>
    /// The file Hider v1 kept its areas in, at v1's place (work instructions, appendix: the v1 README):
    /// &lt;LocalApplicationData&gt;/hider_setting/setting.txt, for the tests of FR-65 (never read, never imported).
    /// On Linux LocalApplicationData follows XDG_DATA_HOME, which is pointed at a temporary folder (process-wide: the tests
    /// run in a collection without parallelization). On Windows it is the user's real folder (not redirectable): an existing
    /// v1 file of the user is used as it is (<see cref="Origin"/> says so in every failure), a missing one is made and only
    /// that file (still with the test's bytes and time) is removed afterwards, and one with exactly the test's bytes and time
    /// is a leftover of an interrupted run, used and removed (with its folder, when that is then empty).
    /// A named mutex, held from the constructor to <see cref="Dispose"/>, keeps the two test assemblies (which
    /// <c>dotnet test</c> runs in parallel) from using the place at the same time.
    /// </summary>
    internal sealed class V1SettingFile : IDisposable
    {
        public const string Content = "1\n10,20,300,200\n";

        /// <summary>The time given to a file the test makes, so that any write would change it.</summary>
        public static readonly DateTime OldTime = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        /// <summary>Machine-wide on Windows; on Unix .NET keeps "Global\" names in a shared folder, so across processes too.</summary>
        internal const string MutexName = @"Global\Hider.Tests.V1Setting";

        internal static readonly TimeSpan MutexWait = TimeSpan.FromMinutes(2);

        private static readonly byte[] contentBytes = Encoding.UTF8.GetBytes(Content);

        private readonly Mutex mutex;
        private readonly string? temp;
        private readonly string? savedXdg;
        private readonly bool removeFile;
        private readonly bool madeFile;
        private readonly bool createdDirectory;
        private readonly bool removeEmptyDirectory;
        private bool constructed;
        private bool disposed;

        public V1SettingFile()
        {
            mutex = new Mutex(false, MutexName);
            try
            {
                if (!mutex.WaitOne(MutexWait))
                {
                    throw new TimeoutException($"the v1 setting place is still in use by another test after {MutexWait}");
                }
            }
            catch (AbandonedMutexException)
            {
                // A run that died while holding it: the mutex is ours now, and a file it left is found below.
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hider_setting");
                    FilePath = Path.Combine(Directory, "setting.txt");
                    if (IsTheTestsFile(FilePath))
                    {
                        Origin = "a v1 file left by an earlier, interrupted test run (it is removed afterwards)";
                        removeFile = true;
                        removeEmptyDirectory = true; // the folder that run made, if it holds nothing else
                    }
                    else if (File.Exists(FilePath))
                    {
                        Origin = "the user's own v1 file, used as it is and left in place";
                    }
                    else
                    {
                        createdDirectory = !System.IO.Directory.Exists(Directory);
                        removeEmptyDirectory = createdDirectory;
                        System.IO.Directory.CreateDirectory(Directory);
                        // Set before writing: a write that fails half way (the time not set yet) still leaves the test's
                        // file, removed by Undo below instead of passing for the user's own file on the next run.
                        removeFile = true;
                        madeFile = true;
                        Write(FilePath);
                        Origin = "a v1 file made by the test";
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
                        throw new InvalidOperationException($"LocalApplicationData is {local}, not XDG_DATA_HOME ({temp})");
                    }
                    Directory = Path.Combine(local, "hider_setting");
                    FilePath = Path.Combine(Directory, "setting.txt");
                    System.IO.Directory.CreateDirectory(Directory);
                    Write(FilePath);
                    removeFile = true;
                    Origin = "a v1 file made by the test in a temporary XDG_DATA_HOME";
                }
                Bytes = File.ReadAllBytes(FilePath);
                WriteTime = File.GetLastWriteTimeUtc(FilePath);
                constructed = true;
            }
            catch
            {
                Undo();
                throw;
            }
        }

        public string Directory { get; } = "";
        public string FilePath { get; } = "";
        public byte[] Bytes { get; } = [];
        public DateTime WriteTime { get; }

        /// <summary>Where the file came from: made by the test, left by an earlier run, or the user's own (Windows).</summary>
        public string Origin { get; } = "";

        /// <summary>Writes a v1 file with the old time at <paramref name="_path"/>.</summary>
        public static void Write(string _path)
        {
            File.WriteAllText(_path, Content);
            File.SetLastWriteTimeUtc(_path, OldTime);
        }

        /// <summary>The file at <paramref name="_path"/> has exactly the bytes and the write time the tests give it.</summary>
        internal static bool IsTheTestsFile(string _path) => HasTheTestsBytes(_path, true);

        private static bool HasTheTestsBytes(string _path, bool _andTime)
        {
            try
            {
                return File.Exists(_path)
                    && (!_andTime || File.GetLastWriteTimeUtc(_path) == OldTime)
                    && File.ReadAllBytes(_path).AsSpan().SequenceEqual(contentBytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Opens the file without sharing: while it is held, any read of it (also by .NET on Linux, which locks with flock)
        /// throws IOException.
        /// </summary>
        public static FileStream Hold(string _path) => new(_path, FileMode.Open, FileAccess.Read, FileShare.None);

        public FileStream Hold() => Hold(FilePath);

        /// <summary>
        /// <see cref="Hold()"/> when <paramref name="_hold"/>, else nothing: the tests run once with the file free (an import,
        /// move or write would show) and once held (a read would throw).
        /// </summary>
        public FileStream? HoldIf(bool _hold) => _hold ? Hold() : null;

        /// <summary>The v1 file has its bytes and write time, and (when the test made it) is the only file in its folder.</summary>
        public void AssertUntouched()
        {
            Assert.True(File.Exists(FilePath), $"{FilePath} is gone ({Origin})");
            Assert.True(Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(FilePath)), $"{FilePath} was changed ({Origin})");
            Assert.True(WriteTime == File.GetLastWriteTimeUtc(FilePath), $"{FilePath} was written ({Origin})");
            if (removeFile)
            {
                Assert.True(OldTime == WriteTime, $"{FilePath} does not have the test's time ({Origin})");
            }
            if (createdDirectory || temp is not null)
            {
                Assert.Equal(new[] { FilePath }, System.IO.Directory.GetFileSystemEntries(Directory));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Undo();
        }

        /// <summary>
        /// Puts everything back and lets the place go: the temporary XDG_DATA_HOME (Linux), or on Windows the file only while
        /// it still has the test's bytes and time (a file the user saved meanwhile is kept) and a folder the test made only
        /// when it is empty.
        /// </summary>
        private void Undo()
        {
            try
            {
                if (temp is not null)
                {
                    Environment.SetEnvironmentVariable("XDG_DATA_HOME", savedXdg);
                    if (System.IO.Directory.Exists(temp))
                    {
                        System.IO.Directory.Delete(temp, true);
                    }
                    return;
                }
                // A file the constructor made but could not finish (its time not set) is the test's by its bytes alone.
                if (removeFile && (IsTheTestsFile(FilePath) || (madeFile && !constructed && HasTheTestsBytes(FilePath, false))))
                {
                    File.Delete(FilePath);
                }
                if (removeEmptyDirectory && System.IO.Directory.Exists(Directory) && !System.IO.Directory.EnumerateFileSystemEntries(Directory).Any())
                {
                    System.IO.Directory.Delete(Directory);
                }
            }
            finally
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }
    }
}
