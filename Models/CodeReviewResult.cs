using System.Text.Json.Serialization;

namespace CodeReviewer.Models;

public record CodeReviewResult(
    [property: JsonPropertyName("issues")] List<CodeIssue> Issues
);