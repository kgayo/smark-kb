using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class PiiRedactionServiceTests
{
    private readonly PiiRedactionService _sut = new();

    #region Email Redaction

    [Fact]
    public void Redact_Email_ReplacesWithPlaceholder()
    {
        var result = _sut.Redact("Contact user@example.com for help");

        Assert.Contains("[REDACTED-EMAIL]", result.RedactedText);
        Assert.DoesNotContain("user@example.com", result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["email"]);
    }

    [Fact]
    public void Redact_MultipleEmails_ReplacesAll()
    {
        var result = _sut.Redact("Email a@b.com and c@d.org");

        Assert.Equal(2, result.RedactionCounts["email"]);
        Assert.DoesNotContain("a@b.com", result.RedactedText);
        Assert.DoesNotContain("c@d.org", result.RedactedText);
    }

    #endregion

    #region SSN Redaction

    [Fact]
    public void Redact_Ssn_ReplacesWithPlaceholder()
    {
        var result = _sut.Redact("SSN: 123-45-6789 on file");

        Assert.Contains("[REDACTED-SSN]", result.RedactedText);
        Assert.DoesNotContain("123-45-6789", result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["ssn"]);
    }

    #endregion

    #region Credit Card Redaction

    [Fact]
    public void Redact_CreditCard_ReplacesWithPlaceholder()
    {
        var result = _sut.Redact("Card: 4111-1111-1111-1111");

        Assert.Contains("[REDACTED-CREDIT-CARD]", result.RedactedText);
        Assert.DoesNotContain("4111-1111-1111-1111", result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["credit_card"]);
    }

    [Fact]
    public void Redact_CreditCardWithSpaces_ReplacesWithPlaceholder()
    {
        var result = _sut.Redact("Card: 4111 1111 1111 1111");

        Assert.Contains("[REDACTED-CREDIT-CARD]", result.RedactedText);
        Assert.DoesNotContain("4111 1111 1111 1111", result.RedactedText);
    }

    #endregion

    #region Phone Redaction

    [Fact]
    public void Redact_Phone_ReplacesWithPlaceholder()
    {
        var result = _sut.Redact("Call 555-123-4567 for help");

        Assert.Contains("[REDACTED-PHONE]", result.RedactedText);
        Assert.DoesNotContain("555-123-4567", result.RedactedText);
        Assert.Equal(1, result.RedactionCounts["phone"]);
    }

    #endregion

    #region Mixed PII

    [Fact]
    public void Redact_MultiplePiiTypes_RedactsAll()
    {
        var text = "Email admin@company.com, SSN 123-45-6789, phone 555-123-4567";
        var result = _sut.Redact(text);

        Assert.Contains("[REDACTED-EMAIL]", result.RedactedText);
        Assert.Contains("[REDACTED-SSN]", result.RedactedText);
        Assert.Contains("[REDACTED-PHONE]", result.RedactedText);
        Assert.DoesNotContain("admin@company.com", result.RedactedText);
        Assert.DoesNotContain("123-45-6789", result.RedactedText);
        Assert.DoesNotContain("555-123-4567", result.RedactedText);
        Assert.True(result.TotalRedactions >= 3);
    }

    #endregion

    #region No PII

    [Fact]
    public void Redact_NoPii_ReturnsOriginalText()
    {
        var text = "No personal information here.";
        var result = _sut.Redact(text);

        Assert.Equal(text, result.RedactedText);
        Assert.Equal(0, result.TotalRedactions);
        Assert.Empty(result.RedactionCounts);
    }

    [Fact]
    public void Redact_EmptyString_ReturnsEmpty()
    {
        var result = _sut.Redact("");

        Assert.Equal("", result.RedactedText);
        Assert.Equal(0, result.TotalRedactions);
    }

    [Fact]
    public void Redact_Null_ReturnsEmpty()
    {
        var result = _sut.Redact(null!);

        Assert.Equal(string.Empty, result.RedactedText);
        Assert.Equal(0, result.TotalRedactions);
    }

    #endregion
}

#region P0-014A: RedactPiiInChunks Tests

public class RedactPiiInChunksTests
{
    private static readonly IPiiRedactionService RedactionService = new PiiRedactionService();

