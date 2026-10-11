using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// The fixed part around a method (design §4.2): pre-blur → pyramid → method shader → color levels. Step 2 has the
    /// pyramid and the method stage; the blur and levels stages and the CPU optimisations come in Step 4.
    /// </summary>
    public sealed class MosaicPipeline
    {
        public void Draw(SKCanvas _canvas, SKRect _dst, MosaicInputs _inputs, IMosaicMethod _method, ParamValues _p, int _levels)
        {
            using var shader = _method.CreateShader(_inputs, _p);
            using var paint = new SKPaint { Shader = shader };
            _canvas.Save();
            _canvas.Translate(_dst.Left, _dst.Top);
            _canvas.Scale(_dst.Width / Math.Max(1, _inputs.AreaSize.Width), _dst.Height / Math.Max(1, _inputs.AreaSize.Height));
            _canvas.DrawRect(0, 0, _inputs.AreaSize.Width, _inputs.AreaSize.Height, paint);
            _canvas.Restore();
        }

        /// <summary>
        /// [0] = source, [k] = level k-1 halved with a 2x2 box filter (sizes round up), until 1x1 or <paramref name="_maxLevels"/>
        /// images. The caller owns every image except [0].
        /// </summary>
        public static IReadOnlyList<SKImage> BuildPyramid(SKImage _source, int _maxLevels)
        {
            var levels = new List<SKImage> { _source };
            var current = _source;
            while (levels.Count < _maxLevels && (current.Width > 1 || current.Height > 1))
            {
                int w = (current.Width + 1) / 2, h = (current.Height + 1) / 2;
                var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                // Each destination pixel centre maps halfway between four source texels, so linear sampling averages them
                // exactly. Clamping repeats the last row / column of odd sizes.
                using var shader = current.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                    new SKSamplingOptions(SKFilterMode.Linear), SKMatrix.CreateScale(0.5f, 0.5f));
                using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
                surface.Canvas.DrawRect(0, 0, w, h, paint);
                current = surface.Snapshot();
                levels.Add(current);
            }
            return levels;
        }
    }
}
