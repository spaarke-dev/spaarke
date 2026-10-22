using System.Runtime.CompilerServices;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Sprk.Bff.Api.Infrastructure.Storage;

/// <summary>
/// The rules every managed-identity blob store in the BFF applies to its configured endpoint (root CLAUDE.md §9, ADR-028):
/// a bare <c>https</c> endpoint URI, never a connection string, account key or SAS.
/// </summary>
/// <remarks>
/// Moved out of <c>SessionFileBlobStore</c> (ebilling-r1 task 090) so the Finance file store refuses exactly what the session
/// store refuses without depending on an AI type (ADR-013). Each store keeps its own exception messages (they name its own
/// configuration key), so the session store's behaviour and messages are unchanged.
/// </remarks>
public static class BlobEndpointRules
{
    /// <summary>
    /// True when <paramref name="blobEndpoint"/> carries a storage secret: a connection string, an account key or a SAS. Such a
    /// value is refused, never used.
    /// </summary>
    public static bool CarriesSecret(string blobEndpoint)
    {
        ArgumentNullException.ThrowIfNull(blobEndpoint);
        var probe = blobEndpoint.Trim();
        return probe.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) ||
               probe.Contains("SharedAccessSignature", StringComparison.OrdinalIgnoreCase) ||
               probe.StartsWith("DefaultEndpointsProtocol=", StringComparison.OrdinalIgnoreCase) ||
               probe.Contains("?sig=", StringComparison.OrdinalIgnoreCase) ||
               probe.Contains("&sig=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a managed-identity blob endpoint: refuses a secret-bearing value, a value that is not an absolute URI and a
    /// scheme other than <c>https</c> (the managed-identity bearer token is sent on this connection).
    /// </summary>
    /// <param name="blobEndpoint">The configured value.</param>
    /// <param name="configKey">The configuration key, named in every refusal so an operator knows what to change.</param>
    /// <exception cref="InvalidOperationException">The value is unusable.</exception>
    public static Uri ParseManagedIdentityEndpoint(string blobEndpoint, string configKey)
    {
        ArgumentNullException.ThrowIfNull(blobEndpoint);
        if (CarriesSecret(blobEndpoint))
        {
            throw new InvalidOperationException(
                $"'{configKey}' must be a bare blob endpoint URI. Connection strings, account keys and SAS tokens are not " +
                "accepted: this store authenticates with managed identity only (root CLAUDE.md §9, ADR-028).");
        }

        if (!Uri.TryCreate(blobEndpoint.Trim(), UriKind.Absolute, out var endpointUri))
        {
            throw new InvalidOperationException(
                $"'{configKey}' is not an absolute URI. Expected the storage account's blob endpoint, e.g. " +
                "https://<account>.blob.core.windows.net");
        }

        if (!string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{configKey}' must use https. A managed-identity access token is sent on this connection; " +
                $"'{endpointUri.Scheme}' would transmit it in cleartext.");
        }

        if (!string.IsNullOrEmpty(endpointUri.Query))
        {
            // A bare endpoint has no query. Anything there is either a SAS the probe above did not recognise or a typo; neither
            // is a value this store should send requests to.
            throw new InvalidOperationException($"'{configKey}' must be a bare blob endpoint URI with no query string.");
        }

        return endpointUri;
    }
}

