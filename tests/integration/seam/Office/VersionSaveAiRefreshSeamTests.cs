using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;
using DocumentMetadata = Sprk.Bff.Api.Models.Office.DocumentMetadata;
using JobOutcome = Sprk.Bff.Api.Services.Jobs.JobOutcome;
using JobStatus = Sprk.Bff.Api.Services.Jobs.JobStatus;
using OfficeEmailMetadata = Sprk.Bff.Api.Models.Office.EmailMetadata;
using SaveContentType = Sprk.Bff.Api.Models.Office.SaveContentType;
using SaveEntityReference = Sprk.Bff.Api.Models.Office.SaveEntityReference;
using SaveRequest = Sprk.Bff.Api.Models.Office.SaveRequest;

namespace Sprk.Bff.Api.Tests.Seam.Office;

/// <summary>
/// Task 029 (spaarkeai-word-add-in-r1) — a VERSION save re-profiles and re-indexes its <c>sprk_document</c>: the
/// vertical slice from the finalization message to the profile and the search index.
/// </summary>
/// <remarks>
/// <para><b>The failure mode.</b> A version save keeps the first save's document id and SPE item id. The profile job's
/// key (<c>analysis-{documentId}-documentprofile</c>) and the index job's key (<c>rag-index-{driveId}-{itemId}</c>) were
/// built from nothing else, so both handlers answered every re-save "already processed" and the AI profile and Find
/// results described the first version forever. Re-indexing also could not REPLACE chunks: ids <c>{item}_{i}</c>
/// overwrite in place, so a shorter version left the old tail (old text, old document vector) in the index.</para>
/// <para><b>What is proven here</b> (owner decisions 2026-09-15): a new version is re-profiled once and re-indexed once;
/// the index then holds exactly the new version's chunks, in the index the document routes to; a redelivery or retry
/// of the SAME save runs nothing; every genuinely new save refreshes even when its bytes repeat an earlier version
/// (B, A, B); a first save and an Email save keep today's keys and payloads; a failed trim leaves the new chunks in
/// place and the retry finishes the replace.</para>
/// <para><b>Task 048 (spaarkeai-word-add-in-r1) update:</b> this file's own scope statement above used to say a
/// first save and an Email save "never trim". That was 029's deliberate, narrower scope — the trim (§F below,
/// <see cref="PostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync"/>) was opt-in per caller via
/// <c>VersionDiscriminator</c>, and only the Office version-save job set it. Task 048 turned the SAME trim on
/// UNCONDITIONALLY for every producer through that seam — first save and Email save included — per its own
/// acceptance criterion that a first index may either skip the trim call entirely OR run one that deletes
/// nothing; this task chose the latter (see the code comment at the enqueuer). <see cref="FirstSave_AndEmailSave_KeepTodaysKeysAndPayloads_AndTheNowUnconditionalTrimDeletesNothing"/>
/// (renamed from "...AndNeverTrim") is updated accordingly — the key/payload assertions this slice's title
/// promises are otherwise untouched by task 048, which changed no key and no skip/retry behaviour anywhere in
/// this file's scope.</para>
/// <para><b>Production types end to end:</b> <see cref="OfficeJobQueue"/> → <see cref="UploadFinalizationWorker"/> →
/// <see cref="PostUploadIndexingEnqueuer"/> → <see cref="RagIndexingJobHandler"/> → <see cref="FileIndexingService"/>,
/// and <see cref="AppOnlyDocumentAnalysisJobHandler"/>. Doubled only at the boundaries: Service Bus (a sender that
/// records the message; a <see cref="JobSubmissionService"/> that records each job), the Redis-backed stores
/// (<see cref="InMemoryTenantCache"/>, an in-memory <see cref="IIdempotencyService"/>), SPE bytes, text
/// extraction/chunking, the AI analysis call, and the Azure AI Search index (<see cref="InMemoryIndex"/>, whose trim
/// follows the <see cref="IRagService.DeleteChunksBeyondCountAsync"/> contract; the real query against Azure AI Search
/// is pinned by <c>RagServiceChunkTrimTests</c>).</para>
/// <para><b>Dependency on task 047.</b> Until 047 lands, the save spine answers a save whose bytes repeat an earlier
/// save of the same document "Duplicate" before anything is written or finalized, so B-then-A-then-B never reaches
/// this pipeline a third time. The revert test drives finalization with the ProcessingJob that 047 will create for
/// that save; this slice already refreshes for it.</para>
/// </remarks>
public sealed class VersionSaveAiRefreshSeamTests : IDisposable
{
    private const string Tenant = "tenant-029";
    private const string Drive = "b!drive-029";
    private const string Item = "01ITEM029";
    private const string RoutedIndex = "spaarke-files-matter-index";
    private const string DefaultIndex = "spaarke-files-index";

