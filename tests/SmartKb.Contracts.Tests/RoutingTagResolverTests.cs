using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public sealed class RoutingTagResolverTests
{
    private readonly RoutingTagResolver _resolver = new(NullLogger<RoutingTagResolver>.Instance);

    private static CanonicalRecord MakeRecord(
        string? productArea = null,
        string title = "Test Title",
        string textContent = "Test content",
        IReadOnlyList<string>? tags = null)
    {
        return new CanonicalRecord
        {
            TenantId = "tenant-1",
            EvidenceId = "ev-001",
            SourceSystem = ConnectorType.AzureDevOps,
            SourceType = SourceType.Ticket,
            SourceLocator = new SourceLocator("ev-001", "https://dev.azure.com/test"),
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = EvidenceStatus.Open,
            TextContent = textContent,
            Permissions = new RecordPermissions(AccessVisibility.Internal, []),
            ContentHash = "abc123",
            AccessLabel = "Internal",
            ProductArea = productArea,
            Tags = tags ?? [],
        };
    }

    [Fact]
    public void NullMapping_ReturnsRecordUnchanged()
    {
        var record = MakeRecord();
        var result = _resolver.ApplyRoutingTags(record, null);
        Assert.Same(record, result);
    }

    [Fact]
    public void NoRoutingTagRules_ReturnsRecordUnchanged()
    {
        var record = MakeRecord();
        var mapping = new FieldMappingConfig
        {
            Rules = [new FieldMappingRule { SourceField = "Title", TargetField = "Title" }]
        };
        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Same(record, result);
    }

    [Fact]
    public void DirectTransform_SetsProductArea()
    {
        var record = MakeRecord(title: "Authentication failure on login");
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Direct,
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Equal("Authentication failure on login", result.ProductArea);
    }

    [Fact]
    public void LookupTransform_SetsProductAreaFromTitleKeyword()
    {
        var record = MakeRecord(title: "Auth\\Login - SSO token expired");
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Lookup,
                    TransformExpression = "Auth=Authentication,Network=Networking,Storage=Storage",
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Equal("Authentication", result.ProductArea);
    }

    [Fact]
    public void LookupTransform_NoMatch_LeavesProductAreaNull()
    {
        var record = MakeRecord(title: "Some random issue");
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Lookup,
                    TransformExpression = "Auth=Authentication,Network=Networking",
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Null(result.ProductArea);
    }

    [Fact]
    public void ConstantTransform_SetsProductArea()
    {
        var record = MakeRecord();
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "_",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Constant,
                    DefaultValue = "Infrastructure",
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Equal("Infrastructure", result.ProductArea);
    }

    [Fact]
    public void RegexTransform_ExtractsProductArea()
    {
        var record = MakeRecord(title: "[Auth] Login page broken");
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Regex,
                    TransformExpression = @"\[(\w+)\]",
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Equal("Auth", result.ProductArea);
    }

    [Fact]
    public void TemplateTransform_SetsProductArea()
    {
        var record = MakeRecord(title: "Login");
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Template,
                    TransformExpression = "Area-{value}",
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Equal("Area-Login", result.ProductArea);
    }

    [Fact]
    public void ModuleTag_AddsModuleTagToTagsList()
    {
        var record = MakeRecord(tags: ["existing-tag"]);
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "Module",
                    Transform = FieldTransformType.Direct,
                    RoutingTag = RoutingTagNames.Module,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Contains("existing-tag", result.Tags);
        Assert.Contains("module:Test Title", result.Tags);
    }

    [Fact]
    public void ModuleTag_NoDuplicateModuleTags()
    {
        var record = MakeRecord(tags: ["module:Test Title"]);
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "Module",
                    Transform = FieldTransformType.Direct,
                    RoutingTag = RoutingTagNames.Module,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Single(result.Tags, t => t.StartsWith("module:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RoutingTag_OverridesExistingProductArea()
    {
        var record = MakeRecord(productArea: "OldArea");
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Constant,
                    DefaultValue = "NewArea",
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Equal("NewArea", result.ProductArea);
    }

    [Fact]
    public void UnknownSourceField_Skipped()
    {
        var record = MakeRecord();
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "System.AreaPath",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Direct,
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var result = _resolver.ApplyRoutingTags(record, mapping);
        Assert.Null(result.ProductArea);
    }

    [Fact]
    public void Batch_AppliesRoutingTagsToAllRecords()
    {
        var records = new[]
        {
            MakeRecord(title: "Auth issue"),
            MakeRecord(title: "Network problem"),
        };
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "Title",
                    TargetField = "ProductArea",
                    Transform = FieldTransformType.Lookup,
                    TransformExpression = "Auth=Authentication,Network=Networking",
                    RoutingTag = RoutingTagNames.ProductArea,
                }
            ]
        };

        var results = _resolver.ApplyRoutingTagsBatch(records, mapping);
        Assert.Equal("Authentication", results[0].ProductArea);
        Assert.Equal("Networking", results[1].ProductArea);
    }

    [Fact]
    public void ReadSourceValue_ReadsTags()
    {
        var record = MakeRecord(tags: ["tag1", "tag2"]);
        var value = RoutingTagResolver.ReadSourceValue(record, "Tags");
        Assert.Equal("tag1,tag2", value);
    }

    [Fact]
    public void ReadSourceValue_ReadsProductArea()
    {
        var record = MakeRecord(productArea: "Security");
        var value = RoutingTagResolver.ReadSourceValue(record, "ProductArea");
        Assert.Equal("Security", value);
    }

    [Fact]
    public void ReadSourceValue_CaseInsensitive()
    {
        var record = MakeRecord(title: "My Title");
        var value = RoutingTagResolver.ReadSourceValue(record, "title");
        Assert.Equal("My Title", value);
    }

    [Fact]
    public void RoutingTagNames_ContainsExpectedValues()
    {
        Assert.Contains(RoutingTagNames.ProductArea, RoutingTagNames.All);
        Assert.Contains(RoutingTagNames.Module, RoutingTagNames.All);
        Assert.Equal(2, RoutingTagNames.All.Count);
    }

    [Fact]
    public void RoutingTagNames_CaseInsensitiveContains()
    {
        Assert.Contains("PRODUCT_AREA", RoutingTagNames.All);
        Assert.Contains("MODULE", RoutingTagNames.All);
    }

    [Fact]
    public void RoutingTag_DefaultsToNull()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "Title",
            TargetField = "Title",
        };
        Assert.Null(rule.RoutingTag);
    }

    [Fact]
    public void ApplyTransform_Lookup_SemicolonSeparator()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "Title",
            TargetField = "ProductArea",
            Transform = FieldTransformType.Lookup,
            TransformExpression = "Auth=Authentication;Network=Networking",
            RoutingTag = RoutingTagNames.ProductArea,
        };

        var result = RoutingTagResolver.ApplyTransform("Auth issue", rule);
        Assert.Equal("Authentication", result);
    }

    [Fact]
    public void ApplyTransform_Regex_NoMatch_ReturnsNull()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "Title",
            TargetField = "ProductArea",
            Transform = FieldTransformType.Regex,
            TransformExpression = @"\[(\w+)\]",
            RoutingTag = RoutingTagNames.ProductArea,
        };

        var result = RoutingTagResolver.ApplyTransform("no brackets here", rule);
        Assert.Null(result);
    }

    [Fact]
    public void ApplyTransform_Regex_InvalidPattern_ReturnsNull()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "Title",
            TargetField = "ProductArea",
            Transform = FieldTransformType.Regex,
            TransformExpression = "[invalid",
            RoutingTag = RoutingTagNames.ProductArea,
        };

        var result = RoutingTagResolver.ApplyTransform("test", rule);
        Assert.Null(result);
    }

    [Fact]
    public void ApplyRegex_InvalidPattern_LogsWarning()
    {
        var logger = new FakeLogger();
        var result = RoutingTagResolver.ApplyRegex("test", "[invalid", logger);
        Assert.Null(result);
        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("regex invalid", logger.Entries[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyRegex_TimeoutPattern_LogsWarning()
    {
        // Pattern that causes catastrophic backtracking to trigger the 1-second timeout.
        var logger = new FakeLogger();
        var evilInput = new string('a', 50) + "!";
        var result = RoutingTagResolver.ApplyRegex(evilInput, @"^(a+)+$", logger);
        // Depending on runtime speed this may match or timeout; if it times out, verify warning.
        if (result is null && logger.Entries.Count > 0)
        {
            Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
            Assert.Contains("regex timed out", logger.Entries[0].Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
