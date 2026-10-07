// unified-access-control-r2 task 172 — GitHub #1011: the membership resolver binds a TEAM-owned Owner column.
//
// The defect: MembershipFieldDiscoveryService gives every AttributeTypeCode.Owner column the synthetic targets
// { systemuser, team } and the classifier used to keep only the FIRST target found in IncludedIdentityTables, so
// `ownerid` was always ONE SystemUser descriptor, bound to the caller's own systemuserid. On a team-owned record
// `ownerid` holds the team's id, so the record resolved to nobody through its Owner column — while team ownership is
// the product convention (owner decision D-11, 2026-09-22).
//
// The fix: a polymorphic lookup binds every identity type its targets admit. `ownerid` is a SystemUser descriptor AND
// a Team descriptor (same field, same role), so on the DEFAULT and ACCESS-CONFERRING surfaces a record owned by a
// team the caller belongs to resolves under `owner`. The PEOPLE-TARGETING surface (ADR-034 A3) admits only the
// SystemUser descriptor and is unchanged.
//
// These tests run the REAL discovery classifier (the Owner synthesis included, via ProjectLookupAttributeRows) into
// the REAL resolver, against a Dataverse fake that EVALUATES the emitted FetchXml's `eq` conditions: a row comes back
// only when the query actually names one of its values. So "the row resolved" means the query asked for it — the
// pre-fix code, which never emitted `ownerid eq {team}`, returns nothing for the team-owned row.

using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

public class MembershipResolverTeamOwnedOwnerTests
{
    private static readonly Guid CallerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OwnerTeamT = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UnrelatedTeam = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CallerBu = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecordBu = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid SomeoneElse = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly Guid TeamOwnedRow = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid UserOwnedRow = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OtherUserOwnedRow = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private const string Matter = "sprk_matter";

    /// <summary>
    /// The Owner column's role as discovery derives it (CamelCase strategy: no sprk_ prefix, no digits →
    /// <c>ownerid</c>). Both of its descriptors — SystemUser and Team — carry this one role.
    /// </summary>
    private const string OwnerRole = "ownerid";
    private const string Document = "sprk_document"; // NOT in the access-conferring registry

    // ── Attribute shapes, as RetrieveEntityRequest hands them to discovery ────────────────────────────────

    /// <summary>The #1011 minimal shape: the Owner column (synthetic targets) plus one registry Contact column.</summary>
    private static AttributeMetadata[] OwnerOnlyShape() => new[]
    {
        OwnerAttribute(),
        Lookup("sprk_assignedattorney1", "contact"),
    };

    /// <summary>Every platform ownership column of a user/team-owned table, plus a registry Contact column.</summary>
    private static AttributeMetadata[] FullPlatformShape() => new[]
    {
        OwnerAttribute(),
        Lookup("owninguser", "systemuser"),
        Lookup("owningteam", "team"),
        Lookup("owningbusinessunit", "businessunit"),
        Lookup("createdby", "systemuser"), // globally excluded
        Lookup("sprk_assignedattorney1", "contact"),
    };

    // ── Rows (what Dataverse holds) ────────────────────────────────────────────────────────────────────────

    private static Entity TeamOwned(string entity) => Row(entity, TeamOwnedRow,
        ("ownerid", new EntityReference("team", OwnerTeamT)),
        ("owningteam", new EntityReference("team", OwnerTeamT)),
        ("owningbusinessunit", new EntityReference("businessunit", RecordBu)));

    private static Entity UserOwned(string entity) => Row(entity, UserOwnedRow,
        ("ownerid", new EntityReference("systemuser", CallerId)),
        ("owninguser", new EntityReference("systemuser", CallerId)),
        ("owningbusinessunit", new EntityReference("businessunit", RecordBu)));

    private static Entity OwnedBySomeoneElse(string entity) => Row(entity, OtherUserOwnedRow,
        ("ownerid", new EntityReference("systemuser", SomeoneElse)),
        ("owninguser", new EntityReference("systemuser", SomeoneElse)),
        ("owningbusinessunit", new EntityReference("businessunit", RecordBu)));

