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
    /// <summary>A matter the ownership double treats as SECURE (task 176 review 2, F2): the double answers Secure for this id only.</summary>
    private static readonly Guid SecureMatterId = Guid.Parse("17600000-0000-4000-8000-000000000004");
    private static readonly Guid SecureTeamId = Guid.Parse("17600000-0000-4000-8000-0000000000aa");

    private const int DirectionOutbound = 100000001;
    private const int DirectionInbound = 100000000;

    // =====================================================================================
    // Send — the inquiry email as InquiryEmailSender.Build hands it to the communication core
    // =====================================================================================

    private static BudgetInquiryRequest Inquiry(Guid? matterId = null) =>
        new(matterId ?? MatterId, ["counsel@firm.example"], "Budget inquiry: Q3", "Please explain the overrun.", Confirmed: true);

    private static SendCommunicationRequest InquirySend(Guid? matterId = null) =>
        InquiryEmailSender.Build(Inquiry(matterId), ServiceRequestId, new InquiryEmailExtras("Acme v. Roe (MAT-000176)", []));

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
        // Task 176 review 2 (F2): the ownership double answers Secure for SecureMatterId ONLY, so the Secure team below is the
        // answer to "this inquiry's matter is the secure one", not an echo of whatever matter the send named.
        var harness = new SendHarness(
            matterId: SecureMatterId, matterAccessPermission: InheritedAccessPermission.Restricted, secureMatterId: SecureMatterId);

        await harness.Service.SendAsync(InquirySend(SecureMatterId), harness.SignedInUser());

        var row = harness.Communication();
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(SecureMatterId);
        row.GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(SecureTeamId);
        row.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)
            .Should().NotBeNull("the matter was on the row when the stamp ran")
            .And.Subject.As<OptionSetValue>().Value.Should().Be(InheritedAccessPermission.Restricted);
        harness.Ownership.Requests.Should().Contain(c => c.Parents.Any(p => p.RecordId == SecureMatterId));
    }

    [Fact]
    public async Task Send_UnderANonSecureMatter_IsNotSecureOwned_AndTakesTheOrdinaryInheritedAccess()
    {
        // The negative of the test above. The world HOLDS a secure matter (Restricted) and the double knows it, but the inquiry is
        // under a different, ordinary matter (Limited): the row is owned by the ordinary team and carries the ordinary matter's
        // value. A double or a stamp that answered Secure/Restricted for any matter would fail here.
        var harness = new SendHarness(
            matterId: MatterId, matterAccessPermission: InheritedAccessPermission.Limited, secureMatterId: SecureMatterId);

        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());

        var row = harness.Communication();
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId);
        var owner = row.GetAttributeValue<EntityReference>("ownerid")!.Id;
        owner.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId, "an ordinary matter's inquiry is owned by the ordinary team");
        owner.Should().NotBe(SecureTeamId);
        row.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)
            .Should().NotBeNull().And.Subject.As<OptionSetValue>().Value.Should().Be(
                InheritedAccessPermission.Limited, "the ordinary matter's own value, not the secure matter's Restricted");
        harness.Ownership.Requests.Should().Contain(c => c.Parents.Any(p => p.RecordId == MatterId));
        harness.Ownership.Requests.Should().NotContain(c => c.Parents.Any(p => p.RecordId == SecureMatterId),
            "the secure matter is not an input to this inquiry's decisions");
    }

    [Fact]
    public async Task Sender_CarriesTheCoresArchivalWarning_WhenTheArchiveFails_Task176Review2()
    {
        // The harness has no archive store, so the core's archive step fails AFTER the send (as when the matter's container cannot be
        // resolved). The sender must hand the core's warning on with the communication id, not return the id alone.
        var harness = new SendHarness();
        var sender = new InquiryEmailSender(harness.Service, new HttpContextAccessor { HttpContext = harness.SignedInUser() });

        var sent = await sender.SendAsync(Inquiry(), ServiceRequestId, CancellationToken.None);

        sent.CommunicationId.Should().Be(harness.CommunicationId);
        sent.ArchivalWarning.Should().NotBeNullOrWhiteSpace().And.Contain("archival failed");
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
    public async Task Send_TheNewThreadIsAnchoredToTheMatter_ThroughTheSendPathsThreadStep_AndTheRealEmailThreadKey()
    {
        // Decision 2: the find-or-create anchor (the first typed lookup in RegardingFieldMap order - the matter) is "the matter's
        // thread". Task 176 review 2 (F2): driven through the REAL send path's thread step (CommunicationService ->
        // ResolveOutboundThreadAsync -> ThreadResolver) with the REAL EmailThreadKeyStrategy, not a stubbed strategy: the thread
        // is created from the communication row the send itself wrote, and the row is stamped with it.
        var harness = new SendHarness(realThreads: true);

        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());

        var thread = harness.Threads.Should().ContainSingle("the send placed the email in a thread (the sweep is not needed)").Subject;
        thread.GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().NotBeNull("the matter outranks the service request")
            .And.Subject.As<EntityReference>().Id.Should().Be(MatterId);
        thread.Contains("sprk_regardingservicerequest").Should().BeFalse("the thread is the matter's, not the inquiry's");
        thread.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString());

        var stamp = harness.ThreadAssignments.Should().ContainSingle("the communication row is stamped with its thread").Subject;
        stamp.CommunicationId.Should().Be(harness.CommunicationId);
        stamp.ThreadId.Should().Be(harness.CreatedThreadId!.Value);
    }

    /// <summary>The real send over a REAL <see cref="CoreAncestorResolver"/> (in-memory filing world) and an owner double. With
    /// <c>realThreads</c> the send path's thread step runs the REAL <see cref="ThreadResolver"/> over the REAL
    /// <see cref="EmailThreadKeyStrategy"/> (only Dataverse is doubled).</summary>
    private sealed class SendHarness
    {
        private readonly List<DataverseEntity> _created = new();
        private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();

        public SendHarness(
            Guid? matterId = null, int? matterAccessPermission = null, Guid? secureMatterId = null, bool realThreads = false)
        {
            var matter = matterId ?? MatterId;
            _world.OrdinaryRoot("sprk_matter", matter);
            if (matterAccessPermission is { } level)
                _world.Set("sprk_matter", matter, InheritedAccessPermission.Column, new OptionSetValue(level));

            if (secureMatterId is { } secure)
            {
                // The secure matter the double knows. When the inquiry is under another matter it is only a distractor in the world.
                if (secure != matter)
                {
                    _world.OrdinaryRoot("sprk_matter", secure);
                    _world.Set("sprk_matter", secure, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));
                }

                // Keyed on the ONE secure matter id (review 2, F2): any other matter gets the ordinary answer.
                Ownership = new RecordOwnershipResolverDouble
                {
                    AnswerFor = ctx => ctx.TargetRecordId == secure || ctx.Parents.Any(p => p.RecordId == secure)
                        ? RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true }
                        : null,
                };
            }
            else
            {
                Ownership = new RecordOwnershipResolverDouble();
            }

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
                .Returns<DataverseEntity, CancellationToken>((e, _) =>
                {
                    _created.Add(e);
                    var id = e.LogicalName switch
                    {
                        "sprk_communication" => CommunicationId,
                        "sprk_communicationthread" => (CreatedThreadId = Guid.NewGuid()).Value,
                        _ => Guid.NewGuid(),
                    };
                    e.Id = id;
                    return Task.FromResult(id);
                });
            entities.Setup(e => e.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, id, fields, _) =>
                {
                    if (fields.TryGetValue("sprk_communicationthread", out var t) && t is EntityReference thread)
                        ThreadAssignments.Add((id, thread.Id));
                })
                .Returns(Task.CompletedTask);
            // The thread step reads the communication row the send wrote (its regarding lookups) to anchor the thread.
            entities.Setup(e => e.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(Communication()));
            var communications = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);

            var dispatcher = new CommunicationChannelDispatcher(
                new ICommunicationChannelSender[] { new AcceptingSender() }, Array.Empty<ICommunicationArchiver>());

            IThreadResolver? threadResolver = realThreads
                ? new ThreadResolver(
                    [new EmailThreadKeyStrategy(communications.Object, entities.Object)],
                    entities.Object, Ownership, NullLogger<ThreadResolver>.Instance)
                : null;

            Service = new CommunicationService(
                dispatcher, validator, communications.Object, entities.Object, Mock.Of<IDocumentDataverseService>(),
                accountService: null!, jobSubmissionService: null!, Mock.Of<ICommunicationEnrichmentService>(),
                Options.Create(options), Ancestors(), Ownership, NullLogger<CommunicationService>.Instance,
                threadResolver: threadResolver);
        }

        public CommunicationService Service { get; }

        public RecordOwnershipResolverDouble Ownership { get; }

        /// <summary>The id the created <c>sprk_communication</c> row gets (the create answers it).</summary>
        public Guid CommunicationId { get; } = Guid.Parse("17600000-0000-4000-8000-0000000000c1");

        /// <summary>The id of the thread the thread step created (null until one is).</summary>
        public Guid? CreatedThreadId { get; private set; }

        /// <summary>Every <c>sprk_communicationthread</c> row created.</summary>
        public IEnumerable<DataverseEntity> Threads => _created.Where(e => e.LogicalName == "sprk_communicationthread");

        /// <summary>Every thread stamp written to a communication (communication id, thread id).</summary>
        public List<(Guid CommunicationId, Guid ThreadId)> ThreadAssignments { get; } = new();

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

        result.InquiryReply.Should().BeTrue("step 4.6b keeps a reply to an inquiry internal-only");
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
        result.InquiryReply.Should().BeFalse();
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
        result.InquiryReply.Should().BeFalse();
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

    // -------------------------------------------------------------------------------------
    // Review round 2, item 4: the reply's denormalized primary is the service request, as the send's is
    // -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)] // the processor adds the matter (the usual reply: the tracking token names the inquiry)
    [InlineData(true)]  // the rungs already filed it to the inquiry's matter too
    public async Task Reply_ToAnInquiry_KeepsTheServiceRequestAsTheDenormalizedPrimary_LikeTheSend(bool matterAlreadyFiled)
    {
        IAssociationRung rung = matterAlreadyFiled
            ? new FilingRungs(("sprk_regardingservicerequest", "sprk_servicerequest", ServiceRequestId), ("sprk_regardingmatter", "sprk_matter", MatterId))
            : FiledToTheServiceRequest();
        var processor = Processor(ReplyWorld(MatterId).Resolver(),
            ServiceRequestRead(() => ServiceRequest(DirectionOutbound, MatterId)).Object, rung);

        var result = await processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        result.InquiryReply.Should().BeTrue();
        result.FilingFields.Should().NotBeNull();
        result.FilingFields!["sprk_regardingrecordid"].Should().Be(ServiceRequestId.ToString("D"),
            "the send's primary is the service request; a reply whose primary is the matter puts the matter in the next "
            + "reply's footer, and that reply resolves to the matter alone, losing the inquiry rule");
        ((EntityReference)result.FilingFields["sprk_regardingservicerequest"]).Id.Should().Be(ServiceRequestId);
        ((EntityReference)result.FilingFields["sprk_regardingmatter"]).Id.Should().Be(MatterId, "the matter stays the extra typed lookup");
    }

    [Theory]
    [InlineData(DirectionInbound)]
    [InlineData(null)]
    public async Task Reply_FiledToANonInquiryServiceRequestAndAMatter_KeepsTheMatterAsItsPrimary(int? direction)
    {
        // Must-not-fire: the global regarding priority (matter before service request) is unchanged for every other email.
        var processor = Processor(ReplyWorld(MatterId).Resolver(),
            ServiceRequestRead(() => ServiceRequest(direction, MatterId)).Object,
            new FilingRungs(("sprk_regardingservicerequest", "sprk_servicerequest", ServiceRequestId), ("sprk_regardingmatter", "sprk_matter", MatterId)));

        var result = await processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        result.InquiryReply.Should().BeFalse();
        result.FilingFields.Should().NotBeNull();
        result.FilingFields!["sprk_regardingrecordid"].Should().Be(MatterId.ToString("D"));
    }

    // -------------------------------------------------------------------------------------
    // Review round 2, item 6: the matter the reply is filed under is in its provenance (the review surface's list)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Reply_ToAnInquiry_ListsItsMatterInTheProvenanceAsFiled()
    {
        var processor = Processor(ReplyWorld(MatterId).Resolver(),
            ServiceRequestRead(() => ServiceRequest(DirectionOutbound, MatterId)).Object, FiledToTheServiceRequest());

        var result = await processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        var matter = result.Decision.Provenance.Candidates.Should()
            .ContainSingle(c => c.Field == "sprk_regardingmatter").Subject;
        matter.TargetEntity.Should().Be("sprk_matter");
        matter.TargetId.Should().Be(MatterId.ToString("D"));
        matter.Written.Should().BeTrue("the reply IS filed under it");
        matter.Conflict.Should().BeFalse();
        result.Decision.Provenance.Candidates.Should().Contain(c => c.Field == "sprk_regardingservicerequest" && c.Written);
        var json = result.FilingFields!["sprk_associationprovenance"].Should().BeOfType<string>().Subject;
        json.Should().Contain("sprk_regardingmatter").And.Contain(MatterId.ToString("D"));
    }

    // -------------------------------------------------------------------------------------
    // Review round 2, item 1: the reply's internal-only wiring, through the real filing AND the real steps 4.5-4.6b
    // -------------------------------------------------------------------------------------

    private static readonly Guid ReplyThreadId = Guid.Parse("17600000-0000-4000-8000-0000000000c1");
    private static readonly Guid ReplyRowId = Guid.Parse("17600000-0000-4000-8000-0000000000c2");
    private static readonly Guid EarlierInquiryEmailId = Guid.Parse("17600000-0000-4000-8000-0000000000c3");

    private sealed record ThreadStepWorld(
        IncomingCommunicationProcessor Processor, Mock<IGenericEntityService> Entities, Mock<IThreadResolver> Resolver);

    /// <summary>
    /// A reply that lands in an OPEN matter thread. <paramref name="parent"/>: "shareable-parent" — its In-Reply-To finds an
    /// inquiry email sent before task 176, explicitly NOT internal-only; "no-parent" — its In-Reply-To and References find
    /// nothing. Both are shapes step 4.6b LOWERS for any other email.
    /// </summary>
    private static ThreadStepWorld ThreadStep(int? direction, string parent)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_servicerequest", ServiceRequestId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceRequest(direction, MatterId));
        var thread = new DataverseEntity("sprk_communicationthread", ReplyThreadId)
        {
            ["sprk_privacystate"] = new OptionSetValue((int)Sprk.Bff.Api.Services.Communication.Membership.ThreadPrivacyState.Open),
        };
        entities.Setup(e => e.RetrieveAsync("sprk_communicationthread", ReplyThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

        var communications = new Mock<ICommunicationDataverseService>();
        if (parent == "shareable-parent")
        {
            communications.Setup(c => c.GetCommunicationByInternetMessageIdAsync("<inquiry@contoso.com>", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataverseEntity("sprk_communication", EarlierInquiryEmailId));
            entities.Setup(e => e.RetrieveAsync("sprk_communication", EarlierInquiryEmailId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataverseEntity("sprk_communication", EarlierInquiryEmailId) { ["sprk_isinternalonly"] = false });
        }
        else if (parent != "no-parent")
        {
            throw new ArgumentOutOfRangeException(nameof(parent));
        }

        var resolver = new Mock<IThreadResolver>();
        resolver.Setup(r => r.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplyThreadId);

        var processor = Processor(ReplyWorld(MatterId).Resolver(), entities.Object, FiledToTheServiceRequest(),
            communications.Object, resolver.Object);
        return new ThreadStepWorld(processor, entities, resolver);
    }

    private static async Task FileAndThread(ThreadStepWorld world)
    {
        var envelope = ReplyEnvelope();
        var filing = await world.Processor.ResolveInboundFilingAsync(envelope, account: null, "graph-176", CancellationToken.None);
        await world.Processor.FileAndThreadNewRecordAsync(
            ReplyRowId, envelope, "graph-176", filing.Decision, filing.FilingFields, filing.InquiryReply, CancellationToken.None);
    }

    private static void VerifyLoweredTimes(ThreadStepWorld world, Times times) =>
        world.Entities.Verify(
            e => e.UpdateAsync("sprk_communication", ReplyRowId,
                It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_isinternalonly") && f["sprk_isinternalonly"].Equals(false)),
                It.IsAny<CancellationToken>()),
            times);

    public static TheoryData<string> ReplyParents => new() { "shareable-parent", "no-parent" };

    [Theory]
    [MemberData(nameof(ReplyParents))]
    public async Task Reply_ToAnInquiry_ThroughTheRealFilingAndThreadSteps_StaysInternalOnly_InAnOpenThread(string parent)
    {
        var world = ThreadStep(DirectionOutbound, parent);

        await FileAndThread(world);

        world.Resolver.Verify(r => r.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyLoweredTimes(world, Times.Never());
    }

    [Theory]
    [MemberData(nameof(ReplyParents))]
    public async Task Reply_ToANonInquiryServiceRequest_ThroughTheSameSteps_IsLowered_TheControl(string parent)
    {
        // Must-fire control: the same thread and parent lower any other reply, so the test above fails if the inquiry flag
        // stops reaching step 4.6b.
        var world = ThreadStep(DirectionInbound, parent);

        await FileAndThread(world);

        VerifyLoweredTimes(world, Times.Once());
    }

    private static IAssociationRung FiledToTheServiceRequest() =>
        new FilingRung("sprk_regardingservicerequest", "sprk_servicerequest", ServiceRequestId);

    /// <summary>Several deterministic explicit references at once (each field its own target).</summary>
    private sealed class FilingRungs(params (string Field, string Entity, Guid Id)[] filings) : IAssociationRung
    {
        public RungKind Kind => RungKind.ExplicitReference;
        public int Order => 0;

        public Task<IReadOnlyList<RungMatch>> EvaluateAsync(NormalizedMessage message, AssociationContext context, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RungMatch>>(filings
                .Select(f => new RungMatch
                {
                    RegardingFieldName = f.Field,
                    Target = new EntityReference(f.Entity, f.Id),
                    Confidence = 1.0,
                    Provenance = "test:tracking-token",
                    Rung = RungKind.ExplicitReference,
                })
                .ToList());
    }

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
        IRecordOwnershipResolver ownership, IGenericEntityService entities, IAssociationRung rung,
        ICommunicationDataverseService? communications = null, IThreadResolver? threadResolver = null)
    {
        var autoFile = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var mapper = new AssociationStatusMapper(
            new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == autoFile)),
            NullLogger<AssociationStatusMapper>.Instance);
        var association = new IncomingAssociationResolver(
            [rung], Mock.Of<ICommunicationDataverseService>(), Mock.Of<IGenericEntityService>(), mapper,
            CoreAncestorResolverFixtures.Inert(), new RecordOwnershipResolverDouble(), NullLogger<IncomingAssociationResolver>.Instance);

        return new IncomingCommunicationProcessor(
            graphClientFactory: null!, communicationService: communications!, genericEntityService: entities, accountService: null!,
            associationResolver: association, messageNormalizer: new GraphMessageNormalizer(),
            emlConverter: null!, scopeFactory: null!, jobSubmissionService: null!, notificationService: null!,
            enrichmentService: null!, options: Options.Create(new CommunicationOptions()), textExtractor: null!,
            attachmentMatchOptions: Options.Create(new AttachmentMatchOptions { Enabled = false }),
            configuration: new ConfigurationBuilder().Build(), ownership: ownership,
            logger: NullLogger<IncomingCommunicationProcessor>.Instance, threadResolver: threadResolver);
    }
}
