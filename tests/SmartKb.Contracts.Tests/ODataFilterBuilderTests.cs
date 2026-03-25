using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Tests;

public class ODataFilterBuilderTests
{
    #region BuildEvidenceFilter

    [Fact]
    public void BuildEvidenceFilter_Null_ReturnsNull()
    {
        Assert.Null(ODataFilterBuilder.BuildEvidenceFilter(null));
    }

    [Fact]
    public void BuildEvidenceFilter_EmptyFilter_ReturnsNull()
    {
        var filter = new RetrievalFilter();
        Assert.Null(ODataFilterBuilder.BuildEvidenceFilter(filter));
    }

    [Fact]
    public void BuildEvidenceFilter_SourceTypes_BuildsSearchIn()
    {
        var filter = new RetrievalFilter
        {
            SourceTypes = ["Ticket", "Document"],
        };

        var result = ODataFilterBuilder.BuildEvidenceFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("search.in(source_type, 'Ticket,Document', ',')", result);
    }

    [Fact]
    public void BuildEvidenceFilter_ProductAreas_BuildsSearchIn()
    {
        var filter = new RetrievalFilter
        {
            ProductAreas = ["Auth", "Billing"],
        };

        var result = ODataFilterBuilder.BuildEvidenceFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("search.in(product_area, 'Auth,Billing', ',')", result);
    }

    [Fact]
    public void BuildEvidenceFilter_TimeHorizon_BuildsGeFilter()
    {
        var filter = new RetrievalFilter
        {
            TimeHorizonDays = 90,
        };

        var result = ODataFilterBuilder.BuildEvidenceFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("updated_at ge", result);
        // The date should be approximately 90 days ago in ISO format.
        Assert.Matches(@"updated_at ge \d{4}-\d{2}-\d{2}T", result);
    }

    [Fact]
    public void BuildEvidenceFilter_Tags_BuildsAnyClause()
    {
        var filter = new RetrievalFilter
        {
            Tags = ["SSO", "timeout"],
        };

        var result = ODataFilterBuilder.BuildEvidenceFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("tags/any(t: search.in(t, 'SSO,timeout', ','))", result);
    }

    [Fact]
    public void BuildEvidenceFilter_Statuses_BuildsSearchIn()
    {
        var filter = new RetrievalFilter
        {
            Statuses = ["Active", "Closed"],
        };

        var result = ODataFilterBuilder.BuildEvidenceFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("search.in(status, 'Active,Closed', ',')", result);
    }

    [Fact]
    public void BuildEvidenceFilter_MultipleFilters_CombinesWithAnd()
    {
        var filter = new RetrievalFilter
        {
            SourceTypes = ["Ticket"],
            ProductAreas = ["Auth"],
            TimeHorizonDays = 30,
        };

        var result = ODataFilterBuilder.BuildEvidenceFilter(filter);

        Assert.NotNull(result);
        Assert.Contains(" and ", result);
        Assert.Contains("source_type", result);
        Assert.Contains("product_area", result);
        Assert.Contains("updated_at ge", result);
    }

    [Fact]
    public void BuildEvidenceFilter_ZeroTimeHorizon_Ignored()
    {
        var filter = new RetrievalFilter
        {
            TimeHorizonDays = 0,
        };

        Assert.Null(ODataFilterBuilder.BuildEvidenceFilter(filter));
    }

    [Fact]
    public void BuildEvidenceFilter_EmptyLists_Ignored()
    {
        var filter = new RetrievalFilter
        {
            SourceTypes = [],
            ProductAreas = [],
            Tags = [],
            Statuses = [],
        };

        Assert.Null(ODataFilterBuilder.BuildEvidenceFilter(filter));
    }

    #endregion

    #region BuildPatternFilter

    [Fact]
    public void BuildPatternFilter_Null_ReturnsNull()
    {
        Assert.Null(ODataFilterBuilder.BuildPatternFilter(null));
    }

    [Fact]
    public void BuildPatternFilter_SourceTypes_IgnoredForPatterns()
    {
        var filter = new RetrievalFilter
        {
            SourceTypes = ["Ticket"], // Patterns have no source_type field
        };

        // source_type filter should NOT apply to patterns
        Assert.Null(ODataFilterBuilder.BuildPatternFilter(filter));
    }

