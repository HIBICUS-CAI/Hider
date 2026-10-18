using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests
{
    /// <summary>
    /// What the product must never do, checked on the sources and build files (src/** and the build files of the
    /// repository root): no API that hides Hider's windows from captures (NFR-01), no Wayland backend (D-1), no system move /
    /// resize drag, no DllImport, no assembly loading at run time (D-12), no reflection-based JSON (NFR-06), no reading of
    /// the old setting.txt (FR-65), no NuGet package outside the allow list (CLAUDE.md). A regression here would pass every
    /// behavioural test on CI (captures in CI do not show recordings), so the rules are checked on the code itself.
    /// </summary>
    public class RepositoryRulesTests
    {
        internal static string RepositoryRoot()
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

        private static bool IsMsBuildFile(string _file)
        {
            return _file.EndsWith(".csproj", StringComparison.Ordinal) || _file.EndsWith(".props", StringComparison.Ordinal)
                || _file.EndsWith(".targets", StringComparison.Ordinal);
        }

        /// <summary>
        /// Every file under <paramref name="_dir"/> (path relative to <paramref name="_root"/>, with '/'), leaving out build
        /// output (bin/, obj/) and hidden folders (.git/, .claude/ and its worktrees).
        /// </summary>
        internal static IEnumerable<string> FilesUnder(string _root, string _dir)
        {
            foreach (string file in Directory.EnumerateFiles(_dir))
            {
                yield return Path.GetRelativePath(_root, file).Replace('\\', '/');
            }
            foreach (string sub in Directory.EnumerateDirectories(_dir))
            {
                string name = Path.GetFileName(sub);
                if (name is "bin" or "obj" || name.StartsWith('.'))
                {
                    continue;
                }
                foreach (string file in FilesUnder(_root, sub))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// The files that make the product (path relative to <paramref name="_root"/>): the sources, project and build files
        /// and lock files under src/, and the build files of the root, which src/ imports.
        /// </summary>
        internal static IEnumerable<string> ProductFiles(string _root)
        {
            foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            {
                if (File.Exists(Path.Combine(_root, name)))
                {
                    yield return name;
                }
            }
            foreach (string relative in FilesUnder(_root, Path.Combine(_root, "src")))
            {
                if (relative.EndsWith(".cs", StringComparison.Ordinal) || relative.EndsWith(".axaml", StringComparison.Ordinal)
                    || IsMsBuildFile(relative) || Path.GetFileName(relative) == "packages.lock.json")
                {
                    yield return relative;
                }
            }
        }

        /// <summary>The text of a file with its comments left out (C# or XML; JSON has none).</summary>
        internal static string CodeOf(string _relative, string _text)
        {
            if (_relative.EndsWith(".cs", StringComparison.Ordinal))
            {
                return SourceText.StripCSharp(_text);
            }
            return _relative.EndsWith(".json", StringComparison.Ordinal) ? _text : SourceText.StripXml(_text);
        }

        /// <summary>Every file of the product with its comments left out.</summary>
        private static IEnumerable<(string File, string Code)> SourceFiles()
        {
            string root = RepositoryRoot();
            foreach (string relative in ProductFiles(root))
            {
                yield return (relative, CodeOf(relative, File.ReadAllText(Path.Combine(root, relative))));
            }
        }

        /// <summary>
        /// Where <paramref name="_pattern"/> matches <paramref name="_code"/> ("line: text of that line"). The whole text is
        /// matched, so a call split over lines is found too; the line is that of the start of the match.
        /// </summary>
        internal static IEnumerable<string> Hits(Regex _pattern, string _code)
        {
            foreach (Match match in _pattern.Matches(_code))
            {
                int start = match.Index == 0 ? 0 : _code.LastIndexOf('\n', match.Index - 1) + 1;
                int end = _code.IndexOf('\n', match.Index);
                int line = 1 + _code.AsSpan(0, match.Index).Count('\n');
                yield return $"{line}: {_code[start..(end < 0 ? _code.Length : end)].Trim()}";
            }
        }

        /// <summary>The lines (comments left out) of every file of the product.</summary>
        private static IEnumerable<(string File, int Line, string Text)> SourceLines()
        {
            foreach (var (relative, code) in SourceFiles())
            {
                int n = 0;
                foreach (string line in code.Split('\n'))
                {
                    n++;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        yield return (relative, n, line.TrimEnd('\r'));
                    }
                }
            }
        }

        /// <summary>Each rule: what it protects, and the pattern of code that breaks it.</summary>
        internal static readonly Dictionary<string, (string Rule, string Pattern)> Rules = new()
        {
            ["NFR-01"] = ("NFR-01: never exclude Hider's windows from captures", @"SetWindowDisplayAffinity|WDA_EXCLUDEFROMCAPTURE|WDA_MONITOR"),
            ["D-1"] = ("D-1: XWayland only", @"Avalonia\.Wayland|\bUseWayland\b"),
            ["drag"] = ("drags are Hider's own (work instructions §2.2)", @"\bBegin(Move|Resize)Drag\b"),
            ["DllImport"] = ("P/Invoke is LibraryImport", @"\bDllImport(Attribute)?\b"),
            // The ways in are banned as names, not as call shapes: a call through a variable, over two lines or with named
            // arguments has the same name. AppDomain stays usable for its events only; Activator for a type the code names;
            // Type.GetType not at all (typeof names a type; a string may name another assembly). A method group is a use too,
            // and so is an alias or a using static of these types, written with global:: or inside a namespace block.
            ["D-12"] = ("D-12: no assemblies loaded at run time", string.Join('|',
                @"\bAssembly\s*\.\s*\w*Load\w*\b",
                @"\bAssemblyLoadContext\b",
                @"\bLoadFrom(AssemblyPath|AssemblyName|Stream|NativeImagePath)\b",
                @"\bAppDomain\b(?!\s*\.\s*CurrentDomain\s*\.\s*(UnhandledException|ProcessExit|FirstChanceException)\b)",
                @"\.\s*ExecuteAssembly\w*\s*\(",
                @"\bActivator\b(?!\s*\.\s*CreateInstance\s*(<|\(\s*typeof\s*\())",
                @"\bType\s*\.\s*GetType\b(?!\s*\(\s*\))",
                @"\bGetDomain\b",
                @"\busing\s+(static\s+)?(\w+\s*=\s*)?(global\s*::\s*)?(System\s*\.\s*)?(Reflection\s*\.\s*Assembly|Runtime\s*\.\s*Loader|Assembly|Activator|AppDomain|Type)\b")),
            ["FR-65"] = ("FR-65: the old setting.txt is never read", @"setting\.txt|hider_setting"),
        };

        public static TheoryData<string> RuleIds() => new(Rules.Keys);

        [Theory]
        [MemberData(nameof(RuleIds))]
        public void CT150_TheProductNeverUses(string _id)
        {
            var (rule, pattern) = Rules[_id];
            var regex = new Regex(pattern);
            var hits = SourceFiles().SelectMany(_f => Hits(regex, _f.Code).Select(_h => $"{_f.File}:{_h}")).ToList();
            Assert.True(hits.Count == 0, $"{rule}:\n{string.Join('\n', hits)}");
        }

        public static TheoryData<string, string, bool> PatternCases() => new()
        {
            { "NFR-01", "SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);", true },
            { "NFR-01", "private const uint WDA_MONITOR = 1;", true },
            { "NFR-01", "DwmGetWindowAttribute(hwnd, DwmCloaked, out int cloaked, sizeof(int));", false },

            { "D-1", "<PackageReference Include=\"Avalonia.Wayland\" />", true },
            { "D-1", "<GlobalPackageReference Include=\"Avalonia.Wayland\" Version=\"12.1.4\" />", true },
            { "D-1", "  .UseWayland()", true },
            { "D-1", "Func<AppBuilder, AppBuilder> f = AppBuilderExtensions.UseWayland;", true },
            { "D-1", "  .UseX11()", false },
            { "D-1", "<PackageReference Include=\"Avalonia.Desktop\" />", false },

            { "drag", "BeginMoveDrag(_e);", true },
            { "drag", "window.BeginResizeDrag (WindowEdge.East, _e);", true },
            { "drag", "Action<WindowEdge, PointerPressedEventArgs> resize = BeginResizeDrag;", true },
            { "drag", "private void StartMoveDragging(PointerPressedEventArgs _e)", false },
            { "drag", "OnBeginMoveDragged();", false },

            { "DllImport", "[DllImport(\"user32.dll\")]", true },
            { "DllImport", "[ DllImport(\"user32.dll\") ]", true },
            { "DllImport", "[System.Runtime.InteropServices.DllImport(\"user32.dll\")]", true },
            { "DllImport", "[DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport(\"user32.dll\")]", true },
            { "DllImport", "[DllImportAttribute(\"user32.dll\")]", true },
            { "DllImport", "[LibraryImport(\"user32.dll\")]", false },
            { "DllImport", "NativeLibrary.SetDllImportResolver(typeof(Hidercap).Assembly, Resolve);", false },
            { "DllImport", "[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]", false },

            { "D-12", "var a = Assembly.Load(bytes);", true },
            { "D-12", "Assembly.LoadFrom(path)", true },
            { "D-12", "System.Reflection.Assembly.LoadFile(path)", true },
            { "D-12", "Assembly.UnsafeLoadFrom(path)", true },
            { "D-12", "new System.Runtime.Loader.AssemblyLoadContext(\"plugins\")", true },
            { "D-12", "context.LoadFromAssemblyPath(path)", true },
            { "D-12", "context.LoadFromStream(stream)", true },
            { "D-12", "AppDomain.CurrentDomain.Load(_path);", true },
            { "D-12", "AppDomain.CurrentDomain.ExecuteAssembly(path);", true },
            { "D-12", "AppDomain.CurrentDomain.CreateInstanceAndUnwrap(\"Plugin\", \"Plugin.Entry\");", true },
            { "D-12", "domain.ExecuteAssemblyByName(name);", true },
            { "D-12", "Activator.CreateInstanceFrom(path, \"Plugin.Entry\")", true },
            { "D-12", "Activator.CreateInstance(\"Plugin\", \"Plugin.Entry\")", true },
            { "D-12", "Type.GetType(\"Plugin.Entry, Plugin\")", true },
            { "D-12", "var domain = AppDomain.CurrentDomain;\ndomain.Load(_name);", true },
            { "D-12", "AppDomain.CurrentDomain\n    .Load(_name);", true },
            { "D-12", "using static System.Reflection.Assembly;\nvar a = Load(_name);", true },
            { "D-12", "var a = Assembly\n    .Load(_name);", true },
            { "D-12", "private const string PluginType = \"Plugin.Entry, Plugin\";\nvar t = Type.GetType(PluginType);", true },
            { "D-12", "Type.GetType(typeName: \"Plugin.Entry, Plugin\")", true },
            { "D-12", "Activator.CreateInstance(assemblyName: \"Plugin\", typeName: \"Plugin.Entry\")", true },
            { "D-12", "Type.GetType(\"System.Int32\")", true },
            { "D-12", "using Asm = System.Reflection.Assembly;", true },
            { "D-12", "var a = Thread.GetDomain().Load(_name);", true },
            { "D-12", "Func<string, Assembly> f = Assembly.Load;", true },
            { "D-12", "Func<string, Type?> g = Type.GetType;", true },
            { "D-12", "using static global::System.Reflection.Assembly;\nvar a = Load(_name);", true },
            { "D-12", "using Asm = global::System.Reflection.Assembly;", true },
            { "D-12", "using System.Reflection;\nnamespace N\n{\n    using Asm = Assembly;\n}", true },
            { "D-12", "using System.Reflection;\nnamespace N\n{\n    using static Assembly;\n}", true },
            { "D-12", "using T = System.Type;\nvar t = T.GetType(\"Plugin.Entry, Plugin\");", true },
            { "D-12", "using static System.Runtime.Loader.AssemblyLoadContext;", true },
            { "D-12", "var state = AreasFile.Load(path, out var status);", false },
            { "D-12", "using var s = typeof(SkslMethod).Assembly.GetManifestResourceStream(name);", false },
            { "D-12", "AppDomain.CurrentDomain.UnhandledException += OnUnhandled;", false },
            { "D-12", "AppDomain.CurrentDomain\n    .ProcessExit += OnExit;", false },
            { "D-12", "Activator.CreateInstance(typeof(Probe))", false },
            { "D-12", "Activator.CreateInstance<Probe>()", false },
            { "D-12", "Type t = value.GetType();", false },
            { "D-12", "using System.Reflection;", false },
            { "D-12", "using System.Runtime.InteropServices;", false },
            { "D-12", "using var stream = File.OpenRead(path);", false },
            { "D-12", "using Types = Hider.Core.Model;", false },
            { "D-12", "var location = typeof(Program).Assembly.Location;", false },
            { "D-12", "AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;", false },

            { "FR-65", "Path.Combine(dir, \"setting.txt\")", true },
            { "FR-65", "Path.Combine(local, \"hider_setting\")", true },
            { "FR-65", "Path.Combine(dir, \"areas.json\")", false },
            { "FR-65", "public const string AreasFileName = \"settings.json\";", false },
        };

        [Theory]
        [MemberData(nameof(PatternCases))]
        public void CT150_EachPattern_CatchesTheWaysToBreakItsRule(string _id, string _code, bool _hit)
        {
            Assert.Equal(_hit, Hits(new Regex(Rules[_id].Pattern), _code).Any());
        }

        [Fact]
        public void CT150_AHit_NamesTheLineWhereItStarts()
        {
            string code = "namespace N\n{\n    var d = AppDomain.CurrentDomain\n        .Load(x);\n}";
            Assert.Equal("3: var d = AppDomain.CurrentDomain", Assert.Single(Hits(new Regex(Rules["D-12"].Pattern), code)));
            Assert.Equal("1: BeginMoveDrag(e);|2: x.BeginResizeDrag(w, e);", string.Join('|', Hits(new Regex(Rules["drag"].Pattern), "BeginMoveDrag(e);\nx.BeginResizeDrag(w, e);")));
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
            // A wrong root would pass every rule with nothing scanned; the build files src/ imports are read too.
            var files = SourceLines().Select(_l => _l.File).Distinct().ToList();
            Assert.Contains("src/Hider.Core/Persistence/AreasFile.cs", files);
            Assert.Contains("src/Hider/Areas/AreaWindow.axaml.cs", files);
            Assert.Contains("src/Hider/Areas/AreaWindow.axaml", files);
            Assert.Contains("src/Hider/Hider.csproj", files);
            Assert.Contains("src/Directory.Build.props", files);
            Assert.Contains("src/Hider/packages.lock.json", files);
            Assert.Contains("Directory.Build.props", files);
            Assert.Contains("Directory.Build.targets", files);
            Assert.Contains("Directory.Packages.props", files);
            Assert.True(files.Count > 50, $"{files.Count} files");
            Assert.DoesNotContain(files, _f => _f.StartsWith("tests/", StringComparison.Ordinal) || _f.Contains("/obj/", StringComparison.Ordinal));
        }

        [Fact]
        public void CT150_TheScan_TakesEveryBuildFileUnderSrc_AndTheRootOnes_NotBuildOutputOrTests()
        {
            string root = Path.Combine(Path.GetTempPath(), "hider-scan-" + Guid.NewGuid().ToString("N"));
            string[] files =
            {
                "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "README.md", "global.json",
                "src/Directory.Build.targets", "src/A/A.csproj", "src/A/X.targets", "src/A/Y.props", "src/A/C.cs", "src/A/V.axaml",
                "src/A/packages.lock.json", "src/A/notes.txt", "src/A/obj/G.cs", "src/A/bin/Release/B.targets", "src/.hidden/H.cs",
                "tests/T/T.cs", "tests/T/T.csproj", "tools/fakeportal/F.cs",
            };
            foreach (string file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, file))!);
                File.WriteAllText(Path.Combine(root, file), "");
            }
            try
            {
                string[] expected =
                {
                    "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "src/A/A.csproj", "src/A/C.cs",
                    "src/A/V.axaml", "src/A/X.targets", "src/A/Y.props", "src/A/packages.lock.json", "src/Directory.Build.targets",
                };
                Assert.Equal(expected, ProductFiles(root).Order(StringComparer.Ordinal));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        public static TheoryData<string, string, string> CSharpStripCases() => new()
        {
            { "a URL in a string is code", "var u = \"https://example.com/x\"; // note", "var u = \"https://example.com/x\";        " },
            { "a comment at the end of a line", "x = \"areas.json\"; // FR-65: not setting.txt", "x = \"areas.json\";                          " },
            { "a block comment over lines", "a /* one\nsetting.txt\n*/ b", "a       \n           \n   b" },
            { "a line starting with * is code", "return 1\n    * new AssemblyLoadContext(\"p\");", "return 1\n    * new AssemblyLoadContext(\"p\");" },
            { "a doc comment", "/// <summary>BeginMoveDrag</summary>\nvoid F();", "                                    \nvoid F();" },
            { "comment markers in a verbatim string", "var p = @\"C:\\a\"\"//b\"; // c", "var p = @\"C:\\a\"\"//b\";     " },
            { "an escaped quote in a string", "s = \"a\\\"// b\"; /* c */", "s = \"a\\\"// b\";        " },
            { "a quote as a character", "c = '\"'; // \"x\"", "c = '\"';       " },
            { "an interpolated string with a string and a comment in a hole", "s = $\"{F(\"//\") /* c */}//\"; // d", "s = $\"{F(\"//\")        }//\";     " },
            { "doubled braces are text", "s = $\"{{//}}\"; // d", "s = $\"{{//}}\";     " },
            { "a raw string over lines", "s = \"\"\"\n// not a comment\n\"\"\"; // c", "s = \"\"\"\n// not a comment\n\"\"\";     " },
            { "an interpolated raw string", "s = $$\"\"\"{x} {{F(/* c */1)}}\"\"\"; // d", "s = $$\"\"\"{x} {{F(       1)}}\"\"\";     " },
            { "a verbatim interpolated string", "s = @$\"\"\"{a}//\"; // d", "s = @$\"\"\"{a}//\";     " },
            { "a division is no comment", "x = a / b * c; // d", "x = a / b * c;     " },
            { "the format of a hole is text", "s = $\"{d:yyyy//MM}\"; // c", "s = $\"{d:yyyy//MM}\";     " },
            { "a format over lines in a verbatim string is text", "s = @$\"{x:yy\n// zz}\"; Assembly.Load(x); // c", "s = @$\"{x:yy\n// zz}\"; Assembly.Load(x);     " },
            { "a named argument in a hole is code", "s = $\"{F(x: 1) /* c */}\";", "s = $\"{F(x: 1)        }\";" },
            { "a directive line holds no code", "#region a /* b\nx = 1; // c", "              \nx = 1;     " },
            { "an indented directive", "  #error \"x\nAssembly.Load(b);", "           \nAssembly.Load(b);" },
            { "a # not at the start of a line is code", "x = a; # y", "x = a; # y" },
        };

        [Theory]
        [MemberData(nameof(CSharpStripCases))]
        public void CT150_CSharpComments_AreLeftOut_StringsAndCodeStay(string _case, string _source, string _expected)
        {
            Assert.True(_expected == SourceText.StripCSharp(_source), _case);
            Assert.Equal(_source.Length, SourceText.StripCSharp(_source).Length);
        }

        public static TheoryData<string, string, string> XmlStripCases() => new()
        {
            { "a comment on one line", "<A /> <!-- Avalonia.Wayland --> <B />", "<A />                           <B />" },
            { "a comment over lines", "<!-- line one\n  Avalonia.Wayland is never referenced -->\n<A />", "             \n                                          \n<A />" },
            { "an unclosed comment runs to the end", "<A /><!-- x\ny", "<A />      \n " },
            { "text outside comments stays", "<PackageReference Include=\"Avalonia.Wayland\" />", "<PackageReference Include=\"Avalonia.Wayland\" />" },
            { "a comment marker in CDATA is text", "<A><![CDATA[ <!-- ]]></A> <!-- c --> <B />", "<A><![CDATA[ <!-- ]]></A>            <B />" },
            { "a comment marker in a processing instruction is text", "<?pi <!-- ?><A /> <!-- c --> <B />", "<?pi <!-- ?><A />            <B />" },
        };

        [Theory]
        [MemberData(nameof(XmlStripCases))]
        public void CT150_XmlComments_AreLeftOut(string _case, string _source, string _expected)
        {
            Assert.True(_expected == SourceText.StripXml(_source), _case);
        }

        [Fact]
        public void CT150_TheRules_CanBeNamedInComments()
        {
            // Why a rule exists is written next to it: a comment naming the forbidden API is not a hit (but code is).
            string cs = "// BeginMoveDrag is not used: drags are Hider's own\nvar f = \"areas.json\"; // not setting.txt\n/* [DllImport] */";
            foreach (var (_, pattern) in Rules.Values)
            {
                Assert.DoesNotMatch(new Regex(pattern), SourceText.StripCSharp(cs));
            }
            string xml = "<Project>\n  <!-- Avalonia.Wayland\n       is never referenced (D-1) -->\n</Project>";
            Assert.DoesNotMatch(new Regex(Rules["D-1"].Pattern), SourceText.StripXml(xml));
            Assert.Matches(new Regex(Rules["D-12"].Pattern), SourceText.StripCSharp("return 1\n    * new System.Runtime.Loader.AssemblyLoadContext(\"plugins\");"));
        }

        /// <summary>
        /// The allow list of NuGet packages (CLAUDE.md, work instructions §2.2): which names, and where they may be referenced.
        /// The central file may declare the version of a test-only package (PackageVersion), but not reference it for every
        /// project (GlobalPackageReference, which reaches src/ too).
        /// </summary>
        private static bool Allowed(string _name, string _file, string _kind, out string _why)
        {
            bool tests = _file.StartsWith("tests/", StringComparison.Ordinal);
            bool central = _file == "Directory.Packages.props" && _kind == "PackageVersion";
            bool bench = _file.Contains("bench", StringComparison.OrdinalIgnoreCase);
            _why = "";
            if (_name == "Avalonia.Wayland")
            {
                _why = "D-1";
                return false;
            }
            if (_name is "Avalonia.Headless.XUnit" || _name.StartsWith("xunit", StringComparison.Ordinal) || _name == "Microsoft.NET.Test.Sdk")
            {
                _why = "tests only";
                return tests || central;
            }
            if (_name == "BenchmarkDotNet")
            {
                _why = "a bench project only";
                return bench || central;
            }
            return _name == "Avalonia" || _name.StartsWith("Avalonia.", StringComparison.Ordinal) || _name == "SkiaSharp"
                || _name.StartsWith("SkiaSharp.NativeAssets.", StringComparison.Ordinal) || _name == "Tmds.DBus.Protocol";
        }

        /// <summary>Every package reference and version of the MSBuild files of the repository (file, element, name, version).</summary>
        private static List<(string File, string Kind, string Name, string? Version)> PackageItems()
        {
            string root = RepositoryRoot();
            var items = new List<(string, string, string, string?)>();
            foreach (string relative in FilesUnder(root, root))
            {
                if (IsMsBuildFile(relative))
                {
                    items.AddRange(PackageItemsOf(relative, XDocument.Load(Path.Combine(root, relative))));
                }
            }
            return items;
        }

        /// <summary>
        /// The package items of one MSBuild file. A version may be an attribute or a child element (Version, VersionOverride),
        /// which MSBuild reads alike.
        /// </summary>
        internal static IEnumerable<(string File, string Kind, string Name, string? Version)> PackageItemsOf(string _file, XDocument _project)
        {
            foreach (var element in _project.Descendants())
            {
                string kind = element.Name.LocalName;
                if (kind is "PackageVersion" or "PackageReference" or "GlobalPackageReference")
                {
                    string name = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update") ?? "";
                    string? version = (string?)element.Attribute("Version") ?? (string?)element.Attribute("VersionOverride")
                        ?? element.Elements().Where(_c => _c.Name.LocalName is "Version" or "VersionOverride").Select(_c => _c.Value.Trim()).FirstOrDefault();
                    yield return (_file, kind, name, version);
                }
            }
        }

        /// <summary>What is wrong with the package items: names off the allow list, versions outside the central file or not the allowed ones.</summary>
        internal static List<string> PackageProblems(IEnumerable<(string File, string Kind, string Name, string? Version)> _items, string _skia, string _dbus)
        {
            var problems = new List<string>();
            foreach (var (file, kind, name, version) in _items)
            {
                if (!Allowed(name, file, kind, out string why))
                {
                    problems.Add($"{file}: {kind} {name} is not allowed{(why.Length > 0 ? " (" + why + ")" : "")}");
                }
                // PackageVersion and GlobalPackageReference carry the version, in the central file only.
                bool declares = kind is "PackageVersion" or "GlobalPackageReference";
                if (!declares && version is not null)
                {
                    problems.Add($"{file}: {kind} {name} has its own version {version} (versions are in Directory.Packages.props)");
                }
                if (declares && file != "Directory.Packages.props")
                {
                    problems.Add($"{file}: {kind} {name} outside Directory.Packages.props");
                }
                if (!declares || version is null)
                {
                    continue;
                }
                if ((name == "Avalonia" || name.StartsWith("Avalonia.", StringComparison.Ordinal)) && !Regex.IsMatch(version, @"^12\.1\.\d+$"))
                {
                    problems.Add($"{name} {version}: Avalonia is 12.1.x");
                }
                if ((name == "SkiaSharp" || name.StartsWith("SkiaSharp.", StringComparison.Ordinal)) && version != _skia)
                {
                    problems.Add($"{name} {version}: SkiaSharp is the version Avalonia.Skia uses ({_skia})");
                }
                if (name == "Tmds.DBus.Protocol" && version != _dbus)
                {
                    problems.Add($"{name} {version}: the version Avalonia.FreeDesktop uses ({_dbus})");
                }
            }
            return problems;
        }

        /// <summary>The version of <paramref name="_dependency"/> that <paramref name="_package"/> depends on (src/Hider's lock file).</summary>
        private static string DependencyVersion(string _package, string _dependency)
        {
            using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Hider", "packages.lock.json")));
            foreach (var framework in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                if (framework.Value.TryGetProperty(_package, out var package) && package.TryGetProperty("dependencies", out var dependencies)
                    && dependencies.TryGetProperty(_dependency, out var version))
                {
                    return version.GetString()!;
                }
            }
            throw new InvalidOperationException($"{_package} -> {_dependency} not in src/Hider/packages.lock.json");
        }

        [Fact]
        public void CT150_NuGetPackages_AreOnTheAllowList_AtTheAllowedVersions()
        {
            var items = PackageItems();
            var problems = PackageProblems(items, DependencyVersion("Avalonia.Skia", "SkiaSharp"), DependencyVersion("Avalonia.FreeDesktop", "Tmds.DBus.Protocol"));
            Assert.True(problems.Count == 0, string.Join('\n', problems));
            // The check reads the real files: the central versions and the references of the app and of the tests.
            Assert.Contains(items, _i => _i is { File: "Directory.Packages.props", Kind: "PackageVersion", Name: "Avalonia" });
            Assert.Contains(items, _i => _i is { File: "src/Hider/Hider.csproj", Kind: "PackageReference", Name: "Avalonia" });
            Assert.Contains(items, _i => _i is { File: "tests/Hider.App.Tests/Hider.App.Tests.csproj", Name: "Avalonia.Headless.XUnit" });
        }

        public static TheoryData<string, string, string, bool> AllowListCases() => new()
        {
            { "Avalonia.Desktop", "src/Hider/Hider.csproj", "PackageReference", true },
            { "SkiaSharp.NativeAssets.Linux", "tests/Hider.Core.Tests/Hider.Core.Tests.csproj", "PackageReference", true },
            { "Tmds.DBus.Protocol", "tools/fakeportal/FakePortal.csproj", "PackageReference", true },
            { "xunit.v3", "tests/Hider.App.Tests/Hider.App.Tests.csproj", "PackageReference", true },
            { "xunit.v3", "Directory.Packages.props", "PackageVersion", true },
            { "xunit.v3", "Directory.Packages.props", "GlobalPackageReference", false },
            { "xunit.v3", "src/Hider/Hider.csproj", "PackageReference", false },
            { "Avalonia.Headless.XUnit", "src/Hider/Hider.csproj", "PackageReference", false },
            { "Microsoft.NET.Test.Sdk", "src/Hider.Core/Hider.Core.csproj", "PackageReference", false },
            { "Avalonia.Wayland", "src/Hider/Hider.csproj", "PackageReference", false },
            { "Avalonia.Wayland", "Directory.Packages.props", "PackageVersion", false },
            { "Newtonsoft.Json", "Directory.Packages.props", "PackageVersion", false },
            { "Newtonsoft.Json", "src/Hider/Hider.csproj", "PackageReference", false },
            { "BenchmarkDotNet", "src/Hider/Hider.csproj", "PackageReference", false },
            { "BenchmarkDotNet", "bench/Hider.Bench/Hider.Bench.csproj", "PackageReference", true },
            { "BenchmarkDotNet", "Directory.Packages.props", "PackageVersion", true },
            { "BenchmarkDotNet", "Directory.Packages.props", "GlobalPackageReference", false },
        };

        [Theory]
        [MemberData(nameof(AllowListCases))]
        public void CT150_TheAllowList(string _name, string _file, string _kind, bool _allowed)
        {
            Assert.Equal(_allowed, Allowed(_name, _file, _kind, out _));
        }

        public static TheoryData<string, string, string, string?> PackageXmlCases() => new()
        {
            { "a version as an attribute", "src/A/A.csproj", "<Project><ItemGroup><PackageReference Include=\"SkiaSharp\" VersionOverride=\"3.119.5\" /></ItemGroup></Project>", "src/A/A.csproj: PackageReference SkiaSharp has its own version 3.119.5 (versions are in Directory.Packages.props)" },
            { "VersionOverride as a child element", "src/A/A.csproj", "<Project><ItemGroup><PackageReference Include=\"SkiaSharp\"><VersionOverride> 3.119.5 </VersionOverride></PackageReference></ItemGroup></Project>", "src/A/A.csproj: PackageReference SkiaSharp has its own version 3.119.5 (versions are in Directory.Packages.props)" },
            { "Version as a child element", "src/A/A.csproj", "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><ItemGroup><PackageReference Include=\"Avalonia\"><Version>12.1.4</Version></PackageReference></ItemGroup></Project>", "src/A/A.csproj: PackageReference Avalonia has its own version 12.1.4 (versions are in Directory.Packages.props)" },
            { "a central version as a child element", "Directory.Packages.props", "<Project><ItemGroup><PackageVersion Include=\"Avalonia\"><Version>12.2.0</Version></PackageVersion></ItemGroup></Project>", "Avalonia 12.2.0: Avalonia is 12.1.x" },
            { "a test package for every project", "Directory.Packages.props", "<Project><ItemGroup><GlobalPackageReference Include=\"xunit.v3\" Version=\"3.2.2\" /></ItemGroup></Project>", "Directory.Packages.props: GlobalPackageReference xunit.v3 is not allowed (tests only)" },
            { "a reference without a version", "src/A/A.csproj", "<Project><ItemGroup><PackageReference Include=\"SkiaSharp\" /></ItemGroup></Project>", null },
        };

        [Theory]
        [MemberData(nameof(PackageXmlCases))]
        public void CT150_PackageItems_ReadVersionsAsAttributesAndChildElements(string _case, string _file, string _xml, string? _problem)
        {
            var problems = PackageProblems(PackageItemsOf(_file, XDocument.Parse(_xml)), "3.119.4", "0.94.1");
            Assert.True(_problem is null ? problems.Count == 0 : problems.SequenceEqual(new[] { _problem }), $"{_case}: {string.Join('\n', problems)}");
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
                V1SettingFile.Write(old);
                using (V1SettingFile.Hold(old))
                {
                    var state = AreasFile.Load(Path.Combine(dir, "areas.json"), out var status);
                    Assert.Equal(LoadStatus.Missing, status);
                    Assert.Empty(state.Areas);
                }
                Assert.Equal(V1SettingFile.Content, File.ReadAllText(old));
                Assert.Equal(V1SettingFile.OldTime, File.GetLastWriteTimeUtc(old));
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
                string old = Path.Combine(dir, "setting.txt");
                File.WriteAllText(old, "garbage that is no areas.json");
                File.SetLastWriteTimeUtc(old, V1SettingFile.OldTime);
                string path = Path.Combine(dir, "areas.json");
                using (V1SettingFile.Hold(old))
                {
                    AreasFile.Save(path, new AppState { Areas = { new AreaState { Id = "0f3b8d2e", X = 1, Y = 2, Width = 100, Height = 50 } } });
                    var state = AreasFile.Load(path, out var status);
                    Assert.Equal(LoadStatus.Loaded, status);
                    Assert.Equal("0f3b8d2e", Assert.Single(state.Areas).Id);
                }
                Assert.Equal("garbage that is no areas.json", File.ReadAllText(old));
                Assert.Equal(V1SettingFile.OldTime, File.GetLastWriteTimeUtc(old));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    /// <summary>FR-65 at v1's own place, &lt;LocalApplicationData&gt;/hider_setting/setting.txt (XDG_DATA_HOME is changed).</summary>
    [Collection(ProcessWideState.Name)]
    public class V1SettingPlaceTests
    {
        /// <summary>
        /// Once with the file free (an import, move or write shows; a read that swallows the lock's IOException would hide it
        /// otherwise) and once held without sharing (a read throws).
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CT149_TheV1FileAtItsPlace_IsNeverRead_NorImported(bool _hold)
        {
            using var v1 = new V1SettingFile();
            string dir = Path.Combine(Path.GetTempPath(), "hider-fr65-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "areas.json");
                using (v1.HoldIf(_hold))
                {
                    if (_hold)
                    {
                        // The lock works on this OS: a read of the v1 file while it is held throws.
                        Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(v1.FilePath));
                    }
                    var state = AreasFile.Load(path, out var status);
                    Assert.Equal(LoadStatus.Missing, status);
                    Assert.Empty(state.Areas);
                    AreasFile.Save(path, state);
                    Assert.Empty(AreasFile.Load(path, out status).Areas);
                    Assert.Equal(LoadStatus.Loaded, status);
                }
                v1.AssertUntouched();
                Assert.Equal("areas.json", Path.GetFileName(Assert.Single(Directory.GetFileSystemEntries(dir))));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void CT149_ThePlace_IsUsedByOneTestAtATime()
        {
            // The named mutex, the same one the other test assembly takes: a second user waits until the first is done.
            using var first = new V1SettingFile(); // disposed again at the end if an assert throws first: a no-op
            using var secondStarted = new ManualResetEventSlim();
            using var secondDone = new ManualResetEventSlim();
            string? secondPath = null;
            // A thread of its own: the mutex belongs to the thread that took it, and is let go on that same thread.
            var second = new Thread(() =>
            {
                secondStarted.Set();
                using (var v1 = new V1SettingFile())
                {
                    secondPath = v1.FilePath;
                }
                secondDone.Set();
            })
            { IsBackground = true };
            second.Start();
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Assert.False(secondDone.Wait(300, TestContext.Current.CancellationToken));
            string firstPath = first.FilePath;
            first.Dispose();
            Assert.True(secondDone.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Assert.True(second.Join(TimeSpan.FromSeconds(30)));
            Assert.NotNull(secondPath);
            if (!OperatingSystem.IsWindows())
            {
                Assert.NotEqual(firstPath, secondPath); // each its own temporary XDG_DATA_HOME
                Assert.False(File.Exists(firstPath));
            }
        }

        [Fact]
        public void CT149_OnlyAFileWithTheTestsBytesAndTime_IsTheTests()
        {
            // What the helper removes, and what it takes for a leftover of an interrupted run; anything else is the user's.
            string dir = Path.Combine(Path.GetTempPath(), "hider-fr65-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "setting.txt");
                Assert.False(V1SettingFile.IsTheTestsFile(path));
                V1SettingFile.Write(path);
                Assert.True(V1SettingFile.IsTheTestsFile(path));
                using (V1SettingFile.Hold(path))
                {
                    Assert.False(V1SettingFile.IsTheTestsFile(path)); // unreadable: not known to be the test's, kept
                }
                File.SetLastWriteTimeUtc(path, V1SettingFile.OldTime.AddSeconds(1));
                Assert.False(V1SettingFile.IsTheTestsFile(path)); // saved again by v1
                File.WriteAllText(path, V1SettingFile.Content + "2\n");
                File.SetLastWriteTimeUtc(path, V1SettingFile.OldTime);
                Assert.False(V1SettingFile.IsTheTestsFile(path)); // other content at the same time
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
