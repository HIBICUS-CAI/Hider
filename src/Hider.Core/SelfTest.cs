using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Hider.Core
{
    /// <summary>
    /// Checks run by "Hider --selftest" without starting Avalonia. Used by CI on the trimmed binary to catch
    /// trimming and native library problems (JSON source generation, SkiaSharp natives, SkSL compilation).
    /// </summary>
    public static class SelfTest
    {
        /// <returns>0 when every check passed, 1 otherwise.</returns>
        public static int Run(TextWriter _output)
        {
            var checks = new (string Name, Func<string?> Check)[]
            {
                ("json round trip", CheckJson),
                ("sksl compile", CheckSksl),
            };

            int failures = 0;
            foreach (var (name, check) in checks)
            {
                string? error;
                try
                {
                    error = check();
                }
                catch (Exception e)
                {
                    error = e.ToString();
                }

                _output.WriteLine(error is null ? $"[ok]   {name}" : $"[fail] {name}: {error}");
                if (error is not null)
                {
                    failures++;
                }
            }

            _output.WriteLine(failures == 0 ? "selftest passed" : $"selftest failed ({failures})");
            return failures == 0 ? 0 : 1;
        }

        private static string? CheckJson()
        {
            var probe = new SelfTestProbe(1, "ok");
            string json = JsonSerializer.Serialize(probe, SelfTestJsonContext.Default.SelfTestProbe);
            var back = JsonSerializer.Deserialize(json, SelfTestJsonContext.Default.SelfTestProbe);
            return back == probe ? null : $"round trip mismatch: {json}";
        }

        private static string? CheckSksl()
        {
            var sources = new List<(string Name, string Source)> { ("identity", IdentitySksl) };
            foreach (var (name, source) in sources)
            {
                using var effect = SKRuntimeEffect.CreateShader(source, out string? errors);
                if (effect is null)
                {
                    return $"{name}: {errors}";
                }
            }
            return null;
        }

        private const string IdentitySksl = "half4 main(float2 p) { return half4(1, 1, 1, 1); }";
    }

    internal sealed record SelfTestProbe(int Version, string Text);

    [JsonSerializable(typeof(SelfTestProbe))]
    internal sealed partial class SelfTestJsonContext : JsonSerializerContext
    {
    }
}
