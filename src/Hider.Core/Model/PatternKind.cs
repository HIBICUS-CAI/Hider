namespace Hider.Core.Model
{
    /// <summary>How an area is drawn. Its JSON names ("fill" | "mosaic" | "pattern", spec §4.7) are LenientPatternKindConverter's.</summary>
    public enum PatternKind
    {
        Fill,

        Mosaic,

        Generated,
    }
}
