using CodeReviewer.Models;

namespace CodeReviewer.Interfaces;

public interface IGitHubPublisher
{
    Task PublishReviewAsync(string owner, string repo, int pullRequestNumber, CodeReviewResult reviewResult);
}