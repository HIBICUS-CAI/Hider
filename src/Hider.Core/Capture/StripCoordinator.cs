using Hider.Core.Model;

namespace Hider.Core.Capture
{
    /// <summary>
    /// Linux capture by strips (spec §6 Linux, design §5.3). The area windows cannot be left out of a screen grab, so each
    /// strip of rows is made transparent in every mosaic area that overlaps the requested one, a frame shown after that is
    /// grabbed, the strip's rows are copied, and the areas are restored. Only one strip is ever exposed (NFR-03); strip
    /// height 0 means the whole area at once (decision 8). Captures of all areas run one after another.
    /// </summary>
    public sealed class StripCoordinator : IUnderlayCapture
    {
        private readonly IScreenGrabber grabber;
        private readonly IStripHost host;
        private readonly Func<IReadOnlyList<StripArea>> areas;
        private readonly TimeProvider time;
        private readonly Func<PhysicalRect>? screen;
        // Not disposed: a capture in flight at Dispose releases it afterwards (SemaphoreSlim holds nothing unmanaged unless
        // its AvailableWaitHandle is read, which is never done here).
        private readonly SemaphoreSlim serial = new(1, 1);
        private volatile bool disposed;

        /// <param name="_grabber">Takes the screen frames (owned: disposed with the coordinator).</param>
        /// <param name="_host">Makes the area windows' strips transparent and back.</param>
        /// <param name="_areas">Current areas; called on the capture thread, so it must be safe to call from there.</param>
        /// <param name="_time">Clock of the not-before timestamps; the system clock when null.</param>
        /// <param name="_screen">
        /// The union of the monitors (what a screenshot covers); empty or null when unknown. Only the part of an area on it is
        /// captured, the rest is opaque black (as on Windows).
        /// </param>
        public StripCoordinator(IScreenGrabber _grabber, IStripHost _host, Func<IReadOnlyList<StripArea>> _areas, TimeProvider? _time = null,
            Func<PhysicalRect>? _screen = null)
        {
            grabber = _grabber;
            host = _host;
            areas = _areas;
            time = _time ?? TimeProvider.System;
            screen = _screen;
        }

        public CaptureTraits Traits => CaptureTraits.NeedsStripCooperation;

        /// <summary>Rows [y0, y1) of each strip of an area <paramref name="_height"/> px high (CT-79, CT-80).</summary>
        public static IEnumerable<(int Y0, int Y1)> Strips(int _height, int _stripPx)
        {
            int step = _stripPx <= 0 ? Math.Max(1, _height) : _stripPx;
            for (int y = 0; y < _height; y += step)
            {
                yield return (y, Math.Min(_height, y + step));
            }
        }

        public async Task<CaptureResult> CaptureAsync(CaptureRequest _request, CancellationToken _ct)
        {
            var rect = _request.Rect;
            if (rect.IsEmpty)
            {
                return CaptureResult.Failed(CaptureFailure.Unavailable);
            }
            ObjectDisposedException.ThrowIf(disposed, this);
            await serial.WaitAsync(_ct).ConfigureAwait(false);
            try
            {
                // Queued behind a capture that was running when the coordinator was disposed: nothing is made transparent.
                ObjectDisposedException.ThrowIf(disposed, this);
                // Mosaic areas over the requested rectangle (the requester among them) hide what is below; fill and generated
                // areas stay in the picture, as they would for a person looking at the screen (CT-83).
                var cover = areas().Where(_a => _a.Kind == PatternKind.Mosaic && _a.Rect.Intersects(rect)).ToList();
                // Only what lies on a monitor can be in a frame: an area partly off the screens is captured where it is on
                // them (an area dragged a little past the edge must not fail every time, exposing a strip first).
                var desktop = screen?.Invoke() ?? default;
                var visible = desktop.IsEmpty ? rect : rect.Intersect(desktop);
                if (visible.IsEmpty)
                {
                    return CaptureResult.Failed(CaptureFailure.Unavailable);
                }
                try
                {
                    // No grabber can deliver: fail before any strip is exposed.
                    if (!await grabber.PrepareAsync(visible, _ct).ConfigureAwait(false))
                    {
                        return CaptureResult.Failed(CaptureFailure.Unavailable);
                    }
                }
                catch (CaptureDeniedException e)
                {
                    Log.Verbose($"strip capture denied: {e.Message}");
                    return CaptureResult.Failed(CaptureFailure.Denied);
                }
                var taken = FrameBuffer.Allocate(visible.Width, visible.Height);
                foreach (var (y0, y1) in Strips(visible.Height, _request.StripPx))
                {
                    var failure = await CaptureStripAsync(visible, y0, y1, cover, taken, _ct).ConfigureAwait(false);
                    if (failure != CaptureFailure.None)
                    {
                        return CaptureResult.Failed(failure);
                    }
                }
                return new CaptureResult(visible == rect ? taken : PlaceInBlack(taken, visible, rect), CaptureFailure.None);
            }
            finally
            {
                serial.Release();
            }
        }

