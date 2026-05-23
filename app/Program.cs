using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VideoAutomation.Models;
using VideoAutomation.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/video-automation-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Video Automation Pipeline starting...");

    var builder = Host.CreateApplicationBuilder(args);

    // Bind configuration sections
    builder.Services.Configure<ComfyUISettings>(
        builder.Configuration.GetSection("ComfyUI"));
    builder.Services.Configure<YouTubeSettings>(
        builder.Configuration.GetSection("YouTube"));
    builder.Services.Configure<PathSettings>(
        builder.Configuration.GetSection("Paths"));

    // Register services
    builder.Services.AddSingleton<PromptFileService>();
    builder.Services.AddSingleton<VideoValidator>();
    builder.Services.AddSingleton<YouTubeUploadService>();

    // Register ComfyUIService with typed HttpClient
    builder.Services.AddHttpClient<ComfyUIService>((sp, client) =>
    {
        var settings = builder.Configuration.GetSection("ComfyUI").Get<ComfyUISettings>() ?? new ComfyUISettings();
        client.BaseAddress = new Uri(settings.BaseUrl);
        client.Timeout = TimeSpan.FromMinutes(settings.TimeoutMinutes + 5);
    });

    // Register the background processing service
    builder.Services.AddHostedService<VideoProcessingService>();

    // Use Serilog
    builder.Services.AddSerilog();

    var app = builder.Build();
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
