using CodeReviewer.Models;

namespace CodeReviewer.Interfaces;

public interface IAiEvaluator
{
    Task<CodeReviewResult> EvaluateDiffAsync(List<string> diffs);
}