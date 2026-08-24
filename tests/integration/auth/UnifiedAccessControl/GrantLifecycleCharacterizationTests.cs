using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The external grant write path — finding A-11 (spec FR-09), ranked #1 of 13 in
/// notes/investigation/10-finding-confirmations.md, closed by task 010.
///
/// <para><b>What was wrong.</b> <c>/grant</c> unconditionally CREATEd a row with no pre-existence check,
/// while <c>/revoke</c> deactivated exactly ONE row by <c>AccessRecordId</c>. Two identical grants
/// therefore produced two active rows, and revoking one left the other active — access survived
/// revocation. The read path hid it: <c>QueryGrantSetAsync</c> collapses duplicates with
/// <c>GroupBy(root).Max(level)</c> and never returns access-record ids, so no effective-access view could
/// reveal that N active rows backed one logical grant.</para>
///
/// <para>The seam is <see cref="DataverseWebApiClient"/>, whose methods are <c>virtual</c> — the module
/// boundary ADR-038 §4 designates for substitution. No <c>Mock&lt;HttpMessageHandler&gt;</c> (ban B1),
/// no reflection into privates (ban B8 — the handler is <c>internal</c> per <c>InternalsVisibleTo</c>).</para>
/// </summary>
public class GrantLifecycleCharacterizationTests
{
    private const string GrantEntitySet = "sprk_externalrecordaccesses";

    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Config sufficient for the real <see cref="DataverseWebApiClient"/> constructor (Moq invokes it).
    /// ClientSecretCredential is constructed but never used — every method the code under test calls is
    /// overridden, so no token is requested and no network call occurs.
    /// </summary>
    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

    // ─────────────────────────────────────────────────────────────────────────────
    // An in-memory sprk_externalrecordaccess table behind the real client's seam.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A faithful-enough stand-in for the grant table: it stores rows, answers
    /// <c>QueryAsync</c> by interpreting the production <c>$filter</c>, and applies
    /// <c>UpdateAsync</c> payloads.
    ///
    /// <para><b>Why it interprets the real filter rather than being told the answer.</b> The whole defect
    /// class here is grant and revoke disagreeing about what "the same grant" means. A fake that returned
    /// canned rows would pass even if the filter were wrong — for instance if the organization branch
    /// forgot <c>_sprk_contact_value eq null</c> and swept a person's grant. Matching on the emitted
    /// filter makes these tests fail when the predicate is wrong, which is the property worth protecting.</para>
    /// </summary>
    private sealed class FakeGrantTable
    {
        private readonly List<ExternalGrantRow> _rows = new();
        private int _seq;

        public int CreateCount { get; private set; }
        public int DeactivateCount { get; private set; }
        public int LevelUpdateCount { get; private set; }

        public IReadOnlyList<ExternalGrantRow> ActiveRows => _rows.Where(r => r.IsActive).ToList();

        public ExternalGrantRow Seed(Guid? contactId, Guid? organizationId, Guid projectId, int level)
        {
            var row = new ExternalGrantRow
            {
                Id = NextId(),
                ContactId = contactId,
                OrganizationId = organizationId,
                ProjectId = projectId,
                AccessLevel = level,
                StateCode = 0
            };
            _rows.Add(row);
            return row;
        }

        private Guid NextId() => Guid.Parse($"aaaaaaaa-0000-0000-0000-{++_seq:D12}");

        public Mock<DataverseWebApiClient> BuildMock()
        {
            var mock = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance);

            mock.Setup(c => c.CreateAsync(GrantEntitySet, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, object payload, CancellationToken _) =>
                {
                    CreateCount++;
                    var row = FromPayload((IDictionary<string, object?>)payload);
                    row.Id = NextId();
                    _rows.Add(row);
                    return row.Id;
                });

            mock.Setup(c => c.QueryAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) =>
                    Match(filter).ToList());

