using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// Optional for methods whose output is constant inside square cells aligned to the area origin. The pipeline then runs
    /// the shader once per cell on a reduced surface and scales it up with nearest sampling, which gives the same pixels at a
    /// fraction of the cost on the CPU path (proposal P-1, spike S-3). The shader itself is unchanged.
    /// </summary>
    public interface ICellMethod
    {
        /// <summary>Cell size in physical pixels for these values, or 1 when the output is not cell-constant.</summary>
        int CellSize(ParamValues _p);

        /// <summary>
        /// Draws anything that is not constant per cell on top of the upscaled cells, with plain fills (e.g. the joints of the
        /// generated blocks). Canvas units are area pixels. Default: nothing.
        /// </summary>
        void DrawJoints(SKCanvas _canvas, SKSizeI _size, int _cell, ParamValues _p)
        {
        }
    }
}
