using Hider.Core.Model;

namespace Hider.Core.Persistence
{
    /// <summary>Contents of areas.json (version 1, work instructions §3.4).</summary>
    public sealed class AppState
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public List<AreaState> Areas { get; set; } = new();
    }

    /// <summary>
    /// One area as stored: x, y in physical pixels (Avalonia Position) and width, height in DIP (Avalonia Width / Height),
    /// as the spec §4.7 defines. Edit mode is never stored (FR-63). Conversion to and from the physical-pixel model needs
    /// the window scale, which the app takes from IAreaWindowHost.
    /// </summary>
    public sealed class AreaState
    {
        public string Id { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public double Width { get; set; } = AreaDefaults.NewAreaWidthDip;
        public double Height { get; set; } = AreaDefaults.NewAreaHeightDip;
        public double Opacity { get; set; } = 1.0;
        public PatternKind Pattern { get; set; } = PatternKind.Mosaic;
        public FillSettings Fill { get; set; } = new();
        public MethodSettings<MosaicCommon> Mosaic { get; set; } = new() { Method = AreaDefaults.MosaicMethod };
        public MethodSettings<GeneratedCommon> Generated { get; set; } = new() { Method = AreaDefaults.GeneratedMethod };

        /// <param name="_area">The area to save (its rectangle in physical px).</param>
        /// <param name="_scale">Physical pixels per DIP of the window the area is on.</param>
        public static AreaState FromArea(Area _area, double _scale)
        {
            return new AreaState
            {
                Id = _area.Id,
                X = _area.Rect.X,
                Y = _area.Rect.Y,
                Width = Math.Round(_area.Rect.Width / _scale, 2),
                Height = Math.Round(_area.Rect.Height / _scale, 2),
                Opacity = _area.Opacity,
                Pattern = _area.Pattern,
                Fill = _area.Fill,
                Mosaic = _area.Mosaic,
                Generated = _area.Generated,
            };
        }

        /// <param name="_scale">Physical pixels per DIP of the screen the stored position is on.</param>
        public Area ToArea(double _scale)
        {
            return new Area
            {
                Id = Id,
                Rect = new PhysicalRect(X, Y, (int)Math.Round(Width * _scale), (int)Math.Round(Height * _scale)),
                Opacity = Opacity,
                Pattern = Pattern,
                Fill = Fill,
                Mosaic = Mosaic,
                Generated = Generated,
            };
        }
    }
}
