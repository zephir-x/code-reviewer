using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReviewer.Interfaces;
using CodeReviewer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CodeReviewer.Services;

public class GeminiAiEvaluator(HttpClient httpClient, IConfiguration config, ILogger<GeminiAiEvaluator> logger) : IAiEvaluator
{
    private const string SystemPrompt = """
        You are a strict, Senior .NET Software Architect performing a Code Review.
        You receive a git diff of a pull request.
        
        Your rules:
        1. Analyze ONLY the added or modified lines (those starting with '+' in the diff).
        2. Focus on architecture (Clean Architecture), SOLID principles, security (secrets, SQL injections), and serious performance flaws.
        3. Reject business logic in ASP.NET Core controllers.
        4. Ignore trivial styling or formatting issues.
        5. To determine the 'lineNumber', look at the hunk header (e.g., @@ -10,4 +10,5 @@) and count exactly to the added line.
        6. Return output EXCLUSIVELY as a JSON object matching exactly this schema:
        {
            "issues": [
                {
                    "fileName": "string (extract from the 'File: ' header)",
                    "lineNumber": int (the exact line number in the modified file),
                    "severity": "High | Medium | Low",
                    "comment": "string (your detailed architectural feedback)"
                }
            ]
        }
        No markdown formatting, no code blocks, just raw JSON.
        """;

    public async Task<CodeReviewResult> EvaluateDiffAsync(List<string> diffs)
    {
        var apiKey = config["Gemini:ApiKey"];
        var model = config["Gemini:Model"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key missing from configuration!");
        }

		if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Gemini Model missing from configuration!");
        }

        logger.LogInformation("Sending {Count} files to Gemini ({Model}) for evaluation...", diffs.Count, model);

        var combinedDiff = string.Join("\n\n", diffs);
        var requestPayload = BuildRequestPayload(combinedDiff);
        
        var requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var response = await httpClient.PostAsJsonAsync(requestUrl, requestPayload);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        return ParseResponse(responseContent);
    }

    private static object BuildRequestPayload(string diffContent)
    {
        return new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[]
            {
                new { parts = new[] { new { text = $"Review the following git diff:\n\n{diffContent}" } } }
            },
            generationConfig = new { responseMimeType = "application/json" }
        };
    }

    private CodeReviewResult ParseResponse(string jsonResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonResponse);
            
            // Extract the actual text response from Gemini's nested JSON structure
            var textResult = document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(textResult))
            {
                logger.LogWarning("Gemini returned an empty response.");
                return new CodeReviewResult([]);
            }

            var result = JsonSerializer.Deserialize<CodeReviewResult>(textResult);
            return result ?? new CodeReviewResult([]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse Gemini API response.");
            return new CodeReviewResult([]);
        }
    }
}