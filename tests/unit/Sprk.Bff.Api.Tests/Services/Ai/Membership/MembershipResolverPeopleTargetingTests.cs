// unified-access-control-r2 task 152 — ADR-034 Amendment A3: the PEOPLE-TARGETING consumption surface.
//
// The contract (owner decisions round 2 item 9 + Q8, round 3 D1): a record is FOR a person when that person
// CREATED it (a HUMAN createdby), is NAMED in a registry Contact-typed "Assigned *" column (through the caller's
// LINKED contact, task 141), or personally OWNS it. Team / business-unit / organization ownership and every
// group-typed descriptor select NOTHING — a team-owned record never fans out to the team's members.
//
// Every assertion that matters here is on the EMITTED FetchXML: that is what Dataverse evaluates, so a descriptor
// that "was filtered" but still reaches the query is the defect. Existing MembershipResolverServiceTests (default
// and AccessConferringOnly surfaces) are untouched — criterion 2 is that they pass unedited.

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

public class MembershipResolverPeopleTargetingTests
{
    private static readonly Guid CallerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BuDefaultTeamId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherTeamId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BusinessUnitId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AccountId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OrganizationId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private const string CallerEmail = "ada.member@spaarke.dev";

    private static readonly Guid CreatedRow = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OwnedRow = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid AssignedRow = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private const string Matter = "sprk_matter";

    /// <summary>Every descriptor shape discovery can hand the resolver for a matter, person and group alike.</summary>
    private static readonly MembershipDescriptor[] AllShapes =
    {
        D("ownerid", "owner", "SystemUser"),
        D("owninguser", "owningUser", "SystemUser"),
        D("owningteam", "owningTeam", "Team"),
        D("owningbusinessunit", "owningBusinessUnit", "BusinessUnit"),
        D("sprk_assignedattorney1", "assignedAttorney", "Contact"),
        D("sprk_assignedtointernal", "assignedToInternal", "Contact"),
        D("sprk_assignedlawfirm1", "assignedLawFirm", "Organization"),
        D("sprk_billingaccount", "billingAccount", "Account"),
        D("sprk_opposingcounsel", "opposingCounsel", "Contact"),     // not in the registry
        D("sprk_reviewer", "reviewer", "SystemUser"),                // maker-authored systemuser lookup
        D("sprk_responsibleteam", "responsibleTeam", "Team"),        // maker-authored team lookup
    };

    // ── Criterion 1: person terms bind; group terms emit zero conditions ──────────────────────────────

    [Fact]
    public async Task People_EmitsConditionsOnlyForPersonTerms_GroupTypedDescriptorsEmitNothing()
    {
        var harness = new Harness(AllShapes, FullIdentity(), human: true);

        await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        var fetch = harness.CapturedFetch.Should().ContainSingle().Subject;

        // Person terms — bound to the caller and the caller's linked contact.
        fetch.Should().Contain(Condition("ownerid", CallerId));
        fetch.Should().Contain(Condition("owninguser", CallerId));
        fetch.Should().Contain(Condition("createdby", CallerId));
        fetch.Should().Contain(Condition("sprk_assignedattorney1", CallerContactId));
        fetch.Should().Contain(Condition("sprk_assignedtointernal", CallerContactId));

        // Group terms — no condition, no projected attribute, no value anywhere in the query.
        foreach (var excluded in new[] { "owningteam", "owningbusinessunit", "sprk_assignedlawfirm1",
                     "sprk_billingaccount", "sprk_opposingcounsel", "sprk_reviewer", "sprk_responsibleteam" })
        {
            fetch.Should().NotContain($"'{excluded}'", $"{excluded} is not a person term on the people surface");
        }

        foreach (var groupValue in new[] { BuDefaultTeamId, OtherTeamId, BusinessUnitId, AccountId, OrganizationId })
        {
            fetch.Should().NotContain(groupValue.ToString("D"),
                "a team, BU, account or organization id must never be a selection value — that is the BU-wide fan-out");
        }
    }

