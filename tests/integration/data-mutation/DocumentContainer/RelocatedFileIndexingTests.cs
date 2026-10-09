// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: the PublicContracts facade through which a relocation re-keys the RAG index (unified-access-
//     control-r2 task 166 f1-v1, owner round 37 item 1). Each test pins what is written or DELETED in the index and in
//     which order: the new item's index job is enqueued BEFORE any old chunk is removed; nothing is removed when the
//     enqueue fails or indexing is switched off; only this document's chunks while the old item is another record's file.
//
// Doubles are module boundaries only: IPostUploadIndexingEnqueuer (the existing indexing seam) and IRagService (the index).
// The #1510 probe (task 177) runs the real enqueuer into the real RagIndexingJobHandler; its doubles are Service Bus
// (JobSubmissionService), Dataverse (IDataverseService), the idempotency store and the file pipeline (IFileIndexingService).

using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using JobStatus = Sprk.Bff.Api.Services.Jobs.JobStatus;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RelocatedFileIndexingTests
{
    private const string Tenant = "tenant-166";
    private static readonly Guid Document = Guid.Parse("4d000000-0000-4000-8000-0000000016f1");

    private readonly Mock<IPostUploadIndexingEnqueuer> _enqueuer = new(MockBehavior.Strict);
    private readonly Mock<IRagService> _rag = new(MockBehavior.Strict);
    private readonly List<string> _order = new();
    private readonly List<(string SpeFileId, string? OnlyFor, string? Index)> _deletes = new();

    private RelocatedFileIndexing Facade(string? tenant = Tenant) => new(
        _enqueuer.Object,
        _rag.Object,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = tenant }).Build(),
        NullLogger<RelocatedFileIndexing>.Instance);

    private static RelocatedFileIndexRequest Request(bool oldItemRemoved = true, string? previousIndex = "spaarke-files-matter-index")
        => new(Document, "b!target", "01NEWITEM", "memo.docx", 1234, "01OLDITEM", oldItemRemoved, previousIndex);

    private void Enqueue(PostUploadIndexingResult result)
        => _enqueuer.Setup(e => e.EnqueueAppOnlyIfApplicableAsync(It.IsAny<PostUploadIndexingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostUploadIndexingRequest _, CancellationToken _) =>
            {
                _order.Add("enqueue");
                return result;
            });

    private void Deletes(Func<string?, Exception?>? faultFor = null)
        => _rag.Setup(r => r.DeleteSupersededFileChunksAsync(Tenant, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string speFileId, string? onlyFor, string? index, CancellationToken _) =>
            {
                _order.Add($"delete {index ?? "(default)"}");
                _deletes.Add((speFileId, onlyFor, index));
                return faultFor?.Invoke(index) is { } fault ? Task.FromException<int>(fault) : Task.FromResult(3);
            });

    [Fact]
    public async Task TheNewItemIsEnqueuedFirst_ThenEveryOldChunkIsRemoved_FromThePreviousAndTheDefaultIndex()
    {
        Enqueue(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        Deletes();

        var outcome = await Facade().ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeTrue();
        _order.Should().Equal("enqueue", "delete spaarke-files-matter-index", "delete (default)");
        _deletes.Should().OnlyContain(d => d.SpeFileId == "01OLDITEM" && d.OnlyFor == null, "the old item is gone: ALL its chunks go");
        _enqueuer.Verify(e => e.EnqueueAppOnlyIfApplicableAsync(It.Is<PostUploadIndexingRequest>(r =>
            r.TenantId == Tenant && r.DriveId == "b!target" && r.ItemId == "01NEWITEM" && r.FileName == "memo.docx"
            && r.DocumentId == Document.ToString("D") && r.Source == RelocatedFileIndexing.SourceTag), It.IsAny<CancellationToken>()), Times.Once,
            "the copy is BFF-written, so the app-only RagIndexing job may read it");
    }

    [Fact]
    public async Task WhileTheOldItemIsAnotherRecordsFile_OnlyThisDocumentsChunksOfItAreRemoved()
    {
        Enqueue(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        Deletes();

        await Facade().ReindexRelocatedFileAsync(Request(oldItemRemoved: false, previousIndex: null));

        _deletes.Should().ContainSingle().Which.Should().Be(("01OLDITEM", Document.ToString("D"), (string?)null));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("FeatureFlagDisabled")]
    [InlineData("MissingTenantId")]
    public async Task WhenTheNewItemIsNotEnqueued_NoOldChunkIsRemoved_AndItIsPending(string why)
    {
        Enqueue(why == "failed" ? PostUploadIndexingResult.Failed("ServiceBusException") : PostUploadIndexingResult.Skipped(why));

        var outcome = await Facade().ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeFalse("the repeat call retries; the document is never made unsearchable by a re-index that will not come");
        _rag.Verify(r => r.DeleteSupersededFileChunksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ANewItemThatIsNotIndexable_StillHasTheOldChunksRemoved()
    {
        Enqueue(PostUploadIndexingResult.Skipped("NonIndexableContentType"));
        Deletes();

        var outcome = await Facade().ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeTrue();
        _deletes.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhenAnOldChunkCannotBeRemoved_ItIsPending()
    {
        Enqueue(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        Deletes(index => index is null ? new InvalidOperationException("2 of 3 superseded chunks could not be deleted") : null);

        var outcome = await Facade().ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeFalse();
        outcome.Detail.Should().Contain("could not be removed");
    }

    [Fact]
    public async Task AnIndexTheAllowListNoLongerAdmits_IsSkipped_TheOthersAreCleaned()
    {
        Enqueue(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        Deletes(index => index is not null
            ? new SdapProblemException("INDEX_NOT_ALLOWED", "Index not allowed", "not on the allow-list", 400)
            : null);

        var outcome = await Facade().ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeTrue();
        _deletes.Select(d => d.Index).Should().Equal("spaarke-files-matter-index", null);
    }

    [Fact]
    public async Task WithRagSwitchedOff_NothingWasEverIndexed_SoItIsSettled()
    {
        Enqueue(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        Deletes(_ => new FeatureDisabledException("ai.rag.disabled", "RAG is off"));

        var outcome = await Facade().ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeTrue();
    }

    /// <summary>
    /// #1510 probe (unified-access-control-r2 task 177): the relocation sends no parent, so the JOB HANDLER — not the
    /// enqueue — must recover it from the document row, or the moved file's new chunks carry no parent and it drops out
    /// of record-scoped search and parent-scoped RAG. Runs the REAL enqueuer (its job captured where Service Bus would
    /// take it) into the REAL handler; the doubles are Dataverse, the idempotency store and the file pipeline.
    /// </summary>
    [Fact]
    public async Task Issue1510_TheRelocatedFile_IsIndexedByTheJobHandler_UnderTheDocumentsParent()
    {
        var matter = Guid.Parse("4d000000-0000-4000-8000-0000000017a1");
        var submitted = new List<JobContract>();
        var jobs = new Mock<JobSubmissionService>(MockBehavior.Loose,
            Options.Create(new ServiceBusOptions { QueueName = "test-jobs" }),
            NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => submitted.Add(job))
            .Returns(Task.CompletedTask);
        var enqueuer = new PostUploadIndexingEnqueuer(
            Mock.Of<IFileIndexingService>(), jobs.Object, Mock.Of<IDocumentDataverseService>(), TestDocumentIndexParentResolver.Over(),
            Options.Create(new AnalysisOptions()), Options.Create(new PostUploadIndexingOptions()),
            NullLogger<PostUploadIndexingEnqueuer>.Instance);
        Deletes();

        var outcome = await new RelocatedFileIndexing(enqueuer, _rag.Object,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Tenant }).Build(),
                NullLogger<RelocatedFileIndexing>.Instance)
            .ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeTrue();
        var job = submitted.Should().ContainSingle().Subject;

        // The document row still names its matter: the relocation moved the file, not the filing.
        var dataverse = new Mock<IDataverseService>();
        var row = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", matter) { Name = "Acme v. Widget" } };
        dataverse.Setup(d => d.RetrieveAsync("sprk_document", Document, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var idempotency = new Mock<IIdempotencyService>();
        idempotency.Setup(i => i.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        FileIndexRequest? indexed = null;
        var files = new Mock<IFileIndexingService>();
        files.Setup(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, CancellationToken>((r, _) => indexed = r)
            .ReturnsAsync(FileIndexingResult.Succeeded(3, TimeSpan.FromSeconds(1), Document.ToString("D")));
        var names = new Mock<ISearchIndexNameResolver>();
        names.Setup(n => n.GetDefaultIndexName()).Returns("spaarke-files-index");

        var result = await new RagIndexingJobHandler(files.Object, idempotency.Object, dataverse.Object, names.Object,
                TestDocumentIndexParentResolver.Over(dataverse.Object), new RagTelemetry(), NullLogger<RagIndexingJobHandler>.Instance)
            .ProcessAsync(job, CancellationToken.None);

        result.Status.Should().Be(JobStatus.Completed);
        indexed.Should().NotBeNull();
        indexed!.ParentEntity.Should().Be(new ParentEntityContext("matter", matter.ToString(), "Acme v. Widget"),
            "the moved file's chunks must stay under the record the document is filed to");
        names.Verify(n => n.ResolveAsync(Document.ToString("D"), "matter", matter.ToString(), It.IsAny<CancellationToken>()), Times.Once,
            "the index routing sees the recovered parent too");
    }

    [Fact]
    public async Task WithNoTenantConfigured_NothingIsEnqueuedOrRemoved_AndItIsPending()
    {
        var outcome = await Facade(tenant: null).ReindexRelocatedFileAsync(Request());

        outcome.Settled.Should().BeFalse();
        _enqueuer.VerifyNoOtherCalls();
        _rag.VerifyNoOtherCalls();
    }
}
