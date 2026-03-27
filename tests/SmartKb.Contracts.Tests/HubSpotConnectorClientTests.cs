using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class HubSpotConnectorClientTests
{
    [Fact]
    public void Type_ReturnsHubSpot()
    {
        var client = CreateClient();
        Assert.Equal(ConnectorType.HubSpot, client.Type);
    }

    // --- ParseSourceConfig tests ---

    [Fact]
    public void ParseSourceConfig_ValidJson_ReturnsConfig()
    {
        var json = JsonSerializer.Serialize(new HubSpotSourceConfig
        {
            PortalId = "12345",
            ObjectTypes = ["tickets", "contacts"],
            BatchSize = 50,
        });

        var config = HubSpotConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.Equal("12345", config.PortalId);
        Assert.Equal(2, config.ObjectTypes.Count);
        Assert.Equal(50, config.BatchSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void ParseSourceConfig_InvalidInput_ReturnsNull(string? input)
    {
        var result = HubSpotConnectorClient.ParseSourceConfig(input);
        Assert.Null(result);
    }

    [Fact]
    public void ParseSourceConfig_DefaultValues()
    {
        var json = """{"portalId":"99"}""";
        var config = HubSpotConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.Equal("https://api.hubapi.com", config.BaseUrl);
        Assert.Single(config.ObjectTypes);
        Assert.Equal("tickets", config.ObjectTypes[0]);
        Assert.Equal(100, config.BatchSize);
        Assert.Empty(config.CustomProperties);
        Assert.Empty(config.Pipelines);
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

    // --- ParseHubSpotDate tests ---

    [Fact]
    public void ParseHubSpotDate_Iso8601_ParsesCorrectly()
    {
        var result = HubSpotConnectorClient.ParseHubSpotDate("2026-03-15T10:30:00Z");
        Assert.Equal(new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ParseHubSpotDate_UnixMilliseconds_ParsesCorrectly()
    {
        var expected = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ms = expected.ToUnixTimeMilliseconds().ToString();
        var result = HubSpotConnectorClient.ParseHubSpotDate(ms);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseHubSpotDate_NullOrEmpty_ReturnsUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var result = HubSpotConnectorClient.ParseHubSpotDate(null);
        Assert.True(result >= before);

        result = HubSpotConnectorClient.ParseHubSpotDate("");
        Assert.True(result >= before);
    }

    // --- ResolveObjectTypes tests ---

    [Fact]
    public void ResolveObjectTypes_DefaultConfig_ReturnsTickets()
    {
        var config = new HubSpotSourceConfig { PortalId = "1" };
        var types = HubSpotConnectorClient.ResolveObjectTypes(config);
        Assert.Single(types);
        Assert.Equal("tickets", types[0]);
    }

    [Fact]
    public void ResolveObjectTypes_FiltersUnsupported()
    {
        var config = new HubSpotSourceConfig
        {
            PortalId = "1",
            ObjectTypes = ["tickets", "invalid_type", "deals"],
        };
        var types = HubSpotConnectorClient.ResolveObjectTypes(config);
        Assert.Equal(2, types.Count);
        Assert.Contains("tickets", types);
        Assert.Contains("deals", types);
    }

    [Fact]
    public void ResolveObjectTypes_EmptyConfig_DefaultsToTickets()
    {
        var config = new HubSpotSourceConfig
        {
            PortalId = "1",
            ObjectTypes = [],
        };
        var types = HubSpotConnectorClient.ResolveObjectTypes(config);
        Assert.Single(types);
        Assert.Equal("tickets", types[0]);
    }

    // --- ResolveProperties tests ---

    [Fact]
    public void ResolveProperties_Tickets_ReturnsDefaults()
    {
        var config = new HubSpotSourceConfig { PortalId = "1" };
        var props = HubSpotConnectorClient.ResolveProperties(config, "tickets");
        Assert.Contains("subject", props);
        Assert.Contains("content", props);
        Assert.Contains("hs_ticket_priority", props);
    }

    [Fact]
    public void ResolveProperties_CustomProperties_OverridesDefaults()
    {
        var config = new HubSpotSourceConfig
        {
            PortalId = "1",
            CustomProperties = ["custom_field_1", "custom_field_2"],
        };
        var props = HubSpotConnectorClient.ResolveProperties(config, "tickets");
        Assert.Equal(2, props.Count);
        Assert.Equal("custom_field_1", props[0]);
    }

    // --- MapObjectToCanonical tests ---

    [Fact]
    public void MapObjectToCanonical_Ticket_MapsCorrectly()
    {
        var obj = new HubSpotObject
        {
            Id = "123",
            Properties = new Dictionary<string, string?>
            {
                ["subject"] = "Login Issue",
                ["content"] = "User cannot login after password reset",
                ["hs_pipeline"] = "support",
                ["hs_pipeline_stage"] = "open",
                ["hs_ticket_priority"] = "HIGH",
                ["hs_ticket_category"] = "Authentication",
                ["createdate"] = "2026-01-15T10:00:00Z",
                ["hs_lastmodifieddate"] = "2026-03-10T14:00:00Z",
                ["hubspot_owner_id"] = "owner-1",
            },
            CreatedAt = DateTimeOffset.Parse("2026-01-15T10:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-03-10T14:00:00Z"),
        };

        var record = HubSpotConnectorClient.MapObjectToCanonical(obj, "tickets", "99", "tenant-1");

        Assert.NotNull(record);
        Assert.Equal("hubspot-ticket-123", record.EvidenceId);
        Assert.Equal(ConnectorType.HubSpot, record.SourceSystem);
        Assert.Equal(SourceType.Ticket, record.SourceType);
        Assert.Equal("Login Issue", record.Title);
        Assert.Equal("User cannot login after password reset", record.TextContent);
        Assert.Equal("P1", record.Severity);
        Assert.Equal("Authentication", record.ProductArea);
        Assert.Equal("owner-1", record.Author);
        Assert.Contains("tickets", record.Tags);
        Assert.Contains("open", record.Tags);
        Assert.Equal(AccessVisibility.Internal, record.Permissions.Visibility);
        Assert.Equal("Internal", record.AccessLabel);
        Assert.Contains("/contacts/99/ticket/123", record.SourceLocator.Url);
        Assert.Equal("support", record.SourceLocator.PipelineId);
        Assert.Equal(64, record.ContentHash.Length);
    }

    [Fact]
    public void MapObjectToCanonical_Contact_MapsCorrectly()
    {
        var obj = new HubSpotObject
        {
            Id = "456",
            Properties = new Dictionary<string, string?>
            {
                ["firstname"] = "Jane",
                ["lastname"] = "Doe",
                ["email"] = "jane@example.com",
                ["company"] = "Acme Inc",
                ["jobtitle"] = "Engineer",
                ["lifecyclestage"] = "customer",
                ["createdate"] = "2026-02-01T00:00:00Z",
                ["lastmodifieddate"] = "2026-03-01T00:00:00Z",
            },
        };

        var record = HubSpotConnectorClient.MapObjectToCanonical(obj, "contacts", "99", "tenant-1");

        Assert.NotNull(record);
        Assert.Equal("hubspot-contact-456", record.EvidenceId);
        Assert.Equal(SourceType.Document, record.SourceType);
        Assert.Equal("Jane Doe", record.Title);
        Assert.Contains("Email: jane@example.com", record.TextContent);
        Assert.Contains("Company: Acme Inc", record.TextContent);
    }

    [Fact]
    public void MapObjectToCanonical_Deal_MapsCorrectly()
    {
        var obj = new HubSpotObject
        {
            Id = "789",
            Properties = new Dictionary<string, string?>
            {
                ["dealname"] = "Enterprise License",
                ["description"] = "Annual enterprise deal",
                ["dealstage"] = "closedwon",
                ["pipeline"] = "default",
                ["createdate"] = "2026-01-01T00:00:00Z",
                ["hs_lastmodifieddate"] = "2026-03-15T00:00:00Z",
            },
        };

        var record = HubSpotConnectorClient.MapObjectToCanonical(obj, "deals", "99", "tenant-1");

        Assert.NotNull(record);
        Assert.Equal("hubspot-deal-789", record.EvidenceId);
        Assert.Equal(EvidenceStatus.Closed, record.Status);
        Assert.Equal("Enterprise License", record.Title);
    }

    [Fact]
    public void MapObjectToCanonical_Company_MapsCorrectly()
    {
        var obj = new HubSpotObject
        {
            Id = "321",
            Properties = new Dictionary<string, string?>
            {
                ["name"] = "Contoso Ltd",
                ["description"] = "Enterprise customer",
                ["createdate"] = "2026-01-15T00:00:00Z",
                ["hs_lastmodifieddate"] = "2026-03-10T00:00:00Z",
            },
        };

        var record = HubSpotConnectorClient.MapObjectToCanonical(obj, "companies", "99", "tenant-1");

        Assert.NotNull(record);
        Assert.Equal("hubspot-company-321", record.EvidenceId);
        Assert.Equal(SourceType.Document, record.SourceType);
        Assert.Equal("Contoso Ltd", record.Title);
        Assert.Equal("Enterprise customer", record.TextContent);
    }

    [Theory]
    [InlineData("tickets", "ticket")]
    [InlineData("contacts", "contact")]
    [InlineData("companies", "company")]
    [InlineData("deals", "deal")]
    [InlineData("widgets", "widget")]
    public void SingularizeObjectType_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, HubSpotConnectorClient.SingularizeObjectType(input));
    }

    [Fact]
    public void MapObjectToCanonical_NullProperties_ReturnsNull()
    {
        var obj = new HubSpotObject { Id = "1", Properties = null };
        var record = HubSpotConnectorClient.MapObjectToCanonical(obj, "tickets", "99", "t1");
        Assert.Null(record);
    }

    [Fact]
    public void MapObjectToCanonical_Priority_Mapping()
    {
        var high = CreateTicketObject("1", priority: "HIGH");
        var medium = CreateTicketObject("2", priority: "MEDIUM");
        var low = CreateTicketObject("3", priority: "LOW");

        Assert.Equal("P1", HubSpotConnectorClient.MapObjectToCanonical(high, "tickets", "99", "t1")!.Severity);
        Assert.Equal("P2", HubSpotConnectorClient.MapObjectToCanonical(medium, "tickets", "99", "t1")!.Severity);
        Assert.Equal("P3", HubSpotConnectorClient.MapObjectToCanonical(low, "tickets", "99", "t1")!.Severity);
    }

    [Fact]
    public void MapObjectToCanonical_ClosedStage_MapsToClosed()
    {
        var closed = CreateTicketObject("1", stage: "closed");
        var closedWon = CreateTicketObject("2", stage: "closedwon");
        var open = CreateTicketObject("3", stage: "open");

        Assert.Equal(EvidenceStatus.Closed, HubSpotConnectorClient.MapObjectToCanonical(closed, "tickets", "99", "t1")!.Status);
        Assert.Equal(EvidenceStatus.Closed, HubSpotConnectorClient.MapObjectToCanonical(closedWon, "deals", "99", "t1")!.Status);
        Assert.Equal(EvidenceStatus.Open, HubSpotConnectorClient.MapObjectToCanonical(open, "tickets", "99", "t1")!.Status);
    }

    // --- Checkpoint tests ---

    [Fact]
    public void HubSpotCheckpoint_Roundtrip()
    {
        var ts = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);
        var cp = new HubSpotCheckpoint(1, "abc123", ts);

        var serialized = cp.Serialize();
        var parsed = HubSpotCheckpoint.Parse(serialized);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.ObjectTypeIndex);
        Assert.Equal("abc123", parsed.AfterCursor);
        Assert.NotNull(parsed.LastModified);
        Assert.Equal(ts, parsed.LastModified.Value);
    }

    [Fact]
    public void HubSpotCheckpoint_Parse_NullInput_ReturnsNull()
    {
        Assert.Null(HubSpotCheckpoint.Parse(null));
        Assert.Null(HubSpotCheckpoint.Parse(""));
        Assert.Null(HubSpotCheckpoint.Parse("   "));
    }

    [Fact]
    public void HubSpotCheckpoint_Parse_InvalidFormat_ReturnsNull()
    {
        Assert.Null(HubSpotCheckpoint.Parse("not-valid"));
        Assert.Null(HubSpotCheckpoint.Parse("abc|cursor|2026-01-01"));
    }

    [Fact]
    public void HubSpotCheckpoint_Serialize_NoCursor()
    {
        var cp = new HubSpotCheckpoint(0, null, null);
        var serialized = cp.Serialize();
        Assert.Equal("0||", serialized);

        var parsed = HubSpotCheckpoint.Parse(serialized);
        Assert.NotNull(parsed);
        Assert.Null(parsed.AfterCursor);
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
        var handler = new MockHttpHandler(HttpStatusCode.OK, """{"portalId":12345,"uiDomain":"app.hubspot.com"}""");
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();

        var result = await client.TestConnectionAsync("t1", config, "my-token");

        Assert.True(result.Success);
        Assert.Contains("Successfully connected", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_Unauthorized()
    {
        var handler = new MockHttpHandler(HttpStatusCode.Unauthorized, """{"status":"error","message":"Unauthorized"}""");
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
    public async Task FetchAsync_Tickets_BackfillMode()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:crm/v3/objects/tickets"] = (HttpStatusCode.OK, $$"""
                {
                    "results": [
                        {
                            "id": "101",
                            "properties": {
                                "subject": "Cannot login",
                                "content": "User reports login failure",
                                "hs_pipeline_stage": "open",
                                "hs_ticket_priority": "HIGH",
                                "createdate": "2026-01-01T00:00:00Z",
                                "hs_lastmodifieddate": "2026-03-01T00:00:00Z"
                            },
                            "createdAt": "2026-01-01T00:00:00Z",
                            "updatedAt": "2026-03-01T00:00:00Z"
                        },
                        {
                            "id": "102",
                            "properties": {
                                "subject": "Billing question",
                                "content": "Charged twice",
                                "hs_pipeline_stage": "closed",
                                "hs_ticket_priority": "MEDIUM",
                                "createdate": "2026-02-01T00:00:00Z",
                                "hs_lastmodifieddate": "2026-03-10T00:00:00Z"
                            },
                            "createdAt": "2026-02-01T00:00:00Z",
                            "updatedAt": "2026-03-10T00:00:00Z"
                        }
                    ],
                    "paging": null
                }
            """),
        };

        var config = CreateSourceConfigJson();
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.Equal(2, result.Records.Count);
        Assert.False(result.HasMore);
        Assert.Empty(result.Errors);

        var first = result.Records[0];
        Assert.Equal("hubspot-ticket-101", first.EvidenceId);
        Assert.Equal("Cannot login", first.Title);
        Assert.Equal(ConnectorType.HubSpot, first.SourceSystem);
        Assert.Equal(SourceType.Ticket, first.SourceType);

        var second = result.Records[1];
        Assert.Equal("hubspot-ticket-102", second.EvidenceId);
        Assert.Equal(EvidenceStatus.Closed, second.Status);
    }

    [Fact]
    public async Task FetchAsync_WithPagination_SetsHasMore()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:crm/v3/objects/tickets"] = (HttpStatusCode.OK, """
                {
                    "results": [
                        {
                            "id": "201",
                            "properties": {
                                "subject": "Test",
                                "content": "Content",
                                "createdate": "2026-01-01T00:00:00Z",
                                "hs_lastmodifieddate": "2026-01-01T00:00:00Z"
                            }
                        }
                    ],
                    "paging": {
                        "next": {
                            "after": "cursor-page2",
                            "link": "https://api.hubapi.com/crm/v3/objects/tickets?after=cursor-page2"
                        }
                    }
                }
            """),
        };

        var config = CreateSourceConfigJson();
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.True(result.HasMore);
        Assert.NotNull(result.NewCheckpoint);

        // Verify checkpoint contains the cursor.
        var cp = HubSpotCheckpoint.Parse(result.NewCheckpoint);
        Assert.NotNull(cp);
        Assert.Equal("cursor-page2", cp.AfterCursor);
    }

    [Fact]
    public async Task FetchAsync_IncrementalMode_UsesSearchApi()
    {
        // When there's a lastModified checkpoint, the client should use the search API.
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:crm/v3/objects/tickets/search"] = (HttpStatusCode.OK, """
                {
                    "results": [
                        {
                            "id": "301",
                            "properties": {
                                "subject": "Updated ticket",
                                "content": "Updated content",
                                "createdate": "2026-01-01T00:00:00Z",
                                "hs_lastmodifieddate": "2026-03-15T00:00:00Z"
                            }
                        }
                    ],
                    "paging": null
                }
            """),
        };

        var config = CreateSourceConfigJson();
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        // Checkpoint with lastModified triggers incremental mode.
        var checkpoint = new HubSpotCheckpoint(0, null, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        var result = await client.FetchAsync("t1", config, null, "token", checkpoint.Serialize(), false);

        Assert.Single(result.Records);
        Assert.Equal("Updated ticket", result.Records[0].Title);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task FetchAsync_MultipleObjectTypes_FetchesAll()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:crm/v3/objects/tickets"] = (HttpStatusCode.OK, """
                {
                    "results": [{"id": "1", "properties": {"subject": "Ticket 1", "content": "", "createdate": "2026-01-01T00:00:00Z", "hs_lastmodifieddate": "2026-01-01T00:00:00Z"}}],
                    "paging": null
                }
            """),
            ["GET:crm/v3/objects/deals"] = (HttpStatusCode.OK, """
                {
                    "results": [{"id": "2", "properties": {"dealname": "Deal 1", "description": "", "createdate": "2026-01-01T00:00:00Z", "hs_lastmodifieddate": "2026-01-01T00:00:00Z"}}],
                    "paging": null
                }
            """),
        };

        var config = CreateSourceConfigJson(objectTypes: ["tickets", "deals"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("hubspot-ticket-1", result.Records[0].EvidenceId);
        Assert.Equal("hubspot-deal-2", result.Records[1].EvidenceId);
    }

    [Fact]
    public async Task FetchAsync_CheckpointProduced_OnCompletion()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:crm/v3/objects/tickets"] = (HttpStatusCode.OK, """
                {
                    "results": [{"id": "1", "properties": {"subject": "T1", "content": "", "createdate": "2026-01-01T00:00:00Z", "hs_lastmodifieddate": "2026-03-15T10:30:00Z"}}],
                    "paging": null
                }
            """),
        };

        var config = CreateSourceConfigJson();
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "token", null, true);

        Assert.NotNull(result.NewCheckpoint);
        Assert.False(result.HasMore);

        var cp = HubSpotCheckpoint.Parse(result.NewCheckpoint);
        Assert.NotNull(cp);
        Assert.Equal(0, cp.ObjectTypeIndex);
        Assert.Null(cp.AfterCursor);
        Assert.NotNull(cp.LastModified);
    }

    // --- PreviewAsync tests ---

    [Fact]
    public async Task PreviewAsync_ReturnsLimitedRecords()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:crm/v3/objects/tickets"] = (HttpStatusCode.OK, """
                {
                    "results": [{"id": "1", "properties": {"subject": "Ticket 1", "content": "Body", "createdate": "2026-01-01T00:00:00Z", "hs_lastmodifieddate": "2026-01-01T00:00:00Z"}}],
                    "paging": null
                }
            """),
        };

        var config = CreateSourceConfigJson();
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var records = await client.PreviewAsync("t1", config, null, "token", 5);

        Assert.Single(records);
        Assert.Equal("Ticket 1", records[0].Title);
    }

    [Fact]
    public async Task PreviewAsync_NoConfig_ReturnsEmpty()
    {
        var client = CreateClient();
        var records = await client.PreviewAsync("t1", null, null, "token", 5);
        Assert.Empty(records);
    }

    // --- SourceConfig model tests ---

    [Fact]
    public void SourceConfig_DefaultsAreCorrect()
    {
        var config = new HubSpotSourceConfig { PortalId = "1" };
        Assert.Equal("https://api.hubapi.com", config.BaseUrl);
        Assert.Single(config.ObjectTypes);
        Assert.Equal("tickets", config.ObjectTypes[0]);
        Assert.Equal(100, config.BatchSize);
        Assert.Empty(config.CustomProperties);
        Assert.Empty(config.Pipelines);
    }

    // --- Helpers ---

    private static HubSpotConnectorClient CreateClient(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = new LoggerFactory().CreateLogger<HubSpotConnectorClient>();
        return new HubSpotConnectorClient(factory, logger);
    }

    private static string CreateSourceConfigJson(
        string portalId = "12345",
        IReadOnlyList<string>? objectTypes = null)
    {
        return JsonSerializer.Serialize(new HubSpotSourceConfig
        {
            PortalId = portalId,
            ObjectTypes = objectTypes ?? ["tickets"],
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // --- MapPriority tests ---

    [Theory]
    [InlineData("HIGH", "P1")]
    [InlineData("high", "P1")]
    [InlineData("High", "P1")]
    [InlineData("MEDIUM", "P2")]
    [InlineData("medium", "P2")]
    [InlineData("LOW", "P3")]
    [InlineData("low", "P3")]
    [InlineData("Critical", "Critical")]
    [InlineData("other", "other")]
    public void MapPriority_CaseInsensitive(string input, string expected)
    {
        Assert.Equal(expected, HubSpotConnectorClient.MapPriority(input));
    }

    [Fact]
    public void MapPriority_Null_ReturnsNull()
    {
        Assert.Null(HubSpotConnectorClient.MapPriority(null));
    }

    [Fact]
    public void MapPriority_Empty_ReturnsNull()
    {
        Assert.Null(HubSpotConnectorClient.MapPriority(""));
    }

    private static HubSpotObject CreateTicketObject(
        string id, string? priority = null, string? stage = null)
    {
        return new HubSpotObject
        {
            Id = id,
            Properties = new Dictionary<string, string?>
            {
                ["subject"] = $"Ticket {id}",
                ["content"] = "Content",
                ["hs_ticket_priority"] = priority,
                ["hs_pipeline_stage"] = stage ?? "open",
                ["createdate"] = "2026-01-01T00:00:00Z",
                ["hs_lastmodifieddate"] = "2026-01-01T00:00:00Z",
            },
        };
    }
}
