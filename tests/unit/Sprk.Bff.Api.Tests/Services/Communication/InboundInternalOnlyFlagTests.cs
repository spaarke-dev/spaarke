using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Task 065 fix round 3, item 4 (owner decision 2026-10-14): received communications follow the same rule as sent ones.
/// The three inbound writers (<see cref="MessagingIngestor"/>, <see cref="EmailUploadCaptureService"/>,
/// <see cref="IncomingCommunicationProcessor"/>) write <c>sprk_isinternalonly</c> explicitly. Each creates the row
/// <c>true</c> (its thread is unknown at create) and lowers it to <c>false</c> only once the thread is confirmed Open (and,
/// for the mailbox email, the parent it replies to is explicitly shareable). A Private thread, a thread with no privacy
/// value, an unreadable thread and an internal-only parent all keep it <c>true</c>.
/// </summary>
public class InboundInternalOnlyFlagTests
{
    private const int Open = 100000000;
    private const int Private = 100000001;
    private static readonly Guid ThreadId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid CommId = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ParentId = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    /// <summary>The thread read the floor makes. Open, Private, a missing value (null), a null row or a throw.</summary>
    private static void ThreadReads(Mock<IGenericEntityService> entities, string privacy)
    {
        var setup = entities.Setup(g => g.RetrieveAsync(
            "sprk_communicationthread", ThreadId,
            It.Is<string[]>(c => c.Contains("sprk_privacystate")), It.IsAny<CancellationToken>()));
        switch (privacy)
        {
            case "open": setup.ReturnsAsync(Thread(Open)); break;
            case "private": setup.ReturnsAsync(Thread(Private)); break;
            case "novalue": setup.ReturnsAsync(Thread(null)); break;
            case "nullrow": setup.ReturnsAsync((DataverseEntity)null!); break;
            case "throws": setup.ThrowsAsync(new InvalidOperationException("dataverse unavailable")); break;
            default: throw new ArgumentOutOfRangeException(nameof(privacy));
        }
    }

    private static DataverseEntity Thread(int? privacy)
    {
        var thread = new DataverseEntity("sprk_communicationthread", ThreadId);
        if (privacy is { } p)
            thread["sprk_privacystate"] = new OptionSetValue(p);
        return thread;
    }

    private static void VerifyLowered(Mock<IGenericEntityService> entities) =>
        entities.Verify(
            g => g.UpdateAsync(
                "sprk_communication", CommId,
                It.Is<Dictionary<string, object>>(f => f.Count == 1 && f["sprk_isinternalonly"].Equals(false)),
                It.IsAny<CancellationToken>()),
            Times.Once);

    private static void VerifyNeverLowered(Mock<IGenericEntityService> entities) =>
        entities.Verify(
            g => g.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Writer 1: MessagingIngestor (inbound ACS chat)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static ChannelIngestRequest ChatRequest(string? inReplyTo = null) => new()
    {
        Message = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = "8:acs:sender-mri",
            To = new[] { "8:acs:recipient-mri" },
            Subject = "Kickoff",
            BodyText = "hello there",
            InReplyTo = inReplyTo,
            SentAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
        },
        ProviderMessageId = "1700000000123",
        ProviderThreadId = "19:acs:thread@thread.v2",
        CorrelationId = "corr-065",
    };

