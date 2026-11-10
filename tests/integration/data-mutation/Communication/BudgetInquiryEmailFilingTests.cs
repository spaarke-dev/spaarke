using System.Reflection;
using System.Security.Claims;
using System.ServiceModel;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 176 (D-216) — the budget inquiry email is filed under BOTH the inquiry (the service request) and the matter,
/// internal-only, archived; a reply resolved to an INQUIRY service request gets the matter too. Driven through the REAL
/// <see cref="CommunicationService"/> (send) and the REAL <see cref="IncomingCommunicationProcessor"/> filing step (reply), with
/// only Dataverse and the channel doubled. Assertions are on the entity / filing fields that reach the create.
/// </summary>
/// <remarks>
/// <para>An inquiry is a <c>sprk_servicerequest</c> with <c>sprk_direction</c> = Outbound (100000001), the column the inquiry
/// executor already writes (<c>BudgetInquiryExecutor.DirectionOutbound</c>); an intake request is Inbound.</para>
/// </remarks>
[Trait("status", "new")]
public class BudgetInquiryEmailFilingTests
{
    private static readonly Guid ServiceRequestId = Guid.Parse("17600000-0000-4000-8000-000000000001");
    private static readonly Guid MatterId = Guid.Parse("17600000-0000-4000-8000-000000000002");
    private static readonly Guid OtherMatterId = Guid.Parse("17600000-0000-4000-8000-000000000003");
    private static readonly Guid SecureTeamId = Guid.Parse("17600000-0000-4000-8000-0000000000aa");

    private const int DirectionOutbound = 100000001;
    private const int DirectionInbound = 100000000;

    // =====================================================================================
    // Send — the inquiry email as InquiryEmailSender.Build hands it to the communication core
    // =====================================================================================

    private static BudgetInquiryRequest Inquiry() =>
        new(MatterId, ["counsel@firm.example"], "Budget inquiry: Q3", "Please explain the overrun.", Confirmed: true);

    private static SendCommunicationRequest InquirySend() =>
        InquiryEmailSender.Build(Inquiry(), ServiceRequestId, new InquiryEmailExtras("Acme v. Roe (MAT-000176)", []));

    [Fact]
    public void Build_AsksForTheArchive_AndForInternalOnly()
    {
        var request = InquirySend();

        request.ArchiveToSpe.Should().BeTrue("D-216: the inquiry email is archived as .eml");
        request.IsInternalOnly.Should().BeTrue("D-216: the inquiry email is internal-only");
        request.Associations![0].EntityType.Should().Be("sprk_servicerequest", "the inquiry stays the primary (footer, reply token)");
    }

    [Fact]
    public async Task Send_FilesTheInquiryEmailUnderTheServiceRequest_AndItsMatter_InternalOnly()
    {
        var harness = new SendHarness();

        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());

