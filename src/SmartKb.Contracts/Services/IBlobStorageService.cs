namespace SmartKb.Contracts.Services;

/// <summary>
/// Provides blob storage operations for raw ingestion content.
/// Tenant isolation is enforced via path convention: {tenantId}/{connectorType}/{evidenceId}/...
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads raw content to blob storage and returns the blob path.
    /// </summary>
    /// <param name="tenantId">Tenant ID for path scoping.</param>
    /// <param name="connectorType">Connector type (e.g. "AzureDevOps", "SharePoint").</param>
    /// <param name="evidenceId">Unique evidence identifier.</param>
    /// <param name="content">Raw text content to store.</param>
    /// <param name="contentType">MIME content type (default: text/plain).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The blob path relative to the container.</returns>
    Task<string> UploadRawContentAsync(
        string tenantId,
        string connectorType,
        string evidenceId,
        string content,
        string contentType = "text/plain; charset=utf-8",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads raw content from blob storage.
    /// </summary>
    /// <param name="blobPath">The blob path relative to the container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw content string, or null if the blob does not exist.</returns>
    Task<string?> DownloadRawContentAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes raw content from blob storage.
    /// </summary>
    /// <param name="blobPath">The blob path relative to the container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the blob was deleted; false if it did not exist.</returns>
    Task<bool> DeleteRawContentAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads binary content to blob storage and returns the blob path.
    /// </summary>
    Task<string> UploadBinaryContentAsync(
        string tenantId,
        string connectorType,
        string evidenceId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads binary content from blob storage as a stream.
    /// </summary>
    /// <returns>The binary stream, or null if the blob does not exist.</returns>
    Task<Stream?> DownloadBinaryContentAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a blob exists at the given path.
    /// </summary>
    Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the canonical blob path for a given record.
    /// Format: {tenantId}/{connectorType}/{evidenceId}/raw
    /// </summary>
    static string BuildBlobPath(string tenantId, string connectorType, string evidenceId)
        => $"{tenantId}/{connectorType}/{evidenceId}/raw";

    /// <summary>
    /// Builds the blob path for binary content (stored separately from text snapshots).
    /// Format: {tenantId}/{connectorType}/{evidenceId}/binary
    /// </summary>
    static string BuildBinaryBlobPath(string tenantId, string connectorType, string evidenceId)
        => $"{tenantId}/{connectorType}/{evidenceId}/binary";
}
