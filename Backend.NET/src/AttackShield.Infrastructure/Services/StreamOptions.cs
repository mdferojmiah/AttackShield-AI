namespace AttackShield.Infrastructure.Services;

/// <summary>Bound from the "Stream" configuration section.</summary>
public sealed class StreamOptions
{
    public const string SectionName = "Stream";

    /// <summary>Root directory (relative to content root or absolute) for HLS output.</summary>
    public string StreamsRoot { get; set; } = "wwwroot/streams";

    /// <summary>Explicit ffmpeg path; when empty the manager auto-resolves it.</summary>
    public string FfmpegPath { get; set; } = string.Empty;
}
