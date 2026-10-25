using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SchemaDefinitions;
using Sws2Nightvision.Configuration;

namespace Sws2Nightvision.Services;

/// <summary>
/// Wraps a dynamically created <c>post_processing_volume</c> entity that applies
/// a night vision exposure boost to a single player.
/// </summary>
public sealed class NightvisionEntity : IDisposable
{
    private const float ExposureAdaptationSpeed = 10f;
    private const float NormalExposure = 1f;

    private readonly ISwiftlyCore _core;
    private readonly int _playerId;

    private CPostProcessingVolume? _volume;
    private bool _spawned;
    private bool _disposed;

    public NightvisionEntity(ISwiftlyCore core, int playerId)
    {
        _core = core;
        _playerId = playerId;
    }

    /// <summary>Whether the underlying entity is still valid.</summary>
    public bool IsActive => !_disposed && _volume is { IsValid: true };

    /// <summary>Whether the volume is attached but currently using the map's normal exposure.</summary>
    public bool IsIdle { get; private set; }

    /// <summary>
    /// Creates and spawns the post-processing volume, then limits transmission to the owner.
    /// </summary>
    public bool Create(NightvisionConfig config, float intensity)
    {
        if (_disposed || IsActive)
        {
            return IsActive;
        }

        try
        {
            _volume = _core.EntitySystem.CreateEntity<CPostProcessingVolume>();
            if (_volume is null || !_volume.IsValid)
            {
                _core.Logger.LogWarning(
                    "[sws2-nightvision] Failed to create post_processing_volume for player {PlayerId}",
                    _playerId);
                return false;
            }

            ApplyEffect(config);
            ApplyIntensity(intensity);

            _volume.DispatchSpawn();
            _spawned = true;

            // The owner is never blocked; make this explicit in case another
            // plugin or an earlier instance changed the transmit bits.
            _volume.SetTransmitState(true, _playerId);

            // The volume is created dynamically, so make sure the client-side
            // camera services receive the volumes list entry as well.
            ApplyEffectNetworkState();
            AttachToOwner();
            BlockForOtherPlayers();

            if (config.Debug)
            {
                _core.Logger.LogDebug(
                    "[sws2-nightvision] Created post_processing_volume for player {PlayerId}: {Diagnostics}",
                    _playerId,
                    GetDiagnostics());
            }

            return true;
        }
        catch (Exception exception)
        {
            _core.Logger.LogError(
                exception,
                "[sws2-nightvision] Error creating post_processing_volume for player {PlayerId}",
                _playerId);
            Dispose();
            return false;
        }
    }

    /// <summary>
    /// Updates the exposure intensity while the volume is active.
    /// </summary>
    public void SetIntensity(float intensity)
    {
        if (!IsActive)
        {
            return;
        }

        IsIdle = false;
        ApplyIntensity(intensity);

        if (_spawned)
        {
            _volume!.Master = true;
            _volume.ExposureControl = true;
            _volume.MasterUpdated();
            _volume.ExposureControlUpdated();
            _volume.MinExposureUpdated();
            _volume.MaxExposureUpdated();
        }
    }

    /// <summary>
    /// Applies changed appearance settings to an existing volume.
    /// </summary>
    public void ApplyConfig(NightvisionConfig config)
    {
        if (!IsActive)
        {
            return;
        }

        ApplyEffect(config);

        if (_spawned)
        {
            ApplyEffectNetworkState();
        }

        AttachToOwner();
    }

