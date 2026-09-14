using System.Text.Json.Serialization;

namespace CodeReviewer.Models;

public record CodeIssue(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("lineNumber")] int LineNumber,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("comment")] string Comment
);