    // ── (a) the regression: DEFAULT surface ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Default_TeamOwnedRow_CallerInTheOwningTeam_ResolvesUnderOwner()
    {
        // ownerid = team T, the caller is a member of T, the caller is NOT the owner. Pre-fix: ownerid discovered as
        // SystemUser only → the only ownerid condition is `eq {caller}` → this row is never asked for → empty.
        var harness = new Harness(Matter, OwnerOnlyShape(), Identity(OwnerTeamT), TeamOwned(Matter));

        var result = await harness.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);

        result.Ids.Should().Equal(TeamOwnedRow);
        result.ByRole[OwnerRole].Should().Equal(TeamOwnedRow);
        harness.SingleFetch.Should().Contain(Condition("ownerid", OwnerTeamT),
            "the Owner column's Team descriptor must bind the caller's team ids");
        harness.SingleFetch.Should().Contain(Condition("ownerid", CallerId),
            "the Owner column's SystemUser descriptor still binds the caller");
    }

    [Fact]
    public async Task Default_OwnerRoleFilter_FullPlatformShape_TeamOwnedRowResolvesUnderOwner()
    {
        // The realistic consumer the defect bit: a caller narrowing to role `owner` (a LookupUserMembership playbook
        // node's `roles`, or GET /api/users/me/memberships/{entity}?roles=owner). owningteam is discovered but is a
        // different role, so it is filtered out; only the Owner column can carry the team-owned row.
        var harness = new Harness(Matter, FullPlatformShape(), Identity(OwnerTeamT),
            TeamOwned(Matter), UserOwned(Matter));

        var result = await harness.Sut.ResolveAsync(
            CallerId, Matter, new MembershipResolveOptions(Roles: new[] { OwnerRole }), CancellationToken.None);

        result.Ids.Should().BeEquivalentTo(new[] { TeamOwnedRow, UserOwnedRow });
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { OwnerRole });
        result.ByRole[OwnerRole].Should().BeEquivalentTo(new[] { TeamOwnedRow, UserOwnedRow });
    }

    // ── (b) ACCESS-CONFERRING surface — registry entity AND a non-registry entity ─────────────────────────

    [Theory]
    [InlineData(Matter)]   // in the access-conferring registry
    [InlineData(Document)] // NOT in the registry — platform ownership confers structurally regardless (A1.1 / C-1)
    public async Task AccessConferringOnly_TeamOwnedRow_CallerInTheOwningTeam_ResolvesUnderOwner(string entity)
    {
        var harness = new Harness(entity, OwnerOnlyShape(), Identity(OwnerTeamT), TeamOwned(entity));

        var result = await harness.Sut.ResolveAsync(
            CallerId, entity, new MembershipResolveOptions(AccessConferringOnly: true), CancellationToken.None);

        result.Ids.Should().Equal(TeamOwnedRow);
        result.ByRole[OwnerRole].Should().Equal(TeamOwnedRow);
        harness.SingleFetch.Should().Contain(Condition("ownerid", OwnerTeamT));
    }

    [Fact]
    public async Task AccessConferringOnly_PolymorphicRegistryColumn_OnlyTheRegisteredTypeConfers_NoStaleEntryWarning()
    {
        // A registry entry names a Customer column (account + contact) as Contact-typed. Discovery now emits it as
        // Account AND Contact. Only the registered type may confer (never widened); the Account descriptor is the
        // column's other type, not a stale entry, so it is dropped without the "declares X but resolved Y" warning.
        var options = SeededOptions();
        options.AccessConferringRoles.Entities[Matter].Add(
            new AccessConferringColumn { Field = "sprk_client", IdentityType = "Contact" });
        var callerAccount = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var identity = Identity() with { AccountId = callerAccount };
        var clientRow = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
        var logger = new CapturingLogger<MembershipResolverService>();
        var harness = new Harness(Matter, new[] { CustomerAttribute("sprk_client") }, identity, cache: null,
            options: options, logger: logger,
            rows: new[] { Row(Matter, clientRow, ("sprk_client", new EntityReference("contact", CallerContactId))) });

        var result = await harness.Sut.ResolveAsync(
            CallerId, Matter, new MembershipResolveOptions(AccessConferringOnly: true), CancellationToken.None);

        result.Ids.Should().Equal(clientRow);
        harness.SingleFetch.Should().Contain(Condition("sprk_client", CallerContactId));
        harness.SingleFetch.Should().NotContain(callerAccount.ToString("D"),
            "the unregistered Account descriptor of the column must not confer");
        logger.Entries.Should().NotContain(e => e.Message.Contains("live discovery resolved", StringComparison.Ordinal),
            "the column's other identity type is not a stale registry entry");
    }

    // ── (c) a user-owned record still resolves ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserOwnedRow_StillResolvesUnderOwner_OnDefaultAndAccessConferring(bool accessConferringOnly)
    {
        var harness = new Harness(Matter, OwnerOnlyShape(), Identity(OwnerTeamT),
            UserOwned(Matter), OwnedBySomeoneElse(Matter));

        var result = await harness.Sut.ResolveAsync(
            CallerId, Matter, new MembershipResolveOptions(AccessConferringOnly: accessConferringOnly), CancellationToken.None);

        result.Ids.Should().Equal(UserOwnedRow);
        result.ByRole[OwnerRole].Should().Equal(UserOwnedRow);
    }

    // ── (d) PEOPLE-TARGETING surface unchanged (ADR-034 A3) ───────────────────────────────────────────────

    [Fact]
    public async Task People_TeamOwnedRowTheCallersTeamOwns_IsNotReturned_UserOwnedRowUnchanged()
    {
        var harness = new Harness(Matter, FullPlatformShape(), Identity(OwnerTeamT),
            TeamOwned(Matter), UserOwned(Matter));

        var result = await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        result.Ids.Should().Equal(UserOwnedRow);
        result.ByRole[OwnerRole].Should().Equal(UserOwnedRow);

        // The query itself: ownerid is bound to the caller ONCE and never to a team; no team id appears anywhere.
        var fetch = harness.SingleFetch;
        CountOccurrences(fetch, "attribute='ownerid' operator=").Should().Be(1,
            "on the people surface only the SystemUser descriptor of ownerid is admitted");
        fetch.Should().Contain(Condition("ownerid", CallerId));
        fetch.Should().NotContain(OwnerTeamT.ToString("D"),
            "a team id is never a selection value on the people surface — that is the BU-wide fan-out A3 closed");
        fetch.Should().NotContain("'owningteam'");
    }

    // ── (e) a caller in no owning team gets nothing from ownerid ──────────────────────────────────────────

    [Fact]
    public async Task Default_CallerInNoOwningTeam_TeamOwnedRowIsNotReturned()
    {
        var harness = new Harness(Matter, OwnerOnlyShape(), Identity(UnrelatedTeam), TeamOwned(Matter));

        var result = await harness.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);

        result.Ids.Should().BeEmpty();
        harness.SingleFetch.Should().NotContain(OwnerTeamT.ToString("D"));
        harness.SingleFetch.Should().Contain(Condition("ownerid", UnrelatedTeam),
            "the caller's own teams are bound — just not the one that owns this row");
    }

    [Fact]
    public async Task Default_CallerWithNoTeams_OwnerColumnBindsOnlyTheCaller()
    {
        // Also the fail-closed shape of a failed teammembership read (IdentityNormalizationService fails soft to an
        // empty TeamIds list): team-owned rows look like "no access", never like someone else's access.
        var harness = new Harness(Matter, OwnerOnlyShape(), Identity(), TeamOwned(Matter));

        var result = await harness.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);

        result.Ids.Should().BeEmpty();
        CountOccurrences(harness.SingleFetch, "attribute='ownerid' operator=").Should().Be(1);
        harness.SingleFetch.Should().Contain(Condition("ownerid", CallerId));
    }

    // ── Determinism (FR-14) ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmittedFetchXml_IsDeterministic_SystemUserLegBeforeTeamLegs()
    {
        var first = new Harness(Matter, FullPlatformShape(), Identity(OwnerTeamT, UnrelatedTeam));
        var second = new Harness(Matter, FullPlatformShape(), Identity(OwnerTeamT, UnrelatedTeam));

        await first.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);
        await second.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);

        first.SingleFetch.Should().Be(second.SingleFetch);
        var fetch = first.SingleFetch;
        fetch.IndexOf(Condition("ownerid", CallerId), StringComparison.Ordinal)
            .Should().BeLessThan(fetch.IndexOf(Condition("ownerid", OwnerTeamT), StringComparison.Ordinal));
        fetch.IndexOf(Condition("ownerid", OwnerTeamT), StringComparison.Ordinal)
            .Should().BeLessThan(fetch.IndexOf(Condition("ownerid", UnrelatedTeam), StringComparison.Ordinal));
        CountOccurrences(fetch, "<attribute name='ownerid' />").Should().Be(1,
            "two descriptors for one field project the column once");
    }

    // ── AC4: a pre-change cached answer is not served after deploy ─────────────────────────────────────────

    [Fact]
    public async Task DiscoveryCache_PreChangeSingleDescriptorEntry_IsNotServed()
    {
        var cache = new VersionedCache();
        // What a pre-change process wrote: ownerid as ONE SystemUser descriptor, under the v1 schema.
        cache.Seed("anonymous", MembershipFieldDiscoveryService.CacheResource, Matter, version: 1,
            new DiscoveryResult(Matter, DateTimeOffset.UtcNow,
                new[] { new MembershipDescriptor("ownerid", OwnerRole, "SystemUser", "systemuser", "auto") },
                Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));
        var discovery = new CannedDiscovery(SeededOptions(), OwnerOnlyShape(), cache);

        var result = await discovery.DiscoverAsync(Matter, CancellationToken.None);

        discovery.FetchCount.Should().Be(1, "the v1 entry must be unreachable, forcing a live metadata read");
        result.DiscoveredFields.Where(d => d.Field == "ownerid").Select(d => d.IdentityType)
            .Should().Equal("SystemUser", "Team");
    }

    [Fact]
    public async Task ResolverCache_PreChangeEntryUnderTheSameId_IsNotServed()
    {
        // Capture the id this composition caches under, then seed that id with a pre-change (v5) EMPTY answer — the
        // answer the old code would have cached for this caller. The new code must not serve it.
        var probe = new VersionedCache();
        var probeHarness = new Harness(Matter, OwnerOnlyShape(), Identity(OwnerTeamT), cache: probe, rows: new[] { TeamOwned(Matter) });
        await probeHarness.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);
        var (_, id, version) = probe.Writes.Single(w => w.Resource == MembershipResolverService.CacheResource);
        version.Should().BeGreaterThan(5);

        var stale = new VersionedCache();
        stale.Seed("anonymous", MembershipResolverService.CacheResource, id, version: 5,
            new MembershipResponse(
                EntityType: Matter,
                PersonIdentity: Identity(OwnerTeamT),
                Ids: Array.Empty<Guid>(),
                ByRole: new Dictionary<string, IReadOnlyList<Guid>> { [OwnerRole] = Array.Empty<Guid>() },
                Count: 0,
                CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(2),
                ContinuationToken: null,
                RelatedByRole: null));
        var harness = new Harness(Matter, OwnerOnlyShape(), Identity(OwnerTeamT), cache: stale, rows: new[] { TeamOwned(Matter) });

        var result = await harness.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);

        result.Ids.Should().Equal(TeamOwnedRow);
    }

    // ── (f) (g) the classifier: single-target lookups and Customer columns ─────────────────────────────────

    [Fact]
    public async Task Discovery_OwnerColumn_YieldsSystemUserThenTeam_SameFieldRoleAndSource()
    {
        var discovery = new CannedDiscovery(SeededOptions(), new[] { OwnerAttribute() });

        var result = await discovery.DiscoverAsync(Matter, CancellationToken.None);

        result.DiscoveredFields.Should().HaveCount(2);
        result.DiscoveredFields.Select(d => (d.Field, d.Role, d.IdentityType, d.TargetTable, d.Source)).Should().Equal(
            ("ownerid", OwnerRole, "SystemUser", "systemuser", "auto"),
            ("ownerid", OwnerRole, "Team", "team", "auto"));
    }

    [Fact]
    public async Task Discovery_SingleTargetMakerLookups_ClassifyExactlyAsBefore()
    {
        var discovery = new CannedDiscovery(SeededOptions(), new AttributeMetadata[]
        {
            Lookup("sprk_reviewer", "systemuser"),
            Lookup("sprk_responsibleteam", "team"),
            Lookup("sprk_assignedattorney1", "contact"),
        });

        var result = await discovery.DiscoverAsync(Matter, CancellationToken.None);

        result.DiscoveredFields.Select(d => (d.Field, d.Role, d.IdentityType, d.TargetTable, d.Source)).Should().Equal(
            ("sprk_assignedattorney1", "assignedattorney", "Contact", "contact", "auto"),
            ("sprk_responsibleteam", "responsibleteam", "Team", "team", "auto"),
            ("sprk_reviewer", "reviewer", "SystemUser", "systemuser", "auto"));
    }

    [Fact]
    public async Task Discovery_CustomerColumn_OnlyContactConfigured_ClassifiesUnchanged()
    {
        var options = SeededOptions();
        options.IncludedIdentityTables.RemoveAll(t => t.Table == "account");
        var discovery = new CannedDiscovery(options, new[] { CustomerAttribute("sprk_client") });

        var result = await discovery.DiscoverAsync(Matter, CancellationToken.None);

        result.DiscoveredFields.Select(d => (d.Field, d.IdentityType, d.TargetTable)).Should().Equal(
            ("sprk_client", "Contact", "contact"));
    }

    [Fact]
    public async Task Discovery_CustomerColumn_AccountAndContactConfigured_YieldsBothInTargetOrder()
    {
        var discovery = new CannedDiscovery(SeededOptions(), new[] { CustomerAttribute("sprk_client") });

        var result = await discovery.DiscoverAsync(Matter, CancellationToken.None);

        result.DiscoveredFields.Select(d => (d.Field, d.IdentityType, d.TargetTable)).Should().Equal(
            ("sprk_client", "Account", "account"),
            ("sprk_client", "Contact", "contact"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────

    private static PersonIdentity Identity(params Guid[] teams) => new(
        SystemUserId: CallerId,
        ContactId: CallerContactId,
        PrimaryEmail: "member@spaarke.dev",
        TeamIds: teams,
        BusinessUnitId: CallerBu);

    private static MembershipOptions SeededOptions()
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(name: null, options);
        return options;
    }

    private static string Condition(string field, Guid value)
        => $"<condition attribute='{field}' operator='eq' value='{value:D}' />";

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static Entity Row(string entity, Guid id, params (string Attr, object Value)[] attributes)
    {
        var row = new Entity(entity) { Id = id };
        foreach (var (attr, value) in attributes)
        {
            row[attr] = value;
        }
        return row;
    }

    private static LookupAttributeMetadata Lookup(string logicalName, params string[] targets)
        => new() { LogicalName = logicalName, Targets = targets };

    private static AttributeMetadata OwnerAttribute()
    {
        var attr = BaseAttribute(AttributeTypeCode.Owner);
        attr.LogicalName = "ownerid";
        return attr;
    }

    private static AttributeMetadata CustomerAttribute(string logicalName)
    {
        var attr = BaseAttribute(AttributeTypeCode.Customer);
        attr.LogicalName = logicalName;
        return attr;
    }

    /// <summary>
    /// A base <see cref="AttributeMetadata"/> of the given type — how the SDK materializes an Owner/Customer column
    /// (no distinct subclass; see MembershipFieldDiscoveryService.ProjectLookupAttributeRows). The constructor is
    /// protected, so reflection, exactly as MembershipFieldDiscoveryServiceTests does.
    /// </summary>
    private static AttributeMetadata BaseAttribute(AttributeTypeCode typeCode)
    {
        var ctor = typeof(AttributeMetadata).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, binder: null,
                types: new[] { typeof(AttributeTypeCode) }, modifiers: null)
            ?? throw new InvalidOperationException("AttributeMetadata(AttributeTypeCode) constructor not found.");
        return (AttributeMetadata)ctor.Invoke(new object[] { typeCode });
    }

    /// <summary>The REAL discovery service, with only the metadata read replaced by the canned attribute list.</summary>
    private sealed class CannedDiscovery : MembershipFieldDiscoveryService
    {
        private readonly IReadOnlyList<LookupAttributeRow> _rows;

        public CannedDiscovery(MembershipOptions options, IEnumerable<AttributeMetadata> attributes, ITenantCache? cache = null)
            : base(new Mock<IDataverseService>().Object, cache ?? new VersionedCache(), Options.Create(options),
                NullLogger<MembershipFieldDiscoveryService>.Instance)
        {
            _rows = ProjectLookupAttributeRows(attributes);
        }

        public int FetchCount { get; private set; }

        protected override Task<IReadOnlyList<LookupAttributeRow>> FetchLookupAttributesAsync(
            string entityLogicalName, CancellationToken ct)
        {
            FetchCount++;
            return Task.FromResult(_rows);
        }
    }

    /// <summary>Real discovery → real resolver → a Dataverse fake that evaluates the emitted `eq` conditions.</summary>
    private sealed class Harness
    {
        public Harness(string entity, AttributeMetadata[] shape, PersonIdentity identity, params Entity[] rows)
            : this(entity, shape, identity, cache: null, rows: rows)
        {
        }

        public Harness(
            string entity,
            AttributeMetadata[] shape,
            PersonIdentity identity,
            ITenantCache? cache,
            MembershipOptions? options = null,
            ILogger<MembershipResolverService>? logger = null,
            params Entity[] rows)
        {
            options ??= SeededOptions();
            var discovery = new CannedDiscovery(options, shape);

            var identityService = new Mock<IIdentityNormalizationService>();
            identityService.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(identity);

            var dataverse = new Mock<IDataverseService>();
            // The people surface's human/application-user check: a human caller (applicationid absent).
            dataverse
                .Setup(x => x.RetrieveAsync("systemuser", CallerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("systemuser") { Id = CallerId });
            dataverse
                .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FetchExpression fe, CancellationToken _) =>
                {
                    Fetches.Add(fe.Query);
                    return new EntityCollection(rows.Where(r => Matches(fe.Query, r)).ToList());
                });

            Sut = new MembershipResolverService(
                discovery,
                identityService.Object,
                dataverse.Object,
                cache ?? new VersionedCache(),
                Options.Create(options),
                logger ?? NullLogger<MembershipResolverService>.Instance);
        }

        public MembershipResolverService Sut { get; }

        public List<string> Fetches { get; } = new();

        public string SingleFetch => Fetches.Should().ContainSingle().Subject;

        /// <summary>
        /// The resolver's query is one entity, one OR filter of `eq` conditions on lookup columns. A row matches when
        /// it is of that entity and ANY condition names the id its column holds.
        /// </summary>
        private static bool Matches(string fetchXml, Entity row)
        {
            var doc = XDocument.Parse(fetchXml);
            var entity = doc.Root!.Element("entity")!;
            if (!string.Equals((string?)entity.Attribute("name"), row.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var condition in entity.Descendants("condition"))
            {
                var attribute = (string?)condition.Attribute("attribute");
                if ((string?)condition.Attribute("operator") != "eq"
                    || attribute is null
                    || !Guid.TryParse((string?)condition.Attribute("value"), out var value)
                    || !row.Contains(attribute))
                {
                    continue;
                }

                if (row[attribute] is EntityReference reference && reference.Id == value)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>In-memory <see cref="ITenantCache"/> keyed by tenant + resource + id + VERSION, so a version bump is observable.</summary>
    private sealed class VersionedCache : ITenantCache
    {
        private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

        public List<(string Resource, string Id, int Version)> Writes { get; } = new();

        public void Seed<T>(string tenantId, string resource, string id, int version, T value)
            => _store[Key(tenantId, resource, id, version)] = value;

        private static string Key(string tenantId, string resource, string id, int version)
            => $"tenant:{tenantId}:{resource}:{id}:v{version}";

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(Key(tenantId, resource, id, version), out var v) ? (T?)v : default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Writes.Add((resource, id, version));
            _store[Key(tenantId, resource, id, version)] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            _store.Remove(Key(tenantId, resource, id, version));
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
            if (existing is not null)
            {
                return existing;
            }
            var produced = await factory(ct);
            await SetAsync(tenantId, resource, id, version, produced, ttl, cacheInstance, ct);
            return produced;
        }
    }
}