        private async Task<CaptureFailure> CaptureStripAsync(PhysicalRect _rect, int _y0, int _y1, List<StripArea> _cover, FrameBuffer _dst, CancellationToken _ct)
        {
            int top = _rect.Y + _y0, bottom = _rect.Y + _y1;
            var touched = new List<string>();
            try
            {
                foreach (var area in _cover)
                {
                    int from = Math.Max(top, area.Rect.Y) - area.Rect.Y, to = Math.Min(bottom, area.Rect.Bottom) - area.Rect.Y;
                    if (from < to)
                    {
                        touched.Add(area.Id);
                        await host.SetTransparentRowsAsync(area.Id, from, to, _ct).ConfigureAwait(false);
                    }
                }
                // Every window has shown the transparent rows by now: any later frame is free of them (CT-82).
                long notBefore = time.GetTimestamp();
                // Only this strip's rows are needed from the frame (ScreenCast copies just those, CopyRows reads just those):
                // a frame of one monitor serves the strips on it even when the area spans monitors stacked vertically.
                var strip = new PhysicalRect(_rect.X, top, _rect.Width, bottom - top);
                ScreenFrame? frame;
                try
                {
                    frame = await grabber.GrabAfterAsync(notBefore, strip, _ct).ConfigureAwait(false);
                }
                catch (CaptureDeniedException e)
                {
                    Log.Verbose($"strip capture denied: {e.Message}");
                    return CaptureFailure.Denied;
                }
                if (frame is null || frame.Covers.Intersect(strip) != strip)
                {
                    return CaptureFailure.Unavailable;
                }
                CopyRows(frame.Frame, frame.Covers, _rect, _y0, _y1, _dst);
                return CaptureFailure.None;
            }
            finally
            {
                // Also after a failure or cancellation: an area must never stay see-through (NFR-05, CT-85, CT-86).
                foreach (string id in touched)
                {
                    try
                    {
                        await host.RestoreAsync(id).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Log.Verbose($"restoring area {id} failed: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Copies rows [y0, y1) of the area from a full-screen frame. The frame covers <paramref name="_bounds"/>, possibly at
        /// another scale (CT-88): area pixel centres are mapped into it and sampled nearest.
        /// </summary>
        public static void CopyRows(FrameBuffer _frame, PhysicalRect _bounds, PhysicalRect _rect, int _y0, int _y1, FrameBuffer _dst)
        {
            double sx = _bounds.Width > 0 ? (double)_frame.Width / _bounds.Width : 1.0;
            double sy = _bounds.Height > 0 ? (double)_frame.Height / _bounds.Height : 1.0;
            var src = _frame.Bgra.Span;
            var dst = _dst.Bgra.Span;
            bool unscaled = sx == 1.0 && sy == 1.0;
            var columns = new int[_rect.Width];
            for (int x = 0; x < columns.Length; x++)
            {
                columns[x] = Math.Clamp((int)Math.Floor((_rect.X + x + 0.5 - _bounds.X) * sx), 0, _frame.Width - 1);
            }
            for (int y = _y0; y < _y1; y++)
            {
                int fy = Math.Clamp((int)Math.Floor((_rect.Y + y + 0.5 - _bounds.Y) * sy), 0, _frame.Height - 1);
                var srcRow = src.Slice(fy * _frame.Stride);
                var dstRow = dst.Slice(y * _dst.Stride, _rect.Width * 4);
                if (unscaled && columns[0] + _rect.Width <= _frame.Width && columns[^1] == columns[0] + _rect.Width - 1)
                {
                    srcRow.Slice(columns[0] * 4, _rect.Width * 4).CopyTo(dstRow);
                    continue;
                }
                for (int x = 0; x < columns.Length; x++)
                {
                    srcRow.Slice(columns[x] * 4, 4).CopyTo(dstRow.Slice(x * 4, 4));
                }
            }
        }

        /// <summary>The area's frame: the part captured on the monitors where it is, opaque black elsewhere.</summary>
        private static FrameBuffer PlaceInBlack(FrameBuffer _taken, PhysicalRect _visible, PhysicalRect _rect)
        {
            var result = FrameBuffer.Allocate(_rect.Width, _rect.Height);
            var dst = result.Bgra.Span;
            for (int i = 3; i < dst.Length; i += 4)
            {
                dst[i] = 255;
            }
            int dx = _visible.X - _rect.X, dy = _visible.Y - _rect.Y;
            var src = _taken.Bgra.Span;
            for (int y = 0; y < _taken.Height; y++)
            {
                src.Slice(y * _taken.Stride, _taken.Width * 4).CopyTo(dst.Slice((y + dy) * result.Stride + dx * 4));
            }
            return result;
        }

        /// <summary>
        /// Disposes the grabber. A capture in flight (app exit) ends with its own outcome: its strip is restored as always,
        /// and a cancellation or result is not replaced by an ObjectDisposedException. Captures queued behind it and later
        /// ones throw ObjectDisposedException before anything is made transparent.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            grabber.Dispose();
        }
    }
}
