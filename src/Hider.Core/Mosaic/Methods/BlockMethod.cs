using SkiaSharp;

namespace Hider.Core.Mosaic.Methods
{
    /// <summary>
    /// Generated blocks (spec §4.5, FR-40): cells of "cellSize" px aligned to the area origin, colored around the base color
    /// by hash(cellX, cellY, seed), with optional joints of "gap" px in the base color. Cells never depend on the area size,
    /// so resizing only adds or removes cells at the edges.
    /// </summary>
    public sealed class BlockMethod : SkslMethod, ICellMethod
    {
        public override string Id => "block";
        public override string LabelKey => "method.block";
        public override MethodKind Kind => MethodKind.Generated;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("cellSize", "param.cellSize", ParamUnit.Px, 2, 256, 2, 64, 12),
            new ParamSpec("variance", "param.variance", ParamUnit.Percent, 0, 1, 0, 1, 0.2),
            new ParamSpec("gap", "param.gap", ParamUnit.Px, 0, 64, 0, 8, 0),
        };

        protected override double UniformValue(string _key, ParamValues _p)
        {
            return _key == "cellSize" ? Math.Round(_p.Raw(_key)) : base.UniformValue(_key, _p);
        }

        /// <summary>Cell colors are constant per cell; joints are filled afterwards by <see cref="DrawJoints"/>.</summary>
        public int CellSize(ParamValues _p)
        {
            return (int)Math.Round(_p.Raw("cellSize"));
        }

        /// <summary>Joints of "gap" px on the right and bottom of each cell in the base color (same as the shader's rule).</summary>
        public void DrawJoints(SKCanvas _canvas, SKSizeI _size, int _cell, ParamValues _p)
        {
            float gap = (float)Math.Min(_p.Raw("gap"), _cell);
            if (gap <= 0)
            {
                return;
            }
            var color = new SKColor((byte)Math.Round(_p.Raw(CommonParams.BaseColorR) * 255), (byte)Math.Round(_p.Raw(CommonParams.BaseColorG) * 255),
                (byte)Math.Round(_p.Raw(CommonParams.BaseColorB) * 255));
            using var paint = new SKPaint { Color = color, BlendMode = SKBlendMode.Src, IsAntialias = false };
            for (int x = _cell; x - gap < _size.Width; x += _cell)
            {
                _canvas.DrawRect(x - gap, 0, gap, _size.Height, paint);
            }
            for (int y = _cell; y - gap < _size.Height; y += _cell)
            {
                _canvas.DrawRect(0, y - gap, _size.Width, gap, paint);
            }
        }
    }
}
