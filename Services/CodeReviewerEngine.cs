using CodeReviewer.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodeReviewer.Services;

public class CodeReviewerEngine(
    IGitHubService gitHubService,
    IAiEvaluator aiEvaluator,
    IGitHubPublisher gitHubPublisher,
    ILogger<CodeReviewerEngine> logger)
{
    public async Task RunPipelineAsync(string owner, string repo, int pullRequestNumber)
    {
        logger.LogInformation("Starting AI Code Review pipeline for {Owner}/{Repo} PR #{PrNumber}", owner, repo, pullRequestNumber);

        // 1. Fetch changed files
        var diffs = await gitHubService.GetPullRequestDiffAsync(owner, repo, pullRequestNumber);
        if (diffs.Count == 0)
        {
            logger.LogInformation("No valid diffs found to review. Pipeline finished.");
            return;
        }

        // 2. Send diffs to Gemini LLM
        var reviewResult = await aiEvaluator.EvaluateDiffAsync(diffs);

        // 3. Publish results back to GitHub
        logger.LogInformation("AI evaluation completed. Found {Count} potential architectural issues.", reviewResult.Issues?.Count ?? 0);
        await gitHubPublisher.PublishReviewAsync(owner, repo, pullRequestNumber, reviewResult);

        logger.LogInformation("Pipeline execution completed successfully.");
    }
}