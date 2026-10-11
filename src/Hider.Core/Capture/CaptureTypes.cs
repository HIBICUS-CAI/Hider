using Hider.Core.Model;

namespace Hider.Core.Capture
{
    /// <summary>Why a capture produced no image (FR-52).</summary>
    public enum CaptureFailure
    {
        None,
        Black,
        Denied,
        Unavailable,
        Timeout,
    }

    [Flags]
    public enum CaptureTraits
    {
        None = 0,

        /// <summary>Cheap enough to follow the content continuously (Windows window capture).</summary>
        Live = 1,

        /// <summary>Capturing never shows the covered content (spec §7).</summary>
        ExposureFree = 2,

        /// <summary>Needs the area windows to make strips transparent while capturing (Linux, spec §6).</summary>
        NeedsStripCooperation = 4,
    }

    /// <summary>A captured image: premultiplied BGRA8888, physical pixels, opaque.</summary>
    public sealed class FrameBuffer
    {
        public int Width;
        public int Height;
        public int Stride;
        public Memory<byte> Bgra;

        public static FrameBuffer Allocate(int _width, int _height)
        {
            return new FrameBuffer { Width = _width, Height = _height, Stride = _width * 4, Bgra = new byte[_width * _height * 4] };
        }

        /// <summary>Makes sure the buffer can hold a frame of this size, reusing the memory when it is large enough.</summary>
        public void Resize(int _width, int _height)
        {
            int needed = _width * _height * 4;
            if (Bgra.Length < needed)
            {
                Bgra = new byte[needed];
            }
            Width = _width;
            Height = _height;
            Stride = _width * 4;
        }
    }

    /// <summary>What to capture: the area rectangle (physical px), Hider's own windows to leave out, the Linux strip height.</summary>
    public sealed record CaptureRequest(PhysicalRect Rect, IReadOnlyList<nint> ExcludeWindows, int StripPx);

    public sealed record CaptureResult(FrameBuffer? Frame, CaptureFailure Failure)
    {
        public static CaptureResult Failed(CaptureFailure _failure) => new(null, _failure);
    }

    /// <summary>Gets the content under an area without Hider's windows (design §5).</summary>
    public interface IUnderlayCapture : IDisposable
    {
        CaptureTraits Traits { get; }

        /// <summary>Captures the rectangle. Runs on a capture thread; never on the UI thread.</summary>
        Task<CaptureResult> CaptureAsync(CaptureRequest _request, CancellationToken _ct);
    }

    /// <summary>Used where no capture exists (yet): every request reports <see cref="CaptureFailure.Unavailable"/> (FR-52).</summary>
    public sealed class UnavailableCapture : IUnderlayCapture
    {
        public CaptureTraits Traits => CaptureTraits.ExposureFree;

        public Task<CaptureResult> CaptureAsync(CaptureRequest _request, CancellationToken _ct)
        {
            return Task.FromResult(CaptureResult.Failed(CaptureFailure.Unavailable));
        }

        public void Dispose()
        {
        }
    }
}
