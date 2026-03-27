using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class ClickUpConnectorClientTests
{
    [Fact]
    public void Type_ReturnsClickUp()
    {
        var client = CreateClient();
        Assert.Equal(ConnectorType.ClickUp, client.Type);
    }

    // --- ParseSourceConfig tests ---

    [Fact]
    public void ParseSourceConfig_ValidJson_ReturnsConfig()
    {
        var json = JsonSerializer.Serialize(new ClickUpSourceConfig
        {
            WorkspaceId = "12345",
            SpaceIds = ["space-1", "space-2"],
            BatchSize = 50,
        });

        var config = ClickUpConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.Equal("12345", config.WorkspaceId);
        Assert.Equal(2, config.SpaceIds.Count);
        Assert.Equal(50, config.BatchSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void ParseSourceConfig_InvalidInput_ReturnsNull(string? input)
    {
        var result = ClickUpConnectorClient.ParseSourceConfig(input);
        Assert.Null(result);
    }

    [Fact]
    public void ParseSourceConfig_DefaultValues()
    {
        var json = """{"workspaceId":"99"}""";
        var config = ClickUpConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.Equal("https://api.clickup.com", config.BaseUrl);
        Assert.True(config.IngestTasks);
        Assert.True(config.IngestDocs);
        Assert.Equal(100, config.BatchSize);
        Assert.Empty(config.SpaceIds);
        Assert.Empty(config.FolderIds);
        Assert.Empty(config.ListIds);
        Assert.Empty(config.TaskStatuses);
    }

    // --- ComputeHash tests ---

    [Fact]
    public void ComputeHash_DeterministicOutput()
    {
        var hash1 = ConnectorHttpHelper.ComputeHash("test input");
        var hash2 = ConnectorHttpHelper.ComputeHash("test input");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_DifferentHashes()
    {
        var hash1 = ConnectorHttpHelper.ComputeHash("input A");
        var hash2 = ConnectorHttpHelper.ComputeHash("input B");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHex()
    {
        var hash = ConnectorHttpHelper.ComputeHash("test");
        Assert.Matches("^[0-9a-f]+$", hash);
        Assert.Equal(64, hash.Length); // SHA256 = 32 bytes = 64 hex chars
    }

    // --- ParseClickUpTimestamp tests ---

    [Fact]
    public void ParseClickUpTimestamp_UnixMilliseconds_ParsesCorrectly()
    {
        var expected = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ms = expected.ToUnixTimeMilliseconds().ToString();
        var result = ClickUpConnectorClient.ParseClickUpTimestamp(ms);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseClickUpTimestamp_Iso8601_ParsesCorrectly()
    {
        var result = ClickUpConnectorClient.ParseClickUpTimestamp("2026-03-15T10:30:00Z");
        Assert.Equal(new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ParseClickUpTimestamp_NullOrEmpty_ReturnsUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var result = ClickUpConnectorClient.ParseClickUpTimestamp(null);
        Assert.True(result >= before);

        result = ClickUpConnectorClient.ParseClickUpTimestamp("");
        Assert.True(result >= before);
    }

    // --- MapTaskToCanonical tests ---

    [Fact]
    public void MapTaskToCanonical_BasicTask_MapsCorrectly()
    {
        var task = new ClickUpTask
        {
            Id = "abc123",
            Name = "Fix login bug",
            TextContent = "Users cannot login after password reset",
            Status = new ClickUpStatus { StatusName = "in progress", Type = "open" },
            Priority = new ClickUpPriority { Id = "1", PriorityName = "urgent" },
            DateCreated = "1704067200000", // 2024-01-01T00:00:00Z
            DateUpdated = "1711000000000",
            Url = "https://app.clickup.com/t/abc123",
            Tags = [new ClickUpTag { Name = "bug" }, new ClickUpTag { Name = "auth" }],
            Assignees = [new ClickUpUser { Id = 1, Username = "johndoe" }],
            List = new ClickUpIdName { Id = "list-1", Name = "Sprint 12" },
            Space = new ClickUpIdName { Id = "space-1", Name = "Engineering" },
        };

        var record = ClickUpConnectorClient.MapTaskToCanonical(task, "ws-1", "tenant-1");

        Assert.NotNull(record);
        Assert.Equal("clickup-task-abc123", record.EvidenceId);
        Assert.Equal(ConnectorType.ClickUp, record.SourceSystem);
        Assert.Equal(SourceType.Task, record.SourceType);
        Assert.Equal("Fix login bug", record.Title);
        Assert.Equal("Users cannot login after password reset", record.TextContent);
        Assert.Equal("P1", record.Severity);
        Assert.Equal("johndoe", record.Author);
        Assert.Contains("bug", record.Tags);
        Assert.Contains("auth", record.Tags);
        Assert.Contains("Sprint 12", record.Tags);
        Assert.Equal(AccessVisibility.Internal, record.Permissions.Visibility);
        Assert.Equal("Internal", record.AccessLabel);
        Assert.Equal("https://app.clickup.com/t/abc123", record.SourceLocator.Url);
        Assert.Equal("Sprint 12", record.SourceLocator.PipelineId);
        Assert.Equal(64, record.ContentHash.Length);
    }

    [Fact]
    public void MapTaskToCanonical_NullId_ReturnsNull()
    {
        var task = new ClickUpTask { Id = "", Name = "Test" };
        var record = ClickUpConnectorClient.MapTaskToCanonical(task, "ws-1", "t1");
        Assert.Null(record);
    }

    [Fact]
    public void MapTaskToCanonical_Priority_Mapping()
    {
        var urgent = CreateTask("1", priorityId: "1");
        var high = CreateTask("2", priorityId: "2");
        var normal = CreateTask("3", priorityId: "3");
        var low = CreateTask("4", priorityId: "4");
        var none = CreateTask("5", priorityId: null);

        Assert.Equal("P1", ClickUpConnectorClient.MapTaskToCanonical(urgent, "ws", "t1")!.Severity);
        Assert.Equal("P2", ClickUpConnectorClient.MapTaskToCanonical(high, "ws", "t1")!.Severity);
        Assert.Equal("P3", ClickUpConnectorClient.MapTaskToCanonical(normal, "ws", "t1")!.Severity);
        Assert.Equal("P4", ClickUpConnectorClient.MapTaskToCanonical(low, "ws", "t1")!.Severity);
        Assert.Null(ClickUpConnectorClient.MapTaskToCanonical(none, "ws", "t1")!.Severity);
    }

    [Fact]
    public void MapTaskToCanonical_ClosedStatus_MapsToClosed()
    {
        var closed = CreateTask("1", statusType: "closed");
        var done = CreateTask("2", statusType: "done");
        var open = CreateTask("3", statusType: "open");

        Assert.Equal(EvidenceStatus.Closed, ClickUpConnectorClient.MapTaskToCanonical(closed, "ws", "t1")!.Status);
        Assert.Equal(EvidenceStatus.Closed, ClickUpConnectorClient.MapTaskToCanonical(done, "ws", "t1")!.Status);
        Assert.Equal(EvidenceStatus.Open, ClickUpConnectorClient.MapTaskToCanonical(open, "ws", "t1")!.Status);
    }

    [Fact]
    public void MapTaskToCanonical_NoUrl_GeneratesDeepLink()
    {
        var task = CreateTask("task-xyz");
        task.Url = null;

        var record = ClickUpConnectorClient.MapTaskToCanonical(task, "ws-1", "t1");
        Assert.NotNull(record);
        Assert.Equal("https://app.clickup.com/t/task-xyz", record.SourceLocator.Url);
    }

    [Fact]
    public void MapTaskToCanonical_UsesTextContentOverDescription()
    {
        var task = new ClickUpTask
        {
            Id = "t1",
            Name = "Test",
            TextContent = "Plain text content",
            Description = "Rich HTML content",
            DateCreated = "1704067200000",
            DateUpdated = "1704067200000",
        };

        var record = ClickUpConnectorClient.MapTaskToCanonical(task, "ws", "t1");
        Assert.NotNull(record);
        Assert.Equal("Plain text content", record.TextContent);
    }

    [Fact]
    public void MapTaskToCanonical_FallsBackToDescription()
    {
        var task = new ClickUpTask
        {
            Id = "t1",
            Name = "Test",
            TextContent = null,
            Description = "Rich HTML content",
            DateCreated = "1704067200000",
            DateUpdated = "1704067200000",
        };

        var record = ClickUpConnectorClient.MapTaskToCanonical(task, "ws", "t1");
        Assert.NotNull(record);
        Assert.Equal("Rich HTML content", record.TextContent);
    }

    // --- Checkpoint tests ---

    [Fact]
    public void ClickUpCheckpoint_Roundtrip()
    {
        var ts = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);
        var cp = new ClickUpCheckpoint(2, 5, ts);

        var serialized = cp.Serialize();
        var parsed = ClickUpCheckpoint.Parse(serialized);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.ListIndex);
        Assert.Equal(5, parsed.Page);
        Assert.NotNull(parsed.LastModified);
        Assert.Equal(ts, parsed.LastModified.Value);
    }

    [Fact]
    public void ClickUpCheckpoint_Parse_NullInput_ReturnsNull()
    {
        Assert.Null(ClickUpCheckpoint.Parse(null));
        Assert.Null(ClickUpCheckpoint.Parse(""));
        Assert.Null(ClickUpCheckpoint.Parse("   "));
    }

    [Fact]
    public void ClickUpCheckpoint_Parse_InvalidFormat_ReturnsNull()
    {
        Assert.Null(ClickUpCheckpoint.Parse("not-valid"));
        Assert.Null(ClickUpCheckpoint.Parse("abc|0|2026-01-01"));
    }

    [Fact]
    public void ClickUpCheckpoint_Serialize_NoLastModified()
    {
        var cp = new ClickUpCheckpoint(0, 0, null);
        var serialized = cp.Serialize();
        Assert.Equal("0|0|", serialized);

        var parsed = ClickUpCheckpoint.Parse(serialized);
        Assert.NotNull(parsed);
        Assert.Equal(0, parsed.ListIndex);
        Assert.Equal(0, parsed.Page);
        Assert.Null(parsed.LastModified);
    }

    // --- TestConnectionAsync tests ---

    [Fact]
    public async Task TestConnectionAsync_NoSourceConfig_ReturnsFalse()
    {
        var client = CreateClient();
        var result = await client.TestConnectionAsync("t1", null, "token");
        Assert.False(result.Success);
        Assert.Contains("source configuration", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_NoSecret_ReturnsFalse()
    {
        var config = CreateSourceConfigJson();
        var client = CreateClient();
        var result = await client.TestConnectionAsync("t1", config, null);
        Assert.False(result.Success);
        Assert.Contains("credentials", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_SuccessfulConnection()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, """{"user":{"id":123,"username":"testuser"}}""");
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();

        var result = await client.TestConnectionAsync("t1", config, "my-token");

        Assert.True(result.Success);
        Assert.Contains("Successfully connected", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_Unauthorized()
    {
        var handler = new MockHttpHandler(HttpStatusCode.Unauthorized, """{"err":"Token invalid","ECODE":"OAUTH_025"}""");
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();

        var result = await client.TestConnectionAsync("t1", config, "bad-token");

        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
    }

    // --- FetchAsync tests ---

    [Fact]
    public async Task FetchAsync_InvalidConfig_ReturnsError()
    {
        var client = CreateClient();
        var result = await client.FetchAsync("t1", null, null, "token", null, true);

        Assert.Empty(result.Records);
        Assert.Single(result.Errors);
        Assert.Contains("source configuration", result.Errors[0]);
    }

    [Fact]
    public async Task FetchAsync_NoSecret_ReturnsError()
    {
        var config = CreateSourceConfigJson();
        var client = CreateClient();
        var result = await client.FetchAsync("t1", config, null, null, null, true);

        Assert.Empty(result.Records);
        Assert.Contains("credentials", result.Errors[0]);
    }

    [Fact]
    public async Task FetchAsync_Tasks_BackfillMode()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:api/v2/list/list-1/task"] = (HttpStatusCode.OK, """
                {
                    "tasks": [
                        {
                            "id": "t101",
                            "name": "Cannot login",
                            "text_content": "User reports login failure",
                            "status": {"status": "open", "type": "open"},
                            "priority": {"id": "1", "priority": "urgent"},
                            "date_created": "1704067200000",
                            "date_updated": "1711000000000",
                            "url": "https://app.clickup.com/t/t101"
                        },
                        {
                            "id": "t102",
                            "name": "Billing question",
                            "text_content": "Charged twice",
                            "status": {"status": "closed", "type": "closed"},
                            "priority": {"id": "3", "priority": "normal"},
                            "date_created": "1704067200000",
                            "date_updated": "1711100000000",
                            "url": "https://app.clickup.com/t/t102"
                        }
                    ],
                    "last_page": true
                }
            """),
        };

        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.Equal(2, result.Records.Count);
        Assert.False(result.HasMore);
        Assert.Empty(result.Errors);

        var first = result.Records[0];
        Assert.Equal("clickup-task-t101", first.EvidenceId);
        Assert.Equal("Cannot login", first.Title);
        Assert.Equal(ConnectorType.ClickUp, first.SourceSystem);
        Assert.Equal(SourceType.Task, first.SourceType);

        var second = result.Records[1];
        Assert.Equal("clickup-task-t102", second.EvidenceId);
        Assert.Equal(EvidenceStatus.Closed, second.Status);
    }

    [Fact]
    public async Task FetchAsync_WithPagination_SetsHasMore()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:api/v2/list/list-1/task"] = (HttpStatusCode.OK, """
                {
                    "tasks": [
                        {
                            "id": "t201",
                            "name": "Test",
                            "text_content": "Content",
                            "date_created": "1704067200000",
                            "date_updated": "1704067200000"
                        }
                    ],
                    "last_page": false
                }
            """),
        };

        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.True(result.HasMore);
        Assert.NotNull(result.NewCheckpoint);

        var cp = ClickUpCheckpoint.Parse(result.NewCheckpoint);
        Assert.NotNull(cp);
        Assert.Equal(0, cp.ListIndex);
        Assert.Equal(1, cp.Page); // Next page
    }

    [Fact]
    public async Task FetchAsync_CheckpointProduced_OnCompletion()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:api/v2/list/list-1/task"] = (HttpStatusCode.OK, """
                {
                    "tasks": [{"id": "t1", "name": "T1", "text_content": "", "date_created": "1704067200000", "date_updated": "1711000000000"}],
                    "last_page": true
                }
            """),
        };

        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.NotNull(result.NewCheckpoint);
        Assert.False(result.HasMore);

        var cp = ClickUpCheckpoint.Parse(result.NewCheckpoint);
        Assert.NotNull(cp);
        Assert.Equal(0, cp.ListIndex);
        Assert.Equal(0, cp.Page);
        Assert.NotNull(cp.LastModified);
    }

    [Fact]
    public async Task FetchAsync_MultipleLists_FetchesAll()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:api/v2/list/list-1/task"] = (HttpStatusCode.OK, """
                {
                    "tasks": [{"id": "t1", "name": "Task 1", "text_content": "", "date_created": "1704067200000", "date_updated": "1704067200000"}],
                    "last_page": true
                }
            """),
            ["GET:api/v2/list/list-2/task"] = (HttpStatusCode.OK, """
                {
                    "tasks": [{"id": "t2", "name": "Task 2", "text_content": "", "date_created": "1704067200000", "date_updated": "1704067200000"}],
                    "last_page": true
                }
            """),
        };

        var config = CreateSourceConfigJson(listIds: ["list-1", "list-2"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("clickup-task-t1", result.Records[0].EvidenceId);
        Assert.Equal("clickup-task-t2", result.Records[1].EvidenceId);
    }

    // --- PreviewAsync tests ---

    [Fact]
    public async Task PreviewAsync_ReturnsLimitedRecords()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:api/v2/list/list-1/task"] = (HttpStatusCode.OK, """
                {
                    "tasks": [{"id": "t1", "name": "Task 1", "text_content": "Body", "date_created": "1704067200000", "date_updated": "1704067200000"}],
                    "last_page": true
                }
            """),
        };

        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var records = await client.PreviewAsync("t1", config, null, "token", 5);

        Assert.Single(records);
        Assert.Equal("Task 1", records[0].Title);
    }

    [Fact]
    public async Task PreviewAsync_NoConfig_ReturnsEmpty()
    {
        var client = CreateClient();
        var records = await client.PreviewAsync("t1", null, null, "token", 5);
        Assert.Empty(records);
    }

    // --- List resolution tests ---

    [Fact]
    public async Task ResolveListIds_DirectListIds_ReturnsAsIs()
    {
        var config = new ClickUpSourceConfig
        {
            WorkspaceId = "ws-1",
            ListIds = ["list-a", "list-b"],
        };
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);
        var httpClient = client.CreateHttpClient("https://api.clickup.com", "token");

        var ids = await client.ResolveListIdsAsync(httpClient, config, CancellationToken.None);

        Assert.Equal(2, ids.Count);
        Assert.Equal("list-a", ids[0]);
        Assert.Equal("list-b", ids[1]);
    }

    [Fact]
    public async Task ResolveListIds_FromFolders_FetchesLists()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:api/v2/folder/folder-1/list"] = (HttpStatusCode.OK, """{"lists":[{"id":"list-from-folder","name":"Sprint"}]}"""),
        };

        var config = new ClickUpSourceConfig
        {
            WorkspaceId = "ws-1",
            FolderIds = ["folder-1"],
        };
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);
        var httpClient = client.CreateHttpClient("https://api.clickup.com", "token");

        var ids = await client.ResolveListIdsAsync(httpClient, config, CancellationToken.None);

        Assert.Single(ids);
        Assert.Equal("list-from-folder", ids[0]);
    }

    [Fact]
    public async Task ResolveListIds_FromWorkspace_TraversesHierarchy()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:api/v2/team/ws-1/space"] = (HttpStatusCode.OK, """{"spaces":[{"id":"space-1","name":"Engineering"}]}"""),
            ["GET:api/v2/space/space-1/list"] = (HttpStatusCode.OK, """{"lists":[{"id":"folderless-list","name":"Backlog"}]}"""),
            ["GET:api/v2/space/space-1/folder"] = (HttpStatusCode.OK, """{"folders":[{"id":"folder-1","name":"Sprints"}]}"""),
            ["GET:api/v2/folder/folder-1/list"] = (HttpStatusCode.OK, """{"lists":[{"id":"sprint-list","name":"Sprint 12"}]}"""),
        };

        var config = new ClickUpSourceConfig
        {
            WorkspaceId = "ws-1",
        };
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);
        var httpClient = client.CreateHttpClient("https://api.clickup.com", "token");

        var ids = await client.ResolveListIdsAsync(httpClient, config, CancellationToken.None);

        Assert.Equal(2, ids.Count);
        Assert.Contains("folderless-list", ids);
        Assert.Contains("sprint-list", ids);
    }

    // --- SourceConfig model tests ---

    [Fact]
    public void SourceConfig_DefaultsAreCorrect()
    {
        var config = new ClickUpSourceConfig { WorkspaceId = "1" };
        Assert.Equal("https://api.clickup.com", config.BaseUrl);
        Assert.True(config.IngestTasks);
        Assert.True(config.IngestDocs);
        Assert.Equal(100, config.BatchSize);
        Assert.Empty(config.SpaceIds);
        Assert.Empty(config.FolderIds);
        Assert.Empty(config.ListIds);
        Assert.Empty(config.TaskStatuses);
    }

    // --- Helpers ---

    private static ClickUpConnectorClient CreateClient(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = new LoggerFactory().CreateLogger<ClickUpConnectorClient>();
        return new ClickUpConnectorClient(factory, logger);
    }

    private static string CreateSourceConfigJson(
        string workspaceId = "12345",
        IReadOnlyList<string>? listIds = null)
    {
        return JsonSerializer.Serialize(new ClickUpSourceConfig
        {
            WorkspaceId = workspaceId,
            ListIds = listIds ?? [],
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static ClickUpTask CreateTask(
        string id, string? priorityId = null, string? statusType = null)
    {
        return new ClickUpTask
        {
            Id = id,
            Name = $"Task {id}",
            TextContent = "Content",
            Priority = priorityId is not null ? new ClickUpPriority { Id = priorityId } : null,
            Status = new ClickUpStatus { StatusName = statusType ?? "open", Type = statusType ?? "open" },
            DateCreated = "1704067200000",
            DateUpdated = "1704067200000",
        };
    }
}
