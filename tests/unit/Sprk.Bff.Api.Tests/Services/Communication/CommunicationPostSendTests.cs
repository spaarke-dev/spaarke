using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// ISS-010 part E (#3184): <see cref="SendCommunicationRequest.DeferPostSend"/> moves a decision email's post-send work (thread, .eml
/// archive, enrichment, participant index, communication-arrived) out of the request into the <see cref="CommunicationPostSendJobHandler"/>
/// job. The send, the <c>sprk_communication</c> create and the response's "sent" stay in the request; a send without the flag is unchanged.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4), as <see cref="OutgoingFiledEmailArchiveTests"/>: the channel sender seam, the
/// <see cref="SpeFileStore"/> facade's upload, the Dataverse writer (<see cref="IGenericEntityService"/>, stateful: a created row answers
/// its reads), the thread resolver, the enrichment service, the job queue and the idempotency cache. The participant indexer is the real one.
/// </remarks>
public class CommunicationPostSendTests
{
    private const string ArchiveContainerId = "drive-archive";
    private const string SharedMailbox = "noreply@contoso.com";
    private const string UserMailbox = "author@contoso.com";
    private const int EmailArchiveSourceType = 659490003;

    private static readonly Guid MatterId = Guid.Parse("a1a1a1a1-0000-0000-0000-0000000000e1");

    private readonly ITestOutputHelper _output;

    public CommunicationPostSendTests(ITestOutputHelper output) => _output = output;

    // ── The profile: what an Assign Task email costs the commit request ────────────────────────────────────────

    [Fact]
    public async Task AssignTaskEmail_Deferred_TheRequestMakesFewerDataverseCalls_AndNoAiCall_AndQueuesOneJob()
    {
        var before = Build();
        await before.Sut.SendAsync(AssignTaskShape(defer: false), UserContext());
        var after = Build();
        await after.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());

        _output.WriteLine($"in-request Dataverse calls: before {before.DataverseCalls}, after {after.DataverseCalls}");
        _output.WriteLine($"in-request enrichment (AI) calls: before {before.EnrichCalls}, after {after.EnrichCalls}");
        _output.WriteLine($"in-request SPE uploads: before {before.UploadPaths.Count}, after {after.UploadPaths.Count}");
        _output.WriteLine($"in-request thread resolves: before {before.ThreadCalls.Count}, after {after.ThreadCalls.Count}");

        before.EnrichCalls.Should().Be(1, "today the email is enriched (triage and propose: the AI) inside the request");
        after.EnrichCalls.Should().Be(0, "the deferred send leaves enrichment to the job");
        before.UploadPaths.Should().ContainSingle();
        after.UploadPaths.Should().BeEmpty("the .eml archive moved to the job");
        before.ThreadCalls.Should().ContainSingle();
        after.ThreadCalls.Should().BeEmpty("the thread moved to the job");
        after.DataverseCalls.Should().BeLessThan(before.DataverseCalls);
        after.PostSendJobs.Should().ContainSingle();
        before.PostSendJobs.Should().BeEmpty();

