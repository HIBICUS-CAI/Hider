namespace Hider.Core.Capture
{
    /// <summary>
    /// Double buffer between the capture thread and the render thread (work instructions §3.5). The capture side fills
    /// <see cref="Back"/> without locking and then <see cref="Publish"/>es it; the render side reads <see cref="Front"/> only
    /// inside <see cref="Read{T}"/>. Publishing waits for a read in progress, so the buffer being read is never written.
    /// </summary>
    public sealed class FrameExchange
    {
        private readonly object gate = new();
        private FrameBuffer back = new();
        private FrameBuffer? front;
        private long version;

        /// <summary>The buffer the capture side may write. Only the capture thread touches it.</summary>
        public FrameBuffer Back => back;

        /// <summary>Incremented on every publish; lets the renderer know a new frame arrived.</summary>
        public long Version
        {
            get
            {
                lock (gate)
                {
                    return version;
                }
            }
        }

        /// <summary>Makes <see cref="Back"/> the front frame and hands the previous front back for writing.</summary>
        public void Publish()
        {
            lock (gate)
            {
                var previous = front;
                front = back;
                back = previous ?? new FrameBuffer();
                version++;
            }
        }

        /// <summary>Drops the front frame (e.g. after a capture failure).</summary>
        public void Clear()
        {
            lock (gate)
            {
                front = null;
                version++;
            }
        }

        /// <summary>Runs <paramref name="_reader"/> with the front frame (null when none) while publishing is held back.</summary>
        public T Read<T>(Func<FrameBuffer?, long, T> _reader)
        {
            lock (gate)
            {
                return _reader(front, version);
            }
        }
    }
}