        var row = harness.Communication();
        row.GetAttributeValue<EntityReference>("sprk_regardingservicerequest")!.Id.Should().Be(ServiceRequestId);
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().NotBeNull("D-216: filed under the matter too")
            .And.Subject.As<EntityReference>().Id.Should().Be(MatterId);
        row.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(ServiceRequestId.ToString(),
            "the pair still names the inquiry: the footer and the reply token are the service request's");
    }

    [Fact]
    public async Task Send_UnderASecureMatter_IsOwnedByTheSecureTeam_AndTakesTheMattersAccessPermission_AtTheStamp()
    {
        // Preflight refuses a secure owner today, so this is the core's own guarantee: the matter is ON the entity when the
        // FR-26 stamp runs, so the inherited Access Permission comes from it, and the owner question names it. Setting the
        // matter after the stamp (the mutation) leaves the row without the matter's Restricted value.
        var harness = new SendHarness(matterAccessPermission: InheritedAccessPermission.Restricted, secureMatter: true);

        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());

        var row = harness.Communication();
        row.GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(SecureTeamId);
        row.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)?.Value
            .Should().Be(InheritedAccessPermission.Restricted, "the matter was on the row when the stamp ran");
        harness.Ownership.Requests.Should().Contain(c =>
            c.TargetRecordId == MatterId || c.Parents.Any(p => p.RecordId == MatterId));
    }

    [Fact]
    public async Task Send_AnyOtherTwoAssociationSender_StillMapsOnlyThePrimary()
    {
        // Not an inquiry: the same two associations with no opt-in keep today's shape — the matter is counted, not written.
        var harness = new SendHarness();
        var request = InquirySend() with
        {
            Associations =
            [
                new CommunicationAssociation { EntityType = "sprk_servicerequest", EntityId = ServiceRequestId, EntityName = "SR" },
                new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId },
            ],
            ArchiveToSpe = false,
            IsInternalOnly = null,
        };

        await harness.Service.SendAsync(request, harness.SignedInUser());

        var row = harness.Communication();
        row.Contains("sprk_regardingmatter").Should().BeFalse();
        row.GetAttributeValue<int>("sprk_associationcount").Should().Be(2);
        row.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Fact]
    public void TheOptIn_CannotBeSetFromARequestBody()
    {
        var optIn = typeof(CommunicationAssociation).GetProperty(
            "AlsoRegarding", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        optIn.Should().NotBeNull("task 176's send opt-in");
        optIn!.GetMethod!.IsPublic.Should().BeFalse("internal: no caller outside the BFF can set it");

        var json = $$"""{"entityType":"sprk_matter","entityId":"{{MatterId}}","alsoRegarding":true,"AlsoRegarding":true}""";
        var bound = JsonSerializer.Deserialize<CommunicationAssociation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        ((bool)optIn.GetValue(bound)!).Should().BeFalse("a JSON body never sets it");
        JsonSerializer.Serialize(InquirySend().Associations![1], new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().NotContainEquivalentOf("alsoRegarding");
    }

    [Fact]
    public async Task Send_TheNewThreadIsAnchoredToTheMatter_ThroughTheRegardingPriorityOrder()
    {
        // Decision 2: the find-or-create anchor (the first typed lookup in RegardingFieldMap order — the matter) is "the
        // matter's thread". The REAL ThreadResolver reads the row the REAL send wrote.
        var harness = new SendHarness();
        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());
        var written = harness.Communication();
        var communicationId = Guid.NewGuid();
        written.Id = communicationId;

        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_communication", communicationId,
                It.Is<string[]>(c => c.Contains("sprk_regardingrecordid")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(written);
        var threads = new List<DataverseEntity>();
        entities.Setup(e => e.CreateAsync(It.Is<DataverseEntity>(x => x.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((x, _) => threads.Add(x))
            .ReturnsAsync(Guid.NewGuid());
        var resolver = new ThreadResolver(
            [new CreateNewStrategy()], entities.Object, new RecordOwnershipResolverDouble(), NullLogger<ThreadResolver>.Instance);

        await resolver.ResolveAndAssignThreadAsync(new ThreadResolutionRequest
        {
            CommunicationId = communicationId,
            ChannelType = CommunicationType.Email,
            Direction = CommunicationDirection.Outgoing,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Outgoing, Subject = "Budget inquiry: Q3" },
        }, CancellationToken.None);

        var thread = threads.Should().ContainSingle().Subject;
        thread.GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().NotBeNull("the matter outranks the service request")
            .And.Subject.As<EntityReference>().Id.Should().Be(MatterId);
        thread.Contains("sprk_regardingservicerequest").Should().BeFalse();
    }

    private sealed class CreateNewStrategy : IThreadKeyStrategy
    {
        public CommunicationType SupportedType => CommunicationType.Email;

        public Task<ThreadKeyResolution> ResolveAsync(ThreadResolutionRequest request, CancellationToken ct) =>
            Task.FromResult(ThreadKeyResolution.CreateNew);

        public Task OnThreadCreatedAsync(Guid threadId, ThreadResolutionRequest request, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>The real send over a REAL <see cref="CoreAncestorResolver"/> (in-memory filing world) and an owner double.</summary>
    private sealed class SendHarness
    {
        private readonly List<DataverseEntity> _created = new();
        private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();

        public SendHarness(int? matterAccessPermission = null, bool secureMatter = false)
        {
            _world.OrdinaryRoot("sprk_matter", MatterId);
            if (matterAccessPermission is { } level)
                _world.Set("sprk_matter", MatterId, InheritedAccessPermission.Column, new OptionSetValue(level));

            Ownership = new RecordOwnershipResolverDouble
            {
                AnswerFor = secureMatter
                    ? ctx => ctx.TargetRecordId == MatterId || ctx.Parents.Any(p => p.RecordId == MatterId)
                        ? RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true }
                        : null
                    : null,
            };

            var options = new CommunicationOptions
            {
                ApprovedSenders = [new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }],
                DefaultMailbox = "noreply@contoso.com",
            };
            var validator = new ApprovedSenderValidator(
                Options.Create(options),
                new CommunicationAccountService(
                    Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                    NullLogger<CommunicationAccountService>.Instance),
                Mock.Of<IDistributedCache>(), NullLogger<ApprovedSenderValidator>.Instance);

            var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
            entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
                .Callback<DataverseEntity, CancellationToken>((e, _) => _created.Add(e))
                .ReturnsAsync(Guid.NewGuid);
            var communications = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);

            var dispatcher = new CommunicationChannelDispatcher(
                new ICommunicationChannelSender[] { new AcceptingSender() }, Array.Empty<ICommunicationArchiver>());

            Service = new CommunicationService(
                dispatcher, validator, communications.Object, entities.Object, Mock.Of<IDocumentDataverseService>(),
                accountService: null!, jobSubmissionService: null!, Mock.Of<ICommunicationEnrichmentService>(),
                Options.Create(options), Ancestors(), Ownership, NullLogger<CommunicationService>.Instance);
        }

        public CommunicationService Service { get; }

        public RecordOwnershipResolverDouble Ownership { get; }

        public DataverseEntity Communication() =>
            _created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;

        public HttpContext SignedInUser() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", Guid.NewGuid().ToString()), new Claim("preferred_username", "user@contoso.com")], "test")),
        };

        private CoreAncestorResolver Ancestors()
        {
            var entities = SecureChildShareWorld.EntitiesOver(() => _world);
            entities
                .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
                {
                    var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns) };
                    query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
                    return Task.FromResult(_world.Answer(query).Entities.SingleOrDefault()
                                           ?? throw new InvalidOperationException($"Test: {table} {id} does not exist."));
                });
            return new CoreAncestorResolver(
                entities.Object, InheritedAccessPermissionStampTests.Probe(), NullLogger<CoreAncestorResolver>.Instance);
        }
    }

    private sealed class AcceptingSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress, ProviderMessageId = "<inquiry@contoso.com>" });
    }

    // =====================================================================================
    // Reply — IncomingCommunicationProcessor.ResolveInboundFilingAsync (the filing the create carries)
    // =====================================================================================

    /// <summary>The world: an ordinary matter, a matter that became SECURE later, and the service request (owned ordinarily).</summary>
    private static Directory ReplyWorld(Guid regardingMatter) => Directory.Standard()
        .WithOrdinaryRoot("sprk_matter", MatterId)
        .WithSecureRoot("sprk_matter", OtherMatterId)
        .WithRecord("sprk_servicerequest", ServiceRequestId, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", regardingMatter) });

    /// <summary>The live read of the service request the reply is filed to.</summary>
    private static Mock<IGenericEntityService> ServiceRequestRead(Func<DataverseEntity?> answer)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_servicerequest", ServiceRequestId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(answer()!));
        return entities;
    }

    private static DataverseEntity ServiceRequest(int? direction, Guid? matter, bool matterKeyAsNull = false)
    {
        var row = new DataverseEntity("sprk_servicerequest", ServiceRequestId);
        if (direction is { } d) row["sprk_direction"] = new OptionSetValue(d);
        if (matter is { } m) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
        else if (matterKeyAsNull) row["sprk_regardingmatter"] = null;
        return row;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reply_ToAnInquiry_IsFiledUnderTheInquiryAndItsMatter_OwnedFromTheMatter(bool matterBecameSecure)
    {
        var matter = matterBecameSecure ? OtherMatterId : MatterId;
        var processor = Processor(ReplyWorld(matter).Resolver(),
            ServiceRequestRead(() => ServiceRequest(DirectionOutbound, matter)).Object, FiledToTheServiceRequest());

        var result = await processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        result.Decision.RegardingWrites.Should().ContainKey("sprk_regardingservicerequest");
        result.Decision.RegardingWrites["sprk_regardingmatter"].Id.Should().Be(matter);
        result.FilingFields.Should().NotBeNull();
        ((EntityReference)result.FilingFields!["sprk_regardingmatter"]).Id.Should().Be(matter);
        ((EntityReference)result.FilingFields["sprk_regardingservicerequest"]).Id.Should().Be(ServiceRequestId);
        if (matterBecameSecure)
            result.Owner.OwningTeamId.Should().Be(Directory.SecureNamedTeam, "the matter became secure after the inquiry was sent");
        else
            result.Owner.OwningTeamId.Should().NotBe(Directory.SecureNamedTeam);
    }

    public static TheoryData<string> UndeterminedMatterReads => new() { "fault", "column-absent", "null-row" };

    [Theory]
    [MemberData(nameof(UndeterminedMatterReads))]
    public async Task Reply_ToAServiceRequestWhoseMatterCannotBeRead_IsHeld_NeverCreatedServiceRequestOnly(string read)
    {
        Func<DataverseEntity?> answer = read switch
        {
            "fault" => () => throw new TimeoutException("Dataverse did not answer"),
            "column-absent" => () => throw new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80041103) },
                new FaultReason("'sprk_servicerequest' entity doesn't contain attribute")),
            "null-row" => () => null,
            _ => throw new ArgumentOutOfRangeException(nameof(read)),
        };
        var processor = Processor(ReplyWorld(MatterId).Resolver(), ServiceRequestRead(answer).Object, FiledToTheServiceRequest());

        var act = () => processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
    }

    [Theory]
    [InlineData(true)]  // the key is present and null
    [InlineData(false)] // the SDK omitted the null column
    public async Task Reply_ToAnInquiryWithAnExplicitlyEmptyMatter_IsFiledAsToday(bool matterKeyAsNull)
    {
        var processor = Processor(ReplyWorld(MatterId).Resolver(),
            ServiceRequestRead(() => ServiceRequest(DirectionOutbound, matter: null, matterKeyAsNull)).Object,
            FiledToTheServiceRequest());

        var result = await processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        result.Decision.RegardingWrites.Keys.Should().Equal("sprk_regardingservicerequest");
    }

    [Theory]
    [InlineData(DirectionInbound)]
    [InlineData(null)]
    public async Task Reply_ToAServiceRequestThatIsNotAnInquiry_IsFiledAsToday(int? direction)
    {
        var processor = Processor(ReplyWorld(MatterId).Resolver(),
            ServiceRequestRead(() => ServiceRequest(direction, MatterId)).Object, FiledToTheServiceRequest());

        var result = await processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        result.Decision.RegardingWrites.Keys.Should().Equal("sprk_regardingservicerequest");
        result.FilingFields!.ContainsKey("sprk_regardingmatter").Should().BeFalse();
    }

    [Fact]
    public async Task AnEmailNotFiledToAServiceRequest_NeverReadsOne()
    {
        var strict = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var processor = Processor(ReplyWorld(MatterId).Resolver(), strict.Object,
            new FilingRung("sprk_regardingmatter", "sprk_matter", MatterId));

        var result = await processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        result.Decision.RegardingWrites.Keys.Should().Equal("sprk_regardingmatter");
        strict.Invocations.Should().BeEmpty();
    }

    private static IAssociationRung FiledToTheServiceRequest() =>
        new FilingRung("sprk_regardingservicerequest", "sprk_servicerequest", ServiceRequestId);

    /// <summary>The tracking token's answer: a deterministic explicit reference to the service request.</summary>
    private sealed class FilingRung(string field, string entity, Guid id) : IAssociationRung
    {
        public RungKind Kind => RungKind.ExplicitReference;
        public int Order => 0;

        public Task<IReadOnlyList<RungMatch>> EvaluateAsync(NormalizedMessage message, AssociationContext context, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RungMatch>>(
            [
                new RungMatch
                {
                    RegardingFieldName = field,
                    Target = new EntityReference(entity, id),
                    Confidence = 1.0,
                    Provenance = "test:tracking-token",
                    Rung = RungKind.ExplicitReference,
                },
            ]);
    }

    private static NormalizedMessage ReplyEnvelope() => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "counsel@firm.example",
        To = ["user@contoso.com"],
        Subject = "Re: Budget inquiry: Q3",
        BodyText = "The overrun is the expert's fee.",
        InReplyTo = "<inquiry@contoso.com>",
    };

    private static IncomingCommunicationProcessor Processor(
        IRecordOwnershipResolver ownership, IGenericEntityService entities, IAssociationRung rung)
    {
        var autoFile = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var mapper = new AssociationStatusMapper(
            new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == autoFile)),
            NullLogger<AssociationStatusMapper>.Instance);
        var association = new IncomingAssociationResolver(
            [rung], Mock.Of<ICommunicationDataverseService>(), Mock.Of<IGenericEntityService>(), mapper,
            CoreAncestorResolverFixtures.Inert(), new RecordOwnershipResolverDouble(), NullLogger<IncomingAssociationResolver>.Instance);

        return new IncomingCommunicationProcessor(
            graphClientFactory: null!, communicationService: null!, genericEntityService: entities, accountService: null!,
            associationResolver: association, messageNormalizer: new GraphMessageNormalizer(),
            emlConverter: null!, scopeFactory: null!, jobSubmissionService: null!, notificationService: null!,
            enrichmentService: null!, options: Options.Create(new CommunicationOptions()), textExtractor: null!,
            attachmentMatchOptions: Options.Create(new AttachmentMatchOptions { Enabled = false }),
            configuration: new ConfigurationBuilder().Build(), ownership: ownership,
            logger: NullLogger<IncomingCommunicationProcessor>.Instance);
    }
}
