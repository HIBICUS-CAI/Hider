using Hider.Core.Model;

namespace Hider.Core.Capture
{
    /// <summary>
    /// Tries grabbers in order (Linux: ScreenCast + helper, then the Screenshot portal, D-6). The next one is asked when a
    /// grabber has no frame for the needed rectangle (helper missing, area on another monitor) or was refused; the refusal
    /// is reported only when no grabber produced a frame. Only the grabbers up to the one <see cref="PrepareAsync"/> found
    /// ready are asked for strips, so an unprepared one never starts slow or interactive work with a strip transparent.
    /// The coordinator runs one capture at a time, so the prepared index needs no lock.
    /// </summary>
    public sealed class FallbackGrabber : IScreenGrabber
    {
        private readonly IReadOnlyList<IScreenGrabber> grabbers;
        private int prepared = -1;

        public FallbackGrabber(params IScreenGrabber[] _grabbers)
        {
            grabbers = _grabbers;
        }

        public async Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct)
        {
            CaptureDeniedException? denied = null;
            for (int i = 0; i <= prepared && i < grabbers.Count; i++)
            {
                try
                {
                    var frame = await grabbers[i].GrabAfterAsync(_notBeforeTimestamp, _needed, _ct).ConfigureAwait(false);
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
            prepared = -1;
            CaptureDeniedException? denied = null;
            for (int i = 0; i < grabbers.Count; i++)
            {
                try
                {
                    if (await grabbers[i].PrepareAsync(_needed, _ct).ConfigureAwait(false))
                    {
                        prepared = i;
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
