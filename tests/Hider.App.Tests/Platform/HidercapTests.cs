using System.Runtime.InteropServices;
using Hider.Platform.Linux;

namespace Hider.App.Tests.Platform
{
    public class HidercapTests
    {
        [Fact]
        public void FrameStruct_MatchesTheCHeader()
        {
            // struct hidercap_frame { int32 width, height, stride, pts_from_meta; int64 pts_ns; uint64 sequence; }
            Assert.Equal(32, Marshal.SizeOf<HidercapFrame>());
            Assert.Equal(16, (int)Marshal.OffsetOf<HidercapFrame>(nameof(HidercapFrame.PtsNs)));
            Assert.Equal(24, (int)Marshal.OffsetOf<HidercapFrame>(nameof(HidercapFrame.Sequence)));
        }
    }
}
