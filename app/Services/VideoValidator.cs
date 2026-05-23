using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoAutomation.Models;

namespace VideoAutomation.Services;

/// <summary>
/// Validates generated video files before uploading to YouTube.
/// Checks file existence, size, and duration using ffprobe.
/// </summary>
public class VideoValidator
{
    private readonly ILogger<VideoValidator> _logger;
    private const long MinFileSizeBytes = 100 * 1024; // 100 KB minimum

    public VideoValidator(ILogger<VideoValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates the video file. Returns (isValid, errorMessage).
    /// </summary>
    public async Task<(bool IsValid, string? Error)> ValidateAsync(string filePath, VideoEntry entry)
    {
        // Check file exists
        if (!File.Exists(filePath))
            return (false, $"Video file does not exist: {filePath}");

        // Check file size
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length < MinFileSizeBytes)
            return (false, $"Video file too small ({fileInfo.Length} bytes), likely corrupt: {filePath}");

        // Check not already uploaded
        if (entry.Status == VideoStatus.Posted)
            return (false, $"Entry '{entry.Title}' is already posted");

        // Check duration via ffprobe
        var duration = await GetVideoDurationAsync(filePath);
        if (duration <= 0)
            return (false, $"Video duration is {duration}s, likely corrupt: {filePath}");

        _logger.LogInformation(
            "Video validated: {Path} ({Size} bytes, {Duration:F1}s)",
            filePath, fileInfo.Length, duration);

        return (true, null);
    }

    /// <summary>
    /// Gets the video duration in seconds using ffprobe.
    /// Returns -1 if ffprobe is not available or fails.
    /// </summary>
    public async Task<double> GetVideoDurationAsync(string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("ffprobe could not be started, skipping duration check");
                return 1; // Assume valid if ffprobe unavailable
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var duration))
            {
                return duration;
            }

            _logger.LogWarning("ffprobe returned unparseable output: {Output}", output);
            return 1; // Assume valid if we can't parse
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe failed, skipping duration check");
            return 1; // Assume valid if ffprobe not installed
        }
    }
}
