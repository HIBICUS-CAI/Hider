using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class MethodTypesTests
    {
        [Fact]
        public void MethodKind_Values()
        {
            Assert.Equal(0, (int)MethodKind.Mosaic);
            Assert.Equal(1, (int)MethodKind.Generated);
            Assert.Equal(2, Enum.GetValues<MethodKind>().Length);
        }

        [Fact]
        public void PlatformSupport_FlagsAndAll()
        {
            Assert.Equal(1, (int)PlatformSupport.Windows);
            Assert.Equal(2, (int)PlatformSupport.Linux);
            Assert.Equal(PlatformSupport.Windows | PlatformSupport.Linux, PlatformSupport.All);
            Assert.True(PlatformSupport.All.HasFlag(PlatformSupport.Windows));
            Assert.True(PlatformSupport.All.HasFlag(PlatformSupport.Linux));
            Assert.False(PlatformSupport.Windows.HasFlag(PlatformSupport.Linux));
        }

        [Fact]
        public void ParamUnit_ValuesAndTheirTexts()
        {
            var units = Enum.GetValues<ParamUnit>();
            Assert.Equal(new[] { ParamUnit.None, ParamUnit.Px, ParamUnit.Colors, ParamUnit.Steps, ParamUnit.Percent, ParamUnit.Ms }, units);
            foreach (var unit in units)
            {
                Assert.NotNull(Strings.Unit(unit)); // every unit has its text (FR-70)
            }
        }

        [Fact]
        public void MosaicInputs_Defaults()
        {
            var inputs = new MosaicInputs();
            Assert.Null(inputs.Source);
            Assert.Empty(inputs.Pyramid);
            Assert.Equal(default, inputs.AreaSize);
            Assert.Equal(default, inputs.AreaOrigin);
            Assert.Same(Array.Empty<SKImage>(), inputs.Pyramid);
        }

        [Fact]
        public void MosaicInputs_InitProperties()
        {
            using var image = SyntheticImage.CreateImage(4, 3);
            var inputs = new MosaicInputs { Source = image, Pyramid = new[] { image }, AreaSize = new SKSizeI(4, 3), AreaOrigin = new SKPointI(-5, 7) };
            Assert.Same(image, inputs.Source);
            Assert.Same(image, Assert.Single(inputs.Pyramid));
            Assert.Equal(new SKSizeI(4, 3), inputs.AreaSize);
            Assert.Equal(new SKPointI(-5, 7), inputs.AreaOrigin);
        }
    }
}
