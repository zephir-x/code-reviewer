using CodeReviewer.Interfaces;
using CodeReviewer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

namespace CodeReviewer.Services;

public class GitHubPublisher(IConfiguration config, ILogger<GitHubPublisher> logger) : IGitHubPublisher
{
    public async Task PublishReviewAsync(string owner, string repo, int pullRequestNumber, CodeReviewResult reviewResult)
    {
        if (reviewResult.Issues == null || reviewResult.Issues.Count == 0)
        {
            logger.LogInformation("No architectural issues found by AI. Skipping GitHub comment publishing.");
            return;
        }

        var githubToken = config["GitHub:Token"];
        if (string.IsNullOrWhiteSpace(githubToken))
        {
            throw new InvalidOperationException("GitHub token missing from configuration!");
        }

        // Initialize GitHub client
        var client = new GitHubClient(new ProductHeaderValue("CICD-CodeReviewer-Agent"))
        {
            Credentials = new Credentials(githubToken)
        };

        // 1. We need the latest commit SHA of the Pull Request to attach comments to the code
        logger.LogInformation("Fetching Pull Request #{PrNumber} details to get the latest commit SHA...", pullRequestNumber);
        var pr = await client.PullRequest.Get(owner, repo, pullRequestNumber);
        var commitSha = pr.Head.Sha;

        // Fetch all existing review comments on this PR to prevent duplicate spam
        logger.LogInformation("Fetching existing comments to check for duplicates...");
        var existingComments = await client.PullRequest.ReviewComment.GetAll(owner, repo, pullRequestNumber);

        logger.LogInformation("Starting to publish {Count} comments on commit {Sha}...", reviewResult.Issues.Count, commitSha);

        // 2. Publish each issue as an inline review comment
        foreach (var issue in reviewResult.Issues)
        {
            try
            {
                // Format the comment nicely using Markdown
                var commentBody = $"**[🤖 AI Architecture Guard | Severity: {issue.Severity}]**\n\n{issue.Comment}";

                // Check if this exact bot comment already exists on this line
                bool isDuplicate = existingComments.Any(c => 
                    c.Path == issue.FileName && 
                    c.Position == issue.LineNumber && 
                    c.Body.Contains("[🤖 AI Architecture Guard"));

                if (isDuplicate)
                {
                    logger.LogInformation("Skipping duplicate comment on {File} at line {Line}.", issue.FileName, issue.LineNumber);
                    continue; // Skip to the next issue
                }

                // Create the comment object
                var comment = new PullRequestReviewCommentCreate(
                    commentBody,
                    commitSha,
                    issue.FileName,
                    issue.LineNumber
                );

                await client.PullRequest.ReviewComment.Create(owner, repo, pullRequestNumber, comment);
                logger.LogInformation("Successfully posted comment on {File} at line {Line}.", issue.FileName, issue.LineNumber);
            }
            catch (Exception ex)
            {
                // We catch exceptions per comment so one invalid line number doesn't crash the whole review process
                logger.LogError(ex, "Failed to post comment on {File} at line {Line}.", issue.FileName, issue.LineNumber);
            }
        }
    }
}