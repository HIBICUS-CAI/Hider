using Hider.Core.Mosaic;
using Hider.Core.Mosaic.Methods;

namespace Hider.Core.Tests.Mosaic
{
    public class MethodRegistryTests
    {
        private static readonly string[] allInOrder = { "pixelate", "hexcell", "blur", "block", "noise" };
        private static readonly string[] mosaicIds = { "pixelate", "hexcell", "blur" };
        private static readonly string[] generatedIds = { "block", "noise" };
        private static readonly string[] bothAndLinux = { "both", "linux" };
        private static readonly string[] bothAndWin = { "both", "win" };
        private static readonly string[] bothOnly = { "both" };

        [Fact]
        public void CT13_All_InTheOrderOfTheMethodList()
        {
            // The settings list the methods in this order (design canvas 4b).
            Assert.Equal(allInOrder, MethodRegistry.All.Select(_m => _m.Id));
            Assert.IsType<PixelateMethod>(MethodRegistry.All[0]);
            Assert.IsType<HexcellMethod>(MethodRegistry.All[1]);
            Assert.IsType<BlurMethod>(MethodRegistry.All[2]);
            Assert.IsType<BlockMethod>(MethodRegistry.All[3]);
            Assert.IsType<NoiseMethod>(MethodRegistry.All[4]);
        }

        [Fact]
        public void CT18_Find_ReturnsTheRegisteredInstance()
        {
            foreach (var method in MethodRegistry.All)
            {
                Assert.Same(method, MethodRegistry.Find(method.Id));
            }
        }

        [Theory]
        [InlineData("Pixelate")]   // ordinal: case matters
        [InlineData("pixelate ")]
        [InlineData("")]
        [InlineData("does-not-exist")]
        public void CT18_Find_UnknownIsNull(string _id)
        {
            Assert.Null(MethodRegistry.Find(_id));
            Assert.Null(MethodRegistry.Find(_id, MethodKind.Mosaic));
            Assert.Null(MethodRegistry.Find(_id, MethodKind.Generated));
        }

        [Theory]
        [InlineData("pixelate", MethodKind.Mosaic, true)]
        [InlineData("pixelate", MethodKind.Generated, false)]
        [InlineData("hexcell", MethodKind.Mosaic, true)]
        [InlineData("blur", MethodKind.Generated, false)]
        [InlineData("block", MethodKind.Generated, true)]
        [InlineData("block", MethodKind.Mosaic, false)]
        [InlineData("noise", MethodKind.Generated, true)]
        [InlineData("noise", MethodKind.Mosaic, false)]
        public void CT18_Find_WithKind_OnlyAMethodOfThatKind(string _id, MethodKind _kind, bool _found)
        {
            var method = MethodRegistry.Find(_id, _kind);
            if (_found)
            {
                Assert.Same(MethodRegistry.Find(_id), method);
            }
            else
            {
                Assert.Null(method);
            }
        }

        [Fact]
        public void CT13_OfKind_SplitsTheList()
        {
            Assert.Equal(mosaicIds, MethodRegistry.OfKind(MethodKind.Mosaic).Select(_m => _m.Id));
            Assert.Equal(generatedIds, MethodRegistry.OfKind(MethodKind.Generated).Select(_m => _m.Id));
            Assert.All(MethodRegistry.OfKind(MethodKind.Mosaic), _m => Assert.Equal(MethodKind.Mosaic, _m.Kind));
            Assert.All(MethodRegistry.OfKind(MethodKind.Generated), _m => Assert.Equal(MethodKind.Generated, _m.Kind));
            Assert.Same(MethodRegistry.OfKind(MethodKind.Mosaic), MethodRegistry.OfKind(MethodKind.Mosaic)); // built once
        }

        [Theory]
        [InlineData(PlatformSupport.Windows)]
        [InlineData(PlatformSupport.Linux)]
        [InlineData(PlatformSupport.All)]
        public void CT16_ForPlatform_EveryBuiltInMethodRunsEverywhere(PlatformSupport _platform)
        {
            Assert.Equal(allInOrder, MethodRegistry.ForPlatform(_platform).Select(_m => _m.Id));
            Assert.All(MethodRegistry.All, _m => Assert.Equal(PlatformSupport.All, _m.Support));
        }

        [Fact]
        public void CT16_Filter_NeedsEveryRequestedFlag_KeepsTheOrder()
        {
            var win = new DummyMethod("win", MethodKind.Mosaic, PlatformSupport.Windows);
            var linux = new DummyMethod("linux", MethodKind.Generated, PlatformSupport.Linux);
            var both = new DummyMethod("both", MethodKind.Mosaic, PlatformSupport.All);
            var list = new IMosaicMethod[] { both, linux, win };
            Assert.Equal(bothAndLinux, MethodRegistry.Filter(list, PlatformSupport.Linux).Select(_m => _m.Id));
            Assert.Equal(bothAndWin, MethodRegistry.Filter(list, PlatformSupport.Windows).Select(_m => _m.Id));
            Assert.Equal(bothOnly, MethodRegistry.Filter(list, PlatformSupport.All).Select(_m => _m.Id));
            Assert.Empty(MethodRegistry.Filter(Array.Empty<IMosaicMethod>(), PlatformSupport.Linux));
        }

        [Fact]
        public void CT16_CurrentPlatform_IsTheRunningOs()
        {
            // Independent of the implementation's OperatingSystem check: the runtime's own OS identification.
            var expected = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                ? PlatformSupport.Windows
                : PlatformSupport.Linux;
            Assert.Equal(expected, MethodRegistry.CurrentPlatform);
            // One platform, never both: ForPlatform(All) would leave out methods of one OS only.
            Assert.NotEqual(PlatformSupport.All, MethodRegistry.CurrentPlatform);
            Assert.True(MethodRegistry.CurrentPlatform is PlatformSupport.Windows or PlatformSupport.Linux);
            Assert.NotEmpty(MethodRegistry.ForPlatform(MethodRegistry.CurrentPlatform));
        }

        [Fact]
        public void CT13_BuiltInMethods_IdsLabelsAndKinds()
        {
            var expected = new (string Id, string Label, MethodKind Kind)[]
            {
                ("pixelate", "method.pixelate", MethodKind.Mosaic),
                ("hexcell", "method.hexcell", MethodKind.Mosaic),
                ("blur", "method.blur", MethodKind.Mosaic),
                ("block", "method.block", MethodKind.Generated),
                ("noise", "method.noise", MethodKind.Generated),
            };
            Assert.Equal(expected, MethodRegistry.All.Select(_m => (_m.Id, _m.LabelKey, _m.Kind)));
        }
    }
}
