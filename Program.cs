using CodeReviewer.Interfaces;
using CodeReviewer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Configure generic host to enable dependency injection, logging, and configuration
using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        // Load configuration from standard files and environment variables (essential for CI/CD secrets)
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        // Register GitHub service for fetching PR diffs
        services.AddTransient<IGitHubService, GitHubService>();
        
        // Register HTTP Client and AI Evaluator for Gemini API communication
        services.AddHttpClient<IAiEvaluator, GeminiAiEvaluator>();

        // TODO: Register GitHub Publisher service for inline comments
        // services.AddTransient<IGitHubPublisher, GitHubPublisher>();
        
        // TODO: Register Core application engine
        // services.AddTransient<CodeReviewerEngine>();
    })
    .Build();

// Retrieve logger to monitor application lifecycle
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Code Reviewer Agent started at {Time}", DateTimeOffset.Now);

try
{
    // Resolve main engine and execute the processing pipeline (Will be uncommented in future)
    // var engine = host.Services.GetRequiredService<CodeReviewerEngine>();
    // await engine.RunPipelineAsync();
    
    logger.LogInformation("Code review pipeline completed successfully.");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "A fatal error occurred during pipeline execution.");
    Environment.ExitCode = 1; // Explicitly fail the GitHub Action workflow on crash
}

logger.LogInformation("Shutting down the agent.");