using Azure.Search.Documents.Indexes.Models;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Tests;

public class VectorSearchDefaultsTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(4, VectorSearchDefaults.HnswM);
        Assert.Equal(400, VectorSearchDefaults.HnswEfConstruction);
        Assert.Equal(500, VectorSearchDefaults.HnswEfSearch);
    }

    [Fact]
    public void CreateHnswParameters_ReturnsCosineMetric()
    {
        var params_ = VectorSearchDefaults.CreateHnswParameters();
        Assert.Equal(VectorSearchAlgorithmMetric.Cosine, params_.Metric);
    }

    [Fact]
    public void CreateHnswParameters_MatchesConstants()
    {
        var params_ = VectorSearchDefaults.CreateHnswParameters();
        Assert.Equal(VectorSearchDefaults.HnswM, params_.M);
        Assert.Equal(VectorSearchDefaults.HnswEfConstruction, params_.EfConstruction);
        Assert.Equal(VectorSearchDefaults.HnswEfSearch, params_.EfSearch);
    }
}
