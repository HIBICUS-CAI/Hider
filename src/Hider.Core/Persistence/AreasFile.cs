using System.Text.Json;
using Hider.Core.Model;
using Hider.Core.Mosaic;

namespace Hider.Core.Persistence
{
    public enum LoadStatus
    {
        Loaded,
        Missing,
        Broken,

        /// <summary>The file exists but could not be read (locked by another program, no permission); it is left as it is.</summary>
        Unreadable,
    }

    /// <summary>Reads and writes areas.json (FR-60 .. FR-65).</summary>
    public static class AreasFile
    {
        /// <summary>
        /// Loads the state. A missing file gives an empty state (<see cref="LoadStatus.Missing"/>). A file that is not valid
        /// version 1 JSON is renamed to &lt;path&gt;.broken (an older .broken is kept under a time-stamped name) and an empty
        /// state is returned (<see cref="LoadStatus.Broken"/>, FR-64). A file that cannot be read or renamed at all gives an
        /// empty state with <see cref="LoadStatus.Unreadable"/>: the caller must not save over it in this session.
        /// Missing values are filled with defaults and invalid ones replaced (see <see cref="Normalize"/>).
        /// </summary>
        public static AppState Load(string _path, out LoadStatus _status)
        {
            if (!File.Exists(_path))
            {
                _status = LoadStatus.Missing;
                return new AppState();
            }
            string text;
            try
            {
                text = File.ReadAllText(_path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Verbose($"areas.json could not be read: {e.Message}");
                _status = LoadStatus.Unreadable;
                return new AppState();
            }
            AppState? state = null;
            try
            {
                state = JsonSerializer.Deserialize(text, HiderJsonContext.Default.AppState);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
            {
                state = null;
            }
            // A null element deserialises fine (nullable annotations are not checked inside collections) but is no area.
            if (state is null || state.Version != AppState.CurrentVersion || state.Areas is null || state.Areas.Contains(null!))
            {
                _status = MoveAside(_path) ? LoadStatus.Broken : LoadStatus.Unreadable;
                return new AppState();
            }
            Normalize(state);
            _status = LoadStatus.Loaded;
            return state;
        }

        /// <summary>Writes &lt;path&gt;.tmp and renames it over the file (FR-62). The existing file is untouched on failure.</summary>
        public static void Save(string _path, AppState _state)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            string tmp = _path + ".tmp";
            try
            {
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(_state, HiderJsonContext.Default.AppState);
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(json);
                    stream.Flush(true);
                }
                File.Move(tmp, _path, overwrite: true);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        public static string ToJson(AppState _state)
        {
            return JsonSerializer.Serialize(_state, HiderJsonContext.Default.AppState);
        }

        public static AppState? FromJson(string _json)
        {
            return JsonSerializer.Deserialize(_json, HiderJsonContext.Default.AppState);
        }

        /// <summary>
        /// Repairs a loaded state: duplicate or malformed ids are replaced, colors that are not "#RRGGBB" fall back to the
        /// default, opacity is clamped, sizes are clamped to [min, max], an unknown pattern becomes mosaic, and every registered method gets all of its
        /// parameters (missing ones = default). Parameters of unknown method ids are kept as they are (§3.4).
        /// </summary>
        public static void Normalize(AppState _state)
        {
            var ids = new HashSet<string>();
            foreach (var area in _state.Areas)
            {
                if (!AreaId.IsValid(area.Id) || !ids.Add(area.Id))
                {
                    area.Id = AreaId.New(ids.Contains);
                    ids.Add(area.Id);
                }
                area.Opacity = double.IsNaN(area.Opacity) ? 1.0 : Math.Clamp(area.Opacity, 0.0, 1.0);
                area.Width = double.IsFinite(area.Width) ? Math.Clamp(area.Width, AreaDefaults.MinSizeDip, AreaDefaults.MaxSizeDip) : AreaDefaults.NewAreaWidthDip;
                area.Height = double.IsFinite(area.Height) ? Math.Clamp(area.Height, AreaDefaults.MinSizeDip, AreaDefaults.MaxSizeDip) : AreaDefaults.NewAreaHeightDip;
                if (!Enum.IsDefined(area.Pattern))
                {
                    area.Pattern = PatternKind.Mosaic; // the converter also takes integers ("pattern": 7)
                }
                area.Fill ??= new FillSettings();
                area.Fill.Color = ColorText.OrDefault(area.Fill.Color);
                area.Mosaic ??= new MethodSettings<MosaicCommon> { Method = AreaDefaults.MosaicMethod };
                area.Generated ??= new MethodSettings<GeneratedCommon> { Method = AreaDefaults.GeneratedMethod };
                area.Mosaic.Common ??= new MosaicCommon();
                area.Mosaic.Params ??= new Dictionary<string, Dictionary<string, double>>();
                area.Generated.Common ??= new GeneratedCommon();
                area.Generated.Params ??= new Dictionary<string, Dictionary<string, double>>();
                area.Generated.Common.BaseColor = ColorText.OrDefault(area.Generated.Common.BaseColor);
                // Older files may hold seeds beyond the hash's range; the same pattern's seed is kept (the hash takes it mod 65536).
                area.Generated.Common.Seed = (int)(((long)area.Generated.Common.Seed % AreaDefaults.SeedCount + AreaDefaults.SeedCount) % AreaDefaults.SeedCount);
                if (string.IsNullOrEmpty(area.Mosaic.Method))
                {
                    area.Mosaic.Method = AreaDefaults.MosaicMethod;
                }
                if (string.IsNullOrEmpty(area.Generated.Method))
                {
                    area.Generated.Method = AreaDefaults.GeneratedMethod;
                }
                NormalizeMosaicCommon(area.Mosaic.Common);
                FillDefaults(area.Mosaic.Params, MethodKind.Mosaic);
                FillDefaults(area.Generated.Params, MethodKind.Generated);
            }
        }

        /// <summary>Adds every registered method of a kind with all its parameters; missing values become defaults.</summary>
        public static void FillDefaults(Dictionary<string, Dictionary<string, double>> _params, MethodKind _kind)
        {
            foreach (var method in MethodRegistry.OfKind(_kind))
            {
                if (!_params.TryGetValue(method.Id, out var values) || values is null)
                {
                    values = new Dictionary<string, double>();
                    _params[method.Id] = values;
                }
                foreach (var spec in method.Params)
                {
                    values[spec.Key] = values.TryGetValue(spec.Key, out double v) ? spec.Clamp(v) : spec.Default;
                }
            }
        }

        private static void NormalizeMosaicCommon(MosaicCommon _common)
        {
            foreach (var spec in CommonParams.Mosaic)
            {
                switch (spec.Key)
                {
                    case CommonParams.Blur:
                        _common.Blur = spec.Clamp(_common.Blur);
                        break;
                    case CommonParams.Levels:
                        _common.Levels = (int)spec.Clamp(_common.Levels);
                        break;
                    case CommonParams.IntervalMs:
                        _common.IntervalMs = (int)spec.Clamp(_common.IntervalMs);
                        break;
                    case CommonParams.StripPx:
                        _common.StripPx = (int)spec.Clamp(_common.StripPx);
                        break;
                }
            }
        }

        /// <summary>Renames the file to &lt;path&gt;.broken, keeping an older one as &lt;path&gt;.broken-&lt;time&gt;.</summary>
        private static bool MoveAside(string _path)
        {
            string broken = _path + ".broken";
            try
            {
                if (File.Exists(broken))
                {
                    File.Move(broken, $"{broken}-{File.GetLastWriteTime(broken):yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
                }
                File.Move(_path, broken);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Verbose($"areas.json could not be moved aside: {e.Message}");
                return false;
            }
        }

        private static void TryDelete(string _path)
        {
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
