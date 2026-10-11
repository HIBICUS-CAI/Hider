namespace Hider.Core.Mosaic
{
    /// <summary>
    /// Optional for methods that work through the fixed pre-blur stage (the Gaussian blur method, design §4.2): their radius is
    /// combined with the common "blur" value as Gaussian radii combine, sqrt(common² + method²).
    /// </summary>
    public interface IPreBlurMethod
    {
        /// <summary>Additional blur radius in px (effective value, already interpolated with the opacity).</summary>
        double PreBlurRadius(ParamValues _p);
    }
}
