using Hider.Core.Model;

namespace Hider.Core.Capture
{
    /// <summary>
    /// Tries grabbers in order (Linux: ScreenCast + helper, then the Screenshot portal, D-6). The next one is asked when a
    /// grabber has no frame for the needed rectangle (helper missing, area on another monitor) or was refused; the refusal
    /// is reported only when no grabber produced a frame.
    /// </summary>
    public sealed class FallbackGrabber : IScreenGrabber
    {
        private readonly IReadOnlyList<IScreenGrabber> grabbers;

        public FallbackGrabber(params IScreenGrabber[] _grabbers)
        {
            grabbers = _grabbers;
        }

        public async Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct)
        {
            CaptureDeniedException? denied = null;
            foreach (var grabber in grabbers)
            {
                try
                {
                    var frame = await grabber.GrabAfterAsync(_notBeforeTimestamp, _needed, _ct).ConfigureAwait(false);
                    if (frame is not null)
                    {
                        return frame;
                    }
                }
                catch (CaptureDeniedException e)
                {
                    denied ??= e;
                }
            }
            return denied is null ? null : throw denied;
        }

        /// <summary>
        /// Prepares the grabbers in order until one expects to deliver (so a later one never opens its dialog while a strip
        /// is transparent). A refusal is reported only when no grabber is ready.
        /// </summary>
        public async Task<bool> PrepareAsync(PhysicalRect _needed, CancellationToken _ct)
        {
            CaptureDeniedException? denied = null;
            foreach (var grabber in grabbers)
            {
                try
                {
                    if (await grabber.PrepareAsync(_needed, _ct).ConfigureAwait(false))
                    {
                        return true;
                    }
                }
                catch (CaptureDeniedException e)
                {
                    denied ??= e;
                }
            }
            return denied is null ? false : throw denied;
        }

        public void Dispose()
        {
            foreach (var grabber in grabbers)
            {
                grabber.Dispose();
            }
        }
    }
}