        // What stays: the row, filed and carrying its correlation id (AlreadySentAsync's key).
        var row = after.Communications.Should().ContainSingle().Subject.Entity;
        row.GetAttributeValue<string>("sprk_correlationid").Should().Be("assign-task-corr");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        row.GetAttributeValue<bool>("sprk_isinternalonly").Should().BeTrue();
    }

    // ── Must not fire: the flag off is today's behaviour ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task FlagOff_EverythingRunsInline_AsBefore_AndNoPostSendJobIsQueued(SendMode mode)
    {
        var h = Build();

        var response = await h.Sut.SendAsync(AssignTaskShape(defer: false) with { SendMode = mode }, mode == SendMode.User ? UserContext() : null);

        h.PostSendJobs.Should().BeEmpty();
        h.ThreadCalls.Should().ContainSingle();
        h.EnrichCalls.Should().Be(1);
        h.ParticipantRows.Should().NotBeEmpty();
        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        response.ArchivedDocumentId.Should().Be(eml.Id);
        h.Jobs.Select(j => j.SubjectId).Should().Contain(eml.Id.ToString(), "the archived .eml is profiled, as before");
    }

    // ── The response says "sent", never "archived", before the job runs ────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task Deferred_TheResponseSaysSent_NeverArchived_AndOneJobCarriesIdsOnly(SendMode mode)
    {
        var h = Build();
        var request = AssignTaskShape(defer: true) with { SendMode = mode, Bcc = ["hidden@example.com"] };

        var response = await h.Sut.SendAsync(request, mode == SendMode.User ? UserContext() : null);

        response.Status.Should().Be(CommunicationStatus.Send);
        response.CommunicationId.Should().Be(h.Communications.Single().Id);
        response.ArchivedDocumentId.Should().BeNull("nothing is archived before the job runs");
        response.ArchivalWarning.Should().BeNull();
        h.ArchivedEmls.Should().BeEmpty();
        h.ParticipantRows.Should().BeEmpty("the participant index moved to the job");

        var job = h.PostSendJobs.Should().ContainSingle().Subject;
        job.IdempotencyKey.Should().Be($"comm-postsend:{response.CommunicationId:N}");
        job.CorrelationId.Should().Be("assign-task-corr");
        job.MaxAttempts.Should().Be(JobContract.MaxDeliveryCount);
        var payload = job.Payload!.RootElement.GetRawText();
        payload.Should().NotContain("recipient@example.com").And.NotContain("hidden@example.com").And.NotContain("Status update");
        var read = job.Payload.Deserialize<CommunicationPostSendPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        read.Archive.Should().BeTrue("the row is filed to a record (D-217)");
        read.Enrich.Should().BeTrue();
    }

    // ── A failed send still fails, with nothing queued ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task Deferred_AFailedSend_StillThrowsTheSendError_AndQueuesNothing(SendMode mode)
    {
        var h = Build(sendFault: new SdapProblemException("GRAPH_SEND_FAILED", "Email Send Failed", "graph 500", 502));

        var act = () => h.Sut.SendAsync(AssignTaskShape(defer: true) with { SendMode = mode }, mode == SendMode.User ? UserContext() : null);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be("GRAPH_SEND_FAILED");
        h.Jobs.Should().BeEmpty();
        h.Communications.Should().BeEmpty();
    }

    // ── The guard ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deferred_OnAMessage_OrIntoAThread_IsRefusedBeforeAnythingIsSent()
    {
        var h = Build();

        await FluentActions.Invoking(() => h.Sut.SendAsync(AssignTaskShape(defer: true) with { CommunicationType = CommunicationType.Message }, UserContext()))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Invoking(() => h.Sut.SendAsync(AssignTaskShape(defer: true) with { ThreadId = Guid.NewGuid() }, UserContext()))
            .Should().ThrowAsync<InvalidOperationException>();

        h.SendCalls.Should().Be(0);
        h.Created.Should().BeEmpty();
    }

    // ── The job: once per communication ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task TheJob_RunTwice_MakesOneDocument_OneThread_AndEnrichesOnce(SendMode mode)
    {
        var h = Build();
        var response = await h.Sut.SendAsync(AssignTaskShape(defer: true) with { SendMode = mode }, mode == SendMode.User ? UserContext() : null);
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();

        var first = await handler.ProcessAsync(job, CancellationToken.None);
        h.Idempotency.Forget(); // the cache lost every entry (it fails open, #984): only the durable guards remain
        var second = await handler.ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Completed);
        second.Status.Should().Be(JobStatus.Completed);
        var eml = h.ArchivedEmls.Should().ContainSingle("ArchiveExistingAsync finds the first run's Document").Subject;
        eml.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(response.CommunicationId!.Value);
        h.UploadPaths.Should().ContainSingle();
        h.ThreadCalls.Should().ContainSingle("the second run sees the row's thread");
        h.EnrichCalls.Should().Be(1, "the second run sees the first run's review row");
        h.EnrichArchiveIds.Should().Equal(eml.Id);
        h.Jobs.Count(j => j.SubjectId == eml.Id.ToString()).Should().Be(1, "the .eml is profiled once");
    }

    [Fact]
    public async Task TheJob_RedeliveredWithItsMarkers_DoesNothingAgain_AndEmitsArrivedOnce()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();

        await handler.ProcessAsync(job, CancellationToken.None);
        var participantRows = h.ParticipantRows.Count;
        var calls = h.DataverseCalls;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        handler.Arrived.Should().Be(1);
        h.ParticipantRows.Count.Should().Be(participantRows).And.BeGreaterThan(0);
        h.DataverseCalls.Should().Be(calls, "a done job reads nothing");
    }

    [Fact]
    public async Task TheJob_CreatesTheThreadPrivate_WhenTheSendAskedForIt()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true) with { CreateThreadPrivate = true }, UserContext());

        await h.Handler().ProcessAsync(h.PostSendJobs.Single(), CancellationToken.None);

        h.ThreadCalls.Should().ContainSingle().Which.CreatePrivate.Should().BeTrue();
    }

    [Fact]
    public async Task TheJob_PlainTextWithBcc_ArchivesTheSentFormatAndBcc()
    {
        // Q2: the job's .eml of a PlainText + Bcc inquiry carries both, as the send-time archive did.
        var h = Build();
        await h.Sut.SendAsync(
            AssignTaskShape(defer: true) with { BodyFormat = BodyFormat.PlainText, Body = "Plain body.", Bcc = ["hidden@example.com"] },
            UserContext());

        await h.Handler().ProcessAsync(h.PostSendJobs.Single(), CancellationToken.None);

        var eml = Encoding.UTF8.GetString(h.UploadedBytes.Should().ContainSingle().Subject);
        eml.Should().Contain("hidden@example.com");
        eml.Should().Contain("text/plain");
    }

    [Fact]
    public async Task TheJob_ARowThatNoLongerExists_IsDone()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        h.Rows.Clear();

        (await h.Handler().ProcessAsync(h.PostSendJobs.Single(), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        h.ArchivedEmls.Should().BeEmpty();
        h.EnrichCalls.Should().Be(0);
    }

    [Fact]
    public async Task TheJob_AnArchiveFaultBeforeTheFinalDelivery_IsRetried_WithoutEnrichment_ThenCompletes()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();

        h.UploadFails = true;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);
        h.EnrichCalls.Should().Be(0, "RAG indexing needs the document; enrichment waits for the retry");
        handler.Arrived.Should().Be(1, "arrived does not wait for the archive");

        h.UploadFails = false;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        h.ArchivedEmls.Should().ContainSingle();
        h.EnrichCalls.Should().Be(1);
        handler.Arrived.Should().Be(1);
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static SendCommunicationRequest AssignTaskShape(bool defer) => new()
    {
        To = ["recipient@example.com"],
        Subject = "Status update",
        Body = "<p>Hello.</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        SendMode = SendMode.User,
        CorrelationId = "assign-task-corr",
        IsInternalOnly = true,
        Associations = [new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId }],
        DeferPostSend = defer,
    };

    private static HttpContext UserContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", UserMailbox),
            new Claim("oid", Guid.NewGuid().ToString()),
        }, "Test")),
    };

    private sealed class Harness
    {
        public required CommunicationService Sut { get; init; }
        public required Mock<IGenericEntityService> Entities { get; init; }
        public required Mock<ICommunicationEnrichmentService> Enrichment { get; init; }
        public required List<(Entity Entity, Guid Id)> Created { get; init; }
        public required Dictionary<Guid, Entity> Rows { get; init; }
        public required List<string> UploadPaths { get; init; }
        public required List<byte[]> UploadedBytes { get; init; }
        public required List<JobContract> Jobs { get; init; }
        public required List<ThreadResolutionRequest> ThreadCalls { get; init; }
        public required InMemoryIdempotency Idempotency { get; init; }
        public required Func<int> SendCount { get; init; }
        public bool UploadFails { get; set; }

        public int SendCalls => SendCount();
        public int DataverseCalls => Entities.Invocations.Count;
        public int EnrichCalls => Enrichment.Invocations.Count(i => i.Method.Name == nameof(ICommunicationEnrichmentService.EnrichAsync));
        public List<Guid?> EnrichArchiveIds => Enrichment.Invocations
            .Where(i => i.Method.Name == nameof(ICommunicationEnrichmentService.EnrichAsync)).Select(i => (Guid?)i.Arguments[3]).ToList();

        public List<(Entity Entity, Guid Id)> Communications => Created.Where(c => c.Entity.LogicalName == "sprk_communication").ToList();
        public List<(Entity Entity, Guid Id)> ParticipantRows => Created.Where(c => c.Entity.LogicalName == "sprk_communicationparticipant").ToList();
        public List<JobContract> PostSendJobs => Jobs.Where(j => j.JobType == CommunicationPostSendJobHandler.JobTypeName).ToList();

        public List<(Entity Entity, Guid Id)> ArchivedEmls => Created
            .Where(c => c.Entity.LogicalName == "sprk_document"
                        && c.Entity.GetAttributeValue<OptionSetValue>("sprk_sourcetype")?.Value == EmailArchiveSourceType)
            .ToList();

        public CountingHandler Handler() => new(Sut, Entities.Object, Enrichment.Object, Idempotency);
    }

    /// <summary>The handler with its arrived emit counted.</summary>
    private sealed class CountingHandler(
        CommunicationService communication, IGenericEntityService entities, ICommunicationEnrichmentService enrichment, IIdempotencyService idempotency)
        : CommunicationPostSendJobHandler(communication, entities, enrichment, idempotency, NullLogger<CommunicationPostSendJobHandler>.Instance)
    {
        public int Arrived { get; private set; }

        internal override Task EmitArrivedAsync(Guid communicationId, CancellationToken ct)
        {
            Arrived++;
            return Task.CompletedTask;
        }
    }

    /// <summary>The ADR-004 idempotency cache, in memory; <see cref="Forget"/> empties it (the cache fails open, #984).</summary>
    private sealed class InMemoryIdempotency : IIdempotencyService
    {
        private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _locks = new(StringComparer.Ordinal);

        public void Forget() => _processed.Clear();

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(_processed.Contains(eventId));

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

    private static Harness Build(Exception? sendFault = null)
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = SharedMailbox, DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = SharedMailbox,
            ArchiveContainerId = ArchiveContainerId,
        };

        // The Dataverse writer, stateful: a created row answers its reads and its updates; an archive Document and a review row answer
        // the queries that look for them.
        var created = new List<(Entity Entity, Guid Id)>();
        var rows = new Dictionary<Guid, Entity>();
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => rows.TryGetValue(id, out var row) ? row : null!);
        entities
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                if (rows.TryGetValue(id, out var row))
                    foreach (var (k, v) in fields) row[k] = v;
                return Task.CompletedTask;
            });
        entities
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var match = q.EntityName switch
                {
                    "sprk_document" when q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_isemailarchive") =>
                        created.Where(c => c.Entity.LogicalName == "sprk_document"
                            && c.Entity.GetAttributeValue<bool?>("sprk_isemailarchive") == true
                            && c.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication")?.Id
                                == (Guid)q.Criteria.Conditions.First(x => x.AttributeName == "sprk_relatedcommunication").Values[0]),
                    "sprk_emailreviewlog" => created.Where(c => c.Entity.LogicalName == "sprk_emailreviewlog"
                        && c.Entity.GetAttributeValue<EntityReference>("sprk_communication")?.Id
                            == (Guid)q.Criteria.Conditions.First(x => x.AttributeName == "sprk_communication").Values[0]),
                    _ => [],
                };
                return new EntityCollection(match.Select(c => new Entity(c.Entity.LogicalName, c.Id)).ToList());
            });
        entities
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                created.Add((e, id));
                if (e.LogicalName == "sprk_communication")
                {
                    var row = new Entity("sprk_communication", id);
                    foreach (var attribute in e.Attributes) row[attribute.Key] = attribute.Value;
                    rows[id] = row;
                }
                return id;
            });

        var sendCount = 0;
        var sender = new Mock<ICommunicationChannelSender>();
        sender.SetupGet(s => s.SupportedType).Returns(CommunicationType.Email);
        sender
            .Setup(s => s.SendAsync(It.IsAny<ChannelSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSendRequest r, CancellationToken _) =>
            {
                sendCount++;
                if (sendFault is not null) throw sendFault;
                return new ChannelSendResult { FromAddress = r.FromAddress };
            });
        var dispatcher = new CommunicationChannelDispatcher(
            new[] { sender.Object },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        var accounts = new[] { SharedMailbox, UserMailbox }.Select(address => new CommunicationAccount
        {
            Id = Guid.NewGuid(),
            Name = address,
            EmailAddress = address,
            SendEnabled = true,
            ArchiveOutgoingOptIn = true,
        }).ToArray();
        var accountCache = new Mock<IDistributedCache>();
        accountCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts)));
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), accountCache.Object, Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options), accountService, Mock.Of<IDistributedCache>(), Mock.Of<ILogger<ApprovedSenderValidator>>());

        var uploadPaths = new List<string>();
        var uploadedBytes = new List<byte[]>();
        Harness? harness = null;
        var spe = BuildSpeFileStore(uploadPaths, uploadedBytes, () => harness?.UploadFails == true);

        var jobs = new List<JobContract>();
        var threadCalls = new List<ThreadResolutionRequest>();
        var threads = new Mock<IThreadResolver>();
        threads.Setup(t => t.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThreadResolutionRequest r, CancellationToken _) =>
            {
                threadCalls.Add(r);
                var threadId = Guid.NewGuid();
                if (rows.TryGetValue(r.CommunicationId, out var row))
                    row["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId);
                return threadId;
            });

        // Enrichment: its propose step writes a review row naming the communication (the durable evidence the job reads).
        var enrichment = new Mock<ICommunicationEnrichmentService>();
        enrichment.Setup(e => e.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CommunicationDirection _, NormalizedMessage _, Guid? _, CancellationToken _) =>
            {
                created.Add((new Entity("sprk_emailreviewlog") { ["sprk_communication"] = new EntityReference("sprk_communication", id) }, Guid.NewGuid()));
                return Task.CompletedTask;
            });

        var communicationLookup = Mock.Of<ICommunicationDataverseService>(c =>
            c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<Guid?>(Guid.NewGuid()));
        var indexer = new CommunicationParticipantIndexer(
            communicationLookup, entities.Object, new RecordOwnershipResolverDouble(), NullLogger<CommunicationParticipantIndexer>.Instance);

        var sut = new CommunicationService(
            dispatcher,
            senderValidator,
            communicationLookup,
            entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            accountService,
            BuildJobQueue(jobs),
            enrichment.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<CommunicationService>>(),
            threadResolver: threads.Object,
            scopeFactory: SpeScopeFactoryStub.Create(spe.Object, SpeScopeFactoryStub.NonSecureContainerResolver()),
            participantIndexer: indexer);

        harness = new Harness
        {
            Sut = sut,
            Entities = entities,
            Enrichment = enrichment,
            Created = created,
            Rows = rows,
            UploadPaths = uploadPaths,
            UploadedBytes = uploadedBytes,
            Jobs = jobs,
            ThreadCalls = threadCalls,
            Idempotency = new InMemoryIdempotency(),
            SendCount = () => sendCount,
        };
        return harness;
    }

    private static Mock<SpeFileStore> BuildSpeFileStore(List<string> uploadPaths, List<byte[]> uploadedBytes, Func<bool> uploadFails)
    {
        var metadataCache = new GraphMetadataCache(
            new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
            Mock.Of<ILogger<GraphMetadataCache>>());
        var gcf = Mock.Of<IGraphClientFactory>();
        var speMock = new Mock<SpeFileStore>(
            MockBehavior.Loose,
            new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>(), metadataCache),
            new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
            null!);

        speMock
            .Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string driveId, string path, Stream content, CancellationToken _) =>
            {
                if (uploadFails())
                    throw new HttpRequestException("SharePoint Embedded upload failed");
                uploadPaths.Add(path);
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                uploadedBytes.Add(copy.ToArray());
                return new FileHandleDto(
                    Id: $"item-{uploadPaths.Count}", Name: path, ParentId: null, Size: 1,
                    CreatedDateTime: DateTimeOffset.UnixEpoch, LastModifiedDateTime: DateTimeOffset.UnixEpoch,
                    ETag: null, IsFolder: false, WebUrl: null, DriveId: driveId);
            });

        return speMock;
    }

    private static JobSubmissionService BuildJobQueue(List<JobContract> submitted)
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });

        var jobs = new Mock<JobSubmissionService>(
            MockBehavior.Loose, sbOptions.Object, Mock.Of<ILogger<JobSubmissionService>>(), new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback((JobContract job, CancellationToken _) => submitted.Add(job))
            .Returns(Task.CompletedTask);
        return jobs.Object;
    }
}
