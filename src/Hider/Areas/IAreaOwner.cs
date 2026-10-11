using Hider.Platform;

namespace Hider.Areas
{
    /// <summary>What an <see cref="AreaController"/> needs from the code that owns all areas (the <see cref="AreaManager"/>).</summary>
    public interface IAreaOwner
    {
        IScreenService Screens { get; }

        /// <summary>Saves now: add, delete, pattern change, drag end, edit end (FR-61).</summary>
        void SaveNow();

        /// <summary>Saves after 500 ms without further changes: keyboard moves (FR-61).</summary>
        void SaveSoon();

        void Delete(AreaController _area);

        /// <summary>An area entered or left edit mode (the tray menu changes, FR-03).</summary>
        void ModeChanged(AreaController _area);
    }
}