    // Chunks are '|'-separated, so the chunk count of each version is explicit.
    private const string FirstVersion = "v1-a|v1-b|v1-c|v1-d|v1-e";
    private const string VersionB = "B-a|B-b|B-c";
    private const string VersionA = "A-a|A-b|A-c|A-d";

    private static readonly Guid MatterId = Guid.NewGuid();

    private readonly Guid _documentId = Guid.NewGuid();
    private readonly Pipeline _pipeline = new();

    public void Dispose() => _pipeline.Dispose();

    // ── the slice ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VersionSave_WithDifferentContent_QueuesOneProfileAndOneIndex_AndLeavesOnlyTheNewVersionsChunks()
    {
        await SaveFirstVersionAsync();
        _pipeline.ProfileRuns.Should().Equal(_documentId);
        CurrentChunks().Should().HaveCount(5);

        var saveJobId = Guid.NewGuid();
        var jobs = await SaveVersionAsync(saveJobId, VersionB, run: false);

        jobs.Should().HaveCount(2, "one profile job and one index job per version");
        var profileJob = jobs.Should().ContainSingle(j => j.JobType == AppOnlyDocumentAnalysisJobHandler.JobTypeName).Subject;
        profileJob.SubjectId.Should().Be(_documentId.ToString());
        profileJob.IdempotencyKey.Should().Be($"analysis-{_documentId}-documentprofile-version-{saveJobId:N}");
        var indexJob = jobs.Should().ContainSingle(j => j.JobType == RagIndexingJobHandler.JobTypeName).Subject;
        indexJob.IdempotencyKey.Should().Be($"rag-index-{Drive}-{Item}-version-{saveJobId:N}");

        await _pipeline.RunAllAsync(jobs);

        _pipeline.ProfileRuns.Should().Equal(new[] { _documentId, _documentId }, "exactly one re-profile of the same document");
        _pipeline.Index.BatchWrites.Should().Be(2, "exactly one re-index");
        CurrentChunks().Select(c => c.Content).Should().Equal(new[] { "B-a", "B-b", "B-c" },
            "the previous version's chunks 3 and 4 are gone — no orphans, no duplicates");
        CurrentChunks().Should().OnlyContain(c => c.ChunkCount == 3);
        _pipeline.Index.ChunksFor(DefaultIndex, Item).Should().BeEmpty(
            "the version is written to, and trimmed in, the index this document routes to");
    }

