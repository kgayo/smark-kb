using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Eval.Cli;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class EvalCliRunnerTests
{
    private static readonly string TestDatasetPath = Path.Combine(
        AppContext.BaseDirectory, "test-dataset.jsonl");

    private static readonly string TestBaselinePath = Path.Combine(
        AppContext.BaseDirectory, "test-baseline.json");

    private static readonly string TestOutputPath = Path.Combine(
        AppContext.BaseDirectory, "test-output.json");

    private static void WriteTestDataset(int count = 35)
    {
        var lines = Enumerable.Range(1, count).Select(i =>
            $$"""{"id":"eval-{{i:D5}}","tenant_id":"eval-tenant","query":"Test query number {{i}} for evaluation","context":{"product_area_hint":"General"},"expected":{"response_type":"final_answer","must_include":["test"],"must_cite_sources":false,"should_have_evidence":true},"tags":["test","tag-{{i % 5}}"]}""");
        File.WriteAllText(TestDatasetPath, string.Join('\n', lines));
    }

    private static void WriteTestBaseline(AggregateMetrics metrics)
    {
        var baseline = new EvalBaseline
        {
            Timestamp = DateTimeOffset.UtcNow,
            RunId = "baseline-test",
            TotalCases = 30,
            Metrics = metrics,
        };
        var json = BaselineComparator.SerializeBaseline(baseline);
        File.WriteAllText(TestBaselinePath, json);
    }

    private static void Cleanup()
    {
        if (File.Exists(TestDatasetPath)) File.Delete(TestDatasetPath);
        if (File.Exists(TestBaselinePath)) File.Delete(TestBaselinePath);
        if (File.Exists(TestOutputPath)) File.Delete(TestOutputPath);
    }

    [Fact]
    public async Task RunAsync_OfflineMode_ValidatesDataset()
    {
        try
        {
            WriteTestDataset(35);
            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                Mode = EvalMode.Full,
            };

            var result = await runner.RunAsync(options);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("validated", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_OfflineMode_WithBaseline_ChecksThresholds()
    {
        try
        {
            WriteTestDataset(35);
            WriteTestBaseline(new AggregateMetrics
            {
                Groundedness = 0.90f,
                CitationCoverage = 0.80f,
                RoutingAccuracy = 0.70f,
                NoEvidenceRate = 0.10f,
                ResponseTypeAccuracy = 0.85f,
                SafetyPassRate = 1.0f,
            });

            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                BaselinePath = TestBaselinePath,
                Mode = EvalMode.Full,
            };

            var result = await runner.RunAsync(options);

            Assert.Equal(0, result.ExitCode); // baseline passes thresholds
            Assert.Contains("baseline", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_OfflineMode_ThresholdViolation_ReturnsExitCode1()
    {
        try
        {
            WriteTestDataset(35);
            WriteTestBaseline(new AggregateMetrics
            {
                Groundedness = 0.50f, // below 0.80 threshold
                CitationCoverage = 0.40f, // below 0.70 threshold
                RoutingAccuracy = 0.30f, // below 0.60 threshold
                NoEvidenceRate = 0.50f, // above 0.25 threshold
            });

            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                BaselinePath = TestBaselinePath,
                Mode = EvalMode.Full,
            };

            var result = await runner.RunAsync(options);

            Assert.Equal(1, result.ExitCode);
            Assert.NotNull(result.Violations);
            Assert.True(result.Violations.Count > 0);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_InsufficientCases_ReturnsError()
    {
        try
        {
            WriteTestDataset(5); // Below minimum of 30
            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                Mode = EvalMode.Full,
            };

            var result = await runner.RunAsync(options);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("Insufficient", result.Annotations);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_DuplicateIds_ReturnsError()
    {
        try
        {
            var lines = new[]
            {
                """{"id":"eval-00001","tenant_id":"eval-tenant","query":"Test query one for eval","expected":{"response_type":"final_answer"},"tags":["test"]}""",
                """{"id":"eval-00001","tenant_id":"eval-tenant","query":"Test query two for eval","expected":{"response_type":"final_answer"},"tags":["test"]}""",
            };
            File.WriteAllText(TestDatasetPath, string.Join('\n', lines));

            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                Mode = EvalMode.Full,
            };

            var result = await runner.RunAsync(options);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("Duplicate", result.Annotations);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_LiveMode_WithMockOrchestrator_ProducesReport()
    {
        try
        {
            WriteTestDataset(35);
            var orchestrator = new FakeOrchestrator();

            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                OutputPath = TestOutputPath,
                Mode = EvalMode.Full,
                Orchestrator = orchestrator,
            };

            var result = await runner.RunAsync(options);

            Assert.Equal(0, result.ExitCode);
            Assert.NotNull(result.Report);
            Assert.Equal(35, result.Report.TotalCases);
            Assert.True(File.Exists(TestOutputPath));
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_SmokeMode_SelectsSubset()
    {
        try
        {
            WriteTestDataset(50);
            var orchestrator = new FakeOrchestrator();

            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                Mode = EvalMode.Smoke,
                SmokeCaseCount = 30,
                Orchestrator = orchestrator,
            };

            var result = await runner.RunAsync(options);

            Assert.Equal(0, result.ExitCode);
            Assert.NotNull(result.Report);
            Assert.Equal(30, result.Report.TotalCases);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_LiveMode_WithRegression_BlocksRelease()
    {
        try
        {
            WriteTestDataset(35);
            WriteTestBaseline(new AggregateMetrics
            {
                Groundedness = 0.95f,
                CitationCoverage = 0.90f,
                RoutingAccuracy = 0.85f,
                NoEvidenceRate = 0.05f,
                ResponseTypeAccuracy = 0.90f,
                SafetyPassRate = 1.0f,
                MustIncludeHitRate = 0.95f,
            });

            // FakeOrchestrator returns responses that will produce lower metrics
            var orchestrator = new FakeOrchestrator(groundedAnswer: false);

            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                BaselinePath = TestBaselinePath,
                OutputPath = TestOutputPath,
                Mode = EvalMode.Full,
                Orchestrator = orchestrator,
            };

            var result = await runner.RunAsync(options);

            // Should block due to regression from high baseline
            Assert.Equal(1, result.ExitCode);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public async Task RunAsync_UpdateBaseline_SavesOnSuccess()
    {
        try
        {
            WriteTestDataset(35);
            var orchestrator = new FakeOrchestrator();
            var baselinePath = TestBaselinePath + ".update";

            var runner = new EvalCliRunner();
            var options = new EvalCliOptions
            {
                DatasetPath = TestDatasetPath,
                BaselinePath = baselinePath,
                Mode = EvalMode.Full,
                UpdateBaseline = true,
                Orchestrator = orchestrator,
            };

            var result = await runner.RunAsync(options);

            if (result.ExitCode == 0)
            {
                Assert.True(File.Exists(baselinePath));
                var savedBaseline = await BaselineComparator.LoadBaselineAsync(baselinePath);
                Assert.NotNull(savedBaseline);
            }

            if (File.Exists(baselinePath)) File.Delete(baselinePath);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void SelectSmokeCases_ReturnsRequestedCount()
    {
        var cases = Enumerable.Range(1, 50).Select(i => new EvalCase
        {
            Id = $"eval-{i:D5}",
            TenantId = "t",
            Query = $"Test query number {i}",
            Expected = new EvalExpected { ResponseType = "final_answer" },
            Tags = [$"tag-{i % 5}"],
        }).ToList();

        var selected = EvalCliRunner.SelectSmokeCases(cases, 20);

        Assert.Equal(20, selected.Count);
        Assert.Equal(selected.Select(c => c.Id).Distinct().Count(), selected.Count); // No duplicates
    }

    [Fact]
    public void SelectSmokeCases_WhenCountExceedsTotal_ReturnsAll()
    {
        var cases = Enumerable.Range(1, 10).Select(i => new EvalCase
        {
            Id = $"eval-{i:D5}",
            TenantId = "t",
            Query = $"Test query number {i}",
            Expected = new EvalExpected { ResponseType = "final_answer" },
            Tags = [$"tag-{i % 3}"],
        }).ToList();

        var selected = EvalCliRunner.SelectSmokeCases(cases, 20);

        Assert.Equal(10, selected.Count);
    }

    [Fact]
    public void SelectSmokeCases_StratifiedSampling_CoversMultipleTags()
    {
        var cases = new List<EvalCase>();
        // 20 cases with tag "auth", 20 with "billing", 10 with "security"
        for (int i = 1; i <= 50; i++)
        {
            var tag = i <= 20 ? "auth" : i <= 40 ? "billing" : "security";
            cases.Add(new EvalCase
            {
                Id = $"eval-{i:D5}",
                TenantId = "t",
                Query = $"Test query number {i}",
                Expected = new EvalExpected { ResponseType = "final_answer" },
                Tags = [tag],
            });
        }

        var selected = EvalCliRunner.SelectSmokeCases(cases, 15);

        Assert.Equal(15, selected.Count);
        var selectedTags = selected.SelectMany(c => c.Tags).Distinct().ToList();
        // Should cover all 3 tag groups
        Assert.Contains("auth", selectedTags);
        Assert.Contains("billing", selectedTags);
        Assert.Contains("security", selectedTags);
    }

    private sealed class FakeOrchestrator : IChatOrchestrator
    {
        private readonly bool _groundedAnswer;

        public FakeOrchestrator(bool groundedAnswer = true)
        {
            _groundedAnswer = groundedAnswer;
        }

        public Task<ChatResponse> OrchestrateAsync(
            string tenantId, string userId, string correlationId,
            ChatRequest request, CancellationToken cancellationToken = default)
        {
            var answer = _groundedAnswer
                ? "This is a test answer containing test keywords for the evaluation."
                : "Unrelated response with no matching keywords.";

            return Task.FromResult(new ChatResponse
            {
                ResponseType = "final_answer",
                Answer = answer,
                Citations = _groundedAnswer
                    ? [new CitationDto { ChunkId = "c1", EvidenceId = "e1", Title = "Test Doc", SourceUrl = "test://doc", SourceSystem = "test", Snippet = "test", UpdatedAt = DateTimeOffset.UtcNow, AccessLabel = "public" }]
                    : [],
                Confidence = _groundedAnswer ? 0.85f : 0.2f,
                ConfidenceLabel = _groundedAnswer ? "High" : "Low",
                TraceId = correlationId,
                HasEvidence = _groundedAnswer,
                SystemPromptVersion = "test-v1",
            });
        }
    }
}
