namespace Sws2Nightvision.Services;

/// <summary>
/// Per-player night vision preference.
/// </summary>
public sealed class PlayerPreference
{
    /// <summary>Whether the player wants night vision enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Last selected exposure intensity.</summary>
    public float Intensity { get; set; }
}
