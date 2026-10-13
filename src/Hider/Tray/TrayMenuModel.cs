using Hider.Core;

namespace Hider.Tray
{
    public enum TrayCommand
    {
        AddArea,
        EditAll,
        EndEdit,
        DeleteAll,
        Exit,
    }

    public sealed record TrayMenuItem(TrayCommand Command, string Label, bool IsEnabled, bool SeparatorBefore);

    /// <summary>
    /// The tray menu (FR-03, FR-04) as data, shared by the native menu, the Windows left-click popup and the D-9 fallback
    /// window so they always show the same items and enabled states.
    /// </summary>
    public static class TrayMenuModel
    {
        public static IReadOnlyList<TrayMenuItem> Build(int _areaCount, bool _anyEditing)
        {
            bool hasAreas = _areaCount > 0;
            return new[]
            {
                new TrayMenuItem(TrayCommand.AddArea, Strings.TrayAdd, true, false),
                _anyEditing
                    ? new TrayMenuItem(TrayCommand.EndEdit, Strings.TrayEndEdit, true, false)
                    : new TrayMenuItem(TrayCommand.EditAll, Strings.TrayEditAll, hasAreas, false),
                new TrayMenuItem(TrayCommand.DeleteAll, Strings.TrayDeleteAll, hasAreas, false),
                new TrayMenuItem(TrayCommand.Exit, Strings.TrayExit, true, true),
            };
        }
    }
}
