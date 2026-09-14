namespace CodeReviewer.Interfaces;

public interface IGitHubService
{
    Task<List<string>> GetPullRequestDiffAsync(string owner, string repo, int pullRequestNumber);
}