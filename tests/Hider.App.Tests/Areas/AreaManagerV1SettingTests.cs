using Avalonia.Headless.XUnit;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;

namespace Hider.App.Tests.Areas
{
    /// <summary>Tests that point LocalApplicationData (XDG_DATA_HOME on Linux) elsewhere: alone, after the parallel ones.</summary>
    [CollectionDefinition(nameof(LocalApplicationDataGroup), DisableParallelization = true)]
    public sealed class LocalApplicationDataGroup
    {
    }

    /// <summary>
    /// FR-65 through the app's own start and exit: a v1 file at v1's place (&lt;LocalApplicationData&gt;/hider_setting/
    /// setting.txt) and one beside areas.json are never read, imported, moved or written.
    /// </summary>
    [Collection(nameof(LocalApplicationDataGroup))]
    public class AreaManagerV1SettingTests
    {
        /// <summary>
        /// Once with the files free (an import, move or write shows; a read that swallows the lock's IOException would hide it
        /// otherwise) and once held without sharing (a read of either file by the app throws instead of passing unseen).
        /// </summary>
        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void GT133_AV1SettingTxt_IsNeverRead_TheAppStartsWithNoAreas_AndLeavesTheFileAsItWas(bool _hold)
        {
            using var v1 = new V1SettingFile();
            using var f = new AreaHarness();
            string beside = Path.Combine(f.Dir, "setting.txt");
            V1SettingFile.Write(beside);
            using (v1.HoldIf(_hold))
            using (_hold ? V1SettingFile.Hold(beside) : null)
            {
                if (_hold)
                {
                    Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(v1.FilePath));
                }
                Assert.Equal(LoadStatus.Missing, f.Manager.Restore());
                Assert.Empty(f.Manager.Areas);
                Assert.Empty(f.Hosts);
                f.Manager.SaveNow();
                f.Manager.Dispose();
            }
            Assert.Equal(2, f.Manager.SaveCount); // SaveNow and the exit's save, both of the empty list
            Assert.Empty(f.Hosts);
            v1.AssertUntouched();
            Assert.Equal(V1SettingFile.Content, File.ReadAllText(beside));
            Assert.Equal(V1SettingFile.OldTime, File.GetLastWriteTimeUtc(beside));
            var saved = AreasFile.Load(f.AreasPath, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
            Assert.Empty(saved.Areas);
            string[] files = { "areas.json", "setting.txt" };
            Assert.Equal(files, Directory.GetFileSystemEntries(f.Dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
    }
}
