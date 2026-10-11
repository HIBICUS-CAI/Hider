using Hider.Core.Model;

namespace Hider.Core.Capture
{
    /// <summary>
    /// Grabs the whole screen (Linux, design §5.3). The frame may be scaled relative to the physical virtual screen
    /// (fractional scaling); <see cref="StripCoordinator"/> maps the coordinates.
    /// </summary>
    public interface IScreenGrabber : IDisposable
    {
        /// <summary>
        /// A frame shown no earlier than <paramref name="_notBeforeTimestamp"/> (a <see cref="TimeProvider.GetTimestamp"/>
        /// value of the coordinator's clock; with the system clock on Linux, CLOCK_MONOTONIC). Null when no frame can be had
        /// (FR-52); throws <see cref="CaptureDeniedException"/> when the user refused.
        /// </summary>
        Task<FrameBuffer?> GrabAfterAsync(long _notBeforeTimestamp, CancellationToken _ct);
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
