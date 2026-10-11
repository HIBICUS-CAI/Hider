namespace Hider.Core.Model
{
    public sealed class FillSettings
    {
        public string Color { get; set; } = AreaDefaults.FillColor;
    }

    /// <summary>
    /// Settings of one pattern kind (decision D-4): the selected method, values shared by every method of the kind, and
    /// per-method values keyed by method id then parameter key. Values of methods that are not selected are kept (FR-35),
    /// and unknown method ids are kept as they are so an older build does not drop them.
    /// </summary>
    public sealed class MethodSettings<TCommon> where TCommon : new()
    {
        public string Method { get; set; } = "";
        // Settable rather than init: the JSON source generator assigns null to init-only properties missing from the file.
        public TCommon Common { get; set; } = new();
        public Dictionary<string, Dictionary<string, double>> Params { get; set; } = new();

        public Dictionary<string, double> ParamsFor(string _methodId)
        {
            if (!Params.TryGetValue(_methodId, out var values))
            {
                values = new Dictionary<string, double>();
                Params[_methodId] = values;
            }
            return values;
        }
    }

    /// <summary>Values shared by every mosaic method (spec §4.5).</summary>
    public sealed class MosaicCommon
    {
        public double Blur { get; set; } = AreaDefaults.MosaicBlur;
        public int Levels { get; set; } = AreaDefaults.MosaicLevels;
        public int IntervalMs { get; set; } = AreaDefaults.IntervalMsForCurrentOs;
        public int StripPx { get; set; } = AreaDefaults.StripPx;
    }

    /// <summary>Values shared by every generated method (spec §4.5).</summary>
    public sealed class GeneratedCommon
    {
        public string BaseColor { get; set; } = AreaDefaults.FillColor;
        public int Seed { get; set; } = AreaDefaults.Seed;
    }
}
