using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// Optional for methods whose shader is too slow on Skia's CPU raster (branches and hashes cost 300 .. 550 ns per pixel
    /// there, CT-58). On a CPU canvas the pipeline lets the method fill the area's pixels itself; on the GPU the shader stays
    /// the implementation. The shader is the reference: a test compares both on the CPU (MethodConsistencyTests). Images are
    /// read with Skia's raster rules (<see cref="RasterSampler"/>, texel edges included), so the two differ only where float
    /// rounding moves a cell boundary or a linear sample by one.
    /// </summary>
    public interface ICpuRenderer
    {
        /// <summary>
        /// Fills <paramref name="_dst"/> (area size, premultiplied BGRA) with what the shader would draw, before the color
        /// levels. Returns false to fall back to the shader (e.g. values where the shader is cheap).
        /// </summary>
        bool TryRender(SKPixmap _dst, MosaicInputs _inputs, ParamValues _p);
    }
}
