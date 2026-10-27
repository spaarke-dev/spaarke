using System.Text.Json;
using Azure.Core;
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
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// FR-08 (spaarke-SPA-external-access-platform-r3 task 037): <c>GET /api/v1/external/notifications</c> and
/// <c>POST /api/v1/external/notifications/seen</c>. Driven through <see cref="ExternalNotificationFeedEndpoints.GetFeedCoreAsync"/>
/// and <see cref="ExternalNotificationFeedEndpoints.MarkSeenCoreAsync"/> → the PRODUCTION module registry (built by
/// <c>AddExternalAccess</c>, partner switch included) → the REAL <see cref="CommunicationThreadReadService.ReadRecentForRecordsAppOnlyAsync"/>
/// → the REAL <see cref="CommunicationAccessFilter"/>. Doubled only at the I/O seams: the app-only message query
/// (<see cref="IAppOnlyCommunicationQuery"/>, "the rows Dataverse returned" — deliberately NOT filtered by the double, so
/// every negative case proves the server's own re-check), the three <see cref="ExternalDataService"/> reads (in memory),
/// and the marker cache (<see cref="InMemoryTenantCache"/>).
/// </summary>
[Trait("category", "external-access")]
public class ExternalNotificationFeedTests
{
    private const string CommunicationSet = "sprk_communications";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const int PrivilegeNone = 100000000;
    private const int PrivilegePotentiallyPrivileged = 100000001;
    private const int PrivilegePrivileged = 100000002;
    private const int StatusDraft = 1;
    private const int StatusSend = 659490002;
    private const int StatusDelivered = 659490003;

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAppOnlyCommunicationQuery> _appOnly = new();
    private readonly Mock<IImpersonatedCommunicationQuery> _impersonated = new(MockBehavior.Strict);
    private readonly Mock<ISystemUserIdentityResolver> _identity = new(MockBehavior.Strict);
    private readonly List<string?> _queries = new();
    private readonly List<Dictionary<string, JsonElement>> _messageRows = new();
    // The secure-flag read (owner option B) is FeedData's REAL base read over this fake Web API, which answers the root-set
    // query through $select and omits a null flag.
    private readonly ExternalRootEventReadTests.FakeDataverse _roots = new();
    private readonly FeedData _data;
    private ITenantCache _cache = new InMemoryTenantCache();

