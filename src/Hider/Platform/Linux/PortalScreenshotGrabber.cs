using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace Hider.Platform.Linux
{
    /// <summary>
    /// Linux stage 1 (D-6): org.freedesktop.portal.Screenshot with interactive=false. GNOME asks for permission once and
    /// remembers it (S-5). The portal writes a PNG and returns its URI; it is read and deleted at once, so no snapshot stays
    /// on disk (FR-53). Each call takes 0.1 .. 0.3 s (design §5.3), which is why stage 2 exists.
    /// </summary>
    public sealed class PortalScreenshotGrabber : IScreenGrabber
    {
        private readonly Func<PhysicalRect> screen;
        private readonly PortalClient portal;
        private bool answered;

        /// <param name="_screen">The virtual screen in physical px: what a screenshot shows.</param>
        /// <param name="_address">D-Bus address; null for the session bus.</param>
        public PortalScreenshotGrabber(Func<PhysicalRect> _screen, string? _address = null)
        {
            screen = _screen;
            portal = new PortalClient(_address);
        }

        /// <summary>
        /// The portal shoots when called, and the coordinator calls after the windows have shown their transparent rows, so
        /// every result is newer than <paramref name="_notBeforeTimestamp"/>.
        /// </summary>
        public async Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct)
        {
            string? path = null;
            try
            {
                var bus = await portal.ConnectAsync(_ct).ConfigureAwait(false);
                if (bus is null)
                {
                    return null;
                }
                var response = await portal.RequestAsync(bus, "org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}", (ref MessageWriter w, string token) =>
                {
                    w.WriteString("");
                    var dict = w.WriteDictionaryStart();
                    PortalClient.Entry(ref w, "handle_token", token);
                    w.WriteDictionaryEntryStart();
                    w.WriteString("interactive");
                    w.WriteVariantBool(false);
                    w.WriteDictionaryEnd(dict);
                }, _ct).ConfigureAwait(false);
                if (response.Results.TryGetValue("uri", out var value) && value.GetString() is { } uri && uri.StartsWith("file://", StringComparison.Ordinal))
                {
                    path = Uri.UnescapeDataString(new Uri(uri).AbsolutePath);
                }
                switch (response.Code)
                {
                    case 0:
                        var frame = path is null ? null : ReadPng(path);
                        answered |= frame is not null;
                        return frame is null ? null : new ScreenFrame(frame, screen());
                    case 1:
                        throw new CaptureDeniedException("screenshot portal: cancelled by the user");
                    default:
                        Log.Verbose($"screenshot portal: response {response.Code}");
                        return null;
                }
            }
            catch (Exception e) when (e is not (OperationCanceledException or CaptureDeniedException))
            {
                Log.Verbose($"screenshot portal: {e.GetType().Name}: {e.Message}");
                portal.Reset();
                return null;
            }
            finally
            {
                if (path is not null)
                {
                    TryDelete(path);
                }
            }
        }

        /// <summary>
        /// The first capture of a run takes one throwaway screenshot while everything is still covered: GNOME may ask for
        /// permission then, and that dialog must not wait with a strip transparent (NFR-05). Later captures need nothing.
        /// </summary>
        public async Task<bool> PrepareAsync(PhysicalRect _needed, CancellationToken _ct)
        {
            if (answered)
            {
                return true;
            }
            return await GrabAfterAsync(0, _needed, _ct).ConfigureAwait(false) is not null;
        }

        /// <summary>Decodes the portal's PNG into a premultiplied BGRA frame (null if it cannot be read).</summary>
        internal static FrameBuffer? ReadPng(string _path)
        {
            using var codec = SKCodec.Create(_path);
            if (codec is null)
            {
                return null;
            }
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var frame = FrameBuffer.Allocate(info.Width, info.Height);
            unsafe
            {
                fixed (byte* pixels = frame.Bgra.Span)
                {
                    var result = codec.GetPixels(info, (nint)pixels);
                    return result is SKCodecResult.Success or SKCodecResult.IncompleteInput ? frame : null;
                }
            }
        }

        private static void TryDelete(string _path)
        {
            try
            {
                File.Delete(_path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Verbose($"could not delete the portal screenshot {_path}: {e.Message}");
            }
        }

        public void Dispose()
        {
            portal.Dispose();
        }
    }
}
