using Hider.Core.Model;

namespace Hider.Core.Capture
{
    /// <summary>A grabbed frame and the part of the virtual screen it shows (physical px; the frame may be scaled).</summary>
    public sealed record ScreenFrame(FrameBuffer Frame, PhysicalRect Covers);

    /// <summary>
    /// Grabs the screen (Linux, design §5.3): the whole virtual screen (Screenshot portal) or one monitor (ScreenCast).
    /// <see cref="StripCoordinator"/> maps area coordinates into the frame by <see cref="ScreenFrame.Covers"/>.
    /// </summary>
    public interface IScreenGrabber : IDisposable
    {
        /// <summary>
        /// A frame shown no earlier than <paramref name="_notBeforeTimestamp"/> (a <see cref="TimeProvider.GetTimestamp"/>
        /// value of the coordinator's clock; with the system clock on Linux, CLOCK_MONOTONIC) that covers
        /// <paramref name="_needed"/>. Null when no such frame can be had (FR-52); throws <see cref="CaptureDeniedException"/>
        /// when the user refused.
        /// </summary>
        Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct);
    }

    /// <summary>The area windows' part of the strip method: draw some rows fully transparent, and back.</summary>
    public interface IStripHost
    {
        /// <summary>
        /// Makes rows [<paramref name="_y0"/>, <paramref name="_y1"/>) of the area (area-local physical px) transparent,
        /// completing once that has reached the screen. Replaces earlier transparent rows of the same area.
        /// </summary>
        Task SetTransparentRowsAsync(string _areaId, int _y0, int _y1, CancellationToken _ct);

        /// <summary>Shows the whole area again.</summary>
        Task RestoreAsync(string _areaId);
    }

    /// <summary>An area as the coordinator sees it (spec §6 Linux: only mosaic areas take part).</summary>
    public readonly record struct StripArea(string Id, PhysicalRect Rect, PatternKind Kind);

    /// <summary>The user refused the screen capture permission (portal response "cancelled").</summary>
    public sealed class CaptureDeniedException : Exception
    {
        public CaptureDeniedException(string _message) : base(_message)
        {
        }
    }
}