    private static (MessagingIngestor Sut, Mock<IGenericEntityService> Entities, List<DataverseEntity> Created) ChatSut(
        string? privacy, bool resolverReturnsThread = true, bool withResolver = true,
        Mock<ICommunicationEnrichmentService>? enrichment = null)
    {
        var created = new List<DataverseEntity>();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(g => g.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(CommId);
        if (privacy is not null)
            ThreadReads(entities, privacy);

        var resolver = new Mock<IThreadResolver>();
        resolver.Setup(r => r.ResolveAndAssignAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolverReturnsThread ? ThreadAssignment.NoGrant(ThreadId) : null);

        var sut = new MessagingIngestor(
            entities.Object, (enrichment ?? new Mock<ICommunicationEnrichmentService>()).Object, NullLogger<MessagingIngestor>.Instance,
            withResolver ? resolver.Object : null);
        return (sut, entities, created);
    }

    [Fact]
    public async Task MessagingIngestor_WritesInternalOnlyTrueOnTheCreate_BecauseItsThreadIsUnknownThen()
    {
        var (sut, _, created) = ChatSut("open");

        await sut.IngestAsync(ChatRequest());

        created.Should().ContainSingle().Which.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Fact]
    public async Task MessagingIngestor_MessageJoiningAnOpenThread_IsLoweredToNotInternalOnly()
    {
        var (sut, entities, _) = ChatSut("open");

        var result = await sut.IngestAsync(ChatRequest());

        result.CommunicationId.Should().Be(CommId);
        VerifyLowered(entities);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("novalue")]
    [InlineData("nullrow")]
    [InlineData("throws")]
    public async Task MessagingIngestor_MessageJoiningAPrivateNoValueOrUnreadableThread_StaysInternalOnly(string privacy)
    {
        var (sut, entities, _) = ChatSut(privacy);

        var result = await sut.IngestAsync(ChatRequest());

        result.CommunicationId.Should().Be(CommId, "the persist is never failed by the floor");
        VerifyNeverLowered(entities);
    }

    [Theory]
    [InlineData("no resolved thread")]
    [InlineData("no resolver")]
    public async Task MessagingIngestor_MessageWithNoResolvedThread_StaysInternalOnly(string case_)
    {
        var (sut, entities, _) = ChatSut(
            "open", resolverReturnsThread: case_ != "no resolved thread", withResolver: case_ != "no resolver");

        await sut.IngestAsync(ChatRequest());

        VerifyNeverLowered(entities);
    }

    [Fact]
    public async Task MessagingIngestor_MessageWithAParentItCannotResolve_StaysInternalOnly()
    {
        // The ACS normalizer never sets InReplyTo; a value there is a parent this writer has no lookup for, so it fails closed.
        var (sut, entities, _) = ChatSut("open");

        await sut.IngestAsync(ChatRequest(inReplyTo: "parent-message"));

        VerifyNeverLowered(entities);
    }

    [Fact]
    public async Task MessagingIngestor_WhenLoweringFaults_StillReturnsThePersistedIdAndStaysInternalOnly()
    {
        var (sut, entities, _) = ChatSut("open");
        entities.Setup(g => g.UpdateAsync("sprk_communication", CommId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        var result = await sut.IngestAsync(ChatRequest());

        result.CommunicationId.Should().Be(CommId);
    }

    [Theory]
    [InlineData("thread read")]
    [InlineData("update")]
    public async Task MessagingIngestor_WhenCancelledWhileLowering_RowStaysInternalOnly_AndProcessingCompletes(string where)
    {
        // Fix round 4 (K2): the row already exists. Cancellation escaping here let a retry create a DUPLICATE row; it must be
        // caught like the other post-create steps, leaving the row internal-only and the later steps run.
        var enrichment = new Mock<ICommunicationEnrichmentService>();
        var (sut, entities, created) = ChatSut("open", enrichment: enrichment);
        if (where == "thread read")
        {
            entities.Setup(g => g.RetrieveAsync(
                    "sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
        }
        else
        {
            entities.Setup(g => g.UpdateAsync("sprk_communication", CommId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
        }

        var result = await sut.IngestAsync(ChatRequest());

        result.CommunicationId.Should().Be(CommId);
        created.Should().ContainSingle("a retry must not be needed, so no second row");
        created[0].GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
        enrichment.Verify(
            e => e.EnrichAsync(CommId, It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once, "processing continues past the cancelled lowering");
    }

    [Fact]
    public async Task CommunicationInternalOnlyFloor_LowerWhenSafe_PropagatesCancellation_NotSwallowedAsAFault()
    {
        // The helper's contract (documented): faults never escape; cancellation propagates to the caller.
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(g => g.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => CommunicationInternalOnlyFloor.LowerWhenSafeAsync(
            entities.Object, CommId, ThreadId, parentNotShareable: false, NullLogger.Instance, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Writer 2: EmailUploadCaptureService (Outlook / add-in "Save to Spaarke")
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static (EmailUploadCaptureService Capture, Mock<IGenericEntityService> Entities, List<DataverseEntity> Created) UploadCapture(
        string? privacy, bool resolverReturnsThread = true, bool withResolver = true, bool withEntities = true,
        Mock<IDataverseService>? dvMock = null, Mock<ICommunicationEnrichmentService>? enrichment = null)
    {
        var created = new List<DataverseEntity>();
        var dv = dvMock ?? new Mock<IDataverseService>();
        dv.Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, string?, CancellationToken>((e, _, _) => created.Add(e))
            .ReturnsAsync((CommId, false));

        var entities = new Mock<IGenericEntityService>();
        if (privacy is not null)
            ThreadReads(entities, privacy);

        var resolver = new Mock<IThreadResolver>();
        resolver.Setup(r => r.ResolveAndAssignAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolverReturnsThread ? ThreadAssignment.NoGrant(ThreadId) : null);

        var associationResolver = new IncomingAssociationResolver(
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
            NullLogger<IncomingAssociationResolver>.Instance);

        var capture = new EmailUploadCaptureService(
            dv.Object, associationResolver, (enrichment ?? new Mock<ICommunicationEnrichmentService>()).Object,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<EmailUploadCaptureService>.Instance,
            withResolver ? resolver.Object : null,
            directThreadAccess: null,
            genericEntityService: withEntities ? entities.Object : null);
        return (capture, entities, created);
    }

    private static SaveRequest EmailSave() => new()
    {
        ContentType = SaveContentType.Email,
        Email = new EmailMetadata
        {
            Subject = "Re: Acme acquisition",
            SenderEmail = "jane@external.com",
            InternetMessageId = "<m065@x.com>",
            Body = "Please file this.",
            IsBodyHtml = false,
        },
    };

    [Fact]
    public async Task EmailUpload_WritesInternalOnlyTrueOnTheCreate_BecauseItsThreadIsUnknownThen()
    {
        var (capture, _, created) = UploadCapture("open");

        await capture.CaptureAsync(EmailSave(), "user-1", CancellationToken.None);

        created.Should().ContainSingle().Which.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Fact]
    public async Task EmailUpload_CaptureJoiningAnOpenThread_IsLoweredToNotInternalOnly()
    {
        var (capture, entities, _) = UploadCapture("open");

        var id = await capture.CaptureAsync(EmailSave(), "user-1", CancellationToken.None);

        id.Should().Be(CommId);
        VerifyLowered(entities);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("novalue")]
    [InlineData("nullrow")]
    [InlineData("throws")]
    public async Task EmailUpload_CaptureJoiningAPrivateNoValueOrUnreadableThread_StaysInternalOnly(string privacy)
    {
        var (capture, entities, _) = UploadCapture(privacy);

        var id = await capture.CaptureAsync(EmailSave(), "user-1", CancellationToken.None);

        id.Should().Be(CommId, "the floor never fails the capture");
        VerifyNeverLowered(entities);
    }

    [Theory]
    [InlineData("no resolved thread")]
    [InlineData("no resolver")]
    [InlineData("no entity service")]
    public async Task EmailUpload_CaptureWithoutAThreadOrTheServiceToCheckIt_StaysInternalOnly(string case_)
    {
        var dv = new Mock<IDataverseService>();
        var (capture, entities, _) = UploadCapture(
            "open",
            resolverReturnsThread: case_ != "no resolved thread",
            withResolver: case_ != "no resolver",
            withEntities: case_ != "no entity service",
            dvMock: dv);

        var id = await capture.CaptureAsync(EmailSave(), "user-1", CancellationToken.None);

        id.Should().Be(CommId, "the capture itself completed; only the lowering was impossible");
        VerifyNeverLowered(entities);
        // With no entity service the OTHER Dataverse service (the capture's own, dv) is the only thing that could write
        // the flag; it must not have lowered it either.
        dv.Verify(
            d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_isinternalonly")), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Fix round 4 (F1 part 1): an upload that carries In-Reply-To / References follows the SAME parent rule as the mailbox path.

    private const string UploadParentMessageId = "<parent@contoso.com>";

    private static SaveRequest EmailSaveWithHeaders(string? inReplyTo = null, string? references = null)
    {
        var save = EmailSave();
        return save with { Email = save.Email! with { InReplyTo = inReplyTo, References = references } };
    }

    /// <summary>The parent communication exists (found by Internet-Message-Id) and carries the named flag.</summary>
    private static void UploadParent(
        Mock<IDataverseService> dv, Mock<IGenericEntityService> entities, string parentMessageId, bool? flag)
    {
        dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync(parentMessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communication", ParentId));
        entities.Setup(g => g.RetrieveAsync(
                "sprk_communication", ParentId, It.Is<string[]>(c => c.Contains("sprk_isinternalonly")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Parent(flag));
    }

    [Fact]
    public async Task EmailUpload_InReplyToNamingAnInternalOnlyParent_StaysInternalOnly()
    {
        var dv = new Mock<IDataverseService>();
        var (capture, entities, _) = UploadCapture("open", dvMock: dv);
        UploadParent(dv, entities, UploadParentMessageId, flag: true);

        var id = await capture.CaptureAsync(EmailSaveWithHeaders(inReplyTo: UploadParentMessageId), "user-1", CancellationToken.None);

        id.Should().Be(CommId);
        VerifyNeverLowered(entities);
    }

    [Fact]
    public async Task EmailUpload_InReplyToNamingAnExplicitlyShareableParent_IsLowered()
    {
        var dv = new Mock<IDataverseService>();
        var (capture, entities, _) = UploadCapture("open", dvMock: dv);
        UploadParent(dv, entities, UploadParentMessageId, flag: false);

        await capture.CaptureAsync(EmailSaveWithHeaders(inReplyTo: UploadParentMessageId), "user-1", CancellationToken.None);

        VerifyLowered(entities);
    }

    [Theory]
    [InlineData(true)]   // internal parent
    [InlineData(null)]   // legacy parent with no flag
    public async Task EmailUpload_ReferencesNamingAParentNotExplicitlyShareable_StaysInternalOnly(bool? flag)
    {
        // No In-Reply-To: the rule walks References newest to oldest, like the mailbox path.
        var dv = new Mock<IDataverseService>();
        var (capture, entities, _) = UploadCapture("open", dvMock: dv);
        UploadParent(dv, entities, UploadParentMessageId, flag);

        await capture.CaptureAsync(
            EmailSaveWithHeaders(references: $"<oldest@contoso.com>  {UploadParentMessageId}\r\n<newer-unknown@contoso.com>"),
            "user-1", CancellationToken.None);

        VerifyNeverLowered(entities);
    }

    [Fact]
    public async Task EmailUpload_BlankReplyHeaders_AreTreatedAsNoHeaders_AndBehaveAsToday()
    {
        var (capture, entities, _) = UploadCapture("open");

        await capture.CaptureAsync(EmailSaveWithHeaders(inReplyTo: "  ", references: " "), "user-1", CancellationToken.None);

        VerifyLowered(entities);
    }

    [Fact]
    public async Task EmailUpload_NoReplyHeaders_BehavesAsToday_LoweredWhenThreadIsOpen_WithNoParentLookup()
    {
        // Owner decision D6 (2026-10-14, final): a no-headers add-in save stays shareable (CommunicationInternalOnlyFloor.AddInSaveWithoutReplyHeadersIsShareable).
        // While that is true, a header-less add-in save is lowered exactly as before and no parent is looked up.
        CommunicationInternalOnlyFloor.AddInSaveWithoutReplyHeadersIsShareable.Should().BeTrue("owner decision D6 keeps header-less add-in saves visible");
        var dv = new Mock<IDataverseService>();
        var (capture, entities, _) = UploadCapture("open", dvMock: dv);

        await capture.CaptureAsync(EmailSave(), "user-1", CancellationToken.None);

        VerifyLowered(entities);
        dv.Verify(d => d.GetCommunicationByInternetMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Task 065 fix round 6 (K1): the add-in capture stores the parent it names, so the filing sweep can see it ──

    [Fact]
    public async Task EmailUpload_WithInReplyTo_StoresItOnTheRow()
    {
        var (capture, _, created) = UploadCapture("open");

        await capture.CaptureAsync(EmailSaveWithHeaders(inReplyTo: UploadParentMessageId), "user-1", CancellationToken.None);

        created.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_inreplyto").Should().Be(UploadParentMessageId);
    }

    [Fact]
    public async Task EmailUpload_WithOnlyReferences_StoresTheNewestReferenceAsTheParent()
    {
        var (capture, _, created) = UploadCapture("open");

        await capture.CaptureAsync(EmailSaveWithHeaders(references: "<oldest@contoso.com>  <newest@contoso.com>"), "user-1", CancellationToken.None);

        created.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_inreplyto").Should().Be("<newest@contoso.com>");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", " ")]
    public async Task EmailUpload_WithNoReplyHeaders_StoresNoInReplyTo(string? inReplyTo, string? references)
    {
        var (capture, _, created) = UploadCapture("open");

        await capture.CaptureAsync(EmailSaveWithHeaders(inReplyTo: inReplyTo, references: references), "user-1", CancellationToken.None);

        created.Should().ContainSingle().Which.Contains("sprk_inreplyto").Should().BeFalse();
    }

    // ── Task 065 fix round 6 (K2): cancellation after the row exists keeps the row internal-only and the capture goes on ──

    [Theory]
    [InlineData("thread read")]
    [InlineData("update")]
    [InlineData("parent read")]
    public async Task EmailUpload_WhenCancelledWhileLowering_RowStaysInternalOnly_AndEnrichmentStillRuns(string where)
    {
        var enrichment = new Mock<ICommunicationEnrichmentService>();
        var dv = new Mock<IDataverseService>();
        var (capture, entities, created) = UploadCapture("open", dvMock: dv, enrichment: enrichment);
        var save = EmailSave();
        switch (where)
        {
            case "thread read":
                entities.Setup(g => g.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new OperationCanceledException());
                break;
            case "update":
                entities.Setup(g => g.UpdateAsync("sprk_communication", CommId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new OperationCanceledException());
                break;
            default:
                // The parent check runs only when the upload carries reply headers.
                dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new OperationCanceledException());
                save = EmailSaveWithHeaders(inReplyTo: UploadParentMessageId);
                break;
        }

        var id = await capture.CaptureAsync(save, "user-1", CancellationToken.None);

        id.Should().Be(CommId, "the capture completes rather than reporting a failure that invites a duplicate on retry");
        created.Should().ContainSingle("no second row");
        created[0].GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
        if (where != "update")
            VerifyNeverLowered(entities); // (for "update" the lowering write itself was attempted and cancelled)
        enrichment.Verify(
            e => e.EnrichAsync(CommId, It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once, "enrichment runs after the cancelled lowering");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Writer 3: IncomingCommunicationProcessor (inbound Graph mailbox email)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static IncomingCommunicationProcessor MailboxProcessor(
        Mock<IDataverseService> dv, IThreadResolver? resolver)
    {
        var accountService = new CommunicationAccountService(
            dv.Object, dv.Object, Mock.Of<IDistributedCache>(), Mock.Of<ILogger<CommunicationAccountService>>());
        var jobOptions = new Mock<IOptions<ServiceBusOptions>>();
        jobOptions.Setup(o => o.Value).Returns(new ServiceBusOptions());
        var jobSubmission = new Mock<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>(
            MockBehavior.Loose,
            jobOptions.Object,
            Mock.Of<ILogger<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object).Object;

        return new IncomingCommunicationProcessor(
            Mock.Of<IGraphClientFactory>(),
            dv.Object,
            dv.Object,
            accountService,
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
            jobSubmission,
            new NotificationService(Mock.Of<IGenericEntityService>(), Mock.Of<ILogger<NotificationService>>()),
            Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(new CommunicationOptions()),
            Mock.Of<ITextExtractor>(),
            Options.Create(new AttachmentMatchOptions { Enabled = false }),
            new ConfigurationBuilder().Build(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<IncomingCommunicationProcessor>.Instance,
            resolver);
    }

    private static NormalizedMessage ReplyEnvelope(string? inReplyTo = null) => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "jane@external.com",
        To = new[] { "intake@contoso.com" },
        Subject = "Re: Acme acquisition",
        InternetMessageId = "<reply@external.com>",
        InReplyTo = inReplyTo,
    };

    private sealed record MailboxWorld(
        IncomingCommunicationProcessor Processor, Mock<IDataverseService> Dv, List<DataverseEntity> Created);

    /// <summary>
    /// <paramref name="parentFlag"/>: "none" (no ancestor row), "true", "false", "unset", "nullrow" or "throws"
    /// (the parent communication row exists and its flag read behaves as named).
    /// </summary>
    private static MailboxWorld Mailbox(string? privacy, string parentFlag = "none", bool resolverReturnsThread = true, bool withResolver = true)
    {
        var created = new List<DataverseEntity>();
        var dv = new Mock<IDataverseService>();
        dv.Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, string?, CancellationToken>((e, _, _) => created.Add(e))
            .ReturnsAsync((CommId, false));
        if (privacy is not null)
        {
            // IDataverseService is the composite: its generic-entity reads are the same ones the floor makes.
            var thread = privacy switch
            {
                "open" => Thread(Open),
                "private" => Thread(Private),
                "novalue" => Thread(null),
                _ => null,
            };
            var setup = dv.Setup(d => d.RetrieveAsync(
                "sprk_communicationthread", ThreadId, It.Is<string[]>(c => c.Contains("sprk_privacystate")), It.IsAny<CancellationToken>()));
            if (privacy == "throws")
                setup.ThrowsAsync(new InvalidOperationException("dataverse unavailable"));
            else
                setup.ReturnsAsync(thread!);
        }

        if (parentFlag != "none")
        {
            dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@contoso.com>", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataverseEntity("sprk_communication", ParentId));
            var parentSetup = dv.Setup(d => d.RetrieveAsync(
                "sprk_communication", ParentId, It.Is<string[]>(c => c.Contains("sprk_isinternalonly")), It.IsAny<CancellationToken>()));
            switch (parentFlag)
            {
                case "true": parentSetup.ReturnsAsync(Parent(true)); break;
                case "false": parentSetup.ReturnsAsync(Parent(false)); break;
                case "unset": parentSetup.ReturnsAsync(Parent(null)); break;
                case "nullrow": parentSetup.ReturnsAsync((DataverseEntity)null!); break;
                case "throws": parentSetup.ThrowsAsync(new InvalidOperationException("dataverse unavailable")); break;
                default: throw new ArgumentOutOfRangeException(nameof(parentFlag));
            }
        }

        var resolver = new Mock<IThreadResolver>();
        resolver.Setup(r => r.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolverReturnsThread ? ThreadId : (Guid?)null);

        return new MailboxWorld(MailboxProcessor(dv, withResolver ? resolver.Object : null), dv, created);
    }

    private static DataverseEntity Parent(bool? flag)
    {
        var parent = new DataverseEntity("sprk_communication", ParentId);
        if (flag is { } f)
            parent["sprk_isinternalonly"] = f;
        return parent;
    }

    private static void VerifyLowered(Mock<IDataverseService> dv) =>
        dv.Verify(
            g => g.UpdateAsync(
                "sprk_communication", CommId,
                It.Is<Dictionary<string, object>>(f => f.Count == 1 && f["sprk_isinternalonly"].Equals(false)),
                It.IsAny<CancellationToken>()),
            Times.Once);

    private static void VerifyNeverLowered(Mock<IDataverseService> dv) =>
        dv.Verify(
            g => g.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task Mailbox_WritesInternalOnlyTrueOnTheCreate_BecauseItsThreadAndParentAreUnknownThen()
    {
        var world = Mailbox("open");

        await world.Processor.CreateCommunicationRecordAsync(
            new Microsoft.Graph.Models.Message { Subject = "Re: Acme", InternetMessageId = "<reply@external.com>" },
            "intake@contoso.com", "graph-1", Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.Unchanged("test"), null, CancellationToken.None);

        world.Created.Should().ContainSingle().Which.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Fact]
    public async Task Mailbox_EmailJoiningAnOpenThreadWithNoParent_IsLoweredToNotInternalOnly()
    {
        var world = Mailbox("open");

        await world.Processor.ResolveThreadAndShareabilityAsync(CommId, ReplyEnvelope(), "graph-1", CancellationToken.None);

        VerifyLowered(world.Dv);
    }

    [Fact]
    public async Task Mailbox_ReplyWhoseParentIsExplicitlyNotInternal_IsLoweredToNotInternalOnly()
    {
        var world = Mailbox("open", parentFlag: "false");

        await world.Processor.ResolveThreadAndShareabilityAsync(
            CommId, ReplyEnvelope("<parent@contoso.com>"), "graph-1", CancellationToken.None);

        VerifyLowered(world.Dv);
    }

    [Theory]
    [InlineData("true")]      // internal parent -> internal
    [InlineData("unset")]     // legacy parent with no flag -> not explicitly shareable -> internal
    [InlineData("nullrow")]   // parent row unreadable -> internal
    [InlineData("throws")]    // parent read faults -> internal (fail closed)
    public async Task Mailbox_ReplyWhoseParentIsInternalUnflaggedOrUnreadable_StaysInternalOnly(string parentFlag)
    {
        var world = Mailbox("open", parentFlag);

        await world.Processor.ResolveThreadAndShareabilityAsync(
            CommId, ReplyEnvelope("<parent@contoso.com>"), "graph-1", CancellationToken.None);

        VerifyNeverLowered(world.Dv);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("novalue")]
    [InlineData("nullrow")]
    [InlineData("throws")]
    public async Task Mailbox_EmailJoiningAPrivateNoValueOrUnreadableThread_StaysInternalOnly(string privacy)
    {
        var world = Mailbox(privacy);

        await world.Processor.ResolveThreadAndShareabilityAsync(CommId, ReplyEnvelope(), "graph-1", CancellationToken.None);

        VerifyNeverLowered(world.Dv);
    }

    [Theory]
    [InlineData("no resolved thread")]
    [InlineData("no resolver")]
    public async Task Mailbox_EmailWithNoResolvedThread_StaysInternalOnly(string case_)
    {
        var world = Mailbox("open", resolverReturnsThread: case_ != "no resolved thread", withResolver: case_ != "no resolver");

        await world.Processor.ResolveThreadAndShareabilityAsync(CommId, ReplyEnvelope(), "graph-1", CancellationToken.None);

        VerifyNeverLowered(world.Dv);
    }

    [Fact]
    public async Task Mailbox_WhenLoweringFaults_DoesNotThrow()
    {
        var world = Mailbox("open");
        world.Dv.Setup(d => d.UpdateAsync("sprk_communication", CommId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        var act = () => world.Processor.ResolveThreadAndShareabilityAsync(CommId, ReplyEnvelope(), "graph-1", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Mailbox_WhenLoweringIsCancelled_DoesNotThrow_AndTheRowIsNotLowered()
    {
        // Fix round 5: cancellation used to escape ResolveThreadAndShareabilityAsync; the retry then deduped on the Graph
        // message id and returned early, so the row (written true) was never lowered. It is now caught like the chat
        // ingestor does: processing completes, the row stays true, nothing else is written.
        var world = Mailbox("open");
        world.Dv.Setup(d => d.UpdateAsync("sprk_communication", CommId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => world.Processor.ResolveThreadAndShareabilityAsync(CommId, ReplyEnvelope(), "graph-1", CancellationToken.None);

        await act.Should().NotThrowAsync();
        world.Dv.Verify(
            d => d.UpdateAsync("sprk_communication", CommId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Mailbox_WhenTheParentReadIsCancelled_DoesNotThrow_AndTheRowIsNotLowered()
    {
        var world = Mailbox("open", parentFlag: "false");
        world.Dv.Setup(d => d.RetrieveAsync(
                "sprk_communication", ParentId, It.Is<string[]>(c => c.Contains("sprk_isinternalonly")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => world.Processor.ResolveThreadAndShareabilityAsync(
            CommId, ReplyEnvelope("<parent@contoso.com>"), "graph-1", CancellationToken.None);

        await act.Should().NotThrowAsync();
        VerifyNeverLowered(world.Dv);
    }

    // ── Fix round 4 (K3): the mailbox parent rule's less-travelled branches ──

    private static NormalizedMessage ReferencesEnvelope(params string[] references) => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "jane@external.com",
        To = new[] { "intake@contoso.com" },
        Subject = "Re: Acme acquisition",
        InternetMessageId = "<reply@external.com>",
        InReplyTo = null,
        References = references,
    };

    [Theory]
    [InlineData("true", false)]    // internal parent found through References -> stays internal
    [InlineData("unset", false)]
    [InlineData("false", true)]    // explicitly shareable parent found through References -> lowered
    public async Task Mailbox_NoInReplyTo_ParentRuleWalksReferencesNewestToOldest(string parentFlag, bool lowered)
    {
        var world = Mailbox("open", parentFlag);

        // References are oldest to newest: the newest is unknown to Dataverse, the parent is older: the walk must go past it.
        await world.Processor.ResolveThreadAndShareabilityAsync(
            CommId, ReferencesEnvelope("<parent@contoso.com>", "<unknown-newer@contoso.com>"), "graph-1", CancellationToken.None);

        if (lowered)
            VerifyLowered(world.Dv);
        else
            VerifyNeverLowered(world.Dv);
    }

    [Fact]
    public async Task Mailbox_ParentFoundOnlyByGraphMessageId_UsesTheGraphIdFallback()
    {
        // Internet-Message-Id finds nothing; the Graph message id lookup finds the (internal-only) parent -> stays internal.
        var world = Mailbox("open");
        world.Dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        world.Dv.Setup(d => d.GetCommunicationByGraphMessageIdAsync("<parent@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communication", ParentId));
        world.Dv.Setup(d => d.RetrieveAsync(
                "sprk_communication", ParentId, It.Is<string[]>(c => c.Contains("sprk_isinternalonly")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Parent(true));

        await world.Processor.ResolveThreadAndShareabilityAsync(
            CommId, ReplyEnvelope("<parent@contoso.com>"), "graph-1", CancellationToken.None);

        world.Dv.Verify(d => d.GetCommunicationByGraphMessageIdAsync("<parent@contoso.com>", It.IsAny<CancellationToken>()), Times.Once);
        VerifyNeverLowered(world.Dv);
    }

    [Fact]
    public async Task Mailbox_InReplyToSetButParentNotInDataverse_IsLowered_BecauseNoKnownAncestorMeansNoParent()
    {
        // Documented rule (CommunicationInternalOnlyFloor.HasParentNotExplicitlyShareableAsync): "A message with no known
        // ancestor has no parent: false". An In-Reply-To whose parent was never captured is not a parent we can judge;
        // only a parent that IS found and is not explicitly shareable (or whose read faults) raises the flag.
        var world = Mailbox("open", parentFlag: "none");

        await world.Processor.ResolveThreadAndShareabilityAsync(
            CommId, ReplyEnvelope("<never-captured@external.com>"), "graph-1", CancellationToken.None);

        VerifyLowered(world.Dv);
    }
}
