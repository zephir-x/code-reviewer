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

// Retrieve logger and configuration to monitor application lifecycle
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var config = host.Services.GetRequiredService<IConfiguration>();

logger.LogInformation("Code Reviewer Agent started at {Time}", DateTimeOffset.Now);

try
{
    // Resolve main engine and execute the processing pipeline
    var engine = host.Services.GetRequiredService<CodeReviewerEngine>();
    
    string owner;
    string repo;
    int prNumber;

    // Check if running inside GitHub Actions CI/CD environment
    bool isGitHubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    if (isGitHubActions)
    {
        // Fetch dynamic context from GitHub Actions environment
        var githubRepo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var prNumberString = Environment.GetEnvironmentVariable("PR_NUMBER");

        if (string.IsNullOrWhiteSpace(githubRepo) || string.IsNullOrWhiteSpace(prNumberString) || !int.TryParse(prNumberString, out prNumber))
        {
            throw new InvalidOperationException("Invalid CI/CD context. GITHUB_REPOSITORY or PR_NUMBER environment variables are missing.");
        }

        // GITHUB_REPOSITORY format is always "owner/repo" (e.g., "zephir-x/GymCore")
        var repoParts = githubRepo.Split('/');
        owner = repoParts[0];
        repo = repoParts[1];
    }
    else
    {
        // Fallback to local development configuration
        logger.LogWarning("Local environment detected. Fetching target repository details from configuration.");
        
        owner = config["TargetRepository:Owner"];
        repo = config["TargetRepository:Name"];
        var prNumberString = config["TargetRepository:PullRequestNumber"];
        
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || !int.TryParse(prNumberString, out prNumber))
        {
            throw new InvalidOperationException("Target repository configuration is missing in appsettings.Development.json.");
        }
    }
    
    logger.LogInformation("Targeting PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
    await engine.RunPipelineAsync(owner, repo, prNumber);
    
    logger.LogInformation("Code review pipeline completed successfully.");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "A fatal error occurred during pipeline execution.");
    Environment.ExitCode = 1; // Explicitly fail the GitHub Action workflow on crash
}

logger.LogInformation("Shutting down the agent.");