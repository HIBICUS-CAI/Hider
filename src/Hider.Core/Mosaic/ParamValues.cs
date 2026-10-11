using System.Globalization;
using System.Text;
using Hider.Core.Model;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// A method's parameters and its kind's common values resolved for drawing. Keys without a stored value take the
    /// spec's default (FR-35), stored values are clamped to the spec range, and unknown keys never throw.
    /// </summary>
    public sealed class ParamValues
    {
        private readonly Dictionary<string, ParamSpec> specs = new();
        private readonly Dictionary<string, double> values = new();
        private string? fingerprint;

        public ParamValues(IEnumerable<ParamSpec> _specs, IReadOnlyDictionary<string, double>? _values, double _opacity)
        {
            foreach (var spec in _specs)
            {
                specs[spec.Key] = spec;
            }
            if (_values is not null)
            {
                foreach (var (key, value) in _values)
                {
                    values[key] = value;
                }
            }
            Opacity = Math.Clamp(_opacity, 0.0, 1.0);
        }

        /// <summary>The fineness interpolation value a (mosaic) or 1 (generated).</summary>
        public double Opacity { get; }

        /// <summary>Stored value clamped to its spec; the spec default when not stored; 0 for a key no spec knows and nothing stores.</summary>
        public double Raw(string _key)
        {
            bool stored = values.TryGetValue(_key, out double value);
            if (specs.TryGetValue(_key, out var spec))
            {
                return stored ? spec.Clamp(value) : spec.Default;
            }
            return stored ? value : 0.0;
        }

        /// <summary><see cref="Raw"/> with the opacity interpolation of <see cref="ParamSpec.LerpWithOpacity"/> applied.</summary>
        public double Effective(string _key)
        {
            return specs.TryGetValue(_key, out var spec) ? spec.Effective(Raw(_key), Opacity) : Raw(_key);
        }

        /// <summary>Every resolved value as "key=value" in key order, for cache keys. Built once (the values never change).</summary>
        public string Fingerprint()
        {
            if (fingerprint is not null)
            {
                return fingerprint;
            }
            var keys = new SortedSet<string>(specs.Keys, StringComparer.Ordinal);
            keys.UnionWith(values.Keys);
            var text = new StringBuilder();
            foreach (string key in keys)
            {
                text.Append(key).Append('=').Append(Raw(key).ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
            text.Append("a=").Append(Opacity.ToString("R", CultureInfo.InvariantCulture));
            return fingerprint = text.ToString();
        }

        /// <summary>Values for drawing a mosaic method: its params, the mosaic common values and the area's opacity.</summary>
        public static ParamValues ForMosaic(IMosaicMethod _method, MethodSettings<MosaicCommon> _settings, double _opacity)
        {
            var values = StoredParams(_settings.Params, _method.Id);
            var common = _settings.Common;
            values[CommonParams.Blur] = common.Blur;
            values[CommonParams.Levels] = common.Levels;
            values[CommonParams.IntervalMs] = common.IntervalMs;
            values[CommonParams.StripPx] = common.StripPx;
            return new ParamValues(_method.Params.Concat(CommonParams.Mosaic), values, _opacity);
        }

        /// <summary>Values for drawing a generated method: its params plus seed and base color (opacity is window alpha, not used).</summary>
        public static ParamValues ForGenerated(IMosaicMethod _method, MethodSettings<GeneratedCommon> _settings)
        {
            var values = StoredParams(_settings.Params, _method.Id);
            var (r, g, b) = ColorText.Parse(_settings.Common.BaseColor);
            values[CommonParams.Seed] = _settings.Common.Seed;
            values[CommonParams.BaseColorR] = r / 255.0;
            values[CommonParams.BaseColorG] = g / 255.0;
            values[CommonParams.BaseColorB] = b / 255.0;
            return new ParamValues(_method.Params, values, 1.0);
        }

        private static Dictionary<string, double> StoredParams(Dictionary<string, Dictionary<string, double>> _params, string _methodId)
        {
            return _params.TryGetValue(_methodId, out var stored) ? new Dictionary<string, double>(stored) : new Dictionary<string, double>();
        }
    }
}
