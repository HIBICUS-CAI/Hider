using System.Diagnostics;
using System.Runtime.Versioning;
using Hider.Core;
using Hider.Core.Capture;

namespace Hider.Platform.Windows
{
    /// <summary>
    /// Windows underlay capture, stage 1 (design §5.2, D-7): PrintWindow of the windows below the area, composited with
    /// <see cref="UnderlayComposer"/>. Never hides or excludes Hider's windows (NFR-01, NFR-02): the content is read from DWM,
    /// so nothing is exposed and recordings / screen shares still show the mosaic.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class Win32UnderlayCapture : IUnderlayCapture
    {
        private readonly Win32WindowSource source = new();
        private readonly UnderlayComposer composer;
        private readonly object gate = new();
        // One output buffer per area window: an area never runs two captures at once (its scheduler merges requests) and copies
        // the result before asking for the next one, so the buffer can be reused.
        private readonly Dictionary<nint, FrameBuffer> outputs = new();

        public Win32UnderlayCapture()
        {
            composer = new UnderlayComposer(source);
        }

        public CaptureTraits Traits => CaptureTraits.Live | CaptureTraits.ExposureFree;

        public Task<CaptureResult> CaptureAsync(CaptureRequest _request, CancellationToken _ct)
        {
            // Captures of all areas run one at a time: PrintWindow dominates the cost, parallel calls only add contention.
            lock (gate)
            {
                _ct.ThrowIfCancellationRequested();
                nint own = _request.ExcludeWindows.Count > 0 ? _request.ExcludeWindows[0] : 0;
                if (!outputs.TryGetValue(own, out var output))
                {
                    output = new FrameBuffer();
                    outputs[own] = output;
                }
                var sw = Stopwatch.StartNew();
                var result = composer.Compose(_request, output);
                // Forget the bitmaps of closed windows and the output buffers of deleted areas (ExcludeWindows lists every
                // Hider window that still exists).
                source.Trim(source.LastEnumerated);
                foreach (nint gone in outputs.Keys.Where(_h => !_request.ExcludeWindows.Contains(_h)).ToArray())
                {
                    outputs.Remove(gone);
                }
                Log.Verbose($"capture {_request.Rect.Width}x{_request.Rect.Height}: {result.Failure}, {composer.LastCaptured.Count} window(s), {sw.Elapsed.TotalMilliseconds:F1} ms");
                return Task.FromResult(result);
            }
        }

        public void Dispose()
        {
        }
    }
}
