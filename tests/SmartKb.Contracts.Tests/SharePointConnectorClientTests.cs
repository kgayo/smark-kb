using System.Net;
using System.Text.Json;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class SharePointConnectorClientTests
{
    [Fact]
    public void Type_ReturnsSharePoint()
    {
        var client = CreateClient();
        Assert.Equal(ConnectorType.SharePoint, client.Type);
    }

    // --- ParseSourceConfig tests ---

    [Fact]
    public void ParseSourceConfig_ValidJson_ReturnsConfig()
    {
        var json = JsonSerializer.Serialize(new SharePointSourceConfig
        {
            SiteUrl = "https://contoso.sharepoint.com/sites/support",
            EntraIdTenantId = "aad-tenant-id",
            ClientId = "client-id",
            DriveIds = ["drive-1"],
            BatchSize = 100,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var config = SharePointConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.Equal("https://contoso.sharepoint.com/sites/support", config.SiteUrl);
        Assert.Equal("aad-tenant-id", config.EntraIdTenantId);
        Assert.Equal("client-id", config.ClientId);
        Assert.Single(config.DriveIds);
        Assert.Equal(100, config.BatchSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void ParseSourceConfig_InvalidInput_ReturnsNull(string? input)
    {
        var result = SharePointConnectorClient.ParseSourceConfig(input);
        Assert.Null(result);
    }

    [Fact]
    public void ParseSourceConfig_DefaultValues()
    {
        var json = """{"siteUrl":"https://x.sharepoint.com/sites/s","entraIdTenantId":"t","clientId":"c"}""";
        var config = SharePointConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.True(config.IngestDocumentLibraries);
        Assert.Equal(200, config.BatchSize);
        Assert.Empty(config.DriveIds);
        Assert.Empty(config.IncludeExtensions);
        Assert.Empty(config.ExcludeFolders);
    }

    // --- ComputeHash tests ---

    [Fact]
    public void ComputeHash_DeterministicOutput()
    {
        var hash1 = SharePointConnectorClient.ComputeHash("test input");
        var hash2 = SharePointConnectorClient.ComputeHash("test input");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_DifferentHashes()
    {
        var hash1 = SharePointConnectorClient.ComputeHash("input A");
        var hash2 = SharePointConnectorClient.ComputeHash("input B");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHex()
    {
        var hash = SharePointConnectorClient.ComputeHash("test");
        Assert.Matches("^[0-9a-f]+$", hash);
        Assert.Equal(64, hash.Length);
    }

    // --- Checkpoint tests ---

    [Fact]
    public void SharePointCheckpoint_Roundtrip()
    {
        var deltaLink = "https://graph.microsoft.com/v1.0/drives/abc/root/delta?token=xyz123";
        var cp = new SharePointCheckpoint(2, deltaLink);

        var serialized = cp.Serialize();
        var parsed = SharePointCheckpoint.Parse(serialized);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.DriveIndex);
        Assert.Equal(deltaLink, parsed.DeltaLink);
    }

    [Fact]
    public void SharePointCheckpoint_Parse_NullInput_ReturnsNull()
    {
        Assert.Null(SharePointCheckpoint.Parse(null));
        Assert.Null(SharePointCheckpoint.Parse(""));
        Assert.Null(SharePointCheckpoint.Parse("   "));
    }

    [Fact]
    public void SharePointCheckpoint_Parse_InvalidFormat_ReturnsNull()
    {
        Assert.Null(SharePointCheckpoint.Parse("not-valid"));
        Assert.Null(SharePointCheckpoint.Parse("abc|deltalink"));
    }

    [Fact]
    public void SharePointCheckpoint_Serialize_NoDeltaLink()
    {
        var cp = new SharePointCheckpoint(0, null);
        var serialized = cp.Serialize();
        Assert.Equal("0|", serialized);

        var parsed = SharePointCheckpoint.Parse(serialized);
        Assert.NotNull(parsed);
        Assert.Null(parsed.DeltaLink);
    }

    [Fact]
    public void SharePointCheckpoint_PreservesPipeInDeltaLink()
    {
        // Delta links from Graph contain query params but no pipes, so this is safe.
        var deltaLink = "https://graph.microsoft.com/v1.0/drives/abc/root/delta?token=a%7Cb";
        var cp = new SharePointCheckpoint(1, deltaLink);
        var parsed = SharePointCheckpoint.Parse(cp.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.DriveIndex);
        Assert.Equal(deltaLink, parsed.DeltaLink);
    }

    // --- TestConnectionAsync tests ---

    [Fact]
    public async Task TestConnectionAsync_NoSourceConfig_ReturnsFalse()
    {
        var client = CreateClient();
        var result = await client.TestConnectionAsync("t1", null, "client-secret");
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
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            // Token acquisition.
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK, """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}"""),
            // Site resolution.
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.OK, """{"id":"site-123","displayName":"Support"}"""),
        };

        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();

        var result = await client.TestConnectionAsync("t1", config, "client-secret");

        Assert.True(result.Success);
        Assert.Contains("Successfully connected", result.Message);
        Assert.Contains("site-123", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_SiteNotFound_ReturnsFalse()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK, """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}"""),
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound"}}"""),
        };

        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.TestConnectionAsync("t1", CreateSourceConfigJson(), "secret");

        Assert.False(result.Success);
        Assert.Contains("site URL", result.Message);
    }

    // --- FetchAsync tests ---

    [Fact]
    public async Task FetchAsync_InvalidConfig_ReturnsError()
    {
        var client = CreateClient();
        var result = await client.FetchAsync("t1", null, null, "secret", null, true);

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
    public async Task FetchAsync_BackfillMode_FetchesDriveItems()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK, """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}"""),
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.OK, """{"id":"site-123"}"""),
            ["GET:sites/site-123/drives"] = (HttpStatusCode.OK, """
                {"value":[{"id":"drive-1","name":"Documents","driveType":"documentLibrary"}]}
            """),
            ["GET:drives/drive-1/root/delta"] = (HttpStatusCode.OK, """
                {
                    "value":[
                        {"id":"item-1","name":"setup-guide.md","webUrl":"https://sp.com/docs/setup-guide.md","size":1024,"createdDateTime":"2026-01-15T10:00:00Z","lastModifiedDateTime":"2026-03-01T14:00:00Z","file":{"mimeType":"text/markdown"},"parentReference":{"path":"/drives/drive-1/root:/guides"},"lastModifiedBy":{"user":{"displayName":"Alice"}}},
                        {"id":"item-2","name":"api-reference.pdf","webUrl":"https://sp.com/docs/api-reference.pdf","size":52400,"createdDateTime":"2026-02-01T09:00:00Z","lastModifiedDateTime":"2026-03-10T11:00:00Z","file":{"mimeType":"application/pdf"},"parentReference":{"path":"/drives/drive-1/root:/docs"}},
                        {"id":"folder-1","name":"Images","folder":{"childCount":5}},
                        {"id":"item-3","name":"photo.jpg","webUrl":"https://sp.com/docs/photo.jpg","size":500000,"file":{"mimeType":"image/jpeg"},"parentReference":{"path":"/drives/drive-1/root:/images"}}
                    ],
                    "@odata.deltaLink":"https://graph.microsoft.com/v1.0/drives/drive-1/root/delta?token=next-token-123"
                }
            """),
        };

        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", CreateSourceConfigJson(), null, "secret", null, true);

        // Should have 2 records: .md and .pdf. Folder skipped, .jpg unsupported.
        Assert.Equal(2, result.Records.Count);
        Assert.False(result.HasMore);
        Assert.NotNull(result.NewCheckpoint);
        Assert.Empty(result.Errors);

        var mdDoc = result.Records.First(r => r.EvidenceId == "sp-drive-1-item-1");
        Assert.Equal("setup-guide", mdDoc.Title);
        Assert.Equal(SourceType.Document, mdDoc.SourceType);
        Assert.Equal(ConnectorType.SharePoint, mdDoc.SourceSystem);
        Assert.Equal("t1", mdDoc.TenantId);
        Assert.Equal(AccessVisibility.Restricted, mdDoc.Permissions.Visibility);
        Assert.Contains("Documents", mdDoc.Permissions.AllowedGroups);
        Assert.Equal("Alice", mdDoc.Author);
        Assert.Contains("guides", mdDoc.ProductArea!);
        Assert.Contains(".md", mdDoc.Tags);
        Assert.Contains("setup-guide.md", mdDoc.SourceLocator.Url);

        var pdfDoc = result.Records.First(r => r.EvidenceId == "sp-drive-1-item-2");
        Assert.Equal("api-reference", pdfDoc.Title);
        Assert.Equal(SourceType.Document, pdfDoc.SourceType);

        // Verify checkpoint has delta link.
        var cp = SharePointCheckpoint.Parse(result.NewCheckpoint);
        Assert.NotNull(cp);
        Assert.Contains("next-token-123", cp.DeltaLink!);
    }

    [Fact]
    public async Task FetchAsync_DeltaTokenExpired_ResetsToFullSync()
    {
        var callCount = 0;
        var handler = new DelegatingMockHandler(request =>
        {
            var url = request.RequestUri?.PathAndQuery ?? "";

            if (url.Contains("oauth2/v2.0/token"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("""{"access_token":"t","token_type":"Bearer","expires_in":3600}""", System.Text.Encoding.UTF8, "application/json"),
                };

            if (url.Contains("sites/contoso.sharepoint.com"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("""{"id":"site-1"}""", System.Text.Encoding.UTF8, "application/json"),
                };

            if (url.Contains("sites/site-1/drives"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("""{"value":[{"id":"d1","name":"Docs"}]}""", System.Text.Encoding.UTF8, "application/json"),
                };

            if (url.Contains("drives/d1/root/delta"))
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call: return 410 Gone (delta token expired).
                    return new HttpResponseMessage(HttpStatusCode.Gone);
                }
                // Second call: return items (full sync).
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("""
                        {"value":[{"id":"i1","name":"doc.txt","size":100,"file":{"mimeType":"text/plain"},"parentReference":{"path":"/drives/d1/root:"}}],
                        "@odata.deltaLink":"https://graph.microsoft.com/v1.0/drives/d1/root/delta?token=new-token"}
                    """, System.Text.Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = CreateClient(handler);
        var oldCheckpoint = "0|https://graph.microsoft.com/v1.0/drives/d1/root/delta?token=expired-token";

        var result = await client.FetchAsync("t1", CreateSourceConfigJson(), null, "secret", oldCheckpoint, false);

        Assert.Single(result.Records);
        Assert.Equal("sp-d1-i1", result.Records[0].EvidenceId);
        // Delta was called twice: once with expired token (410), once fresh.
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task FetchAsync_ExcludesFolders()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK, """{"access_token":"t","token_type":"Bearer","expires_in":3600}"""),
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.OK, """{"id":"site-1"}"""),
            ["GET:sites/site-1/drives"] = (HttpStatusCode.OK, """{"value":[{"id":"d1","name":"Docs"}]}"""),
            ["GET:drives/d1/root/delta"] = (HttpStatusCode.OK, """
                {"value":[
                    {"id":"i1","name":"included.txt","size":50,"file":{"mimeType":"text/plain"},"parentReference":{"path":"/drives/d1/root:/docs"}},
                    {"id":"i2","name":"excluded.txt","size":50,"file":{"mimeType":"text/plain"},"parentReference":{"path":"/drives/d1/root:/archive/old"}}
                ],
                "@odata.deltaLink":"https://graph.microsoft.com/v1.0/drives/d1/root/delta?token=t1"}
            """),
        };

        var config = CreateSourceConfigJson(excludeFolders: ["archive"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "secret", null, true);

        Assert.Single(result.Records);
        Assert.Equal("sp-d1-i1", result.Records[0].EvidenceId);
    }

    [Fact]
    public async Task FetchAsync_IncludeExtensionsFilter()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK, """{"access_token":"t","token_type":"Bearer","expires_in":3600}"""),
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.OK, """{"id":"site-1"}"""),
            ["GET:sites/site-1/drives"] = (HttpStatusCode.OK, """{"value":[{"id":"d1","name":"Docs"}]}"""),
            ["GET:drives/d1/root/delta"] = (HttpStatusCode.OK, """
                {"value":[
                    {"id":"i1","name":"readme.md","size":50,"file":{"mimeType":"text/markdown"},"parentReference":{"path":"/drives/d1/root:"}},
                    {"id":"i2","name":"data.csv","size":50,"file":{"mimeType":"text/csv"},"parentReference":{"path":"/drives/d1/root:"}},
                    {"id":"i3","name":"report.pdf","size":50,"file":{"mimeType":"application/pdf"},"parentReference":{"path":"/drives/d1/root:"}}
                ],
                "@odata.deltaLink":"https://graph.microsoft.com/v1.0/drives/d1/root/delta?token=t1"}
            """),
        };

        var config = CreateSourceConfigJson(includeExtensions: [".md", ".pdf"]);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "secret", null, true);

        Assert.Equal(2, result.Records.Count);
        Assert.Contains(result.Records, r => r.EvidenceId == "sp-d1-i1");
        Assert.Contains(result.Records, r => r.EvidenceId == "sp-d1-i3");
    }

    [Fact]
    public async Task FetchAsync_SkipsDeletedItems()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK, """{"access_token":"t","token_type":"Bearer","expires_in":3600}"""),
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.OK, """{"id":"site-1"}"""),
            ["GET:sites/site-1/drives"] = (HttpStatusCode.OK, """{"value":[{"id":"d1","name":"Docs"}]}"""),
            ["GET:drives/d1/root/delta"] = (HttpStatusCode.OK, """
                {"value":[
                    {"id":"i1","name":"active.txt","size":50,"file":{"mimeType":"text/plain"},"parentReference":{"path":"/drives/d1/root:"}},
                    {"id":"i2","name":"deleted.txt","size":50,"file":{"mimeType":"text/plain"},"deleted":{"state":"deleted"},"parentReference":{"path":"/drives/d1/root:"}}
                ],
                "@odata.deltaLink":"https://graph.microsoft.com/v1.0/drives/d1/root/delta?token=t1"}
            """),
        };

        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", CreateSourceConfigJson(), null, "secret", null, true);

        Assert.Single(result.Records);
        Assert.Equal("sp-d1-i1", result.Records[0].EvidenceId);
    }

    // --- PreviewAsync tests ---

    [Fact]
    public async Task PreviewAsync_ReturnsLimitedRecords()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK, """{"access_token":"t","token_type":"Bearer","expires_in":3600}"""),
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.OK, """{"id":"site-1"}"""),
            ["GET:sites/site-1/drives"] = (HttpStatusCode.OK, """{"value":[{"id":"d1","name":"Docs"}]}"""),
            ["GET:drives/d1/root/delta"] = (HttpStatusCode.OK, """
                {"value":[
                    {"id":"i1","name":"a.txt","size":10,"file":{"mimeType":"text/plain"},"parentReference":{"path":"/drives/d1/root:"}},
                    {"id":"i2","name":"b.txt","size":10,"file":{"mimeType":"text/plain"},"parentReference":{"path":"/drives/d1/root:"}},
                    {"id":"i3","name":"c.txt","size":10,"file":{"mimeType":"text/plain"},"parentReference":{"path":"/drives/d1/root:"}}
                ],
                "@odata.deltaLink":"https://graph.microsoft.com/v1.0/drives/d1/root/delta?token=t1"}
            """),
        };

        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var records = await client.PreviewAsync("t1", CreateSourceConfigJson(), null, "secret", 2);

        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task PreviewAsync_NoConfig_ReturnsEmpty()
    {
        var client = CreateClient();
        var records = await client.PreviewAsync("t1", null, null, "secret", 5);
        Assert.Empty(records);
    }

    // --- SourceConfig model tests ---

    [Fact]
    public void SourceConfig_DefaultsAreCorrect()
    {
        var config = new SharePointSourceConfig
        {
            SiteUrl = "https://x.sharepoint.com/sites/s",
            EntraIdTenantId = "t",
            ClientId = "c",
        };
        Assert.True(config.IngestDocumentLibraries);
        Assert.Equal(200, config.BatchSize);
        Assert.Empty(config.DriveIds);
        Assert.Empty(config.IncludeExtensions);
        Assert.Empty(config.ExcludeFolders);
    }

    // --- DownloadAndExtractText tests ---

    [Fact]
    public async Task DownloadAndExtractText_TextFile_DownloadsAndSetsContent()
    {
        var handler = new DelegatingMockHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/content"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("# README\nThis is a text file."),
                };
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = CreateClient(handler);
        var httpClient = new TestHttpClientFactory(handler).CreateClient("SharePoint");
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");

        var item = new GraphDriveItem { Id = "item1", Name = "readme.md", Size = 100 };
        var record = CreateTestRecord("sp-drive1-item1", "[metadata only]");

        var result = await client.DownloadAndExtractTextAsync(httpClient, "drive1", item, record, CancellationToken.None);

        Assert.Equal("# README\nThis is a text file.", result.TextContent);
    }

    [Fact]
    public async Task DownloadAndExtractText_BinaryPdf_ExtractsText()
    {
        var pdfBytes = CreateSimplePdf("Extracted PDF content");
        var handler = new DelegatingMockHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/content"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pdfBytes),
                };
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var extractor = new SmartKb.Contracts.Services.TextExtractionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SmartKb.Contracts.Services.TextExtractionService>.Instance);
        var client = CreateClientWithExtractor(handler, extractor);
        var httpClient = new TestHttpClientFactory(handler).CreateClient("SharePoint");

        var item = new GraphDriveItem { Id = "item2", Name = "report.pdf", Size = pdfBytes.Length };
        var record = CreateTestRecord("sp-drive1-item2", "[metadata only]");

        var result = await client.DownloadAndExtractTextAsync(httpClient, "drive1", item, record, CancellationToken.None);

        Assert.Contains("Extracted PDF content", result.TextContent);
    }

    [Fact]
    public async Task DownloadAndExtractText_DownloadFails_FallsBackToMetadata()
    {
        var handler = new DelegatingMockHandler(req =>
            new HttpResponseMessage(HttpStatusCode.Forbidden));

        var client = CreateClient(handler);
        var httpClient = new TestHttpClientFactory(handler).CreateClient("SharePoint");

        var item = new GraphDriveItem { Id = "item3", Name = "secret.pdf", Size = 500 };
        var record = CreateTestRecord("sp-drive1-item3", "[metadata only]");

        var result = await client.DownloadAndExtractTextAsync(httpClient, "drive1", item, record, CancellationToken.None);

        Assert.Equal("[metadata only]", result.TextContent);
    }

    [Fact]
    public async Task DownloadAndExtractText_UnsupportedExtension_KeepsMetadata()
    {
        var client = CreateClient();
        var httpClient = new TestHttpClientFactory(
            new MockHttpHandler(HttpStatusCode.OK, "test")).CreateClient("SharePoint");

        var item = new GraphDriveItem { Id = "item4", Name = "image.png", Size = 1024 };
        var record = CreateTestRecord("sp-drive1-item4", "[metadata only]");

        var result = await client.DownloadAndExtractTextAsync(httpClient, "drive1", item, record, CancellationToken.None);

        Assert.Equal("[metadata only]", result.TextContent);
    }

    [Fact]
    public async Task DownloadAndExtractText_CorruptBinary_FallsBackToMetadata()
    {
        var handler = new DelegatingMockHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/content"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 0x00, 0x01, 0x02 }),
                };
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var extractor = new SmartKb.Contracts.Services.TextExtractionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SmartKb.Contracts.Services.TextExtractionService>.Instance);
        var client = CreateClientWithExtractor(handler, extractor);
        var httpClient = new TestHttpClientFactory(handler).CreateClient("SharePoint");

        var item = new GraphDriveItem { Id = "item5", Name = "corrupt.docx", Size = 3 };
        var record = CreateTestRecord("sp-drive1-item5", "[metadata only]");

        var result = await client.DownloadAndExtractTextAsync(httpClient, "drive1", item, record, CancellationToken.None);

        Assert.Equal("[metadata only]", result.TextContent);
    }

    // --- Helpers ---

    private static SharePointConnectorClient CreateClient(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SharePointConnectorClient>.Instance;
        var extractor = new NullTextExtractionService();
        return new SharePointConnectorClient(factory, extractor, logger);
    }

    private static SharePointConnectorClient CreateClientWithExtractor(
        HttpMessageHandler handler, SmartKb.Contracts.Services.ITextExtractionService extractor)
    {
        var factory = new TestHttpClientFactory(handler);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SharePointConnectorClient>.Instance;
        return new SharePointConnectorClient(factory, extractor, logger);
    }

    private static CanonicalRecord CreateTestRecord(string evidenceId, string textContent)
    {
        return new CanonicalRecord
        {
            TenantId = "tenant-1",
            EvidenceId = evidenceId,
            SourceSystem = ConnectorType.SharePoint,
            SourceType = SourceType.Document,
            SourceLocator = new SourceLocator("id", "https://example.com"),
            Title = "Test",
            TextContent = textContent,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = EvidenceStatus.Open,
            Permissions = new RecordPermissions(AccessVisibility.Internal, []),
            ContentHash = "hash",
            AccessLabel = "Internal",
        };
    }

    private static byte[] CreateSimplePdf(string text)
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(
            UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(72, 700), font);
        return builder.Build();
    }

    private static string CreateSourceConfigJson(
        string siteUrl = "https://contoso.sharepoint.com/sites/support",
        IReadOnlyList<string>? excludeFolders = null,
        IReadOnlyList<string>? includeExtensions = null)
    {
        return JsonSerializer.Serialize(new SharePointSourceConfig
        {
            SiteUrl = siteUrl,
            EntraIdTenantId = "aad-tenant-123",
            ClientId = "app-client-id",
            ExcludeFolders = excludeFolders ?? [],
            IncludeExtensions = includeExtensions ?? [],
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

/// <summary>
/// Allows custom per-request response logic via a delegate.
/// </summary>
internal class DelegatingMockHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public DelegatingMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}
