using System.Text.Json;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Eval.Models;

namespace SmartKb.Eval;

/// <summary>
/// Loads and validates gold dataset cases from JSONL files (D-007).
/// </summary>
public static class GoldDatasetLoader
{
    /// <summary>
    /// Loads eval cases from a JSONL file. Each line is a JSON object matching EvalCase schema.
    /// </summary>
    public static async Task<IReadOnlyList<EvalCase>> LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Gold dataset file not found: {filePath}");

        var cases = new List<EvalCase>();
        var lineNumber = 0;

        await foreach (var line in ReadLinesAsync(filePath, cancellationToken))
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            EvalCase? evalCase;
            try
            {
                evalCase = JsonSerializer.Deserialize<EvalCase>(trimmed, SharedJsonOptions.CaseInsensitive);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to deserialize eval case at line {lineNumber}: {ex.Message}", ex);
            }

            if (evalCase is null)
                throw new InvalidOperationException($"Failed to deserialize eval case at line {lineNumber}");

            var errors = Validate(evalCase, lineNumber);
            if (errors.Count > 0)
                throw new InvalidOperationException($"Validation errors at line {lineNumber}: {string.Join("; ", errors)}");

            cases.Add(evalCase);
        }

        return cases;
    }

    /// <summary>
    /// Loads eval cases from a JSONL string content (for testing).
    /// </summary>
    public static IReadOnlyList<EvalCase> LoadFromString(string jsonlContent)
    {
        var cases = new List<EvalCase>();
        var lineNumber = 0;

        foreach (var line in jsonlContent.Split('\n'))
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            EvalCase? evalCase;
            try
            {
                evalCase = JsonSerializer.Deserialize<EvalCase>(trimmed, SharedJsonOptions.CaseInsensitive);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to deserialize eval case at line {lineNumber}: {ex.Message}", ex);
            }

            if (evalCase is null)
                throw new InvalidOperationException($"Failed to deserialize eval case at line {lineNumber}");

            var errors = Validate(evalCase, lineNumber);
            if (errors.Count > 0)
                throw new InvalidOperationException($"Validation errors at line {lineNumber}: {string.Join("; ", errors)}");

            cases.Add(evalCase);
        }

        return cases;
    }

    /// <summary>
    /// Validates a single eval case. Returns list of validation error messages.
    /// </summary>
    public static IReadOnlyList<string> Validate(EvalCase evalCase, int lineNumber = 0)
    {
        var errors = new List<string>();
        var prefix = lineNumber > 0 ? $"Line {lineNumber}: " : "";

        if (string.IsNullOrWhiteSpace(evalCase.Id))
            errors.Add($"{prefix}Id is required");
        else if (!evalCase.Id.StartsWith("eval-", StringComparison.Ordinal) || evalCase.Id.Length != 10)
            errors.Add($"{prefix}Id must match format eval-NNNNN (got '{evalCase.Id}')");

        if (string.IsNullOrWhiteSpace(evalCase.TenantId))
            errors.Add($"{prefix}TenantId is required");

        if (string.IsNullOrWhiteSpace(evalCase.Query))
            errors.Add($"{prefix}Query is required");
        else if (evalCase.Query.Length < 5)
            errors.Add($"{prefix}Query must be at least 5 characters");

        if (!ChatResponseType.AllValues.Contains(evalCase.Expected.ResponseType))
            errors.Add($"{prefix}Expected.ResponseType must be one of: {string.Join(", ", ChatResponseType.AllValues)}");

        if (evalCase.Expected.MinConfidence is < 0f or > 1f)
            errors.Add($"{prefix}Expected.MinConfidence must be between 0 and 1");

        if (evalCase.Expected.MinCitations is < 0)
            errors.Add($"{prefix}Expected.MinCitations must be non-negative");

        return errors;
    }

    /// <summary>
    /// Checks for duplicate IDs in a dataset.
    /// </summary>
    public static IReadOnlyList<string> FindDuplicateIds(IReadOnlyList<EvalCase> cases)
    {
        return cases
            .GroupBy(c => c.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(filePath);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }
}
