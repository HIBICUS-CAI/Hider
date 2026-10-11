using Hider.Core.Capture;
using Hider.Platform;

namespace Hider.Areas
{
    /// <summary>What the area code needs from the platform, gathered so tests can swap any part.</summary>
    public sealed record AreaServices(
        IScreenService Screens,
        Func<AreaWindow, IAreaWindowHost> HostFactory,
        string AreasPath,
        TimeProvider Time,
        Action<Action> UiPost,
        bool IsWindows,
        IUnderlayCapture Capture,
        Func<Func<Task<CaptureResult>>, Task<CaptureResult>> RunCapture)
    {
        /// <summary>Runs captures on the thread pool (never on the UI thread, work instructions §2.3).</summary>
        public static Task<CaptureResult> OnThreadPool(Func<Task<CaptureResult>> _capture)
        {
            return Task.Run(_capture);
        }
    }
}
