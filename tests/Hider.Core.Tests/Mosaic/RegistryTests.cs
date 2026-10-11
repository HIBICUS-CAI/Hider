using Hider.Core.Model;
using Hider.Core.Mosaic;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class RegistryTests
    {
        // Rename detector: ids are JSON keys and must never change. D-5 fixes the v2.0 set.
        private static readonly string[] KnownIds = { "pixelate", "hexcell", "blur", "block", "noise" };

        [Fact]
        public void CT13_Registry_IdsUniqueAndKnown()
        {
            var ids = MethodRegistry.All.Select(m => m.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.Contains(id, KnownIds));
            Assert.Contains("pixelate", ids);
            Assert.Contains("block", ids);
        }

        [Fact]
        public void CT14_AllSksl_Compile()
        {
            var ids = ShaderSource.All();
            Assert.NotEmpty(ids);
            foreach (string id in ids)
            {
                using var effect = SKRuntimeEffect.CreateShader(ShaderSource.Load(id), out string? errors);
                Assert.True(effect is not null, $"{id}.sksl: {errors}");
            }
            // Every registered method has its shader.
            foreach (var method in MethodRegistry.All)
            {
                Assert.Contains(method.Id, ids);
            }
        }

        [Fact]
        public void CT15_Uniforms_MatchParamSpecsAndFixedUniforms()
        {
            foreach (var method in MethodRegistry.All)
            {
                using var effect = ShaderSource.Compile(method.Id);
                var expected = method.Params.Select(p => p.Key).Concat(CommonParams.FixedUniforms(method.Kind)).Order().ToList();
                Assert.Equal(expected, effect.Uniforms.Order().ToList());
                Assert.Equal(method.Kind == MethodKind.Mosaic ? new[] { "src" } : Array.Empty<string>(), effect.Children.ToArray());
            }
        }

        [Fact]
        public void CT16_PlatformSupport_FiltersMethods()
        {
            var windowsOnly = new DummyMethod("winonly", MethodKind.Mosaic, PlatformSupport.Windows);
            var both = new DummyMethod("both", MethodKind.Mosaic, PlatformSupport.All);
            var linux = MethodRegistry.Filter(new IMosaicMethod[] { windowsOnly, both }, PlatformSupport.Linux);
            Assert.Equal(new[] { "both" }, linux.Select(m => m.Id));
            var windows = MethodRegistry.Filter(new IMosaicMethod[] { windowsOnly, both }, PlatformSupport.Windows);
            Assert.Equal(new[] { "winonly", "both" }, windows.Select(m => m.Id));
            Assert.All(MethodRegistry.ForPlatform(PlatformSupport.Linux), m => Assert.True(m.Support.HasFlag(PlatformSupport.Linux)));
        }

        [Fact]
        public void CT17_MethodKind_SourceRequirement()
        {
            var generated = MethodRegistry.OfKind(MethodKind.Generated).First();
            var inputs = new MosaicInputs { Source = null, AreaSize = new SKSizeI(32, 32) };
            using (var shader = generated.CreateShader(inputs, ParamValues.ForGenerated(generated, new MethodSettings<GeneratedCommon>())))
            {
                Assert.NotNull(shader);
            }
            var mosaic = MethodRegistry.OfKind(MethodKind.Mosaic).First();
            Assert.Throws<ArgumentException>(() => mosaic.CreateShader(inputs, ParamValues.ForMosaic(mosaic, new MethodSettings<MosaicCommon>(), 1.0)));
        }

        [Fact]
        public void CT18_Find_UnknownIsNull()
        {
            Assert.Null(MethodRegistry.Find("does-not-exist"));
            Assert.NotNull(MethodRegistry.Find("pixelate"));
        }

        [Fact]
        public void CT20_LabelKeys_ExistInStrings()
        {
            foreach (var method in MethodRegistry.All)
            {
                Assert.True(Strings.Contains(method.LabelKey), $"{method.Id}: {method.LabelKey}");
                foreach (var spec in method.Params)
                {
                    Assert.True(Strings.Contains(spec.LabelKey), $"{method.Id}.{spec.Key}: {spec.LabelKey}");
                }
            }
            foreach (var spec in CommonParams.Mosaic)
            {
                Assert.True(Strings.Contains(spec.LabelKey), $"common {spec.Key}: {spec.LabelKey}");
            }
        }
    }
}