    [Fact]
    public void ChunksWithNoPii_PassThrough()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Safe content with no PII"),
            MakeChunk("c2", "Another clean chunk"),
        };

        var (redacted, count) = ChatOrchestrator.RedactPiiInChunks(chunks, RedactionService);

        Assert.Equal(2, redacted.Count);
        Assert.Equal(0, count);
        Assert.Equal("Safe content with no PII", redacted[0].ChunkText);
    }

    [Fact]
    public void ChunkWithEmail_IsRedacted()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Contact user@example.com for help"),
        };

        var (redacted, count) = ChatOrchestrator.RedactPiiInChunks(chunks, RedactionService);

        Assert.Equal(1, count);
        Assert.Contains("[REDACTED-EMAIL]", redacted[0].ChunkText);
        Assert.DoesNotContain("user@example.com", redacted[0].ChunkText);
    }

    [Fact]
    public void ChunkWithPiiInContext_IsRedacted()
    {
        var chunk = MakeChunk("c1", "Clean text") with
        {
            ChunkContext = "Reporter: admin@corp.com"
        };

        var (redacted, count) = ChatOrchestrator.RedactPiiInChunks([chunk], RedactionService);

        Assert.Equal(1, count);
        Assert.Contains("[REDACTED-EMAIL]", redacted[0].ChunkContext);
        Assert.DoesNotContain("admin@corp.com", redacted[0].ChunkContext!);
    }

    [Fact]
    public void MixedChunks_OnlyPiiChunksRedacted()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Safe content"),
            MakeChunk("c2", "SSN is 123-45-6789"),
            MakeChunk("c3", "Also safe"),
        };

        var (redacted, count) = ChatOrchestrator.RedactPiiInChunks(chunks, RedactionService);

        Assert.Equal(3, redacted.Count);
        Assert.Equal(1, count);
        Assert.Equal("Safe content", redacted[0].ChunkText);
        Assert.Contains("[REDACTED-SSN]", redacted[1].ChunkText);
        Assert.DoesNotContain("123-45-6789", redacted[1].ChunkText);
        Assert.Equal("Also safe", redacted[2].ChunkText);
    }

    [Fact]
    public void EmptyChunks_ReturnsEmpty()
    {
        var (redacted, count) = ChatOrchestrator.RedactPiiInChunks([], RedactionService);

        Assert.Empty(redacted);
        Assert.Equal(0, count);
    }

    [Fact]
    public void RedactedChunk_PreservesAllOtherFields()
    {
        var original = MakeChunk("c1", "Email: test@test.com") with
        {
            EvidenceId = "ev-123",
            Title = "Bug Report",
            SourceUrl = "https://example.com/123",
            RrfScore = 0.85,
            Visibility = "Internal",
            ProductArea = "Auth",
        };

        var (redacted, _) = ChatOrchestrator.RedactPiiInChunks([original], RedactionService);

        Assert.Equal("ev-123", redacted[0].EvidenceId);
        Assert.Equal("Bug Report", redacted[0].Title);
        Assert.Equal("https://example.com/123", redacted[0].SourceUrl);
        Assert.Equal(0.85, redacted[0].RrfScore);
        Assert.Equal("Internal", redacted[0].Visibility);
        Assert.Equal("Auth", redacted[0].ProductArea);
        Assert.Contains("[REDACTED-EMAIL]", redacted[0].ChunkText);
    }

    [Fact]
    public void PiiNeverReachesPrompt_IntegrationProof()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "User john.doe@company.com reported SSN 123-45-6789 exposed"),
            MakeChunk("c2", "Safe content with no PII"),
        };

        var (redacted, count) = ChatOrchestrator.RedactPiiInChunks(chunks, RedactionService);

        Assert.Equal(1, count);

        var prompt = ChatOrchestrator.BuildSystemPrompt(redacted);

        Assert.DoesNotContain("john.doe@company.com", prompt);
        Assert.DoesNotContain("123-45-6789", prompt);
        Assert.Contains("[REDACTED-EMAIL]", prompt);
        Assert.Contains("[REDACTED-SSN]", prompt);
        Assert.Contains("Safe content with no PII", prompt);
    }

    private static RetrievedChunk MakeChunk(
        string chunkId,
        string text,
        string visibility = "Internal") => new()
    {
        ChunkId = chunkId,
        EvidenceId = $"ev-{chunkId}",
        ChunkText = text,
        Title = $"Title {chunkId}",
        SourceUrl = $"https://example.com/{chunkId}",
        SourceSystem = "AzureDevOps",
        SourceType = "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = visibility == "Restricted" ? "Restricted" : visibility,
        Visibility = visibility,
        AllowedGroups = [],
        RrfScore = 0.5,
    };
}

#endregion
