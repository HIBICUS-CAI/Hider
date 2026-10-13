namespace Hider.Core.Mosaic
{
    /// <summary>Unit of a <see cref="ParamSpec"/>; its text comes from <see cref="Strings.Unit"/> (FR-70).</summary>
    public enum ParamUnit
    {
        None,
        Px,
        Colors,
        Steps,

        /// <summary>The stored value is a fraction, shown times 100 (0.2 = 20%).</summary>
        Percent,
        Ms,
    }
}
