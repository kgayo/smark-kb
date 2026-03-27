using Azure.Search.Documents.Indexes.Models;

namespace SmartKb.Contracts.Search;

/// <summary>
/// Shared HNSW algorithm parameters for both Evidence and Case-Pattern vector search indexes.
/// </summary>
public static class VectorSearchDefaults
{
    public const int HnswM = 4;
    public const int HnswEfConstruction = 400;
    public const int HnswEfSearch = 500;

    /// <summary>
    /// Creates a standard <see cref="HnswParameters"/> instance with Cosine metric and shared tuning values.
    /// </summary>
    public static HnswParameters CreateHnswParameters() => new()
    {
        Metric = VectorSearchAlgorithmMetric.Cosine,
        M = HnswM,
        EfConstruction = HnswEfConstruction,
        EfSearch = HnswEfSearch,
    };
}