    public ExternalNotificationFeedTests()
    {
        _data = new FeedData(_roots);
        _appOnly
            .Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, odata, _) => _queries.Add(odata))
            // In the query's own order (createdon desc), as Dataverse answers — but NOT filtered: the server re-checks. Each
            // row is projected through the query's $select, so a column the query dropped is absent (r3 task 039, K3).
            .Returns<string, string?, CancellationToken>((_, odata, _) =>
                Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(AppOnlySelectProjection.Project(
                    AppOnlySelectProjection.WithOpenThread(_messageRows).OrderByDescending(r => r["createdon"].GetDateTimeOffset()), odata)));
        // Owner decision D7: the feed shows a row only when its thread reads Open. These cases are not about D7, so every row
        // sits in one Open thread.
        _appOnly
            .Setup(q => q.QueryAsync("sprk_communicationthreads", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, CancellationToken>((_, odata, _) =>
                Task.FromResult(AppOnlySelectProjection.OpenThreadAnswer(odata)));
        // Task 187 x D7 (merge of master into r3): the thread is also a LIVE PARENT of every message (sprk_communicationthread is a
        // lineage lookup of sprk_communication), so the live-parent walk reads it app-only and withholds the row when the thread
        // does not come back (fail closed). The shared Open thread therefore exists in the filing world too, filed under nothing.
        _roots.AddFilingRow("sprk_communicationthread", AppOnlySelectProjection.OpenThreadId);
    }

    // ── principals ──────────────────────────────────────────────────────────────────────────────────

    private static CallerPrincipal Ciam(Guid contactId, Guid? project = null, Guid? matter = null, Guid? workAssignment = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contactId,
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

    private static CallerPrincipal WorkforceContact(Guid contactId, Guid? project = null) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = contactId,
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

    // ── The workforce ceiling (unified-access-control-r2 task 186, review round 1 F1) ─────────────────────────────
    // The feed mixes tables, so it FILTERS rather than refuses: a table the Workforce role cannot Read contributes no item
    // and is not read. The predicate is the one the route handler builds from the role (WorkforceRoleCeilingGate.CanRead).

    private Task<IResult> FeedUnderCeiling(CallerPrincipal caller, WorkforceRoleCeiling ceiling) =>
        ExternalNotificationFeedEndpoints.GetFeedCoreAsync(
            caller, Tenant, ProductionRegistry(), _data, ReadService(), _cache, Now, NullLogger.Instance, CancellationToken.None,
            table => WorkforceRoleCeilingGate.CanRead(ceiling, table), _invoiceHiding.Seam);

    /// <summary>ebilling-r1 task 098: the (a') predicate the route passes; hides nothing unless a test says so.</summary>
    private InvoiceHidingStub _invoiceHiding = InvoiceHidingStub.NoneHidden();

    private static WorkforceRoleCeiling EveryRoutePrivilegeWithout(string table, AccessRights removed)
    {
        var map = Sprk.Bff.Api.Tests.AccessControl.WorkforceCeilingDouble.OwnerPrerequisite.ToDictionary(kv => kv.Key, kv => kv.Value);
        map[table] &= ~removed;
        return WorkforceRoleCeiling.Available(map);
    }

    [Fact]
    public async Task Ceiling_WithoutServiceRequestRead_DropsTheRequestsMessages_AndNeverQueriesTheRequests_ButKeepsTheProjects()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var mine = _data.AddServiceRequest(requestedBy: me, "NDA with Contoso");
        Message("sprk_regardingservicerequest", mine, Now.AddHours(-1), senderName: "Legal");
        var onProject = Message("sprk_regardingproject", project, Now.AddHours(-2), senderName: "Pat Lawyer");
        _data.Name(project, "Project Alpha");

        var feed = Body(await FeedUnderCeiling(
            WorkforceContact(me, project), EveryRoutePrivilegeWithout("sprk_servicerequest", AccessRights.Read)));

        feed.Value.Select(i => i.Id).Should().Equal(new[] { IdOf(onProject) }, "the feed is filtered, not refused");
        _data.ServiceRequestReads.Should().BeEmpty("a table the role cannot Read is not read");
        _queries.Single().Should().NotContain(mine.ToString());
    }

    [Fact]
    public async Task Ceiling_WithoutCommunicationRead_ReadsNoMessage_ButKeepsTheGrant()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var grant = _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-1));
        Message("sprk_regardingproject", project, Now.AddHours(-2));
        _data.Name(project, "Project Alpha");

        var feed = Body(await FeedUnderCeiling(
            WorkforceContact(me, project), EveryRoutePrivilegeWithout("sprk_communication", AccessRights.Read)));

        feed.Value.Select(i => i.Id).Should().Equal(grant.ToString("D"));
        _queries.Should().BeEmpty("no message is read without Read on sprk_communication");
    }

    [Fact]
    public async Task Ceiling_WithoutProjectRead_DropsTheProjectsGrantAndMessages()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-1));
        Message("sprk_regardingproject", project, Now.AddHours(-2));
        _data.Name(project, "Project Alpha");

        var feed = Body(await FeedUnderCeiling(
            WorkforceContact(me, project), EveryRoutePrivilegeWithout("sprk_project", AccessRights.Read)));

        feed.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnavailableCeiling_IsAnEmptyFeed_NotAnError_AndReadsNothing()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-1));
        _data.AddServiceRequest(requestedBy: me, "NDA with Contoso");
        Message("sprk_regardingproject", project, Now.AddHours(-2));

        var feed = Body(await FeedUnderCeiling(WorkforceContact(me, project), WorkforceRoleCeiling.Unavailable("read_failed")));

        feed.Value.Should().BeEmpty();
        feed.UnreadCount.Should().Be(0);
        _data.GrantReads.Should().BeEmpty();
        _data.ServiceRequestReads.Should().BeEmpty();
        _queries.Should().BeEmpty();
    }

    [Fact]
    public async Task ACeilingThatHoldsEveryRoutePrivilege_FiltersNothing()
    {
        // Must-not-fire: the same items as a caller the ceiling does not apply to.
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var grant = _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-1));
        var mine = _data.AddServiceRequest(requestedBy: me, "NDA with Contoso");
        var onMine = Message("sprk_regardingservicerequest", mine, Now.AddHours(-1), senderName: "Legal");
        var onProject = Message("sprk_regardingproject", project, Now.AddHours(-2), senderName: "Pat Lawyer");
        _data.Name(project, "Project Alpha");

        var feed = Body(await FeedUnderCeiling(
            WorkforceContact(me, project),
            WorkforceRoleCeiling.Available(Sprk.Bff.Api.Tests.AccessControl.WorkforceCeilingDouble.OwnerPrerequisite)));

        feed.Value.Select(i => i.Id).Should().BeEquivalentTo(new[] { IdOf(onMine), IdOf(onProject), grant.ToString("D") });
    }

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

    private CommunicationThreadReadService ReadService() => new(
        _impersonated.Object, // Strict: the external feed never impersonates.
        new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
        Mock.Of<ICallerSystemUserResolver>(MockBehavior.Strict),
        _identity.Object,
        Mock.Of<ILogger<CommunicationThreadReadService>>(),
        _appOnly.Object);

    private Task<IResult> Feed(CallerPrincipal caller, ExternalModuleRegistry? registry = null, DateTimeOffset? now = null) =>
        ExternalNotificationFeedEndpoints.GetFeedCoreAsync(
            caller, Tenant, registry ?? ProductionRegistry(), _data, ReadService(), _cache, now ?? Now,
            NullLogger.Instance, CancellationToken.None, hideInvoices: _invoiceHiding.Seam);

    private Task<IResult> Seen(CallerPrincipal caller, DateTimeOffset at) =>
        ExternalNotificationFeedEndpoints.MarkSeenCoreAsync(caller, Tenant, _cache, at, NullLogger.Instance, CancellationToken.None);

    private static ExternalNotificationFeedResponse Body(IResult result)
    {
        result.Should().BeOfType<Ok<ExternalNotificationFeedResponse>>();
        return ((Ok<ExternalNotificationFeedResponse>)result).Value!;
    }

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);

    /// <summary>A message row as the app-only query returns it.</summary>
    private Dictionary<string, JsonElement> Message(
        string regardingField,
        Guid regardingId,
        DateTimeOffset createdOn,
        Guid? id = null,
        bool? internalOnly = false,
        int? privilege = PrivilegeNone,
        int? status = StatusDelivered,
        Guid? sentBy = null,
        Guid? createdByContact = null,
        string? senderName = "Pat Lawyer")
    {
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El((id ?? Guid.NewGuid()).ToString("D")),
            ["sprk_subject"] = El("Subject"),
            ["sprk_body"] = El("Body"),
            ["sprk_bodyformat"] = El(100000000),
            ["sprk_direction"] = El(100000000),
            ["createdon"] = El(createdOn.ToString("o")),
            ["sprk_privilegeclassification"] = El(privilege),
            [$"_{regardingField}_value"] = El(regardingId.ToString("D")),
        };
        if (internalOnly is { } flag) row["sprk_isinternalonly"] = El(flag);
        if (status is { } code) row["statuscode"] = El(code);
        var sender = sentBy ?? (createdByContact is null ? Guid.NewGuid() : null);
        if (sender is { } systemUser)
        {
            row["_sprk_sentby_value"] = El(systemUser.ToString("D"));
            row["_sprk_sentby_value@OData.Community.Display.V1.FormattedValue"] = El(senderName);
        }
        if (createdByContact is { } contact)
        {
            row["_sprk_createdbycontact_value"] = El(contact.ToString("D"));
            row["_sprk_createdbycontact_value@OData.Community.Display.V1.FormattedValue"] = El(senderName);
        }
        _messageRows.Add(row);
        return row;
    }

    private static string IdOf(Dictionary<string, JsonElement> row) => row["sprk_communicationid"].GetString()!;

    // ── AC1: a grant to the caller within 30 days appears with its record type, id and title ───────

    [Theory]
    [InlineData(ExternalGrantRootType.Project, "projects")]
    [InlineData(ExternalGrantRootType.Matter, "matters")]
    [InlineData(ExternalGrantRootType.WorkAssignment, "workassignments")]
    public async Task ANewGrantToTheCaller_Appears_WithItsRecordTypeIdAndTitle(ExternalGrantRootType rootType, string segment)
    {
        var me = Guid.NewGuid();
        var root = Guid.NewGuid();
        var caller = rootType switch
        {
            ExternalGrantRootType.Project => Ciam(me, project: root),
            ExternalGrantRootType.Matter => Ciam(me, matter: root),
            _ => Ciam(me, workAssignment: root),
        };
        var grant = _data.AddGrant(me, rootType, root, Now.AddDays(-2));
        _data.Name(root, "Acme v. Widget");

        var feed = Body(await Feed(caller));

        var item = feed.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be(ExternalNotificationDto.KindGrant);
        item.Id.Should().Be(grant.ToString("D"));
        item.RecordType.Should().Be(segment);
        item.RecordId.Should().Be(root.ToString("D"));
        item.RecordTitle.Should().Be("Acme v. Widget");
        item.OccurredOn.Should().Be(Now.AddDays(-2));
        item.IsUnread.Should().BeTrue();
        feed.UnreadCount.Should().Be(1);
        _data.GrantReads.Should().ContainSingle().Which.Should().Be(
            (me, DateOnly.FromDateTime(Now.AddDays(-30).UtcDateTime), DateOnly.FromDateTime(Now.UtcDateTime)),
            "the grant read is keyed on the caller's OWN contact, from the server-resolved principal, for the 30-day window");
    }

    // ── Owner decision 2026-10-12: a grant renewed or restored in place produces a bell item ─────────────────────

    [Fact]
    public async Task ARenewedGrant_WithAnOldGrantedDate_Appears_AtTheRenewalTime()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        var grant = _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-60), renewedOn: Now.AddDays(-2));
        _data.Name(project, "Acme v. Widget");

        var feed = Body(await Feed(caller));

        var item = feed.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be(ExternalNotificationDto.KindGrant);
        item.Id.Should().Be(grant.ToString("D"));
        item.RecordType.Should().Be("projects");
        item.RecordTitle.Should().Be("Acme v. Widget");
        item.OccurredOn.Should().Be(Now.AddDays(-2), "the item time is the renewal, not the original grant");
        item.IsUnread.Should().BeTrue();
        _data.ModifiedSinceReads.Should().ContainSingle().Which.Should().Be(Now.AddDays(-30),
            "the read asks for rows modified inside the 30-day window");
    }

    [Fact]
    public async Task AGrantCreatedInTheWindow_AndThenRenewed_IsOneItem_AtTheRenewalTime()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        var grant = _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-20), renewedOn: Now.AddHours(-5));

        var feed = Body(await Feed(caller));

        var item = feed.Value.Should().ContainSingle("one row is one item, however many times it changed").Subject;
        item.Id.Should().Be(grant.ToString("D"));
        item.OccurredOn.Should().Be(Now.AddHours(-5));
    }

    [Fact]
    public async Task AnOldGrant_NeverRenewed_DoesNotAppear()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-60));

        Body(await Feed(caller)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ARenewalOutsideTheWindow_DoesNotAppear_EvenIfTheReadReturnsTheRow()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        _data.ReturnAllGrantRows = true; // the endpoint's own per-row re-check is the only gate here
        _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-60), renewedOn: Now.AddDays(-31));
        var inside = _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-60), renewedOn: Now.AddDays(-29));

        Body(await Feed(caller)).Value.Select(i => i.Id).Should().Equal(inside.ToString("D"));
    }

    [Fact]
    public async Task ARenewedGrant_OnARecordNoLongerAccessible_IsDropped()
    {
        var me = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var caller = Ciam(me, project: mine);
        _data.AddGrant(me, ExternalGrantRootType.Project, Guid.NewGuid(), Now.AddDays(-60), renewedOn: Now.AddDays(-1));
        var kept = _data.AddGrant(me, ExternalGrantRootType.Project, mine, Now.AddDays(-60), renewedOn: Now.AddDays(-2));

        Body(await Feed(caller)).Value.Select(i => i.Id).Should().Equal(kept.ToString("D"));
    }

    [Fact]
    public async Task ASomeoneElsesRenewedGrant_DoesNotAppear()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        _data.AddGrant(Guid.NewGuid(), ExternalGrantRootType.Project, project, Now.AddDays(-60), renewedOn: Now.AddDays(-1));

        Body(await Feed(caller)).Value.Should().BeEmpty("the read is keyed on the caller's own contact");
    }

    [Fact]
    public async Task ARenewal_IsUnreadOnlyAfterTheSeenMarker()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        var before = _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-60), renewedOn: Now.AddDays(-3));
        await Seen(caller, Now.AddDays(-2));

        var afterSeen = Body(await Feed(caller));
        afterSeen.Value.Single().IsUnread.Should().BeFalse("it happened before the marker");
        afterSeen.UnreadCount.Should().Be(0);

        var renewedAgain = _data.AddGrant(me, ExternalGrantRootType.Matter, Guid.NewGuid(), Now.AddDays(-60), renewedOn: Now.AddHours(-1));
        var caller2 = Ciam(me, project: project, matter: _data.LastRoot);
        var later = Body(await Feed(caller2));

        later.UnreadCount.Should().Be(1);
        later.Value.Single(i => i.IsUnread).Id.Should().Be(renewedAgain.ToString("D"));
        later.Value.Single(i => !i.IsUnread).Id.Should().Be(before.ToString("D"));
    }

    // ── AC2: a message by someone else on an accessible record appears; the caller's own does not ──

    [Fact]
    public async Task AMessageBySomeoneElse_OnAnAccessibleRecord_Appears_WithTheRecordAndSender()
    {
        var project = Guid.NewGuid();
        var caller = Ciam(Guid.NewGuid(), project: project);
        var row = Message("sprk_regardingproject", project, Now.AddHours(-3), senderName: "Pat Lawyer");
        _data.Name(project, "Project Alpha");

        var feed = Body(await Feed(caller));

        var item = feed.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be(ExternalNotificationDto.KindMessage);
        item.Id.Should().Be(IdOf(row));
        item.RecordType.Should().Be("projects");
        item.RecordId.Should().Be(project.ToString("D"));
        item.RecordTitle.Should().Be("Project Alpha");
        item.SenderName.Should().Be("Pat Lawyer");
        item.OccurredOn.Should().Be(Now.AddHours(-3));
        feed.UnreadCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AContactCallersOwnMessage_DoesNotAppear_ButAnotherContactsDoes(bool workforce)
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = workforce ? WorkforceContact(me, project) : Ciam(me, project: project);
        Message("sprk_regardingproject", project, Now.AddHours(-1), createdByContact: me, senderName: "Me");
        var theirs = Message("sprk_regardingproject", project, Now.AddHours(-2), createdByContact: Guid.NewGuid(), senderName: "Colleague");

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(theirs));
        feed.UnreadCount.Should().Be(1, "the caller's own post is neither an item nor counted");
    }

    [Fact]
    public async Task ASystemUserCallersOwnMessage_DoesNotAppear_DecidedBySentBy()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = SystemUser(me, project);
        _identity.Setup(r => r.IsExternalAsync(me, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Message("sprk_regardingproject", project, Now.AddHours(-1), sentBy: me);
        var other = Message("sprk_regardingproject", project, Now.AddHours(-2), sentBy: Guid.NewGuid());

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(other));
    }

    // ── AC3: an internal-only message never appears for a contact caller ────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, null)] // an unreadable flag is hidden from a contact (the shared filter fails closed)
    [InlineData(true, true)]
    [InlineData(true, null)]
    public async Task AnInternalOnlyMessage_NeverAppears_NorCounts_ForAContactCaller(bool workforce, bool? internalOnly)
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = workforce ? WorkforceContact(me, project) : Ciam(me, project: project);
        Message("sprk_regardingproject", project, Now.AddHours(-1), internalOnly: internalOnly);
        var visible = Message("sprk_regardingproject", project, Now.AddHours(-2), internalOnly: false);

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(visible));
        feed.UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task AnInternalSystemUser_DoesSeeAnInternalOnlyMessage_TheFilterIsTheSharedOne()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        _identity.Setup(r => r.IsExternalAsync(me, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var internalOnly = Message("sprk_regardingproject", project, Now.AddHours(-1), internalOnly: true);

        var feed = Body(await Feed(SystemUser(me, project)));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(internalOnly));
    }

    // ── owner 2026-10-11: privileged messages are never visible to external callers ─────────────────

    [Theory]
    [InlineData(PrivilegePrivileged)]
    [InlineData(PrivilegePotentiallyPrivileged)]
    [InlineData(100000099)] // a value the code does not define: hidden, not shown
    public async Task AHiddenPrivilegeMessage_NeverProducesAFeedItemOrACount_EvenForAnInternalSystemUser(int privilege)
    {
        var project = Guid.NewGuid();
        var contact = Ciam(Guid.NewGuid(), project: project);
        var staffId = Guid.NewGuid();
        _identity.Setup(r => r.IsExternalAsync(staffId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Message("sprk_regardingproject", project, Now.AddHours(-1), privilege: privilege);

        foreach (var caller in new[] { contact, SystemUser(staffId, project) })
        {
            var feed = Body(await Feed(caller));
            feed.Value.Should().BeEmpty("a hidden-privilege message is never a feed item");
            feed.UnreadCount.Should().Be(0, "nor is it counted");
        }

        _queries.Should().NotBeEmpty().And.AllSatisfy(q =>
            q.Should().Contain(CommunicationThreadReadService.ExternalPrivilegeFilterClause,
                "the privilege rule is applied by Dataverse before the bound, from the one shared list"));
    }

    [Fact]
    public async Task ANoneOrUnclassifiedMessage_IsShown()
    {
        var project = Guid.NewGuid();
        var none = Message("sprk_regardingproject", project, Now.AddHours(-1), privilege: PrivilegeNone);
        var unclassified = Message("sprk_regardingproject", project, Now.AddHours(-2), privilege: null);

        var feed = Body(await Feed(Ciam(Guid.NewGuid(), project: project)));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(none), IdOf(unclassified));
    }

    [Fact]
    public async Task AnUnsentMessage_Draft_NeverAppears()
    {
        var project = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1), status: StatusDraft);
        var sent = Message("sprk_regardingproject", project, Now.AddHours(-2), status: StatusSend);

        var feed = Body(await Feed(Ciam(Guid.NewGuid(), project: project)));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(sent));
    }

    // ── AC4: a record outside the caller's accessible set never appears ─────────────────────────────

    [Fact]
    public async Task AGrantRowForARecordOutsideTheAccessibleSet_IsDropped()
    {
        var me = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var revokedOrRestricted = Guid.NewGuid();
        var caller = Ciam(me, project: mine);
        _data.AddGrant(me, ExternalGrantRootType.Project, revokedOrRestricted, Now.AddDays(-1));
        _data.AddGrant(me, ExternalGrantRootType.Matter, Guid.NewGuid(), Now.AddDays(-1));
        var kept = _data.AddGrant(me, ExternalGrantRootType.Project, mine, Now.AddDays(-3));

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(kept.ToString("D"));
        feed.UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task AMessageOnARecordOutsideTheSet_NeverAppears_AndTheQueryIsKeyedOnlyOnTheSet()
    {
        var project = Guid.NewGuid();
        var foreignProject = Guid.NewGuid();
        var foreignMatter = Guid.NewGuid();
        var caller = Ciam(Guid.NewGuid(), project: project);
        Message("sprk_regardingproject", foreignProject, Now.AddHours(-1));
        Message("sprk_regardingmatter", foreignMatter, Now.AddHours(-1));
        Message("sprk_regardinginvoice", Guid.NewGuid(), Now.AddHours(-1));
        var mine = Message("sprk_regardingproject", project, Now.AddHours(-2));

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(mine));
        var query = _queries.Should().ContainSingle().Subject!;
        query.Should().Contain($"_sprk_regardingproject_value eq {project}");
        query.Should().NotContain(foreignProject.ToString()).And.NotContain(foreignMatter.ToString());
    }

    [Fact]
    public async Task ACallerWithNoAccessibleRecordAndNoGrant_GetsAnEmptyFeed_WithNoMessageRead()
    {
        Message("sprk_regardingproject", Guid.NewGuid(), Now.AddHours(-1));

        var feed = Body(await Feed(Ciam(Guid.NewGuid())));

        feed.Value.Should().BeEmpty();
        feed.UnreadCount.Should().Be(0);
        _appOnly.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── service requests: the requester's own (sprk_requestedby), through the module predicate ─────

    [Fact]
    public async Task AMessageOnTheRequestersOwnServiceRequest_Appears_ButNoOneElsesRequestIsRead()
    {
        var me = Guid.NewGuid();
        var mine = _data.AddServiceRequest(requestedBy: me, "NDA with Contoso");
        var theirs = _data.AddServiceRequest(requestedBy: Guid.NewGuid(), "Someone else's");
        var onMine = Message("sprk_regardingservicerequest", mine, Now.AddHours(-1), senderName: "Legal");
        Message("sprk_regardingservicerequest", theirs, Now.AddHours(-1));

        var feed = Body(await Feed(WorkforceContact(me)));

        var item = feed.Value.Should().ContainSingle().Subject;
        item.Id.Should().Be(IdOf(onMine));
        item.RecordType.Should().Be("servicerequests");
        item.RecordId.Should().Be(mine.ToString("D"));
        item.RecordTitle.Should().Be("NDA with Contoso");
        _data.ServiceRequestReads.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { me });
        _queries.Single().Should().NotContain(theirs.ToString());
    }

    [Fact]
    public async Task ACiamRequester_SwitchOff_GetsNoServiceRequestMessages_AndSwitchOn_Does()
    {
        var me = Guid.NewGuid();
        var mine = _data.AddServiceRequest(requestedBy: me, "Request");
        var onMine = Message("sprk_regardingservicerequest", mine, Now.AddHours(-1));

        Body(await Feed(Ciam(me), ProductionRegistry(partnerServiceRequestsEnabled: false))).Value.Should().BeEmpty();
        _data.ServiceRequestReads.Should().BeEmpty("with the switch off a partner has no service-request scope, so nothing is read");

        Body(await Feed(Ciam(me), ProductionRegistry(partnerServiceRequestsEnabled: true))).Value
            .Select(i => i.Id).Should().Equal(IdOf(onMine));
    }

    // ── deleted records (task 032) never break the feed ─────────────────────────────────────────────

    [Fact]
    public async Task AnItemWhoseRecordNoLongerExists_IsDropped_AndTheFeedStillAnswers()
    {
        var me = Guid.NewGuid();
        var live = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var caller = Ciam(me, project: live, matter: deleted);
        _data.Name(live, "Live");
        _data.Delete(deleted);
        // `deleted` is in the (cached) accessible set and still has a grant row and a message, but no longer exists.
        _data.AddGrant(me, ExternalGrantRootType.Matter, deleted, Now.AddDays(-1));
        Message("sprk_regardingmatter", deleted, Now.AddHours(-1));
        var kept = Message("sprk_regardingproject", live, Now.AddHours(-2));

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(kept));
    }

    [Fact]
    public async Task WhenTheTitleReadFails_ItemsAreKept_WithNoTitle()
    {
        var project = Guid.NewGuid();
        _data.NamesReadFails = true;
        var row = Message("sprk_regardingproject", project, Now.AddHours(-1));

        var feed = Body(await Feed(Ciam(Guid.NewGuid(), project: project)));

        var item = feed.Value.Should().ContainSingle().Subject;
        item.Id.Should().Be(IdOf(row));
        item.RecordTitle.Should().BeNull("an unreadable title is not a deleted record");
        feed.Truncated.Should().BeTrue("a title read that failed is reported, never silent (review K2)");
    }

    [Fact]
    public async Task WhenEveryTitleReadIsComplete_TheFeedIsNotFlaggedTruncated()
    {
        var project = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1));

        Body(await Feed(Ciam(Guid.NewGuid(), project: project))).Truncated.Should().BeFalse();
    }

    // ── review F3-a: a contact that could not be READ must not produce a silently incomplete feed ──

    private static CallerPrincipal UnreadableContact(Guid systemUserId, Guid project) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.Empty,
        ContactUnreadable = true,
        SystemUserId = systemUserId,
        Email = "lawyer@spaarke.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(project, ExternalAccessLevel.FullAccess) },
    };

    [Fact]
    public async Task AnUnreadableContact_GetsA503_WithTheCode_AndNoFeedReadIsMade()
    {
        var project = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1));

        var result = await Feed(UnreadableContact(Guid.NewGuid(), project));

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be(ExternalNotificationFeedEndpoints.ErrorContactUnreadable);
        ExternalNotificationFeedEndpoints.ErrorContactUnreadable.Should().Be("EXT_NOTIFICATIONS_CONTACT_UNREADABLE");
        problem.ProblemDetails.Detail.Should().Be(
            "Your account could not be checked right now. Your notifications were not read or changed; try again shortly.",
            "a fixed retry detail, no exception text");
        _data.GrantReads.Should().BeEmpty();
        _data.ServiceRequestReads.Should().BeEmpty();
        _data.NameReads.Should().BeEmpty();
        _queries.Should().BeEmpty("no message query is made after the check");
        _appOnly.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnreadableContact_SeenIs503_AndNoMarkerIsWritten()
    {
        var cache = new Mock<ITenantCache>(MockBehavior.Strict);
        _cache = cache.Object;

        var result = await Seen(UnreadableContact(Guid.NewGuid(), Guid.NewGuid()), Now);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be(ExternalNotificationFeedEndpoints.ErrorContactUnreadable);
        cache.VerifyNoOtherCalls(); // Strict: any SetAsync would have thrown; none was made.
    }

    [Fact]
    public async Task ASystemUserWithAContact_IsUnaffectedByTheUnreadableContactCheck()
    {
        var systemUser = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = SystemUser(systemUser, project);
        _identity.Setup(i => i.IsExternalAsync(systemUser, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Message("sprk_regardingproject", project, Now.AddHours(-1));

        var feed = Body(await Feed(caller));
        feed.Value.Should().ContainSingle();
        _data.GrantReads.Should().ContainSingle("the grant source is read for a readable contact");

        (await Seen(caller, Now)).Should().BeOfType<Ok<ExternalNotificationSeenResponse>>();
    }

    // ── AC5: unreadCount against last-seen; POST /seen clears it until a newer item arrives ─────────

    [Fact]
    public async Task UnreadCount_DropsToZeroAfterSeen_UntilANewerItemArrives()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-1));
        Message("sprk_regardingproject", project, Now.AddHours(-1));

        var before = Body(await Feed(caller));
        before.UnreadCount.Should().Be(2);
        before.LastSeenAt.Should().BeNull();

        var seen = await Seen(caller, Now);
        seen.Should().BeOfType<Ok<ExternalNotificationSeenResponse>>()
            .Which.Value!.LastSeenAt.Should().Be(Now);

        var after = Body(await Feed(caller, now: Now.AddMinutes(1)));
        after.UnreadCount.Should().Be(0);
        after.LastSeenAt.Should().Be(Now);
        after.Value.Should().HaveCount(2).And.OnlyContain(i => !i.IsUnread, "the items stay in the feed, read");

        var newer = Message("sprk_regardingproject", project, Now.AddMinutes(5));
        var later = Body(await Feed(caller, now: Now.AddMinutes(10)));
        later.UnreadCount.Should().Be(1);
        later.Value.Single(i => i.IsUnread).Id.Should().Be(IdOf(newer));
    }

    [Fact]
    public async Task TheMarkerIsPerPrincipal_OneCallersSeenDoesNotClearAnothers()
    {
        var project = Guid.NewGuid();
        var alice = Ciam(Guid.NewGuid(), project: project);
        var bob = Ciam(Guid.NewGuid(), project: project);
        Message("sprk_regardingproject", project, Now.AddHours(-1));

        await Seen(alice, Now);

        Body(await Feed(alice)).UnreadCount.Should().Be(0);
        Body(await Feed(bob)).UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task ACacheMiss_MarksEveryItemUnread()
    {
        var project = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1));
        Message("sprk_regardingproject", project, Now.AddDays(-29));

        var feed = Body(await Feed(Ciam(Guid.NewGuid(), project: project)));

        feed.LastSeenAt.Should().BeNull();
        feed.UnreadCount.Should().Be(2);
        feed.Value.Should().OnlyContain(i => i.IsUnread);
    }

    [Fact]
    public async Task AnUnreadableMarker_IsACacheMiss_AndTheFeedStillAnswers()
    {
        var project = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1));
        var faulty = new Mock<ITenantCache>();
        faulty.Setup(c => c.GetAsync<ExternalNotificationFeedEndpoints.SeenMarker>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        _cache = faulty.Object;

        var feed = Body(await Feed(Ciam(Guid.NewGuid(), project: project)));

        feed.UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task Seen_StoresTheMarker_KeyedByThePrincipalId_WithAThirtyDayTtl()
    {
        var contact = Guid.NewGuid();
        var systemUser = Guid.NewGuid();
        var cache = new Mock<ITenantCache>();
        _cache = cache.Object;

        await Seen(Ciam(contact), Now);
        await Seen(SystemUser(systemUser), Now);

        cache.Verify(c => c.SetAsync(Tenant, ExternalNotificationFeedEndpoints.MarkerResource, $"contact:{contact:D}",
            ExternalNotificationFeedEndpoints.MarkerVersion, It.Is<ExternalNotificationFeedEndpoints.SeenMarker>(m => m.SeenAt == Now),
            TimeSpan.FromDays(30), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.SetAsync(Tenant, ExternalNotificationFeedEndpoints.MarkerResource, $"systemuser:{systemUser:D}",
            ExternalNotificationFeedEndpoints.MarkerVersion, It.IsAny<ExternalNotificationFeedEndpoints.SeenMarker>(),
            TimeSpan.FromDays(30), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "a systemuser is keyed by its systemuser id even though it also carries a linked contact");
    }

    [Fact]
    public async Task Seen_ByAnUnattributablePrincipal_Is403_AndACacheFault_Is503()
    {
        var unattributable = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.Empty,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };
        var forbidden = (await Seen(unattributable, Now)).Should().BeOfType<ProblemHttpResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        forbidden.ProblemDetails.Extensions["reasonCode"].Should().Be(ExternalNotificationFeedEndpoints.DenyCallerUnattributable);

        var faulty = new Mock<ITenantCache>();
        faulty.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<ExternalNotificationFeedEndpoints.SeenMarker>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        _cache = faulty.Object;
        var unavailable = (await Seen(Ciam(Guid.NewGuid()), Now)).Should().BeOfType<ProblemHttpResult>().Subject;
        unavailable.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        unavailable.ProblemDetails.Extensions["errorCode"].Should().Be(ExternalNotificationFeedEndpoints.ErrorSeenUnavailable);
    }

    // ── AC6: older than 30 days excluded; at most 100 items, newest first ───────────────────────────

    [Fact]
    public async Task ItemsOlderThanThirtyDays_AreExcluded()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-31));
        var recentGrant = _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddDays(-29));
        Message("sprk_regardingproject", project, Now.AddDays(-30).AddMinutes(-1));
        var recent = Message("sprk_regardingproject", project, Now.AddDays(-30).AddMinutes(1));

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().BeEquivalentTo(new[] { recentGrant.ToString("D"), IdOf(recent) });
        _queries.Single().Should().Contain("createdon ge 2026-09-12T12:00:00.000Z", "the window is applied by the query too");
    }

    [Fact]
    public async Task AGrantOnTheBoundaryDay_IsKeptOrDroppedByItsInstant_NotByItsDate()
    {
        // Now = 2026-10-12T12:00Z, so the cutoff is 2026-09-12T12:00Z. The in-memory read, like a date-only filter, keeps
        // every row of the boundary DAY; the endpoint's own per-row re-check must cut the morning one (review K4).
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        _data.AddGrant(me, ExternalGrantRootType.Project, project, new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero));
        var afternoon = _data.AddGrant(me, ExternalGrantRootType.Project, project, new DateTimeOffset(2026, 9, 12, 13, 0, 0, TimeSpan.Zero));

        var feed = Body(await Feed(caller));

        _data.GrantReads.Single().Since.Should().Be(new DateOnly(2026, 9, 12), "the read is handed the boundary DAY");
        feed.Value.Select(i => i.Id).Should().Equal(afternoon.ToString("D"));
    }

    [Fact]
    public async Task AtMostOneHundredItems_NewestFirst_AndTheCutIsFlagged()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Ciam(me, project: project);
        for (var i = 0; i < 70; i++)
            _data.AddGrant(me, ExternalGrantRootType.Project, project, Now.AddHours(-1 - (2 * i)));
        for (var i = 0; i < 70; i++)
            Message("sprk_regardingproject", project, Now.AddHours(-2 - (2 * i)));

        var feed = Body(await Feed(caller));

        feed.Value.Should().HaveCount(ExternalNotificationFeedEndpoints.MaxItems);
        feed.Value.Select(i => i.OccurredOn).Should().BeInDescendingOrder();
        feed.Value.First().OccurredOn.Should().Be(Now.AddHours(-1));
        feed.Truncated.Should().BeTrue("140 qualifying items were cut to 100");
        feed.UnreadCount.Should().Be(100);
        _queries.Single().Should().Contain("$top=101");
    }

    // ── a message filed to SEVERAL core roots (fix round, task 039; owner decision 2026-10-12, option B) ──
    // Not reported when a stamped root the caller cannot read is SECURE, or its secure state cannot be read.

    private static ExternalGrantRootType RootOfStamp(string column) =>
        column == "_sprk_regardingmatter_value" ? ExternalGrantRootType.Matter : ExternalGrantRootType.WorkAssignment;

    [Theory]
    [InlineData("_sprk_regardingmatter_value")]
    [InlineData("_sprk_regardingworkassignment_value")]
    public async Task AMessageAlsoFiledToASecureRootTheCallerCannotRead_IsNotInTheFeed(string otherRootColumn)
    {
        var project = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        _roots.AddSecureRoot(RootOfStamp(otherRootColumn), hidden);
        var caller = Ciam(Guid.NewGuid(), project: project);
        var plain = Message("sprk_regardingproject", project, Now.AddHours(-2));
        var dual = Message("sprk_regardingproject", project, Now.AddHours(-1));
        dual[otherRootColumn] = El(hidden.ToString("D"));
        _data.Name(project, "Project Alpha");

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(plain));
        feed.UnreadCount.Should().Be(1);
    }

    [Theory]
    [InlineData("_sprk_regardingmatter_value")]
    [InlineData("_sprk_regardingworkassignment_value")]
    public async Task AMessageAlsoFiledToANonSecureRootTheCallerCannotRead_IsInTheFeed_OptionB(string otherRootColumn)
    {
        var project = Guid.NewGuid();
        var open = Guid.NewGuid();
        _roots.AddOpenRoot(RootOfStamp(otherRootColumn), open);
        var caller = Ciam(Guid.NewGuid(), project: project);
        var dual = Message("sprk_regardingproject", project, Now.AddHours(-1));
        dual[otherRootColumn] = El(open.ToString("D"));
        _data.Name(project, "Project Alpha");

        Body(await Feed(caller)).Value.Select(i => i.Id).Should().Equal(IdOf(dual));
    }

    [Fact]
    public async Task AMessageAlsoFiledToASecureRootTheCallerCanRead_IsInTheFeed_Once_WithoutAFlagRead()
    {
        var project = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, matter);
        var caller = Ciam(Guid.NewGuid(), project: project, matter: matter);
        var dual = Message("sprk_regardingproject", project, Now.AddHours(-1));
        dual["_sprk_regardingmatter_value"] = El(matter.ToString("D"));
        _data.Name(project, "Project Alpha");
        _data.Name(matter, "Matter Beta");

        var feed = Body(await Feed(caller));

        feed.Value.Select(i => i.Id).Should().Equal(IdOf(dual));
        _roots.FlagReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("fault")]
    [InlineData("missing")]
    [InlineData("flag-null")]
    [InlineData("malformed")]
    public async Task AMessageWhoseOtherRootsSecureStateCannotBeRead_IsNotInTheFeed_FailClosed(string failure)
    {
        var project = Guid.NewGuid();
        var matter = Guid.NewGuid();
        switch (failure)
        {
            case "throws": _roots.AddOpenRoot(ExternalGrantRootType.Matter, matter); _roots.FlagReadThrows = true; break;
            case "fault": _roots.AddOpenRoot(ExternalGrantRootType.Matter, matter); _roots.FlagFault = System.Net.HttpStatusCode.ServiceUnavailable; break;
            case "flag-null": _roots.AddRoot(ExternalGrantRootType.Matter, matter, isSecure: null); break;
            // "missing": no matter record; "malformed": the stamp is not a GUID.
        }

        var caller = Ciam(Guid.NewGuid(), project: project);
        var plain = Message("sprk_regardingproject", project, Now.AddHours(-2));
        var dual = Message("sprk_regardingproject", project, Now.AddHours(-1));
        dual["_sprk_regardingmatter_value"] = El(failure == "malformed" ? "not-a-guid" : matter.ToString("D"));
        _data.Name(project, "Project Alpha");

        Body(await Feed(caller)).Value.Select(i => i.Id).Should().Equal(IdOf(plain));
    }

    // Task 187 fix round 1 (R2): a feed message's OTHER parent lookups are parsed and their LIVE roots decide. A message on P
    // whose sprk_regardinginvoice / sprk_regardingevent names a record filed under secure matter X is not reported to a
    // reader of P alone; it is to a reader of P and X.
    [Theory]
    [InlineData("_sprk_regardinginvoice_value", "sprk_invoice", "sprk_matter")]
    [InlineData("_sprk_regardingevent_value", "sprk_event", "sprk_regardingmatter")]
    public async Task AMessageWhoseOtherParentIsUnderSecureX_IsNotInTheFeedOfAPOnlyReader_ButIsForAPAndXReader(
        string column, string parentTable, string parentRootColumn)
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var secure = Guid.NewGuid();
        var parent = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, secure);
        _roots.AddFilingRow(parentTable, parent, (parentRootColumn, "sprk_matter", secure));
        var plain = Message("sprk_regardingproject", project, Now.AddHours(-2));
        var carrier = Message("sprk_regardingproject", project, Now.AddHours(-1));
        carrier[column] = El(parent.ToString("D"));
        _data.Name(project, "Project Alpha");
        _data.Name(secure, "Matter X");

        Body(await Feed(Ciam(me, project: project))).Value.Select(i => i.Id)
            .Should().Equal(new[] { IdOf(plain) }, "the message's live parent is under secure X, which the caller cannot read");
        Body(await Feed(Ciam(me, project: project, matter: secure))).Value.Select(i => i.Id)
            .Should().Equal(IdOf(carrier), IdOf(plain));
    }

    // Task 065 (D7) x task 187 (merge of master into r3): the two rules meet on the message's own thread. The thread reads
    // explicitly OPEN, so the D7 gate admits the message; the thread is also a parent the live-parent rule walks, and this
    // one is filed under secure matter X. The message is then behind X's wall: not reported to a reader of P alone, reported
    // to a reader of P and X.
    [Fact]
    public async Task AMessageInAnOpenThreadFiledUnderSecureX_IsNotInTheFeedOfAPOnlyReader_ButIsForAPAndXReader()
    {
        var me = Guid.NewGuid();
        var project = Guid.NewGuid();
        var secure = Guid.NewGuid();
        _roots.AddSecureRoot(ExternalGrantRootType.Matter, secure);
        _roots.AddFilingRow("sprk_communicationthread", AppOnlySelectProjection.OpenThreadId, ("sprk_regardingmatter", "sprk_matter", secure));
        var message = Message("sprk_regardingproject", project, Now.AddHours(-1));
        _data.Name(project, "Project Alpha");
        _data.Name(secure, "Matter X");

        Body(await Feed(Ciam(me, project: project))).Value.Should().BeEmpty(
            "the thread is Open, but it is filed under secure X, which the caller cannot read");
        Body(await Feed(Ciam(me, project: project, matter: secure))).Value.Select(i => i.Id).Should().Equal(IdOf(message));
    }

    // Main-session decision 2026-10-12: "secure" means EFFECTIVELY secure (UAC-r2 task 174). A stamped work assignment W whose
    // OWN flag is false but which is filed under a secure matter is secure, even when the message carries no stamp for it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMessageFiledToAWorkAssignmentWithAFalseOwnFlag_IsInTheFeedOnlyWhenItsMatterIsNotSecure(bool matterSecure)
    {
        var project = Guid.NewGuid();
        var workAssignment = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.WorkAssignment, workAssignment);
        _roots.AddRoot(ExternalGrantRootType.Matter, matter, matterSecure);
        _roots.FileWorkAssignmentUnder(workAssignment, ExternalGrantRootType.Matter, matter);
        var caller = Ciam(Guid.NewGuid(), project: project);
        var plain = Message("sprk_regardingproject", project, Now.AddHours(-2));
        var dual = Message("sprk_regardingproject", project, Now.AddHours(-1));
        dual["_sprk_regardingworkassignment_value"] = El(workAssignment.ToString("D"));
        _data.Name(project, "Project Alpha");

        Body(await Feed(caller)).Value.Select(i => i.Id).Should().Equal(
            matterSecure ? new[] { IdOf(plain) } : new[] { IdOf(dual), IdOf(plain) });
    }

    [Fact]
    public async Task AMessageFiledToAWorkAssignment_WhoseFilingChainCannotBeRead_IsNotInTheFeed_FailClosed()
    {
        var project = Guid.NewGuid();
        var workAssignment = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.WorkAssignment, workAssignment);
        _roots.AddOpenRoot(ExternalGrantRootType.Matter, matter);
        _roots.FileWorkAssignmentUnder(workAssignment, ExternalGrantRootType.Matter, matter);
        // Only the work assignment's chain (its matter) faults. Every message's thread is now also read by the live-parent walk
        // (task 187 x D7), so a blanket FilingReadThrows would withhold the plain message too, which is not what this case is about.
        _roots.FilingFaultEntity = "sprk_matter";
        var caller = Ciam(Guid.NewGuid(), project: project);
        var plain = Message("sprk_regardingproject", project, Now.AddHours(-2));
        var dual = Message("sprk_regardingproject", project, Now.AddHours(-1));
        dual["_sprk_regardingworkassignment_value"] = El(workAssignment.ToString("D"));
        _data.Name(project, "Project Alpha");

        Body(await Feed(caller)).Value.Select(i => i.Id).Should().Equal(IdOf(plain));
    }

    [Fact]
    public async Task ManyFeedMessagesStampedWithTheSameUnreadableRoot_CostOneFlagQuery()
    {
        var project = Guid.NewGuid();
        var open = Guid.NewGuid();
        _roots.AddOpenRoot(ExternalGrantRootType.Matter, open);
        var caller = Ciam(Guid.NewGuid(), project: project);
        for (var i = 1; i <= 5; i++)
        {
            var row = Message("sprk_regardingproject", project, Now.AddHours(-i));
            row["_sprk_regardingmatter_value"] = El(open.ToString("D"));
        }

        _data.Name(project, "Project Alpha");

        Body(await Feed(caller)).Value.Should().HaveCount(5);
        _roots.FlagReads.Should().ContainSingle().Which.Should().Contain(open.ToString());
    }

    [Fact]
    public async Task TheFeedQuery_ProjectsNoBodyFormatOrSubject_ButEveryColumnItsRulesNeed()
    {
        var project = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1));

        await Feed(Ciam(Guid.NewGuid(), project: project));

        var query = _queries.Should().ContainSingle().Subject!;
        var select = query[..query.IndexOf('&')];
        // Task 187 appends the message's non-root parent lookups (ExternalMultiRootRule.LiveParentSelect("sprk_communication"),
        // ordered by column) after the three root stamps. The thread lookup is one of them; ExternalSelect de-duplicates, so it is
        // selected once, as D7's column.
        select.Should().Be(
            "$select=sprk_communicationid,sprk_direction,_sprk_sentby_value,_sprk_createdbycontact_value,_createdby_value,createdon," +
            "sprk_isinternalonly,sprk_privilegeclassification,statuscode,_sprk_communicationthread_value," +
            "_sprk_regardingproject_value,_sprk_regardingmatter_value,_sprk_regardingworkassignment_value," +
            "_sprk_regardinganalysis_value,_sprk_regardingbudget_value,_sprk_regardingevent_value," +
            "_sprk_regardinginvoice_value,_sprk_regardingreportcard_value");
        select.Should().NotContain("sprk_body").And.NotContain("sprk_subject");
        // Task 062 rework: createdby is expanded to its applicationid + accessmode so the sender-name fallback can tell a
        // person from the BFF's application user (nothing else of the user is read).
        query.Should().Contain("&$expand=createdby($select=applicationid,accessmode)&");
    }

    [Fact]
    public async Task AFeedUnderTheBound_IsNotFlaggedTruncated()
    {
        var project = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1));

        Body(await Feed(Ciam(Guid.NewGuid(), project: project))).Truncated.Should().BeFalse();
    }

    // ── the response crosses only the allow-listed fields ───────────────────────────────────────────

    [Fact]
    public async Task TheSerializedFeed_CarriesNoBodySubjectAddressPrivilegeOrIds()
    {
        var project = Guid.NewGuid();
        var sender = Guid.NewGuid();
        Message("sprk_regardingproject", project, Now.AddHours(-1), sentBy: sender);

        var json = JsonSerializer.Serialize(Body(await Feed(Ciam(Guid.NewGuid(), project: project))));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("value", "unreadCount", "lastSeenAt");
        doc.RootElement.GetProperty("value")[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "kind", "id", "recordType", "recordId", "recordTitle", "occurredOn", "senderName", "isUnread");
        json.Should().NotContain(sender.ToString()).And.NotContain("Body").And.NotContain("Subject");
    }

    // =====================================================================
    // In-memory ExternalDataService: the three feed reads
    // =====================================================================

    private sealed class FeedData : ExternalDataService
    {
        private readonly List<(Guid Contact, RecentGrant Grant)> _grants = new();
        private readonly List<RequestedServiceRequest> _requests = new();
        private readonly Dictionary<Guid, string> _names = new();
        private readonly HashSet<Guid> _deleted = new();

        public FeedData(ExternalRootEventReadTests.FakeDataverse dataverse)
            : base(
                new HttpClient(dataverse),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                    .Build(),
                // A test token, so the base secure-flag read reaches the fake Web API (the three feed reads are overridden).
                new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
                NullLogger<ExternalDataService>.Instance,
                dataverse.FilingReader)
        {
        }

        public List<(Guid Contact, DateOnly Since, DateOnly Today)> GrantReads { get; } = new();
        public List<DateTimeOffset?> ModifiedSinceReads { get; } = new();

        /// <summary>When true the read returns every row of the contact, unfiltered: the endpoint's own re-check is then the only gate.</summary>
        public bool ReturnAllGrantRows { get; set; }

        /// <summary>The root id of the last grant added (so a test can build a principal that can read it).</summary>
        public Guid LastRoot { get; private set; }
        public List<IReadOnlyCollection<Guid>> ServiceRequestReads { get; } = new();
        public List<string> NameReads { get; } = new();
        public bool NamesReadFails { get; set; }

        public Guid AddGrant(Guid contact, ExternalGrantRootType rootType, Guid rootId, DateTimeOffset grantedOn,
            DateTimeOffset? renewedOn = null)
        {
            var id = Guid.NewGuid();
            LastRoot = rootId;
            _grants.Add((contact, new RecentGrant(id, rootType, rootId, grantedOn, renewedOn)));
            return id;
        }

        public Guid AddServiceRequest(Guid requestedBy, string name)
        {
            var id = Guid.NewGuid();
            _requests.Add(new RequestedServiceRequest(id, name, requestedBy));
            return id;
        }

        public void Name(Guid id, string name) => _names[id] = name;

        /// <summary>The record no longer exists: a complete title read does not return it.</summary>
        public void Delete(Guid id) => _deleted.Add(id);

        // As Dataverse would: the caller's contact, active, granted on/after the date (the expiry is not modelled here).
        // `(granteddate ge since or modifiedon ge modifiedSince)`: a renewed row is returned by its modifiedon.
        public override Task<RecentGrantsRead> GetRecentGrantsAsync(
            Guid contactId, DateOnly since, DateOnly today, CancellationToken ct = default,
            DateTimeOffset? modifiedSince = null)
        {
            GrantReads.Add((contactId, since, today));
            ModifiedSinceReads.Add(modifiedSince);
            var rows = _grants
                .Where(g => g.Contact == contactId
                            && (ReturnAllGrantRows
                                || DateOnly.FromDateTime(g.Grant.GrantedOn.UtcDateTime) >= since
                                || (modifiedSince is { } m && g.Grant.RenewedOn is { } renewed && renewed >= m)))
                .Select(g => g.Grant)
                .ToList();
            return Task.FromResult(new RecentGrantsRead(rows, false));
        }

        public override Task<RequestedServiceRequestsRead> GetServiceRequestsRequestedByAsync(
            IReadOnlyCollection<Guid> requesterIds, CancellationToken ct = default)
        {
            ServiceRequestReads.Add(requesterIds.ToList());
            return Task.FromResult(new RequestedServiceRequestsRead(
                _requests.Where(r => requesterIds.Contains(r.RequestedBy)).ToList(), false));
        }

        // Every record exists (titled "Record" unless named) except one marked deleted.
        public override Task<RecordNamesRead> GetRecordNamesAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        {
            NameReads.Add(entityLogicalName);
            if (NamesReadFails)
                return Task.FromResult(new RecordNamesRead(new Dictionary<Guid, string?>(), Complete: false));

            var names = ids.Where(id => !_deleted.Contains(id))
                .ToDictionary(id => id, id => (string?)(_names.TryGetValue(id, out var name) ? name : "Record"));
            return Task.FromResult(new RecordNamesRead(names, Complete: true));
        }
    }
}

