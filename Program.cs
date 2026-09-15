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
        services.AddHttpClient<IAiEvaluator, GeminiAiEvaluator>()
            .AddStandardResilienceHandler(options => // Automatic request retries for 503, 502, 504 errors, etc.
            {
                // We are increasing the time allowed for a single attempt to 60 seconds (the AI needs time to generate a response)
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                
                // The circuit breaker's sampling duration MUST be at least double the attempt timeout
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
                
                // Total time per request, including any retries, is up to 5 minutes
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                
                // The default is 3, but we provide 5 attempts
                options.Retry.MaxRetryAttempts = 5; 
                
                // It starts with a 3-second pause and increases exponentially
                options.Retry.Delay = TimeSpan.FromSeconds(3);
            });

        // Register GitHub Publisher service for inline comments
        services.AddTransient<IGitHubPublisher, GitHubPublisher>();
        
        // Register Core application engine
        services.AddTransient<CodeReviewerEngine>();
    })
    .Build();

// Retrieve logger to monitor application lifecycle
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Code Reviewer Agent started at {Time}", DateTimeOffset.Now);

try
{
    // Resolve main engine and execute the processing pipeline
    var engine = host.Services.GetRequiredService<CodeReviewerEngine>();
    
    // TEMPORARY: Hardcoded repo variables for local testing
    // In next phase, these will be dynamically extracted from GitHub Actions environment variables (GITHUB_REPOSITORY, etc.)
    await engine.RunPipelineAsync("zephir-x", "code-review-test", 2);
    
    logger.LogInformation("Code review pipeline completed successfully.");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "A fatal error occurred during pipeline execution.");
    Environment.ExitCode = 1; // Explicitly fail the GitHub Action workflow on crash
}

logger.LogInformation("Shutting down the agent.");