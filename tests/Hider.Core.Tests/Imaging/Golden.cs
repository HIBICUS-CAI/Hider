using SkiaSharp;

namespace Hider.Core.Tests.Imaging
{
    /// <summary>
    /// Golden image comparison (test tab "ゴールデン画像・性能計測の運用"): every pixel, max channel difference within the
    /// test's tolerance. Goldens live in tests/Hider.Core.Tests/Golden and are made on the CPU raster with
    /// HIDER_UPDATE_GOLDEN=1. A failure writes TestResults/golden/&lt;name&gt;_actual.png and _diff.png (uploaded by CI).
    /// </summary>
    public static class Golden
    {
        public static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hider.slnx")))
                {
                    dir = dir.Parent;
                }
                return dir?.FullName ?? throw new InvalidOperationException("Hider.slnx not found above " + AppContext.BaseDirectory);
            }
        }

        public static string GoldenDir => Path.Combine(RepoRoot, "tests", "Hider.Core.Tests", "Golden");
        public static string OutputDir => Path.Combine(RepoRoot, "TestResults", "golden");
        public static bool Updating => Environment.GetEnvironmentVariable("HIDER_UPDATE_GOLDEN") == "1";

        /// <summary>Names written in this run (update mode): one case, one golden.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> written = new();

        /// <summary>Compares with the golden <paramref name="_name"/>, which this test owns (update mode writes it).</summary>
        public static void AssertMatches(string _name, SKBitmap _actual, int _tolerance)
        {
            string path = Path.Combine(GoldenDir, _name + ".png");
            if (Updating)
            {
                // Tests run in parallel: a second writer would race the first and make the golden depend on the order.
                Assert.True(written.TryAdd(_name, true), $"Golden {_name}.png is written by two tests; let one own it (AssertMatchesExisting).");
                Directory.CreateDirectory(GoldenDir);
                Save(_actual, path);
                return;
            }
            Compare(_name, path, _actual, _tolerance);
        }

        /// <summary>
        /// Compares with a golden owned by another test (never written here). In update mode it does nothing: the owner may be
        /// writing the file at the same time; the next normal run checks it against the new golden.
        /// </summary>
        public static void AssertMatchesExisting(string _name, SKBitmap _actual, int _tolerance)
        {
            if (!Updating)
            {
                Compare(_name, Path.Combine(GoldenDir, _name + ".png"), _actual, _tolerance);
            }
        }

        private static void Compare(string _name, string _path, SKBitmap _actual, int _tolerance)
        {
            if (!File.Exists(_path))
            {
                Directory.CreateDirectory(OutputDir);
                Save(_actual, Path.Combine(OutputDir, _name + "_actual.png"));
                Assert.Fail($"Golden {_name}.png is missing; run with HIDER_UPDATE_GOLDEN=1 to create it.");
            }
            using var expected = SKBitmap.Decode(_path);
            Assert.Equal(expected.Width, _actual.Width);
            Assert.Equal(expected.Height, _actual.Height);
            int worst = MaxDiff(expected, _actual, out var diff);
            using (diff)
            {
                if (worst > _tolerance)
                {
                    Directory.CreateDirectory(OutputDir);
                    Save(_actual, Path.Combine(OutputDir, _name + "_actual.png"));
                    Save(diff, Path.Combine(OutputDir, _name + "_diff.png"));
                    Assert.Fail($"{_name}: max channel difference {worst}/255 > {_tolerance}/255 (see {OutputDir})");
                }
            }
        }

        /// <summary>Largest per-channel difference (RGBA, unpremultiplied) and a difference image amplified 16x.</summary>
        public static int MaxDiff(SKBitmap _a, SKBitmap _b, out SKBitmap _diff)
        {
            _diff = new SKBitmap(_a.Width, _a.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            int worst = 0;
            for (int y = 0; y < _a.Height; y++)
            {
                for (int x = 0; x < _a.Width; x++)
                {
                    var ca = _a.GetPixel(x, y);
                    var cb = _b.GetPixel(x, y);
                    int dr = Math.Abs(ca.Red - cb.Red), dg = Math.Abs(ca.Green - cb.Green), db = Math.Abs(ca.Blue - cb.Blue), da = Math.Abs(ca.Alpha - cb.Alpha);
                    worst = Math.Max(worst, Math.Max(Math.Max(dr, dg), Math.Max(db, da)));
                    _diff.SetPixel(x, y, new SKColor((byte)Math.Min(255, dr * 16), (byte)Math.Min(255, dg * 16), (byte)Math.Min(255, db * 16), 255));
                }
            }
            return worst;
        }

        public static int MaxDiff(SKBitmap _a, SKBitmap _b)
        {
            int worst = MaxDiff(_a, _b, out var diff);
            diff.Dispose();
            return worst;
        }

        private static void Save(SKBitmap _bitmap, string _path)
        {
            using var image = SKImage.FromBitmap(_bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(_path);
            data.SaveTo(file);
        }
    }
}
