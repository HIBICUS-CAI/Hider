namespace Hider.Core.Model
{
    /// <summary>The eight resize handles of an area in edit mode, plus the body (move).</summary>
    [Flags]
    public enum ResizeHandle
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        TopLeft = Top | Left,
        TopRight = Top | Right,
        BottomLeft = Bottom | Left,
        BottomRight = Bottom | Right,
    }
}
