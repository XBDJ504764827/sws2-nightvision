using System.Text;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Sws2Nightvision.Configuration;

namespace Sws2Nightvision.Services;

/// <summary>
/// Result of a night vision toggle request.
/// </summary>
public enum NightvisionToggleResult
{
    Enabled,
    Disabled,
    FeatureDisabled,
    SpectatorDisabled,
    Unavailable,
    Failed
}

/// <summary>
/// Current night vision state for a player.
/// </summary>
public readonly record struct NightvisionStatus(bool Enabled, bool Active, float Intensity);

/// <summary>
/// Owns per-player post-processing volumes and preference state.
/// </summary>
public sealed class NightvisionManager : IDisposable
{
    private readonly ISwiftlyCore _core;
    private readonly PreferenceStore _store;
    private readonly object _lock = new();

    private readonly Dictionary<int, NightvisionEntity> _active = new();
    private readonly Dictionary<int, PlayerPreference> _sessionPreferences = new();

    private NightvisionConfig _config;
    private float _minIntensity;
    private float _maxIntensity;
    private float _defaultIntensity;

    private CancellationTokenSource? _reattachTimer;

    public NightvisionManager(ISwiftlyCore core, NightvisionConfig config, PreferenceStore store)
    {
        _core = core;
        _config = config;
        _store = store;

        UpdateBounds();
        _store.SetPersistenceEnabled(config.Behavior.SavePreferences);
    }

    /// <summary>
    /// Starts the low-frequency maintenance timer. The engine may rebuild the
    /// player's post-processing volume list, so active entries are re-added.
    /// </summary>
    public void Start()
    {
        Stop();
        _reattachTimer = _core.Scheduler.RepeatBySeconds(1f, ReattachActive);
    }

    /// <summary>
    /// Stops the maintenance timer.
    /// </summary>
    public void Stop()
    {
        _reattachTimer?.Cancel();
        _reattachTimer?.Dispose();
        _reattachTimer = null;
    }

    /// <summary>Lowest selectable intensity after configuration normalization.</summary>
    public float MinIntensity => _minIntensity;

    /// <summary>Highest selectable intensity after configuration normalization.</summary>
    public float MaxIntensity => _maxIntensity;

    /// <summary>
    /// Toggles night vision for a player.
    /// </summary>
    public NightvisionToggleResult Toggle(IPlayer player)
    {
        lock (_lock)
        {
            if (!_config.Behavior.Enabled)
            {
                return NightvisionToggleResult.FeatureDisabled;
            }

            if (!player.IsValid)
            {
                return NightvisionToggleResult.Unavailable;
            }

            var preference = GetPreferenceLocked(player);
            return SetEnabledLocked(player, !preference.Enabled);
        }
    }

    /// <summary>
    /// Explicitly enables or disables night vision for a player.
    /// </summary>
    public NightvisionToggleResult SetEnabled(IPlayer player, bool enabled)
    {
        lock (_lock)
        {
            return SetEnabledLocked(player, enabled);
        }
    }

    private NightvisionToggleResult SetEnabledLocked(IPlayer player, bool enabled)
    {
        if (!_config.Behavior.Enabled)
        {
            return NightvisionToggleResult.FeatureDisabled;
        }

        if (!player.IsValid)
        {
            return NightvisionToggleResult.Unavailable;
        }

        var preference = GetPreferenceLocked(player);

        // Turning night vision off must always work, even while dead or
        // spectating. Keep the volume alive and switch it to the map's normal
        // exposure so there is no hard removal/creation flash.
        if (!enabled)
        {
            preference.Enabled = false;

            if (_active.TryGetValue(player.PlayerID, out var idleEntity))
            {
                idleEntity.SetIdle();
            }

            PersistLocked();
            return NightvisionToggleResult.Disabled;
        }

        var eligibility = GetEligibility(player);
        if (eligibility != NightvisionToggleResult.Enabled)
        {
            return eligibility;
        }

        var intensity = ClampIntensity(preference.Intensity);
        preference.Intensity = intensity;

        if (!CreateActiveLocked(player, intensity))
        {
            preference.Enabled = false;
            PersistLocked();
            return NightvisionToggleResult.Failed;
        }

        preference.Enabled = true;
        PersistLocked();
        return NightvisionToggleResult.Enabled;
    }

