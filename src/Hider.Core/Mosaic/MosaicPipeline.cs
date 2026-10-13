using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// The fixed part around a method (design §4.2): pre-blur → pyramid → method shader → color levels.
    /// <see cref="Prepare"/> does the first two (once per captured frame and blur value), <see cref="Draw"/> the rest.
    /// Everything runs on the canvas' context: GPU when Avalonia renders on the GPU, Skia's CPU raster otherwise.
    /// </summary>
    public sealed class MosaicPipeline
    {
        /// <summary>Gaussian sigma per px of blur radius (radius r ≈ 2 sigma).</summary>
        public const float SigmaPerRadius = 0.5f;

        public const int MaxPyramidLevels = 9;

        private readonly Dictionary<int, SKColorFilter> levelFilters = new();

        /// <summary>
        /// Builds the method inputs from a captured image: blurred by the common "blur" value (effective, so it fades with
        /// the opacity a), combined with the method's own radius for <see cref="IPreBlurMethod"/>, and its pyramid. The caller
        /// disposes the result with <see cref="Release"/>.
        /// </summary>
        public static MosaicInputs Prepare(SKImage _source, SKPointI _areaOrigin, ParamValues _p, GRRecordingContext? _context = null, IMosaicMethod? _method = null)
        {
            double common = _p.Effective(CommonParams.Blur);
            double own = _method is IPreBlurMethod preBlur ? Math.Max(0, preBlur.PreBlurRadius(_p)) : 0;
            double radius = Math.Sqrt(common * common + own * own);
            var source = radius > 0.01 ? Blur(_source, (float)radius * SigmaPerRadius, _context) : _source;
            return new MosaicInputs
            {
                Source = source,
                // Built on first use: methods that never sample it (blur, pass-through values) skip ~30 ms per 1080p frame on the CPU.
                Pyramid = new LazyPyramid(source, _context),
                AreaSize = new SKSizeI(_source.Width, _source.Height),
                AreaOrigin = _areaOrigin,
            };
        }

        /// <summary>Disposes the images <see cref="Prepare"/> created (never the original capture).</summary>
        public static void Release(MosaicInputs _inputs, SKImage _original)
        {
            var images = new List<SKImage>();
            if (_inputs.Source is not null)
            {
                images.Add(_inputs.Source);
            }
            if (_inputs.Pyramid is not LazyPyramid { IsBuilt: false })
            {
                images.AddRange(_inputs.Pyramid);
            }
            foreach (var image in images.Distinct())
            {
                if (!ReferenceEquals(image, _original))
                {
                    image.Dispose();
                }
            }
        }

        /// <summary>
        /// Draws the method into <paramref name="_dst"/> (canvas coordinates; the area's physical pixels are scaled to it), then
        /// limits each channel to <paramref name="_levels"/> values (256 = no change).
        /// </summary>
        public void Draw(SKCanvas _canvas, SKRect _dst, MosaicInputs _inputs, IMosaicMethod _method, ParamValues _p, int _levels)
        {
            int w = Math.Max(1, _inputs.AreaSize.Width), h = Math.Max(1, _inputs.AreaSize.Height);
            if (_canvas.Context is null && _method is ICpuRenderer cpu && DrawOnCpu(_canvas, _dst, _inputs, cpu, _p, _levels, w, h))
            {
                return;
            }
            using var shader = _method.CreateShader(_inputs, _p);
            using var paint = new SKPaint { Shader = shader, ColorFilter = LevelFilter(_levels), BlendMode = SKBlendMode.Src };
            var cellMethod = _method as ICellMethod;
            int cell = cellMethod is not null ? Math.Max(1, cellMethod.CellSize(_p)) : 1;

            _canvas.Save();
            _canvas.ClipRect(_dst);
            _canvas.Translate(_dst.Left, _dst.Top);
            _canvas.Scale(_dst.Width / w, _dst.Height / h);
            if (cell < 2)
            {
                _canvas.DrawRect(0, 0, w, h, paint);
            }
            else
            {
                // One shader evaluation per cell (proposal P-1): reduced pixel j is evaluated at the centre of the cell's top-left
                // pixel (j·cell + 0.5), so the cell values are exact; nearest upscaling restores the cells aligned to the area
                // origin. Not the cell centre: block's joints lie on the right / bottom of a cell, and with gap ≥ cell / 2 the
                // centre itself is a joint, which turned every cell into the base color. DrawJoints paints the joints.
                int cw = (w + cell - 1) / cell, ch = (h + cell - 1) / cell;
                var info = new SKImageInfo(cw, ch, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = CreateSurface(_canvas.Context, info);
                surface.Canvas.Scale(1f / cell, 1f / cell);
                surface.Canvas.Translate((cell - 1) / 2f, (cell - 1) / 2f);
                surface.Canvas.DrawRect(-cell, -cell, (cw + 2) * cell, (ch + 2) * cell, paint);
                using var cells = surface.Snapshot();
                using var copy = new SKPaint { BlendMode = SKBlendMode.Src };
                _canvas.DrawImage(cells, SKRect.Create(0, 0, cw * cell, ch * cell), new SKSamplingOptions(SKFilterMode.Nearest), copy);
                cellMethod!.DrawJoints(_canvas, new SKSizeI(w, h), cell, _p);
            }
            _canvas.Restore();
        }

        private bool DrawOnCpu(SKCanvas _canvas, SKRect _dst, MosaicInputs _inputs, ICpuRenderer _cpu, ParamValues _p, int _levels, int _w, int _h)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var pixmap = bitmap.PeekPixels())
            {
                if (!_cpu.TryRender(pixmap, _inputs, _p))
                {
                    return false;
                }
            }
            bitmap.NotifyPixelsChanged();
            using var image = SKImage.FromBitmap(bitmap);
            using var paint = new SKPaint { ColorFilter = LevelFilter(_levels), BlendMode = SKBlendMode.Src };
            _canvas.Save();
            _canvas.ClipRect(_dst);
            _canvas.DrawImage(image, _dst, new SKSamplingOptions(SKFilterMode.Nearest), paint);
            _canvas.Restore();
            return true;
        }

        /// <summary>
        /// [0] = source, [k] = level k-1 halved with a 2x2 box filter (sizes round up), until 1x1 or <paramref name="_maxLevels"/>
        /// images. The caller owns every image except [0].
        /// </summary>
        public static IReadOnlyList<SKImage> BuildPyramid(SKImage _source, int _maxLevels)
        {
            return BuildPyramid(_source, _maxLevels, null);
        }

        public static IReadOnlyList<SKImage> BuildPyramid(SKImage _source, int _maxLevels, GRRecordingContext? _context)
        {
            var levels = new List<SKImage> { _source };
            var current = _source;
            while (levels.Count < _maxLevels && (current.Width > 1 || current.Height > 1))
            {
                int w = (current.Width + 1) / 2, h = (current.Height + 1) / 2;
                using var surface = CreateSurface(_context, new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
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

        /// <summary>Per-channel quantisation to <paramref name="_levels"/> values: floor(c·L) / (L−1) (256 = identity).</summary>
        public SKColorFilter? LevelFilter(int _levels)
        {
            int levels = Math.Clamp(_levels, 2, 256);
            if (levels >= 256)
            {
                return null;
            }
            if (!levelFilters.TryGetValue(levels, out var filter))
            {
                var identity = new byte[256];
                var table = new byte[256];
                for (int i = 0; i < 256; i++)
                {
                    identity[i] = (byte)i;
                    int q = Math.Min(levels - 1, i * levels / 256);
                    table[i] = (byte)Math.Round(q * 255.0 / (levels - 1));
                }
                filter = SKColorFilter.CreateTable(identity, table, table, table);
                levelFilters[levels] = filter;
            }
            return filter;
        }

        private static SKImage Blur(SKImage _source, float _sigma, GRRecordingContext? _context)
        {
            using var surface = CreateSurface(_context, new SKImageInfo(_source.Width, _source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var filter = SKImageFilter.CreateBlur(_sigma, _sigma, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Src };
            surface.Canvas.DrawImage(_source, 0, 0, paint);
            return surface.Snapshot();
        }

        internal static SKSurface CreateSurface(GRRecordingContext? _context, SKImageInfo _info)
        {
            return (_context is not null ? SKSurface.Create(_context, false, _info) : null) ?? SKSurface.Create(_info)
                ?? throw new InvalidOperationException($"Cannot create a {_info.Width}x{_info.Height} surface");
        }

        /// <summary>The pyramid of <see cref="BuildPyramid(SKImage, int, GRRecordingContext?)"/>, built when first read.</summary>
        private sealed class LazyPyramid : IReadOnlyList<SKImage>
        {
            private readonly SKImage source;
            private readonly GRRecordingContext? context;
            private IReadOnlyList<SKImage>? levels;

            public LazyPyramid(SKImage _source, GRRecordingContext? _context)
            {
                source = _source;
                context = _context;
            }

            public bool IsBuilt => levels is not null;

            private IReadOnlyList<SKImage> Levels => levels ??= BuildPyramid(source, MaxPyramidLevels, context);

            public SKImage this[int _index] => Levels[_index];

            public int Count => Levels.Count;

            public IEnumerator<SKImage> GetEnumerator()
            {
                return Levels.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