            mock.Setup(c => c.RetrieveAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string? _, CancellationToken _) =>
                    _rows.FirstOrDefault(r => r.Id == id));

            mock.Setup(c => c.UpdateAsync(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, object payload, CancellationToken _) =>
                {
                    var row = _rows.FirstOrDefault(r => r.Id == id);
                    if (row is not null)
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(payload);
                        if (json.Contains("\"statecode\":1"))
                        {
                            row.StateCode = 1;
                            DeactivateCount++;
                        }
                        var level = Regex.Match(json, @"""sprk_accesslevel"":(\d+)");
                        if (level.Success)
                        {
                            row.AccessLevel = int.Parse(level.Groups[1].Value);
                            LevelUpdateCount++;
                        }
                    }
                    return Task.CompletedTask;
                });

            // No `systemusers` setup is needed: every call site here passes callerOid: null, which
            // short-circuits ResolveGrantedBySystemUserIdAsync before it reaches the client, so the
            // audited sprk_grantedby field is simply omitted.
            return mock;
        }

        /// <summary>Interprets the production filter emitted by <c>ExternalGrantKey.ToActiveRowsFilter</c>.</summary>
        private IEnumerable<ExternalGrantRow> Match(string? filter)
        {
            if (filter is null) return Enumerable.Empty<ExternalGrantRow>();

            var rootId = Guid.Parse(Regex.Match(filter, @"_sprk_project_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
            var wantsOrg = filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal);

            if (wantsOrg)
            {
                var orgId = Guid.Parse(Regex.Match(filter, @"_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
                return _rows.Where(r => r.IsActive && r.ProjectId == rootId
                                        && r.OrganizationId == orgId && r.ContactId is null);
            }

            var contactId = Guid.Parse(Regex.Match(filter, @"_sprk_contact_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
            return _rows.Where(r => r.IsActive && r.ProjectId == rootId && r.ContactId == contactId);
        }

        private static ExternalGrantRow FromPayload(IDictionary<string, object?> payload)
        {
            static Guid? BoundId(IDictionary<string, object?> p, string key) =>
                p.TryGetValue(key, out var v) && v is string s
                    ? Guid.Parse(Regex.Match(s, @"\(([0-9a-fA-F-]{36})\)").Groups[1].Value)
                    : null;

            return new ExternalGrantRow
            {
                ContactId = BoundId(payload, "sprk_Contact@odata.bind"),
                OrganizationId = BoundId(payload, "sprk_Organization@odata.bind"),
                ProjectId = BoundId(payload, "sprk_Project@odata.bind"),
                AccessLevel = payload.TryGetValue("sprk_accesslevel", out var lvl) ? (int?)lvl : null,
                StateCode = 0
            };
        }
    }

    private static GrantAccessRequest Request(
        ExternalAccessLevel level = ExternalAccessLevel.ViewOnly,
        Guid? contactId = null,
        Guid? organizationId = null) =>
        new(
            ContactId: contactId ?? ContactId,
            ProjectId: ProjectId,
            AccessLevel: level,
            ExpiryDate: null,
            OrganizationId: organizationId);

    private static Task<Guid> Grant(Mock<DataverseWebApiClient> client, GrantAccessRequest request) =>
        GrantExternalAccessEndpoint.CreateGrantAsync(
            request, ExternalGrantRootType.Project, ProjectId,
            callerOid: null, client.Object, Mock.Of<ITenantCache>(),
            new DefaultHttpContext(), NullLogger.Instance, CancellationToken.None);

    /// <summary>
    /// ContainerId is null throughout this class, so the SPE step is never attempted and the membership
    /// service is only a constructor argument. Task 017 swapped the handler's <c>IGraphClientFactory</c>
    /// for <see cref="SpeContainerMembershipService"/> when the endpoint's forked (and broken) SPE matcher
    /// was deleted in favour of the service's own — see <c>SpeRevokeMatcherTests</c>.
    /// </summary>
    private static Task<IResult> Revoke(Mock<DataverseWebApiClient> client, Guid accessRecordId, Guid contactId) =>
        RevokeExternalAccessEndpoint.RevokeAccessAsync(
            new RevokeAccessRequest(accessRecordId, contactId, ProjectId, ContainerId: null),
            client.Object,
            new SpeContainerMembershipService(
                Mock.Of<IGraphClientFactory>(), NullLogger<SpeContainerMembershipService>.Instance),
            Mock.Of<ITenantCache>(),
            new DefaultHttpContext(), NullLogger<Program>.Instance, CancellationToken.None);

    private static RevokeAccessResponse RevokeBody(IResult result) =>
        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!;

    // ─────────────────────────────────────────────────────────────────────────────
    // A-11 — FLIPPED BY TASK 010 (FR-09). Idempotent grant.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED — was <c>Characterization_CreateGrantAsync_CalledTwiceWithSameInput_CreatesTwoActiveRows</c>.
    /// Granting the same contact the same level on the same root twice is now a no-op the second time:
    /// one CREATE, one active row, and the same id returned. FR-09 acceptance criterion 1.
    /// </summary>
    [Fact]
    public async Task CreateGrantAsync_CalledTwiceWithSameInput_IsIdempotent()
    {
        var table = new FakeGrantTable();
        var client = table.BuildMock();

        var firstId = await Grant(client, Request());
        var secondId = await Grant(client, Request());

        secondId.Should().Be(firstId, "the second grant must resolve to the existing row, not a new one");
        table.ActiveRows.Should().ContainSingle("exactly one active row backs one logical grant");
        table.CreateCount.Should().Be(1, "the second call must not issue a CREATE");
    }

    /// <summary>
    /// ✅ FLIPPED — was <c>Characterization_CreateGrantAsync_PerformsNoPreExistenceCheckBeforeCreating</c>.
    /// The pre-existence query is the mechanism that makes the upsert possible; its absence WAS the root
    /// cause. This asserts it happens before the write.
    /// </summary>
    [Fact]
    public async Task CreateGrantAsync_QueriesForAnExistingGrantBeforeCreating()
    {
        var table = new FakeGrantTable();
        var client = table.BuildMock();

        await Grant(client, Request());

        client.Verify(
            c => c.QueryAsync<ExternalGrantRow>(
                GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "an upsert must ask whether the logical grant already exists before writing");
    }

    /// <summary>
    /// ✅ FLIPPED — was <c>Characterization_CreateGrantAsync_ForSameContactAtDifferentLevels_CreatesSeparateRows</c>.
    /// A level change now updates the existing row IN PLACE. FR-09 acceptance criterion 2.
    ///
    /// This was the most dangerous duplicate shape: with two rows at different levels, revoking the
    /// FullAccess row left ViewOnly standing — or, worse, revoking ViewOnly left FullAccess.
    /// </summary>
    [Fact]
    public async Task CreateGrantAsync_ForSameGranteeAtDifferentLevel_UpdatesInPlace()
    {
        var table = new FakeGrantTable();
        var client = table.BuildMock();

        var viewOnlyId = await Grant(client, Request(ExternalAccessLevel.ViewOnly));
        var fullAccessId = await Grant(client, Request(ExternalAccessLevel.FullAccess));

        fullAccessId.Should().Be(viewOnlyId, "the level change updates the existing row");
        table.ActiveRows.Should().ContainSingle();
        table.ActiveRows[0].AccessLevel.Should().Be((int)ExternalAccessLevel.FullAccess);
        table.CreateCount.Should().Be(1);
        table.LevelUpdateCount.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A-11 — the headline scenario: grant → grant → revoke leaves NOTHING.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FR-09 ACCEPTANCE, verbatim: "granting twice then revoking once leaves zero active grants;
    /// effective access is None."
    ///
    /// This is the confirmed failure scenario from the investigation: R1 and R2 both active, revoke R1,
    /// R2 persists — and nothing in the product could show it.
    /// </summary>
    [Fact]
    public async Task GrantTwiceThenRevokeOnce_LeavesZeroActiveGrants()
    {
        var table = new FakeGrantTable();
        var client = table.BuildMock();

        var firstId = await Grant(client, Request());
        await Grant(client, Request());

        var result = await Revoke(client, firstId, ContactId);

        RevokeBody(result).DeactivatedCount.Should().BeGreaterThan(0);
        table.ActiveRows.Should().BeEmpty(
            "after revoking the grant, NO active row may remain for that grantee on that root — a " +
            "surviving sibling is privilege retained after revocation (A-11)");
    }

    /// <summary>
    /// The same guarantee against duplicates that PREDATE task 010 — rows already in the table before
    /// the upsert existed. Revoke sweeps by logical key, so it collapses history it did not create.
    /// </summary>
    [Fact]
    public async Task Revoke_WithPreExistingDuplicateRows_DeactivatesEveryOne()
    {
        var table = new FakeGrantTable();
        var first = table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.FullAccess);
        table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var client = table.BuildMock();

        var result = await Revoke(client, first.Id, ContactId);

        RevokeBody(result).DeactivatedCount.Should().Be(3);
        table.ActiveRows.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — the sweep must be precise. Over-sweeping is a privilege LOSS bug.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-09 acceptance criterion 4: revoking a PERSON grant must not touch an ORGANIZATION grant on the
    /// same root, and vice versa. They are distinct logical grants.
    ///
    /// This is the assertion that catches a missing <c>_sprk_contact_value eq null</c> in the org filter —
    /// without it, revoking the org grant would also sweep every member's personal grant on that root.
    /// </summary>
    [Fact]
    public async Task Revoke_OfPersonGrant_DoesNotDeactivateOrganizationGrantOnSameRoot()
    {
        var table = new FakeGrantTable();
        var personRow = table.Seed(ContactId, OrganizationId, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var orgRow = table.Seed(null, OrganizationId, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var client = table.BuildMock();

        var result = await Revoke(client, personRow.Id, ContactId);

        RevokeBody(result).DeactivatedCount.Should().Be(1);
        table.ActiveRows.Should().ContainSingle().Which.Id.Should().Be(orgRow.Id,
            "the organization grant is a DIFFERENT logical grant and must survive");
    }

    [Fact]
    public async Task Revoke_OfOrganizationGrant_DoesNotDeactivatePersonGrantOnSameRoot()
    {
        var table = new FakeGrantTable();
        var orgRow = table.Seed(null, OrganizationId, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var personRow = table.Seed(ContactId, OrganizationId, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var client = table.BuildMock();

        var result = await Revoke(client, orgRow.Id, Guid.Empty);

        RevokeBody(result).DeactivatedCount.Should().Be(1);
        table.ActiveRows.Should().ContainSingle().Which.Id.Should().Be(personRow.Id,
            "a person's own grant is a DIFFERENT logical grant and must survive an org revoke");
    }

    /// <summary>
    /// Another grantee's grant on the same root is untouched — the sweep is keyed on the grantee, not
    /// just the root.
    /// </summary>
    [Fact]
    public async Task Revoke_DoesNotDeactivateAnotherContactsGrantOnSameRoot()
    {
        var table = new FakeGrantTable();
        var mine = table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var theirs = table.Seed(OtherContactId, null, ProjectId, (int)ExternalAccessLevel.FullAccess);
        var client = table.BuildMock();

        await Revoke(client, mine.Id, ContactId);

        table.ActiveRows.Should().ContainSingle().Which.Id.Should().Be(theirs.Id);
    }

    /// <summary>
    /// Granting to an organization does NOT match a person's existing grant on the same root — so the
    /// org grant is created rather than mistakenly treated as already existing.
    /// </summary>
    [Fact]
    public async Task CreateGrantAsync_ForOrganization_DoesNotMatchAnExistingPersonGrant()
    {
        var table = new FakeGrantTable();
        table.Seed(ContactId, OrganizationId, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var client = table.BuildMock();

        await Grant(client, Request(contactId: Guid.Empty, organizationId: OrganizationId));

        table.ActiveRows.Should().HaveCount(2, "an org grant and a person grant coexist as distinct grants");
        table.CreateCount.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — no-op and failure semantics.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-09 acceptance criterion 5: revoking an already-inactive grant is an explicit no-op —
    /// <c>DeactivatedCount = 0</c> — not an error and not a silent success.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAlreadyInactiveGrant_IsAnExplicitNoOp()
    {
        var table = new FakeGrantTable();
        var row = table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var client = table.BuildMock();

        await Revoke(client, row.Id, ContactId);      // first revoke deactivates it
        var second = await Revoke(client, row.Id, ContactId);

        RevokeBody(second).DeactivatedCount.Should().Be(0,
            "nothing was left to deactivate — the caller is told so explicitly");
        table.ActiveRows.Should().BeEmpty();
    }

    /// <summary>
    /// An already-inactive TARGET must still sweep live siblings. "The row you named is already off" is
    /// not the same as "this grant confers nothing" — and answering the first question when the caller
    /// asked the second is how A-11 hid.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTargetIsInactiveButSiblingsAreActive_StillSweepsTheSiblings()
    {
        var table = new FakeGrantTable();
        var target = table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.FullAccess);
        target.StateCode = 1; // already revoked earlier
        var client = table.BuildMock();

        var result = await Revoke(client, target.Id, ContactId);

        RevokeBody(result).DeactivatedCount.Should().Be(1);
        table.ActiveRows.Should().BeEmpty("the live sibling must not survive because the named row was already off");
    }

    [Fact]
    public async Task Revoke_OfNonexistentGrant_ReturnsNotFound()
    {
        var table = new FakeGrantTable();
        var client = table.BuildMock();

        var result = await Revoke(client, Guid.NewGuid(), ContactId);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// FR-09 acceptance criterion 6, and the ADR-003 constraint verbatim: if the sibling-row query fails,
    /// /revoke returns an error — it never reports success. A success response with rows still active is
    /// the worst outcome available, because the caller believes access is gone.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenSiblingQueryFails_ReturnsErrorAndNeverSuccess()
    {
        var table = new FakeGrantTable();
        var row = table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        var client = table.BuildMock();

        client.Setup(c => c.QueryAsync<ExternalGrantRow>(
                GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var result = await Revoke(client, row.Id, ContactId);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        table.ActiveRows.Should().ContainSingle("nothing was deactivated, and the caller was told so");
    }

    /// <summary>
    /// A row with no derivable root has no queryable siblings, so the revoke FAILS rather than
    /// deactivating only the target. Per this task's ADR-003 constraint, /revoke must never report
    /// success while any matching active row remains unqueried — and here they cannot even be identified.
    /// Deactivating just the target would be the silent partial revocation A-11 describes.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenRowHasNoDerivableGrantKey_FailsLoudlyAndDeactivatesNothing()
    {
        var table = new FakeGrantTable();
        var orphan = table.Seed(ContactId, null, ProjectId, (int)ExternalAccessLevel.ViewOnly);
        orphan.ProjectId = null; // no root lookup at all
        var client = table.BuildMock();

        var result = await Revoke(client, orphan.Id, ContactId);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        table.ActiveRows.Should().ContainSingle("refusing is fail-closed: nothing was deactivated");
    }

    /// <summary>
    /// A returned row with no usable id is NOT an existing grant: it cannot be updated or deactivated,
    /// so adopting it would make the upsert a silent no-op that still reports success — and would aim an
    /// UPDATE at <see cref="Guid.Empty"/>.
    ///
    /// Found by <c>ExternalAccessContractTests.InviteAndGrant_WhenGranting_…</c>, whose stub client
    /// answers every query with the same canned payload: the grant returned an empty
    /// <c>accessRecordId</c> and wrote nothing. The stub is unrealistic, but the defect it exposed was
    /// real — unusable rows must be discarded, not adopted.
    /// </summary>
    [Fact]
    public async Task CreateGrantAsync_WhenQueryReturnsRowsWithNoUsableId_StillCreatesARealGrant()
    {
        var table = new FakeGrantTable();
        var client = table.BuildMock();

        client.Setup(c => c.QueryAsync<ExternalGrantRow>(
                GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalGrantRow> { new() { Id = Guid.Empty, StateCode = 0 } });

        var id = await Grant(client, Request());

        id.Should().NotBeEmpty("an unaddressable row must never be adopted as the existing grant");
        client.Verify(
            c => c.CreateAsync(GrantEntitySet, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(
            c => c.UpdateAsync(GrantEntitySet, Guid.Empty, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no update may be aimed at an empty id");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The logical key itself — pinned independently of the fake table.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GrantKey_ForContact_FiltersOnRootAndContactAndActiveState()
    {
        var filter = ExternalGrantKey.ForContact(ExternalGrantRootType.Project, ProjectId, ContactId)
            .ToActiveRowsFilter();

        filter.Should().Be($"_sprk_project_value eq {ProjectId} and _sprk_contact_value eq {ContactId} and statecode eq 0");
    }

    /// <summary>
    /// The <c>_sprk_contact_value eq null</c> clause is load-bearing: without it the org filter also
    /// matches every person grant whose contact belongs to that organization, so revoking an org grant
    /// would sweep individuals' personal grants. It mirrors the read side
    /// (<c>ExternalParticipationService</c>) term for term.
    /// </summary>
    [Fact]
    public void GrantKey_ForOrganization_RequiresContactToBeNull()
    {
        var filter = ExternalGrantKey.ForOrganization(ExternalGrantRootType.Project, ProjectId, OrganizationId)
            .ToActiveRowsFilter();

        filter.Should().Be(
            $"_sprk_project_value eq {ProjectId} and _sprk_organization_value eq {OrganizationId} " +
            "and _sprk_contact_value eq null and statecode eq 0");
    }

    [Theory]
    [InlineData(ExternalGrantRootType.Project, "_sprk_project_value")]
    [InlineData(ExternalGrantRootType.Matter, "_sprk_matter_value")]
    [InlineData(ExternalGrantRootType.WorkAssignment, "_sprk_workassignment_value")]
    public void GrantKey_UsesTheLookupValueColumnForEachRoot(ExternalGrantRootType rootType, string expectedColumn)
    {
        var filter = ExternalGrantKey.ForContact(rootType, ProjectId, ContactId).ToActiveRowsFilter();

        filter.Should().StartWith($"{expectedColumn} eq {ProjectId}",
            "filters use the _value column, not the PascalCase @odata.bind navigation property — the " +
            "latter matches nothing and would read as 'no sibling rows to sweep'");
    }

    /// <summary>
    /// A row carrying BOTH a contact and an organization is a PERSON grant whose firm is recorded as
    /// metadata. Deriving it as an org grant would make revoke sweep the wrong set.
    /// </summary>
    [Fact]
    public void DeriveKey_ForRowWithBothContactAndOrganization_IsAPersonGrant()
    {
        var row = new ExternalGrantRow
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            ContactId = ContactId,
            OrganizationId = OrganizationId,
            StateCode = 0
        };

        var key = ExternalGrantLifecycle.DeriveKey(row);

        key.Should().NotBeNull();
        key!.Value.IsOrganizationGrant.Should().BeFalse();
        key.Value.ContactId.Should().Be(ContactId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — payload contract. Task 010 must not disturb the bind keys.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildGrantPayload_ForPerContactGrant_BindsContactAndExactlyOneTypedRoot()
    {
        var payload = GrantExternalAccessEndpoint.BuildGrantPayload(
            Request(), ExternalGrantRootType.Project, ProjectId, grantedBySystemUserId: null);

        var dict = payload.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;

        dict.Should().ContainKey("sprk_Project@odata.bind");
        dict["sprk_Project@odata.bind"].Should().Be($"/sprk_projects({ProjectId})");
        dict.Should().ContainKey("sprk_Contact@odata.bind");
        dict["sprk_Contact@odata.bind"].Should().Be($"/contacts({ContactId})");

        dict.Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal)
                             && k.Contains("sprk_Matter", StringComparison.Ordinal))
            .Should().Be(0);
        dict.Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal)
                             && k.Contains("sprk_WorkAssignment", StringComparison.Ordinal))
            .Should().Be(0);
    }

    [Fact]
    public void BuildGrantPayload_ForOrganizationGrant_OmitsContactBind()
    {
        var orgRequest = Request(contactId: Guid.Empty, organizationId: OrganizationId);

        var payload = GrantExternalAccessEndpoint.BuildGrantPayload(
            orgRequest, ExternalGrantRootType.Project, ProjectId, grantedBySystemUserId: null);

        var dict = payload.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        dict.Should().NotContainKey("sprk_Contact@odata.bind");
        dict.Should().ContainKey("sprk_Project@odata.bind");
    }
}