/// <summary>Properties of one blob, as one <c>Get Blob Properties</c> call reports them.</summary>
/// <param name="ContentLength">Length in bytes.</param>
/// <param name="ContentType">The stored content type, when one was set.</param>
/// <param name="LastModified">Last-Modified: when the blob's content (or metadata) last changed.</param>
/// <param name="CreatedOn">When the blob was created.</param>
/// <param name="ETag">The ETag, used to pin a later read to the content these properties describe.</param>
/// <param name="Metadata">The blob's metadata (keys as stored).</param>
internal sealed record BlobPropertiesInfo(
    long ContentLength,
    string? ContentType,
    DateTimeOffset LastModified,
    DateTimeOffset CreatedOn,
    ETag ETag,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>One row of a flat blob listing.</summary>
/// <param name="ETag">The ETag the listing saw (ebilling-r1 task 090): a later delete can be pinned to the blob this row describes.</param>
internal sealed record BlobListingItem(
    string Name,
    long? SizeBytes,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? LastModified,
    IReadOnlyDictionary<string, string> Metadata,
    ETag? ETag = null);

/// <summary>The answer of a write-once create.</summary>
internal enum BlobCreateOutcome
{
    /// <summary>The blob did not exist and was written.</summary>
    Created,

    /// <summary>A blob already exists under the name; nothing was written (If-None-Match: *).</summary>
    AlreadyExists,
}

/// <summary>
/// The blob boundary: one managed-identity Azure Blob client, addressed by (container, blob name). Callers own their name rules
/// and tenant partitioning; this type owns only the Azure SDK calls and their "not found" mapping.
/// </summary>
/// <remarks>
/// <para>Moved out of <c>SessionFileBlobStore</c> (ebilling-r1 task 090): the session store's gateway is now a thin adapter over
/// this one, and the Finance file store (<c>StoredFileBlobs</c>) uses it directly, so neither depends on the other and no Finance
/// type depends on an AI type (ADR-013).</para>
/// <para>Internal on purpose: it is the stores' collaborator, not a DI seam (ADR-010). Tests substitute it through each store's
/// test constructor. Every "not found" answer is narrowed to <see cref="BlobErrorCode.BlobNotFound"/>: a missing CONTAINER
/// also answers 404, and swallowing that would report an unprovisioned deployment as "no such file".</para>
/// </remarks>
internal abstract class BlobGateway
{
    /// <summary>Writes <paramref name="content"/>, overwriting any blob of that name (the session store's write).</summary>
    public abstract Task UploadAsync(string container, string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="content"/> only when no blob of that name exists (<c>If-None-Match: *</c>), with
    /// <paramref name="metadata"/> in the same request. An existing blob is left unchanged.
    /// </summary>
    public abstract Task<BlobCreateOutcome> CreateIfNotExistsAsync(
        string container, string blobName, Stream content, string? contentType, IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);

    /// <summary>The whole blob and its content type, or <c>null</c> when the blob does not exist.</summary>
    public abstract Task<(BinaryData Content, string? ContentType)?> DownloadContentAsync(string container, string blobName, CancellationToken cancellationToken);

    /// <summary>
    /// A read stream over the blob, or <c>null</c> when it does not exist. With <paramref name="ifMatch"/>, the read is refused
    /// (<see cref="RequestFailedException"/> 412) when the blob changed since that ETag was read.
    /// </summary>
    public abstract Task<Stream?> OpenReadAsync(string container, string blobName, ETag? ifMatch, CancellationToken cancellationToken);

    /// <summary>The blob's properties, or <c>null</c> when it does not exist.</summary>
    public abstract Task<BlobPropertiesInfo?> GetPropertiesAsync(string container, string blobName, CancellationToken cancellationToken);

    /// <summary>The blob's index tags, or <c>null</c> when the blob does not exist.</summary>
    public abstract Task<IReadOnlyDictionary<string, string>?> GetTagsAsync(string container, string blobName, CancellationToken cancellationToken);

    /// <summary>Flat listing under <paramref name="prefix"/>; ordering is not guaranteed.</summary>
    public abstract IAsyncEnumerable<BlobListingItem> ListAsync(string container, string prefix, bool includeMetadata, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one blob with its snapshots. <c>false</c> when it did not exist (deletes are idempotent). With
    /// <paramref name="ifMatch"/>, the delete is refused (<see cref="RequestFailedException"/> 412) when the blob changed since that
    /// ETag was read, so a caller that judged the blob deletes only what it judged.
    /// </summary>
    public abstract Task<bool> DeleteIfExistsAsync(string container, string blobName, ETag? ifMatch, CancellationToken cancellationToken);
}

/// <summary>The managed-identity Azure Blob implementation of <see cref="BlobGateway"/>.</summary>
internal sealed class AzureBlobGateway : BlobGateway
{
    private readonly BlobServiceClient _service;

    /// <summary>Production constructor: a bare endpoint (validated by <see cref="BlobEndpointRules"/>) and the shared managed-identity credential.</summary>
    public AzureBlobGateway(Uri blobEndpoint, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(blobEndpoint);
        ArgumentNullException.ThrowIfNull(credential);
        _service = new BlobServiceClient(blobEndpoint, credential);
    }

    private BlobClient Blob(string container, string blobName) =>
        _service.GetBlobContainerClient(container).GetBlobClient(blobName);

    public override async Task UploadAsync(string container, string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken)
    {
        // No Conditions => overwrite. The session store's re-upload of the same (tenant, session, file) is idempotent.
        var options = new BlobUploadOptions();
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };
        }

        await Blob(container, blobName).UploadAsync(content, options, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<BlobCreateOutcome> CreateIfNotExistsAsync(
        string container, string blobName, Stream content, string? contentType, IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var options = new BlobUploadOptions
        {
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            Metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal),
        };
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };
        }

        try
        {
            await Blob(container, blobName).UploadAsync(content, options, cancellationToken).ConfigureAwait(false);
            return BlobCreateOutcome.Created;
        }
        catch (RequestFailedException ex) when (IsAlreadyExists(ex))
        {
            return BlobCreateOutcome.AlreadyExists;
        }
    }

    /// <summary>
    /// The create's only precondition is <c>If-None-Match: *</c>, so its failure means "a blob of that name exists" whichever way
    /// the service reports it: 409 <c>BlobAlreadyExists</c> for a single-shot Put Blob, or 412 <c>ConditionNotMet</c> when a large
    /// upload commits its block list. The caller then compares the existing blob's hash and length (reuse or a content conflict).
    /// </summary>
    internal static bool IsAlreadyExists(RequestFailedException ex) =>
        ex.ErrorCode == BlobErrorCode.BlobAlreadyExists
        || (ex.Status == 412 && ex.ErrorCode == BlobErrorCode.ConditionNotMet);

    public override async Task<(BinaryData Content, string? ContentType)?> DownloadContentAsync(
        string container, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Blob(container, blobName).DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            return (response.Value.Content, response.Value.Details?.ContentType);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    public override async Task<Stream?> OpenReadAsync(string container, string blobName, ETag? ifMatch, CancellationToken cancellationToken)
    {
        var options = new BlobOpenReadOptions(allowModifications: false);
        if (ifMatch is { } etag)
        {
            options.Conditions = new BlobRequestConditions { IfMatch = etag };
        }

        try
        {
            return await Blob(container, blobName).OpenReadAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    public override async Task<BlobPropertiesInfo?> GetPropertiesAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var properties = (await Blob(container, blobName).GetPropertiesAsync(conditions: null, cancellationToken).ConfigureAwait(false)).Value;
            return new BlobPropertiesInfo(
                properties.ContentLength,
                properties.ContentType,
                properties.LastModified,
                properties.CreatedOn,
                properties.ETag,
                new Dictionary<string, string>(properties.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            // HEAD has no error body, but the service sends x-ms-error-code and the SDK reads it, so a missing CONTAINER
            // (ContainerNotFound) still reaches the caller as a fault.
            return null;
        }
    }

    public override async Task<IReadOnlyDictionary<string, string>?> GetTagsAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var result = (await Blob(container, blobName).GetTagsAsync(conditions: null, cancellationToken).ConfigureAwait(false)).Value;
            return new Dictionary<string, string>(result.Tags ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    public override async IAsyncEnumerable<BlobListingItem> ListAsync(
        string container, string prefix, bool includeMetadata, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var traits = includeMetadata ? BlobTraits.Metadata : BlobTraits.None;
        await foreach (var item in _service.GetBlobContainerClient(container)
            .GetBlobsAsync(traits: traits, states: BlobStates.None, prefix: prefix, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            yield return new BlobListingItem(
                item.Name,
                item.Properties?.ContentLength,
                item.Properties?.CreatedOn,
                item.Properties?.LastModified,
                item.Metadata is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(item.Metadata, StringComparer.Ordinal),
                item.Properties?.ETag);
        }
    }

    public override async Task<bool> DeleteIfExistsAsync(string container, string blobName, ETag? ifMatch, CancellationToken cancellationToken)
    {
        // DeleteIfExists returns false for a blob that is already gone AND for a missing container (the SDK maps both
        // BlobNotFound and ContainerNotFound to false), so "false" means only "nothing was there to delete". A failed If-Match is
        // not mapped: it throws RequestFailedException 412 (ConditionNotMet).
        var conditions = ifMatch is { } etag ? new BlobRequestConditions { IfMatch = etag } : null;
        var response = await Blob(container, blobName)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, conditions, cancellationToken)
            .ConfigureAwait(false);
        return response.Value;
    }
}
