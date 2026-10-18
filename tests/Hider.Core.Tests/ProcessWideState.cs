namespace Hider.Core.Tests
{
    /// <summary>
    /// Tests that change process-wide state (standard error, Log.Enabled, the self test's fixed temp file name) run in this
    /// collection: alone, after the parallel ones, so no other test sees the change.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ProcessWideState
    {
        public const string Name = "Process-wide state";
    }
}