    /// <summary>
    /// Sets a player's intensity. The value is clamped to the configured range.
    /// </summary>
    public float SetIntensity(IPlayer player, float intensity)
    {
        lock (_lock)
        {
            var applied = ClampIntensity(intensity);
            var preference = GetPreferenceLocked(player);
            preference.Intensity = applied;

            if (preference.Enabled
                && _active.TryGetValue(player.PlayerID, out var entity))
            {
                entity.SetIntensity(applied);
            }

            PersistLocked();
            return applied;
        }
    }

    /// <summary>
    /// Returns the player's current intensity, clamped to the configured range.
    /// </summary>
    public float GetIntensity(IPlayer player)
    {
        lock (_lock)
        {
            var preference = GetPreferenceLocked(player);
            var clamped = ClampIntensity(preference.Intensity);

            if (!clamped.Equals(preference.Intensity))
            {
                preference.Intensity = clamped;
                PersistLocked();
            }

            return clamped;
        }
    }

    /// <summary>
    /// Returns the player's desired state, whether a volume is active, and the intensity.
    /// </summary>
    public NightvisionStatus GetStatus(IPlayer player)
    {
        lock (_lock)
        {
            var preference = GetPreferenceLocked(player);
            var clamped = ClampIntensity(preference.Intensity);

            if (!clamped.Equals(preference.Intensity))
            {
                preference.Intensity = clamped;
                PersistLocked();
            }

            var active = preference.Enabled
                && _active.TryGetValue(player.PlayerID, out var entity)
                && entity.IsActive;

            return new NightvisionStatus(
                preference.Enabled,
                active,
                clamped);
        }
    }

    /// <summary>
    /// Restores a saved preference after a player spawns. Safe to call repeatedly.
    /// </summary>
    public bool RestoreForPlayer(int playerId)
    {
        lock (_lock)
        {
            if (!_config.Behavior.Enabled || !_config.Behavior.RestorePreferencesOnSpawn)
            {
                return false;
            }

            var player = _core.PlayerManager.GetPlayer(playerId);
            if (player is null || GetEligibility(player) != NightvisionToggleResult.Enabled)
            {
                return false;
            }

            var preference = GetPreferenceLocked(player);
            if (!preference.Enabled)
            {
                return false;
            }

            var intensity = ClampIntensity(preference.Intensity);
            preference.Intensity = intensity;
            return CreateActiveLocked(player, intensity);
        }
    }

    /// <summary>
    /// Restores saved preferences for every valid player currently on the server.
    /// </summary>
    public void RestoreAll()
    {
        foreach (var player in _core.PlayerManager.GetAllValidPlayers().ToList())
        {
            RestoreForPlayer(player.PlayerID);
        }
    }

    /// <summary>
    /// Turns night vision off for a player, optionally clearing the saved preference.
    /// </summary>
    public void DisableForPlayer(int playerId, bool clearPreference, string reason)
    {
        lock (_lock)
        {
            if (_active.TryGetValue(playerId, out var entity))
            {
                entity.SetIdle();
            }

            if (!clearPreference)
            {
                return;
            }

            var player = _core.PlayerManager.GetPlayer(playerId);
            if (player is null)
            {
                return;
            }

            var preference = GetPreferenceLocked(player);
            preference.Enabled = false;
            PersistLocked();
            Debug("Night vision disabled for player {PlayerId} ({Reason})", playerId, reason);
        }
    }

    /// <summary>
    /// Removes all runtime state for a disconnecting player.
    /// </summary>
    public void CleanupPlayer(int playerId)
    {
        lock (_lock)
        {
            RemoveActiveLocked(playerId, "disconnect");
            _sessionPreferences.Remove(playerId);
        }
    }

    /// <summary>
    /// Removes all active volumes, keeping saved preferences.
    /// </summary>
    public void CleanupActive(string reason)
    {
        lock (_lock)
        {
            CleanupActiveLocked(reason);
        }
    }

