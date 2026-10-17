namespace Hider.Core.Tests.Fakes
{
    /// <summary>
    /// A random source for AreaId.New: NextInt64 returns the given values in order, records the range each call asked for,
    /// and fails the test when more values are drawn than scripted.
    /// </summary>
    public sealed class ScriptedRandom(params long[] _values) : Random
    {
        private readonly Queue<long> values = new(_values);

        /// <summary>The values not drawn yet.</summary>
        public int Left => values.Count;

        /// <summary>The (min, max) of every NextInt64 call, in order.</summary>
        public List<(long Min, long Max)> Ranges { get; } = new();

        public override long NextInt64(long _minValue, long _maxValue)
        {
            Ranges.Add((_minValue, _maxValue));
            Assert.True(values.Count > 0, "more ids drawn than scripted");
            return values.Dequeue();
        }
    }
}
