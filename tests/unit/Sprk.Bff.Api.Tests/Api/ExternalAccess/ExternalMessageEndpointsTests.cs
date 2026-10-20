using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// FR-04 (spaarke-SPA-external-access-platform-r3 task 033): <c>GET /api/v1/external/{root}/{id}/messages</c>.
/// Driven through the real pipeline below the route: <see cref="ExternalMessageEndpoints.GetMessagesCoreAsync"/> → the
/// PRODUCTION module registry (built by <c>AddExternalAccess</c>, so the real Tier-2 predicates, partner switch included)
/// → the real <see cref="CommunicationThreadReadService.ReadRegardingAppOnlyAsync"/> → the real
/// <see cref="CommunicationAccessFilter"/>. Only the two genuine I/O boundaries are doubled: the app-only Dataverse query
/// (<see cref="IAppOnlyCommunicationQuery"/>, "the rows Dataverse returned") and the app-only record read the gate makes
/// for an invoice or a service request.
/// </summary>
public class ExternalMessageEndpointsTests
{
    private const string CommunicationSet = "sprk_communications";
    private const int Incoming = 100000000;
    private const int Outgoing = 100000001;

    // sprk_privilegeclassification (CommunicationPrivilegeClassification).
    private const int PrivilegeNone = 100000000;
    private const int PrivilegePotentiallyPrivileged = 100000001;
    private const int PrivilegePrivileged = 100000002;

    // sprk_communication Status Reason (Entity.xml statuscode optionset / CommunicationStatus).
    private const int StatusDraft = 1;
    private const int StatusDeleted = 2;
    private const int StatusQueued = 659490001;
    private const int StatusSend = 659490002;
    private const int StatusDelivered = 659490003;
    private const int StatusFailed = 659490004;
    private const int StatusBounded = 659490005;
    private const int StatusRecalled = 659490006;

    private readonly Mock<IAppOnlyCommunicationQuery> _appOnly = new();
    private readonly Mock<IImpersonatedCommunicationQuery> _impersonated = new(MockBehavior.Strict);
    // Strict: a principal kind that reaches IsExternalAsync without an explicit setup fails loudly instead of being
    // treated as internal by a default-false mock.
    private readonly Mock<ISystemUserIdentityResolver> _identity = new(MockBehavior.Strict);
    private readonly List<(string Entity, Guid Id, string[]? Select)> _recordReads = new();
    private readonly Dictionary<(string Entity, Guid Id), IReadOnlyDictionary<string, object?>> _records = new();
    private string? _lastOData;

    // The secure-flag read (owner option B) runs through the REAL ExternalDataService over this fake Web API, which answers
    // the root-set query through $select and omits a null flag.
    private readonly ExternalRootEventReadTests.FakeDataverse _roots = new();

    // ── principals ──────────────────────────────────────────────────────────────────────────────────

    private static CallerPrincipal Ciam(
        Guid? project = null, Guid? matter = null, Guid? workAssignment = null, Guid? contactId = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contactId ?? Guid.NewGuid(),
        Email = "partner@outside.test",
        ProjectAccess = project is { } p
            ? new[] { CallerProjectAccess.FromLevel(p, ExternalAccessLevel.ViewOnly) }
            : Array.Empty<CallerProjectAccess>(),
        MatterAccess = matter is { } m
            ? new Dictionary<Guid, AccessRights> { [m] = AccessRights.Read }
            : new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = workAssignment is { } w
            ? new Dictionary<Guid, AccessRights> { [w] = AccessRights.Read }
            : new Dictionary<Guid, AccessRights>(),
    };

    /// <summary>A workforce CONTACT (no systemuser) — the licence-free customer staff member.</summary>
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
        ContactId = Guid.NewGuid(),
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

    public ExternalMessageEndpointsTests()
    {
        SetupOpenThread();
        // Task 187 x D7 (merge of master into r3): the thread is also a LIVE PARENT of every message (sprk_communicationthread is a
        // lineage lookup of sprk_communication), so the live-parent walk reads it app-only and withholds the row when the thread
        // does not come back (fail closed). The shared Open thread therefore exists in the filing world too, filed under nothing.
        _roots.AddFilingRow("sprk_communicationthread", AppOnlySelectProjection.OpenThreadId);
    }

    private CommunicationThreadReadService ReadService() => new(
        _impersonated.Object, // Strict: any impersonated call fails the test — this plane reads app-only.
        new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
        Mock.Of<ICallerSystemUserResolver>(MockBehavior.Strict),
        _identity.Object,
        Mock.Of<ILogger<CommunicationThreadReadService>>(),
        _appOnly.Object);

