using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// spaarke-intake-pipeline-r1 task 005, design §10.1 item 5a. The email archival paths
/// (<see cref="CommunicationService"/>, <see cref="IncomingCommunicationProcessor"/>) queued the document's profile under
/// <c>DocAnalysis:{documentId}</c>, while every upload path queues it under
/// <see cref="AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey"/>. The handler dedups on the key it is GIVEN, so a
/// document reached by both (an .eml archived by email and the same file uploaded) was profiled twice. Both archival sites now use
/// the upload key with the upload paths' MaxAttempts of 3. Each test drives the REAL enqueue site and reads the contract it
/// submits; the dedup test then runs the REAL handler over both contracts.
/// </summary>
public class Intake10_1_Item5a_DocumentProfileIdempotencyKeyTests
{
    private static readonly Guid DocumentId = Guid.Parse("d0c0d0c0-0000-0000-0000-00000000000d");
    private static readonly Guid OtherDocumentId = Guid.Parse("d0c0d0c0-0000-0000-0000-00000000000e");
    private static readonly Guid CommunicationId = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    // ── The real enqueue sites ────────────────────────────────────────────────────────────────────────────────

    private static Mock<JobSubmissionService> CapturingJobs(List<JobContract> submitted)
    {
        var options = new Mock<IOptions<ServiceBusOptions>>();
        options.Setup(o => o.Value).Returns(new ServiceBusOptions());
        var jobs = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            options.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => submitted.Add(job))
            .Returns(Task.CompletedTask);
        return jobs;
    }

    /// <summary>The outbound/archive site: <c>CommunicationService.EnqueueDocumentAnalysisAsync</c>.</summary>
    private static async Task<JobContract> SubmittedByCommunicationService(Guid documentId)
    {
        var submitted = new List<JobContract>();
        var options = Options.Create(new CommunicationOptions());
        var service = new CommunicationService(
            CommunicationChannelTestFactory.CreateDispatcher(Mock.Of<IGraphClientFactory>()),
            new ApprovedSenderValidator(
                options,
                new CommunicationAccountService(
                    Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                    NullLogger<CommunicationAccountService>.Instance),
                Mock.Of<IDistributedCache>(),
                NullLogger<ApprovedSenderValidator>.Instance),
            Mock.Of<ICommunicationDataverseService>(),
            Mock.Of<IGenericEntityService>(),
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService: not reached by the enqueue
            CapturingJobs(submitted).Object,
            Mock.Of<ICommunicationEnrichmentService>(),
            options,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<CommunicationService>.Instance);

        await service.EnqueueDocumentAnalysisAsync(documentId, "intake-5a", CancellationToken.None);

        return submitted.Should().ContainSingle("the archive site queues exactly one profile job").Subject;
    }

    /// <summary>The inbound mailbox site: <c>IncomingCommunicationProcessor.EnqueueDocumentAnalysisAsync</c>.</summary>
    private static async Task<JobContract> SubmittedByIncomingProcessor(Guid documentId)
    {
        var submitted = new List<JobContract>();
        var dv = new Mock<IDataverseService>();
        var processor = new IncomingCommunicationProcessor(
            Mock.Of<IGraphClientFactory>(),
            dv.Object,
            dv.Object,
            new CommunicationAccountService(
                dv.Object, dv.Object, Mock.Of<IDistributedCache>(), Mock.Of<ILogger<CommunicationAccountService>>()),
            new IncomingAssociationResolver(
                new IAssociationRung[]
                {
                    new ExplicitReferenceRung(dv.Object),
                    new ThreadContinuityRung(dv.Object),
                    new ParticipantCorrelationRung(dv.Object),
                },
                dv.Object, dv.Object,
                AssociationTestSupport.Mapper(),
                Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
                NullLogger<IncomingAssociationResolver>.Instance),
            new GraphMessageNormalizer(),
            new GraphMessageToEmlConverter(),
            Mock.Of<IServiceScopeFactory>(),
            CapturingJobs(submitted).Object,
            new NotificationService(Mock.Of<IGenericEntityService>(), Mock.Of<ILogger<NotificationService>>()),
            Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(new CommunicationOptions()),
            Mock.Of<ITextExtractor>(),
            Options.Create(new AttachmentMatchOptions { Enabled = false }),
            new ConfigurationBuilder().Build(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<IncomingCommunicationProcessor>.Instance,
            threadResolver: null);

        await processor.EnqueueDocumentAnalysisAsync(documentId, CommunicationId, CancellationToken.None);

        return submitted.Should().ContainSingle("the inbound site queues exactly one profile job").Subject;
    }

    /// <summary>
    /// The upload path's contract, as <c>DocumentOperationsEndpoints</c> / <c>UploadFinalizationWorker</c> build it (key
    /// <see cref="AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey"/>, MaxAttempts 3).
    /// </summary>
    private static JobContract UploadPathContract(Guid documentId) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
        SubjectId = documentId.ToString(),
        CorrelationId = "upload",
        IdempotencyKey = AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentId),
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse(JsonSerializer.Serialize(new { DocumentId = documentId, Source = "Upload" })),
    };

    // ── The contract ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailArchiveSite_QueuesTheProfileUnderTheUploadKey_WithMaxAttemptsThree()
    {
        var job = await SubmittedByCommunicationService(DocumentId);

        job.IdempotencyKey.Should().Be(AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(DocumentId));
        job.IdempotencyKey.Should().NotStartWith("DocAnalysis:");
        job.MaxAttempts.Should().Be(UploadPathContract(DocumentId).MaxAttempts);
    }

    [Fact]
    public async Task InboundMailboxSite_QueuesTheProfileUnderTheUploadKey_WithMaxAttemptsThree()
    {
        var job = await SubmittedByIncomingProcessor(DocumentId);

        job.IdempotencyKey.Should().Be(AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(DocumentId));
        job.IdempotencyKey.Should().NotStartWith("DocAnalysis:");
        job.MaxAttempts.Should().Be(UploadPathContract(DocumentId).MaxAttempts);
    }

    [Fact]
    public async Task EmailAndUploadPaths_ForTheSameDocument_ProduceOneProfile()
    {
        var analysis = new Mock<IAppOnlyAnalysisService>();
        var profiled = new List<Guid>();
        analysis.Setup(a => a.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, string? _, CancellationToken _) =>
            {
                profiled.Add(id);
                return AppOnlyDocumentAnalysisResult.Success(id, null);
            });
        var handler = new AppOnlyDocumentAnalysisJobHandler(
            analysis.Object, new InMemoryIdempotency(), new DocumentTelemetry(), NullLogger<AppOnlyDocumentAnalysisJobHandler>.Instance);

        var viaEmail = await SubmittedByCommunicationService(DocumentId);
        var viaInbound = await SubmittedByIncomingProcessor(DocumentId);
        var viaUpload = UploadPathContract(DocumentId);

        // Same Service Bus message id for all three, and the handler runs ONE profile for the set, whichever lands first.
        var outcomes = new[]
        {
            await handler.ProcessAsync(viaEmail, CancellationToken.None),
            await handler.ProcessAsync(viaUpload, CancellationToken.None),
            await handler.ProcessAsync(viaInbound, CancellationToken.None),
        };

        outcomes.Should().OnlyContain(o => o.Status == JobStatus.Completed, "a duplicate is skipped as a success, never failed");
        profiled.Should().Equal(new[] { DocumentId }, "one profile for the document, not one per path");
    }

    [Fact]
    public async Task EmailAndUploadPaths_ForDifferentDocuments_AreNotDeduplicated()
    {
        var emailKey = (await SubmittedByCommunicationService(DocumentId)).IdempotencyKey;
        var otherUploadKey = UploadPathContract(OtherDocumentId).IdempotencyKey;

        emailKey.Should().NotBe(otherUploadKey, "a different document must still be profiled");
    }

    /// <summary>The Redis-backed ADR-004 idempotency store, in memory.</summary>
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
