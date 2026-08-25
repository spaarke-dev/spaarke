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

    private sealed record StoredBlob(BinaryData Content, string? ContentType);

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
    public void Seed(string blobName, BinaryData content, string? contentType = null)
        => _blobs[blobName] = new StoredBlob(content, contentType);

    /// <summary>
    /// When set, the next <see cref="UploadAsync"/> throws instead of storing. Lets a seam test drive
    /// the endpoint's enabled-but-write-failed branch (which must be a 500, never a lying 202) through
    /// the real wire, rather than asserting it at the store's own API.
    /// </summary>
    public bool FailNextWrite { get; set; }

    public void Clear()
    {
        _blobs.Clear();
        FailNextWrite = false;
    }

    public override Task UploadAsync(string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken)
    {
        if (FailNextWrite)
        {
            FailNextWrite = false;
            throw new InvalidOperationException("simulated durable-store write failure");
        }

        _blobs[blobName] = new StoredBlob(content, contentType);
        return Task.CompletedTask;
    }

    public override Task<SessionFileBytes?> DownloadAsync(string blobName, CancellationToken cancellationToken)
        => Task.FromResult(_blobs.TryGetValue(blobName, out var stored)
            ? new SessionFileBytes(stored.Content, stored.ContentType)
            : null);
}
