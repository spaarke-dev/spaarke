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
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Identity;
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
    internal static readonly Guid ServiceRequestId = Guid.Parse("17600000-0000-4000-8000-000000000001");
    internal static readonly Guid MatterId = Guid.Parse("17600000-0000-4000-8000-000000000002");
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

    internal static SendCommunicationRequest InquirySend(Guid? matterId = null) =>
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
    internal sealed class SendHarness
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

            var entities = Entities;
            entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
                .Returns<DataverseEntity, CancellationToken>((e, _) =>
                {
                    _created.Add(e);
                    var id = e.LogicalName switch
                    {
                        // The first communication is the send under test; a later one (a follow-up post) gets its own id.
                        "sprk_communication" => CommunicationRows.Count() == 1 ? CommunicationId : Guid.NewGuid(),
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
                .Returns(() => Task.FromResult(CommunicationRows.First()));
            // Task 176 review 2, item 7: a thread the send created reads back as it was written (a later post into it reads its
            // privacy and regarding); any other thread does not exist.
            entities.Setup(e => e.RetrieveAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, string[] _, CancellationToken _) => Task.FromResult(Threads.FirstOrDefault(t => t.Id == id)!));
            var communications = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
            // Issue #2033: a send as user needs the caller's enabled systemuser.
            communications.Setup(c => c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());

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

        /// <summary>The Dataverse double (every create, read and write the send makes).</summary>
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Loose);

        /// <summary>Every <c>sprk_communication</c> row created, in order.</summary>
        public IEnumerable<DataverseEntity> CommunicationRows => _created.Where(e => e.LogicalName == "sprk_communication");

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
    // Review round 3, item R3-1: the reply whose primary is the SERVICE REQUEST is FILED, not held, and owned from its matter
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// R3-1: the reply of round 2 slice A's shape (pair = the service request, plus <c>sprk_regardingmatter</c> = M, plus
    /// <c>sprk_regardingservicerequest</c>) through the REAL <see cref="IncomingCommunicationProcessor.ResolveInboundFilingAsync"/>:
    /// the real <see cref="IncomingAssociationResolver.OwnershipContextForNewRecordAsync"/> (its stamp derivation over the row as
    /// written), the REAL <see cref="RecordOwnershipResolver"/> and a REAL <see cref="CoreAncestorResolver"/> (the access value).
    /// Only Dataverse is doubled. The ordinary matter sits in a business unit OTHER than the service request's, so the matter's
    /// default team is distinguishable from the request's.
    /// </summary>
    [Theory]
    [InlineData(true)]  // must-fire: a SECURE matter - the Secure team owns, with the secure matter's Restricted value
    [InlineData(false)] // control: an ordinary matter - the matter's default team owns, with the matter's own Limited value
    public async Task Reply_WithTheServiceRequestAsPrimary_IsFiledNotHeld_AndOwnedByItsMattersTeam_R3_1(bool secureMatter)
    {
        var matter = secureMatter ? OtherMatterId : MatterId;
        var ownership = Directory.Standard()
            .WithRecord("sprk_matter", MatterId, Directory.CustomerChildBu, isSecure: false, owningTeam: Directory.CustomerChildTeam)
            .WithSecureRoot("sprk_matter", OtherMatterId)
            .WithRecord("sprk_servicerequest", ServiceRequestId, Directory.ChildBu, owningTeam: Directory.ChildTeam,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) });
        var processor = Processor(ownership.Resolver(),
            ServiceRequestRead(() => ServiceRequest(DirectionOutbound, matter)).Object, FiledToTheServiceRequest(),
            ancestors: AccessValueAncestors());

        var act = () => processor.ResolveInboundFilingAsync(ReplyEnvelope(), account: null, "graph-176", CancellationToken.None);

        // Not held: no ParentUndetermined (the stamp derivation or the evaluation failing), no owner refusal, no container code.
        var result = (await act.Should().NotThrowAsync("the reply is filed, never held")).Subject;
        result.InquiryReply.Should().BeTrue();
        result.Owner.IsRefused.Should().BeFalse();
        result.Owner.IsOwned.Should().BeTrue();
        result.FilingFields.Should().NotBeNull("an owned reply is created WITH its filing");
        result.FilingFields!["sprk_regardingrecordid"].Should().Be(ServiceRequestId.ToString("D"), "the shape under test: the request is the primary");
        ((EntityReference)result.FilingFields["sprk_regardingservicerequest"]).Id.Should().Be(ServiceRequestId);
        ((EntityReference)result.FilingFields["sprk_regardingmatter"]).Id.Should().Be(matter);

        if (secureMatter)
        {
            result.Owner.OwningTeamId.Should().Be(Directory.SecureNamedTeam, "a secure matter's reply is the Secure team's");
            result.Owner.IsSecureOwner.Should().BeTrue();
        }
        else
        {
            result.Owner.OwningTeamId.Should().Be(Directory.CustomerChildTeam,
                "the matter's default team, not the service request's (" + Directory.ChildTeam + ")");
            result.Owner.IsSecureOwner.Should().BeFalse();
        }

        result.FilingFields.Should().ContainKey(InheritedAccessPermission.Column, "the access value comes from the matter");
        ((OptionSetValue)result.FilingFields[InheritedAccessPermission.Column]).Value.Should().Be(
            secureMatter ? InheritedAccessPermission.Restricted : InheritedAccessPermission.Limited);
    }

    /// <summary>A REAL <see cref="CoreAncestorResolver"/> over a world holding both matters with their own Access Permission:
    /// the secure one Restricted, the ordinary one Limited.</summary>
    private static CoreAncestorResolver AccessValueAncestors()
    {
        var world = SecureChildShareWorld.Standard()
            .OrdinaryRoot("sprk_matter", MatterId)
            .SecureRoot("sprk_matter", OtherMatterId);
        world.Set("sprk_matter", MatterId, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Limited));
        world.Set("sprk_matter", OtherMatterId, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));
        var entities = SecureChildShareWorld.EntitiesOver(() => world);
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns) };
                query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
                return Task.FromResult(world.Answer(query).Entities.SingleOrDefault()
                                       ?? throw new InvalidOperationException($"Test: {table} {id} does not exist."));
            });
        return new CoreAncestorResolver(
            entities.Object, InheritedAccessPermissionStampTests.Probe(), NullLogger<CoreAncestorResolver>.Instance);
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

    // -------------------------------------------------------------------------------------
    // Review round 2, item 7 (unified-access-control-r2's ruling): the inquiry's NEW thread is created Private, in the create
    // -------------------------------------------------------------------------------------

    private const int PrivacyOpen = (int)Sprk.Bff.Api.Services.Communication.Membership.ThreadPrivacyState.Open;
    private const int PrivacyPrivate = (int)Sprk.Bff.Api.Services.Communication.Membership.ThreadPrivacyState.Private;

    private static int? PrivacyOf(DataverseEntity thread) =>
        thread.GetAttributeValue<OptionSetValue>(CommunicationInternalOnlyFloor.ThreadPrivacyField)?.Value;

    /// <summary>Every write the double saw that touches a thread row other than its create (an update, upsert, bulk or delete).</summary>
    private static IEnumerable<IInvocation> ThreadWritesAfterCreate(Mock<IGenericEntityService> entities) =>
        entities.Invocations.Where(i =>
            !i.Method.Name.StartsWith("Retrieve", StringComparison.Ordinal)
            && !i.Method.Name.StartsWith("Get", StringComparison.Ordinal)
            && i.Method.Name != nameof(IGenericEntityService.CreateAsync)
            && i.Arguments.Any(a => a is "sprk_communicationthread" or DataverseEntity { LogicalName: "sprk_communicationthread" }));

    [Fact]
    public async Task Send_CreatesTheInquirysMatterThreadPrivate_InTheCreate_AndNeverPatchesItsPrivacy_Item7()
    {
        var harness = new SendHarness(realThreads: true);

        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());

        var thread = harness.Threads.Should().ContainSingle().Subject;
        thread.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId);
        PrivacyOf(thread).Should().Be(PrivacyPrivate,
            "the composer defaults to External, so a follow-up posted into an Open inquiry thread would be partner-visible");
        ThreadWritesAfterCreate(harness.Entities).Should().BeEmpty(
            "Private is written in the SAME create, never by a follow-up PATCH, so the thread is never Open even for a moment");
    }

    [Fact]
    public async Task Send_ANormalMatterEmail_StillCreatesItsThreadOpen_MustNotFire_Item7()
    {
        var harness = new SendHarness(realThreads: true);
        var request = new SendCommunicationRequest
        {
            To = ["counsel@firm.example"],
            Subject = "Hearing dates",
            Body = "Please confirm.",
            BodyFormat = BodyFormat.PlainText,
            SendMode = SendMode.User,
            Associations = [new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId, EntityName = "Acme v. Roe" }],
        };

        await harness.Service.SendAsync(request, harness.SignedInUser());

        var thread = harness.Threads.Should().ContainSingle().Subject;
        thread.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId);
        PrivacyOf(thread).Should().Be(PrivacyOpen, "only the inquiry send asks for a Private thread");
    }

    [Fact]
    public async Task APostIntoTheInquirysThread_MarkedNotInternal_IsStoredInternalOnly_ThroughTheRealSendPath_Item7()
    {
        // The follow-up a user posts into the inquiry's thread with the composer's default (External: isInternalOnly false).
        // The thread is the one the inquiry send created, read back as written; the floor (CommunicationService) raises the
        // post to internal-only because the thread is not Open.
        var harness = new SendHarness(realThreads: true);
        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());
        var threadId = harness.CreatedThreadId!.Value;

        await harness.Service.SendAsync(
            new SendCommunicationRequest
            {
                To = ["counsel@firm.example"],
                Subject = "Re: Budget inquiry: Q3",
                Body = "Following up on the overrun.",
                BodyFormat = BodyFormat.PlainText,
                SendMode = SendMode.User,
                ThreadId = threadId,
                IsInternalOnly = false,
            },
            harness.SignedInUser());

        var post = harness.CommunicationRows.Should().HaveCount(2).And.Subject.Last();
        post.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue(
            "a post into the Private inquiry thread can never be published to partners, whatever the composer sent");
        post.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId, "it is filed under the thread's matter");
    }

    [Fact]
    public async Task AnExternalReadOfTheMattersThreads_ReturnsNeitherTheInquiryThreadNorItsName_Item7()
    {
        // The thread rows are what the inquiry send's create wrote. The impersonated thread query applies its $filter the way
        // Dataverse would (the matter, and the Open-only clause when the read sends it).
        var harness = new SendHarness(realThreads: true);
        await harness.Service.SendAsync(InquirySend(), harness.SignedInUser());
        var thread = harness.Threads.Should().ContainSingle().Subject;
        var name = thread.GetAttributeValue<string>("sprk_name");
        name.Should().NotBeNullOrWhiteSpace();

        var external = ThreadReads(harness.Threads, callerIsExternal: true);
        var byRegarding = await external.ReadByRegardingAsync("sprk_matter", MatterId, Reader(), CancellationToken.None);
        var listed = await external.ListThreadsAsync(Reader(), null, null, null, CancellationToken.None);

        byRegarding.Threads.Should().BeEmpty("the inquiry thread is Private");
        byRegarding.Threads.Select(t => t.Name).Should().NotContain(name);
        listed.Threads.Should().BeEmpty();
        listed.Threads.Select(t => t.Name).Should().NotContain(name);

        // Control: the same rows, read by an internal user, do carry the thread and its name (the double is not empty).
        var internalRead = ThreadReads(harness.Threads, callerIsExternal: false);
        (await internalRead.ReadByRegardingAsync("sprk_matter", MatterId, Reader(), CancellationToken.None))
            .Threads.Select(t => t.Name).Should().Equal(name);
        (await internalRead.ListThreadsAsync(Reader(), null, null, null, CancellationToken.None))
            .Threads.Select(t => t.ThreadId).Should().Equal(thread.Id);
    }

    private static readonly Guid ReaderSystemUserId = Guid.Parse("17600000-0000-4000-8000-0000000000e1");

    private static ClaimsPrincipal Reader() =>
        new(new ClaimsIdentity([new Claim("oid", Guid.NewGuid().ToString())], "test"));

    /// <summary>The REAL <see cref="CommunicationThreadReadService"/> over an impersonated query that answers the thread set
    /// from <paramref name="threads"/> (as written by the create), applying the query's regarding and Open-only clauses.</summary>
    private static CommunicationThreadReadService ThreadReads(IEnumerable<DataverseEntity> threads, bool callerIsExternal)
    {
        var rows = threads.ToList();
        var impersonated = new Mock<IImpersonatedCommunicationQuery>();
        impersonated.Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), ReaderSystemUserId, It.IsAny<CancellationToken>()))
            .Returns<string, string, Guid, CancellationToken>((set, odata, _, _) =>
            {
                if (set != "sprk_communicationthreads")
                    return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(Array.Empty<Dictionary<string, JsonElement>>());

                var openOnly = odata.Contains(CommunicationOpenThreadGate.OpenOnlyThreadFilterClause, StringComparison.Ordinal);
                // The Web API returns every selected lookup as its GUID or an explicit null; the list read selects the typed
                // regarding lookups and withholds a row that does not carry them all (unknown anchor).
                var selectedLookups = System.Text.RegularExpressions.Regex.Match(odata, @"\$select=([^&]*)").Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Where(c => c.StartsWith('_') && c.EndsWith("_value", StringComparison.Ordinal))
                    .ToList();
                var answer = rows
                    .Where(t => !odata.Contains("_sprk_regardingmatter_value eq ", StringComparison.Ordinal)
                                || odata.Contains($"_sprk_regardingmatter_value eq {t.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id}", StringComparison.Ordinal))
                    .Where(t => !openOnly || CommunicationInternalOnlyFloor.IsConfirmedOpen(t))
                    .Select(t =>
                    {
                        var row = new Dictionary<string, JsonElement>
                        {
                            ["sprk_communicationthreadid"] = JsonSerializer.SerializeToElement(t.Id.ToString("D")),
                            ["sprk_name"] = JsonSerializer.SerializeToElement(t.GetAttributeValue<string>("sprk_name")),
                            ["createdon"] = JsonSerializer.SerializeToElement("2026-11-10T09:00:00Z"),
                            ["sprk_ispinned"] = JsonSerializer.SerializeToElement(false),
                        };
                        foreach (var column in selectedLookups)
                            row[column] = JsonSerializer.SerializeToElement(
                                t.GetAttributeValue<EntityReference>(column[1..^"_value".Length])?.Id.ToString("D"));
                        return row;
                    })
                    .ToList();
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(answer);
            });

        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(ReaderSystemUserId.ToString("D")));
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(ReaderSystemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(callerIsExternal);

        return new CommunicationThreadReadService(
            impersonated.Object,
            new CommunicationAccessFilter(NullLogger<CommunicationAccessFilter>.Instance),
            callers.Object,
            identity.Object,
            NullLogger<CommunicationThreadReadService>.Instance);
    }

    [Fact]
    public void TheThreadOptIn_CannotBeSetFromARequestBody_Item7()
    {
        var optIn = typeof(SendCommunicationRequest).GetProperty(
            "CreateThreadPrivate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        optIn.Should().NotBeNull("task 176's private-thread opt-in");
        optIn!.GetMethod!.IsPublic.Should().BeFalse("internal: no caller outside the BFF can set it");
        ((bool)optIn.GetValue(InquirySend())!).Should().BeTrue("only the inquiry send sets it");

        var json = """{"to":["counsel@firm.example"],"subject":"s","body":"b","createThreadPrivate":true,"CreateThreadPrivate":true}""";
        var bound = JsonSerializer.Deserialize<SendCommunicationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        ((bool)optIn.GetValue(bound)!).Should().BeFalse("a JSON body never sets it");
        JsonSerializer.Serialize(InquirySend(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().NotContainEquivalentOf("createThreadPrivate");
    }

    // ── the inbound side: a counterparty reply, through the REAL filing, the REAL ThreadResolver and EmailThreadKeyStrategy ──

    private static readonly Guid ExistingThreadId = Guid.Parse("17600000-0000-4000-8000-0000000000d1");

    /// <summary>
    /// A reply to the inquiry email run through the real <see cref="IncomingCommunicationProcessor.ResolveInboundFilingAsync"/>
    /// and <see cref="IncomingCommunicationProcessor.FileAndThreadNewRecordAsync"/>, whose thread step is the REAL
    /// <see cref="ThreadResolver"/> over the REAL <see cref="EmailThreadKeyStrategy"/>; only Dataverse is doubled. The new row
    /// reads back with the filing it was created with. <c>existingThreadPrivacy</c> null: the reply's In-Reply-To finds
    /// nothing, so the strategy creates a thread; otherwise it finds an earlier email (explicitly shareable) sitting in a
    /// matter thread with that privacy, and the reply joins it.
    /// </summary>
    private sealed class InboundThreadWorld
    {
        private DataverseEntity? _replyRow;

        public InboundThreadWorld(int? direction, int? existingThreadPrivacy)
        {
            Entities.Setup(e => e.RetrieveAsync("sprk_servicerequest", ServiceRequestId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceRequest(direction, MatterId));
            Entities.Setup(e => e.RetrieveAsync("sprk_communication", ReplyRowId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(_replyRow!));
            Entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
                .Returns<DataverseEntity, CancellationToken>((e, _) =>
                {
                    Created.Add(e);
                    e.Id = Guid.NewGuid();
                    return Task.FromResult(e.Id);
                });

            var communications = new Mock<ICommunicationDataverseService>();
            if (existingThreadPrivacy is { } privacy)
            {
                communications.Setup(c => c.GetCommunicationByInternetMessageIdAsync("<inquiry@contoso.com>", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DataverseEntity("sprk_communication", EarlierInquiryEmailId));
                Entities.Setup(e => e.RetrieveAsync("sprk_communication", EarlierInquiryEmailId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DataverseEntity("sprk_communication", EarlierInquiryEmailId)
                    {
                        ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", ExistingThreadId),
                        ["sprk_isinternalonly"] = false,
                    });
                Entities.Setup(e => e.RetrieveAsync("sprk_communicationthread", ExistingThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DataverseEntity("sprk_communicationthread", ExistingThreadId)
                    {
                        [CommunicationInternalOnlyFloor.ThreadPrivacyField] = new OptionSetValue(privacy),
                        ["sprk_threadtype"] = new OptionSetValue(100000000), // Record-Anchored
                        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                    });
            }

            var resolver = new ThreadResolver(
                [new EmailThreadKeyStrategy(communications.Object, Entities.Object)],
                Entities.Object, new RecordOwnershipResolverDouble(), NullLogger<ThreadResolver>.Instance);
            Processor = BudgetInquiryEmailFilingTests.Processor(
                ReplyWorld(MatterId).Resolver(), Entities.Object, FiledToTheServiceRequest(), communications.Object, resolver);
        }

        public Mock<IGenericEntityService> Entities { get; } = new();

        public List<DataverseEntity> Created { get; } = new();

        public IEnumerable<DataverseEntity> CreatedThreads => Created.Where(e => e.LogicalName == "sprk_communicationthread");

        public IncomingCommunicationProcessor Processor { get; }

        public bool InquiryReply { get; private set; }

        public async Task FileAndThreadAsync()
        {
            var envelope = ReplyEnvelope();
            var filing = await Processor.ResolveInboundFilingAsync(envelope, account: null, "graph-176", CancellationToken.None);
            InquiryReply = filing.InquiryReply;
            _replyRow = new DataverseEntity("sprk_communication", ReplyRowId);
            foreach (var (field, value) in filing.FilingFields!)
                _replyRow[field] = value;
            await Processor.FileAndThreadNewRecordAsync(
                ReplyRowId, envelope, "graph-176", filing.Decision, filing.FilingFields, filing.InquiryReply, CancellationToken.None);
        }

        /// <summary>The thread the reply row was last stamped with.</summary>
        public Guid? StampedThread() =>
            ReplyRowUpdates()
                .Select(f => f.TryGetValue("sprk_communicationthread", out var t) ? ((EntityReference)t).Id : (Guid?)null)
                .LastOrDefault(id => id is not null);

        /// <summary>Whether step 4.6b lowered the reply to partner-visible.</summary>
        public bool WasLowered() =>
            ReplyRowUpdates().Any(f => f.TryGetValue("sprk_isinternalonly", out var flag) && flag.Equals(false));

        private IEnumerable<Dictionary<string, object>> ReplyRowUpdates() =>
            Entities.Invocations
                .Where(i => i.Method.Name == nameof(IGenericEntityService.UpdateAsync)
                            && (string)i.Arguments[0] == "sprk_communication" && (Guid)i.Arguments[1] == ReplyRowId)
                .Select(i => (Dictionary<string, object>)i.Arguments[2]);
    }

    [Fact]
    public async Task InboundReply_ToTheInquiry_ThatCreatesAThread_CreatesItPrivate_Item7()
    {
        var world = new InboundThreadWorld(DirectionOutbound, existingThreadPrivacy: null);

        await world.FileAndThreadAsync();

        world.InquiryReply.Should().BeTrue();
        var thread = world.CreatedThreads.Should().ContainSingle().Subject;
        thread.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId, "the reply's thread is the matter's");
        PrivacyOf(thread).Should().Be(PrivacyPrivate, "an inquiry reply that creates its thread creates it Private, in the create");
        ThreadWritesAfterCreate(world.Entities).Should().BeEmpty();
        world.StampedThread().Should().Be(thread.Id);
        world.WasLowered().Should().BeFalse();
    }

    [Fact]
    public async Task InboundReply_ToANonInquiryRequest_ThatCreatesAThread_StillCreatesItOpen_MustNotFire_Item7()
    {
        var world = new InboundThreadWorld(DirectionInbound, existingThreadPrivacy: null);

        await world.FileAndThreadAsync();

        world.InquiryReply.Should().BeFalse();
        PrivacyOf(world.CreatedThreads.Should().ContainSingle().Subject).Should().Be(PrivacyOpen);
    }

    [Fact]
    public async Task InboundReply_ToTheInquiry_ThatJoinsThePrivateInquiryThread_StaysInternalOnly_Item7()
    {
        var world = new InboundThreadWorld(DirectionOutbound, existingThreadPrivacy: PrivacyPrivate);

        await world.FileAndThreadAsync();

        world.CreatedThreads.Should().BeEmpty();
        world.StampedThread().Should().Be(ExistingThreadId);
        ThreadWritesAfterCreate(world.Entities).Should().BeEmpty();
        world.WasLowered().Should().BeFalse();
    }

    [Theory]
    [InlineData(DirectionOutbound, false)] // the inquiry reply: the thread is untouched and the reply stays internal-only
    [InlineData(DirectionInbound, true)]   // control: any other reply joining the same Open thread IS lowered
    public async Task InboundReply_ThatJoinsAnExistingOpenThread_LeavesTheThreadOpen_Condition2_Item7(int direction, bool lowered)
    {
        // Condition 2: the opt-in is create-only. A join never downgrades or upgrades the thread it joins (no write to the
        // thread at all); the inquiry reply stays internal-only through keepInternalOnly. The control proves the thread really
        // reads Open and the parent is shareable, so the inquiry case is kept internal by the inquiry flag alone.
        var world = new InboundThreadWorld(direction, existingThreadPrivacy: PrivacyOpen);

        await world.FileAndThreadAsync();

        world.CreatedThreads.Should().BeEmpty("the reply joins its parent's thread");
        world.StampedThread().Should().Be(ExistingThreadId);
        ThreadWritesAfterCreate(world.Entities).Should().BeEmpty("joining never changes the thread's privacy");
        world.WasLowered().Should().Be(lowered);
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
        ICommunicationDataverseService? communications = null, IThreadResolver? threadResolver = null,
        CoreAncestorResolver? ancestors = null)
    {
        var autoFile = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var mapper = new AssociationStatusMapper(
            new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == autoFile)),
            NullLogger<AssociationStatusMapper>.Instance);
        var association = new IncomingAssociationResolver(
            [rung], Mock.Of<ICommunicationDataverseService>(), Mock.Of<IGenericEntityService>(), mapper,
            ancestors ?? CoreAncestorResolverFixtures.Inert(), new RecordOwnershipResolverDouble(),
            NullLogger<IncomingAssociationResolver>.Instance);

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
