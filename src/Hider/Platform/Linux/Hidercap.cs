using System.Runtime.InteropServices;
using Hider.Core;

namespace Hider.Platform.Linux
{
    /// <summary>One frame's description from libhidercap (struct hidercap_frame).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HidercapFrame
    {
        public int Width;
        public int Height;
        public int Stride;
        public int PtsFromMeta;
        public long PtsNs;
        public ulong Sequence;
    }

    /// <summary>libhidercap's API (native/hidercap/hidercap.h), behind an interface so the grabber can be tested without it.</summary>
    public interface IHidercap
    {
        /// <summary>Loads the library (and libpipewire); false when either is missing (CT-92: stage 1 takes over).</summary>
        bool IsAvailable { get; }

        /// <summary>Takes ownership of <paramref name="_fd"/>.</summary>
        int Start(int _fd, uint _nodeId);

        int GrabAfter(long _notBeforeNs, int _timeoutMs, Span<byte> _dst, out HidercapFrame _frame);

        ulong LastSequence();

        void Stop();
    }

    /// <summary>The real libhidercap.so next to the executable (scripts/build.py builds it; Hider.csproj ships it).</summary>
    public sealed partial class Hidercap : IHidercap
    {
        public const int Ok = 0;
        public const int BufferTooSmall = 1;
        public const int Timeout = -1;
        public const int NotStarted = -2;
        public const int StreamError = -3;
        public const int AlreadyStarted = -4;

        private const string Library = "hidercap";

        private static readonly Lazy<bool> available = new(Load);

        public bool IsAvailable => available.Value;

        private static bool Load()
        {
            // The app directory first (single-file publish puts native libraries next to the executable), then the default
            // search. Loading also pulls in libpipewire-0.3.so.0, so a system without PipeWire fails here too.
            string local = Path.Combine(AppContext.BaseDirectory, "libhidercap.so");
            if (NativeLibrary.TryLoad(local, out var handle) || NativeLibrary.TryLoad("libhidercap.so", out handle))
            {
                NativeLibrary.SetDllImportResolver(typeof(Hidercap).Assembly, (_name, _, _) => _name == Library ? handle : 0);
                return true;
            }
            Log.Verbose("libhidercap.so could not be loaded: the Screenshot portal is used (stage 1)");
            return false;
        }

        public int Start(int _fd, uint _nodeId)
        {
            return hidercap_start(_fd, _nodeId);
        }

        public unsafe int GrabAfter(long _notBeforeNs, int _timeoutMs, Span<byte> _dst, out HidercapFrame _frame)
        {
            fixed (byte* dst = _dst)
            {
                return hidercap_grab_after(_notBeforeNs, _timeoutMs, dst, _dst.Length, out _frame);
            }
        }

        public ulong LastSequence()
        {
            return hidercap_last_sequence();
        }

        public void Stop()
        {
            hidercap_stop();
        }

        [LibraryImport(Library)]
        private static partial int hidercap_start(int _fd, uint _nodeId);

        [LibraryImport(Library)]
        private static unsafe partial int hidercap_grab_after(long _notBeforeNs, int _timeoutMs, byte* _dst, int _dstSize, out HidercapFrame _frame);

        [LibraryImport(Library)]
        private static partial ulong hidercap_last_sequence();

        [LibraryImport(Library)]
        private static partial void hidercap_stop();
    }
}
