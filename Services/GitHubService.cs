using CodeReviewer.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

namespace CodeReviewer.Services;

public class GitHubService(IConfiguration config, ILogger<GitHubService> logger) : IGitHubService
{
    public async Task<List<string>> GetPullRequestDiffAsync(string owner, string repo, int pullRequestNumber)
    {
        logger.LogInformation("Downloading files from PR #{PrNumber} for {Owner}/{Repo}...", pullRequestNumber, owner, repo);

        var githubToken = config["GitHub:Token"];
        if (string.IsNullOrWhiteSpace(githubToken))
        {
            throw new InvalidOperationException("GitHub token missing from configuration!");
        }

        // Initialize the official Octokit client with the required User-Agent
        var client = new GitHubClient(new ProductHeaderValue("CICD-CodeReviewer-Agent"))
        {
            Credentials = new Credentials(githubToken)
        };

        var diffs = new List<string>();
        try
        {
            // Query the API for the list of files in a given Pull Request
            var prFiles = await client.PullRequest.Files(owner, repo, pullRequestNumber);

            foreach (var file in prFiles)
            {
                // Skip deleted files and unchanged files (Patch)
                if (file.Status != "removed" && !string.IsNullOrWhiteSpace(file.Patch))
                {
                    // We save the file name and its diff (exactly what was added/removed)
                    diffs.Add($"File: {file.FileName}\nChanges:\n{file.Patch}");
                }
            }
            
            logger.LogInformation("Downloaded {Count} modified files for analysis.", diffs.Count);
            return diffs;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error communicating with the GitHub API.");
            return [];
        }
    }
}