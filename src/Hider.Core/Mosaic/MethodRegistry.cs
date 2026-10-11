using System.Collections.Frozen;
using Hider.Core.Mosaic.Methods;

namespace Hider.Core.Mosaic
{
    /// <summary>Every method known at compile time (D-12: no runtime plugins).</summary>
    public static class MethodRegistry
    {
        public static IReadOnlyList<IMosaicMethod> All { get; } = new IMosaicMethod[]
        {
            new PixelateMethod(),
            new HexcellMethod(),
            new BlurMethod(),
            new BlockMethod(),
            new NoiseMethod(),
        };

        // Looked up on every repaint: built once.
        private static readonly FrozenDictionary<string, IMosaicMethod> byId = All.ToFrozenDictionary(_m => _m.Id, StringComparer.Ordinal);
        private static readonly IMosaicMethod[] mosaic = All.Where(_m => _m.Kind == MethodKind.Mosaic).ToArray();
        private static readonly IMosaicMethod[] generated = All.Where(_m => _m.Kind == MethodKind.Generated).ToArray();

        public static IMosaicMethod? Find(string _id)
        {
            return byId.GetValueOrDefault(_id);
        }

        public static IReadOnlyList<IMosaicMethod> OfKind(MethodKind _kind)
        {
            return _kind == MethodKind.Mosaic ? mosaic : generated;
        }

        /// <summary>Methods usable on a platform.</summary>
        public static IReadOnlyList<IMosaicMethod> ForPlatform(PlatformSupport _platform)
        {
            return Filter(All, _platform);
        }

        public static IReadOnlyList<IMosaicMethod> Filter(IEnumerable<IMosaicMethod> _methods, PlatformSupport _platform)
        {
            return _methods.Where(_m => (_m.Support & _platform) == _platform).ToArray();
        }

        /// <summary>The platform this process runs on.</summary>
        public static PlatformSupport CurrentPlatform => OperatingSystem.IsWindows() ? PlatformSupport.Windows : PlatformSupport.Linux;
    }
}
