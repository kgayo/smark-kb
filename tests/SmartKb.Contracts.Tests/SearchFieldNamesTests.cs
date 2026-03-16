using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Tests;

public class SearchFieldNamesTests
{
    [Fact]
    public void AclFields_Defined()
    {
        Assert.Equal("tenant_id", SearchFieldNames.TenantId);
        Assert.Equal("visibility", SearchFieldNames.Visibility);
        Assert.Equal("allowed_groups", SearchFieldNames.AllowedGroups);
        Assert.Equal("access_label", SearchFieldNames.AccessLabel);
    }

    [Fact]
    public void MetadataFilterFields_Defined()
    {
        Assert.Equal("source_system", SearchFieldNames.SourceSystem);
        Assert.Equal("source_type", SearchFieldNames.SourceType);
        Assert.Equal("status", SearchFieldNames.Status);
        Assert.Equal("updated_at", SearchFieldNames.UpdatedAt);
        Assert.Equal("product_area", SearchFieldNames.ProductArea);
        Assert.Equal("tags", SearchFieldNames.Tags);
    }

    [Fact]
    public void SearchableFields_Defined()
    {
        Assert.Equal("chunk_text", SearchFieldNames.ChunkText);
        Assert.Equal("chunk_context", SearchFieldNames.ChunkContext);
        Assert.Equal("title", SearchFieldNames.Title);
    }

    [Fact]
    public void VectorField_Defined()
    {
        Assert.Equal("embedding_vector", SearchFieldNames.EmbeddingVector);
        Assert.Equal(1536, SearchFieldNames.EmbeddingDimensions);
    }

    [Fact]
    public void SearchProfiles_Defined()
    {
        Assert.Equal("evidence-vector-profile", SearchFieldNames.VectorProfileName);
        Assert.Equal("evidence-semantic-config", SearchFieldNames.SemanticConfigName);
    }

    [Fact]
    public void AllFieldNames_UseSnakeCase()
    {
        var fieldNames = new[]
        {
            SearchFieldNames.ChunkId, SearchFieldNames.ChunkText, SearchFieldNames.ChunkContext,
            SearchFieldNames.Title, SearchFieldNames.EmbeddingVector, SearchFieldNames.TenantId,
            SearchFieldNames.EvidenceId, SearchFieldNames.SourceSystem, SearchFieldNames.SourceType,
            SearchFieldNames.Status, SearchFieldNames.UpdatedAt, SearchFieldNames.ProductArea,
            SearchFieldNames.Tags, SearchFieldNames.Visibility, SearchFieldNames.AllowedGroups,
            SearchFieldNames.AccessLabel, SearchFieldNames.SourceUrl
        };

        foreach (var name in fieldNames)
        {
            Assert.DoesNotContain(" ", name);
            Assert.Equal(name, name.ToLowerInvariant());
        }
    }
}
