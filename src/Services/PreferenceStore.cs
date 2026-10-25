using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace Sws2Nightvision.Services;

/// <summary>
/// JSON-backed store for per-player preferences. All callers are expected to run on
/// the game thread; an internal lock keeps the store safe against accidental
/// concurrent access from configuration callbacks.
/// </summary>
public sealed class PreferenceStore
{
    private sealed class PreferenceDocument
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, PlayerPreference?> Players { get; set; } = new(StringComparer.Ordinal);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly ISwiftlyCore _core;
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly Dictionary<ulong, PlayerPreference> _players = new();

    private bool _persistenceEnabled;
    private bool _dirty;

    public PreferenceStore(ISwiftlyCore core, string filePath, bool persistenceEnabled)
    {
        _core = core;
        _filePath = filePath;
        _persistenceEnabled = persistenceEnabled;
    }

    /// <summary>
    /// Enables or disables writing preferences to disk. Enabling the store writes
    /// the current in-memory preferences immediately.
    /// </summary>
    public void SetPersistenceEnabled(bool enabled)
    {
        lock (_lock)
        {
            if (_persistenceEnabled == enabled)
            {
                return;
            }

            _persistenceEnabled = enabled;

            if (enabled)
            {
                _dirty = true;
                SaveLocked();
            }
        }
    }

    /// <summary>
    /// Loads preferences from disk. A missing or invalid file is treated as empty.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            if (!_persistenceEnabled || !File.Exists(_filePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var document = JsonSerializer.Deserialize<PreferenceDocument>(json, JsonOptions);

                if (document?.Players is null)
                {
                    return;
                }

                foreach (var (key, preference) in document.Players)
                {
                    if (preference is not null
                        && ulong.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId))
                    {
                        _players[steamId] = preference;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                _core.Logger.LogWarning(exception, "[sws2-nightvision] Failed to load preferences from {Path}", _filePath);
            }
        }
    }

    /// <summary>
    /// Gets the preference for a SteamID, creating a default entry if necessary.
    /// </summary>
    public PlayerPreference GetOrCreate(ulong steamId, float defaultIntensity)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(steamId, out var preference))
            {
                preference = new PlayerPreference { Intensity = defaultIntensity };
                _players[steamId] = preference;
                return preference;
            }

            if (!float.IsFinite(preference.Intensity) || preference.Intensity <= 0f)
            {
                preference.Intensity = defaultIntensity;
            }

            return preference;
        }
    }

    /// <summary>
    /// Marks preferences as changed and writes them to disk if persistence is enabled.
    /// </summary>
    public void MarkDirtyAndSave()
    {
        lock (_lock)
        {
            _dirty = true;
            SaveLocked();
        }
    }

    /// <summary>
    /// Writes pending changes to disk.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        if (!_persistenceEnabled || !_dirty)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new PreferenceDocument
            {
                Players = _players.ToDictionary(
                    pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                    pair => (PlayerPreference?)pair.Value,
                    StringComparer.Ordinal)
            };

            var json = JsonSerializer.Serialize(document, JsonOptions);
            var temporaryPath = _filePath + ".tmp";

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _core.Logger.LogWarning(exception, "[sws2-nightvision] Failed to save preferences to {Path}", _filePath);
        }
    }
}
