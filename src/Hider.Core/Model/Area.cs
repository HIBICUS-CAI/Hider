namespace Hider.Core.Model
{
    /// <summary>One area on the screen (1 area = 1 window). All geometry is in physical pixels.</summary>
    public sealed class Area
    {
        private double opacity = 1.0;

        public required string Id { get; init; }

        public PhysicalRect Rect { get; set; }

        /// <summary>
        /// 0..1. Fill / Generated: window alpha. Mosaic: the fineness interpolation value a (spec §4.5, decision 13).
        /// The range allowed for the current pattern is applied by <see cref="EffectiveOpacity"/>.
        /// </summary>
        public double Opacity
        {
            get => opacity;
            set => opacity = double.IsNaN(value) ? 1.0 : Math.Clamp(value, 0.0, 1.0);
        }

        public PatternKind Pattern { get; set; } = PatternKind.Mosaic;
        public FillSettings Fill { get; init; } = new();
        public MethodSettings<MosaicCommon> Mosaic { get; init; } = new() { Method = AreaDefaults.MosaicMethod };
        public MethodSettings<GeneratedCommon> Generated { get; init; } = new() { Method = AreaDefaults.GeneratedMethod };

        /// <summary><see cref="Opacity"/> clamped to the range of the current pattern.</summary>
        public double EffectiveOpacity => ClampOpacity(Pattern, Opacity);

        public const double MinAlphaOpacity = 0.10;

        /// <summary>A new area with the defaults of FR-14: mosaic / pixelate, refresh interval for the given OS.</summary>
        public static Area CreateNew(string _id, PhysicalRect _rect, bool _isWindows)
        {
            var area = new Area { Id = _id, Rect = _rect };
            area.Mosaic.Common.IntervalMs = AreaDefaults.IntervalMs(_isWindows);
            return area;
        }

        /// <summary>Fill / Generated: 0.10..1.00 (always visible). Mosaic: 0.00..1.00 (spec §4.5).</summary>
        public static double ClampOpacity(PatternKind _pattern, double _value)
        {
            double min = _pattern == PatternKind.Mosaic ? 0.0 : MinAlphaOpacity;
            return double.IsNaN(_value) ? 1.0 : Math.Clamp(_value, min, 1.0);
        }
    }
}
