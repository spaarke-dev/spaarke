using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 177 (D-217, owner 2026-11-10): every outgoing email whose created <c>sprk_communication</c> is filed to a record
/// (any typed regarding lookup written on the row) is archived as an .eml Document, whatever <c>ArchiveToSpe</c> says, on BOTH
/// send paths (shared mailbox / app-only and send-as-user) and per recipient of a bulk send. A record-less send keeps the
/// opt-in flag; the sender account's <c>ArchiveOutgoingOptIn</c> still skips it; an archive failure is still non-fatal.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4), as <see cref="OutboundAttachmentArchiveTests"/>: the channel sender seam, the
/// <see cref="SpeFileStore"/> facade's virtual upload, the Dataverse writer (<see cref="IGenericEntityService"/>), the account
/// cache and the job queue. The assertions read the entities the service handed to the writer: the created communication (what
/// was filed) and the created archive document (what was archived).
/// </remarks>
public class OutgoingFiledEmailArchiveTests
{
    private const string ArchiveContainerId = "drive-archive";
    private const string SharedMailbox = "noreply@contoso.com";
    private const string UserMailbox = "author@contoso.com";
    private const int EmailArchiveSourceType = 659490003;

    private static readonly Guid MatterId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    private static readonly Guid OtherMatterId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000002");
    private static readonly Guid ServiceRequestId = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000001");

    private sealed class Harness
    {
        public required CommunicationService Sut { get; init; }
        public required List<(Entity Entity, Guid Id)> Created { get; init; }
        public required List<string> UploadPaths { get; init; }
        public required List<JobContract> Jobs { get; init; }

        public List<(Entity Entity, Guid Id)> Communications =>
            Created.Where(c => c.Entity.LogicalName == "sprk_communication").ToList();

        public List<(Entity Entity, Guid Id)> ArchivedEmls => Created
            .Where(c => c.Entity.LogicalName == "sprk_document"
                        && c.Entity.GetAttributeValue<OptionSetValue>("sprk_sourcetype")?.Value == EmailArchiveSourceType)
            .ToList();
    }

    // ── criterion 1: a filed send with ArchiveToSpe = false archives, on both paths ─────────────────────────────

    [Fact]
    public async Task SharedMailboxPath_AssignTaskNoticeShape_FiledToAMatter_IsArchived_ThoughArchiveToSpeIsFalse()
    {
        // The Assign Task notice: shared mailbox, internal-only, filed under its record, no ArchiveToSpe.
        var h = Build();
        var request = Request(SendMode.SharedMailbox) with
        {
            IsInternalOnly = true,
            Associations = [Matter(MatterId)],
        };

        var response = await h.Sut.SendAsync(request);

        var communication = h.Communications.Should().ContainSingle().Subject;
        communication.Entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        var eml = h.ArchivedEmls.Should().ContainSingle("a communication filed to a record is archived (D-217)").Subject;
        eml.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(communication.Id);
        response.ArchivedDocumentId.Should().Be(eml.Id);
        response.ArchivalWarning.Should().BeNull();
        h.Jobs.Select(j => j.SubjectId).Should().Contain(eml.Id.ToString(), "the archived .eml is profiled, as a requested archive is");
    }

