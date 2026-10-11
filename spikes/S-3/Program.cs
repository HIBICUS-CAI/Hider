using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace Spike
{
    /// <summary>
    /// S-3 (design §10): pixelate a 1920x1080 image with an SkSL runtime shader.
    ///   (no args)  CPU raster: average draw time over 30 frames for the full-resolution shader, the same shader evaluated
    ///              once per cell (reduced resolution, then nearest upscale), a color table filter and a blur.
    ///              Pass: the per-cell variant &lt; 30 ms.
    ///   --window   Same shader inside an Avalonia window through ISkiaSharpApiLeaseFeature. Reports gpu=True/False,
    ///              the draw time on that context and the max channel difference against the CPU result. Pass: GPU &lt; 2 ms,
    ///              difference &lt;= 1/255.
    /// </summary>
    internal static class Program
    {
        public const int W = 1920, H = 1080, Block = 12, Frames = 30;

        public const string PixelateSksl = @"
uniform float block;
uniform float2 areaSize;
uniform shader src;
half4 main(float2 p) {
    float2 cell0 = floor(p / block) * block;
    float2 cell1 = min(cell0 + block, areaSize);
    return src.eval((cell0 + cell1) * 0.5);
}";

        [STAThread]
        public static int Main(string[] _args)
        {
            using var source = Synthetic.Create(W, H);
            using var cpu = RenderCpu(source, out double cpuMs);
            Console.WriteLine($"cpu full-res shader : {W}x{H} block {Block}: {cpuMs:F2} ms/frame");
            using var cpuCells = RenderCpuPerCell(source, out double cellMs);
            Console.WriteLine($"cpu per-cell shader : {cellMs:F2} ms/frame (pass < 30 ms: {cellMs < 30})");
            using (var a = SKBitmap.FromImage(cpu))
            {
                Console.WriteLine($"per-cell vs full-res: max diff {MaxDiff(a, cpuCells)}/255");
            }
            Console.WriteLine($"cpu color table     : {TimeColorTable(source):F2} ms/frame");
            Console.WriteLine($"cpu blur r=8        : {TimeBlur(source):F2} ms/frame");
            Console.WriteLine($"cpu: {Environment.ProcessorCount} logical cores, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            if (!_args.Contains("--window"))
            {
                return cellMs < 30 ? 0 : 1;
            }

            Reference = cpu;
            Source = source;
            AppBuilder.Configure<Application>().UsePlatformDetect().StartWithClassicDesktopLifetime(_args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
            return 0;
        }

        public static SKImage? Reference;
        public static SKImage? Source;

        public static SKShader CreateShader(SKImage _source)
        {
            using var effect = SKRuntimeEffect.CreateShader(PixelateSksl, out string? errors) ?? throw new InvalidOperationException(errors);
            var uniforms = new SKRuntimeEffectUniforms(effect) { ["block"] = (float)Block, ["areaSize"] = new[] { (float)_source.Width, _source.Height } };
            // Sample a 1/8 pyramid level with linear filtering: one sample approximates the cell average.
            var children = new SKRuntimeEffectChildren(effect) { ["src"] = _source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear)) };
            return effect.ToShader(uniforms, children);
        }

        private static SKImage RenderCpu(SKImage _source, out double _ms)
        {
            var info = new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            using var shader = CreateShader(_source);
            using var paint = new SKPaint { Shader = shader };
            surface.Canvas.DrawRect(0, 0, W, H, paint); // warm up
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Frames; i++)
            {
                surface.Canvas.DrawRect(0, 0, W, H, paint);
                surface.Flush();
            }
            _ms = sw.Elapsed.TotalMilliseconds / Frames;
            return surface.Snapshot();
        }

        public const string PixelatePerCellSksl = @"
uniform float block;
uniform float2 areaSize;
uniform shader src;
half4 main(float2 p) {
    float2 cell0 = floor(p) * block;
    float2 cell1 = min(cell0 + block, areaSize);
    return src.eval((cell0 + cell1) * 0.5);
}";

        /// <summary>Evaluates the shader once per cell on a (W/block x H/block) surface and scales it up with nearest sampling.</summary>
        private static SKImage RenderCpuPerCell(SKImage _source, out double _ms)
        {
            int cw = (W + Block - 1) / Block, ch = (H + Block - 1) / Block;
            using var effect = SKRuntimeEffect.CreateShader(PixelatePerCellSksl, out string? errors) ?? throw new InvalidOperationException(errors);
            var uniforms = new SKRuntimeEffectUniforms(effect) { ["block"] = (float)Block, ["areaSize"] = new[] { (float)W, H } };
            var children = new SKRuntimeEffectChildren(effect) { ["src"] = _source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear)) };
            using var shader = effect.ToShader(uniforms, children);
            using var paint = new SKPaint { Shader = shader };
            using var cells = SKSurface.Create(new SKImageInfo(cw, ch, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Frames; i++)
            {
                cells.Canvas.DrawRect(0, 0, cw, ch, paint);
                using var small = cells.Snapshot();
                surface.Canvas.DrawImage(small, SKRect.Create(0, 0, cw * Block, ch * Block), new SKSamplingOptions(SKFilterMode.Nearest));
                surface.Flush();
            }
            _ms = sw.Elapsed.TotalMilliseconds / Frames;
            return surface.Snapshot();
        }

        private static double TimeColorTable(SKImage _source)
        {
            var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            var table = Enumerable.Range(0, 256).Select(i => (byte)(Math.Floor(i / 256.0 * 4) / 3 * 255)).ToArray();
            using var filter = SKColorFilter.CreateTable(identity, table, table, table);
            using var paint = new SKPaint { ColorFilter = filter };
            using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Frames; i++)
            {
                surface.Canvas.DrawImage(_source, 0, 0, paint);
                surface.Flush();
            }
            return sw.Elapsed.TotalMilliseconds / Frames;
        }

        private static double TimeBlur(SKImage _source)
        {
            using var filter = SKImageFilter.CreateBlur(8, 8);
            using var paint = new SKPaint { ImageFilter = filter };
            using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                surface.Canvas.DrawImage(_source, 0, 0, paint);
                surface.Flush();
            }
            return sw.Elapsed.TotalMilliseconds / 10;
        }

        public static int MaxDiff(SKBitmap _a, SKImage _b)
        {
            using var b = SKBitmap.FromImage(_b);
            int max = 0;
            for (int y = 0; y < Math.Min(_a.Height, b.Height); y += 3)
            {
                for (int x = 0; x < Math.Min(_a.Width, b.Width); x += 3)
                {
                    var ca = _a.GetPixel(x, y);
                    var cb = b.GetPixel(x, y);
                    max = Math.Max(max, Math.Max(Math.Abs(ca.Red - cb.Red), Math.Max(Math.Abs(ca.Green - cb.Green), Math.Abs(ca.Blue - cb.Blue))));
                }
            }
            return max;
        }
    }

    internal sealed class Application : Avalonia.Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new Window { Width = 960, Height = 540, Title = "S-3", Content = new ShaderView() };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    internal sealed class ShaderView : Control
    {
        private int frame;

        public override void Render(DrawingContext _context)
        {
            _context.Custom(new Op(new Rect(Bounds.Size), frame++));
            if (frame <= Program.Frames + 2)
            {
                Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
            }
        }

        private sealed class Op : ICustomDrawOperation
        {
            private static readonly List<double> times = new();
            private readonly int frame;

            public Op(Rect _bounds, int _frame)
            {
                Bounds = _bounds;
                frame = _frame;
            }

            public Rect Bounds { get; }

            public bool HitTest(Point _p) => false;

            public bool Equals(ICustomDrawOperation? _other) => false;

            public void Dispose()
            {
            }

            public void Render(ImmediateDrawingContext _context)
            {
                var lease = _context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
                if (lease is null)
                {
                    Console.WriteLine("no skia lease");
                    return;
                }
                using (lease)
                {
                    var gr = lease.GrContext;
                    var info = new SKImageInfo(Program.W, Program.H, SKColorType.Bgra8888, SKAlphaType.Premul);
                    // Draw at full size into an offscreen surface on the same context, then show it scaled.
                    using var surface = gr is not null ? SKSurface.Create(gr, false, info) : SKSurface.Create(info);
                    using var shader = Program.CreateShader(Program.Source!);
                    using var paint = new SKPaint { Shader = shader };
                    var sw = Stopwatch.StartNew();
                    surface.Canvas.DrawRect(0, 0, Program.W, Program.H, paint);
                    surface.Flush(true, true);
                    if (frame > 1)
                    {
                        times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    using var snapshot = surface.Snapshot();
                    lease.SkCanvas.DrawImage(snapshot, SKRect.Create(0, 0, (float)Bounds.Width, (float)Bounds.Height));
                    if (frame == Program.Frames + 1)
                    {
                        var bitmap = new SKBitmap(info);
                        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
                        int diff = Program.MaxDiff(bitmap, Program.Reference!);
                        double avg = times.Average();
                        Console.WriteLine($"window: gpu={gr is not null} backend={gr?.Backend} {avg:F2} ms/frame, max diff vs cpu = {diff}/255");
                        Console.WriteLine(gr is not null
                            ? $"pass (gpu < 2 ms, diff <= 1): {avg < 2 && diff <= 1}"
                            : $"software path (diff <= 1): {diff <= 1}");
                    }
                }
            }
        }
    }

    /// <summary>Deterministic test image: gradients, thin text-like strokes, a grid and flat regions.</summary>
    internal static class Synthetic
    {
        public static SKImage Create(int _w, int _h)
        {
            using var surface = SKSurface.Create(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
            var c = surface.Canvas;
            using (var g = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(_w, _h), new[] { SKColors.MidnightBlue, SKColors.Orange, SKColors.Teal }, SKShaderTileMode.Clamp) })
            {
                c.DrawRect(0, 0, _w, _h, g);
            }
            using var stroke = new SKPaint { Color = SKColors.White, StrokeWidth = 1, IsAntialias = false };
            var rng = new Random(1);
            for (int y = 20; y < _h; y += 16)
            {
                for (int x = 20; x < _w - 40; x += rng.Next(6, 14))
                {
                    c.DrawLine(x, y, x + rng.Next(2, 8), y + rng.Next(-4, 9), stroke);
                }
            }
            using var grid = new SKPaint { Color = new SKColor(0, 0, 0, 120), StrokeWidth = 1 };
            for (int x = 0; x < _w; x += 64)
            {
                c.DrawLine(x, 0, x, _h, grid);
            }
            c.DrawRect(_w * 0.6f, _h * 0.6f, 300, 200, new SKPaint { Color = SKColors.Crimson });
            return surface.Snapshot();
        }
    }
}
