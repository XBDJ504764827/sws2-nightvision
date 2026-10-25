namespace Sws2Nightvision.Configuration;

/// <summary>
/// Plugin configuration bound from the "sws2-nightvision" section of config.jsonc.
/// </summary>
public sealed class NightvisionConfig
{
    /// <summary>Enable debug logging.</summary>
    public bool Debug { get; set; }

    /// <summary>Gameplay and preference settings.</summary>
    public NightvisionBehavior Behavior { get; set; } = new();

    /// <summary>Post-processing appearance settings.</summary>
    public NightvisionEffect Effect { get; set; } = new();
}

/// <summary>
/// Gameplay, command and preference settings.
/// </summary>
public sealed class NightvisionBehavior
{
    /// <summary>Whether night vision is available at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Intensity used for players without a saved preference.</summary>
    public float DefaultIntensity { get; set; } = 3.0f;

    /// <summary>Lowest intensity a player may select.</summary>
    public float MinIntensity { get; set; } = 0.5f;

    /// <summary>Highest intensity a player may select.</summary>
    public float MaxIntensity { get; set; } = 10.0f;

    /// <summary>Whether players may change their intensity with !nvi.</summary>
    public bool AllowIntensityCommand { get; set; } = true;

    /// <summary>Optional permission required for !nvi. Empty means everyone.</summary>
    public string IntensityPermission { get; set; } = string.Empty;

    /// <summary>Whether to send a chat message when toggling night vision.</summary>
    public bool NotifyOnToggle { get; set; } = true;

    /// <summary>Whether death should turn night vision off and clear the saved preference.</summary>
    public bool TurnOffOnDeath { get; set; }

    /// <summary>Whether a team change should turn night vision off and clear the saved preference.</summary>
    public bool TurnOffOnTeamSwitch { get; set; } = true;

    /// <summary>Whether spectators are prevented from using night vision.</summary>
    public bool DisableInSpectator { get; set; } = true;

    /// <summary>Whether per-player preferences are saved to preferences.json.</summary>
    public bool SavePreferences { get; set; } = true;

    /// <summary>Whether a saved enabled preference is restored when a player spawns.</summary>
    public bool RestorePreferencesOnSpawn { get; set; } = true;
}

/// <summary>
/// Post-processing volume appearance settings.
/// </summary>
public sealed class NightvisionEffect
{
    /// <summary>Fade duration in seconds when the volume is applied.</summary>
    public float FadeDuration { get; set; }

    /// <summary>Additional exposure compensation applied on top of the intensity.</summary>
    public float ExposureCompensation { get; set; }
}
