using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// FR-11 (spaarke-SPA-external-access-platform-r3 task 034): <c>POST /api/v1/external/{root}/{id}/messages</c>.
/// Driven through <see cref="ExternalMessageEndpoints.PostMessageCoreAsync"/> → the PRODUCTION module registry (built by
/// <c>AddExternalAccess</c>: the real Tier-2 predicates, partner switch included) → the REAL
/// <see cref="CommunicationService.PostToRecordThreadAsync"/> (real filing step, real creator stamp). Doubled are only the
/// I/O boundaries: the Dataverse create (<see cref="IGenericEntityService"/>, which records the row), the gate's record
/// read, the ownership resolver, the thread resolver, and the channel senders — which THROW, so any email/chat send fails
/// the test. The read-back runs the REAL task-033 read over the row the post created, as Dataverse would return it.
/// </summary>
public class ExternalMessageSendTests
{
    private const int PlainText = 100000000;
    private const int Html = 100000001;
    private const int Incoming = 100000000;
    private const int Outgoing = 100000001;
    private const int StatusSend = 659490002;
    private const int StatusDelivered = 659490003;
    private const int TypeMessage = 100000004;

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly List<Entity> _created = new();
    private readonly Mock<IThreadResolver> _threads = new();
    private readonly ThrowingSender _emailSender = new(CommunicationType.Email);
    private readonly ThrowingSender _messageSender = new(CommunicationType.Message);
    private readonly Mock<ICommunicationDataverseService> _communicationDataverse = new(MockBehavior.Strict);
    private readonly RecordOwnershipResolverDouble _ownership = new();
    private readonly List<(string Entity, Guid Id, string[]? Select)> _recordReads = new();
    private readonly Dictionary<(string Entity, Guid Id), IReadOnlyDictionary<string, object?>> _records = new();
    private readonly Guid _threadId = Guid.NewGuid();

    // Read-back (task 033 read) doubles.
    private readonly Mock<IAppOnlyCommunicationQuery> _appOnly = new();
    private readonly Mock<ISystemUserIdentityResolver> _identity = new(MockBehavior.Strict);
    private string? _lastReadOData;

    public ExternalMessageSendTests()
    {
        _entities
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(() => Guid.NewGuid());
        _threads
            .Setup(t => t.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_threadId);
    }

    /// <summary>A channel sender that fails the test if the post ever reaches it (no email, no chat send — FR-11).</summary>
    private sealed class ThrowingSender : ICommunicationChannelSender
    {
        public ThrowingSender(CommunicationType type) => SupportedType = type;
        public CommunicationType SupportedType { get; }
        public int Calls { get; private set; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException($"FR-11: a thread post must not send ({SupportedType}).");
        }
    }

    // ── principals ──────────────────────────────────────────────────────────────────────────────────

    private static CallerPrincipal Ciam(
        Guid? project = null,
        ExternalAccessLevel projectLevel = ExternalAccessLevel.Collaborate,
        Guid? matter = null,
        AccessRights matterRights = AccessRights.Read | AccessRights.Create | AccessRights.Write,
        Guid? workAssignment = null,
        AccessRights workAssignmentRights = AccessRights.Read | AccessRights.Create | AccessRights.Write,
        Guid? contactId = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contactId ?? Guid.NewGuid(),
        Email = "partner@outside.test",
        ProjectAccess = project is { } p
            ? new[] { CallerProjectAccess.FromLevel(p, projectLevel) }
            : Array.Empty<CallerProjectAccess>(),
        MatterAccess = matter is { } m
            ? new Dictionary<Guid, AccessRights> { [m] = matterRights }
            : new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = workAssignment is { } w
            ? new Dictionary<Guid, AccessRights> { [w] = workAssignmentRights }
            : new Dictionary<Guid, AccessRights>(),
    };