    [Fact]
    public async Task People_ReturnsRowsMatchedByCreatedByOwnerAndAssigned_UnderTheirRoles()
    {
        var harness = new Harness(AllShapes, FullIdentity(), human: true,
            Row(CreatedRow, ("createdby", new EntityReference("systemuser", CallerId))),
            Row(OwnedRow, ("ownerid", new EntityReference("systemuser", CallerId))),
            Row(AssignedRow, ("sprk_assignedattorney1", new EntityReference("contact", CallerContactId))));

        var result = await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        result.Ids.Should().BeEquivalentTo(new[] { CreatedRow, OwnedRow, AssignedRow });
        result.ByRole["createdBy"].Should().Equal(CreatedRow);
        result.ByRole["owner"].Should().Equal(OwnedRow);
        result.ByRole["assignedAttorney"].Should().Equal(AssignedRow);
        result.ByRole.Should().NotContainKeys("owningTeam", "owningBusinessUnit", "assignedLawFirm");
    }

    [Fact]
    public async Task People_TeamOwnedRowWhoseOnlyLinkIsTheBuDefaultTeam_IsNotSelected()
    {
        // The row Dataverse would return for an owningteam condition. The people query must not ASK for it: the
        // fake answers only when the query actually names the team, so a returned row would mean the team was bound.
        var teamOwnedRow = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000ff");
        var harness = new Harness(AllShapes, FullIdentity(), human: true);
        harness.AnswerWhenQueryContains(BuDefaultTeamId, Row(teamOwnedRow, ("owningteam", new EntityReference("team", BuDefaultTeamId))));

        var result = await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        result.Ids.Should().NotContain(teamOwnedRow,
            "a record owned by the caller's BU default team is an ACCESS fact, not an attention fact");
    }

    // ── Criterion 7 at the resolver (verifier round 1 item 9): sprk_todo, whose registry column is sprk_assignedto ──

    private const string Todo = "sprk_todo";
    private static readonly Guid OtherMemberId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid OtherMemberContactId = Guid.Parse("99999999-0000-0000-0000-000000000099");

    /// <summary>The descriptor shapes discovery hands the resolver for a to-do.</summary>
    private static readonly MembershipDescriptor[] TodoShapes =
    {
        D("ownerid", "owner", "SystemUser"),
        D("owninguser", "owningUser", "SystemUser"),
        D("owningteam", "owningTeam", "Team"),
        D("owningbusinessunit", "owningBusinessUnit", "BusinessUnit"),
        D("sprk_assignedto", "assignedTo", "Contact"),
    };

    [Fact]
    public async Task People_Todo_TeamOwnedToDo_IsForItsHumanCreatorAndItsAssignee_NotForAnotherTeamMember()
    {
        var createdByCaller = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var assignedToCaller = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var teamOwnedOnly = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");
        var team = new EntityReference("team", BuDefaultTeamId);

        // Dataverse stand-in: each team-owned to-do comes back only when the query ASKS for the term that names it.
        Harness Build(Guid caller, PersonIdentity identity)
        {
            var h = new Harness(TodoShapes, identity, human: true, cache: null, logger: null, entity: Todo, callerId: caller);
            h.AnswerWhenQueryContains(CallerId, TodoRow(createdByCaller, ("createdby", new EntityReference("systemuser", CallerId)), ("ownerid", team)));
            h.AnswerWhenQueryContains(CallerContactId, TodoRow(assignedToCaller, ("sprk_assignedto", new EntityReference("contact", CallerContactId)), ("ownerid", team)));
            h.AnswerWhenQueryContains(BuDefaultTeamId, TodoRow(teamOwnedOnly, ("ownerid", team)));
            return h;
        }

        // The creator / assignee (the same person here): both to-dos, never the one only the team links.
        var forCaller = Build(CallerId, FullIdentity());
        var callerResult = await forCaller.Sut.ResolveAsync(CallerId, Todo, MembershipResolveOptions.People, CancellationToken.None);

        var fetch = forCaller.CapturedFetch.Should().ContainSingle().Subject;
        fetch.Should().Contain(Condition("createdby", CallerId));
        fetch.Should().Contain(Condition("sprk_assignedto", CallerContactId), "sprk_todo's registry Contact column binds the linked contact");
        fetch.Should().Contain(Condition("ownerid", CallerId));
        fetch.Should().NotContain("'owningteam'").And.NotContain(BuDefaultTeamId.ToString("D"));
        callerResult.Ids.Should().BeEquivalentTo(new[] { createdByCaller, assignedToCaller });
        callerResult.ByRole["assignedTo"].Should().Equal(assignedToCaller);

        // Another member of the SAME BU default team, named on neither to-do: nothing.
        var otherIdentity = new PersonIdentity(
            SystemUserId: OtherMemberId, ContactId: OtherMemberContactId, TeamIds: new[] { BuDefaultTeamId },
            BusinessUnitId: BusinessUnitId);
        var forOther = Build(OtherMemberId, otherIdentity);
        var otherResult = await forOther.Sut.ResolveAsync(OtherMemberId, Todo, MembershipResolveOptions.People, CancellationToken.None);

        otherResult.Ids.Should().BeEmpty("a team-owned to-do never fans out to the team's members");
        forOther.CapturedFetch.Single().Should().NotContain(BuDefaultTeamId.ToString("D"));
    }

