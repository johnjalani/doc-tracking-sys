using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoAutomation.Models;

namespace VideoAutomation.Services;

/// <summary>
/// Integrates with the ComfyUI REST API to queue video generation workflows,
/// poll for completion, and download the resulting MP4 files.
/// </summary>
public class ComfyUIService
{
    private readonly HttpClient _http;
    private readonly ComfyUISettings _settings;
    private readonly PathSettings _paths;
    private readonly ILogger<ComfyUIService> _logger;

    public ComfyUIService(
        HttpClient http,
        IOptions<ComfyUISettings> settings,
        IOptions<PathSettings> paths,
        ILogger<ComfyUIService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _paths = paths.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates a video for the given entry:
    /// 1. Loads and customizes the workflow JSON
    /// 2. Queues it on ComfyUI
    /// 3. Polls until completion
    /// 4. Downloads the output MP4
    /// Returns the local file path of the downloaded video.
    /// </summary>
    public async Task<string> GenerateVideoAsync(VideoEntry entry, CancellationToken ct = default)
    {
        // Step 1: Load and customize workflow
        var workflowJson = await LoadWorkflowAsync(entry, ct);
        var customized = CustomizeWorkflow(workflowJson, entry);

        // Step 2: Queue the prompt
        var promptId = await QueuePromptAsync(customized, ct);
        _logger.LogInformation("Queued ComfyUI prompt {PromptId} for '{Title}'", promptId, entry.Title);

        // Step 3: Poll for completion
        await PollForCompletionAsync(promptId, ct);

        // Step 4: Download the output
        var outputPath = await DownloadOutputAsync(promptId, entry, ct);
        _logger.LogInformation("Downloaded video to {Path}", outputPath);

        return outputPath;
    }

    private async Task<string> LoadWorkflowAsync(VideoEntry entry, CancellationToken ct)
    {
        // Use per-entry workflow if specified, otherwise default
        var workflowFile = !string.IsNullOrEmpty(entry.Workflow)
            ? Path.Combine(Path.GetDirectoryName(_settings.DefaultWorkflow) ?? "workflows", $"{entry.Workflow}.json")
            : _settings.DefaultWorkflow;

        if (!Path.IsPathRooted(workflowFile))
            workflowFile = Path.Combine(AppContext.BaseDirectory, workflowFile);

        if (!File.Exists(workflowFile))
            throw new FileNotFoundException($"Workflow file not found: {workflowFile}");

        return await File.ReadAllTextAsync(workflowFile, ct);
    }

    private string CustomizeWorkflow(string workflowJson, VideoEntry entry)
    {
        var node = JsonNode.Parse(workflowJson);
        if (node == null)
            throw new InvalidOperationException("Failed to parse workflow JSON");

        // Walk all nodes to find and replace prompt text, model, and resolution
        foreach (var (key, value) in node.AsObject())
        {
            if (value == null) continue;

            var classType = value["class_type"]?.GetValue<string>();
            var inputs = value["inputs"];
            if (inputs == null) continue;

            // Replace text prompt in CLIPTextEncode or similar nodes
            if (classType is "CLIPTextEncode" or "WanVideoTextEncode")
            {
                if (inputs["text"] != null)
                {
                    inputs["text"] = entry.Prompt;
                    _logger.LogDebug("Set prompt text in node {Node} ({Type})", key, classType);
                }
            }

            // Replace checkpoint/model name
            if (classType is "CheckpointLoaderSimple" or "UNETLoader" && !string.IsNullOrEmpty(entry.Model))
            {
                if (inputs["ckpt_name"] != null)
                    inputs["ckpt_name"] = entry.Model;
                if (inputs["unet_name"] != null)
                    inputs["unet_name"] = entry.Model;
            }

            // Set resolution based on aspect ratio
            if (classType is "EmptyLatentImage" or "EmptySD3LatentImage" or "WanEmptyLatentVideo")
            {
                var (width, height) = ParseAspectRatio(entry.AspectRatio);
                if (inputs["width"] != null) inputs["width"] = width;
                if (inputs["height"] != null) inputs["height"] = height;
            }
        }

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static (int width, int height) ParseAspectRatio(string aspectRatio)
    {
        return aspectRatio switch
        {
            "9:16" => (1080, 1920),  // YouTube Shorts portrait
            "16:9" => (1920, 1080),  // Standard landscape
            "1:1" => (1080, 1080),   // Square
            "4:3" => (1440, 1080),
            _ => (1920, 1080)
        };
    }

    private async Task<string> QueuePromptAsync(string workflowJson, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["prompt"] = JsonNode.Parse(workflowJson),
            ["client_id"] = Guid.NewGuid().ToString()
        };

        var response = await _http.PostAsync(
            "/prompt",
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        var promptId = result?["prompt_id"]?.GetValue<string>();

        if (string.IsNullOrEmpty(promptId))
            throw new InvalidOperationException("ComfyUI did not return a prompt_id");

        return promptId;
    }

    private async Task PollForCompletionAsync(string promptId, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMinutes(_settings.TimeoutMinutes);
        var pollInterval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var response = await _http.GetAsync($"/history/{promptId}", ct);
            if (response.IsSuccessStatusCode)
            {
                var history = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
                var entry = history?[promptId];

                if (entry != null)
                {
                    var status = entry["status"];
                    var statusStr = status?["status_str"]?.GetValue<string>();
                    var completed = status?["completed"]?.GetValue<bool>();

                    if (completed == true || statusStr == "success")
                    {
                        _logger.LogInformation("ComfyUI prompt {PromptId} completed successfully", promptId);
                        return;
                    }

                    // Check for errors in the status
                    var messages = status?["messages"]?.AsArray();
                    if (messages != null)
                    {
                        foreach (var msg in messages)
                        {
                            var msgType = msg?[0]?.GetValue<string>();
                            if (msgType == "execution_error")
                            {
                                var errorDetail = msg?[1]?.ToJsonString() ?? "Unknown error";
                                throw new InvalidOperationException(
                                    $"ComfyUI execution failed: {errorDetail}");
                            }
                        }
                    }
                }
            }

            _logger.LogDebug("Polling ComfyUI for prompt {PromptId}...", promptId);
            await Task.Delay(pollInterval, ct);
        }

        throw new TimeoutException(
            $"ComfyUI prompt {promptId} did not complete within {_settings.TimeoutMinutes} minutes");
    }

    private async Task<string> DownloadOutputAsync(string promptId, VideoEntry entry, CancellationToken ct)
    {
        var response = await _http.GetAsync($"/history/{promptId}", ct);
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<JsonNode>(ct);

        var outputs = history?[promptId]?["outputs"];
        if (outputs == null)
            throw new InvalidOperationException("No outputs found in ComfyUI history");

        // Find the first video/image output
        string? filename = null;
        string? subfolder = null;
        string? fileType = null;

        foreach (var (nodeId, nodeOutput) in outputs.AsObject())
        {
            // Check for gifs/videos first, then images
            foreach (var outputType in new[] { "gifs", "videos", "images" })
            {
                var files = nodeOutput?[outputType]?.AsArray();
                if (files == null || files.Count == 0) continue;

                var firstFile = files[0];
                filename = firstFile?["filename"]?.GetValue<string>();
                subfolder = firstFile?["subfolder"]?.GetValue<string>() ?? "";
                fileType = firstFile?["type"]?.GetValue<string>() ?? "output";

                if (!string.IsNullOrEmpty(filename))
                    break;
            }
            if (!string.IsNullOrEmpty(filename))
                break;
        }

        if (string.IsNullOrEmpty(filename))
            throw new InvalidOperationException("No output file found in ComfyUI results");

        // Download the file
        var viewUrl = $"/view?filename={Uri.EscapeDataString(filename)}" +
                      $"&subfolder={Uri.EscapeDataString(subfolder ?? "")}" +
                      $"&type={Uri.EscapeDataString(fileType ?? "output")}";

        var fileResponse = await _http.GetAsync(viewUrl, ct);
        fileResponse.EnsureSuccessStatusCode();

        // Save to output directory
        var outputDir = _paths.OutputDir;
        if (!Path.IsPathRooted(outputDir))
            outputDir = Path.Combine(AppContext.BaseDirectory, outputDir);
        Directory.CreateDirectory(outputDir);

        var slug = Regex.Replace(entry.Title.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var extension = Path.GetExtension(filename);
        if (string.IsNullOrEmpty(extension)) extension = ".mp4";
        var outputFilename = $"{slug}_{timestamp}{extension}";
        var outputPath = Path.Combine(outputDir, outputFilename);

        await using var fileStream = File.Create(outputPath);
        await fileResponse.Content.CopyToAsync(fileStream, ct);

        return outputPath;
    }
}
