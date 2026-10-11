using Hider.Core.Mosaic.Methods;

namespace Hider.Core.Mosaic
{
    /// <summary>Every method known at compile time (D-12: no runtime plugins).</summary>
    public static class MethodRegistry
    {
        public static IReadOnlyList<IMosaicMethod> All { get; } = new IMosaicMethod[]
        {
            new PixelateMethod(),
            new BlockMethod(),
        };

        public static IMosaicMethod? Find(string _id)
        {
            return All.FirstOrDefault(m => m.Id == _id);
        }

        public static IReadOnlyList<IMosaicMethod> OfKind(MethodKind _kind)
        {
            return All.Where(m => m.Kind == _kind).ToArray();
        }

        /// <summary>Methods usable on a platform.</summary>
        public static IReadOnlyList<IMosaicMethod> ForPlatform(PlatformSupport _platform)
        {
            return Filter(All, _platform);
        }

        public static IReadOnlyList<IMosaicMethod> Filter(IEnumerable<IMosaicMethod> _methods, PlatformSupport _platform)
        {
            return _methods.Where(m => (m.Support & _platform) == _platform).ToArray();
        }

        /// <summary>The platform this process runs on.</summary>
        public static PlatformSupport CurrentPlatform => OperatingSystem.IsWindows() ? PlatformSupport.Windows : PlatformSupport.Linux;
    }
}
