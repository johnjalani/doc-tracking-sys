using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoAutomation.Models;

namespace VideoAutomation.Services;

/// <summary>
/// Reads and writes prompts.md, parsing VIDEO blocks into VideoEntry objects
/// and supporting in-place status updates.
/// </summary>
public class PromptFileService
{
    private readonly PathSettings _paths;
    private readonly ILogger<PromptFileService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    // Regex to split on ## VIDEO headings (keeps the heading as part of the block)
    private static readonly Regex VideoBlockRegex = new(
        @"(?=^## VIDEO\b)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Regex to match key: value lines (e.g., "title: My Video")
    private static readonly Regex KeyValueRegex = new(
        @"^(?<key>[a-z_]+)\s*:\s*(?<value>.+)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to extract fenced code block content after "prompt:"
    private static readonly Regex PromptBlockRegex = new(
        @"prompt:\s*\r?\n```(?:txt|text)?\s*\r?\n(?<content>[\s\S]*?)```",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public PromptFileService(IOptions<PathSettings> paths, ILogger<PromptFileService> logger)
    {
        _paths = paths.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns all video entries from prompts.md.
    /// </summary>
    public async Task<List<VideoEntry>> GetAllEntriesAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var filePath = GetPromptsFilePath();
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Prompts file not found: {Path}", filePath);
                return new List<VideoEntry>();
            }

            var content = await File.ReadAllTextAsync(filePath, ct);
            return ParseEntries(content);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Returns pending entries whose schedule time has passed.
    /// </summary>
    public async Task<List<VideoEntry>> GetPendingEntriesAsync(CancellationToken ct = default)
    {
        var all = await GetAllEntriesAsync(ct);
        var now = DateTimeOffset.UtcNow;

        return all
            .Where(e => e.Status == VideoStatus.Pending && e.Schedule <= now)
            .OrderBy(e => e.Schedule)
            .ToList();
    }

    /// <summary>
    /// Updates the status of a specific video entry in prompts.md in-place.
    /// Optionally appends youtube_id and posted_at fields.
    /// </summary>
    public async Task UpdateEntryStatusAsync(
        VideoEntry entry,
        VideoStatus newStatus,
        string? youtubeId = null,
        CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var filePath = GetPromptsFilePath();
            var content = await File.ReadAllTextAsync(filePath, ct);

            // Find and replace the status line within this entry's raw block
            var oldStatusLine = $"status: {entry.Status.ToString().ToLowerInvariant()}";
            var newStatusLine = $"status: {newStatus.ToString().ToLowerInvariant()}";

            // Build the replacement block
            var updatedBlock = entry.RawBlock.Replace(oldStatusLine, newStatusLine);

            // Append youtube_id and posted_at if transitioning to posted
            if (newStatus == VideoStatus.Posted)
            {
                if (!string.IsNullOrEmpty(youtubeId) && !updatedBlock.Contains("youtube_id:"))
                {
                    var insertPoint = updatedBlock.IndexOf(newStatusLine) + newStatusLine.Length;
                    var extraFields = $"\nyoutube_id: {youtubeId}\nposted_at: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}";
                    updatedBlock = updatedBlock.Insert(insertPoint, extraFields);
                }
            }

            // Replace in the full content
            content = content.Replace(entry.RawBlock, updatedBlock);

            await File.WriteAllTextAsync(filePath, content, ct);

            // Update the entry object to reflect the change
            entry.Status = newStatus;
            entry.RawBlock = updatedBlock;
            if (!string.IsNullOrEmpty(youtubeId))
                entry.YouTubeId = youtubeId;

            _logger.LogInformation(
                "Updated entry '{Title}' status: {OldStatus} → {NewStatus}",
                entry.Title, oldStatusLine, newStatusLine);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// On startup, resets entries stuck in generating/uploading back to pending.
    /// </summary>
    public async Task ResetStuckEntriesAsync(CancellationToken ct = default)
    {
        var all = await GetAllEntriesAsync(ct);
        foreach (var entry in all.Where(e =>
            e.Status == VideoStatus.Generating || e.Status == VideoStatus.Uploading))
        {
            _logger.LogWarning(
                "Resetting stuck entry '{Title}' from {Status} to pending",
                entry.Title, entry.Status);
            await UpdateEntryStatusAsync(entry, VideoStatus.Pending, ct: ct);
        }
    }

    private List<VideoEntry> ParseEntries(string content)
    {
        var entries = new List<VideoEntry>();
        var blocks = VideoBlockRegex.Split(content);

        var currentIndex = 0;
        foreach (var rawBlock in blocks)
        {
            // Find this block's position in the original content
            var blockStart = content.IndexOf(rawBlock, currentIndex);
            currentIndex = blockStart + rawBlock.Length;

            if (!rawBlock.TrimStart().StartsWith("## VIDEO"))
                continue;

            var entry = ParseSingleEntry(rawBlock, blockStart);
            if (entry != null)
                entries.Add(entry);
        }

        return entries;
    }

    private VideoEntry? ParseSingleEntry(string block, int startIndex)
    {
        var entry = new VideoEntry
        {
            RawBlock = block,
            BlockStartIndex = startIndex,
            BlockLength = block.Length
        };

        // Extract key-value pairs (ignore the prompt: line itself, handled separately)
        foreach (Match match in KeyValueRegex.Matches(block))
        {
            var key = match.Groups["key"].Value.Trim().ToLowerInvariant();
            var value = match.Groups["value"].Value.Trim();

            switch (key)
            {
                case "title":
                    entry.Title = value;
                    break;
                case "tags":
                    entry.Tags = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
                case "description":
                    entry.Description = value;
                    break;
                case "model":
                    entry.Model = value;
                    break;
                case "workflow":
                    entry.Workflow = value;
                    break;
                case "aspect_ratio":
                    entry.AspectRatio = value;
                    break;
                case "schedule":
                    if (DateTimeOffset.TryParse(value, out var schedule))
                        entry.Schedule = schedule;
                    break;
                case "status":
                    entry.Status = ParseStatus(value);
                    break;
                case "youtube_id":
                    entry.YouTubeId = value;
                    break;
                case "posted_at":
                    if (DateTimeOffset.TryParse(value, out var posted))
                        entry.PostedAt = posted;
                    break;
            }
        }

        // Extract prompt from fenced code block
        var promptMatch = PromptBlockRegex.Match(block);
        if (promptMatch.Success)
        {
            entry.Prompt = promptMatch.Groups["content"].Value.Trim();
        }

        if (string.IsNullOrWhiteSpace(entry.Title))
        {
            _logger.LogWarning("Skipping VIDEO block without a title");
            return null;
        }

        return entry;
    }

    private static VideoStatus ParseStatus(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "pending" => VideoStatus.Pending,
            "generating" => VideoStatus.Generating,
            "uploading" => VideoStatus.Uploading,
            "posted" => VideoStatus.Posted,
            "failed" => VideoStatus.Failed,
            _ => VideoStatus.Pending
        };
    }

    private string GetPromptsFilePath()
    {
        var path = _paths.PromptsFile;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(AppContext.BaseDirectory, path);
        return path;
    }
}