    /// <summary>
    /// Keeps the volume alive but switches it to the map's normal exposure.
    /// This avoids a hard volume removal/creation cycle (and its exposure flash)
    /// when the player toggles night vision off and on.
    /// </summary>
    public void SetIdle()
    {
        if (!IsActive)
        {
            return;
        }

        IsIdle = true;

        var (found, min, max) = GetMapExposureRange();

        try
        {
            // If the map has its own exposure range, mirror it so the player
            // sees the same brightness as before night vision was enabled. If
            // not, make the volume inert instead of forcing an arbitrary level.
            _volume!.Master = found;
            _volume.ExposureControl = found;
            _volume.MinExposure = found ? min : NormalExposure;
            _volume.MaxExposure = found ? max : NormalExposure;
            _volume.ExposureCompensation = 0f;
            _volume.ExposureFadeSpeedUp = ExposureAdaptationSpeed;
            _volume.ExposureFadeSpeedDown = ExposureAdaptationSpeed;
            _volume.TonemapEVSmoothingRange = 0f;
            _volume.MasterUpdated();
            _volume.ExposureControlUpdated();
            _volume.MinExposureUpdated();
            _volume.MaxExposureUpdated();
            _volume.ExposureCompensationUpdated();
            _volume.ExposureFadeSpeedUpUpdated();
            _volume.ExposureFadeSpeedDownUpdated();
            _volume.TonemapEVSmoothingRangeUpdated();
        }
        catch (Exception exception)
        {
            _core.Logger.LogDebug(
                exception,
                "[sws2-nightvision] Failed to put volume into idle mode for player {PlayerId}",
                _playerId);
        }

        if (_core.Logger.IsEnabled(LogLevel.Debug))
        {
            _core.Logger.LogDebug(
                "[sws2-nightvision] Night vision idle for player {PlayerId}: found={Found} exposure={Min:0.00}-{Max:0.00}",
                _playerId,
                found,
                min,
                max);
        }
    }

    private (bool Found, float Min, float Max) GetMapExposureRange()
    {
        try
        {
            var player = _core.PlayerManager.GetPlayer(_playerId);
            var camera = player?.PlayerPawn?.CameraServices;
            if (camera is not null && _volume is not null)
            {
                ref var volumes = ref camera.PostProcessingVolumes;

                for (var index = 0; index < volumes.Count; index++)
                {
                    var other = volumes[index].Value;
                    if (other is null
                        || other.Index == _volume.Index
                        || !other.ExposureControl)
                    {
                        continue;
                    }

                    if (IsSaneExposure(other.MinExposure, other.MaxExposure))
                    {
                        return (true, other.MinExposure, other.MaxExposure);
                    }
                }

                var tonemap = camera.TonemapController.Value;
                var min = tonemap?.AutoExposureMin ?? 0f;
                var max = tonemap?.AutoExposureMax ?? 0f;
                if (IsSaneExposure(min, max))
                {
                    return (true, min, max);
                }
            }
        }
        catch
        {
            // Fall through to inert mode.
        }

        return (false, NormalExposure, NormalExposure);
    }

    private static bool IsSaneExposure(float min, float max)
    {
        return float.IsFinite(min)
            && float.IsFinite(max)
            && min >= 0.01f
            && max >= min
            && max <= 64f;
    }

    /// <summary>
    /// Adds this volume to the owning player's camera services list if needed.
    /// The engine can rebuild this list, so this is safe to call repeatedly.
    /// </summary>
    public void AttachToOwner()
    {
        if (!IsActive)
        {
            return;
        }

        try
        {
            var player = _core.PlayerManager.GetPlayer(_playerId);
            var camera = player?.PlayerPawn?.CameraServices;
            if (camera is null || _volume is null)
            {
                return;
            }

            var handle = _core.EntitySystem.GetRefEHandle(_volume);
            ref var volumes = ref camera.PostProcessingVolumes;

            if (volumes.Find(handle) >= 0)
            {
                return;
            }

            volumes.AddToTail(handle);
            camera.PostProcessingVolumesUpdated();
        }
        catch (Exception exception)
        {
            _core.Logger.LogDebug(
                exception,
                "[sws2-nightvision] Failed to attach volume to player {PlayerId}",
                _playerId);
        }
    }

    /// <summary>
    /// Blocks this volume for a player other than its owner.
    /// </summary>
    public void BlockFor(int playerId)
    {
        if (!IsActive || playerId == _playerId)
        {
            return;
        }

        try
        {
            _volume!.SetTransmitState(false, playerId);
        }
        catch (Exception exception)
        {
            _core.Logger.LogDebug(
                exception,
                "[sws2-nightvision] Failed to block volume for player {PlayerId}",
                playerId);
        }
    }