    private static CallerPrincipal WorkforceContact(Guid? project = null, Guid? contactId = null) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = contactId ?? Guid.NewGuid(),
        SystemUserId = null,
        Email = "staff@customer.test",
        ProjectAccess = project is { } p
            ? new[] { CallerProjectAccess.FromLevel(p, ExternalAccessLevel.Collaborate) }
            : Array.Empty<CallerProjectAccess>(),
    };

    private static CallerPrincipal SystemUser(Guid systemUserId, Guid? project = null) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(), // a linked contact: the KIND is still systemuser (task 030 rule)
        SystemUserId = systemUserId,
        Email = "lawyer@spaarke.test",
        ProjectAccess = project is { } p
            ? new[] { CallerProjectAccess.FromLevel(p, ExternalAccessLevel.FullAccess) }
            : Array.Empty<CallerProjectAccess>(),
    };

    // ── fixture ─────────────────────────────────────────────────────────────────────────────────────

    private static ExternalModuleRegistry ProductionRegistry(bool partnerServiceRequestsEnabled = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ExternalAccess:PartnerServiceRequestsEnabled"] = partnerServiceRequestsEnabled ? "true" : "false",
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddExternalAccess();
        return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
    }

    private CommunicationService Service()
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = Array.Empty<ApprovedSenderConfig>(),
            DefaultMailbox = "noreply@contoso.com",
        };
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options), accountService, Mock.Of<IDistributedCache>(), Mock.Of<ILogger<ApprovedSenderValidator>>());
        var dispatcher = new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { _emailSender, _messageSender },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        return new CommunicationService(
            dispatcher,
            senderValidator,
            _communicationDataverse.Object, // Strict: no systemuser/oid lookup (no OBO-style resolution) on this path
            _entities.Object,
            Mock.Of<IDocumentDataverseService>(MockBehavior.Strict),
            accountService,
            null!, // JobSubmissionService — a thread post enqueues nothing
            Mock.Of<ICommunicationEnrichmentService>(MockBehavior.Strict),
            Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            _ownership,
            Mock.Of<ILogger<CommunicationService>>(),
            _threads.Object);
    }

    private Task<IReadOnlyDictionary<string, object?>> ReadRecord(
        string entity, Guid id, string[]? select, CancellationToken ct)
    {
        _recordReads.Add((entity, id, select));
        return _records.TryGetValue((entity, id), out var record)
            ? Task.FromResult(record)
            : throw new RecordNotFoundException(entity, id);
    }

    private Task<IResult> Post(
        string root, Guid id, CallerPrincipal caller, string? body = "Can you confirm the filing date?",
        ExternalModuleRegistry? registry = null) =>
        Post(root, id, caller, new PostExternalMessageRequest { SprkBody = body }, registry);

    private Task<IResult> Post(
        string root, Guid id, CallerPrincipal caller, PostExternalMessageRequest? request,
        ExternalModuleRegistry? registry = null, Action<MessagePostedNotification>? onPosted = null) =>
        ExternalMessageEndpoints.PostMessageCoreAsync(
            root, id, request, caller, registry ?? ProductionRegistry(), ReadRecord, Service(), NullLogger.Instance,
            CancellationToken.None, onPosted);

    /// <summary>A request body bound exactly as the route binds it (System.Text.Json, web defaults).</summary>
    private static PostExternalMessageRequest Bind(string json) =>
        JsonSerializer.Deserialize<PostExternalMessageRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static Dictionary<string, object?> Lookup(Guid id, string entity) =>
        new(StringComparer.OrdinalIgnoreCase) { ["id"] = id, ["logicalName"] = entity, ["name"] = "n" };

    private Guid ServiceRequestRequestedBy(Guid contactId)
    {
        var request = Guid.NewGuid();
        _records[("sprk_servicerequest", request)] = new Dictionary<string, object?>
        {
            ["id"] = request,
            ["sprk_requestedby"] = Lookup(contactId, "contact"),
        };
        return request;
    }

    private static ExternalMessageDto Created(IResult result)
    {
        var created = result.Should().BeOfType<Created<ExternalMessageDto>>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        return created.Value!;
    }

    private static void ShouldBeForbidden(IResult result, string reasonCode)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(reasonCode);
    }

    private static void ShouldBeBadRequest(IResult result, string errorCode)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be(errorCode);
    }

    private void ShouldHaveWrittenNothing()
    {
        _created.Should().BeEmpty("a refused post creates no row");
        _entities.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        _threads.VerifyNoOtherCalls();
    }

    private void ShouldHaveSentNothing()
    {
        _emailSender.Calls.Should().Be(0, "no email is sent (FR-11)");
        _messageSender.Calls.Should().Be(0, "no chat/ACS send either");
        _communicationDataverse.VerifyNoOtherCalls();
    }

    private Entity TheRow() => _created.Should().ContainSingle("exactly one sprk_communication").Subject;

    // ── the task-033 read over the created row ──────────────────────────────────────────────────────

    /// <summary>The created row as the app-only read would get it back from Dataverse (lookups as _x_value with their
    /// FormattedValue names; option sets as numbers).</summary>
    private static Dictionary<string, JsonElement> AsDataverseRow(
        Entity row, Guid id, string createdOn, string? sentByName = null, string? createdByContactName = null)
    {
        var result = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString("D")),
            ["createdon"] = El(createdOn),
            ["statecode"] = El(((OptionSetValue)row["statecode"]).Value),
        };
        foreach (var (key, value) in row.Attributes)
        {
            switch (value)
            {
                case OptionSetValue option:
                    result[key] = El(option.Value);
                    break;
                case EntityReference reference:
                    result[$"_{key}_value"] = El(reference.Id.ToString("D"));
                    var name = key switch
                    {
                        "sprk_sentby" => sentByName,
                        "sprk_createdbycontact" => createdByContactName,
                        _ => null,
                    };
                    if (name is not null)
                        result[$"_{key}_value@OData.Community.Display.V1.FormattedValue"] = El(name);
                    break;
                case string s:
                    result[key] = El(s);
                    break;
                case bool b:
                    result[key] = El(b);
                    break;
            }
        }

        return result;
    }

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);

    private Task<IResult> ReadBack(string root, Guid id, CallerPrincipal caller, params Dictionary<string, JsonElement>[] rows)
    {
        _appOnly
            .Setup(q => q.QueryAsync("sprk_communications", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, odata, _) => _lastReadOData = odata)
            .ReturnsAsync(rows.ToList());
        var read = new CommunicationThreadReadService(
            Mock.Of<IImpersonatedCommunicationQuery>(MockBehavior.Strict),
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            Mock.Of<ICallerSystemUserResolver>(MockBehavior.Strict),
            _identity.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>(),
            _appOnly.Object);
        return ExternalMessageEndpoints.GetMessagesCoreAsync(
            root, id, caller, ProductionRegistry(), ReadRecord, read, NullLogger.Instance, CancellationToken.None);
    }

    private static IReadOnlyList<ExternalMessageDto> Messages(IResult result) =>
        result.Should().BeOfType<Ok<ExternalCollectionResponse<ExternalMessageDto>>>().Subject.Value!.Value;

    // ── AC 1: accessible record → 201, one row on the record's thread, visible through the task-033 read ──────────

    [Theory]
    [InlineData("projects", "sprk_project", "sprk_regardingproject")]
    [InlineData("matters", "sprk_matter", "sprk_regardingmatter")]
    [InlineData("workassignments", "sprk_workassignment", "sprk_regardingworkassignment")]
    public async Task AccessibleRecord_ContactPost_Is201_OneRowRegardingTheRecord_AndTheThreadReadShowsItWithTheSender(
        string root, string entity, string regardingField)
    {
        var record = Guid.NewGuid();
        var me = Guid.NewGuid();
        var caller = root switch
        {
            "projects" => Ciam(project: record, contactId: me),
            "matters" => Ciam(matter: record, contactId: me),
            _ => Ciam(workAssignment: record, contactId: me),
        };

        var dto = Created(await Post(root, record, caller, "Can you confirm the filing date?"));

        var row = TheRow();
        row.LogicalName.Should().Be("sprk_communication");
        row.GetAttributeValue<EntityReference>(regardingField).Should().BeEquivalentTo(new EntityReference(entity, record));
        row.GetAttributeValue<OptionSetValue>("sprk_communicationtype").Value.Should().Be(TypeMessage);
        row.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(StatusDelivered);
        row.GetAttributeValue<OptionSetValue>("sprk_direction").Value.Should().Be(Incoming);
        row.GetAttributeValue<OptionSetValue>("sprk_bodyformat").Value.Should().Be(PlainText);
        row["sprk_isinternalonly"].Should().Be(false, "never internal-only, and explicitly so (the read hides an unset flag)");
        row.GetAttributeValue<string>("sprk_body").Should().Be("Can you confirm the filing date?");
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId);
        dto.SprkBody.Should().Be("Can you confirm the filing date?");
        dto.SprkBodyformat.Should().Be(PlainText);
        dto.SprkDirection.Should().Be(Incoming);

        // Filed in the record's thread by the existing message-thread step (no ACS id → the record's default thread).
        _threads.Verify(t => t.ResolveAndAssignThreadAsync(
            It.Is<ThreadResolutionRequest>(r => r.ChannelType == CommunicationType.Message && r.AcsThreadId == null),
            It.IsAny<CancellationToken>()), Times.Once);
        ShouldHaveSentNothing();

        // Read-back through the task-033 read, over the row as Dataverse returns it.
        var id = Guid.Parse(dto.SprkCommunicationid);
        var messages = Messages(await ReadBack(root, record, caller,
            AsDataverseRow(row, id, "2026-10-11T09:00:00Z", createdByContactName: "Pat Partner")));

        messages.Should().ContainSingle();
        messages[0].SprkCommunicationid.Should().Be(dto.SprkCommunicationid);
        messages[0].SprkBody.Should().Be("Can you confirm the filing date?");
        messages[0].SenderName.Should().Be("Pat Partner", "the contact author's name comes from sprk_createdbycontact");
        _lastReadOData.Should().Contain("_sprk_createdbycontact_value");
    }

    [Fact]
    public async Task SystemUserPost_Is201_Outgoing_SentByTheCaller_AndTheThreadReadShowsTheSender()
    {
        var project = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        var caller = SystemUser(systemUserId, project);
        _identity.Setup(i => i.IsExternalAsync(systemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var dto = Created(await Post("projects", project, caller));

        var row = TheRow();
        row.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(StatusSend);
        row.GetAttributeValue<OptionSetValue>("sprk_direction").Value.Should().Be(Outgoing);
        row.GetAttributeValue<EntityReference>("sprk_sentby").Should().BeEquivalentTo(new EntityReference("systemuser", systemUserId));

        var messages = Messages(await ReadBack("projects", project, caller,
            AsDataverseRow(row, Guid.Parse(dto.SprkCommunicationid), "2026-10-11T09:00:00Z", sentByName: "Lee Lawyer")));

        messages.Should().ContainSingle().Which.SenderName.Should().Be("Lee Lawyer");
    }

    [Fact]
    public async Task InvoiceUnderAMatterTheCallerCanCreateOn_Is201_RegardingTheInvoice()
    {
        var matter = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _records[("sprk_invoice", invoice)] = new Dictionary<string, object?>
        {
            ["id"] = invoice,
            ["sprk_matter"] = Lookup(matter, "sprk_matter"),
        };

        Created(await Post("invoices", invoice, Ciam(matter: matter)));

        TheRow().GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(invoice);
    }

    // ── AC 2: outside the caller's set → 403, no row (foreign record, nonexistent id, no grants at all) ──────────

    [Fact]
    public async Task AnotherPartysProject_Is403_AndCreatesNoRow()
    {
        var result = await Post("projects", Guid.NewGuid(), Ciam(project: Guid.NewGuid()));

        ShouldBeForbidden(result, ExternalMessageEndpoints.DenyRecordNotAccessible);
        ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task AnotherPartysInvoice_AndANonexistentInvoice_GetTheSame403_AndCreateNoRow()
    {
        var myMatter = Guid.NewGuid();
        var theirInvoice = Guid.NewGuid();
        _records[("sprk_invoice", theirInvoice)] = new Dictionary<string, object?>
        {
            ["id"] = theirInvoice,
            ["sprk_matter"] = Lookup(Guid.NewGuid(), "sprk_matter"),
        };

        var theirs = await Post("invoices", theirInvoice, Ciam(matter: myMatter));
        var missing = await Post("invoices", Guid.NewGuid(), Ciam(matter: myMatter));

        ShouldBeForbidden(theirs, ExternalMessageEndpoints.DenyRecordNotAccessible);
        ShouldBeForbidden(missing, ExternalMessageEndpoints.DenyRecordNotAccessible);
        ((ProblemHttpResult)theirs).ProblemDetails.Detail.Should().Be(((ProblemHttpResult)missing).ProblemDetails.Detail);
        ShouldHaveWrittenNothing();
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    [InlineData("invoices")]
    [InlineData("servicerequests")]
    public async Task NonexistentRecord_Is403_NotA404_AndCreatesNoRow(string root)
    {
        var caller = Ciam(project: Guid.NewGuid(), matter: Guid.NewGuid(), workAssignment: Guid.NewGuid());

        var result = await Post(root, Guid.NewGuid(), caller, registry: ProductionRegistry(partnerServiceRequestsEnabled: true));

        ShouldBeForbidden(result, ExternalMessageEndpoints.DenyRecordNotAccessible);
        ShouldHaveWrittenNothing();
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    [InlineData("invoices")]
    public async Task CallerWithNoRecordGrants_Is403_WithNoDataverseReadOrWrite(string root)
    {
        var result = await Post(root, Guid.NewGuid(), Ciam());

        ShouldBeForbidden(result, ExternalMessageEndpoints.DenyRecordNotAccessible);
        _recordReads.Should().BeEmpty();
        ShouldHaveWrittenNothing();
    }

    // ── Create is required: a view-only caller reads the thread but cannot post ──────────────────────────────────

    [Fact]
    public async Task ViewOnlyProjectCaller_Is403InsufficientRights_AndCreatesNoRow()
    {
        var project = Guid.NewGuid();

        var result = await Post("projects", project, Ciam(project: project, projectLevel: ExternalAccessLevel.ViewOnly));

        ShouldBeForbidden(result, ExternalMessageEndpoints.DenyInsufficientRights);
        ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task ReadOnlyMatterOrWorkAssignmentCaller_Is403InsufficientRights_AndCreatesNoRow()
    {
        var matter = Guid.NewGuid();
        var workAssignment = Guid.NewGuid();
        var caller = Ciam(
            matter: matter, matterRights: AccessRights.Read,
            workAssignment: workAssignment, workAssignmentRights: AccessRights.Read);

        ShouldBeForbidden(await Post("matters", matter, caller), ExternalMessageEndpoints.DenyInsufficientRights);
        ShouldBeForbidden(await Post("workassignments", workAssignment, caller), ExternalMessageEndpoints.DenyInsufficientRights);
        ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task InvoiceUnderAReadOnlyMatter_Is403InsufficientRights_AndCreatesNoRow()
    {
        var matter = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _records[("sprk_invoice", invoice)] = new Dictionary<string, object?>
        {
            ["id"] = invoice,
            ["sprk_matter"] = Lookup(matter, "sprk_matter"),
        };

        var result = await Post("invoices", invoice, Ciam(matter: matter, matterRights: AccessRights.Read));

        ShouldBeForbidden(result, ExternalMessageEndpoints.DenyInsufficientRights);
        ShouldHaveWrittenNothing();
    }

    // ── AC 3: the stored sender is the caller, whatever the request names ────────────────────────────────────────

    private const string BodyNamingAnotherSender = """
        {
          "sprk_body": "Hello",
          "sprk_createdbycontact": "11111111-1111-1111-1111-111111111111",
          "sprk_CreatedByContact@odata.bind": "/contacts(11111111-1111-1111-1111-111111111111)",
          "sprk_createdbyperson": "22222222-2222-2222-2222-222222222222",
          "sprk_CreatedByPerson@odata.bind": "/systemusers(22222222-2222-2222-2222-222222222222)",
          "createdby": "22222222-2222-2222-2222-222222222222",
          "sprk_sentby": "22222222-2222-2222-2222-222222222222",
          "sprk_from": "ceo@spaarke.test",
          "sender": "someone-else@outside.test",
          "sprk_bodyformat": 100000001,
          "sprk_isinternalonly": true
        }
        """;

    [Fact]
    public async Task ContactCaller_RequestNamingAnotherSender_IsStoredWithTheCallersContactStamp_Only()
    {
        var project = Guid.NewGuid();
        var me = Guid.NewGuid();

        Created(await Post("projects", project, WorkforceContact(project, contactId: me), Bind(BodyNamingAnotherSender)));

        var row = TheRow();
        row.GetAttributeValue<EntityReference>("sprk_createdbycontact").Should().BeEquivalentTo(new EntityReference("contact", me));
        row.Attributes.Should().NotContainKey("sprk_createdbyperson");
        row.Attributes.Should().NotContainKey("sprk_sentby");
        row.GetAttributeValue<string>("sprk_from").Should().Be("staff@customer.test");
        row.GetAttributeValue<OptionSetValue>("sprk_bodyformat").Value.Should().Be(PlainText);
        row["sprk_isinternalonly"].Should().Be(false);
        ShouldHaveSentNothing();
    }

    [Fact]
    public async Task SystemUserCaller_RequestNamingAnotherSender_IsStoredWithTheCallersPersonStamp_Only()
    {
        var project = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();

        Created(await Post("projects", project, SystemUser(systemUserId, project), Bind(BodyNamingAnotherSender)));

        var row = TheRow();
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Should().BeEquivalentTo(new EntityReference("systemuser", systemUserId));
        row.GetAttributeValue<EntityReference>("sprk_sentby").Id.Should().Be(systemUserId);
        row.Attributes.Should().NotContainKey("sprk_createdbycontact", "the kind decides: a systemuser with a linked contact is a systemuser");
        row.GetAttributeValue<string>("sprk_from").Should().Be("lawyer@spaarke.test");
    }

    // ── AC 4: no email, no Graph ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APost_NeverReachesAChannelSender_OrAnyIdentityLookup()
    {
        var project = Guid.NewGuid();

        Created(await Post("projects", project, Ciam(project: project)));

        ShouldHaveSentNothing();
        TheRow().Attributes.Keys.Should().NotContain(new[] { "sprk_graphmessageid", "sprk_acsmessageid", "sprk_acsthreadid", "sprk_to" });
    }

    // ── AC 5: attachments, empty and over-long bodies → 400 ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{ "sprk_body": "see attached", "attachments": [ { "name": "a.pdf" } ] }""")]
    [InlineData("""{ "sprk_body": "see attached", "attachmentDocumentIds": [ "a" ] }""")]
    [InlineData("""{ "sprk_body": "see attached", "sprk_hasattachments": true }""")]
    [InlineData("""{ "sprk_body": "see attached", "Files": [ "x" ] }""")]
    public async Task ARequestWithAttachments_Is400_BeforeAnyReadOrWrite(string json)
    {
        var project = Guid.NewGuid();

        var result = await Post("projects", project, Ciam(project: project), Bind(json));

        ShouldBeBadRequest(result, ExternalMessageEndpoints.ErrorAttachmentsNotSupported);
        _recordReads.Should().BeEmpty();
        ShouldHaveWrittenNothing();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public async Task AnEmptyBody_Is400_AndCreatesNoRow(string? body)
    {
        var project = Guid.NewGuid();

        var result = await Post("projects", project, Ciam(project: project), body);

        ShouldBeBadRequest(result, ExternalMessageEndpoints.ErrorInvalidBody);
        ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task AMissingRequestBody_Is400_AndCreatesNoRow()
    {
        var project = Guid.NewGuid();

        var result = await Post("projects", project, Ciam(project: project), (PostExternalMessageRequest?)null);

        ShouldBeBadRequest(result, ExternalMessageEndpoints.ErrorInvalidBody);
        ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task AnOverLongBody_Is400_AndCreatesNoRow_ButTheLimitItselfIsAccepted()
    {
        var project = Guid.NewGuid();
        var caller = Ciam(project: project);

        var tooLong = await Post("projects", project, caller, new string('x', CommunicationService.MaxThreadPostBodyLength + 1));
        ShouldBeBadRequest(tooLong, ExternalMessageEndpoints.ErrorInvalidBody);
        ShouldHaveWrittenNothing();

        Created(await Post("projects", project, caller, new string('x', CommunicationService.MaxThreadPostBodyLength)));
        TheRow().GetAttributeValue<string>("sprk_body").Should().HaveLength(10_000);
    }

    // ── review 033 K1: a script-bearing HTML body is stored as plain TEXT, never as HTML ─────────────────────────

    [Fact]
    public async Task AScriptBearingHtmlBody_IsStoredAndReadBackAsPlainText_NeverAsHtml()
    {
        const string hostile = "<script>alert(document.cookie)</script><img src=x onerror=\"fetch('//evil')\"><p>hi</p>";
        var project = Guid.NewGuid();
        var me = Guid.NewGuid();
        var caller = Ciam(project: project, contactId: me);

        var dto = Created(await Post("projects", project, caller,
            Bind($$"""{ "sprk_body": {{JsonSerializer.Serialize(hostile)}}, "sprk_bodyformat": {{Html}} }""")));

        var row = TheRow();
        row.GetAttributeValue<OptionSetValue>("sprk_bodyformat").Value.Should().Be(PlainText,
            "an external author's markup is text to every reader; the format is never taken from the request");
        row.GetAttributeValue<string>("sprk_body").Should().Be(hostile, "stored verbatim; every renderer escapes a plain-text body");
        dto.SprkBodyformat.Should().Be(PlainText);

        var read = Messages(await ReadBack("projects", project, caller,
            AsDataverseRow(row, Guid.Parse(dto.SprkCommunicationid), "2026-10-11T09:00:00Z")));
        read.Should().ContainSingle().Which.SprkBodyformat.Should().Be(PlainText);
    }

    // ── AC 6: the requester posts on their own service request; anyone else gets 403 ─────────────────────────────

    [Fact]
    public async Task Requester_PostsOnTheirOwnServiceRequest_Is201_RegardingTheRequest()
    {
        var me = Guid.NewGuid();
        var request = ServiceRequestRequestedBy(me);

        Created(await Post("servicerequests", request, WorkforceContact(contactId: me), "Here is the answer to your question."));

        var row = TheRow();
        row.GetAttributeValue<EntityReference>("sprk_regardingservicerequest").Id.Should().Be(request);
        row.GetAttributeValue<EntityReference>("sprk_createdbycontact").Id.Should().Be(me);
        _recordReads.Should().ContainSingle().Which.Select.Should().Equal("sprk_requestedby");
    }

    [Fact]
    public async Task SomeoneElsesServiceRequest_Is403_AndCreatesNoRow()
    {
        var request = ServiceRequestRequestedBy(Guid.NewGuid());

        var result = await Post("servicerequests", request, WorkforceContact(contactId: Guid.NewGuid()));

        ShouldBeForbidden(result, ExternalMessageEndpoints.DenyRecordNotAccessible);
        ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task CiamRequester_PartnerSwitchOff_Is403_AndSwitchOn_Posts()
    {
        var me = Guid.NewGuid();
        var request = ServiceRequestRequestedBy(me);
        var caller = Ciam(contactId: me);

        ShouldBeForbidden(
            await Post("servicerequests", request, caller, registry: ProductionRegistry(partnerServiceRequestsEnabled: false)),
            ExternalMessageEndpoints.DenyRecordNotAccessible);
        ShouldHaveWrittenNothing();

        Created(await Post("servicerequests", request, caller, registry: ProductionRegistry(partnerServiceRequestsEnabled: true)));
        TheRow().GetAttributeValue<EntityReference>("sprk_createdbycontact").Id.Should().Be(me);
    }

    // ── unattributable principal → 403 before any read or write ──────────────────────────────────────────────────

    [Fact]
    public async Task AnUnattributablePrincipal_Is403_BeforeAnyReadOrWrite()
    {
        var project = Guid.NewGuid();
        var nobody = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.Empty,
            SystemUserId = null,
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(project, ExternalAccessLevel.FullAccess) },
        };
        var emptySystemUser = SystemUser(Guid.Empty, project);

        ShouldBeForbidden(await Post("projects", project, nobody), ExternalMessageEndpoints.DenySenderUnattributable);
        ShouldBeForbidden(await Post("projects", project, emptySystemUser), ExternalMessageEndpoints.DenySenderUnattributable);
        _recordReads.Should().BeEmpty();
        ShouldHaveWrittenNothing();
    }

    // ── unsupported root → 404, nothing read or written ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("documents")]
    [InlineData("events")]
    [InlineData("contacts")]
    public async Task UnsupportedRoot_Is404_AndCreatesNoRow(string root)
    {
        var result = await Post(root, Guid.NewGuid(), Ciam(project: Guid.NewGuid()));

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        _recordReads.Should().BeEmpty();
        ShouldHaveWrittenNothing();
    }

    // ── the filing step's refusal surfaces, and writes nothing ───────────────────────────────────────────────────

    [Fact]
    public async Task AnOwnerRefusal_Is409_AndCreatesNoRow()
    {
        var project = Guid.NewGuid();
        _ownership.TeamId = null;

        var result = await Post("projects", project, Ciam(project: project));

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        // The pass-through uses the same extension key as the endpoint's own 400/404/500 answers (review fix): errorCode.
        problem.ProblemDetails.Extensions.Should().ContainKey("errorCode").And.NotContainKey("code");
        problem.ProblemDetails.Extensions["errorCode"].Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
        ShouldHaveWrittenNothing();
    }

    // ── review fix: isMine on the 201, and the thread request carries the row's direction ─────────────────────────

    [Fact]
    public async Task TheCreatedResponse_IsMine_ForAContactAndForASystemUser()
    {
        var project = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();

        Created(await Post("projects", project, Ciam(project: project))).IsMine.Should().BeTrue();
        Created(await Post("projects", project, SystemUser(systemUserId, project))).IsMine.Should().BeTrue();
        JsonSerializer.Serialize(Created(await Post("projects", project, Ciam(project: project))))
            .Should().Contain("\"isMine\":true");
    }

    [Fact]
    public async Task ContactPost_ThreadRequestIsIncoming_MatchingTheRow()
    {
        var project = Guid.NewGuid();
        ThreadResolutionRequest? seen = null;
        _threads
            .Setup(t => t.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ThreadResolutionRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(_threadId);

        Created(await Post("projects", project, Ciam(project: project)));

        TheRow().GetAttributeValue<OptionSetValue>("sprk_direction").Value.Should().Be(Incoming);
        seen.Should().NotBeNull();
        seen!.Direction.Should().Be(CommunicationDirection.Incoming, "the resolver request matches the row's direction");
        seen.Message.Direction.Should().Be(CommunicationDirection.Incoming);
    }

    [Fact]
    public async Task SystemUserPost_ThreadRequestIsOutgoing_MatchingTheRow()
    {
        var project = Guid.NewGuid();
        ThreadResolutionRequest? seen = null;
        _threads
            .Setup(t => t.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ThreadResolutionRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(_threadId);

        Created(await Post("projects", project, SystemUser(Guid.NewGuid(), project)));

        TheRow().GetAttributeValue<OptionSetValue>("sprk_direction").Value.Should().Be(Outgoing);
        seen!.Direction.Should().Be(CommunicationDirection.Outgoing);
        seen.Message.Direction.Should().Be(CommunicationDirection.Outgoing);
    }

    // ── Task 036 (FR-07): the member notification hand-off ───────────────────────────────────────────────────────

    private static CallerPrincipal CreateOn(Guid? matter = null, Guid? project = null, Guid? contactId = null,
        AccessRights matterRights = AccessRights.Read | AccessRights.Create | AccessRights.Write,
        ExternalAccessLevel projectLevel = ExternalAccessLevel.Collaborate) =>
        Ciam(project: project, projectLevel: projectLevel, matter: matter, matterRights: matterRights, contactId: contactId);

    private Guid InvoiceUnder(Guid? matter, Guid? project)
    {
        var invoice = Guid.NewGuid();
        var record = new Dictionary<string, object?> { ["id"] = invoice };
        if (matter is { } m) record["sprk_matter"] = Lookup(m, "sprk_matter");
        if (project is { } p) record["sprk_project"] = Lookup(p, "sprk_project");
        _records[("sprk_invoice", invoice)] = record;
        return invoice;
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    public async Task APostedMessage_HandsTheRecordAsItsOwnRoot_ToTheMemberNotification_OnceWithTheAuthorsContact(string root)
    {
        var record = Guid.NewGuid();
        var me = Guid.NewGuid();
        var caller = root switch
        {
            "projects" => Ciam(project: record, contactId: me),
            "matters" => Ciam(matter: record, contactId: me),
            _ => Ciam(workAssignment: record, contactId: me),
        };
        var seen = new List<MessagePostedNotification>();

        var dto = Created(await Post(root, record, caller, new PostExternalMessageRequest { SprkBody = "Hello" }, onPosted: seen.Add));

        var posted = seen.Should().ContainSingle("one post, one notification").Subject;
        var expected = root switch
        {
            "projects" => ExternalGrantRootType.Project,
            "matters" => ExternalGrantRootType.Matter,
            _ => ExternalGrantRootType.WorkAssignment,
        };
        posted.Roots.Should().Equal(new MessageNotifyRoot(expected, record));
        posted.AuthorContactId.Should().Be(me, "the author is the resolved principal's contact, so they are never told");
        posted.CommunicationId.Should().NotBe(Guid.Empty);
        dto.SprkBody.Should().Be("Hello");
        ShouldHaveSentNothing(); // the thread post itself still sends no email (FR-11); the members' email is the notifier's
    }

    [Fact]
    public async Task ACallbackThatThrows_NeverFailsOrChangesThePost_ItIsStillA201WithOneRow()
    {
        var project = Guid.NewGuid();

        var result = await Post("projects", project, Ciam(project: project),
            new PostExternalMessageRequest { SprkBody = "Hello" },
            onPosted: _ => throw new InvalidOperationException("the launcher blew up"));

        Created(result).SprkBody.Should().Be("Hello");
        TheRow();
    }

    [Fact]
    public async Task ARefusedPost_NeverNotifiesAnyone()
    {
        var project = Guid.NewGuid();
        var viewOnly = Ciam(project: project, projectLevel: ExternalAccessLevel.ViewOnly);
        var foreign = Ciam(project: Guid.NewGuid());
        var seen = new List<MessagePostedNotification>();

        var denied = await Post("projects", project, viewOnly, new PostExternalMessageRequest { SprkBody = "Hello" }, onPosted: seen.Add);
        var outside = await Post("projects", project, foreign, new PostExternalMessageRequest { SprkBody = "Hello" }, onPosted: seen.Add);
        var blank = await Post("projects", project, Ciam(project: project), new PostExternalMessageRequest { SprkBody = " " }, onPosted: seen.Add);

        ShouldBeForbidden(denied, ExternalMessageEndpoints.DenyInsufficientRights);
        ShouldBeForbidden(outside, ExternalMessageEndpoints.DenyRecordNotAccessible);
        ShouldBeBadRequest(blank, ExternalMessageEndpoints.ErrorInvalidBody);
        seen.Should().BeEmpty("no row was created, so there is nothing to tell anyone");
        ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task AnInvoicePost_NotifiesOnlyTheParentRootsTheCallerHoldsCreateOn_NeverTheOtherParentsMembers()
    {
        var matter = Guid.NewGuid();
        var project = Guid.NewGuid();
        var invoice = InvoiceUnder(matter, project);
        var seen = new List<MessagePostedNotification>();

        // Create on the matter; the project is readable only (ViewOnly) — the post is admitted through the matter.
        Created(await Post("invoices", invoice,
            CreateOn(matter: matter, project: project, projectLevel: ExternalAccessLevel.ViewOnly),
            new PostExternalMessageRequest { SprkBody = "Question on line 3" }, onPosted: seen.Add));

        seen.Should().ContainSingle().Which.Roots.Should().Equal(
            new MessageNotifyRoot(ExternalGrantRootType.Matter, matter));
    }

    [Fact]
    public async Task AnInvoicePost_UnderTwoParentsTheCallerCanCreateOn_NotifiesBoth()
    {
        var matter = Guid.NewGuid();
        var project = Guid.NewGuid();
        var invoice = InvoiceUnder(matter, project);
        var seen = new List<MessagePostedNotification>();

        Created(await Post("invoices", invoice, CreateOn(matter: matter, project: project),
            new PostExternalMessageRequest { SprkBody = "Question" }, onPosted: seen.Add));

        seen.Should().ContainSingle().Which.Roots.Should().BeEquivalentTo(new[]
        {
            new MessageNotifyRoot(ExternalGrantRootType.Matter, matter),
            new MessageNotifyRoot(ExternalGrantRootType.Project, project),
        });
    }

    [Fact]
    public async Task AServiceRequestPost_HasNoMembers_SoNoNotificationIsHandedOver()
    {
        var me = Guid.NewGuid();
        var request = ServiceRequestRequestedBy(me);
        var seen = new List<MessagePostedNotification>();

        Created(await Post("servicerequests", request, WorkforceContact(contactId: me),
            new PostExternalMessageRequest { SprkBody = "Any update?" }, onPosted: seen.Add));

        seen.Should().BeEmpty();
        TheRow();
    }

    [Fact]
    public async Task TheBackgroundLauncher_ReturnsAtOnce_WhileTheNotificationIsStillRunning_AndLogsItsFault()
    {
        // The notifier is resolved from a NEW scope and blocks there; the launcher must have returned long before.
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(GrantAccessNotifier)))
            .Returns(() =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                throw new InvalidOperationException("the notifier could not be built");
            });
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(f => f.CreateScope()).Returns(scope.Object);
        var logger = new Mock<ILogger>();
        var faulted = new TaskCompletionSource();
        logger.Setup(l => l.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => faulted.TrySetResult());
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var launch = ExternalMessageEndpoints.BackgroundMemberNotifier(scopes.Object, logger.Object);
        var started = System.Diagnostics.Stopwatch.StartNew();
        launch(new MessagePostedNotification(
            new[] { new MessageNotifyRoot(ExternalGrantRootType.Project, Guid.NewGuid()) }, Guid.NewGuid(), Guid.NewGuid()));
        started.Stop();

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the post's response never waits on the email");
        entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the work did start, in its own scope");
        faulted.Task.IsCompleted.Should().BeFalse("it is still blocked");

        release.Set();
        (await Task.WhenAny(faulted.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(faulted.Task,
            "a fault in the background work is logged, never thrown");
        scope.Verify(s => s.Dispose(), Times.AtLeastOnce);
    }
}
