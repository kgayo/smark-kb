using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public class CostEstimationTests
{
    [Fact]
    public void PromptCost_DefaultRate_CalculatesCorrectly()
    {
        var settings = new CostOptimizationSettings();
        var promptTokens = 1_000_000;

        var cost = promptTokens * settings.PromptTokenCostPerMillion / 1_000_000m;

        Assert.Equal(2.50m, cost);
    }

    [Fact]
    public void CompletionCost_DefaultRate_CalculatesCorrectly()
    {
        var settings = new CostOptimizationSettings();
        var completionTokens = 1_000_000;

        var cost = completionTokens * settings.CompletionTokenCostPerMillion / 1_000_000m;

        Assert.Equal(10.00m, cost);
    }

    [Fact]
    public void EmbeddingCost_DefaultRate_CalculatesCorrectly()
    {
        var settings = new CostOptimizationSettings();
        var embeddingTokens = 1_000_000;

        var cost = embeddingTokens * settings.EmbeddingTokenCostPerMillion / 1_000_000m;

        Assert.Equal(0.13m, cost);
    }

    [Fact]
    public void TotalCost_CombinesAllComponents()
    {
        var settings = new CostOptimizationSettings();
        var promptTokens = 5000;
        var completionTokens = 1000;
        var embeddingTokens = 500;

        var promptCost = promptTokens * settings.PromptTokenCostPerMillion / 1_000_000m;
        var completionCost = completionTokens * settings.CompletionTokenCostPerMillion / 1_000_000m;
        var embeddingCost = embeddingTokens * settings.EmbeddingTokenCostPerMillion / 1_000_000m;
        var totalCost = promptCost + completionCost + embeddingCost;

        // prompt: 5000 * 2.50 / 1M = 0.0125
        // completion: 1000 * 10.00 / 1M = 0.01
        // embedding: 500 * 0.13 / 1M = 0.000065
        var expected = 0.0125m + 0.01m + 0.000065m;
        Assert.Equal(expected, totalCost);
    }

    [Fact]
    public void ZeroTokens_ZeroCost()
    {
        var settings = new CostOptimizationSettings();

        var promptCost = 0 * settings.PromptTokenCostPerMillion / 1_000_000m;
        var completionCost = 0 * settings.CompletionTokenCostPerMillion / 1_000_000m;
        var embeddingCost = 0 * settings.EmbeddingTokenCostPerMillion / 1_000_000m;

        Assert.Equal(0m, promptCost);
        Assert.Equal(0m, completionCost);
        Assert.Equal(0m, embeddingCost);
    }

    [Fact]
    public void CustomRates_ApplyCorrectly()
    {
        var settings = new CostOptimizationSettings
        {
            PromptTokenCostPerMillion = 5.00m,
            CompletionTokenCostPerMillion = 20.00m,
            EmbeddingTokenCostPerMillion = 0.50m,
        };

        var promptCost = 10_000 * settings.PromptTokenCostPerMillion / 1_000_000m;
        var completionCost = 2_000 * settings.CompletionTokenCostPerMillion / 1_000_000m;
        var embeddingCost = 1_000 * settings.EmbeddingTokenCostPerMillion / 1_000_000m;

        Assert.Equal(0.05m, promptCost);
        Assert.Equal(0.04m, completionCost);
        Assert.Equal(0.0005m, embeddingCost);
    }

    [Fact]
    public void DefaultSettings_HaveExpectedValues()
    {
        var settings = new CostOptimizationSettings();

        Assert.True(settings.EnableEmbeddingCache);
        Assert.Equal(24, settings.EmbeddingCacheTtlHours);
        Assert.False(settings.EnableRetrievalCompression);
        Assert.Equal(1500, settings.MaxChunkCharsCompressed);
        Assert.Equal(80, settings.BudgetAlertThresholdPercent);
        Assert.Equal(2.50m, settings.PromptTokenCostPerMillion);
        Assert.Equal(10.00m, settings.CompletionTokenCostPerMillion);
        Assert.Equal(0.13m, settings.EmbeddingTokenCostPerMillion);
    }

    [Fact]
    public void LargeTokenCount_CalculatesWithoutOverflow()
    {
        var settings = new CostOptimizationSettings();
        var promptTokens = 100_000_000L;

        var cost = promptTokens * settings.PromptTokenCostPerMillion / 1_000_000m;

        Assert.Equal(250.00m, cost);
    }
}
