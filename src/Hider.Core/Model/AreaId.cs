namespace Hider.Core.Model
{
    /// <summary>Area ids: 8 lower case hex digits, unique among the areas that exist.</summary>
    public static class AreaId
    {
        /// <param name="_isTaken">Returns true for ids already in use; a new id is drawn until it returns false.</param>
        public static string New(Func<string, bool> _isTaken, Random? _random = null)
        {
            var random = _random ?? Random.Shared;
            while (true)
            {
                string id = ((uint)random.NextInt64(0, 1L << 32)).ToString("x8");
                if (!_isTaken(id))
                {
                    return id;
                }
            }
        }

        public static bool IsValid(string? _id)
        {
            return _id is { Length: 8 } && _id.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));
        }
    }
}
