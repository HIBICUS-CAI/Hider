using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    public class ModelTests
    {
        [Fact]
        public void CT01_PhysicalRect_Normalizes()
        {
            var r = new PhysicalRect(100, 50, -30, -20).Normalized();
            Assert.Equal(new PhysicalRect(70, 30, 30, 20), r);
            Assert.Equal(new PhysicalRect(10, 5, 20, 15), PhysicalRect.FromEdges(30, 20, 10, 5));
            Assert.True(r.Width >= 0 && r.Height >= 0);
        }

        [Fact]
        public void CT03_Opacity_ClampedPerPattern()
        {
            Assert.Equal(0.10, Area.ClampOpacity(PatternKind.Fill, 0.0));
            Assert.Equal(0.10, Area.ClampOpacity(PatternKind.Generated, 0.05));
            Assert.Equal(0.0, Area.ClampOpacity(PatternKind.Mosaic, 0.0));
            Assert.Equal(1.0, Area.ClampOpacity(PatternKind.Mosaic, 1.5));
            Assert.Equal(1.0, Area.ClampOpacity(PatternKind.Fill, 3.0));
            var area = new Area { Id = "00000001", Opacity = 0.05, Pattern = PatternKind.Fill };
            Assert.Equal(0.05, area.Opacity);
            Assert.Equal(0.10, area.EffectiveOpacity);
            area.Pattern = PatternKind.Mosaic;
            Assert.Equal(0.05, area.EffectiveOpacity);
        }

        [Fact]
        public void CT09_PatternSwitch_KeepsAllSettings()
        {
            var area = new Area { Id = "00000001" };
            area.Fill.Color = "#2B2F36";
            area.Mosaic.ParamsFor("pixelate")["block"] = 20;
            area.Mosaic.Common.Levels = 8;
            area.Generated.Common.Seed = 42;
            area.Generated.ParamsFor("block")["gap"] = 2;

            foreach (var pattern in new[] { PatternKind.Fill, PatternKind.Generated, PatternKind.Mosaic })
            {
                area.Pattern = pattern;
            }

            Assert.Equal("#2B2F36", area.Fill.Color);
            Assert.Equal(20, area.Mosaic.Params["pixelate"]["block"]);
            Assert.Equal(8, area.Mosaic.Common.Levels);
            Assert.Equal(42, area.Generated.Common.Seed);
            Assert.Equal(2, area.Generated.Params["block"]["gap"]);
        }

        [Fact]
        public void CT11_AreaId_EightHexDigits_NoCollisionIn100k()
        {
            var ids = new HashSet<string>();
            var random = new Random(1234);
            for (int i = 0; i < 100_000; i++)
            {
                string id = AreaId.New(ids.Contains, random);
                Assert.True(AreaId.IsValid(id), id);
                Assert.True(ids.Add(id), $"collision at {i}: {id}");
            }
        }

        [Fact]
        public void CT94_ColorText_ParsesAndFallsBack()
        {
            Assert.Equal((0x68, 0x76, 0x8A), ColorText.Parse("#68768a"));
            Assert.Equal("#68768A", ColorText.OrDefault("#68768a"));
            Assert.Equal(AreaDefaults.FillColor, ColorText.OrDefault("68768A"));
            Assert.Equal(AreaDefaults.FillColor, ColorText.OrDefault("#12345G"));
            Assert.Equal(AreaDefaults.FillColor, ColorText.OrDefault(null));
        }

   }
}
