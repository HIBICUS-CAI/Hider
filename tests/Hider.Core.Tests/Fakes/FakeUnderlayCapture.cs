using Hider.Core.Capture;

namespace Hider.Core.Tests.Fakes
{
    /// <summary>Returns the synthetic image (or a configured failure) for any request and counts the calls.</summary>
    public sealed class FakeUnderlayCapture : IUnderlayCapture
    {
        public CaptureTraits Traits { get; set; } = CaptureTraits.Live | CaptureTraits.ExposureFree;
        public CaptureFailure Failure { get; set; } = CaptureFailure.None;
        public int Calls { get; private set; }
        public List<CaptureRequest> Requests { get; } = new();

        public Task<CaptureResult> CaptureAsync(CaptureRequest _request, CancellationToken _ct)
        {
            Calls++;
            Requests.Add(_request);
            if (Failure != CaptureFailure.None)
            {
                return Task.FromResult(CaptureResult.Failed(Failure));
            }
            int w = Math.Max(1, _request.Rect.Width), h = Math.Max(1, _request.Rect.Height);
            var frame = new FrameBuffer { Width = w, Height = h, Stride = w * 4, Bgra = SyntheticImage.CreatePixels(w, h) };
            return Task.FromResult(new CaptureResult(frame, CaptureFailure.None));
        }

        public void Dispose()
        {
        }
    }
}
