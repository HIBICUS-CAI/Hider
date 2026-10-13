using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>Loads and compiles the embedded Mosaic/Methods/&lt;id&gt;.sksl shaders.</summary>
    public static class ShaderSource
    {
        private const string Prefix = "Hider.Core.Mosaic.Methods.";
        private const string Suffix = ".sksl";

        public static string Load(string _id)
        {
            using var stream = typeof(ShaderSource).Assembly.GetManifestResourceStream(Prefix + _id + Suffix)
                ?? throw new InvalidOperationException($"Missing embedded shader {_id}{Suffix}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>Ids of every embedded shader.</summary>
        public static IReadOnlyList<string> All()
        {
            return typeof(ShaderSource).Assembly.GetManifestResourceNames()
                .Where(_n => _n.StartsWith(Prefix, StringComparison.Ordinal) && _n.EndsWith(Suffix, StringComparison.Ordinal))
                .Select(_n => _n.Substring(Prefix.Length, _n.Length - Prefix.Length - Suffix.Length))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>Compiles a shader; throws with Skia's error text when it does not compile.</summary>
        public static SKRuntimeEffect Compile(string _id)
        {
            return SKRuntimeEffect.CreateShader(Load(_id), out string? errors)
                ?? throw new InvalidOperationException($"{_id}{Suffix} does not compile: {errors}");
        }
    }
}