/// <summary>
/// The feed routes are mapped in the <c>/api/v1/external</c> group (its authorization policy inherited) and throttled per
/// user; nothing else is mapped on <c>/notifications</c>.
/// </summary>
[Trait("category", "external-access")]
public class ExternalNotificationFeedRouteTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ExternalNotificationFeedRouteTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void FeedRoutes_AreMappedInTheExternalGroup_Authorized_AndRateLimited()
    {
        EndpointTable.AssertMapped(_factory, "GET", "/api/v1/external/notifications");
        EndpointTable.AssertMapped(_factory, "POST", "/api/v1/external/notifications/seen");
        EndpointTable.Maps(_factory, "POST", "/api/v1/external/notifications").Should().BeFalse();
        EndpointTable.Maps(_factory, "DELETE", "/api/v1/external/notifications").Should().BeFalse();
        EndpointTable.Maps(_factory, "GET", "/api/v1/external/notifications/seen").Should().BeFalse();

        var endpoints = EndpointTable.Endpoints(_factory)
            .Where(e => e.RoutePattern.RawText is "/api/v1/external/notifications" or "/api/v1/external/notifications/seen")
            .ToList();
        endpoints.Should().HaveCount(2);
        endpoints.Should().AllSatisfy(endpoint =>
        {
            endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Should().NotBeEmpty("each route inherits the external collaboration group's authorization policy");
            endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()!
                .PolicyName.Should().Be("dataverse-query");
        });
    }
}