    // ── Task 147 r1 (owner round 28 item 1): "createdbyperson, else createdby" ───────────────────────────────────────

    private static readonly Guid BffApplicationUser = Guid.Parse("cccccccc-0000-0000-0000-0000000000a1");

    /// <summary>The to-do shapes once the child schema step has put the creator-person column on the table.</summary>
    private static readonly MembershipDescriptor[] TodoShapesWithCreatorPerson =
        TodoShapes.Append(D("sprk_createdbyperson", "createdByPerson", "SystemUser")).ToArray();

    [Fact]
    public async Task People_Todo_AnAppCreatedToDo_IsForThePersonWhoAskedForIt_NotForAnotherTeamMember()
    {
        // A to-do the browser created through the BFF (G5): createdby is the APPLICATION, sprk_createdbyperson the caller,
        // owned by the team. It must stay in its creator's briefing — and only theirs.
        var appCreated = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000a1");
        var team = new EntityReference("team", BuDefaultTeamId);

        Harness Build(Guid caller, PersonIdentity identity)
        {
            var h = new Harness(TodoShapesWithCreatorPerson, identity, human: true, cache: null, logger: null, entity: Todo, callerId: caller);
            h.AnswerWhenQueryContains(CallerId, TodoRow(appCreated,
                ("createdby", new EntityReference("systemuser", BffApplicationUser)),
                ("sprk_createdbyperson", new EntityReference("systemuser", CallerId)),
                ("ownerid", team)));
            return h;
        }

        var forCaller = Build(CallerId, FullIdentity());
        var callerResult = await forCaller.Sut.ResolveAsync(CallerId, Todo, MembershipResolveOptions.People, CancellationToken.None);

        forCaller.CapturedFetch.Should().ContainSingle().Which.Should().Contain(Condition("sprk_createdbyperson", CallerId));
        callerResult.Ids.Should().Equal(appCreated);
        callerResult.ByRole["createdBy"].Should().BeEquivalentTo(new[] { appCreated }, "the person who asked is its creator on this surface");

        var otherIdentity = new PersonIdentity(
            SystemUserId: OtherMemberId, ContactId: OtherMemberContactId, TeamIds: new[] { BuDefaultTeamId },
            BusinessUnitId: BusinessUnitId);
        var forOther = Build(OtherMemberId, otherIdentity);
        (await forOther.Sut.ResolveAsync(OtherMemberId, Todo, MembershipResolveOptions.People, CancellationToken.None))
            .Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task People_ACreatorPersonTerm_IsNeverEmitted_WhereTheTableHasNoSuchColumn()
    {
        // Before the schema step (or on a table without the column) discovery does not find it, so the query never names a
        // column the table lacks — that would fail the whole briefing query.
        var harness = new Harness(TodoShapes, FullIdentity(), human: true, cache: null, logger: null, entity: Todo, callerId: CallerId);

        await harness.Sut.ResolveAsync(CallerId, Todo, MembershipResolveOptions.People, CancellationToken.None);

        harness.CapturedFetch.Should().ContainSingle().Which.Should().NotContain("sprk_createdbyperson");
    }

    [Fact]
    public async Task People_ForAnApplicationUser_NeitherCreatorTermBinds()
    {
        var harness = new Harness(TodoShapesWithCreatorPerson, FullIdentity(), human: false, cache: null, logger: null, entity: Todo, callerId: CallerId);

        await harness.Sut.ResolveAsync(CallerId, Todo, MembershipResolveOptions.People, CancellationToken.None);

        var fetch = harness.CapturedFetch.Should().ContainSingle().Subject;
        fetch.Should().NotContain("'sprk_createdbyperson'").And.NotContain("'createdby'");
    }

    private static Entity TodoRow(Guid id, params (string Attr, object Value)[] attributes)
    {
        var entity = new Entity(Todo) { Id = id };
        foreach (var (attr, value) in attributes)
        {
            entity[attr] = value;
        }
        return entity;
    }

    // ── Criterion 3: the cache key separates the surfaces ──────────────────────────────────────────────

    [Fact]
    public async Task People_AndDefault_NeverShareACacheEntry_InEitherOrder()
    {
        var cache = new RecordingCache();
        var harness = new Harness(AllShapes, FullIdentity(), human: true, cache: cache);

        // Dangerous order for the briefing: the AI-scoping call lands first and caches the team/BU-wide set.
        var scoping = await harness.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);
        var people = await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        harness.CapturedFetch.Should().HaveCount(2, "the people call must resolve its own answer, not hit the scoping entry");
        scoping.ByRole.Should().ContainKey("owningTeam");
        people.ByRole.Should().NotContainKey("owningTeam");

        cache.SetIds.Should().HaveCount(2);
        cache.SetIds[0].Should().NotBe(cache.SetIds[1]);
    }

