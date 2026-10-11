namespace Hider.Core.Mosaic
{
    /// <summary>
    /// One numeric setting of a method or of a pattern kind. The settings popup builds a "slider + number box" row from it
    /// (FR-37): the box accepts <see cref="Min"/>..<see cref="Max"/>, the slider covers the usual range
    /// <see cref="SliderMin"/>..<see cref="SliderMax"/>. A unit of "%" means the stored value is a fraction (0.2 = 20%).
    /// </summary>
    public sealed record ParamSpec(
        string Key, string LabelKey, string Unit,
        double Min, double Max,
        double SliderMin, double SliderMax,
        double Default, bool LogScale = false,
        bool LerpWithOpacity = false, double LerpFloor = 1.0)
    {
        /// <summary>Share of the slider used for Min..SliderMin when a log slider must also reach Min (e.g. interval 0).</summary>
        public const double LogZeroZone = 0.02;

        /// <summary>Factor from the stored value to the number shown (100 for "%").</summary>
        public double DisplayFactor => Unit == "%" ? 100.0 : 1.0;

        public double Clamp(double _value)
        {
            return double.IsNaN(_value) ? Default : Math.Clamp(_value, Min, Max);
        }

        /// <summary>
        /// Effective value used for drawing: lerp(a, LerpFloor, value) when <see cref="LerpWithOpacity"/> (spec §4.5), where a
        /// is the area's opacity; the value itself otherwise.
        /// </summary>
        public double Effective(double _value, double _opacity)
        {
            double value = Clamp(_value);
            if (!LerpWithOpacity)
            {
                return value;
            }
            double a = Math.Clamp(_opacity, 0.0, 1.0);
            return LerpFloor + (value - LerpFloor) * a;
        }

        /// <summary>Slider position 0..1 for a value. Values outside the slider range stick to an end (GT-28).</summary>
        public double ToSliderPosition(double _value)
        {
            double v = Clamp(_value);
            if (!LogScale)
            {
                return Math.Clamp((v - SliderMin) / (SliderMax - SliderMin), 0.0, 1.0);
            }
            double zone = HasLogZeroZone ? LogZeroZone : 0.0;
            if (v >= SliderMax)
            {
                return 1.0;
            }
            if (v < SliderMin)
            {
                return zone == 0.0 || v <= Min ? 0.0 : zone * (v - Min) / (SliderMin - Min);
            }
            return zone + (1.0 - zone) * Math.Log(v / SliderMin) / Math.Log(SliderMax / SliderMin);
        }

        /// <summary>Value for a slider position 0..1 (inverse of <see cref="ToSliderPosition"/>).</summary>
        public double FromSliderPosition(double _position)
        {
            double p = Math.Clamp(_position, 0.0, 1.0);
            if (!LogScale)
            {
                return SliderMin + (SliderMax - SliderMin) * p;
            }
            double zone = HasLogZeroZone ? LogZeroZone : 0.0;
            if (p < zone)
            {
                return Min + (SliderMin - Min) * p / zone;
            }
            return SliderMin * Math.Pow(SliderMax / SliderMin, (p - zone) / (1.0 - zone));
        }

        private bool HasLogZeroZone => Min < SliderMin;
    }
}
