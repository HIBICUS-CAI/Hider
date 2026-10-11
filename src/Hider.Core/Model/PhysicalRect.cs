namespace Hider.Core.Model
{
    /// <summary>A rectangle in physical pixels (virtual screen coordinates).</summary>
    public readonly record struct PhysicalRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        /// <summary>Builds a rectangle from two corners given in any order.</summary>
        public static PhysicalRect FromEdges(int _left, int _top, int _right, int _bottom)
        {
            return new PhysicalRect(Math.Min(_left, _right), Math.Min(_top, _bottom), Math.Abs(_right - _left), Math.Abs(_bottom - _top));
        }

        /// <summary>Same area with a non-negative width and height (a negative size flips the origin).</summary>
        public PhysicalRect Normalized()
        {
            return FromEdges(X, Y, X + Width, Y + Height);
        }

        public bool Intersects(PhysicalRect _other)
        {
            return !IsEmpty && !_other.IsEmpty
                && X < _other.Right && _other.X < Right
                && Y < _other.Bottom && _other.Y < Bottom;
        }

        public PhysicalRect Intersect(PhysicalRect _other)
        {
            int left = Math.Max(X, _other.X);
            int top = Math.Max(Y, _other.Y);
            int right = Math.Min(Right, _other.Right);
            int bottom = Math.Min(Bottom, _other.Bottom);
            return right <= left || bottom <= top ? default : new PhysicalRect(left, top, right - left, bottom - top);
        }

        public PhysicalRect Offset(int _dx, int _dy)
        {
            return this with { X = X + _dx, Y = Y + _dy };
        }
    }
}