    // ── Criterion 2 (cache half): every pre-existing caller keeps its exact key ─────────────────────────

    [Fact]
    public async Task Default_CacheKey_IsByteIdenticalToThePreTaskComposition()
    {
        var cache = new RecordingCache();
        var harness = new Harness(AllShapes, FullIdentity(), human: true, cache: cache);

        await harness.Sut.ResolveAsync(CallerId, Matter, options: null, CancellationToken.None);
        await harness.Sut.ResolveAsync(CallerId, Matter, new MembershipResolveOptions(AccessConferringOnly: true), CancellationToken.None);

        // The composition MembershipResolverService.HashOptions used before task 152, reproduced literally.
        cache.SetIds.Should().Equal(
            $"{CallerId:D}:{Matter}:{PreTaskHash("r:*|i:*|x:*|l:500|c:|a:0|o:*")}",
            $"{CallerId:D}:{Matter}:{PreTaskHash("r:*|i:*|x:*|l:500|c:|a:1|o:*")}");
    }

    // ── Criterion 4: no linked contact ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task People_NoLinkedContact_AssignedBindsNothing_CreatedByAndOwnerStill_NoEmailPath()
    {
        var logger = new CapturingLogger<MembershipResolverService>();
        var identity = FullIdentity() with { ContactId = null };
        var harness = new Harness(AllShapes, identity, human: true, logger: logger);

        await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        var fetch = harness.CapturedFetch.Should().ContainSingle().Subject;
        fetch.Should().Contain(Condition("createdby", CallerId));
        fetch.Should().Contain(Condition("ownerid", CallerId));
        fetch.Should().NotContain("sprk_assignedattorney1' operator", "with no linked contact the Assigned term binds nothing");
        fetch.Should().NotContain(CallerEmail, "there is no email fallback (C7 hijack path)");
        fetch.Should().NotContain("like", "no substring/email matching of any kind");

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("people_targeting_no_linked_contact"));

