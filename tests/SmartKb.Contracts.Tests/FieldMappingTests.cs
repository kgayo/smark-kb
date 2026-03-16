using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public sealed class FieldMappingTests
{
    [Fact]
    public void FieldMappingRule_DirectTransformIsDefault()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "System.Title",
            TargetField = "Title",
        };

        Assert.Equal(FieldTransformType.Direct, rule.Transform);
        Assert.Null(rule.TransformExpression);
        Assert.False(rule.IsRequired);
        Assert.Null(rule.DefaultValue);
    }

    [Fact]
    public void FieldMappingRule_RegexTransform()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "fields.tags",
            TargetField = "Tags",
            Transform = FieldTransformType.Regex,
            TransformExpression = @"\s*;\s*",
            IsRequired = false,
        };

        Assert.Equal(FieldTransformType.Regex, rule.Transform);
        Assert.NotNull(rule.TransformExpression);
    }

    [Fact]
    public void FieldMappingRule_TemplateTransform()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "fields.org",
            TargetField = "ProductArea",
            Transform = FieldTransformType.Template,
            TransformExpression = "{value} - Engineering",
        };

        Assert.Equal(FieldTransformType.Template, rule.Transform);
    }

    [Fact]
    public void FieldMappingRule_LookupTransform()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "type",
            TargetField = "SourceType",
            Transform = FieldTransformType.Lookup,
            TransformExpression = "Bug=Ticket,Task=Task",
            IsRequired = true,
        };

        Assert.True(rule.IsRequired);
        Assert.Equal(FieldTransformType.Lookup, rule.Transform);
    }

    [Fact]
    public void FieldMappingRule_ConstantTransform()
    {
        var rule = new FieldMappingRule
        {
            SourceField = "_",
            TargetField = "Language",
            Transform = FieldTransformType.Constant,
            DefaultValue = "en-US",
        };

        Assert.Equal(FieldTransformType.Constant, rule.Transform);
        Assert.Equal("en-US", rule.DefaultValue);
    }

    [Fact]
    public void FieldMappingConfig_EmptyRules()
    {
        var config = new FieldMappingConfig();
        Assert.Empty(config.Rules);
    }

    [Fact]
    public void FieldMappingConfig_WithMultipleRules()
    {
        var config = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule { SourceField = "a", TargetField = "Title" },
                new FieldMappingRule { SourceField = "b", TargetField = "TextContent" },
                new FieldMappingRule { SourceField = "c", TargetField = "SourceType" },
            ],
        };

        Assert.Equal(3, config.Rules.Count);
    }

    [Theory]
    [InlineData(FieldTransformType.Direct)]
    [InlineData(FieldTransformType.Template)]
    [InlineData(FieldTransformType.Regex)]
    [InlineData(FieldTransformType.Lookup)]
    [InlineData(FieldTransformType.Constant)]
    public void FieldTransformType_AllValuesExist(FieldTransformType transformType)
    {
        Assert.True(Enum.IsDefined(transformType));
    }
}
