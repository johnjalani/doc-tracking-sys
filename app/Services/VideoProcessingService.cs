using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoAutomation.Models;

namespace VideoAutomation.Services;

/// <summary>
/// Background service that runs the video generation pipeline on a schedule.
/// Ticks every 60 seconds, processes pending entries sequentially.
/// </summary>
public class VideoProcessingService : BackgroundService
{
    private readonly PromptFileService _promptService;
    private readonly ComfyUIService _comfyService;
    private readonly VideoValidator _validator;
    private readonly YouTubeUploadService _youtubeService;
    private readonly ILogger<VideoProcessingService> _logger;
    private readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(60);
    private const int MaxRetries = 3;

    public VideoProcessingService(
        PromptFileService promptService,
        ComfyUIService comfyService,
        VideoValidator validator,
        YouTubeUploadService youtubeService,
        ILogger<VideoProcessingService> logger)
    {
        _promptService = promptService;
        _comfyService = comfyService;
        _validator = validator;
        _youtubeService = youtubeService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Video processing service started");

        // Reset any entries stuck from a previous crash
        try
        {
            await _promptService.ResetStuckEntriesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset stuck entries on startup");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in processing loop");
            }

            await Task.Delay(_tickInterval, stoppingToken);
        }

        _logger.LogInformation("Video processing service stopped");
    }

    private async Task ProcessPendingEntriesAsync(CancellationToken ct)
    {
        var pending = await _promptService.GetPendingEntriesAsync(ct);
        if (pending.Count == 0)
        {
            _logger.LogDebug("No pending entries to process");
            return;
        }

        _logger.LogInformation("Found {Count} pending entries to process", pending.Count);

        // Process sequentially to avoid GPU contention
        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessSingleEntryAsync(entry, ct);
        }
    }

    private async Task ProcessSingleEntryAsync(VideoEntry entry, CancellationToken ct)
    {
        _logger.LogInformation("Processing entry: '{Title}'", entry.Title);

        string? videoPath = null;

        try
        {
            // Step 1: Generate video
            await _promptService.UpdateEntryStatusAsync(entry, VideoStatus.Generating, ct: ct);
            videoPath = await ExecuteWithRetryAsync(
                () => _comfyService.GenerateVideoAsync(entry, ct),
                "ComfyUI generation",
                ct);

            // Step 2: Validate video
            var (isValid, error) = await _validator.ValidateAsync(videoPath, entry);
            if (!isValid)
            {
                _logger.LogError("Video validation failed for '{Title}': {Error}", entry.Title, error);
                await _promptService.UpdateEntryStatusAsync(entry, VideoStatus.Failed, ct: ct);
                return;
            }

            // Step 3: Upload to YouTube
            await _promptService.UpdateEntryStatusAsync(entry, VideoStatus.Uploading, ct: ct);
            var youtubeId = await ExecuteWithRetryAsync(
                () => _youtubeService.UploadAsync(videoPath, entry, ct),
                "YouTube upload",
                ct);

            // Step 4: Mark as posted
            await _promptService.UpdateEntryStatusAsync(entry, VideoStatus.Posted, youtubeId, ct);
            _logger.LogInformation(
                "Successfully processed '{Title}' → https://youtube.com/watch?v={VideoId}",
                entry.Title, youtubeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process entry '{Title}'", entry.Title);
            await SafeUpdateStatusAsync(entry, VideoStatus.Failed, ct);
        }
    }

    /// <summary>
    /// Executes an async operation with exponential backoff retry.
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < MaxRetries && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "{Operation} attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}s",
                    operationName, attempt, MaxRetries, delay.TotalSeconds);

                await Task.Delay(delay, ct);
                delay *= 2; // Exponential backoff
            }
        }

        // Final attempt — let exception propagate
        return await operation();
    }

    private async Task SafeUpdateStatusAsync(VideoEntry entry, VideoStatus status, CancellationToken ct)
    {
        try
        {
            await _promptService.UpdateEntryStatusAsync(entry, status, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status to {Status} for '{Title}'",
                status, entry.Title);
        }
    }
}
