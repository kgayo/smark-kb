using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class PolicyAwarePiiRedactionTests
{
    private readonly PiiRedactionService _sut = new();

    private static PiiPolicyResponse MakePolicy(
        string mode = "redact",
        IReadOnlyList<string>? types = null,
        IReadOnlyList<CustomPiiPattern>? customPatterns = null)
    {
        return new PiiPolicyResponse
        {
            TenantId = "t1",
            EnforcementMode = mode,
            EnabledPiiTypes = types ?? ["email", "phone", "ssn", "credit_card"],
            CustomPatterns = customPatterns ?? [],
            AuditRedactions = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void Redact_WithDisabledPolicy_ReturnsOriginal()
    {
        var policy = MakePolicy("disabled");
        var result = _sut.Redact("Contact user@example.com", policy);

        Assert.Equal("Contact user@example.com", result.RedactedText);
        Assert.Equal(0, result.TotalRedactions);
    }

    [Fact]
    public void Redact_WithDetectMode_CountsButDoesNotModify()
    {
        var policy = MakePolicy("detect");
        var text = "Email user@example.com and SSN 123-45-6789";
        var result = _sut.Redact(text, policy);

        Assert.Equal(text, result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["email"]);
        Assert.Equal(1, result.RedactionCounts["ssn"]);
        Assert.Equal(2, result.TotalRedactions);
    }

    [Fact]
    public void Redact_WithRedactMode_MasksEnabled()
    {
        var policy = MakePolicy("redact", types: ["email", "ssn"]);
        var text = "Email user@example.com, SSN 123-45-6789, Phone 555-123-4567";

        var result = _sut.Redact(text, policy);

        Assert.Contains("[REDACTED-EMAIL]", result.RedactedText);
        Assert.Contains("[REDACTED-SSN]", result.RedactedText);
        Assert.Contains("555-123-4567", result.RedactedText); // Phone NOT enabled
        Assert.DoesNotContain("user@example.com", result.RedactedText);
    }

    [Fact]
    public void Redact_WithOnlyEmailEnabled_IgnoresOtherTypes()
    {
        var policy = MakePolicy("redact", types: ["email"]);
        var text = "Email a@b.com, SSN 123-45-6789, Card 4111-1111-1111-1111";

        var result = _sut.Redact(text, policy);

        Assert.Contains("[REDACTED-EMAIL]", result.RedactedText);
        Assert.Contains("123-45-6789", result.RedactedText);
        Assert.Contains("4111-1111-1111-1111", result.RedactedText);
        Assert.Equal(1, result.TotalRedactions);
    }

    [Fact]
    public void Redact_WithCustomPattern_RedactsMatches()
    {
        var policy = MakePolicy("redact", types: ["email"],
            customPatterns:
            [
                new CustomPiiPattern
                {
                    Name = "order_id",
                    Pattern = @"ORD-\d{8}",
                    Placeholder = "[REDACTED-ORDER-ID]",
                },
            ]);
        var text = "Order ORD-12345678 by user@test.com";

        var result = _sut.Redact(text, policy);

        Assert.Contains("[REDACTED-ORDER-ID]", result.RedactedText);
        Assert.Contains("[REDACTED-EMAIL]", result.RedactedText);
        Assert.DoesNotContain("ORD-12345678", result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["order_id"]);
        Assert.Equal(1, result.RedactionCounts["email"]);
    }

    [Fact]
    public void Redact_WithDetectMode_CustomPatterns_DetectsOnly()
    {
        var policy = MakePolicy("detect", types: [],
            customPatterns:
            [
                new CustomPiiPattern
                {
                    Name = "internal_id",
                    Pattern = @"INT-\d{6}",
                    Placeholder = "[REDACTED-INT-ID]",
                },
            ]);
        var text = "Reference INT-123456";

        var result = _sut.Redact(text, policy);

        Assert.Equal(text, result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["internal_id"]);
    }

    [Fact]
    public void Redact_EmptyText_WithPolicy_ReturnsEmpty()
    {
        var policy = MakePolicy("redact");
        var result = _sut.Redact("", policy);

        Assert.Equal("", result.RedactedText);
        Assert.Equal(0, result.TotalRedactions);
    }

    [Fact]
    public void Redact_NullText_WithPolicy_ReturnsEmpty()
    {
        var policy = MakePolicy("redact");
        var result = _sut.Redact(null!, policy);

        Assert.Equal(string.Empty, result.RedactedText);
        Assert.Equal(0, result.TotalRedactions);
    }

    [Fact]
    public void Redact_EmptyEnabledTypes_NoCustom_NoRedactions()
    {
        var policy = MakePolicy("redact", types: []);
        var text = "Email user@example.com, SSN 123-45-6789";

        var result = _sut.Redact(text, policy);

        Assert.Equal(text, result.RedactedText);
        Assert.Equal(0, result.TotalRedactions);
    }

    [Fact]
    public void Redact_DefaultOverload_StillRedactsAll()
    {
        var text = "Email user@example.com, SSN 123-45-6789, Phone 555-123-4567";
        var result = _sut.Redact(text);

        Assert.Contains("[REDACTED-EMAIL]", result.RedactedText);
        Assert.Contains("[REDACTED-SSN]", result.RedactedText);
        Assert.Contains("[REDACTED-PHONE]", result.RedactedText);
    }
}

public class PolicyAwareRedactPiiInChunksTests
{
    private static readonly IPiiRedactionService RedactionService = new PiiRedactionService();

    [Fact]
    public void WithPolicy_RedactsOnlyEnabledTypes()
    {
        var policy = new PiiPolicyResponse
        {
            TenantId = "t1",
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email"],
            CustomPatterns = [],
            AuditRedactions = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Email user@test.com and SSN 123-45-6789"),
        };

        var (redacted, count, aggregatedCounts, affectedIds) =
            ChatOrchestrator.RedactPiiInChunksWithPolicy(chunks, RedactionService, policy);

        Assert.Equal(1, count);
        Assert.Contains("[REDACTED-EMAIL]", redacted[0].ChunkText);
        Assert.Contains("123-45-6789", redacted[0].ChunkText); // SSN not enabled
        Assert.Contains("email", aggregatedCounts.Keys);
        Assert.DoesNotContain("ssn", aggregatedCounts.Keys);
        Assert.Single(affectedIds);
        Assert.Equal("c1", affectedIds[0]);
    }

    [Fact]
    public void WithDisabledPolicy_NoRedaction()
    {
        var policy = new PiiPolicyResponse
        {
            TenantId = "t1",
            EnforcementMode = "disabled",
            EnabledPiiTypes = ["email"],
            CustomPatterns = [],
            AuditRedactions = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Email user@test.com"),
        };

        var (redacted, count, _, _) =
            ChatOrchestrator.RedactPiiInChunksWithPolicy(chunks, RedactionService, policy);

        Assert.Equal(0, count);
        Assert.Contains("user@test.com", redacted[0].ChunkText);
    }

    [Fact]
    public void WithDetectPolicy_CountsButDoesNotModify()
    {
        var policy = new PiiPolicyResponse
        {
            TenantId = "t1",
            EnforcementMode = "detect",
            EnabledPiiTypes = ["email", "ssn"],
            CustomPatterns = [],
            AuditRedactions = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Email user@test.com and SSN 123-45-6789"),
        };

        var (redacted, count, aggregatedCounts, affectedIds) =
            ChatOrchestrator.RedactPiiInChunksWithPolicy(chunks, RedactionService, policy);

        // Detect mode: counts but doesn't modify text.
        Assert.Equal(1, count);
        Assert.Contains("user@test.com", redacted[0].ChunkText);
        Assert.Contains("123-45-6789", redacted[0].ChunkText);
        Assert.Equal(1, aggregatedCounts["email"]);
        Assert.Equal(1, aggregatedCounts["ssn"]);
    }

    [Fact]
    public void AggregatesCounts_AcrossMultipleChunks()
    {
        var policy = new PiiPolicyResponse
        {
            TenantId = "t1",
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email"],
            CustomPatterns = [],
            AuditRedactions = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Email a@b.com"),
            MakeChunk("c2", "Email x@y.com and z@w.com"),
            MakeChunk("c3", "No PII here"),
        };

        var (_, count, aggregatedCounts, affectedIds) =
            ChatOrchestrator.RedactPiiInChunksWithPolicy(chunks, RedactionService, policy);

        Assert.Equal(2, count);
        Assert.Equal(3, aggregatedCounts["email"]);
        Assert.Equal(2, affectedIds.Count);
        Assert.DoesNotContain("c3", affectedIds);
    }

    private static RetrievedChunk MakeChunk(string chunkId, string text) => new()
    {
        ChunkId = chunkId,
        EvidenceId = $"ev-{chunkId}",
        ChunkText = text,
        Title = $"Title {chunkId}",
        SourceUrl = $"https://example.com/{chunkId}",
        SourceSystem = "AzureDevOps",
        SourceType = "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = "Internal",
        Visibility = "Internal",
        AllowedGroups = [],
        RrfScore = 0.5,
    };

    [Fact]
    public void Redact_WithInvalidRegexCustomPattern_SkipsAndContinues()
    {
        var policy = MakePolicy("redact", types: ["email"],
            customPatterns:
            [
                new CustomPiiPattern
                {
                    Name = "bad_pattern",
                    Pattern = @"[invalid(regex",
                    Placeholder = "[REDACTED-BAD]",
                },
                new CustomPiiPattern
                {
                    Name = "order_id",
                    Pattern = @"ORD-\d{8}",
                    Placeholder = "[REDACTED-ORDER-ID]",
                },
            ]);
        var text = "Order ORD-12345678 by user@test.com";

        var result = _sut.Redact(text, policy);

        // Invalid pattern skipped gracefully, valid patterns still applied.
        Assert.Contains("[REDACTED-ORDER-ID]", result.RedactedText);
        Assert.Contains("[REDACTED-EMAIL]", result.RedactedText);
        Assert.DoesNotContain("ORD-12345678", result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["order_id"]);
        Assert.False(result.RedactionCounts.ContainsKey("bad_pattern"));
    }
}
