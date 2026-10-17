using System.Text.RegularExpressions;
using Hider.Core.Persistence;

namespace Hider.Core.Tests
{
    /// <summary>
    /// What the product must never do, checked on the sources (src/**): no API that hides Hider's windows from captures
    /// (NFR-01), no Wayland backend (D-1), no system move / resize drag, no DllImport, no assembly loading at run time
    /// (D-12), no reflection-based JSON (NFR-06), no reading of the old setting.txt (FR-65). A regression here would pass
    /// every behavioural test on CI (captures in CI do not show recordings), so the rules are checked on the code itself.
    /// </summary>
    public class RepositoryRulesTests
    {
        private static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Hider.slnx")))
                {
                    return dir.FullName;
                }
            }
            throw new InvalidOperationException("Hider.slnx not found above " + AppContext.BaseDirectory);
        }

        /// <summary>The code lines (comments left out) of every source file of the product.</summary>
        private static IEnumerable<(string File, int Line, string Text)> SourceLines()
        {
            string src = Path.Combine(RepositoryRoot(), "src");
            foreach (string file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(src, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                    || !(file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".csproj", StringComparison.Ordinal)
                        || file.EndsWith(".axaml", StringComparison.Ordinal) || file.EndsWith(".props", StringComparison.Ordinal)))
                {
                    continue;
                }
                int n = 0;
                foreach (string line in File.ReadLines(file))
                {
                    n++;
                    string t = line.TrimStart();
                    if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith('*') || t.StartsWith("<!--", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    yield return (relative, n, line);
                }
            }
        }

        public static TheoryData<string, string> ForbiddenPatterns() => new()
        {
            { "NFR-01: never exclude Hider's windows from captures", @"SetWindowDisplayAffinity|WDA_EXCLUDEFROMCAPTURE|WDA_MONITOR" },
            { "D-1: XWayland only", @"Avalonia\.Wayland|UseWayland\s*\(" },
            { "drags are Hider's own (work instructions §2.2)", @"\bBeginMoveDrag\s*\(|\bBeginResizeDrag\s*\(" },
            { "P/Invoke is LibraryImport", @"\[\s*DllImport\b|DllImportAttribute" },
            { "D-12: no assemblies loaded at run time", @"Assembly\.Load(From|File)?\s*\(|AssemblyLoadContext" },
            { "FR-65: the old setting.txt is never read", @"setting\.txt" },
        };

        [Theory]
        [MemberData(nameof(ForbiddenPatterns))]
        public void CT150_TheProductNeverUses(string _rule, string _pattern)
        {
            var regex = new Regex(_pattern);
            var hits = SourceLines().Where(_l => regex.IsMatch(_l.Text)).Select(_l => $"{_l.File}:{_l.Line}: {_l.Text.Trim()}").ToList();
            Assert.True(hits.Count == 0, $"{_rule}:\n{string.Join('\n', hits)}");
        }

        [Fact]
        public void CT150_JsonAlwaysGoesThroughTheSourceGeneratedContext()
        {
            // NFR-06: every (de)serialization call names HiderJsonContext (no reflection-based overloads).
            var call = new Regex(@"JsonSerializer\.(Serialize|Deserialize)");
            var hits = SourceLines().Where(_l => call.IsMatch(_l.Text) && !_l.Text.Contains("HiderJsonContext", StringComparison.Ordinal))
                .Select(_l => $"{_l.File}:{_l.Line}: {_l.Text.Trim()}").ToList();
            Assert.True(hits.Count == 0, string.Join('\n', hits));
            Assert.Contains(SourceLines(), _l => call.IsMatch(_l.Text)); // the check looks at the real calls
        }

        [Fact]
        public void CT150_TheScanSeesTheSources()
        {
            // A wrong root would pass every rule with nothing scanned.
            var files = SourceLines().Select(_l => _l.File).Distinct().ToList();
            Assert.Contains("Hider.Core/Persistence/AreasFile.cs", files);
            Assert.Contains("Hider/Areas/AreaWindow.axaml.cs", files);
            Assert.Contains("Hider/Hider.csproj", files);
            Assert.True(files.Count > 50, $"{files.Count} files");
        }

        [Fact]
        public void CT150_CommentsAreLeftOut_SoTheRulesCanBeNamedThere()
        {
            // AreaWindow's comment names BeginMoveDrag (why drags are Hider's own); only code counts.
            Assert.DoesNotContain(SourceLines(), _l => _l.Text.Contains("BeginMoveDrag", StringComparison.Ordinal));
            Assert.Contains(File.ReadLines(Path.Combine(RepositoryRoot(), "src/Hider/Areas/AreaWindow.axaml.cs")),
                _l => _l.Contains("BeginMoveDrag", StringComparison.Ordinal));
        }

        [Fact]
        public void CT149_OnlyASettingTxt_IsNoAreas_AndItIsLeftAlone()
        {
            // FR-65: a v1 folder has setting.txt and no areas.json: v2 starts empty and never reads, moves or deletes it.
            string dir = Path.Combine(Path.GetTempPath(), "hider-fr65-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string old = Path.Combine(dir, "setting.txt");
                File.WriteAllText(old, "1\n10,20,300,200\n");
                var before = File.GetLastWriteTimeUtc(old);
                var state = AreasFile.Load(Path.Combine(dir, "areas.json"), out var status);
                Assert.Equal(LoadStatus.Missing, status);
                Assert.Empty(state.Areas);
                Assert.Equal("1\n10,20,300,200\n", File.ReadAllText(old));
                Assert.Equal(before, File.GetLastWriteTimeUtc(old));
                Assert.Equal("setting.txt", Path.GetFileName(Assert.Single(Directory.GetFiles(dir))));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void CT149_SettingTxtBesideAreasJson_IsIgnored()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hider-fr65-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "setting.txt"), "garbage that is no areas.json");
                string path = Path.Combine(dir, "areas.json");
                AreasFile.Save(path, new AppState { Areas = { new AreaState { Id = "0f3b8d2e", X = 1, Y = 2, Width = 100, Height = 50 } } });
                var state = AreasFile.Load(path, out var status);
                Assert.Equal(LoadStatus.Loaded, status);
                Assert.Equal("0f3b8d2e", Assert.Single(state.Areas).Id);
                Assert.Equal("garbage that is no areas.json", File.ReadAllText(Path.Combine(dir, "setting.txt")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
