using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoAutomation.Models;

namespace VideoAutomation.Services;

/// <summary>
/// Uploads videos to YouTube using the YouTube Data API v3 with OAuth2 credentials.
/// </summary>
public class YouTubeUploadService
{
    private readonly YouTubeSettings _settings;
    private readonly ILogger<YouTubeUploadService> _logger;

    public YouTubeUploadService(
        IOptions<YouTubeSettings> settings,
        ILogger<YouTubeUploadService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Uploads the video file to YouTube with metadata from the VideoEntry.
    /// Returns the YouTube video ID on success.
    /// </summary>
    public async Task<string> UploadAsync(string filePath, VideoEntry entry, CancellationToken ct = default)
    {
        var youtubeService = await CreateServiceAsync(ct);

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = BuildTitle(entry),
                Description = BuildDescription(entry),
                Tags = entry.Tags.Count > 0 ? entry.Tags : null,
                CategoryId = _settings.CategoryId.ToString()
            },
            Status = new Google.Apis.YouTube.v3.Data.VideoStatus
            {
                PrivacyStatus = _settings.DefaultPrivacy,
                SelfDeclaredMadeForKids = false
            }
        };

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        var insertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
        insertRequest.ChunkSize = ResumableUpload.MinimumChunkSize * 4; // 1MB chunks

        string? videoId = null;

        insertRequest.ProgressChanged += progress =>
        {
            switch (progress.Status)
            {
                case UploadStatus.Uploading:
                    _logger.LogDebug("Uploading '{Title}': {Bytes} bytes sent",
                        entry.Title, progress.BytesSent);
                    break;
                case UploadStatus.Failed:
                    _logger.LogError("Upload failed for '{Title}': {Error}",
                        entry.Title, progress.Exception?.Message);
                    break;
            }
        };

        insertRequest.ResponseReceived += uploadedVideo =>
        {
            videoId = uploadedVideo.Id;
            _logger.LogInformation(
                "Upload complete for '{Title}': https://youtube.com/watch?v={VideoId}",
                entry.Title, videoId);
        };

        var result = await insertRequest.UploadAsync(ct);

        if (result.Status == UploadStatus.Failed)
        {
            throw new InvalidOperationException(
                $"YouTube upload failed for '{entry.Title}': {result.Exception?.Message}",
                result.Exception);
        }

        return videoId ?? throw new InvalidOperationException("Upload succeeded but no video ID returned");
    }

    private async Task<YouTubeService> CreateServiceAsync(CancellationToken ct)
    {
        var clientSecretPath = _settings.ClientSecretPath;
        if (!Path.IsPathRooted(clientSecretPath))
            clientSecretPath = Path.Combine(AppContext.BaseDirectory, clientSecretPath);

        if (!File.Exists(clientSecretPath))
            throw new FileNotFoundException(
                $"YouTube client secret file not found: {clientSecretPath}. " +
                "Download it from Google Cloud Console → APIs & Services → Credentials.");

        UserCredential credential;
        await using (var stream = new FileStream(clientSecretPath, FileMode.Open, FileAccess.Read))
        {
            var tokenStorePath = _settings.TokenStorePath;
            if (!Path.IsPathRooted(tokenStorePath))
                tokenStorePath = Path.Combine(AppContext.BaseDirectory, tokenStorePath);

            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets,
                new[] { YouTubeService.Scope.YoutubeUpload },
                "user",
                ct,
                new FileDataStore(tokenStorePath, true));
        }

        return new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "VideoAutomation"
        });
    }

    /// <summary>
    /// Builds the YouTube title, appending #Shorts for vertical short-form content.
    /// </summary>
    private static string BuildTitle(VideoEntry entry)
    {
        var title = entry.Title;

        // Auto-append #Shorts for 9:16 aspect ratio content
        if (entry.AspectRatio == "9:16" && !title.Contains("#Shorts", StringComparison.OrdinalIgnoreCase))
        {
            title += " #Shorts";
        }

        // YouTube title limit is 100 characters
        if (title.Length > 100)
            title = title[..97] + "...";

        return title;
    }

    /// <summary>
    /// Builds the YouTube description from entry metadata.
    /// </summary>
    private static string BuildDescription(VideoEntry entry)
    {
        var desc = !string.IsNullOrEmpty(entry.Description) ? entry.Description : entry.Prompt;

        if (entry.AspectRatio == "9:16" && !desc.Contains("#Shorts", StringComparison.OrdinalIgnoreCase))
        {
            desc += "\n\n#Shorts";
        }

        return desc;
    }
}
