using System.Collections.Concurrent;
using Sprk.Bff.Api.Services.Ai.Sessions;

namespace Sprk.Bff.Api.Tests.Mocks;

/// <summary>
/// In-memory stand-in for the Azure Blob boundary behind <see cref="SessionFileBlobStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this shape matters.</b> The tenant-isolation tests must be a genuine <i>reachability</i>
/// check, not an assertion about a path string. This gateway therefore behaves the way Azure Blob
/// actually behaves for the operations the store uses: blob names are opaque, <b>ordinal
/// case-sensitive</b> keys, and lookup is exact. There is no path normalisation, no <c>..</c>
/// resolution, no hierarchy — because Blob Storage has none either.
/// </para>
/// <para>
/// The consequence is that whether one tenant can read another tenant's bytes is decided entirely by
/// the blob name the PRODUCTION code computes. Delete the tenant segment from
/// <c>SessionFileBlobStore.BuildBlobName</c> and the cross-tenant read starts succeeding against this
/// gateway, exactly as it would against the real service. That is the property the tests are pinned
/// to. (Same philosophy as <c>ReferenceRetrievalTenantPinTests</c>, which evaluates the OData filter
/// the production code actually produced instead of asserting on its text.)
/// </para>
/// </remarks>
internal sealed class InMemorySessionFileBlobGateway : SessionFileBlobGateway
{
    private readonly ConcurrentDictionary<string, StoredBlob> _blobs = new(StringComparer.Ordinal);

    private sealed record StoredBlob(BinaryData Content, string? ContentType, DateTimeOffset? CreatedOn);

    /// <summary>
    /// Creation timestamp stamped on the next write, so a retention test can age a blob without
    /// waiting. Null (the default) uses <see cref="DateTimeOffset.UtcNow"/>, matching Azure.
    /// </summary>
    public DateTimeOffset? NextWriteCreatedOn { get; set; }

    /// <summary>Every blob name written so far, exactly as the production code composed it.</summary>
    public IReadOnlyList<string> BlobNames => _blobs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public int Count => _blobs.Count;

    /// <summary>
    /// Out-of-band peek by literal blob name. Used by tests to prove that a cross-tenant read missed
    /// because of PARTITIONING, not because the write never happened.
    /// </summary>
    public bool TryPeek(string blobName, out BinaryData? content)
    {
        if (_blobs.TryGetValue(blobName, out var stored))
        {
            content = stored.Content;
            return true;
        }

        content = null;
        return false;
    }

    /// <summary>Seeds a blob directly, bypassing the store — used to plant another tenant's bytes.</summary>
    public void Seed(string blobName, BinaryData content, string? contentType = null, DateTimeOffset? createdOn = null)
        => _blobs[blobName] = new StoredBlob(content, contentType, createdOn ?? DateTimeOffset.UtcNow);

    /// <summary>
    /// When set, the next <see cref="ListAsync"/> throws instead of enumerating. Lets a test drive the
    /// "availability probe failed" branch, which must report UNKNOWN and never "unavailable".
    /// </summary>
    public bool FailNextList { get; set; }

    /// <summary>When set, the next <see cref="DeleteAsync"/> throws instead of deleting.</summary>
    public bool FailNextDelete { get; set; }

    /// <summary>Deletes observed so far, in call order — proves WHAT a retention pass destroyed.</summary>
    public IReadOnlyList<string> DeletedBlobNames => _deleted.ToList();

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _deleted = new();

    /// <summary>
    /// When set, the next <see cref="UploadAsync"/> throws instead of storing. Lets a seam test drive
    /// the endpoint's enabled-but-write-failed branch (which must be a 500, never a lying 202) through
    /// the real wire, rather than asserting it at the store's own API.
    /// </summary>
    public bool FailNextWrite { get; set; }

    public void Clear()
    {
        _blobs.Clear();
        _deleted.Clear();
        FailNextWrite = false;
        FailNextList = false;
        FailNextDelete = false;
        NextWriteCreatedOn = null;
    }

    public override Task UploadAsync(string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken)
    {
        if (FailNextWrite)
        {
            FailNextWrite = false;
            throw new InvalidOperationException("simulated durable-store write failure");
        }

        var createdOn = NextWriteCreatedOn ?? DateTimeOffset.UtcNow;
        NextWriteCreatedOn = null;

        _blobs[blobName] = new StoredBlob(content, contentType, createdOn);
        return Task.CompletedTask;
    }

    public override Task<SessionFileBytes?> DownloadAsync(string blobName, CancellationToken cancellationToken)
        => Task.FromResult(_blobs.TryGetValue(blobName, out var stored)
            ? new SessionFileBytes(stored.Content, stored.ContentType)
            : null);

    /// <summary>
    /// Prefix listing with Azure Blob's actual semantics for the operations the store uses: names are
    /// opaque ORDINAL keys and the prefix match is a plain ordinal <c>StartsWith</c> — there is no path
    /// awareness, no normalisation and no case folding. So whether a listing crosses a tenant boundary
    /// is decided entirely by the prefix the PRODUCTION code composed, which is the property the tenant
    /// suite pins.
    /// </summary>
    public override async IAsyncEnumerable<SessionFileBlobListing> ListAsync(
        string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (FailNextList)
        {
            FailNextList = false;
            throw new InvalidOperationException("simulated durable-store list failure");
        }

        await Task.CompletedTask;

        foreach (var (name, stored) in _blobs.ToArray().OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(prefix) && !name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            yield return new SessionFileBlobListing(
                BlobName: name,
                SizeBytes: stored.Content.ToMemory().Length,
                CreatedOn: stored.CreatedOn);
        }
    }

    public override Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        if (FailNextDelete)
        {
            FailNextDelete = false;
            throw new InvalidOperationException("simulated durable-store delete failure");
        }

        var removed = _blobs.TryRemove(blobName, out _);
        if (removed)
        {
            _deleted.Enqueue(blobName);
        }

        return Task.FromResult(removed);
    }
}
