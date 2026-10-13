namespace DeskBox.Models;

/// <summary>
/// Music widget presentation preferences.
/// </summary>
public sealed class MusicSettingsSlice
{
    /// <summary>Whether the Music widget uses album artwork color as a soft backdrop.</summary>
    public bool MusicUseArtworkBackdrop { get; set; } = true;

    /// <summary>Whether the Music widget album cover reacts lightly to pointer hover.</summary>
    public bool MusicEnableCoverHoverMotion { get; set; } = true;

    /// <summary>
    /// Music widget layout mode. Valid values: <c>"Auto"</c>, <c>"Cover"</c>, <c>"Controls"</c>.
    /// </summary>
    public string MusicDisplayMode { get; set; } = "Auto";

    /// <summary>
    /// Whether the progress bar and time labels collapse when the player
    /// publishes no timeline (position and duration) through SMTC.
    /// </summary>
    public bool MusicHideTimelineWhenUnavailable { get; set; } = true;
}