    [Fact]
    public async Task SameVersionSave_RedeliveredOrRetried_RunsNoFurtherProfileOrIndex()
    {
        await SaveFirstVersionAsync();
        var jobs = await SaveVersionAsync(Guid.NewGuid(), VersionB);
        var message = _pipeline.FinalizationMessages[^1];
        var profileRuns = _pipeline.ProfileRuns.Count;
        var writes = _pipeline.Index.BatchWrites;
        var trims = _pipeline.Index.TrimCalls;

        // Service Bus redelivers the finalization message to the same instance: the worker's own mark answers it.
        (await _pipeline.DeliverAsync(message, _pipeline.Worker)).Should().BeEmpty();

        // The finalization runs again where its mark is not visible (another instance, an evicted cache): it queues
        // the SAME keys, because the discriminator is the save's, not the run's.
        var requeued = await _pipeline.DeliverAsync(message, _pipeline.NewWorker());
        requeued.Select(j => j.IdempotencyKey).Should().BeEquivalentTo(jobs.Select(j => j.IdempotencyKey));

        // Those, and a plain redelivery of the original jobs, are all skipped by the handlers.
        await _pipeline.RunAllAsync(requeued.Concat(jobs));

        _pipeline.ProfileRuns.Should().HaveCount(profileRuns, "no second profile for the same version");
        _pipeline.Index.BatchWrites.Should().Be(writes, "no second index write for the same version");
        _pipeline.Index.TrimCalls.Should().Be(trims);
        CurrentChunks().Select(c => c.Content).Should().Equal("B-a", "B-b", "B-c");
    }

    [Fact]
    public async Task RevertedContent_BThenAThenB_RefreshesForEveryNewVersion()
    {
        await SaveFirstVersionAsync();
        var keys = new List<string>();

        foreach (var text in new[] { VersionB, VersionA, VersionB })
        {
            var jobs = await SaveVersionAsync(Guid.NewGuid(), text);
            keys.AddRange(jobs.Select(j => j.IdempotencyKey));

            CurrentChunks().Select(c => c.Content).Should().Equal(text.Split('|'),
                "after each version the index holds that version's chunks and nothing else");
        }

        _pipeline.ProfileRuns.Should().HaveCount(4,
            "the first save plus three versions — the third repeats the first version's bytes and still refreshes");
        _pipeline.Index.BatchWrites.Should().Be(4);
        keys.Should().OnlyHaveUniqueItems("each save is its own refresh, so no content hash can collapse two of them");
    }

    /// <summary>
    /// Task 048 (spaarkeai-word-add-in-r1) renamed this test from
    /// "...AndNeverTrim" and updated its two stale assertions (the <c>ReplaceStaleChunks</c> payload
    /// property and the trim-call count). Task 029 made the trim opt-in per caller via
    /// <c>VersionDiscriminator</c>, so a first save / Email save — neither of which ever sets it — kept
    /// <c>ReplaceStaleChunks</c> off the wire entirely and never called the trim. Task 048's own
    /// acceptance criterion for a first index explicitly allows either "no trim call, or a trim that
    /// deletes nothing"; this task's design chose the latter, applied UNCONDITIONALLY at the shared
    /// <see cref="PostUploadIndexingEnqueuer"/> seam this test exercises. Everything else this slice's
    /// name promises — today's keys, today's job shape, no <c>VersionSaveJobId</c> — is untouched: task
    /// 048 changed no key and no skip/retry behaviour anywhere in this file's scope.
    /// </summary>
    [Fact]
    public async Task FirstSave_AndEmailSave_KeepTodaysKeysAndPayloads_AndTheNowUnconditionalTrimDeletesNothing()
    {
        _pipeline.SpeText[Item] = FirstVersion;
        var firstJobs = await _pipeline.FinalizeAsync(DocumentSave(), Guid.NewGuid(), Item, _documentId, isVersionSave: false);
        HasProperty(_pipeline.FinalizationMessages[^1].Payload, "VersionSaveJobId").Should().BeFalse();
        firstJobs.Select(j => j.IdempotencyKey).Should().BeEquivalentTo(new[]
        {
            $"analysis-{_documentId}-documentprofile",
            $"rag-index-{Drive}-{Item}",
        });

        const string emailItem = "01EMAIL029";
        var emailDocumentId = Guid.NewGuid();
        _pipeline.SpeText[emailItem] = "mail-a|mail-b";
        var emailJobs = await _pipeline.FinalizeAsync(EmailSave(), Guid.NewGuid(), emailItem, emailDocumentId, isVersionSave: false);
        HasProperty(_pipeline.FinalizationMessages[^1].Payload, "VersionSaveJobId").Should().BeFalse();
        emailJobs.Select(j => j.IdempotencyKey).Should().BeEquivalentTo(new[]
        {
            $"analysis-{emailDocumentId}-documentprofile",
            $"rag-index-{Drive}-{emailItem}",
        });

        firstJobs.Concat(emailJobs)
            .Where(j => j.JobType == RagIndexingJobHandler.JobTypeName)
            .Should().OnlyContain(j => HasProperty(j.Payload!.RootElement, "ReplaceStaleChunks")
                                        && j.Payload!.RootElement.GetProperty("ReplaceStaleChunks").GetBoolean(),
                "task 048: every non-version index job now asks for the trim too — only the version-key " +
                "suffix stays exclusive to a version save, not the trim flag");

        await _pipeline.RunAllAsync(firstJobs.Concat(emailJobs));

        _pipeline.Index.TrimCalls.Should().Be(2,
            "task 048: the trim now runs once per non-version index job (first save + Email save) " +
            "instead of never running — but each call has nothing to remove");
        _pipeline.Index.ChunksFor(RoutedIndex, Item).Should().HaveCount(5,
            "the first save's own 5 chunks must all survive its own no-op trim");
        _pipeline.Index.ChunksFor(RoutedIndex, emailItem).Should().HaveCount(2,
            "the Email save's own 2 chunks must all survive its own no-op trim");
    }

