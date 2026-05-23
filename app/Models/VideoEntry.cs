namespace VideoAutomation.Models;

/// <summary>
/// Represents a single video entry parsed from prompts.md.
/// </summary>
public class VideoEntry
{
    public string Title { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Workflow { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "16:9";
    public DateTimeOffset Schedule { get; set; }
    public VideoStatus Status { get; set; } = VideoStatus.Pending;
    public string Prompt { get; set; } = string.Empty;
    public string? YouTubeId { get; set; }
    public DateTimeOffset? PostedAt { get; set; }

    /// <summary>
    /// The raw text of this VIDEO block as it appears in the markdown file,
    /// used for in-place replacement when updating status.
    /// </summary>
    public string RawBlock { get; set; } = string.Empty;

    /// <summary>
    /// Start character index of this block in the file.
    /// </summary>
    public int BlockStartIndex { get; set; }

    /// <summary>
    /// Length of the raw block in the file.
    /// </summary>
    public int BlockLength { get; set; }
}

public enum VideoStatus
{
    Pending,
    Generating,
    Uploading,
    Posted,
    Failed
}