    // The double answers in the order the query asks for (createdon DESC, the production query's $orderby), as Dataverse
    // would, so tests can list rows in natural chronological order.
    private void SetupMessages(params Dictionary<string, JsonElement>[] rows) =>
        _appOnly
            .Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, odata, _) => _lastOData = odata)
            // Each row is projected through the query's $select, so a column the query dropped is absent (r3 task 039, K3).
            .Returns<string, string?, CancellationToken>((_, odata, _) =>
                Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(AppOnlySelectProjection.Project(
                    AppOnlySelectProjection.WithOpenThread(rows).OrderByDescending(r => r["createdon"].GetDateTimeOffset()), odata)));

    // Owner decision D7: the thread read shows a row only when its thread reads Open. These cases are not about D7, so every
    // row sits in one Open thread (SetupMessages seeds the lookup).
    private void SetupOpenThread() =>
        _appOnly
            .Setup(q => q.QueryAsync("sprk_communicationthreads", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, CancellationToken>((_, odata, _) =>
                Task.FromResult(AppOnlySelectProjection.OpenThreadAnswer(odata)));

    private Task<IReadOnlyDictionary<string, object?>> ReadRecord(
        string entity, Guid id, string[]? select, CancellationToken ct)
    {
        _recordReads.Add((entity, id, select));
        return _records.TryGetValue((entity, id), out var record)
            ? Task.FromResult(record)
            : throw new RecordNotFoundException(entity, id);
    }

    // Task 187 fix round 1 (R2): the live-parent reader is passed as the route passes it (GetMessagesAsync), the REAL walk over
    // _roots' filing world, so a message row's parent lookups are parsed, walked and decided as in production.
    private Task<IResult> Get(string root, Guid id, CallerPrincipal caller, ExternalModuleRegistry? registry = null)
    {
        var data = _roots.NewService();
        return ExternalMessageEndpoints.GetMessagesCoreAsync(
            root, id, caller, registry ?? ProductionRegistry(), ReadRecord, ReadService(),
            data.GetNonSecureRootsAsync, NullLogger.Instance, CancellationToken.None, data.GetLiveParentRootsAsync);
    }

    /// <summary>A lookup value as RecordService projects it.</summary>
    private static Dictionary<string, object?> Lookup(Guid id, string entity) =>
        new(StringComparer.OrdinalIgnoreCase) { ["id"] = id, ["logicalName"] = entity, ["name"] = "n" };

    private static Dictionary<string, JsonElement> MessageRow(
        Guid id,
        string regardingField,
        Guid regardingId,
        bool? internalOnly,
        string createdOn,
        string subject = "Subject",
        int direction = Incoming,
        int? status = StatusDelivered,
        int? privilege = PrivilegeNone)
    {
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString("D")),
            ["sprk_subject"] = El(subject),
            ["sprk_body"] = El("<p>Body</p>"),
            ["sprk_bodyformat"] = El(100000001),
            ["sprk_direction"] = El(direction),
            ["_sprk_sentby_value"] = El(Guid.NewGuid().ToString("D")),
            ["_sprk_sentby_value@OData.Community.Display.V1.FormattedValue"] = El("Pat Lawyer"),
            ["createdon"] = El(createdOn),
            ["sprk_privilegeclassification"] = El(privilege),
            [$"_{regardingField}_value"] = El(regardingId.ToString("D")),
            // Columns that must never cross the external surface even if Dataverse returned them.
            ["sprk_from"] = El("pat.lawyer@spaarke.test"),
            ["sprk_to"] = El("someone@spaarke.test"),
        };
        if (internalOnly is { } flag)
            row["sprk_isinternalonly"] = El(flag);
        if (status is { } statusCode)
            row["statuscode"] = El(statusCode);
        return row;
    }

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static IReadOnlyList<ExternalMessageDto> Messages(IResult result)
    {
        result.Should().BeOfType<Ok<ExternalCollectionResponse<ExternalMessageDto>>>();
        return ((Ok<ExternalCollectionResponse<ExternalMessageDto>>)result).Value!.Value;
    }

    private static void ShouldBeForbiddenNotAccessible(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ExternalMessageEndpoints.DenyRecordNotAccessible);
    }

    private void ShouldNotHaveReadMessages() =>
        _appOnly.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── accessible record → its thread ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccessibleProject_ReturnsItsThread_NewestLast_AppOnly_KeyedOnTheRecord()
    {
        var project = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        SetupMessages(
            MessageRow(first, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z", "First"),
            MessageRow(second, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z", "Second", Outgoing));

        var result = await Get("projects", project, Ciam(project: project));

        var messages = Messages(result);
        messages.Select(m => m.SprkCommunicationid).Should().Equal(first.ToString("D"), second.ToString("D"));
        messages[0].SprkSubject.Should().Be("First");
        messages[0].SprkBody.Should().Be("<p>Body</p>");
        messages[0].SprkBodyformat.Should().Be(100000001);
        messages[0].SenderName.Should().Be("Pat Lawyer");
        messages[0].Createdon.Should().Be(DateTimeOffset.Parse("2026-10-01T09:00:00Z"));
        messages[1].SprkDirection.Should().Be(Outgoing);

        _lastOData.Should().Contain($"_sprk_regardingproject_value eq {project}")
            .And.Contain("statecode eq 0")
            .And.Contain("(statuscode eq 659490002 or statuscode eq 659490003)")
            .And.Contain("$orderby=createdon desc");
        _lastOData.Should().NotContain("sprk_from").And.NotContain("sprk_to,");
        // Task 037 review K3: the thread read keeps its EXACT projection (body, format and subject included) — only the
        // notification feed's read drops them. Task 187 appends the message's non-root parent lookups (ExternalMultiRootRule.
        // LiveParentSelect("sprk_communication"), ordered by column) after the three root stamps; the thread lookup is one of them;
        // ExternalSelect de-duplicates, so it is selected once, as D7's column.
        _lastOData.Should().StartWith(
            "$select=sprk_communicationid,sprk_body,sprk_bodyformat,sprk_subject,sprk_direction,_sprk_sentby_value," +
            "_sprk_createdbycontact_value,_createdby_value,createdon,sprk_isinternalonly,sprk_privilegeclassification,statuscode," +
            "_sprk_communicationthread_value,_sprk_regardingproject_value,_sprk_regardingmatter_value,_sprk_regardingworkassignment_value," +
            "_sprk_regardinganalysis_value,_sprk_regardingbudget_value,_sprk_regardingevent_value," +
            "_sprk_regardinginvoice_value,_sprk_regardingreportcard_value&" +
            "$expand=createdby($select=applicationid,accessmode)&");
        _impersonated.VerifyNoOtherCalls();
    }

    // ── a message filed to SEVERAL core roots (fix round, task 039; owner decision 2026-10-12, option B) ──
    // A communication filed under an invoice of secure matter M and project P is stamped with both. The thread read is
    // keyed on one lookup, so P's thread used to list M's message to a reader of P alone. It is withheld when a stamped
    // root the caller cannot read is SECURE, or its secure state cannot be read; a non-secure one no longer hides it.

    /// <summary>A caller holding Read on <paramref name="record"/> of the route's root type, its regarding field, and the
    /// stamp column and root type of ANOTHER root (a matter for a project route, a project otherwise).</summary>
    private static (string RegardingField, CallerPrincipal Caller, string OtherStamp, ExternalGrantRootType OtherRoot) RouteOf(
        string root, Guid record) => root switch
    {
        "projects" => ("sprk_regardingproject", Ciam(project: record), "_sprk_regardingmatter_value", ExternalGrantRootType.Matter),
        "matters" => ("sprk_regardingmatter", Ciam(matter: record), "_sprk_regardingproject_value", ExternalGrantRootType.Project),
        _ => ("sprk_regardingworkassignment", Ciam(workAssignment: record), "_sprk_regardingproject_value", ExternalGrantRootType.Project),
    };

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    public async Task AMessageAlsoFiledToASecureRootTheCallerCannotRead_IsWithheld_OnEveryRootRoute(string root)
    {
        var record = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        var (regardingField, caller, otherStamp, otherRoot) = RouteOf(root, record);
        _roots.AddSecureRoot(otherRoot, hidden);
        var plain = Guid.NewGuid();
        var dual = Guid.NewGuid();
        var dualRow = MessageRow(dual, regardingField, record, internalOnly: false, "2026-10-02T09:00:00Z", "Secure matter message");
        dualRow[otherStamp] = El(hidden.ToString("D"));
        SetupMessages(
            MessageRow(plain, regardingField, record, internalOnly: false, "2026-10-01T09:00:00Z", "Plain"),
            dualRow);

        var messages = Messages(await Get(root, record, caller));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(plain.ToString("D"));
        _roots.FlagReads.Should().ContainSingle().Which.Should().Contain(hidden.ToString());
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    public async Task AMessageAlsoFiledToANonSecureRootTheCallerCannotRead_IsListed_OptionB(string root)
    {
        var record = Guid.NewGuid();
        var open = Guid.NewGuid();
        var (regardingField, caller, otherStamp, otherRoot) = RouteOf(root, record);
        _roots.AddOpenRoot(otherRoot, open);
        var dual = Guid.NewGuid();
        var dualRow = MessageRow(dual, regardingField, record, internalOnly: false, "2026-10-02T09:00:00Z");
        dualRow[otherStamp] = El(open.ToString("D"));
        SetupMessages(dualRow);

        Messages(await Get(root, record, caller)).Select(m => m.SprkCommunicationid).Should().Equal(dual.ToString("D"));
    }

    [Fact]
    public async Task AMessageAlsoFiledToASecureRootTheCallerCanRead_IsListed_WithoutAFlagRead()
    {
        var project = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, matter);
        var dual = Guid.NewGuid();
        var row = MessageRow(dual, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        row["_sprk_regardingmatter_value"] = El(matter.ToString("D"));
        SetupMessages(row);

        var messages = Messages(await Get("projects", project, Ciam(project: project, matter: matter)));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(dual.ToString("D"));
        _roots.FlagReads.Should().BeEmpty("a root the caller can read never needs its secure flag");
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("fault")]
    [InlineData("missing")]
    [InlineData("flag-null")]
    public async Task AMessageWhoseOtherRootsSecureStateCannotBeRead_IsWithheld_FailClosed(string failure)
    {
        var project = Guid.NewGuid();
        var matter = Guid.NewGuid();
        switch (failure)
        {
            case "throws": _roots.AddOpenRoot(ExternalGrantRootType.Matter, matter); _roots.FlagReadThrows = true; break;
            case "fault": _roots.AddOpenRoot(ExternalGrantRootType.Matter, matter); _roots.FlagFault = System.Net.HttpStatusCode.InternalServerError; break;
            case "flag-null": _roots.AddRoot(ExternalGrantRootType.Matter, matter, isSecure: null); break;
            // "missing": no matter record at all.
        }

        var plain = Guid.NewGuid();
        var row = MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        row["_sprk_regardingmatter_value"] = El(matter.ToString("D"));
        SetupMessages(MessageRow(plain, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"), row);

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(plain.ToString("D"));
    }

    // Main-session decision 2026-10-12: "secure" means EFFECTIVELY secure (UAC-r2 task 174). A stamped work assignment W whose
    // OWN flag is false but which is filed under a secure matter is secure, even when the message carries no stamp for it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMessageFiledToAWorkAssignmentWithAFalseOwnFlag_IsWithheldOnlyWhenItsMatterIsSecure(bool matterSecure)
    {
        var project = Guid.NewGuid();
        var workAssignment = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.WorkAssignment, workAssignment);
        _roots.AddRoot(ExternalGrantRootType.Matter, matter, matterSecure);
        _roots.FileWorkAssignmentUnder(workAssignment, ExternalGrantRootType.Matter, matter);
        var plain = Guid.NewGuid();
        var dual = Guid.NewGuid();
        var dualRow = MessageRow(dual, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        dualRow["_sprk_regardingworkassignment_value"] = El(workAssignment.ToString("D"));
        SetupMessages(MessageRow(plain, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"), dualRow);

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(matterSecure ? new[] { plain.ToString("D") } : new[] { plain.ToString("D"), dual.ToString("D") });
    }

    [Fact]
    public async Task AMessageFiledToAWorkAssignment_WhoseFilingChainCannotBeRead_IsWithheld_FailClosed()
    {
        var project = Guid.NewGuid();
        var workAssignment = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.WorkAssignment, workAssignment);
        _roots.AddOpenRoot(ExternalGrantRootType.Matter, matter);
        _roots.FileWorkAssignmentUnder(workAssignment, ExternalGrantRootType.Matter, matter);
        _roots.FilingFaultEntity = "sprk_matter";
        var plain = Guid.NewGuid();
        var dualRow = MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        dualRow["_sprk_regardingworkassignment_value"] = El(workAssignment.ToString("D"));
        SetupMessages(MessageRow(plain, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"), dualRow);

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(plain.ToString("D"));
    }

    [Fact]
    public async Task ManyMessagesStampedWithTheSameUnreadableRoots_CostOneFlagQueryPerRootType()
    {
        var project = Guid.NewGuid();
        var openMatter = Guid.NewGuid();
        var secureMatter = Guid.NewGuid();
        var openWorkAssignment = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.Matter, openMatter);
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, secureMatter);
        _roots.AddOpenRoot(ExternalGrantRootType.WorkAssignment, openWorkAssignment);
        var rows = new List<Dictionary<string, JsonElement>>();
        var listed = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            var row = MessageRow(id, "sprk_regardingproject", project, internalOnly: false, $"2026-10-0{i + 1}T09:00:00Z");
            row["_sprk_regardingmatter_value"] = El((i % 3 == 0 ? secureMatter : openMatter).ToString("D"));
            row["_sprk_regardingworkassignment_value"] = El(openWorkAssignment.ToString("D"));
            rows.Add(row);
            if (i % 3 != 0) listed.Add(id.ToString("D"));
        }

        SetupMessages(rows.ToArray());

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(listed);
        _roots.FlagReads.Should().HaveCount(2, "one query per root entity, never one per row");
        _roots.FlagReads.Should().ContainSingle(u => u.Contains("/sprk_matters?"))
            .Which.Should().Contain(openMatter.ToString()).And.Contain(secureMatter.ToString())
            .And.Contain("$select=sprk_matterid,sprk_issecure");
    }

    [Fact]
    public async Task AMessageFiledToAMalformedRoot_IsWithheld_FailClosed()
    {
        var project = Guid.NewGuid();
        var row = MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        row["_sprk_regardingworkassignment_value"] = El("not-a-guid");
        SetupMessages(row);

        Messages(await Get("projects", project, Ciam(project: project))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("_sprk_regardingmatter_value", 7)]
    [InlineData("_sprk_regardingworkassignment_value", true)]
    [InlineData("_sprk_regardingmatter_value", new[] { 1 })]
    public async Task AMessageWhoseRootStampIsPresentButNotAString_IsWithheld_FailClosed(string stamp, object value)
    {
        // K3 (fix round 3): a stamp that is present as a non-string JSON value is malformed, not "not filed". Same verdict as
        // DocumentRootLinks and the delete-target parse.
        var project = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var row = MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        row[stamp] = El(value);
        SetupMessages(MessageRow(plain, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"), row);

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(plain.ToString("D"));
    }

    [Fact]
    public async Task AMessageWhoseOtherRootStampIsJsonNull_IsListed_NotFiled()
    {
        var project = Guid.NewGuid();
        var id = Guid.NewGuid();
        var row = MessageRow(id, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        row["_sprk_regardingmatter_value"] = JsonSerializer.SerializeToElement<string?>(null);
        SetupMessages(row);

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(id.ToString("D"));
    }

    // ── task 187 fix round 1 (R2): a message row's OTHER parent lookups are parsed and their LIVE roots decide ──
    // A message on P's thread whose sprk_regardinginvoice / sprk_regardingevent names a record filed under secure matter X is
    // behind X's wall (SecureChildReconciler's secure-if-any), whatever its stamps say.

    [Theory]
    [InlineData("_sprk_regardinginvoice_value", "sprk_invoice", "sprk_matter")]
    [InlineData("_sprk_regardingevent_value", "sprk_event", "sprk_regardingmatter")]
    public async Task AMessageWhoseOtherParentIsUnderSecureX_IsWithheld_FromAPOnlyReader_AndListed_ForAPAndXReader(
        string column, string parentTable, string parentRootColumn)
    {
        var project = Guid.NewGuid();
        var secure = Guid.NewGuid();
        var parent = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, secure);
        _roots.AddFilingRow(parentTable, parent, (parentRootColumn, "sprk_matter", secure));
        var plain = Guid.NewGuid();
        var carrier = Guid.NewGuid();
        var carrierRow = MessageRow(carrier, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        carrierRow[column] = El(parent.ToString("D"));
        SetupMessages(MessageRow(plain, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"), carrierRow);

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(new[] { plain.ToString("D") }, "the message's live parent is under secure X, which the caller cannot read");
        Messages(await Get("projects", project, Ciam(project: project, matter: secure))).Select(m => m.SprkCommunicationid)
            .Should().Equal(plain.ToString("D"), carrier.ToString("D"));
    }

    [Fact]
    public async Task AMessageWhoseOtherParentIsUnderAnOrdinaryRoot_IsListed()
    {
        var project = Guid.NewGuid();
        var open = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.Matter, open);
        _roots.AddFilingRow("sprk_invoice", invoice, ("sprk_matter", "sprk_matter", open));
        var id = Guid.NewGuid();
        var row = MessageRow(id, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z");
        row["_sprk_regardinginvoice_value"] = El(invoice.ToString("D"));
        SetupMessages(row);

        Messages(await Get("projects", project, Ciam(project: project))).Select(m => m.SprkCommunicationid)
            .Should().Equal(id.ToString("D"));
    }

    // Task 065 (D7) x task 187 (merge of master into r3): the two rules meet on the message's own thread. The thread reads
    // explicitly OPEN, so the D7 gate admits the message; the thread is also a parent the live-parent rule walks, and this
    // one is filed under secure matter X. The message is then behind X's wall: withheld from a reader of P alone, listed for a
    // reader of P and X. (The gate alone would list it to both; the walk alone would not see a thread that is not Open.)
    [Fact]
    public async Task AMessageInAnOpenThreadFiledUnderSecureX_IsWithheld_FromAPOnlyReader_AndListed_ForAPAndXReader()
    {
        var project = Guid.NewGuid();
        var secure = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, secure);
        _roots.AddFilingRow("sprk_communicationthread", AppOnlySelectProjection.OpenThreadId, ("sprk_regardingmatter", "sprk_matter", secure));
        var id = Guid.NewGuid();
        SetupMessages(MessageRow(id, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z"));

        Messages(await Get("projects", project, Ciam(project: project))).Should().BeEmpty(
            "the thread is Open, but it is filed under secure X, which the caller cannot read");
        Messages(await Get("projects", project, Ciam(project: project, matter: secure))).Select(m => m.SprkCommunicationid)
            .Should().Equal(id.ToString("D"));
    }

    [Fact]
    public async Task TheThreadQuery_AlwaysReadsAllThreeRootStamps_WhateverRecordItIsKeyedOn()
    {
        var matter = Guid.NewGuid();
        SetupMessages();

        await Get("matters", matter, Ciam(matter: matter));

        // The projection is what lets the rule see a second root; a query that dropped one would list such a row.
        _lastOData.Should().Contain("_sprk_regardingproject_value").And.Contain("_sprk_regardingmatter_value")
            .And.Contain("_sprk_regardingworkassignment_value");
    }

    // ---- Task 187, fix round 1: invoice threads (owner round 100, D1 and D4) ----
    //
    // Owner decision 2026-10-11 (option A "inherit") still admits a caller who reaches the invoice through ONE of its parents
    // when the others are ordinary. D4: an invoice whose LIVE roots include a SECURE root the caller cannot read (its own
    // matter or project, or the root its agreement is filed under) is behind that root's wall, so its thread is refused, as
    // the invoices list withholds it (D2). D1: each message's OTHER parents (not the route invoice, which the gate decided)
    // are walked, so a message also filed under secure X is withheld from a caller who cannot read X.

    private void AddInvoice(Guid invoice, Guid? matter = null, Guid? project = null, Guid? agreement = null)
    {
        var record = new Dictionary<string, object?> { ["id"] = invoice };
        if (matter is { } m) record["sprk_matter"] = Lookup(m, "sprk_matter");
        if (project is { } p) record["sprk_project"] = Lookup(p, "sprk_project");
        if (agreement is { } a) record["sprk_regardingagreement"] = Lookup(a, "sprk_agreement");
        _records[("sprk_invoice", invoice)] = record;
    }

    private static CallerPrincipal CiamOnMatters(params Guid[] matters) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "partner@outside.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = matters.ToDictionary(m => m, _ => AccessRights.Read),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    [Fact]
    public async Task AnInvoiceAlsoUnderASecureRootTheCallerCannotRead_ItsThreadIsRefused_AndNoMessageIsRead_D4()
    {
        var matter = Guid.NewGuid();
        var project = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Project, project);
        AddInvoice(invoice, matter: matter, project: project);
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z"));

        ShouldBeForbiddenNotAccessible(await Get("invoices", invoice, Ciam(matter: matter)));
        _lastOData.Should().BeNull("no message is read behind the wall");
    }

    [Fact]
    public async Task AnInvoiceAlsoUnderAnOrdinaryRootTheCallerCannotRead_ItsThreadIsListed_OptionA()
    {
        var matter = Guid.NewGuid();
        var project = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.Project, project);
        AddInvoice(invoice, matter: matter, project: project);
        var row = MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z");
        row["_sprk_regardingmatter_value"] = El(matter.ToString("D"));
        row["_sprk_regardingproject_value"] = El(project.ToString("D"));
        SetupMessages(row);

        Messages(await Get("invoices", invoice, Ciam(matter: matter))).Should().HaveCount(1);
    }

    [Fact]
    public async Task AnInvoiceWhoseAgreementIsUnderSecureX_ItsThreadIsRefused_AndListed_ForACallerWhoReadsX_D4()
    {
        var matter = Guid.NewGuid();
        var secure = Guid.NewGuid();
        var agreement = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, secure);
        _roots.AddFilingRow("sprk_agreement", agreement, ("sprk_regardingmatter", "sprk_matter", secure));
        AddInvoice(invoice, matter: matter, agreement: agreement);
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z"));

        ShouldBeForbiddenNotAccessible(await Get("invoices", invoice, CiamOnMatters(matter)));
        _recordReads.Last().Select.Should().Contain("sprk_regardingagreement", "the gate reads every attribute the rule needs");
        Messages(await Get("invoices", invoice, CiamOnMatters(matter, secure))).Should().HaveCount(1);
    }

    [Fact]
    public async Task AnInvoiceWhoseAgreementCannotBeWalked_ItsThreadIsRefused_FailClosed()
    {
        var matter = Guid.NewGuid();
        var agreement = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _roots.AddFilingRow("sprk_agreement", agreement, ("sprk_regardingmatter", "sprk_matter", matter));
        _roots.FilingReadThrows = true;
        AddInvoice(invoice, matter: matter, agreement: agreement);
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z"));

        ShouldBeForbiddenNotAccessible(await Get("invoices", invoice, CiamOnMatters(matter)));
    }

    [Fact]
    public async Task AnInvoiceThreadMessage_AlsoFiledUnderAnEventOfSecureX_IsWithheld_FromAnMOnlyReader_AndListed_ForMAndX_D1()
    {
        var matter = Guid.NewGuid();
        var secure = Guid.NewGuid();
        var ev = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, secure);
        _roots.AddFilingRow("sprk_event", ev, ("sprk_regardingmatter", "sprk_matter", secure));
        AddInvoice(invoice, matter: matter);
        var plain = Guid.NewGuid();
        var carrier = Guid.NewGuid();
        var carrierRow = MessageRow(carrier, "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-02T09:00:00Z");
        carrierRow["_sprk_regardingevent_value"] = El(ev.ToString("D"));
        SetupMessages(MessageRow(plain, "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z"), carrierRow);

        Messages(await Get("invoices", invoice, CiamOnMatters(matter))).Select(m => m.SprkCommunicationid)
            .Should().Equal(new[] { plain.ToString("D") }, "the message is also filed under an event of secure X");
        Messages(await Get("invoices", invoice, CiamOnMatters(matter, secure))).Select(m => m.SprkCommunicationid)
            .Should().Equal(plain.ToString("D"), carrier.ToString("D"));
    }

    [Fact]
    public async Task AnInvoiceThreadMessage_AlsoFiledUnderAnEventOfAnOrdinaryRoot_IsListed_D1()
    {
        var matter = Guid.NewGuid();
        var open = Guid.NewGuid();
        var ev = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.Matter, open);
        _roots.AddFilingRow("sprk_event", ev, ("sprk_regardingmatter", "sprk_matter", open));
        AddInvoice(invoice, matter: matter);
        var row = MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-02T09:00:00Z");
        row["_sprk_regardingevent_value"] = El(ev.ToString("D"));
        SetupMessages(row);

        Messages(await Get("invoices", invoice, CiamOnMatters(matter))).Should().HaveCount(1);
    }

    [Fact]
    public async Task AnInvoiceThread_DoesNotWalkTheRouteInvoiceAgain_ItIsTheGatesDecision_D1()
    {
        // The route invoice is the gate's (D4); each message's walk covers only its OTHER parents.
        var matter = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        AddInvoice(invoice, matter: matter);
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z"));

        Messages(await Get("invoices", invoice, CiamOnMatters(matter))).Should().HaveCount(1);
        // Task 065 x 187 (merge): every message a non-internal caller can see sits in a thread, and the thread is itself a parent
        // of the message (sprk_communicationthread is a lineage lookup of sprk_communication), so the filter's walk makes ONE read:
        // the thread. The route invoice (the gate's decision) and its agreement are still never walked.
        _roots.FilingReadLog.Should().Equal(new[] { "sprk_communicationthread" },
            "the gate (no agreement) walks nothing, and the message filter walks only the message's thread, not the route invoice");
    }

    [Theory]
    [InlineData("matters", "sprk_regardingmatter")]
    [InlineData("workassignments", "sprk_regardingworkassignment")]
    [InlineData("MATTERS", "sprk_regardingmatter")]
    public async Task AccessibleMatterOrWorkAssignment_ReturnsItsThread(string root, string regardingField)
    {
        var record = Guid.NewGuid();
        var caller = root.Equals("workassignments", StringComparison.OrdinalIgnoreCase)
            ? Ciam(workAssignment: record)
            : Ciam(matter: record);
        SetupMessages(MessageRow(Guid.NewGuid(), regardingField, record, internalOnly: false, "2026-10-01T09:00:00Z"));

        var messages = Messages(await Get(root, record, caller));

        messages.Should().HaveCount(1);
        _lastOData.Should().Contain($"_{regardingField}_value eq {record}");
    }

    [Fact]
    public async Task InvoiceUnderAnAccessibleMatter_ReturnsItsThread()
    {
        var matter = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _records[("sprk_invoice", invoice)] = new Dictionary<string, object?>
        {
            ["id"] = invoice,
            ["sprk_matter"] = Lookup(matter, "sprk_matter"),
        };
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z"));

        var messages = Messages(await Get("invoices", invoice, Ciam(matter: matter)));

        messages.Should().HaveCount(1);
        _lastOData.Should().Contain($"_sprk_regardinginvoice_value eq {invoice}");
        // Task 187 D4: plus the invoice's agreement, whose live roots join the gate's rule.
        _recordReads.Should().ContainSingle()
            .Which.Select.Should().BeEquivalentTo(new[] { "sprk_matter", "sprk_project", "sprk_regardingagreement" });
    }

    // ── outside the accessible set → 403, and nothing is read ──────────────────────────────────────

    [Fact]
    public async Task AnotherPartysProject_Is403_AndNoMessageIsRead()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardingproject", theirs, internalOnly: false, "2026-10-01T09:00:00Z"));

        var result = await Get("projects", theirs, Ciam(project: mine));

        ShouldBeForbiddenNotAccessible(result);
        ShouldNotHaveReadMessages();
    }

    [Fact]
    public async Task AnotherPartysInvoice_AndANonexistentInvoice_GetTheSame403()
    {
        var myMatter = Guid.NewGuid();
        var theirMatter = Guid.NewGuid();
        var theirInvoice = Guid.NewGuid();
        var missingInvoice = Guid.NewGuid();
        _records[("sprk_invoice", theirInvoice)] = new Dictionary<string, object?>
        {
            ["id"] = theirInvoice,
            ["sprk_matter"] = Lookup(theirMatter, "sprk_matter"),
        };
        SetupMessages();

        var theirs = await Get("invoices", theirInvoice, Ciam(matter: myMatter));
        var missing = await Get("invoices", missingInvoice, Ciam(matter: myMatter));

        ShouldBeForbiddenNotAccessible(theirs);
        ShouldBeForbiddenNotAccessible(missing);
        var a = (ProblemHttpResult)theirs;
        var b = (ProblemHttpResult)missing;
        a.ProblemDetails.Detail.Should().Be(b.ProblemDetails.Detail, "a missing record and a foreign one must be indistinguishable");
        a.ProblemDetails.Title.Should().Be(b.ProblemDetails.Title);
        ShouldNotHaveReadMessages();
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    [InlineData("invoices")]
    [InlineData("servicerequests")]
    public async Task NonexistentRecord_Is403_NotA404(string root)
    {
        // Every accessible set non-empty, so the gate is the only thing deciding; the id exists nowhere.
        var caller = Ciam(project: Guid.NewGuid(), matter: Guid.NewGuid(), workAssignment: Guid.NewGuid());
        SetupMessages();

        var result = await Get(root, Guid.NewGuid(), caller, ProductionRegistry(partnerServiceRequestsEnabled: true));

        ShouldBeForbiddenNotAccessible(result);
        ShouldNotHaveReadMessages();
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    [InlineData("invoices")]
    public async Task TierOneOnlyCaller_WithNoRecordGrants_Is403_AndCausesNoDataverseRead(string root)
    {
        // Entitled to modules (Tier-1) but holding no Tier-2 grant on anything.
        SetupMessages();

        var result = await Get(root, Guid.NewGuid(), WorkforceContact());

        ShouldBeForbiddenNotAccessible(result);
        ShouldNotHaveReadMessages();
        _recordReads.Should().BeEmpty("a caller with an empty accessible set must not cause a record read");
    }

    // ── internal-only by principal kind ────────────────────────────────────────────────────────────

    private (Guid Project, Guid Visible, Guid InternalOnly, Guid Unflagged) SetupMixedThread()
    {
        var project = Guid.NewGuid();
        var visible = Guid.NewGuid();
        var internalOnly = Guid.NewGuid();
        var unflagged = Guid.NewGuid();
        SetupMessages(
            MessageRow(visible, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"),
            MessageRow(internalOnly, "sprk_regardingproject", project, internalOnly: true, "2026-10-02T09:00:00Z"),
            MessageRow(unflagged, "sprk_regardingproject", project, internalOnly: null, "2026-10-03T09:00:00Z"));
        return (project, visible, internalOnly, unflagged);
    }

    [Fact]
    public async Task CiamCaller_NeverReceivesAnInternalOnlyMessage()
    {
        var (project, visible, _, _) = SetupMixedThread();

        var messages = Messages(await Get("projects", project, Ciam(project: project)));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(visible.ToString("D"));
    }

    [Fact]
    public async Task WorkforceContactCaller_NeverReceivesAnInternalOnlyMessage()
    {
        var (project, visible, _, _) = SetupMixedThread();

        var messages = Messages(await Get("projects", project, WorkforceContact(project: project)));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(visible.ToString("D"));
        _identity.Verify(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SystemUserCaller_ReceivesInternalOnlyMessages()
    {
        var (project, visible, internalOnly, unflagged) = SetupMixedThread();
        var systemUserId = Guid.NewGuid();
        _identity.Setup(i => i.IsExternalAsync(systemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var messages = Messages(await Get("projects", project, SystemUser(systemUserId, project)));

        messages.Select(m => m.SprkCommunicationid)
            .Should().Equal(visible.ToString("D"), internalOnly.ToString("D"), unflagged.ToString("D"));
    }

    [Fact]
    public async Task ExternalLicensedSystemUser_DoesNotReceiveInternalOnlyMessages()
    {
        // #675 parity with the impersonated reads: sprk_isexternal = true makes a systemuser non-internal.
        var (project, visible, _, _) = SetupMixedThread();
        var systemUserId = Guid.NewGuid();
        _identity.Setup(i => i.IsExternalAsync(systemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var messages = Messages(await Get("projects", project, SystemUser(systemUserId, project)));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(visible.ToString("D"));
    }

    // ── isMine: the caller-relative author flag (task 034, review fix) ─────────────────────────────

    /// <summary>A message row authored by the given contact (a task-034 external post) or sent by the given systemuser.</summary>
    private static Dictionary<string, JsonElement> AuthoredRow(
        Guid project, string createdOn, Guid? createdByContact = null, Guid? sentBy = null)
    {
        var row = MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, createdOn);
        row.Remove("_sprk_sentby_value");
        row.Remove("_sprk_sentby_value@OData.Community.Display.V1.FormattedValue");
        if (sentBy is { } systemUser)
            row["_sprk_sentby_value"] = El(systemUser.ToString("D"));
        if (createdByContact is { } contact)
            row["_sprk_createdbycontact_value"] = El(contact.ToString("D"));
        return row;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContactCaller_IsMine_IsTrueForOwnContactPost_FalseForAnotherContactsAndForASystemUsersPost(bool workforce)
    {
        var project = Guid.NewGuid();
        var me = Guid.NewGuid();
        var own = AuthoredRow(project, "2026-10-01T09:00:00Z", createdByContact: me);
        var theirs = AuthoredRow(project, "2026-10-02T09:00:00Z", createdByContact: Guid.NewGuid());
        var lawyers = AuthoredRow(project, "2026-10-03T09:00:00Z", sentBy: Guid.NewGuid());
        var anonymous = AuthoredRow(project, "2026-10-04T09:00:00Z");
        SetupMessages(own, theirs, lawyers, anonymous);
        var caller = workforce ? WorkforceContact(project: project, contactId: me) : Ciam(project: project, contactId: me);

        var messages = Messages(await Get("projects", project, caller));

        messages.Select(m => m.IsMine).Should().Equal(true, false, false, false);
    }

    [Fact]
    public async Task SystemUserCaller_IsMine_IsTrueForOwnMessage_FalseForAnotherUsersAndForAContactsPost()
    {
        var project = Guid.NewGuid();
        var me = Guid.NewGuid();
        _identity.Setup(i => i.IsExternalAsync(me, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var caller = SystemUser(me, project);
        var own = AuthoredRow(project, "2026-10-01T09:00:00Z", sentBy: me);
        var colleagues = AuthoredRow(project, "2026-10-02T09:00:00Z", sentBy: Guid.NewGuid());
        // A contact post whose contact id happens to equal the systemuser caller's LINKED contact id is still not theirs:
        // the kind decides (a systemuser authors as its systemuser, exactly as the stamp does).
        var linkedContactPost = AuthoredRow(project, "2026-10-03T09:00:00Z", createdByContact: caller.ContactId);
        var partnersPost = AuthoredRow(project, "2026-10-04T09:00:00Z", createdByContact: Guid.NewGuid());
        SetupMessages(own, colleagues, linkedContactPost, partnersPost);

        var messages = Messages(await Get("projects", project, caller));

        messages.Select(m => m.IsMine).Should().Equal(true, false, false, false);
    }

    [Fact]
    public async Task IsMine_NeverExposesAContactOrSystemUserId()
    {
        var project = Guid.NewGuid();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        SetupMessages(AuthoredRow(project, "2026-10-01T09:00:00Z", createdByContact: me),
            AuthoredRow(project, "2026-10-02T09:00:00Z", createdByContact: other));

        var json = JsonSerializer.Serialize(Messages(await Get("projects", project, Ciam(project: project, contactId: me))));

        json.Should().Contain("\"isMine\":true").And.Contain("\"isMine\":false");
        json.Should().NotContain(me.ToString("D"), "the id is compared server-side and never returned")
            .And.NotContain(other.ToString("D"))
            .And.NotContainEquivalentOf("createdbycontact");
    }

    // ── service requests: requester scope ──────────────────────────────────────────────────────────

    private Guid ServiceRequestRequestedBy(Guid requester)
    {
        var request = Guid.NewGuid();
        _records[("sprk_servicerequest", request)] = new Dictionary<string, object?>
        {
            ["id"] = request,
            ["sprk_requestedby"] = Lookup(requester, "contact"),
        };
        return request;
    }

    [Fact]
    public async Task Requester_ReadsTheThreadOnTheirOwnServiceRequest()
    {
        var me = Guid.NewGuid();
        var request = ServiceRequestRequestedBy(me);
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardingservicerequest", request, internalOnly: false, "2026-10-01T09:00:00Z"));

        var messages = Messages(await Get("servicerequests", request, WorkforceContact(contactId: me)));

        messages.Should().HaveCount(1);
        _lastOData.Should().Contain($"_sprk_regardingservicerequest_value eq {request}");
        _recordReads.Should().ContainSingle().Which.Select.Should().Equal("sprk_requestedby");
    }

    [Fact]
    public async Task SomeoneElsesServiceRequest_Is403_AndNoMessageIsRead()
    {
        var request = ServiceRequestRequestedBy(Guid.NewGuid());
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardingservicerequest", request, internalOnly: false, "2026-10-01T09:00:00Z"));

        var result = await Get("servicerequests", request, WorkforceContact(contactId: Guid.NewGuid()));

        ShouldBeForbiddenNotAccessible(result);
        ShouldNotHaveReadMessages();
    }

    [Fact]
    public async Task CiamRequester_PartnerSwitchOff_Is403_WithNoRead_AndSwitchOn_ReadsOwnThread()
    {
        var me = Guid.NewGuid();
        var request = ServiceRequestRequestedBy(me);
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardingservicerequest", request, internalOnly: false, "2026-10-01T09:00:00Z"));

        var off = await Get("servicerequests", request, Ciam(contactId: me), ProductionRegistry(partnerServiceRequestsEnabled: false));
        ShouldBeForbiddenNotAccessible(off);
        _recordReads.Should().BeEmpty();
        ShouldNotHaveReadMessages();

        var on = await Get("servicerequests", request, Ciam(contactId: me), ProductionRegistry(partnerServiceRequestsEnabled: true));
        Messages(on).Should().HaveCount(1);
    }

    // ── unsupported roots → 404 ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("documents")] // sprk_communication has no regarding lookup to a document
    [InlineData("events")]
    [InlineData("organizations")]
    [InlineData("contacts")]
    [InlineData("todos")]
    [InlineData("api")]
    public async Task UnsupportedRoot_Is404_AndNothingIsRead(string root)
    {
        SetupMessages();

        var result = await Get(root, Guid.NewGuid(), Ciam(project: Guid.NewGuid()));

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be(ExternalMessageEndpoints.ErrorRootNotSupported);
        ShouldNotHaveReadMessages();
        _recordReads.Should().BeEmpty();
    }

    // ── every returned row is re-checked; the read is bounded ──────────────────────────────────────

    [Fact]
    public async Task ARowRegardingAnotherRecord_IsDropped_EvenIfTheQueryReturnedIt()
    {
        var project = Guid.NewGuid();
        var mine = Guid.NewGuid();
        SetupMessages(
            MessageRow(mine, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", Guid.NewGuid(), internalOnly: false, "2026-10-02T09:00:00Z"));

        var messages = Messages(await Get("projects", project, Ciam(project: project)));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(mine.ToString("D"));
    }

    [Fact]
    public async Task AThreadPastTheReadBound_ReturnsTheNewest500_OldestFirst_AndFlagsTruncated()
    {
        var project = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray();
        // Dataverse answers the query's own order (createdon DESC): index 0 is the newest, index 500 the oldest.
        var rows = Enumerable.Range(0, 501)
            .Select(i => MessageRow(ids[i], "sprk_regardingproject", project, internalOnly: false,
                start.AddMinutes(500 - i).ToString("O")))
            .ToArray();
        SetupMessages(rows);

        var result = await Get("projects", project, Ciam(project: project));

        var response = ((Ok<ExternalCollectionResponse<ExternalMessageDto>>)result).Value!;
        response.Value.Should().HaveCount(500);
        response.Truncated.Should().BeTrue();
        _lastOData.Should().Contain("$top=501").And.Contain("$orderby=createdon desc");
        var returned = response.Value.Select(m => m.SprkCommunicationid).ToList();
        returned.Should().Contain(ids[0].ToString("D"), "the newest message must survive truncation");
        returned.Should().NotContain(ids[500].ToString("D"), "the oldest message is the one dropped");
        returned[0].Should().Be(ids[499].ToString("D"), "the page is oldest to newest");
        returned[^1].Should().Be(ids[0].ToString("D"), "newest last");
    }

    // ── status allow-list: only sent / received messages reach an external reader (review F1) ──────

    private (Guid Project, Guid Sent, Guid Received) SetupStatusMix()
    {
        var project = Guid.NewGuid();
        var sent = Guid.NewGuid();
        var received = Guid.NewGuid();
        SetupMessages(
            MessageRow(sent, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z", status: StatusSend, direction: Outgoing),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z", status: StatusDraft),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-03T09:00:00Z", status: StatusDeleted),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-04T09:00:00Z", status: StatusRecalled),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-05T09:00:00Z", status: StatusQueued),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-06T09:00:00Z", status: StatusFailed),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-07T09:00:00Z", status: StatusBounded),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-08T09:00:00Z", status: null),
            MessageRow(received, "sprk_regardingproject", project, internalOnly: false, "2026-10-09T09:00:00Z", status: StatusDelivered, direction: Incoming));
        return (project, sent, received);
    }

    public static IEnumerable<object[]> PrincipalKinds()
    {
        yield return new object[] { "ciam-contact" };
        yield return new object[] { "workforce-contact" };
        yield return new object[] { "external-licensed-systemuser" };
        yield return new object[] { "internal-systemuser" };
    }

    private CallerPrincipal PrincipalOfKind(string kind, Guid project)
    {
        var systemUserId = Guid.NewGuid();
        switch (kind)
        {
            case "ciam-contact":
                return Ciam(project: project);
            case "workforce-contact":
                return WorkforceContact(project: project);
            case "external-licensed-systemuser":
                _identity.Setup(i => i.IsExternalAsync(systemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
                return SystemUser(systemUserId, project);
            default:
                _identity.Setup(i => i.IsExternalAsync(systemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
                return SystemUser(systemUserId, project);
        }
    }

    [Theory]
    [MemberData(nameof(PrincipalKinds))]
    public async Task DraftDeletedRecalledAndOtherUnsentRows_AreDropped_ForEveryPrincipalKind(string kind)
    {
        var (project, sent, received) = SetupStatusMix();

        var messages = Messages(await Get("projects", project, PrincipalOfKind(kind, project)));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(
            new[] { sent, received }.Select(g => g.ToString("D")),
            "only a Send / Delivered (sent or inbound-received) message is returned; Draft, Deleted, Recalled, Queued, " +
            "Failed, Bounded and a row with no readable status are not");
        _lastOData.Should().Contain("(statuscode eq 659490002 or statuscode eq 659490003)");
    }

    // ── privilege: hidden from external callers (owner decision 2026-10-11, task 033 follow-up) ─────

    private const string ExpectedPrivilegeClause =
        "(sprk_privilegeclassification eq null or (sprk_privilegeclassification ne 100000001 and sprk_privilegeclassification ne 100000002))";

    [Theory]
    [MemberData(nameof(PrincipalKinds))]
    public async Task PrivilegedAndPotentiallyPrivilegedMessages_AreAbsent_NoneAndNullAreShown_ForEveryPrincipalKind(string kind)
    {
        var project = Guid.NewGuid();
        var none = Guid.NewGuid();
        var nullClassification = Guid.NewGuid();
        SetupMessages(
            MessageRow(none, "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z", privilege: PrivilegeNone),
            MessageRow(nullClassification, "sprk_regardingproject", project, internalOnly: false, "2026-10-02T09:00:00Z", privilege: null),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-03T09:00:00Z", privilege: PrivilegePotentiallyPrivileged),
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-04T09:00:00Z", privilege: PrivilegePrivileged),
            // A classification this code does not define is unreadable, so it is hidden too (fail closed).
            MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-05T09:00:00Z", privilege: 7));

        var messages = Messages(await Get("projects", project, PrincipalOfKind(kind, project)));

        messages.Select(m => m.SprkCommunicationid).Should().Equal(
            none.ToString("D"), nullClassification.ToString("D"));
    }

    [Fact]
    public async Task ThePrivilegeRule_IsInTheDataverseFilter_NotOnlyAfterTheRowsCame_SoPagingCannotShortChangeIt()
    {
        var project = Guid.NewGuid();
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"));

        await Get("projects", project, Ciam(project: project));

        _lastOData.Should().Contain($" and {ExpectedPrivilegeClause}&$orderby=")
            .And.Contain("$top=501", "the bound is applied to rows that already passed the filter");
        CommunicationThreadReadService.ExternalPrivilegeFilterClause.Should().Be(ExpectedPrivilegeClause);
    }

    [Fact]
    public void TheHiddenSet_IsOneNamedList_PotentiallyPrivilegedAndPrivileged()
    {
        CommunicationThreadReadService.ExternallyHiddenPrivilege.Should().Equal(
            CommunicationPrivilegeClassification.PotentiallyPrivileged, CommunicationPrivilegeClassification.Privileged);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(PrivilegeNone, false)]
    [InlineData(PrivilegePotentiallyPrivileged, true)]
    [InlineData(PrivilegePrivileged, true)]
    [InlineData(7, true)]
    [InlineData(0, true)]
    public void IsHiddenFromExternal_FollowsTheNamedSet(int? classification, bool hidden) =>
        CommunicationThreadReadService.IsHiddenFromExternal(classification).Should().Be(hidden);

    [Fact]
    public async Task ThePostResponse_NeverCarriesAClassification_ItIsTheAuthorsOwnPlainRow()
    {
        // A post's 201 body is built from the values written (no read-back), and ExternalMessageDto has no privilege
        // member at all, so there is nothing for the rule to hide; the row a post writes never sets a classification.
        typeof(ExternalMessageDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("rivilege", StringComparison.OrdinalIgnoreCase));
    }

    // ── a fault anywhere in the endpoint is its coded 500 ──────────────────────────────────────────

    [Fact]
    public async Task AFaultInTheRecordGate_IsTheEndpointsCoded500_AndNoMessageIsRead()
    {
        var matter = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardinginvoice", invoice, internalOnly: false, "2026-10-01T09:00:00Z"));

        // An invoice is a child record: the gate reads it app-only, and that read faults (not a "not found").
        var result = await ExternalMessageEndpoints.GetMessagesCoreAsync(
            "invoices", invoice, Ciam(matter: matter), ProductionRegistry(),
            (_, _, _, _) => throw new HttpRequestException("Dataverse unavailable"),
            ReadService(), _roots.NewService().GetNonSecureRootsAsync, NullLogger.Instance, CancellationToken.None);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("EXT_MESSAGES_INTERNAL_ERROR");
        ShouldNotHaveReadMessages();
    }

    [Fact]
    public async Task ARecordNotFoundInTheGate_StaysA403_NotA500()
    {
        // Pins the no-existence-oracle rule against the try/catch widening: not-found is a deny, a fault is a 500.
        var matter = Guid.NewGuid();
        SetupMessages();

        var result = await ExternalMessageEndpoints.GetMessagesCoreAsync(
            "invoices", Guid.NewGuid(), Ciam(matter: matter), ProductionRegistry(),
            (entity, id, _, _) => throw new RecordNotFoundException(entity, id),
            ReadService(), _roots.NewService().GetNonSecureRootsAsync, NullLogger.Instance, CancellationToken.None);

        ShouldBeForbiddenNotAccessible(result);
        ShouldNotHaveReadMessages();
    }

    // ── the response is an allow-list ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExternalMessageDto_SerializesOnlyTheAllowListedFields()
    {
        var json = JsonSerializer.SerializeToElement(new ExternalMessageDto { SprkCommunicationid = Guid.NewGuid().ToString() });

        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "sprk_communicationid", "sprk_subject", "sprk_body", "sprk_bodyformat", "sprk_direction", "sendername", "createdon", "isMine");
    }

    [Fact]
    public async Task TheSerializedResponse_CarriesNoAddressPrivilegeOrInternalOnlyFields()
    {
        // The Dataverse rows carry sprk_from / sprk_to / the sentby lookup / privilege / the internal-only flag; none of
        // them may appear in the serialized endpoint response, by name or by value.
        var project = Guid.NewGuid();
        SetupMessages(MessageRow(Guid.NewGuid(), "sprk_regardingproject", project, internalOnly: false, "2026-10-01T09:00:00Z"));

        var result = await Get("projects", project, Ciam(project: project));

        var body = ((Ok<ExternalCollectionResponse<ExternalMessageDto>>)result).Value!;
        var json = JsonSerializer.Serialize(body);
        json.Should().NotContainEquivalentOf("sprk_from")
            .And.NotContainEquivalentOf("\"sprk_to\"")
            .And.NotContainEquivalentOf("_sprk_sentby_value")
            .And.NotContainEquivalentOf("privilege")
            .And.NotContainEquivalentOf("isinternalonly")
            .And.NotContain("pat.lawyer@spaarke.test")
            .And.NotContain("someone@spaarke.test");
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("value")[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "sprk_communicationid", "sprk_subject", "sprk_body", "sprk_bodyformat", "sprk_direction", "sendername", "createdon", "isMine");
    }
}

/// <summary>
/// Route-mapped proof for <c>GET /api/v1/external/{root}/{id}/messages</c>, read from the REAL app's endpoint table
/// (<see cref="EndpointTable"/> - an anonymous probe cannot prove a route since the authorization FallbackPolicy answers
/// 401 for unmatched requests too). Also pins that the route carries authorization metadata (the shared external group's
/// policy), so it cannot be mounted anonymously.
/// </summary>
public class ExternalMessageRouteTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ExternalMessageRouteTests(CustomWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    [InlineData("invoices")]
    [InlineData("servicerequests")]
    public void MessagesRoute_IsMapped_ForEverySupportedRoot(string root)
    {
        EndpointTable.AssertMapped(_factory, "GET", $"/api/v1/external/{root}/{Guid.NewGuid()}/messages");
        // Task 034 (FR-11): the post shares the path.
        EndpointTable.AssertMapped(_factory, "POST", $"/api/v1/external/{root}/{Guid.NewGuid()}/messages");
    }

    [Fact]
    public void MessagesRoutes_AreGetAndPostOnly_AndEachRequiresAuthorization()
    {
        EndpointTable.Maps(_factory, "PUT", $"/api/v1/external/projects/{Guid.NewGuid()}/messages").Should().BeFalse();
        EndpointTable.Maps(_factory, "DELETE", $"/api/v1/external/projects/{Guid.NewGuid()}/messages")
            .Should().BeFalse("messages are not deletable (owner, 2026-10-10)");

        var endpoints = EndpointTable.Endpoints(_factory)
            .Where(e => e.RoutePattern.RawText == "/api/v1/external/{root}/{id:guid}/messages")
            .ToList();
        endpoints.Should().HaveCount(2, "GET (task 033) and POST (task 034)");
        endpoints.Should().AllSatisfy(endpoint =>
            endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Should().NotBeEmpty("each route inherits the external collaboration group's authorization policy"));
    }

    [Fact]
    public void PostRoute_IsRateLimited_OnAPerUserPolicy()
    {
        // The write path carries a per-user throttle. "dataverse-query" partitions on CallerResolution.ResolveOpaqueCallerKey
        // (oid in either form, then sub / NameIdentifier) - claims a CIAM token carries - and falls back to the IP, so a
        // CIAM caller is throttled per user, not per shared bucket.
        var endpoints = EndpointTable.Endpoints(_factory)
            .Where(e => e.RoutePattern.RawText == "/api/v1/external/{root}/{id:guid}/messages")
            .ToList();
        var post = endpoints.Single(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()!
            .HttpMethods.Contains("POST"));

        post.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            .Should().NotBeNull("the external write path is throttled")
            .And.Subject.As<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            .PolicyName.Should().Be("dataverse-query");
    }
}
