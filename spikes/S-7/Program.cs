using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace Spike
{
    /// <summary>
    /// S-7 (design §10): run on the trimmed single-file publish of each RID.
    /// Checks: JSON source generation save/restore through a file, SkSL compilation (SkiaSharp natives), a D-Bus call
    /// with Tmds.DBus.Protocol (Linux, needs a session bus), and an Avalonia window with FluentTheme + ColorPicker
    /// rendered by Skia on the headless platform. Exit code 0 when all pass.
    /// </summary>
    internal static class Program
    {
        private static int failures;

        public static int Main(string[] _args)
        {
            Check("json save / restore", CheckJson);
            Check("sksl compile", CheckSksl);
            if (OperatingSystem.IsLinux())
            {
                Check("dbus GetId (session bus)", CheckDbus);
            }
            Check("avalonia + ColorPicker render", CheckColorPicker);
            Console.WriteLine(failures == 0 ? "S-7 passed" : $"S-7 failed ({failures})");
            return failures == 0 ? 0 : 1;
        }

        private static void Check(string _name, Func<string> _check)
        {
            try
            {
                Console.WriteLine($"[ok]   {_name}: {_check()}");
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine($"[fail] {_name}: {e}");
            }
        }

        private static string CheckJson()
        {
            var state = new State
            {
                Version = 1,
                Areas = { new AreaRecord { Id = "0f3b8d2e", X = -1200, Y = 340, Width = 800.5, Pattern = Kind.Generated, Params = { ["pixelate"] = new() { ["block"] = 12 } } } },
            };
            string path = Path.Combine(Path.GetTempPath(), $"hider-s7-{Environment.ProcessId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(state, StateJson.Default.State));
            var back = JsonSerializer.Deserialize(File.ReadAllText(path), StateJson.Default.State)!;
            File.Delete(path);
            var a = back.Areas[0];
            if (a.Id != "0f3b8d2e" || a.X != -1200 || a.Width != 800.5 || a.Pattern != Kind.Generated || a.Params["pixelate"]["block"] != 12)
            {
                throw new InvalidOperationException("round trip mismatch");
            }
            return JsonSerializer.Serialize(back, StateJson.Default.State);
        }

        private static string CheckSksl()
        {
            using var effect = SKRuntimeEffect.CreateShader("uniform float block; half4 main(float2 p) { return half4(fract(p / block), 0, 1); }", out string? errors);
            return effect is null ? throw new InvalidOperationException(errors) : $"uniforms: {string.Join(",", effect.Uniforms)}";
        }

        private static string CheckDbus()
        {
            if (Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is null)
            {
                throw new InvalidOperationException("no session bus (run inside dbus-run-session)");
            }
            using var connection = new DBusConnection(DBusAddress.Session!);
            connection.ConnectAsync().AsTask().GetAwaiter().GetResult();
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetId");
                message = writer.CreateMessage();
            }
            string id = connection.CallMethodAsync(message, (Message m, object? _) => m.GetBodyReader().ReadString()).GetAwaiter().GetResult();
            return $"bus id {id}, unique name {connection.UniqueName}";
        }

        private static string CheckColorPicker()
        {
            AppBuilder.Configure<SpikeApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            var picker = new ColorPicker { Color = Color.Parse("#68768A") };
            var window = new Window { Width = 360, Height = 120, Content = new StackPanel { Children = { picker, new ColorView { Color = Colors.Orange } } } };
            window.Show();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
            var colors = new HashSet<int>();
            int width, height;
            using (var pixels = frame.Lock())
            {
                width = pixels.Size.Width;
                height = pixels.Size.Height;
                for (int y = 0; y < height; y += 2)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        colors.Add(System.Runtime.InteropServices.Marshal.ReadInt32(pixels.Address, y * pixels.RowBytes + x * 4));
                    }
                }
            }
            window.Close();
            if (colors.Count < 8)
            {
                throw new InvalidOperationException($"frame looks empty ({colors.Count} colors)");
            }
            return $"{width}x{height}, {colors.Count} distinct colors";
        }
    }

    public partial class SpikeApp : Application
    {
        public override void Initialize()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<Kind>))]
    internal enum Kind
    {
        [JsonStringEnumMemberName("fill")] Fill,
        [JsonStringEnumMemberName("pattern")] Generated,
    }

    internal sealed class State
    {
        public int Version { get; set; }
        public List<AreaRecord> Areas { get; init; } = new();
    }

    internal sealed class AreaRecord
    {
        public string Id { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public double Width { get; set; }
        public Kind Pattern { get; set; }
        public Dictionary<string, Dictionary<string, double>> Params { get; init; } = new();
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(State))]
    internal sealed partial class StateJson : JsonSerializerContext
    {
    }
}
