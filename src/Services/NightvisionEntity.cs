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

    /// <summary>The owning player slot.</summary>
    public int PlayerId => _playerId;

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

            NotifyAllUpdated();

            // Fail closed: if the volume cannot be restricted to its owner, it must be
            // destroyed so it cannot affect every player on the server.
            if (!ApplyTransmission())
            {
                _core.Logger.LogWarning(
                    "[sws2-nightvision] Failed to restrict post_processing_volume to player {PlayerId}",
                    _playerId);
                Dispose();
                return false;
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
            NotifyAllUpdated();
        }
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
        _volume.ExposureFadeSpeedUp = Math.Max(0f, effect.ExposureFadeSpeedUp);
        _volume.ExposureFadeSpeedDown = Math.Max(0f, effect.ExposureFadeSpeedDown);
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

    private void NotifyAllUpdated()
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
        _volume.MinExposureUpdated();
        _volume.MaxExposureUpdated();
    }

    private bool ApplyTransmission()
    {
        if (_volume is null)
        {
            return false;
        }

        // Block the entity for everyone, then allow only its owner. The volume
        // itself is a global master volume, so per-player transmission is what
        // makes the effect private.
        _volume.SetTransmitState(false);
        _volume.SetTransmitState(true, _playerId);

        return _volume.IsTransmitting(_playerId);
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
