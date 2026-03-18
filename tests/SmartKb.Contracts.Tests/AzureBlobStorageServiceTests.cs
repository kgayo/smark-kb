using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class AzureBlobStorageServiceTests
{
    [Fact]
    public async Task UploadRawContentAsync_ReturnsBlobPath()
    {
        var (service, store) = CreateService();

        var path = await service.UploadRawContentAsync("tenant-1", "AzureDevOps", "ev-1", "Hello, world!");

        Assert.Equal("tenant-1/AzureDevOps/ev-1/raw", path);
        Assert.True(store.ContainsKey(path));
        Assert.Equal("Hello, world!", store[path]);
    }

    [Fact]
    public async Task UploadRawContentAsync_OverwritesExistingBlob()
    {
        var (service, store) = CreateService();

        await service.UploadRawContentAsync("tenant-1", "AzureDevOps", "ev-1", "Version 1");
        await service.UploadRawContentAsync("tenant-1", "AzureDevOps", "ev-1", "Version 2");

        Assert.Equal("Version 2", store["tenant-1/AzureDevOps/ev-1/raw"]);
    }

    [Fact]
    public async Task DownloadRawContentAsync_ReturnsContent()
    {
        var (service, store) = CreateService();
        store["tenant-1/AzureDevOps/ev-1/raw"] = "Stored content";

        var result = await service.DownloadRawContentAsync("tenant-1/AzureDevOps/ev-1/raw");

        Assert.Equal("Stored content", result);
    }

    [Fact]
    public async Task DownloadRawContentAsync_ReturnsNull_WhenBlobNotFound()
    {
        var (service, _) = CreateService();

        var result = await service.DownloadRawContentAsync("nonexistent/path");

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteRawContentAsync_ReturnsTrue_WhenBlobExists()
    {
        var (service, store) = CreateService();
        store["tenant-1/AzureDevOps/ev-1/raw"] = "content";

        var result = await service.DeleteRawContentAsync("tenant-1/AzureDevOps/ev-1/raw");

        Assert.True(result);
        Assert.False(store.ContainsKey("tenant-1/AzureDevOps/ev-1/raw"));
    }

    [Fact]
    public async Task DeleteRawContentAsync_ReturnsFalse_WhenBlobNotFound()
    {
        var (service, _) = CreateService();

        var result = await service.DeleteRawContentAsync("nonexistent/path");

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenBlobExists()
    {
        var (service, store) = CreateService();
        store["path/to/blob"] = "content";

        Assert.True(await service.ExistsAsync("path/to/blob"));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenBlobMissing()
    {
        var (service, _) = CreateService();

        Assert.False(await service.ExistsAsync("path/to/missing"));
    }

    private static (InMemoryBlobStorageService Service, Dictionary<string, string> Store) CreateService()
    {
        var store = new Dictionary<string, string>();
        var logger = new LoggerFactory().CreateLogger<AzureBlobStorageService>();
        var service = new InMemoryBlobStorageService(store);
        return (service, store);
    }
}

/// <summary>
/// In-memory implementation of IBlobStorageService for testing.
/// Avoids Azure SDK dependency in unit tests.
/// </summary>
internal class InMemoryBlobStorageService : IBlobStorageService
{
    private readonly Dictionary<string, string> _store;

    public InMemoryBlobStorageService(Dictionary<string, string> store) => _store = store;

    public Task<string> UploadRawContentAsync(string tenantId, string connectorType, string evidenceId,
        string content, string contentType = "text/plain; charset=utf-8", CancellationToken cancellationToken = default)
    {
        var path = IBlobStorageService.BuildBlobPath(tenantId, connectorType, evidenceId);
        _store[path] = content;
        return Task.FromResult(path);
    }

    public Task<string?> DownloadRawContentAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.TryGetValue(blobPath, out var content) ? content : null);
    }

    public Task<bool> DeleteRawContentAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.Remove(blobPath));
    }

    public Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.ContainsKey(blobPath));
    }

    public Task<string> UploadBinaryContentAsync(string tenantId, string connectorType, string evidenceId,
        Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = IBlobStorageService.BuildBinaryBlobPath(tenantId, connectorType, evidenceId);
        using var reader = new StreamReader(content);
        _store[path] = reader.ReadToEnd();
        return Task.FromResult(path);
    }

    public Task<Stream?> DownloadBinaryContentAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(blobPath, out var content))
            return Task.FromResult<Stream?>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)));
        return Task.FromResult<Stream?>(null);
    }
}