    [Fact]
    public async Task VersionReindex_WhenTheTrimFails_KeepsTheNewChunks_AndTheRetryFinishesTheReplace()
    {
        await SaveFirstVersionAsync();
        var jobs = await SaveVersionAsync(Guid.NewGuid(), VersionB, run: false);
        var indexJob = jobs.Single(j => j.JobType == RagIndexingJobHandler.JobTypeName);

        _pipeline.Index.FailNextTrim = new InvalidOperationException("AI Search rejected the delete");
        var failed = await _pipeline.RunAsync(indexJob);

        failed.Status.Should().Be(JobStatus.Failed, "a failed trim is transient: the job is retried, never poisoned");
        CurrentChunks().Take(3).Select(c => c.Content).Should().Equal(new[] { "B-a", "B-b", "B-c" },
            "the new chunks were written BEFORE the trim, so the document is never without its current content");
        (await _pipeline.Idempotency.IsEventProcessedAsync(indexJob.IdempotencyKey)).Should().BeFalse(
            "the version is not marked indexed until the replace is complete");

        var retried = await _pipeline.RunAsync(indexJob);

        retried.Status.Should().Be(JobStatus.Completed);
        CurrentChunks().Select(c => c.Content).Should().Equal("B-a", "B-b", "B-c");
    }

    // ── scenario helpers ─────────────────────────────────────────────────────────────────────────────

    private async Task SaveFirstVersionAsync()
    {
        _pipeline.SpeText[Item] = FirstVersion;
        var jobs = await _pipeline.FinalizeAsync(DocumentSave(), Guid.NewGuid(), Item, _documentId, isVersionSave: false);
        await _pipeline.RunAllAsync(jobs);
    }

    /// <summary>A version save: new bytes on the SAME item, finalized against the SAME document, with its own job.</summary>
    private async Task<IReadOnlyList<JobContract>> SaveVersionAsync(Guid saveJobId, string text, bool run = true)
    {
        _pipeline.SpeText[Item] = text;
        var jobs = await _pipeline.FinalizeAsync(VersionSave(text), saveJobId, Item, _documentId, isVersionSave: true);
        if (run)
        {
            await _pipeline.RunAllAsync(jobs);
        }

        return jobs;
    }

    private IReadOnlyList<KnowledgeDocument> CurrentChunks() => _pipeline.Index.ChunksFor(RoutedIndex, Item);

