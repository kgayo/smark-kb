using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class CanonicalRecordTests
{
    private static CanonicalRecord CreateSample() => new()
    {
        TenantId = "tenant-1",
        EvidenceId = "ev-001",
        SourceSystem = ConnectorType.AzureDevOps,
        SourceType = SourceType.WikiPage,
        SourceLocator = new SourceLocator("obj-1", "https://dev.azure.com/org/proj/_wiki/page-1"),
        Title = "Setup Guide",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        UpdatedAt = DateTimeOffset.UtcNow,
        Status = EvidenceStatus.Open,
        TextContent = "How to set up the service...",
        Permissions = new RecordPermissions(AccessVisibility.Internal, ["support-team", "engineering"]),
        ContentHash = "abc123",
        AccessLabel = "Internal - Support Team, Engineering"
    };

    [Fact]
    public void CanonicalRecord_RequiredFieldsPopulated()
    {
        var record = CreateSample();

        Assert.Equal("tenant-1", record.TenantId);
        Assert.Equal("ev-001", record.EvidenceId);
        Assert.Equal(ConnectorType.AzureDevOps, record.SourceSystem);
        Assert.Equal(SourceType.WikiPage, record.SourceType);
        Assert.Equal("obj-1", record.SourceLocator.ObjectId);
        Assert.Equal(EvidenceStatus.Open, record.Status);
        Assert.Equal(AccessVisibility.Internal, record.Permissions.Visibility);
        Assert.Equal(2, record.Permissions.AllowedGroups.Count);
    }

    [Fact]
    public void CanonicalRecord_DefaultsApplied()
    {
        var record = CreateSample();

        Assert.Equal("en-US", record.Language);
        Assert.Empty(record.Tags);
        Assert.Empty(record.CustomerRefs);
        Assert.Null(record.ProductArea);
        Assert.Null(record.Severity);
        Assert.Null(record.Author);
        Assert.Null(record.ParentEvidenceId);
        Assert.Null(record.ThreadId);
        Assert.Null(record.PiiFlags);
        Assert.Null(record.SensitivityLabel);
    }

    [Fact]
    public void CanonicalRecord_OptionalFieldsPopulated()
    {
        var record = CreateSample() with
        {
            Tags = ["networking", "vpn"],
            ProductArea = "Infrastructure",
            Severity = "P2",
            Author = "user@corp.com",
            CustomerRefs = ["cust-42"],
            ParentEvidenceId = "ev-000",
            ThreadId = "thread-99",
            PiiFlags = ["email"],
            SensitivityLabel = "Confidential"
        };

        Assert.Equal(2, record.Tags.Count);
        Assert.Equal("Infrastructure", record.ProductArea);
        Assert.Equal("P2", record.Severity);
        Assert.Equal("user@corp.com", record.Author);
        Assert.Single(record.CustomerRefs);
        Assert.Equal("ev-000", record.ParentEvidenceId);
        Assert.Equal("thread-99", record.ThreadId);
        Assert.Single(record.PiiFlags!);
        Assert.Equal("Confidential", record.SensitivityLabel);
    }

    [Fact]
    public void SourceLocator_OptionalPipelineId()
    {
        var loc = new SourceLocator("obj-1", "https://example.com");
        Assert.Null(loc.PipelineId);

        var locWithPipeline = new SourceLocator("obj-2", "https://example.com", "pipe-1");
        Assert.Equal("pipe-1", locWithPipeline.PipelineId);
    }

    [Fact]
    public void RecordPermissions_AllowedGroupsReadOnly()
    {
        var perms = new RecordPermissions(AccessVisibility.Restricted, ["admins"]);

        Assert.Equal(AccessVisibility.Restricted, perms.Visibility);
        Assert.Single(perms.AllowedGroups);
        Assert.Equal("admins", perms.AllowedGroups[0]);
    }
}