    [Fact]
    public async Task UserPath_FiledToAMatter_IsArchived_ThoughArchiveToSpeIsFalse()
    {
        var h = Build();

        var response = await h.Sut.SendAsync(
            Request(SendMode.User) with { Associations = [Matter(MatterId)] }, UserContext());

        var communication = h.Communications.Should().ContainSingle().Subject;
        var eml = h.ArchivedEmls.Should().ContainSingle("a communication filed to a record is archived (D-217)").Subject;
        eml.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(communication.Id);
        response.ArchivedDocumentId.Should().Be(eml.Id);
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task BudgetInquiryShape_TwoLookups_IsArchivedOnce(SendMode mode)
    {
        // Task 176: filed under the service request AND the matter. One .eml per communication, never one per lookup.
        var h = Build();
        var request = Request(mode) with
        {
            IsInternalOnly = true,
            Associations =
            [
                new CommunicationAssociation { EntityType = "sprk_servicerequest", EntityId = ServiceRequestId },
                Matter(MatterId) with { AlsoRegarding = true },
            ],
        };

        await h.Sut.SendAsync(request, mode == SendMode.User ? UserContext() : null);

        var communication = h.Communications.Should().ContainSingle().Subject.Entity;
        communication.GetAttributeValue<EntityReference>("sprk_regardingservicerequest").Id.Should().Be(ServiceRequestId);
        communication.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        h.ArchivedEmls.Should().ContainSingle("one document per communication");
    }

    // ── C1: the archive document names its communication IN its create, on both paths ───────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task ArchivedEml_CarriesItsCommunication_InTheSameCreate(SendMode mode)
    {
        var h = Build();

        await h.Sut.SendAsync(Request(mode) with { Associations = [Matter(MatterId)] }, mode == SendMode.User ? UserContext() : null);

        // The entity handed to CreateAsync is the whole row: the link is not a later update.
        var eml = h.ArchivedEmls.Should().ContainSingle().Subject.Entity;
        eml.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Should().NotBeNull();
        eml.GetAttributeValue<EntityReference>("sprk_relatedcommunication").LogicalName.Should().Be("sprk_communication");
        eml.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(h.Communications.Single().Id);
    }

    // ── criterion 2: reply inheritance; record-less sends keep the opt-in ───────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task Reply_WithNoAssociations_InheritingRegardingFromItsSource_IsArchived(SendMode mode)
    {
        var source = Guid.NewGuid();
        var h = Build(sourceCommunication: new Entity("sprk_communication", source)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_isinternalonly"] = false,
        });

        await h.Sut.SendAsync(
            Request(mode) with { InheritRegardingFromCommunicationId = source },
            mode == SendMode.User ? UserContext() : null);

        h.Communications.Single().Entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId,
            "the reply inherited its source's regarding");
        h.ArchivedEmls.Should().ContainSingle("the filing is decided from the created row, not from Associations");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task RecordLessSend_ArchiveToSpeFalse_IsNotArchived(SendMode mode)
    {
        var h = Build();

        var response = await h.Sut.SendAsync(Request(mode), mode == SendMode.User ? UserContext() : null);

        h.Communications.Should().ContainSingle();
        h.ArchivedEmls.Should().BeEmpty("a record-less send keeps the opt-in flag");
        h.UploadPaths.Should().BeEmpty();
        response.ArchivedDocumentId.Should().BeNull();
        response.ArchivalWarning.Should().BeNull();
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task RecordLessSend_ArchiveToSpeTrue_IsArchived_AsToday(SendMode mode)
    {
        var h = Build();

        var response = await h.Sut.SendAsync(Request(mode) with { ArchiveToSpe = true }, mode == SendMode.User ? UserContext() : null);

        h.ArchivedEmls.Should().ContainSingle();
        response.ArchivedDocumentId.Should().Be(h.ArchivedEmls.Single().Id);
    }

    // ── criterion 3: the sender account's switch still skips; a failure is still non-fatal ──────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task FiledSend_SenderAccountOptedOut_IsNotArchived_WithTodaysWarning(SendMode mode)
    {
        var h = Build(optedOutMailbox: mode == SendMode.User ? UserMailbox : SharedMailbox);
        var request = Request(mode) with
        {
            Associations = [Matter(MatterId)],
            // The user path reads the switch from the FromMailbox account, as today.
            FromMailbox = mode == SendMode.User ? UserMailbox : null,
        };

        var response = await h.Sut.SendAsync(request, mode == SendMode.User ? UserContext() : null);

        h.ArchivedEmls.Should().BeEmpty();
        h.UploadPaths.Should().BeEmpty();
        response.ArchivalWarning.Should().Be("Archival skipped: ArchiveOutgoingOptIn is disabled for this sender account.");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task FiledSend_ArchiveFails_SendStillSucceeds_WithTodaysWarning(SendMode mode)
    {
        var h = Build(uploadFails: true);

        var response = await h.Sut.SendAsync(
            Request(mode) with { Associations = [Matter(MatterId)] }, mode == SendMode.User ? UserContext() : null);

        response.CommunicationId.Should().Be(h.Communications.Single().Id, "the send and its record succeed");
        response.ArchivedDocumentId.Should().BeNull();
        response.ArchivalWarning.Should().StartWith("Email sent successfully but archival failed:");
        h.ArchivedEmls.Should().BeEmpty();
    }

    // ── criterion 4: bulk send archives each recipient's filed communication ───────────────────────────────────

    [Fact]
    public async Task BulkSend_FiledToAMatter_ArchivesEachRecipientsCommunication()
    {
        var h = Build();
        var bulk = new BulkSendRequest
        {
            Subject = "Status",
            Body = "<p>Update.</p>",
            Associations = [Matter(MatterId)],
            Recipients =
            [
                new BulkRecipient { To = "one@example.com" },
                new BulkRecipient { To = "two@example.com" },
            ],
        };

        var handler = typeof(CommunicationEndpoints).GetMethod(
            "SendBulkCommunicationAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task<IResult>)handler.Invoke(null, new object?[]
        {
            bulk, h.Sut, Mock.Of<ILogger<CommunicationService>>(), new DefaultHttpContext(), CancellationToken.None,
        })!;

        h.Communications.Should().HaveCount(2, "one communication per recipient");
        h.ArchivedEmls.Should().HaveCount(2, "each recipient's filed communication is archived");
        h.ArchivedEmls.Select(e => e.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id)
            .Should().BeEquivalentTo(h.Communications.Select(c => c.Id));
    }

    // ── A1/A2 + C2: the archived .eml through the REAL external document narrowing ─────────────────────────────

    [Theory]
    [InlineData(true, ThreadPrivacyState.Open, false)]   // an internal-only message's .eml: hidden
    [InlineData(false, ThreadPrivacyState.Private, false)] // a Private-thread message's .eml: hidden
    [InlineData(false, ThreadPrivacyState.Open, true)]   // control: an Open, not internal-only message's .eml: shown
    public async Task ArchivedEml_ThroughTheRealExternalNarrowing_FollowsItsMessage(
        bool internalOnly, ThreadPrivacyState threadPrivacy, bool visible)
    {
        var h = Build();
        await h.Sut.SendAsync(Request(SendMode.SharedMailbox) with { Associations = [Matter(MatterId)] });
        var (emlEntity, emlId) = h.ArchivedEmls.Single();
        var communicationId = h.Communications.Single().Id;

        // The document exactly as the send created it, with a record link on it as well: the communication leg alone decides.
        var document = new Entity("sprk_document", emlId);
        foreach (var attribute in emlEntity.Attributes)
            document[attribute.Key] = attribute.Value;
        document["sprk_matter"] = new EntityReference("sprk_matter", MatterId);

        // The communication and its thread as Dataverse would answer the narrowing's app-only reads. A contact and a systemuser
        // flagged external are both "not an internal systemuser": the narrowing is the same decision for both.
        var threadId = Guid.NewGuid();
        var world = new NarrowingWorld();
        world.Add("sprk_communications", "sprk_communicationid", communicationId, new Dictionary<string, object?>
        {
            ["sprk_isinternalonly"] = internalOnly,
            ["_sprk_communicationthread_value"] = threadId.ToString(),
        });
        world.Add("sprk_communicationthreads", "sprk_communicationthreadid", threadId, new Dictionary<string, object?>
        {
            ["sprk_privacystate"] = (int)threadPrivacy,
        });

        var row = ExternalDocumentNarrowing.FromEntity(document);
        var result = await ExternalDocumentNarrowing.DecideAsync([row], world.Query, CancellationToken.None);

        result.IsVisible(row).Should().Be(visible);
        if (!visible)
            result.ReasonFor(row).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static CommunicationAssociation Matter(Guid id) => new() { EntityType = "sprk_matter", EntityId = id };

    private static SendCommunicationRequest Request(SendMode mode) => new()
    {
        To = ["recipient@example.com"],
        Subject = "Status update",
        Body = "<p>Hello.</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        SendMode = mode,
        ArchiveToSpe = false,
    };

    private static HttpContext UserContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", UserMailbox),
            new Claim("oid", Guid.NewGuid().ToString()),
        }, "Test")),
    };

    private static Harness Build(Entity? sourceCommunication = null, string? optedOutMailbox = null, bool uploadFails = false)
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = SharedMailbox, DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = SharedMailbox,
            ArchiveContainerId = ArchiveContainerId,
        };

        // The Dataverse writer: every create recorded with the id it was given; the reply source answers its read.
        var created = new List<(Entity Entity, Guid Id)>();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                sourceCommunication is not null && sourceCommunication.Id == id ? sourceCommunication : null!);
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                created.Add((e, id));
                return id;
            });

        var sender = new Mock<ICommunicationChannelSender>();
        sender.SetupGet(s => s.SupportedType).Returns(CommunicationType.Email);
        sender
            .Setup(s => s.SendAsync(It.IsAny<ChannelSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSendRequest r, CancellationToken _) => new ChannelSendResult { FromAddress = r.FromAddress });
        var dispatcher = new CommunicationChannelDispatcher(
            new[] { sender.Object },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        // The send-enabled accounts (the cache answers them): the opted-out mailbox has ArchiveOutgoingOptIn = false.
        var accounts = new[] { SharedMailbox, UserMailbox }.Select(address => new CommunicationAccount
        {
            Id = Guid.NewGuid(),
            Name = address,
            EmailAddress = address,
            SendEnabled = true,
            ArchiveOutgoingOptIn = !string.Equals(address, optedOutMailbox, StringComparison.OrdinalIgnoreCase),
        }).ToArray();
        var accountCache = new Mock<IDistributedCache>();
        accountCache
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts)));
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), accountCache.Object, Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options), accountService, Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var uploadPaths = new List<string>();
        var spe = BuildSpeFileStore(uploadPaths, uploadFails);

        var jobs = new List<JobContract>();
        var sut = new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService.Object,
            Mock.Of<IDocumentDataverseService>(),
            accountService,
            BuildJobQueue(jobs),
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<CommunicationService>>(),
            scopeFactory: SpeScopeFactoryStub.Create(spe.Object, SpeScopeFactoryStub.NonSecureContainerResolver()));

        return new Harness { Sut = sut, Created = created, UploadPaths = uploadPaths, Jobs = jobs };
    }

    private static Mock<SpeFileStore> BuildSpeFileStore(List<string> uploadPaths, bool uploadFails)
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
            .ReturnsAsync((string driveId, string path, Stream _, CancellationToken _) =>
            {
                if (uploadFails)
                    throw new InvalidOperationException("SharePoint Embedded upload failed");
                uploadPaths.Add(path);
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

    /// <summary>
    /// The narrowing's app-only reads over seeded rows: each by-key read answers the rows whose key is in the filter, projected
    /// through the query's own <c>$select</c> (a column the predicate does not select reads as absent).
    /// </summary>
    private sealed class NarrowingWorld
    {
        private readonly Dictionary<string, (string Key, List<Dictionary<string, JsonElement>> Rows)> _sets = new();

        public void Add(string set, string key, Guid id, Dictionary<string, object?> columns)
        {
            if (!_sets.TryGetValue(set, out var entry))
                _sets[set] = entry = (key, new List<Dictionary<string, JsonElement>>());
            var row = columns.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
            row[key] = JsonSerializer.SerializeToElement(id.ToString());
            entry.Rows.Add(row);
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> Query(string set, string odata, CancellationToken ct)
        {
            if (!_sets.TryGetValue(set, out var entry))
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>([]);
            var ids = Regex.Matches(odata, $@"{entry.Key} eq ([0-9a-fA-F-]{{36}})").Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var select = Regex.Match(odata, @"\$select=([^&]*)").Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<Dictionary<string, JsonElement>> hit = entry.Rows
                .Where(r => ids.Contains(r[entry.Key].GetString()!))
                .Select(r => r.Where(kv => select.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();
            return Task.FromResult(hit);
        }
    }
}
