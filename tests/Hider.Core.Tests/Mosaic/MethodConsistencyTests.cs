using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>
    /// ICpuRenderer paths against the shader they stand in for (the shader is what the GPU draws). Float rounding may move a
    /// boundary by a pixel and linear sampling may differ by 1, so a small share of scattered differing pixels is allowed; a
    /// logic difference changes far more, or concentrates in a row or column.
    /// </summary>
    public class MethodConsistencyTests
    {
        private const int Width = 211, Height = 143;

        [Theory]
        [InlineData("hexcell", "radius", 10)]
        [InlineData("hexcell", "radius", 3)]
        [InlineData("hexcell", "radius", 1.1)]
        [InlineData("hexcell", "radius", 37)]
        public void Mosaic_CpuPathMatchesShader(string _id, string _key, double _value)
        {
            var method = MethodRegistry.Find(_id)!;
            Assert.IsAssignableFrom<ICpuRenderer>(method);
            var settings = new MethodSettings<MosaicCommon> { Method = _id };
            settings.ParamsFor(_id)[_key] = _value;
            var p = ParamValues.ForMosaic(method, settings, 1.0);
            using var source = SyntheticImage.CreateImage(Width, Height);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                AssertClose(method, inputs, p, 0.005);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Theory]
        [InlineData(4, 6, 1234)]
        [InlineData(1, 16, 7)]
        [InlineData(13.5, 2, 65535)]
        [InlineData(64, 64, 42)]
        public void Generated_NoiseCpuPathMatchesShader(double _scale, double _levels, int _seed)
        {
            var method = MethodRegistry.Find("noise")!;
            Assert.IsAssignableFrom<ICpuRenderer>(method);
            var settings = new MethodSettings<GeneratedCommon> { Method = "noise" };
            settings.Common.Seed = _seed;
            settings.ParamsFor("noise")["scale"] = _scale;
            settings.ParamsFor("noise")["levels"] = _levels;
            var p = ParamValues.ForGenerated(method, settings);
            // The lattice hash is exact (LatticeHash); only the float interpolation between nodes may put a pixel right at a
            // step boundary into the neighbouring step.
            AssertClose(method, new MosaicInputs { AreaSize = new SKSizeI(Width, Height) }, p, 0.002);
        }

        [Theory]
        [InlineData(12, 1.5)]
        [InlineData(12, 2.5)]
        [InlineData(12, 3)]
        [InlineData(7.6, 2.4)]
        public void Generated_BlockCellsAndJointsMatchTheShader_FractionalValuesToo(double _cell, double _gap)
        {
            // The cell cache plus DrawJoints stand in for the shader; a fractional gap (a hand-edited file) used to make the
            // joints a pixel narrower there.
            var method = MethodRegistry.Find("block")!;
            var settings = new MethodSettings<GeneratedCommon> { Method = "block" };
            settings.ParamsFor("block")["cellSize"] = _cell;
            settings.ParamsFor("block")["gap"] = _gap;
            settings.ParamsFor("block")["variance"] = 1.0;
            var p = ParamValues.ForGenerated(method, settings);
            AssertClose(method, new MosaicInputs { AreaSize = new SKSizeI(Width, Height) }, p, 0);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 300)]
        [InlineData(300, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 5)]
        public void Mosaic_HexcellTinyAndThinAreas_CpuPathIsTheShader(int _w, int _h)
        {
            // Where the clamp of the hexagon centre (maxX = w - 0.5, 0.5 for one pixel) and a pyramid of a single level or
            // a few take effect.
            var method = MethodRegistry.Find("hexcell")!;
            foreach (double radius in new[] { 10.0, 2.0 })
            {
                var settings = new MethodSettings<MosaicCommon> { Method = "hexcell" };
                settings.ParamsFor("hexcell")["radius"] = radius;
                var p = ParamValues.ForMosaic(method, settings, 1.0);
                using var source = SyntheticImage.CreateImage(_w, _h);
                var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
                try
                {
                    AssertClose(method, inputs, p, 0);
                }
                finally
                {
                    MosaicPipeline.Release(inputs, source);
                }
            }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 300)]
        [InlineData(300, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 5)]
        public void Generated_NoiseTinyAndThinAreas_CpuPathIsTheShader(int _w, int _h)
        {
            // Smaller than one lattice step: cols = floor(w / step) + 2 and the Math.Min(.., cols - 2) of the CPU path.
            var method = MethodRegistry.Find("noise")!;
            foreach (double? scale in new double?[] { null, 64 })
            {
                var settings = new MethodSettings<GeneratedCommon> { Method = "noise" };
                if (scale is { } value)
                {
                    settings.ParamsFor("noise")["scale"] = value;
                }
                var p = ParamValues.ForGenerated(method, settings);
                AssertClose(method, new MosaicInputs { AreaSize = new SKSizeI(_w, _h) }, p, 0);
            }
        }

        internal static void AssertClose(IMosaicMethod _method, MosaicInputs _inputs, ParamValues _p, double _maxShare)
        {
            int w = _inputs.AreaSize.Width, h = _inputs.AreaSize.Height;
            using var cpu = Render.Draw(w, h, _c => new MosaicPipeline().Draw(_c, SKRect.Create(w, h), _inputs, _method, _p, 256));
            using var shader = _method.CreateShader(_inputs, _p);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            using var reference = Render.Draw(w, h, _c => _c.DrawRect(0, 0, w, h, paint));
            int far = 0;
            var farInRow = new int[h];
            var farInColumn = new int[w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var a = cpu.GetPixel(x, y);
                    var b = reference.GetPixel(x, y);
                    int d = Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)), Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
                    if (d > 1)
                    {
                        far++;
                        farInRow[y]++;
                        farInColumn[x]++;
                    }
                }
            }
            Assert.True(far <= w * h * _maxShare, $"{_method.Id}: {far} of {w * h} pixels differ by more than 1");
            // Scattered, not one wrong row or column (an edge handled differently would hide inside the share).
            Assert.True(farInRow.Max() <= Math.Max(2, w / 20), $"{_method.Id}: {farInRow.Max()} pixels of one row differ");
            Assert.True(farInColumn.Max() <= Math.Max(2, h / 20), $"{_method.Id}: {farInColumn.Max()} pixels of one column differ");
        }
    }
}
