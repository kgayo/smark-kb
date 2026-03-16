using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public class RetrievalSettingsTests
{
    [Fact]
    public void Defaults_TopK_Is20()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(20, settings.TopK);
    }

    [Fact]
    public void Defaults_RrfK_Is60()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(60, settings.RrfK);
    }

    [Fact]
    public void Defaults_EqualWeights()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(1.0f, settings.Bm25Weight);
        Assert.Equal(1.0f, settings.VectorWeight);
    }

    [Fact]
    public void Defaults_SemanticReranking_Enabled()
    {
        var settings = new RetrievalSettings();
        Assert.True(settings.EnableSemanticReranking);
    }

    [Fact]
    public void Defaults_NoEvidenceScoreThreshold_Is03()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(0.3f, settings.NoEvidenceScoreThreshold);
    }

    [Fact]
    public void Defaults_NoEvidenceMinResults_Is3()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(3, settings.NoEvidenceMinResults);
    }

    [Fact]
    public void SectionName_IsRetrieval()
    {
        Assert.Equal("Retrieval", RetrievalSettings.SectionName);
    }
}