    private static SaveRequest DocumentSave() => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = MatterId },
        Document = new DocumentMetadata { FileName = "Brief.docx", ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(FirstVersion)) },
    };

    private SaveRequest VersionSave(string text) => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = MatterId },
        Document = new DocumentMetadata
        {
            FileName = "Brief.docx",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            IsNewVersion = true,
            ExistingDocumentId = _documentId,
        },
    };

    private static SaveRequest EmailSave() => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = MatterId },
        Email = new OfficeEmailMetadata
        {
            Subject = "Status update",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero),
            InternetMessageId = "<status-029@test.com>",
            Body = "Filed today.",
        },
    };

    private static bool HasProperty(JsonElement json, string name) =>
        json.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    // ── the pipeline under test, doubled only at its boundaries ──────────────────────────────────────

    private sealed class Pipeline : IDisposable
    {
        private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        private readonly ServiceBusOptions _serviceBusOptions = new()
        {
            QueueName = "test-jobs",
            CommunicationQueueName = "test-comms",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        };

        private readonly Mock<JobSubmissionService> _jobSubmission;
        private readonly ServiceProvider _provider;
        private readonly OfficeJobQueue _queue;
        private readonly RagIndexingJobHandler _indexHandler;
        private readonly AppOnlyDocumentAnalysisJobHandler _profileHandler;
        private readonly DocumentTelemetry _documentTelemetry = new();

        public Pipeline()
        {
            _jobSubmission = new Mock<JobSubmissionService>(
                MockBehavior.Loose,
                Options.Create(_serviceBusOptions),
                NullLogger<JobSubmissionService>.Instance,
                new Mock<ServiceBusClient>().Object);
            _jobSubmission
                .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Callback<JobContract, CancellationToken>((job, _) => Submitted.Add(job))
                .Returns(Task.CompletedTask);

            var services = new ServiceCollection();
            services.AddScoped<IPostUploadIndexingEnqueuer>(_ => new PostUploadIndexingEnqueuer(
                Mock.Of<IFileIndexingService>(),
                _jobSubmission.Object,
                Mock.Of<IDocumentDataverseService>(),
                Options.Create(new AnalysisOptions { SharedIndexName = DefaultIndex }),
                Options.Create(new PostUploadIndexingOptions()),
                NullLogger<PostUploadIndexingEnqueuer>.Instance));
            _provider = services.BuildServiceProvider();

            Worker = NewWorker();

            // The save's Service Bus send: the finalization message exactly as OfficeJobQueue serializes it.
            var sender = new Mock<ServiceBusSender>();
            sender
                .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
                .Callback<ServiceBusMessage, CancellationToken>((message, _) => FinalizationMessages.Add(
                    JsonSerializer.Deserialize<OfficeJobMessage>(message.Body.ToString(), CaseInsensitive)!))
                .Returns(Task.CompletedTask);
            var serviceBus = new Mock<ServiceBusClient>();
            serviceBus.Setup(c => c.CreateSender("office-upload-finalization")).Returns(sender.Object);
            _queue = new OfficeJobQueue(serviceBus.Object, Options.Create(_serviceBusOptions), NullLogger<OfficeJobQueue>.Instance);

            // SPE: the item's CURRENT version, which is what an index job downloads by item id.
            var spe = new Mock<ISpeFileOperations>();
            spe.Setup(s => s.DownloadFileAsync(Drive, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string itemId, CancellationToken _) =>
                    (Stream?)new MemoryStream(Encoding.UTF8.GetBytes(SpeText[itemId])));
            var extractor = new Mock<ITextExtractor>();
            extractor.Setup(e => e.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream stream, string _, CancellationToken _) =>
                    TextExtractionResult.Succeeded(new StreamReader(stream).ReadToEnd(), TextExtractionMethod.Native));
            var chunker = new Mock<ITextChunkingService>();
            chunker.Setup(c => c.ChunkTextAsync(It.IsAny<string?>(), It.IsAny<ChunkingOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? text, ChunkingOptions? _, CancellationToken _) =>
                    (IReadOnlyList<TextChunk>)text!.Split('|')
                        .Select((part, i) => new TextChunk { Content = part, Index = i, StartPosition = 0, EndPosition = part.Length })
                        .ToList());

            // The document routes to a per-record index — the case the tenant-default-only delete could not reach.
            var resolver = new Mock<ISearchIndexNameResolver>();
            resolver.Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RoutedIndex);
            resolver.Setup(r => r.GetDefaultIndexName()).Returns(DefaultIndex);

            _indexHandler = new RagIndexingJobHandler(
                new FileIndexingService(spe.Object, extractor.Object, chunker.Object, Index, NullLogger<FileIndexingService>.Instance),
                Idempotency,
                Mock.Of<IDocumentDataverseService>(),
                resolver.Object,
                new RagTelemetry(),
                NullLogger<RagIndexingJobHandler>.Instance);

            var analysis = new Mock<IAppOnlyAnalysisService>();
            analysis.Setup(a => a.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid documentId, string? _, CancellationToken _) =>
                {
                    ProfileRuns.Add(documentId);
                    return AppOnlyDocumentAnalysisResult.Success(documentId, null);
                });
            _profileHandler = new AppOnlyDocumentAnalysisJobHandler(
                analysis.Object, Idempotency, _documentTelemetry, NullLogger<AppOnlyDocumentAnalysisJobHandler>.Instance);
        }

        public Dictionary<string, string> SpeText { get; } = new(StringComparer.Ordinal);
        public InMemoryIndex Index { get; } = new();
        public InMemoryIdempotency Idempotency { get; } = new();
        public List<JobContract> Submitted { get; } = new();
        public List<OfficeJobMessage> FinalizationMessages { get; } = new();
        public List<Guid> ProfileRuns { get; } = new();

        /// <summary>The BFF instance that receives finalization messages (its own finalization cache).</summary>
        public UploadFinalizationWorker Worker { get; }

        /// <summary>Another BFF instance: same Service Bus and idempotency store, but an empty finalization cache.</summary>
        public UploadFinalizationWorker NewWorker() => new(
            NullLogger<UploadFinalizationWorker>.Instance,
            new InMemoryTenantCache(),
            new Mock<ServiceBusClient>().Object,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(_serviceBusOptions),
            Options.Create(new GraphOptions()),
            Mock.Of<IDocumentDataverseService>(),
            Mock.Of<IProcessingJobService>(),
            _jobSubmission.Object,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["TENANT_ID"] = Tenant })
                .Build());

        /// <summary>Queues the save's finalization (production <see cref="OfficeJobQueue"/>) and delivers it.</summary>
        public async Task<IReadOnlyList<JobContract>> FinalizeAsync(
            SaveRequest request, Guid saveJobId, string itemId, Guid documentId, bool isVersionSave)
        {
            await _queue.QueueUploadFinalizationAsync(
                saveJobId,
                $"save-{saveJobId:N}",
                "corr-029",
                "user-029",
                request,
                Drive,
                itemId,
                request.Document?.FileName ?? "status-update.eml",
                64,
                documentId,
                isVersionSave,
                CancellationToken.None);

            return await DeliverAsync(FinalizationMessages[^1], Worker);
        }

        /// <summary>Delivers a finalization message to a worker; returns the jobs it queued.</summary>
        public async Task<IReadOnlyList<JobContract>> DeliverAsync(OfficeJobMessage message, UploadFinalizationWorker worker)
        {
            var before = Submitted.Count;
            var outcome = await worker.ProcessAsync(message, CancellationToken.None);
            outcome.IsSuccess.Should().BeTrue();
            return Submitted.Skip(before).ToList();
        }

        public Task<JobOutcome> RunAsync(JobContract job) => job.JobType switch
        {
            RagIndexingJobHandler.JobTypeName => _indexHandler.ProcessAsync(job, CancellationToken.None),
            AppOnlyDocumentAnalysisJobHandler.JobTypeName => _profileHandler.ProcessAsync(job, CancellationToken.None),
            _ => throw new InvalidOperationException($"Unexpected job type {job.JobType}"),
        };

        public async Task RunAllAsync(IEnumerable<JobContract> jobs)
        {
            foreach (var job in jobs.ToList())
            {
                var outcome = await RunAsync(job);
                outcome.Status.Should().Be(JobStatus.Completed);
            }
        }

        public void Dispose()
        {
            _provider.Dispose();
            _documentTelemetry.Dispose();
        }
    }

    /// <summary>
    /// Azure AI Search stand-in: chunks keyed by (index, id) with merge-or-upload semantics — exactly how an id-keyed
    /// re-index overwrites in place — and a trim that follows the <see cref="IRagService.DeleteChunksBeyondCountAsync"/>
    /// contract. Nothing else of <see cref="IRagService"/> is on this path.
    /// </summary>
    private sealed class InMemoryIndex : IRagService
    {
        private readonly Dictionary<(string Index, string Id), KnowledgeDocument> _chunks = new();

        public int BatchWrites { get; private set; }
        public int TrimCalls { get; private set; }
        public Exception? FailNextTrim { get; set; }

        public IReadOnlyList<KnowledgeDocument> ChunksFor(string index, string speFileId) => _chunks
            .Where(c => c.Key.Index == index && c.Value.SpeFileId == speFileId)
            .Select(c => c.Value)
            .OrderBy(c => c.ChunkIndex)
            .ToList();

        public Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
            IEnumerable<KnowledgeDocument> documents, string? searchIndexName, CancellationToken cancellationToken = default)
        {
            BatchWrites++;
            var index = string.IsNullOrWhiteSpace(searchIndexName) ? DefaultIndex : searchIndexName;
            var results = new List<IndexResult>();
            foreach (var document in documents)
            {
                _chunks[(index, document.Id)] = document;
                results.Add(IndexResult.Success(document.Id));
            }

            return Task.FromResult<IReadOnlyList<IndexResult>>(results);
        }

        public Task<int> DeleteChunksBeyondCountAsync(
            string tenantId, string speFileId, int keepChunkCount, string? searchIndexName, CancellationToken cancellationToken = default)
        {
            TrimCalls++;
            if (FailNextTrim is { } failure)
            {
                FailNextTrim = null;
                throw failure;
            }

            var index = string.IsNullOrWhiteSpace(searchIndexName) ? DefaultIndex : searchIndexName;
            var leftovers = _chunks
                .Where(c => c.Key.Index == index
                            && c.Value.TenantId == tenantId
                            && c.Value.SpeFileId == speFileId
                            && c.Value.ChunkIndex >= keepChunkCount
                            && c.Value.Id == $"{speFileId}_{c.Value.ChunkIndex}")
                .Select(c => c.Key)
                .ToList();
            foreach (var key in leftovers)
            {
                _chunks.Remove(key);
            }

            return Task.FromResult(leftovers.Count);
        }

        public Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
            IEnumerable<KnowledgeDocument> documents, CancellationToken cancellationToken = default) =>
            IndexDocumentsBatchAsync(documents, null, cancellationToken);

        public Task<RagSearchResponse> SearchAsync(string query, RagSearchOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RagSearchResponse> SearchAsync(RagQuery ragQuery, Guid? deploymentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeDocument> IndexDocumentAsync(KnowledgeDocument document, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteDocumentAsync(string documentId, string tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> DeleteBySourceDocumentAsync(string sourceDocumentId, string tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReadOnlyMemory<float>> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeIndexHealth> GetIndexHealthAsync(string tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IndexedDocumentsPage> GetIndexedDocumentsAsync(
            string indexName, string tenantId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> DeleteIndexedDocumentAsync(
            string indexName, string documentId, string tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>The Redis-backed ADR-004 idempotency store, in memory and shared by both handlers.</summary>
    private sealed class InMemoryIdempotency : IIdempotencyService
    {
        private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _locks = new(StringComparer.Ordinal);

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_processed.Contains(eventId));

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            _processed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(_locks.Add(eventId));

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
        {
            _locks.Remove(eventId);
            return Task.CompletedTask;
        }
    }
}
