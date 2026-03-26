using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class GoldDatasetLoaderTests
{
    private const string ValidCase = """{"id":"eval-00001","tenant_id":"test-tenant","query":"How do I reset my password?","expected":{"response_type":"final_answer","must_include":["password","reset"],"must_cite_sources":true}}""";

    private const string MinimalCase = """{"id":"eval-00002","tenant_id":"test-tenant","query":"What is the status?","expected":{"response_type":"next_steps_only"}}""";

    [Fact]
    public void LoadFromString_ValidCase_ParsesCorrectly()
    {
        var cases = GoldDatasetLoader.LoadFromString(ValidCase);

        Assert.Single(cases);
        var c = cases[0];
        Assert.Equal("eval-00001", c.Id);
        Assert.Equal("test-tenant", c.TenantId);
        Assert.Equal("How do I reset my password?", c.Query);
        Assert.Equal("final_answer", c.Expected.ResponseType);
        Assert.Equal(new[] { "password", "reset" }, c.Expected.MustInclude);
        Assert.True(c.Expected.MustCiteSources);
    }

    [Fact]
    public void LoadFromString_MinimalCase_ParsesCorrectly()
    {
        var cases = GoldDatasetLoader.LoadFromString(MinimalCase);

        Assert.Single(cases);
        Assert.Equal("eval-00002", cases[0].Id);
        Assert.Equal("next_steps_only", cases[0].Expected.ResponseType);
        Assert.Null(cases[0].Expected.MustInclude);
        Assert.Null(cases[0].Expected.MustCiteSources);
    }

    [Fact]
    public void LoadFromString_MultipleCases_LoadsAll()
    {
        var jsonl = $"{ValidCase}\n{MinimalCase}";
        var cases = GoldDatasetLoader.LoadFromString(jsonl);
        Assert.Equal(2, cases.Count);
    }

    [Fact]
    public void LoadFromString_SkipsEmptyLinesAndComments()
    {
        var jsonl = $"\n# comment\n{ValidCase}\n\n";
        var cases = GoldDatasetLoader.LoadFromString(jsonl);
        Assert.Single(cases);
    }

    [Fact]
    public void LoadFromString_InvalidJson_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GoldDatasetLoader.LoadFromString("not json"));
        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public void Validate_ValidCase_NoErrors()
    {
        var evalCase = new EvalCase
        {
            Id = "eval-00001",
            TenantId = "test-tenant",
            Query = "How do I reset my password?",
            Expected = new EvalExpected { ResponseType = "final_answer" },
        };

        var errors = GoldDatasetLoader.Validate(evalCase);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidId_ReturnsError()
    {
        var evalCase = new EvalCase
        {
            Id = "bad-id",
            TenantId = "test-tenant",
            Query = "How do I reset my password?",
            Expected = new EvalExpected { ResponseType = "final_answer" },
        };

        var errors = GoldDatasetLoader.Validate(evalCase);
        Assert.Single(errors);
        Assert.Contains("eval-NNNNN", errors[0]);
    }

    [Fact]
    public void Validate_EmptyQuery_ReturnsError()
    {
        var evalCase = new EvalCase
        {
            Id = "eval-00001",
            TenantId = "test-tenant",
            Query = "",
            Expected = new EvalExpected { ResponseType = "final_answer" },
        };

        var errors = GoldDatasetLoader.Validate(evalCase);
        Assert.Contains(errors, e => e.Contains("Query"));
    }

    [Fact]
    public void Validate_InvalidResponseType_ReturnsError()
    {
        var evalCase = new EvalCase
        {
            Id = "eval-00001",
            TenantId = "test-tenant",
            Query = "Valid query here",
            Expected = new EvalExpected { ResponseType = "invalid_type" },
        };

        var errors = GoldDatasetLoader.Validate(evalCase);
        Assert.Single(errors);
        Assert.Contains("ResponseType", errors[0]);
    }

    [Fact]
    public void Validate_NegativeMinConfidence_ReturnsError()
    {
        var evalCase = new EvalCase
        {
            Id = "eval-00001",
            TenantId = "test-tenant",
            Query = "Valid query here",
            Expected = new EvalExpected { ResponseType = "final_answer", MinConfidence = -0.5f },
        };

        var errors = GoldDatasetLoader.Validate(evalCase);
        Assert.Single(errors);
        Assert.Contains("MinConfidence", errors[0]);
    }

    [Fact]
    public void FindDuplicateIds_NoDuplicates_ReturnsEmpty()
    {
        var cases = new[]
        {
            MakeCase("eval-00001"),
            MakeCase("eval-00002"),
        };

        var duplicates = GoldDatasetLoader.FindDuplicateIds(cases);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void FindDuplicateIds_WithDuplicates_ReturnsDuplicateIds()
    {
        var cases = new[]
        {
            MakeCase("eval-00001"),
            MakeCase("eval-00001"),
            MakeCase("eval-00002"),
        };

        var duplicates = GoldDatasetLoader.FindDuplicateIds(cases);
        Assert.Single(duplicates);
        Assert.Equal("eval-00001", duplicates[0]);
    }

    [Fact]
    public async Task LoadFromFileAsync_LoadsBaselineDataset()
    {
        var path = Path.Combine(FindRepoRoot(), "eval", "gold-dataset", "baseline.jsonl");
        if (!File.Exists(path))
        {
            // Skip if not running from repo root
            return;
        }

        var cases = await GoldDatasetLoader.LoadFromFileAsync(path);
        Assert.True(cases.Count >= 50, $"Expected at least 50 cases, got {cases.Count}");

        var duplicates = GoldDatasetLoader.FindDuplicateIds(cases);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void LoadFromString_CaseWithContext_ParsesContext()
    {
        var json = """{"id":"eval-00001","tenant_id":"t","query":"Test query here","context":{"product_area_hint":"Auth","customer_refs":["customer:acme"],"environment":{"region":"us-east-1"}},"expected":{"response_type":"final_answer"}}""";
        var cases = GoldDatasetLoader.LoadFromString(json);

        Assert.Single(cases);
        Assert.Equal("Auth", cases[0].Context?.ProductAreaHint);
        Assert.Equal(new[] { "customer:acme" }, cases[0].Context?.CustomerRefs);
        Assert.Equal("us-east-1", cases[0].Context?.Environment?["region"]);
    }

    [Fact]
    public void LoadFromString_CaseWithEscalationExpectation_ParsesCorrectly()
    {
        var json = """{"id":"eval-00001","tenant_id":"t","query":"Critical production issue","expected":{"response_type":"escalate","expected_escalation":{"recommended":true,"target_team":"Engineering"}}}""";
        var cases = GoldDatasetLoader.LoadFromString(json);

        Assert.Single(cases);
        Assert.NotNull(cases[0].Expected.ExpectedEscalation);
        Assert.True(cases[0].Expected.ExpectedEscalation!.Recommended);
        Assert.Equal("Engineering", cases[0].Expected.ExpectedEscalation!.TargetTeam);
    }

    [Fact]
    public void LoadFromString_CaseWithTags_ParsesTags()
    {
        var json = """{"id":"eval-00001","tenant_id":"t","query":"Test query here","expected":{"response_type":"final_answer"},"tags":["auth","sso"]}""";
        var cases = GoldDatasetLoader.LoadFromString(json);

        Assert.Equal(new[] { "auth", "sso" }, cases[0].Tags);
    }

    [Fact]
    public void LoadFromString_CaseWithSessionHistory_ParsesMultiTurn()
    {
        var json = """{"id":"eval-00051","tenant_id":"t","query":"Can you clarify?","context":{"product_area_hint":"Auth","session_history":[{"role":"user","content":"SSO login loop after cert rotation"},{"role":"assistant","content":"Check the certificate thumbprint."}]},"expected":{"response_type":"final_answer","must_include":["certificate"]},"tags":["auth","multi-turn"]}""";
        var cases = GoldDatasetLoader.LoadFromString(json);

        Assert.Single(cases);
        var c = cases[0];
        Assert.NotNull(c.Context?.SessionHistory);
        Assert.Equal(2, c.Context!.SessionHistory!.Count);
        Assert.Equal("user", c.Context.SessionHistory[0].Role);
        Assert.Equal("SSO login loop after cert rotation", c.Context.SessionHistory[0].Content);
        Assert.Equal("assistant", c.Context.SessionHistory[1].Role);
        Assert.Contains("multi-turn", c.Tags);
    }

    [Fact]
    public void LoadFromString_CaseWithLongSessionHistory_ParsesAllMessages()
    {
        var json = """{"id":"eval-00062","tenant_id":"t","query":"Summarize the situation","context":{"session_history":[{"role":"user","content":"msg1"},{"role":"assistant","content":"reply1"},{"role":"user","content":"msg2"},{"role":"assistant","content":"reply2"},{"role":"user","content":"msg3"},{"role":"assistant","content":"reply3"},{"role":"user","content":"msg4"},{"role":"assistant","content":"reply4"},{"role":"user","content":"msg5"},{"role":"assistant","content":"reply5"}]},"expected":{"response_type":"final_answer"}}""";
        var cases = GoldDatasetLoader.LoadFromString(json);

        Assert.Single(cases);
        Assert.Equal(10, cases[0].Context!.SessionHistory!.Count);
    }

    [Fact]
    public async Task LoadFromFileAsync_BaselineDatasetContainsMultiTurnCases()
    {
        var path = Path.Combine(FindRepoRoot(), "eval", "gold-dataset", "baseline.jsonl");
        if (!File.Exists(path))
            return;

        var cases = await GoldDatasetLoader.LoadFromFileAsync(path);
        var multiTurnCases = cases.Where(c => c.Context?.SessionHistory?.Count > 0).ToList();
        Assert.True(multiTurnCases.Count >= 12, $"Expected at least 12 multi-turn cases, got {multiTurnCases.Count}");

        // Verify all multi-turn cases have valid session_history entries
        foreach (var c in multiTurnCases)
        {
            Assert.All(c.Context!.SessionHistory!, m =>
            {
                Assert.Contains(m.Role, new[] { "user", "assistant" });
                Assert.False(string.IsNullOrWhiteSpace(m.Content));
            });
        }

        // Verify multi-turn category coverage via tags
        var multiTurnTags = multiTurnCases.SelectMany(c => c.Tags).ToHashSet();
        Assert.Contains("follow-up-clarification", multiTurnTags);
        Assert.Contains("context-accumulation", multiTurnTags);
        Assert.Contains("escalation-after-failure", multiTurnTags);
        Assert.Contains("topic-switch", multiTurnTags);
        Assert.Contains("session-summary", multiTurnTags);
    }

    private static EvalCase MakeCase(string id) => new()
    {
        Id = id,
        TenantId = "test-tenant",
        Query = "Test query here",
        Expected = new EvalExpected { ResponseType = "final_answer" },
    };

    [Fact]
    public void LoadFromString_MalformedJson_ThrowsInvalidOperationWithInnerJsonException()
    {
        var malformed = "{not valid json}";

        var ex = Assert.Throws<InvalidOperationException>(() => GoldDatasetLoader.LoadFromString(malformed));
        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
        Assert.Contains("line 1", ex.Message);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? AppContext.BaseDirectory;
    }
}
