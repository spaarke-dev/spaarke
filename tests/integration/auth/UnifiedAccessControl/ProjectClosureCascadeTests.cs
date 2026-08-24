using System.Security.Claims;
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
/// The close-project revocation cascade — finding A-12 (spec FR-15), closed by task 016.
///
/// <para><b>What was wrong — two independent defects, either of which alone retains privilege.</b></para>
///
/// <para><i>1. The query could not run.</i> The cascade <c>$select</c>ed <c>_sprk_contactid_value</c>, an
/// attribute that does not exist: live metadata for <c>sprk_externalrecordaccess</c> declares the lookup
/// <c>sprk_contact</c>, which Dataverse projects as <c>_sprk_contact_value</c>. A <c>$select</c> naming a
/// nonexistent column returns 400, the helper rethrew, and <c>Handle</c> had no <c>try</c> — so every
/// closure 500'd having deactivated nothing, and never reached SPE removal either. Task 070 had already
/// fixed the sibling project lookup in this very file and left the contact one stale.</para>
///
/// <para><i>2. Organization grants were filtered out.</i> The projection required the contact to be
/// non-null — and a row with no contact is precisely how this schema represents an ORGANIZATION grant
/// (the discriminator <c>ExternalGrantKey</c> and <c>ExternalParticipationService</c> both key on). So
/// even with the column corrected, closing a project would leave every organization grant active.</para>
///
/// <para><b>Why no test caught it.</b> <c>ExternalAccessRow</c> was <c>private</c>, so no test could name
/// <c>QueryAsync&lt;ExternalAccessRow&gt;</c> to substitute at the seam. The pre-existing unit test
/// <c>CloseProject_DataverseQueryThrows_PropagatesException</c> said as much in its own comments and then
/// asserted <c>Guid.Empty == Guid.Empty</c>. Task 016 makes the type <c>internal</c> — the sanctioned
/// alternative to reflection (ADR-038 §4, ban B8) — and these tests drive the real handler.</para>
///
/// <para>The seam is <see cref="DataverseWebApiClient"/>, whose methods are <c>virtual</c>. No
/// <c>Mock&lt;HttpMessageHandler&gt;</c> (ban B1), no reflection into privates (ban B8).</para>
/// </summary>
public class ProjectClosureCascadeTests
{
    private const string GrantEntitySet = "sprk_externalrecordaccesses";
    private const string TenantId = "00000000-0000-0000-0000-0000000000cc";

    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherProjectId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// Every column Dataverse actually exposes on <c>sprk_externalrecordaccess</c>, from live metadata
    /// (task 016 step 1, the escalation-gated verification). A <c>$select</c> naming anything outside this
    /// set is a 400 — which is exactly how A-12 broke closure, so the fake reproduces it rather than
    /// tolerating it.
    /// </summary>
    private static readonly HashSet<string> LiveColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_externalrecordaccessid", "sprk_name", "sprk_accesslevel", "sprk_expiresdate",
        "sprk_granteddate", "statecode", "statuscode",
        "_sprk_contact_value", "_sprk_organization_value", "_sprk_project_value",
        "_sprk_matter_value", "_sprk_workassignment_value", "_sprk_invoice_value",
        "_sprk_grantedby_value", "_sprk_recordtype_value",
        "createdon", "modifiedon", "ownerid"
    };

    /// <summary>
    /// Config sufficient for the real <see cref="DataverseWebApiClient"/> constructor (Moq invokes it).
    /// Every method the code under test calls is overridden, so no token is requested and nothing dials out.
    /// </summary>
    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = TenantId
        }).Build();

    // ─────────────────────────────────────────────────────────────────────────────
    // An in-memory sprk_externalrecordaccess table that behaves like Dataverse.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores grant rows, answers <c>QueryAsync</c> by interpreting the production <c>$filter</c> and
    /// <c>$select</c>, and applies <c>UpdateAsync</c> payloads.
    ///
    /// <para><b>Why it validates the <c>$select</c> instead of ignoring it.</b> A fake that returned canned
    /// rows regardless of the projection would pass just as happily with <c>_sprk_contactid_value</c> as
    /// with the real column — it would have gone green on the exact code that shipped A-12. Rejecting
    /// unknown columns the way Dataverse does is what makes the column name a tested property rather
    /// than a comment.</para>
    /// </summary>
    private sealed class FakeGrantTable
    {
        private readonly List<Row> _rows = new();
        private int _seq;

        /// <summary>Ids whose deactivation PATCH should fail, simulating a mid-sweep Dataverse error.</summary>
        public HashSet<Guid> FailDeactivationFor { get; } = new();

        /// <summary>Set to make the enumeration query fail outright.</summary>
        public Exception? QueryFailure { get; set; }

        /// <summary>The <c>$select</c> the production code last emitted.</summary>
        public string? LastSelect { get; private set; }

        public IReadOnlyList<Row> ActiveRows => _rows.Where(r => r.StateCode == 0).ToList();

        public sealed class Row
        {
            public Guid Id { get; set; }
            public Guid? ContactId { get; set; }
            public Guid? OrganizationId { get; set; }
            public Guid ProjectId { get; set; }
            public int StateCode { get; set; }
        }

        public Row SeedContactGrant(Guid contactId, Guid projectId, Guid? organizationId = null)
            => Seed(contactId, organizationId, projectId);

        public Row SeedOrganizationGrant(Guid organizationId, Guid projectId)
            => Seed(null, organizationId, projectId);

        private Row Seed(Guid? contactId, Guid? organizationId, Guid projectId)
        {
            var row = new Row
            {
                Id = Guid.Parse($"aaaaaaaa-0000-0000-0000-{++_seq:D12}"),
                ContactId = contactId,
                OrganizationId = organizationId,
                ProjectId = projectId,
                StateCode = 0
            };
            _rows.Add(row);
            return row;
        }

        public Mock<DataverseWebApiClient> BuildMock()
        {
            var mock = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance);

            mock.Setup(c => c.QueryAsync<ProjectClosureEndpoint.ExternalAccessRow>(
                    GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? select, int? _, int? _, CancellationToken _) =>
                {
                    LastSelect = select;

                    if (QueryFailure is not null)
                        throw QueryFailure;

                    RejectUnknownColumns(select);
                    return Match(filter).Select(Project).ToList();
                });

            mock.Setup(c => c.UpdateAsync(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, object payload, CancellationToken _) =>
                {
                    if (FailDeactivationFor.Contains(id))
                        throw new InvalidOperationException($"Dataverse rejected the update for {id}");

                    var row = _rows.FirstOrDefault(r => r.Id == id);
                    if (row is not null &&
                        System.Text.Json.JsonSerializer.Serialize(payload).Contains("\"statecode\":1"))
                    {
                        row.StateCode = 1;
                    }

                    return Task.CompletedTask;
                });

            return mock;
        }

        /// <summary>Dataverse's own behaviour: a projection naming a column the table lacks is a 400.</summary>
        private static void RejectUnknownColumns(string? select)
        {
            foreach (var column in (select ?? string.Empty)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!LiveColumns.Contains(column))
                {
                    throw new InvalidOperationException(
                        $"Dataverse 400: Could not find a property named '{column}' on type " +
                        "'Microsoft.Dynamics.CRM.sprk_externalrecordaccess'.");
                }
            }
        }

        /// <summary>Interprets the filter emitted by <c>BuildActiveProjectGrantsFilter</c>.</summary>
        private IEnumerable<Row> Match(string? filter)
        {
            if (filter is null) return Enumerable.Empty<Row>();

            var projectMatch = Regex.Match(filter, @"_sprk_project_value eq ([0-9a-fA-F-]{36})");
            if (!projectMatch.Success) return Enumerable.Empty<Row>();

            var projectId = Guid.Parse(projectMatch.Groups[1].Value);
            var activeOnly = filter.Contains("statecode eq 0", StringComparison.Ordinal);

            return _rows.Where(r => r.ProjectId == projectId && (!activeOnly || r.StateCode == 0));
        }

        private static ProjectClosureEndpoint.ExternalAccessRow Project(Row row) => new()
        {
            sprk_externalrecordaccessid = row.Id,
            _sprk_contact_value = row.ContactId,
            _sprk_organization_value = row.OrganizationId
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Driving the real handler.
    // ─────────────────────────────────────────────────────────────────────────────

    private static HttpContext AuthenticatedContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("tid", TenantId) }, authenticationType: "Test"));
        return context;
    }

    private static Task<IResult> CloseProject(
        Mock<DataverseWebApiClient> client,
        ITenantCache? cache = null,
        Guid? projectId = null,
        string? containerId = null) =>
        ProjectClosureEndpoint.Handle(
            new CloseProjectRequest(projectId ?? ProjectId, containerId),
            client.Object,
            new SpeContainerMembershipService(
                Mock.Of<IGraphClientFactory>(),
                NullLogger<SpeContainerMembershipService>.Instance),
            cache ?? Mock.Of<ITenantCache>(),
            AuthenticatedContext(),
            NullLogger<Program>.Instance,
            CancellationToken.None);

    private static CloseProjectResponse OkBody(IResult result) =>
        result.Should().BeOfType<Ok<CloseProjectResponse>>(
            "closure must report success only when it actually closed").Subject.Value!;

    private static ProblemHttpResult Problem(IResult result) =>
        result.Should().BeOfType<ProblemHttpResult>().Subject;

    // ─────────────────────────────────────────────────────────────────────────────
    // A-12 — FLIPPED BY TASK 016 (FR-15). The cascade runs, and sweeps BOTH grant kinds.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED — the pre-fix behaviour was a 500 with zero rows deactivated, because the projection
    /// named a column that does not exist.
    ///
    /// FR-15 acceptance, verbatim: "closure returns 200 and all active grants for the project are
    /// deactivated." This is the whole finding in one test: two contact grants and one organization grant
    /// go in, nothing active comes out.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithContactAndOrganizationGrants_Returns200AndDeactivatesEveryGrant()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();

        var result = await CloseProject(client);

        OkBody(result).AccessRecordsRevoked.Should().Be(3);
        table.ActiveRows.Should().BeEmpty(
            "no participant may retain access after their project is closed (FR-15)");
    }

    /// <summary>
    /// The organization half of A-12 in isolation. The pre-fix <c>.Where(r =&gt; …ContactId.HasValue)</c>
    /// dropped every contact-less row, so a project whose only external access came through an
    /// organization grant closed "successfully" while the whole firm kept its access.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithOnlyAnOrganizationGrant_DeactivatesIt()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();

        var result = await CloseProject(client);

        OkBody(result).AccessRecordsRevoked.Should().Be(1,
            "an organization grant is a grant — a null contact is its discriminator, not a reason to skip it");
        table.ActiveRows.Should().BeEmpty();
    }

    /// <summary>
    /// A contact grant that also records the contact's firm must be swept as a CONTACT grant and must not
    /// be double-counted or mistaken for the organization's own grant. Both rows are distinct logical
    /// grants (per <c>ExternalGrantKey</c>) and closure ends both.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithPersonAndOrganizationGrantsOnTheSameFirm_DeactivatesBoth()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId, organizationId: OrganizationId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();

        var result = await CloseProject(client);

        OkBody(result).AccessRecordsRevoked.Should().Be(2);
        table.ActiveRows.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The projection itself — the mechanism of A-12, pinned directly.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The direct guard on A-12's mechanism: every column the cascade projects must exist on the table.
    /// This is the assertion that fails the instant someone reintroduces a <c>*id_value</c> form.
    ///
    /// <para>It matters because the failure is loud but useless — a 400 surfaces as "closure errored",
    /// never as "your column name is wrong", and the same class of typo already shipped twice in this one
    /// file (task 070 fixed the project lookup, A-12 found the contact lookup).</para>
    /// </summary>
    [Fact]
    public void ActiveGrantSelect_NamesOnlyColumnsThatExistOnTheTable()
    {
        var columns = ProjectClosureEndpoint.ActiveGrantSelect
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        columns.Should().OnlyContain(c => LiveColumns.Contains(c),
            "a $select naming a nonexistent column returns 400, which reads downstream as a failed " +
            "closure rather than a schema mistake");
    }

    /// <summary>
    /// The contact lookup is <c>_sprk_contact_value</c> — verified against live
    /// <c>sprk_externalrecordaccess</c> metadata in task 016, and matching the runtime read path in
    /// <c>ExternalParticipationService</c>. The solution's <c>views-schema.md</c> still says
    /// <c>sprk_contactid</c> and is stale; do not "correct" this back to it.
    /// </summary>
    [Fact]
    public void ActiveGrantSelect_UsesTheContactLookupValueColumn()
    {
        ProjectClosureEndpoint.ActiveGrantSelect.Should().Contain("_sprk_contact_value");
        ProjectClosureEndpoint.ActiveGrantSelect.Should().NotContain("_sprk_contactid_value");
    }

    /// <summary>
    /// The emitted projection is validated end-to-end, not just the constant — a constant can be correct
    /// while the call site passes something else.
    /// </summary>
    [Fact]
    public async Task CloseProject_EmitsAProjectionDataverseAccepts()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();

        await CloseProject(client);

        table.LastSelect.Should().NotBeNullOrEmpty();
        table.LastSelect!.Split(',', StringSplitOptions.TrimEntries)
            .Should().OnlyContain(c => LiveColumns.Contains(c));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — never report a success the cascade did not achieve (ADR-003).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-15 acceptance, negative case, and this task's ADR-003 constraint verbatim: when the grants
    /// cannot be enumerated the closure must NOT return a success that leaves grants active.
    ///
    /// <para>Pre-fix this was an unhandled exception — a 500, so technically not a false success, but
    /// untyped and indistinguishable from any other crash. Now it is a ProblemDetails carrying a
    /// machine-readable reason code, so a caller can tell "retry this closure" from "this endpoint is
    /// broken".</para>
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenEnumerationFails_ReportsIncompleteAndNeverSuccess()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.QueryFailure = new InvalidOperationException("Dataverse unavailable");
        var client = table.BuildMock();

        var result = await CloseProject(client);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should()
            .Be(ProjectClosureEndpoint.ClosureEnumerationFailedReason);
        table.ActiveRows.Should().ContainSingle(
            "nothing was deactivated — and the caller was told so rather than shown a 200");
    }

    /// <summary>
    /// If enumeration fails we do not know which grants exist, so the deactivation sweep must not run at
    /// all. (Steps 2-4 stay unreachable — the POML's "reachable only after Step 1 succeeds".)
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenEnumerationFails_AttemptsNoDeactivation()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.QueryFailure = new InvalidOperationException("Dataverse unavailable");
        var client = table.BuildMock();

        await CloseProject(client);

        client.Verify(
            c => c.UpdateAsync(GrantEntitySet, It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A row that could not be deactivated is a participant who still has access. Answering 200 would tell
    /// the operator the project is closed when it is not — the same false-success shape ADR-003 forbids for
    /// the enumeration failure, and the one the prior code produced by counting only successes.
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenSomeDeactivationsFail_ReportsIncompleteRatherThan200()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var stubborn = table.SeedContactGrant(OtherContactId, ProjectId);
        table.FailDeactivationFor.Add(stubborn.Id);
        var client = table.BuildMock();

        var result = await CloseProject(client);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should()
            .Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        problem.ProblemDetails.Extensions["accessRecordsRevoked"].Should().Be(1,
            "'we revoked none' and 'we revoked all but one' need different operator responses");
    }

    /// <summary>
    /// One row's failure must not abort the sweep: every other participant should still lose access.
    /// Stopping at the first error would leave strictly MORE access standing.
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenOneDeactivationFails_StillDeactivatesTheOthers()
    {
        var table = new FakeGrantTable();
        var stubborn = table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        table.FailDeactivationFor.Add(stubborn.Id);
        var client = table.BuildMock();

        await CloseProject(client);

        table.ActiveRows.Should().ContainSingle().Which.Id.Should().Be(stubborn.Id,
            "only the row that genuinely failed may survive");
    }

    // ⚠️ NOT TESTED HERE, AND THE REASON IS ITSELF A FINDING — filed onto task 017.
    //
    // ProjectClosureEndpoint guards the SPE step and answers
    // `reasonCode: sdap.closure.incomplete.container_not_cleared` if clearing the container fails. That
    // guard cannot be exercised today because `RemoveAllExternalMembersAsync` CANNOT fail:
    // `SpeContainerMembershipService.ListExternalMembersAsync` catches ServiceException AND Exception and
    // returns `[]` in both, so an unreachable Graph is indistinguishable from an empty container. Closure
    // then reports `SpeContainerMembersRemoved: 0` with a 200 while every external user may still hold
    // file permission on the container.
    //
    // That is FR-15's own failure shape ("no participant retains access post-closure") on the SPE half,
    // and it is NOT fixed by task 016 — the defect is in SpeContainerMembershipService, task 017's file.
    // The guard is kept because it is correct the moment that service reports failure honestly, and
    // because leaving the call bare would turn that fix into an unhandled 500 here.

    /// <summary>
    /// Cache invalidation runs even when a container id is supplied and the SPE step does nothing —
    /// that step only ever removes access, so it must not be skipped or reordered behind the SPE call.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithAContainerId_StillInvalidatesContactCaches()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        var cache = new Mock<ITenantCache>();

        await CloseProject(client, cache.Object, containerId: "container-abc-123");

        cache.Verify(
            c => c.RemoveAsync(
                TenantId, ExternalParticipationService.ExternalAccessResource,
                ContactId.ToString(), ExternalParticipationService.CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — the sweep must stay precise and quiet when there is nothing to do.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-15 acceptance, negative case: a project with zero grants closes cleanly. Pre-fix this was the
    /// ONLY path that returned 200 — the query never ran, so the bad column never surfaced.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithNoGrants_Returns200AndRevokesNothing()
    {
        var table = new FakeGrantTable();
        var client = table.BuildMock();

        var result = await CloseProject(client);

        var body = OkBody(result);
        body.AccessRecordsRevoked.Should().Be(0);
        body.AffectedContactIds.Should().BeEmpty();
    }

    /// <summary>
    /// Over-sweeping is a privilege LOSS bug, and the mirror risk of broadening the filter. Closing one
    /// project must not touch another project's grants.
    /// </summary>
    [Fact]
    public async Task CloseProject_DoesNotDeactivateGrantsOnAnotherProject()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var untouched = table.SeedContactGrant(ContactId, OtherProjectId);
        table.SeedOrganizationGrant(OrganizationId, OtherProjectId);
        var client = table.BuildMock();

        await CloseProject(client);

        table.ActiveRows.Should().HaveCount(2)
            .And.Contain(r => r.Id == untouched.Id);
        table.ActiveRows.Should().OnlyContain(r => r.ProjectId == OtherProjectId,
            "the cascade is scoped to the project being closed");
    }

    /// <summary>
    /// Already-inactive rows are not re-swept: the filter carries <c>statecode eq 0</c>, so a second
    /// closure is a clean no-op. Closure is idempotent, which is what makes "retry it" the right advice
    /// after a partial failure.
    /// </summary>
    [Fact]
    public async Task CloseProject_CalledTwice_IsIdempotent()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();

        await CloseProject(client);
        var second = await CloseProject(client);

        OkBody(second).AccessRecordsRevoked.Should().Be(0);
        table.ActiveRows.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Reporting — who the caller is told was affected.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>AffectedContactIds</c> drives per-contact cache invalidation, and an organization grant names no
    /// contact. Reporting a null or empty GUID for it would invalidate a nonexistent cache entry and
    /// mislead the caller about who was affected; the organization's members fall back to the 60s ADR-009
    /// TTL, documented on <c>InvalidateContactCachesAsync</c>.
    /// </summary>
    [Fact]
    public async Task CloseProject_ReportsOnlyRealContactsAsAffected()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();

        var result = await CloseProject(client);

        var body = OkBody(result);
        body.AccessRecordsRevoked.Should().Be(2, "both grants are revoked…");
        body.AffectedContactIds.Should().ContainSingle().Which.Should().Be(ContactId,
            "…but only the contact grant names a contact whose cache can be invalidated");
        body.AffectedContactIds.Should().NotContain(Guid.Empty);
    }

    /// <summary>
    /// The same contact holding two grants on one project is reported once — the list keys cache
    /// invalidation, not grant count.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithDuplicateGrantsForOneContact_ReportsThatContactOnce()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();

        var result = await CloseProject(client);

        var body = OkBody(result);
        body.AccessRecordsRevoked.Should().Be(2);
        body.AffectedContactIds.Should().ContainSingle().Which.Should().Be(ContactId);
    }

    /// <summary>
    /// Each affected contact's participation cache is cleared, so access stops at once rather than after
    /// the TTL. The cache is what the enforcement path reads.
    /// </summary>
    [Fact]
    public async Task CloseProject_InvalidatesTheParticipationCacheForEachAffectedContact()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId);
        var client = table.BuildMock();
        var cache = new Mock<ITenantCache>();

        await CloseProject(client, cache.Object);

        foreach (var contactId in new[] { ContactId, OtherContactId })
        {
            cache.Verify(
                c => c.RemoveAsync(
                    TenantId, ExternalParticipationService.ExternalAccessResource,
                    contactId.ToString(), ExternalParticipationService.CacheVersion,
                    It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — an unaddressable row.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A row returned without an id cannot be PATCHed, so it cannot be deactivated. Skipping it quietly
    /// (as the old <c>.Where(… .HasValue)</c> did for the id too) would leave an active grant behind a 200.
    /// It counts as a deactivation failure so the closure reports itself incomplete.
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenARowHasNoUsableId_DoesNotReportSuccess()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();

        client.Setup(c => c.QueryAsync<ProjectClosureEndpoint.ExternalAccessRow>(
                GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectClosureEndpoint.ExternalAccessRow>
            {
                new() { sprk_externalrecordaccessid = null, _sprk_contact_value = ContactId }
            });

        var result = await CloseProject(client);

        Problem(result).ProblemDetails.Extensions["reasonCode"].Should()
            .Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        client.Verify(
            c => c.UpdateAsync(GrantEntitySet, Guid.Empty, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no update may be aimed at an empty id");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The filter — unchanged by this task, guarded so the fix does not regress it.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Task 070's fix (<c>_sprk_projectid_value</c> → <c>_sprk_project_value</c>) had exactly the same
    /// silent-failure shape as A-12 and lives one line away. Pinned so broadening the sweep does not undo it.
    /// </summary>
    [Fact]
    public void BuildActiveProjectGrantsFilter_ScopesToTheProjectAndActiveRowsOnly()
    {
        ProjectClosureEndpoint.BuildActiveProjectGrantsFilter(ProjectId)
            .Should().Be($"_sprk_project_value eq {ProjectId} and statecode eq 0");
    }

    /// <summary>
    /// The cascade must NOT filter on expiry. Task 007 added
    /// <c>(sprk_expiresdate eq null or sprk_expiresdate ge …)</c> to the grant READ paths; applying it here
    /// would make an expired-but-still-active row invisible to closure and therefore permanently
    /// unrevokable. Expired rows are exactly what a closure sweep should clean up.
    /// </summary>
    [Fact]
    public void BuildActiveProjectGrantsFilter_DoesNotFilterOnExpiry()
    {
        ProjectClosureEndpoint.BuildActiveProjectGrantsFilter(ProjectId)
            .Should().NotContain("sprk_expiresdate",
                "a revocation sweep must SEE expired rows — filtering them makes them unrevokable (task 007)");
    }
}