    [Fact]
    public void BuildPatternFilter_ProductAreas_Applied()
    {
        var filter = new RetrievalFilter
        {
            ProductAreas = ["Auth"],
        };

        var result = ODataFilterBuilder.BuildPatternFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("search.in(product_area, 'Auth', ',')", result);
    }

    [Fact]
    public void BuildPatternFilter_TimeHorizon_Applied()
    {
        var filter = new RetrievalFilter
        {
            TimeHorizonDays = 60,
        };

        var result = ODataFilterBuilder.BuildPatternFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("updated_at ge", result);
    }

    [Fact]
    public void BuildPatternFilter_Tags_Applied()
    {
        var filter = new RetrievalFilter
        {
            Tags = ["SSO"],
        };

        var result = ODataFilterBuilder.BuildPatternFilter(filter);

        Assert.NotNull(result);
        Assert.Contains("tags/any(t: search.in(t, 'SSO', ','))", result);
    }

    [Fact]
    public void BuildPatternFilter_Statuses_IgnoredForPatterns()
    {
        var filter = new RetrievalFilter
        {
            Statuses = ["Active"],
        };

        Assert.Null(ODataFilterBuilder.BuildPatternFilter(filter));
    }

    #endregion

    #region CombineFilters

    [Fact]
    public void CombineFilters_NullAdditional_ReturnsBase()
    {
        var result = ODataFilterBuilder.CombineFilters("tenant_id eq 'abc'", null);
        Assert.Equal("tenant_id eq 'abc'", result);
    }

    [Fact]
    public void CombineFilters_EmptyAdditional_ReturnsBase()
    {
        var result = ODataFilterBuilder.CombineFilters("tenant_id eq 'abc'", "");
        Assert.Equal("tenant_id eq 'abc'", result);
    }

    [Fact]
    public void CombineFilters_WithAdditional_CombinesWithAnd()
    {
        var result = ODataFilterBuilder.CombineFilters(
            "tenant_id eq 'abc'",
            "search.in(source_type, 'Ticket', ',')");

        Assert.Equal("tenant_id eq 'abc' and search.in(source_type, 'Ticket', ',')", result);
    }

    #endregion

    #region OData Value Escaping

    [Fact]
    public void EscapeODataValue_SingleQuotes_Doubled()
    {
        Assert.Equal("O''Brien", ODataFilterBuilder.EscapeODataValue("O'Brien"));
    }

    [Fact]
    public void EscapeODataValue_NoSpecialChars_Unchanged()
    {
        Assert.Equal("normal-value", ODataFilterBuilder.EscapeODataValue("normal-value"));
    }

    [Fact]
    public void SearchInValue_Commas_Stripped()
    {
        // Commas in values are stripped to prevent delimiter confusion
        var result = ODataFilterBuilder.BuildSearchInClause("field", ["val,ue1", "value2"]);
        Assert.Contains("'value1,value2'", result);
    }

    [Fact]
    public void EscapeODataValue_UnicodeChars_Unchanged()
    {
        // Verify ordinal comparison doesn't alter Unicode content
        Assert.Equal("café''s", ODataFilterBuilder.EscapeODataValue("café's"));
    }

    #endregion

    #region RetrievalFilter.IsEmpty

    [Fact]
    public void IsEmpty_True_WhenAllNull()
    {
        Assert.True(new RetrievalFilter().IsEmpty);
    }

    [Fact]
    public void IsEmpty_True_WhenAllEmptyLists()
    {
        var filter = new RetrievalFilter
        {
            SourceTypes = [],
            ProductAreas = [],
            Tags = [],
            Statuses = [],
        };
        Assert.True(filter.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenSourceTypes()
    {
        Assert.False(new RetrievalFilter { SourceTypes = ["Ticket"] }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenTimeHorizon()
    {
        Assert.False(new RetrievalFilter { TimeHorizonDays = 30 }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenProductAreas()
    {
        Assert.False(new RetrievalFilter { ProductAreas = ["Auth"] }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenTags()
    {
        Assert.False(new RetrievalFilter { Tags = ["SSO"] }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenStatuses()
    {
        Assert.False(new RetrievalFilter { Statuses = ["Active"] }.IsEmpty);
    }

    #endregion
}
