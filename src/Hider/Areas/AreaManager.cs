using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Persistence;
using Hider.Platform;

namespace Hider.Areas
{
    /// <summary>
    /// All areas: create, restore, delete, edit all, and saving to areas.json (FR-02, FR-05, FR-06, FR-10, FR-61).
    /// Runs on the UI thread; the debounce timer posts back through <paramref name="_uiPost"/>.
    /// </summary>
    public sealed class AreaManager : IAreaOwner, IDisposable
    {
        public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

        private readonly List<AreaController> areas = new();
        private readonly string areasPath;
        private ITimer? saveTimer;
        private bool saveBlocked;
        private StripArea[] stripAreas = Array.Empty<StripArea>();
        private PhysicalRect stripScreen;
        private IReadOnlyList<ScreenInfo> stripScreens = Array.Empty<ScreenInfo>();

        public AreaManager(AreaServices _services)
        {
            Services = _services;
            areasPath = _services.AreasPath;
        }

        public AreaServices Services { get; }
        public IScreenService Screens => Services.Screens;
        public IReadOnlyList<AreaController> Areas => areas;
        public bool AnyEditing => areas.Any(a => a.Mode == AreaMode.Editing);

        /// <summary>Number of saves written, for tests.</summary>
        public int SaveCount { get; private set; }

        /// <summary>Area count or edit state changed (tray menu and tooltip, FR-03, FR-07).</summary>
        public event Action? Changed;

        /// <summary>Restores saved areas in hidden mode (FR-02), moving rectangles that are off every screen (FR-63).</summary>
        public LoadStatus Restore()
        {
            var state = AreasFile.Load(areasPath, out var status);
            Log.Verbose($"areas.json: {status}, {state.Areas.Count} area(s)");
            // A file that exists but cannot be read (locked by a backup or sync tool, no permission) must not be replaced by
            // this session's empty state: nothing is saved until the next start.
            saveBlocked = status == LoadStatus.Unreadable;
            var screens = Screens.Screens;
            var bounds = screens.Select(s => s.Bounds).ToArray();
            int primary = Screens.PrimaryIndex();
            foreach (var stored in state.Areas)
            {
                double scale = Screens.ScreenAt(stored.X, stored.Y)?.Scale ?? 1.0;
                var area = stored.ToArea(scale);
                area.Rect = AreaGeometry.EnsureMinSize(Placement.Fit(area.Rect, bounds, primary), scale);
                var controller = CreateController(area);
                controller.Show();
            }
            Changed?.Invoke();
            return status;
        }

        /// <summary>New area: 400 x 240 DIP at the centre of the cursor's monitor, mosaic / pixelate, edit mode (FR-14).</summary>
        public AreaController AddNew()
        {
            var screen = Screens.CursorScreen is { } cursor
                ? Screens.Screens.FirstOrDefault(s => s.Bounds == cursor) ?? Screens.Primary()
                : Screens.Primary();
            var workArea = screen?.WorkingArea ?? new PhysicalRect(0, 0, 1920, 1080);
            double scale = screen?.Scale ?? 1.0;
            var area = Area.CreateNew(AreaId.New(id => areas.Any(a => a.Model.Id == id)), Placement.NewArea(workArea, scale), Services.IsWindows);
            AreasFile.FillDefaults(area.Mosaic.Params, MethodKind.Mosaic);
            AreasFile.FillDefaults(area.Generated.Params, MethodKind.Generated);
            var controller = CreateController(area);
            controller.Show();
            controller.EnterEdit();
            SaveNow();
            Changed?.Invoke();
            return controller;
        }

        public void EditAll()
        {
            foreach (var area in areas)
            {
                area.EnterEdit();
            }
        }

        public void EndEditAll()
        {
            foreach (var area in areas.ToArray())
            {
                area.ExitEdit();
            }
        }

        /// <summary>Removes every area (after the confirmation dialog, FR-05) and saves.</summary>
        public void DeleteAll()
        {
            foreach (var area in areas)
            {
                area.Dispose();
            }
            areas.Clear();
            SaveNow();
            Changed?.Invoke();
        }

        public void Delete(AreaController _area)
        {
            if (areas.Remove(_area))
            {
                _area.Dispose();
                SaveNow();
                Changed?.Invoke();
            }
        }

        public void ModeChanged(AreaController _area)
        {
            Changed?.Invoke();
        }

        /// <summary>Native handles of every Hider window, left out of captures (spec §6.1).</summary>
        public IReadOnlyList<nint> OwnWindows => areas.Select(a => a.Host.Handle).Where(h => h != 0).ToArray();

        /// <summary>Areas as the Linux strip capture sees them: refreshed on the UI thread whenever a capture starts.</summary>
        public IReadOnlyList<StripArea> StripAreas => Volatile.Read(ref stripAreas);

        /// <summary>The virtual screen (union of all monitors, physical px) at the start of the latest capture.</summary>
        public PhysicalRect StripScreen
        {
            get
            {
                lock (areas)
                {
                    return stripScreen;
                }
            }
        }

        /// <summary>The monitors at the start of the latest capture.</summary>
        public IReadOnlyList<ScreenInfo> StripScreens
        {
            get
            {
                lock (areas)
                {
                    return stripScreens;
                }
            }
        }

        public void CaptureStarting()
        {
            if (Services.IsWindows)
            {
                return; // only the strip capture (Linux) reads these
            }
            // XWAYLAND-ASSUMPTION: area rectangles and the monitor union are absolute X11 coordinates, and a portal frame covers
            // that union (StripCoordinator maps between them by scale and offset).
            Volatile.Write(ref stripAreas, areas.Select(a => new StripArea(a.Model.Id, a.Model.Rect, a.Model.Pattern)).ToArray());
            var monitors = Screens.Screens.ToArray();
            var bounds = monitors.Select(s => s.Bounds).ToList();
            var union = bounds.Count == 0 ? default : PhysicalRect.FromEdges(bounds.Min(b => b.X), bounds.Min(b => b.Y), bounds.Max(b => b.Right), bounds.Max(b => b.Bottom));
            lock (areas)
            {
                stripScreen = union;
                stripScreens = monitors;
            }
        }

        /// <summary>areas.json could not be read at start, so this session does not write it.</summary>
        public bool SaveBlocked => saveBlocked;

        public void SaveNow()
        {
            saveTimer?.Dispose();
            saveTimer = null;
            if (saveBlocked)
            {
                Log.Verbose("not saving: areas.json could not be read at start and is left untouched");
                return;
            }
            var state = new AppState { Areas = areas.Select(a => AreaState.FromArea(a.Model, a.Host.Scale)).ToList() };
            try
            {
                AreasFile.Save(areasPath, state);
                SaveCount++;
                Log.Verbose($"saved {state.Areas.Count} area(s)");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Keep running; the next change or exit tries again.
                Log.Verbose($"save failed: {e.Message}");
            }
        }

        public void SaveSoon()
        {
            saveTimer?.Dispose();
            saveTimer = Services.Time.CreateTimer(_ => Services.UiPost(SaveNow), null, SaveDebounce, Timeout.InfiniteTimeSpan);
        }

        private AreaController CreateController(Area _area)
        {
            var controller = new AreaController(_area, this, Services.HostFactory);
            areas.Add(controller);
            return controller;
        }

        /// <summary>Saves (FR-06) and closes every window.</summary>
        public void Dispose()
        {
            saveTimer?.Dispose();
            SaveNow();
            foreach (var area in areas)
            {
                area.Dispose();
            }
            areas.Clear();
        }
    }
}