    /// <summary>
    /// Short diagnostic string for the <c>!nvdebug</c> command.
    /// </summary>
    public string GetDiagnostics()
    {
        if (!IsActive || _volume is null)
        {
            return "volume=inactive";
        }

        var transmitting = _volume.IsTransmitting(_playerId);
        var attached = false;

        try
        {
            var player = _core.PlayerManager.GetPlayer(_playerId);
            var camera = player?.PlayerPawn?.CameraServices;
            if (camera is not null)
            {
                var handle = _core.EntitySystem.GetRefEHandle(_volume);
                attached = camera.PostProcessingVolumes.Find(handle) >= 0;
            }
        }
        catch
        {
            // Diagnostics must never throw.
        }

        return $"index={_volume.Index} ownerTransmit={transmitting} attached={attached} idle={IsIdle} "
            + $"master={_volume.Master} exposureControl={_volume.ExposureControl} "
            + $"min={_volume.MinExposure:0.00} max={_volume.MaxExposure:0.00}";
    }

    private void ApplyEffect(NightvisionConfig config)
    {
        if (_volume is null)
        {
            return;
        }

        var effect = config.Effect;

        // A master, exposure-controlled post-processing volume overrides the
        // player's camera exposure. This is the same technique used by CS2
        // night vision implementations.
        _volume.Master = true;
        _volume.ExposureControl = true;
        _volume.FadeDuration = Math.Max(0f, effect.FadeDuration);
        _volume.ExposureCompensation = effect.ExposureCompensation;
        _volume.ExposureFadeSpeedUp = ExposureAdaptationSpeed;
        _volume.ExposureFadeSpeedDown = ExposureAdaptationSpeed;
        _volume.TonemapEVSmoothingRange = 0f;
    }

    private void ApplyIntensity(float intensity)
    {
        if (_volume is null)
        {
            return;
        }

        var value = Math.Clamp(intensity, 0.1f, 32f);
        _volume.MinExposure = value;
        _volume.MaxExposure = value;
    }

    private void ApplyEffectNetworkState()
    {
        if (_volume is null)
        {
            return;
        }

        _volume.MasterUpdated();
        _volume.ExposureControlUpdated();
        _volume.FadeDurationUpdated();
        _volume.ExposureCompensationUpdated();
        _volume.ExposureFadeSpeedUpUpdated();
        _volume.ExposureFadeSpeedDownUpdated();
        _volume.TonemapEVSmoothingRangeUpdated();
        _volume.MinExposureUpdated();
        _volume.MaxExposureUpdated();
    }

    private void BlockForOtherPlayers()
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player.PlayerID != _playerId && !player.IsFakeClient)
            {
                BlockFor(player.PlayerID);
            }
        }
    }

    private void DetachFromOwner()
    {
        if (_volume is null)
        {
            return;
        }

        try
        {
            var player = _core.PlayerManager.GetPlayer(_playerId);
            var camera = player?.PlayerPawn?.CameraServices;
            if (camera is null)
            {
                return;
            }

            var handle = _core.EntitySystem.GetRefEHandle(_volume);
            ref var volumes = ref camera.PostProcessingVolumes;

            if (volumes.FindAndFastRemove(handle))
            {
                camera.PostProcessingVolumesUpdated();
            }
        }
        catch (Exception exception)
        {
            _core.Logger.LogDebug(
                exception,
                "[sws2-nightvision] Failed to detach volume from player {PlayerId}",
                _playerId);
        }
    }

    /// <summary>
    /// Destroys the post-processing volume.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DetachFromOwner();

        _disposed = true;
        IsIdle = false;
        _spawned = false;

        try
        {
            if (_volume is { IsValid: true })
            {
                _volume.Despawn();
            }
        }
        catch (Exception exception)
        {
            _core.Logger.LogDebug(
                exception,
                "[sws2-nightvision] Error despawning post_processing_volume for player {PlayerId}",
                _playerId);
        }
        finally
        {
            _volume = null;
        }
    }
}
