using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Services;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(BlobContainerClient container, ILogger<AzureBlobStorageService> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<string> UploadRawContentAsync(
        string tenantId,
        string connectorType,
        string evidenceId,
        string content,
        string contentType = SmartKb.Contracts.CustomMediaTypes.TextPlainUtf8,
        CancellationToken cancellationToken = default)
    {
        var blobPath = IBlobStorageService.BuildBlobPath(tenantId, connectorType, evidenceId);
        var blobClient = _container.GetBlobClient(blobPath);

        var data = new BinaryData(content);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        };

        await blobClient.UploadAsync(data, options, cancellationToken);

        _logger.LogDebug(
            "Uploaded raw content for {EvidenceId} to {BlobPath} ({Length} chars)",
            evidenceId, blobPath, content.Length);

        return blobPath;
    }

    public async Task<string?> DownloadRawContentAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Blob not found at {BlobPath}", blobPath);
            return null;
        }
    }

    public async Task<bool> DeleteRawContentAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        if (response.Value)
            _logger.LogDebug("Deleted blob at {BlobPath}", blobPath);

        return response.Value;
    }

    public async Task<string> UploadBinaryContentAsync(
        string tenantId,
        string connectorType,
        string evidenceId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var blobPath = IBlobStorageService.BuildBinaryBlobPath(tenantId, connectorType, evidenceId);
        var blobClient = _container.GetBlobClient(blobPath);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        };

        await blobClient.UploadAsync(content, options, cancellationToken);

        _logger.LogDebug(
            "Uploaded binary content for {EvidenceId} to {BlobPath} ({ContentType})",
            evidenceId, blobPath, contentType);

        return blobPath;
    }

    public async Task<Stream?> DownloadBinaryContentAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);

        try
        {
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Binary blob not found at {BlobPath}", blobPath);
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }
}
