using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class RegistryTests
    {
        private static readonly string[] sourceChild = { "src" };
        private static readonly string[] bothOnly = { "both" };
        private static readonly string[] winOnlyAndBoth = { "winonly", "both" };

        // Rename detector: ids are JSON keys and must never change. D-5 fixes the v2.0 set.
        private static readonly string[] knownIds = { "pixelate", "hexcell", "blur", "block", "noise" };

        [Fact]
        public void CT13_Registry_IdsUniqueAndKnown()
        {
            var ids = MethodRegistry.All.Select(_m => _m.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Equal(knownIds.Order(), ids.Order());
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
                var expected = method.Params.Select(_p => _p.Key).Concat(CommonParams.FixedUniforms(method.Kind)).Order().ToList();
                Assert.Equal(expected, effect.Uniforms.Order().ToList());
                Assert.Equal(method.Kind == MethodKind.Mosaic ? sourceChild : Array.Empty<string>(), effect.Children.ToArray());
            }
        }

        [Fact]
        public void CT16_PlatformSupport_FiltersMethods()
        {
            var windowsOnly = new DummyMethod("winonly", MethodKind.Mosaic, PlatformSupport.Windows);
            var both = new DummyMethod("both", MethodKind.Mosaic, PlatformSupport.All);
            var linux = MethodRegistry.Filter(new IMosaicMethod[] { windowsOnly, both }, PlatformSupport.Linux);
            Assert.Equal(bothOnly, linux.Select(_m => _m.Id));
            var windows = MethodRegistry.Filter(new IMosaicMethod[] { windowsOnly, both }, PlatformSupport.Windows);
            Assert.Equal(winOnlyAndBoth, windows.Select(_m => _m.Id));
            Assert.All(MethodRegistry.ForPlatform(PlatformSupport.Linux), _m => Assert.True(_m.Support.HasFlag(PlatformSupport.Linux)));
        }

        [Fact]
        public void CT17_MethodKind_SourceRequirement()
        {
            var generated = MethodRegistry.OfKind(MethodKind.Generated)[0];
            var inputs = new MosaicInputs { Source = null, AreaSize = new SKSizeI(32, 32) };
            using (var shader = generated.CreateShader(inputs, ParamValues.ForGenerated(generated, new MethodSettings<GeneratedCommon>())))
            {
                Assert.NotNull(shader);
            }
            var mosaic = MethodRegistry.OfKind(MethodKind.Mosaic)[0];
            Assert.Throws<ArgumentException>(() => mosaic.CreateShader(inputs, ParamValues.ForMosaic(mosaic, new MethodSettings<MosaicCommon>(), 1.0)));
        }

        [Fact]
        public void CT18_Find_UnknownIsNull()
        {
            Assert.Null(MethodRegistry.Find("does-not-exist"));
            Assert.NotNull(MethodRegistry.Find("pixelate"));
        }

        [Fact]
        public void PassThrough_MatchesTheShader()
        {
            // The pass-through shortcut (CT-43, CT-73, CT-75 rely on it) must give exactly what the method's SkSL gives.
            using var source = SyntheticImage.CreateImage(97, 61);
            var checkedMethods = new HashSet<string>();
            foreach (var method in MethodRegistry.OfKind(MethodKind.Mosaic).OfType<SkslMethod>())
            {
                foreach (double opacity in new[] { 0.0, 1.0 })
                {
                    var p = ParamValues.ForMosaic(method, new MethodSettings<MosaicCommon> { Method = method.Id }, opacity);
                    if (!method.IsPassThrough(p))
                    {
                        continue;
                    }
                    var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
                    try
                    {
                        using var fast = DrawShader(method.CreateShader(inputs, p), source);
                        using var effect = DrawShader(method.CreateEffectShader(inputs, p), source);
                        Assert.True(Golden.MaxDiff(fast, effect) == 0, $"{method.Id} at opacity {opacity}");
                        checkedMethods.Add(method.Id);
                    }
                    finally
                    {
                        MosaicPipeline.Release(inputs, source);
                    }
                }
            }
            // Overriding IsPassThrough is optional for new methods; the built-in ones all use it at opacity 0.
            Assert.Superset(new HashSet<string> { "pixelate", "hexcell", "blur" }, checkedMethods);
        }

        private static SKBitmap DrawShader(SKShader _shader, SKImage _size)
        {
            using (_shader)
            {
                using var paint = new SKPaint { Shader = _shader, BlendMode = SKBlendMode.Src };
                return Render.Draw(_size.Width, _size.Height, _c => _c.DrawRect(0, 0, _size.Width, _size.Height, paint));
            }
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