    /// <summary>
    /// Blocks all active volumes for a player who just connected or spawned.
    /// </summary>
    public void BlockActiveForPlayer(int playerId)
    {
        lock (_lock)
        {
            foreach (var (ownerId, entity) in _active)
            {
                if (ownerId != playerId)
                {
                    entity.BlockFor(playerId);
                }
            }
        }
    }

    /// <summary>
    /// Re-adds active volumes to their owners' camera services list.
    /// </summary>
    private void ReattachActive()
    {
        lock (_lock)
        {
            foreach (var entity in _active.Values)
            {
                entity.AttachToOwner();
            }
        }
    }

    /// <summary>
    /// Returns a diagnostic string for the <c>!nvdebug</c> command.
    /// </summary>
    public string GetDiagnostics(IPlayer player)
    {
        lock (_lock)
        {
            var preference = GetPreferenceLocked(player);
            var hasEntity = _active.TryGetValue(player.PlayerID, out var entity);
            var active = preference.Enabled && hasEntity && entity!.IsActive;
            var camera = player.PlayerPawn?.CameraServices;
            var cameraVolumes = camera?.PostProcessingVolumes.Count ?? -1;

            var volumeDetails = "none";
            if (camera is not null)
            {
                try
                {
                    volumeDetails = DescribeVolumes(camera);
                }
                catch
                {
                    volumeDetails = "error";
                }
            }

            var postProcess = "unknown";
            try
            {
                postProcess = player.GetClientConvarValue("mat_postprocess_enable");
            }
            catch
            {
                // Client convar should never break diagnostics.
            }

            return $"player={player.PlayerID} valid={player.IsValid} fake={player.IsFakeClient} "
                + $"team={player.PlayerPawn?.Team} postProcess={postProcess} prefEnabled={preference.Enabled} "
                + $"intensity={ClampIntensity(preference.Intensity):0.00} active={active} "
                + $"cameraVolumes={cameraVolumes} volumes=[{volumeDetails}] "
                + $"entity={(hasEntity ? entity!.GetDiagnostics() : "none")}";
        }
    }

    /// <summary>
    /// Applies a new configuration at runtime.
    /// </summary>
    public void ApplyConfig(NightvisionConfig config)
    {
        lock (_lock)
        {
            var wasEnabled = _config.Behavior.Enabled;
            _config = config;

            UpdateBounds();
            _store.SetPersistenceEnabled(config.Behavior.SavePreferences);

            if (!config.Behavior.Enabled)
            {
                CleanupActiveLocked("feature_disabled");
                return;
            }

            foreach (var (playerId, entity) in _active.ToList())
            {
                var player = _core.PlayerManager.GetPlayer(playerId);
                if (!entity.IsActive
                    || player is null
                    || GetEligibility(player) != NightvisionToggleResult.Enabled)
                {
                    entity.Dispose();
                    _active.Remove(playerId);
                    continue;
                }

                var preference = GetPreferenceLocked(player);
                var intensity = ClampIntensity(preference.Intensity);
                preference.Intensity = intensity;

                entity.ApplyConfig(config);

                if (preference.Enabled)
                {
                    entity.SetIntensity(intensity);
                }
                else
                {
                    entity.SetIdle();
                }
            }

            PersistLocked();

            if (!wasEnabled)
            {
                RestoreAll();
            }

            Debug("Configuration applied (active volumes: {Count})", _active.Count);
        }
    }

    /// <summary>
    /// Disposes all active volumes and flushes preferences.
    /// </summary>
    public void Shutdown()
    {
        Stop();

        lock (_lock)
        {
            CleanupActiveLocked("plugin_unload");
            _store.Save();
        }
    }

    public void Dispose()
    {
        Shutdown();
    }

    private NightvisionToggleResult GetEligibility(IPlayer player)
    {
        if (!player.IsValid)
        {
            return NightvisionToggleResult.Unavailable;
        }

        if (player.IsFakeClient)
        {
            return NightvisionToggleResult.Unavailable;
        }

        if (_config.Behavior.DisableInSpectator && player.PlayerPawn is { Team: Team.Spectator })
        {
            return NightvisionToggleResult.SpectatorDisabled;
        }

        return NightvisionToggleResult.Enabled;
    }

