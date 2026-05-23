namespace VideoAutomation.Models;

public class ComfyUISettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8188";
    public string DefaultWorkflow { get; set; } = "workflows/workflow_api.json";
    public int PollIntervalSeconds { get; set; } = 5;
    public int TimeoutMinutes { get; set; } = 30;
}

public class YouTubeSettings
{
    public string ClientSecretPath { get; set; } = "client_secret.json";
    public string TokenStorePath { get; set; } = "tokens";
    public string DefaultPrivacy { get; set; } = "unlisted";
    public int CategoryId { get; set; } = 22; // People & Blogs
}

public class PathSettings
{
    public string PromptsFile { get; set; } = "data/prompts.md";
    public string OutputDir { get; set; } = "output/generated_videos";
}
