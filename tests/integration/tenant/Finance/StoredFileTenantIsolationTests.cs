using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Files;

/// <summary>
/// <c>tests/integration/tenant/**</c> — tenant-isolation KEEP category (ADR-038 §2 path #4). The Finance file store (ebilling-r1 task
/// 090; procedure-and-file-model 4.1) names every blob <c>{tenantId}/{parent table}/{parent id}/{file id}</c>; two tenants sharing one
/// storage account (a Model 1 shared stack) must never reach each other's bytes through stage, read, list or delete.
/// </summary>
/// <remarks>
/// Reachability tests, as <c>SessionFileBlobStoreTenantIsolationTests</c>: both tenants run the production <see cref="StoredFileBlobs"/>
/// and <see cref="StoredFileService"/> over ONE <see cref="InMemoryBlobGateway"/> (opaque, exact-match names, like Azure Blob), so
/// whether a read hits is decided only by the names the store builds and accepts.
/// </remarks>
public sealed class StoredFileTenantIsolationTests
{
    private static readonly Guid TenantA = StoredFileRig.Tenant;
    private static readonly Guid TenantB = Guid.Parse("0ff1e000-0000-4000-8000-000000000999");
    private static readonly byte[] SecretA = Encoding.UTF8.GetBytes("%PDF tenant A's invoice");
    private static readonly StoredFileParent Parent = new(StoredFileParentKind.Invoice, Guid.Parse("1a000000-0000-4000-8000-0000000000aa"));

    private static (StoredFileRig A, StoredFileRig B) TwoTenants()
    {
        var shared = new InMemoryBlobGateway();
        return (new StoredFileRig(sharedGateway: shared, tenant: TenantA), new StoredFileRig(sharedGateway: shared, tenant: TenantB));
    }

    private static StoredFileRowSpec Spec(Guid fileId) => new()
    {
        FileId = fileId, Purpose = StoredFilePurpose.InvoicePdf, Parent = Parent, Name = "invoice.pdf", ContentType = "application/pdf",
    };

    [Fact(DisplayName = "Stage puts the tenant first; the same parent and file ids in two tenants are two blobs, neither a conflict for the other")]
    public async Task Stage_TenantFirst_SameIdsDoNotCollide()
    {
        var (a, b) = TwoTenants();
        var file = Guid.NewGuid();

        var stagedA = await a.Service.StageAsync(Spec(file), new MemoryStream(SecretA), CancellationToken.None);
        var stagedB = await b.Service.StageAsync(Spec(file), new MemoryStream(Encoding.UTF8.GetBytes("tenant B")), CancellationToken.None);

        stagedA.Blob!.BlobPath.Should().StartWith($"{TenantA:D}/");
        stagedB.Blob!.BlobPath.Should().StartWith($"{TenantB:D}/");
        stagedB.Blob.Reused.Should().BeFalse();
        a.Gateway.Get("invoice-files", stagedA.Blob.BlobPath)!.Content.Should().Equal(SecretA);
    }

    [Fact(DisplayName = "Must-fire: tenant B's row pointing at tenant A's blob is never served, flipped or read for a job")]
    public async Task OtherTenantsPath_NeverRead()
    {
        var (a, b) = TwoTenants();
        var fileA = Guid.NewGuid();
        a.SeedFile(fileA, Parent, SecretA);
        var pathA = StoredFileBlobName.Build(TenantA, Parent, fileA);

        // B's row carries A's exact name (the same file id, so only the tenant segment differs).
        b.SeedFile(fileA, Parent, SecretA, StoredFileStatus.NotYetServable, blobPathOverride: pathA);
        var before = b.Gateway.StorageCalls;

        (await b.Service.FlipToServableAsync(fileA, CancellationToken.None)).RefusalCode.Should().Be(StoredFileRefusal.RowNotFlippable);
        (await b.Service.OpenForProcessingAsync(fileA, CancellationToken.None)).Outcome.Should().Be(StoredFileProcessingOutcome.Missing);
        var row = await b.Service.ReadRowAsync(fileA, CancellationToken.None);
        (await b.Service.JudgeServableAsync(row!, Guid.NewGuid(), CancellationToken.None)).Outcome.Should().Be(StoredFileDownloadOutcome.NotFound);

        // SeedFile above wrote B's own blob too; nothing read A's.
        b.Gateway.Calls.Skip(before).Should().NotContain(c => c.Contains(pathA, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Must-fire: every blob-level read and delete refuses another tenant's name before any storage call")]
    public async Task BlobLevel_RefusesOtherTenant()
    {
        var (a, b) = TwoTenants();
        var fileA = Guid.NewGuid();
        a.SeedFile(fileA, Parent, SecretA);
        var pathA = StoredFileBlobName.Build(TenantA, Parent, fileA);
        var before = b.Gateway.StorageCalls;

        await b.Invoking(x => x.Blobs.GetScanStateAsync("invoice-files", pathA, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await b.Invoking(x => x.Blobs.VerifyAsync("invoice-files", pathA, null, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await b.Invoking(x => x.Blobs.OpenReadAsync("invoice-files", pathA, default, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await b.Invoking(x => x.Blobs.ReadAllAsync("invoice-files", pathA, default, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await b.Invoking(x => x.Blobs.DeleteAsync("invoice-files", pathA, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();

        b.Gateway.StorageCalls.Should().Be(before);
        a.Gateway.Get("invoice-files", pathA).Should().NotBeNull();
    }

    [Fact(DisplayName = "Must-not-fire: the same blob-level calls succeed for the owning tenant")]
    public async Task BlobLevel_OwnTenantReads()
    {
        var (a, _) = TwoTenants();
        var fileA = Guid.NewGuid();
        a.SeedFile(fileA, Parent, SecretA);
        var pathA = StoredFileBlobName.Build(TenantA, Parent, fileA);

        (await a.Blobs.GetScanStateAsync("invoice-files", pathA, CancellationToken.None))!.Value.State.Should().Be(StoredFileScanState.Clean);
        (await a.Blobs.DeleteAsync("invoice-files", pathA, CancellationToken.None)).Should().BeTrue();
    }

    [Fact(DisplayName = "Listing returns only the calling tenant's blobs from a shared container")]
    public async Task Listing_OwnTenantOnly()
    {
        var (a, b) = TwoTenants();
        a.SeedFile(Guid.NewGuid(), Parent, SecretA);
        var fileB = Guid.NewGuid();
        b.SeedFile(fileB, Parent, Encoding.UTF8.GetBytes("B"));

        var listed = new List<StoredBlobListing>();
        await foreach (var item in b.Blobs.ListAllAsync(CancellationToken.None))
        {
            listed.Add(item);
        }

        listed.Should().ContainSingle().Which.FileId.Should().Be(fileB);
        listed.Single().BlobPath.Should().StartWith($"{TenantB:D}/");
    }
}