        // The only Dataverse reads are the human check and the one membership query — no contact-by-email lookup.
        harness.Dataverse.Verify(x => x.RetrieveAsync("systemuser", CallerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Dataverse.Verify(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Dataverse.VerifyNoOtherCalls();
    }

    // ── Criterion 5: application user ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task People_ApplicationUserCaller_CreatedByEmitsNoCondition_AndWarns()
    {
        var logger = new CapturingLogger<MembershipResolverService>();
        var harness = new Harness(AllShapes, FullIdentity(), human: false, logger: logger);

        await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        var fetch = harness.CapturedFetch.Should().ContainSingle().Subject;
        fetch.Should().NotContain("'createdby'", "the BFF application user must never receive the set of everything it created");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("people_targeting_createdby_skipped")
            && e.Message.Contains("application_user"));
    }

    [Fact]
    public async Task People_SystemUserRowUnreadable_CreatedByTreatedAsNotAPerson()
    {
        var logger = new CapturingLogger<MembershipResolverService>();
        var harness = new Harness(AllShapes, FullIdentity(), human: true, logger: logger);
        harness.Dataverse
            .Setup(x => x.RetrieveAsync("systemuser", CallerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));

        await harness.Sut.ResolveAsync(CallerId, Matter, MembershipResolveOptions.People, CancellationToken.None);

        harness.CapturedFetch.Single().Should().NotContain("'createdby'", "unknown is never treated as human");
        logger.Entries.Should().Contain(e => e.Message.Contains("systemuser_unreadable"));
    }

    // ── Criterion 6: invalid combination ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task People_WithAccessConferringOnly_ThrowsBeforeAnyIo()
    {
        var harness = new Harness(AllShapes, FullIdentity(), human: true);

        var act = () => harness.Sut.ResolveAsync(
            CallerId, Matter, new MembershipResolveOptions(AccessConferringOnly: true, PeopleTargeting: true),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*mutually exclusive*");
        harness.Dataverse.VerifyNoOtherCalls();
        harness.Discovery.Verify(d => d.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string Condition(string field, Guid value) =>
        $"<condition attribute='{field}' operator='eq' value='{value:D}' />";

    private static string PreTaskHash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)), 0, 8).ToLowerInvariant();

    private static MembershipDescriptor D(string field, string role, string identityType) =>
        new(field, role, identityType, identityType.ToLowerInvariant(), "auto");

    private static PersonIdentity FullIdentity() => new(
        SystemUserId: CallerId,
        ContactId: CallerContactId,
        PrimaryEmail: CallerEmail,
        TeamIds: new[] { BuDefaultTeamId, OtherTeamId },
        BusinessUnitId: BusinessUnitId,
        AccountId: AccountId,
        OrganizationIds: new[] { OrganizationId });

    private static Entity Row(Guid id, params (string Attr, object Value)[] attributes)
    {
        var entity = new Entity(Matter) { Id = id };
        foreach (var (attr, value) in attributes)
        {
            entity[attr] = value;
        }
        return entity;
    }

    private sealed class Harness
    {
        private readonly List<(Guid Needle, Entity Row)> _conditional = new();

        public Harness(
            MembershipDescriptor[] descriptors,
            PersonIdentity identity,
            bool human,
            params Entity[] rows)
            : this(descriptors, identity, human, cache: null, logger: null, entity: Matter, callerId: null, rows)
        {
        }

        public Harness(
            MembershipDescriptor[] descriptors,
            PersonIdentity identity,
            bool human,
            ITenantCache? cache = null,
            ILogger<MembershipResolverService>? logger = null,
            string entity = Matter,
            Guid? callerId = null,
            params Entity[] rows)
        {
            var caller = callerId ?? CallerId;
            Discovery = new Mock<IMembershipFieldDiscoveryService>();
            Discovery
                .Setup(d => d.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DiscoveryResult(entity, DateTimeOffset.UtcNow, descriptors,
                    Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));

            var identityMock = new Mock<IIdentityNormalizationService>();
            identityMock.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(identity);

            Dataverse = new Mock<IDataverseService>();
            var systemUser = new Entity("systemuser") { Id = caller };
            if (!human)
            {
                systemUser["applicationid"] = Guid.Parse("99999999-9999-9999-9999-999999999999");
            }
            Dataverse
                .Setup(x => x.RetrieveAsync("systemuser", caller, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(systemUser);
            Dataverse
                .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FetchExpression fe, CancellationToken _) =>
                {
                    CapturedFetch.Add(fe.Query);
                    var answer = rows.ToList();
                    answer.AddRange(_conditional
                        .Where(c => fe.Query.Contains(c.Needle.ToString("D"), StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Row));
                    return new EntityCollection(answer);
                });

            var options = new MembershipOptions();
            new MembershipOptionsDefaults().PostConfigure(name: null, options);

            Sut = new MembershipResolverService(
                Discovery.Object,
                identityMock.Object,
                Dataverse.Object,
                cache ?? new RecordingCache(),
                Options.Create(options),
                logger ?? NullLogger<MembershipResolverService>.Instance);
        }

        public MembershipResolverService Sut { get; }
        public Mock<IDataverseService> Dataverse { get; }
        public Mock<IMembershipFieldDiscoveryService> Discovery { get; }
        public List<string> CapturedFetch { get; } = new();

        public void AnswerWhenQueryContains(Guid needle, Entity row) => _conditional.Add((needle, row));
    }

    /// <summary>In-memory <see cref="ITenantCache"/> recording every id it is asked to store.</summary>
    private sealed class RecordingCache : ITenantCache
    {
        private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);
        public List<string> SetIds { get; } = new();

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue($"{resource}:{id}", out var v) ? (T?)v : default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            SetIds.Add(id);
            _store[$"{resource}:{id}"] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            _store.Remove($"{resource}:{id}");
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
