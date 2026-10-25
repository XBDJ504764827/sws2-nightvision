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

        ApplyIntensity(intensity);

        if (_spawned)
        {
            _volume!.MinExposureUpdated();
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

        return $"index={_volume.Index} ownerTransmit={transmitting} attached={attached} "
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
