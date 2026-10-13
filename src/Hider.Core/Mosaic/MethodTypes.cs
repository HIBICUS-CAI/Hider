using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>Mosaic: processes the captured content. Generated: draws without any input (fallback, FR-52).</summary>
    public enum MethodKind
    {
        Mosaic,
        Generated,
    }

    [Flags]
    public enum PlatformSupport
    {
        Windows = 1,
        Linux = 2,
        All = Windows | Linux,
    }

    /// <summary>Inputs of a method's shader. Skia types stay behind this class (design §4.6).</summary>
    public sealed class MosaicInputs
    {
        /// <summary>Captured image in physical pixels; null for generated methods.</summary>
        public SKImage? Source { get; init; }

        /// <summary>[0] = Source, [k] = 1/2^k box-filtered (empty for generated methods).</summary>
        public IReadOnlyList<SKImage> Pyramid { get; init; } = Array.Empty<SKImage>();

        /// <summary>Area size in physical pixels.</summary>
        public SKSizeI AreaSize { get; init; }

        /// <summary>Area origin on the virtual screen in physical pixels.</summary>
        public SKPointI AreaOrigin { get; init; }
    }

    /// <summary>
    /// A mosaic or generated method. Adding one = an .sksl file under Mosaic/Methods, a class implementing this interface
    /// (usually derived from <see cref="SkslMethod"/>), one line in <see cref="MethodRegistry"/>, labels in Strings and a
    /// golden image test (design §4.3).
    /// </summary>
    public interface IMosaicMethod
    {
        /// <summary>Key in areas.json. Never rename.</summary>
        string Id { get; }

        string LabelKey { get; }

        MethodKind Kind { get; }

        PlatformSupport Support { get; }

        /// <summary>The method's own settings (the kind's common values are in <see cref="CommonParams"/>).</summary>
        IReadOnlyList<ParamSpec> Params { get; }

        SKShader CreateShader(MosaicInputs _inputs, ParamValues _p);
    }
}