    private PlayerPreference GetPreferenceLocked(IPlayer player)
    {
        if (_config.Behavior.SavePreferences && !player.IsFakeClient && player.SteamID != 0)
        {
            return _store.GetOrCreate(player.SteamID, _defaultIntensity);
        }

        if (!_sessionPreferences.TryGetValue(player.PlayerID, out var preference))
        {
            preference = new PlayerPreference { Intensity = _defaultIntensity };
            _sessionPreferences[player.PlayerID] = preference;
        }
        else if (!float.IsFinite(preference.Intensity) || preference.Intensity <= 0f)
        {
            preference.Intensity = _defaultIntensity;
        }

        return preference;
    }

    private bool CreateActiveLocked(IPlayer player, float intensity)
    {
        if (_active.TryGetValue(player.PlayerID, out var existing))
        {
            if (existing.IsActive)
            {
                existing.ApplyConfig(_config);
                existing.SetIntensity(ClampIntensity(intensity));
                return true;
            }

            _active.Remove(player.PlayerID);
            existing.Dispose();
        }

        if (GetEligibility(player) != NightvisionToggleResult.Enabled)
        {
            return false;
        }

        var entity = new NightvisionEntity(_core, player.PlayerID);
        if (!entity.Create(_config, ClampIntensity(intensity)))
        {
            entity.Dispose();
            return false;
        }

        _active[player.PlayerID] = entity;
        Debug("Night vision volume created for player {PlayerId}", player.PlayerID);
        return true;
    }

    private void RemoveActiveLocked(int playerId, string reason)
    {
        if (!_active.Remove(playerId, out var entity))
        {
            return;
        }

        entity.Dispose();
        Debug("Night vision volume removed for player {PlayerId} ({Reason})", playerId, reason);
    }

    private void CleanupActiveLocked(string reason)
    {
        foreach (var entity in _active.Values.ToList())
        {
            entity.Dispose();
        }

        _active.Clear();
        Debug("All night vision volumes removed ({Reason})", reason);
    }

    private void PersistLocked()
    {
        _store.MarkDirtyAndSave();
    }

    private void UpdateBounds()
    {
        _minIntensity = Sanitize(_config.Behavior.MinIntensity, 0.5f);
        _minIntensity = Math.Clamp(_minIntensity, 0.1f, 32f);

        _maxIntensity = Sanitize(_config.Behavior.MaxIntensity, 10f);
        _maxIntensity = Math.Clamp(_maxIntensity, _minIntensity, 32f);

        _defaultIntensity = Sanitize(_config.Behavior.DefaultIntensity, 1f);
        _defaultIntensity = Math.Clamp(_defaultIntensity, _minIntensity, _maxIntensity);
    }

    private float ClampIntensity(float intensity)
    {
        return float.IsFinite(intensity)
            ? Math.Clamp(intensity, _minIntensity, _maxIntensity)
            : _defaultIntensity;
    }

    private static float Sanitize(float value, float fallback)
    {
        return float.IsFinite(value) ? value : fallback;
    }

    private static string DescribeVolumes(CCSPlayer_CameraServices camera)
    {
        var builder = new StringBuilder();
        ref var volumes = ref camera.PostProcessingVolumes;

        for (var index = 0; index < volumes.Count; index++)
        {
            var volume = volumes[index].Value;
            if (volume is null)
            {
                builder.Append($"[{index}: invalid] ");
                continue;
            }

            builder.Append(
                $"[{index}: idx={volume.Index} master={volume.Master} exp={volume.ExposureControl} "
                + $"min={volume.MinExposure:0.00} max={volume.MaxExposure:0.00}] ");
        }

        return builder.ToString().TrimEnd();
    }

    private void Debug(string message, params object?[] args)
    {
        if (_config.Debug)
        {
            _core.Logger.LogDebug("[sws2-nightvision] " + message, args);
        }
    }
}
