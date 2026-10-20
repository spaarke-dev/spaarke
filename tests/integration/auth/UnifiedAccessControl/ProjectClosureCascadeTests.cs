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
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;
using Sprk.Bff.Api.Tests.TestInfrastructure;

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
        // Task 140's issuer columns (the contact lookup and its text provenance), kept in step with
        // RecordShareExpiryTests' copy of the same live set so a closure path that ever selects them is not a false 400.
        "_sprk_grantedbycontact_value", "sprk_grantedbycontactid",
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
            // auth-v4 (master) made DataverseWebApiClient select its credential from THIS FLAG
            // rather than from the presence of a client secret, and it now THROWS when Managed
            // Identity is disabled and neither a TokenCredential nor an IConfidentialClientProvider
            // is supplied. Enabling it takes the MI branch, whose DefaultAzureCredential is
            // constructed lazily and never authenticates — this client is fully stubbed.
            ["Graph:ManagedIdentity:Enabled"] = "true",
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

        /// <summary>#2104: set to make only the REVOKED-rows read (<c>statecode eq 1</c>) fail.</summary>
        public Exception? RevokedQueryFailure { get; set; }

        /// <summary>#2104: every <c>$top</c> the production code passed, in order.</summary>
        public List<int?> Tops { get; } = new();

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
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance,
                // Moq matches a class-proxy constructor EXACTLY; master's auth-v4 widened this
                // ctor with two OPTIONAL params (TokenCredential, IConfidentialClientProvider)
                // and optional args do not participate in proxy ctor selection. Passed
                // explicitly as null: this double never authenticates.
                // Positional and null-forgiving, both deliberately. Mock<T> takes `params object[]`
                // for the proxied type's ctor args, so (a) NAMED arguments bind to Mock's own ctor
                // and fail CS1739, and (b) a bare null literal fails CS8625 against the
                // non-nullable element type. This double never authenticates, so both credential
                // slots are genuinely unused.
                null!, null!);

            mock.Setup(c => c.QueryAsync<ProjectClosureEndpoint.ExternalAccessRow>(
                    GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? select, int? top, int? _, CancellationToken _) =>
                {
                    LastSelect = select;
                    Tops.Add(top);

                    if (QueryFailure is not null)
                        throw QueryFailure;

                    if (RevokedQueryFailure is not null && filter?.Contains("statecode eq 1", StringComparison.Ordinal) == true)
                        throw RevokedQueryFailure;

                    RejectUnknownColumns(select);
                    // $top is honoured, as Dataverse does: a bound the code relies on must be a tested property.
                    return Match(filter).Select(Project).Take(top ?? int.MaxValue).ToList();
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

        /// <summary>Interprets the filters emitted by <c>BuildActiveProjectGrantsFilter</c> and (#2104)
        /// <c>BuildRevokedProjectGrantsFilter</c>.</summary>
        private IEnumerable<Row> Match(string? filter)
        {
            if (filter is null) return Enumerable.Empty<Row>();

            var projectMatch = Regex.Match(filter, @"_sprk_project_value eq ([0-9a-fA-F-]{36})");
            if (!projectMatch.Success) return Enumerable.Empty<Row>();

            var projectId = Guid.Parse(projectMatch.Groups[1].Value);
            var activeOnly = filter.Contains("statecode eq 0", StringComparison.Ordinal);
            var inactiveOnly = filter.Contains("statecode eq 1", StringComparison.Ordinal);

            return _rows.Where(r => r.ProjectId == projectId
                                    && (!activeOnly || r.StateCode == 0)
                                    && (!inactiveOnly || r.StateCode == 1));
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

    /// <summary>
    /// Drives the REAL handler. Task 166: the container is no longer a request field — the handler DERIVES it from
    /// the project through the real <see cref="RecordContainerResolver"/>. By default the project is NOT secure, so
    /// the container step is skipped (the pre-166 "no containerId" behaviour these grant-sweep tests assume); pass
    /// <paramref name="resolver"/> to make it a secure project with its own container.
    /// </summary>
    private static Task<IResult> CloseProject(
        Mock<DataverseWebApiClient> client,
        ITenantCache? cache = null,
        Guid? projectId = null,
        Mock<SpeContainerMembershipService>? spe = null,
        ExternalParticipationService? participations = null,
        RecordContainerResolver? resolver = null) =>
        ProjectClosureEndpoint.Handle(
            new CloseProjectRequest(projectId ?? ProjectId),
            client.Object,
            spe?.Object ?? new SpeContainerMembershipService(
                TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()),
                NullLogger<SpeContainerMembershipService>.Instance),
            // Task 137: the closure invalidates through the ONE routine, run here for real over this test's cache and
            // the request's tid, so the RemoveAsync verifications below still read what the production code removed.
            participations ?? GrantPolicyTestDoubles.RealInvalidationOver(cache ?? Mock.Of<ITenantCache>(), AuthenticatedContext()),
            resolver ?? TestRecordContainerResolver.ForNonSecureRecord("sprk_project", projectId ?? ProjectId),
            AuthenticatedContext(),
            NullLogger<Program>.Instance,
            CancellationToken.None);

    /// <summary>The secure project's OWN container — the only one task 166 lets a closure touch.</summary>
    private const string OwnContainer = "b!secure-project-own-container";

    private const string ContactEmail = "external.counsel@clientfirm.com";
    private const string OtherContactEmail = "second.counsel@clientfirm.com";
    private const string MemberEmail = "org.member@clientfirm.com";
    private static readonly Guid MemberContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// Arranges the two identity reads the grantee-scoped container step makes (task 166, amendment e): each
    /// contact's email, and each revoked organization's active members.
    /// </summary>
    private static void ArrangeGranteeIdentities(Mock<DataverseWebApiClient> client)
    {
        var emails = new Dictionary<Guid, string>
        {
            [ContactId] = ContactEmail,
            [OtherContactId] = OtherContactEmail,
            [MemberContactId] = MemberEmail,
        };

        client.Setup(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
                "contacts", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string _, CancellationToken _) =>
                new RevokeExternalAccessEndpoint.ContactEmailRow
                {
                    emailaddress1 = emails.TryGetValue(id, out var email) ? email : null
                });

        client.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                ExternalOrganizationMembership.EntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalOrganizationMembership.ContactOrganizationRow>
            {
                new() { ContactId = MemberContactId }
            });
    }

    /// <summary>The membership seam: answers RemoveMembershipsAsync per key from <paramref name="answer"/> (by the
    /// key's email — the contacts here have no bound object id) and records every (container, emails) it was called
    /// with. #2104 round 2 (F1): <paramref name="containerState"/> is the container-level answer ("one remains" unless a
    /// test says otherwise; three-state since round 4), and
    /// <paramref name="confirmations"/> (when given) records each call's <c>confirmNoContactPermission</c>.</summary>
    private static Mock<SpeContainerMembershipService> SpeAnswering(
        Func<string, SpeContainerMembershipResult> answer,
        List<(string ContainerId, IReadOnlyCollection<string> Emails)> calls,
        SpeContainerContactPermissionState containerState = SpeContainerContactPermissionState.ContactPermissionRemains,
        List<bool>? confirmations = null)
    {
        var spe = new Mock<SpeContainerMembershipService>(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);
        spe.Setup(s => s.RemoveMembershipsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SpeMembershipKey>>(), It.IsAny<DataverseWebApiClient>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string containerId, IReadOnlyCollection<SpeMembershipKey> keys, DataverseWebApiClient _,
                bool confirm, CancellationToken _) =>
            {
                calls.Add((containerId, keys.Select(k => k.Email ?? k.ToString()).ToList()));
                confirmations?.Add(confirm);
                var results = new Dictionary<SpeMembershipKey, SpeContainerMembershipResult>();
                foreach (var key in keys)
                {
                    results[key] = answer(key.Email ?? key.ToString());
                }

                return new SpeMembershipRemoval(results, containerState);
            });
        return spe;
    }

    private static readonly SpeContainerMembershipResult Removed = new(true, "permission-1", null);

    private static SpeContainerMembershipResult NotFound(string email) =>
        new(false, null, $"{SpeContainerMembershipService.NoPermissionFoundError} for user '{email}' in container.");

    private static readonly SpeContainerMembershipResult GraphError = new(false, null, "Graph API error (503)");

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

    // ─────────────────────────────────────────────────────────────────────────────
    // The container step — task 017's guard, re-based by task 166 (S-39 + amendment e).
    //
    // Task 016 built the `container_not_cleared` guard; task 017 made it reachable. Task 166 changed WHICH
    // container and WHO is removed: the container is DERIVED from the project (a secure project's own
    // container, never a client-supplied id, never the shared business-unit container), and only the
    // grantees this closure revoked are removed — through the same identity-keyed RemoveMembershipsAsync the
    // single-grant revoke uses — instead of every permission carrying a user identity (internal users too).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// SPE container membership IS access: a grantee still on the container can still reach the project's files
    /// even with every Dataverse grant deactivated. A failure to clear them must not read as a closed project — and
    /// must not throw away the fact that the grants WERE revoked.
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenTheContainerCannotBeCleared_ReportsIncompleteWithTheRevokedCount()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var spe = new Mock<SpeContainerMembershipService>(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);
        spe.Setup(s => s.RemoveMembershipsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SpeMembershipKey>>(), It.IsAny<DataverseWebApiClient>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph unreachable"));

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should()
            .Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        problem.ProblemDetails.Extensions["accessRecordsRevoked"].Should().Be(2,
            "the grants were revoked and the operator needs to know that, even though closure failed");
        table.ActiveRows.Should().BeEmpty("the grant sweep completed before the container step");
    }

    /// <summary>
    /// A PARTIAL clear: one revoked grantee's permission could not be removed, so they keep file access and the
    /// project is not closed.
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenARevokedGranteeRemainsOnTheContainer_ReportsIncomplete()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string, IReadOnlyCollection<string>)>();
        var spe = SpeAnswering(email => email == OtherContactEmail ? GraphError : Removed, calls);

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

        Problem(result).ProblemDetails.Extensions["reasonCode"].Should()
            .Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason,
                "one revoked grantee retains file access — the project is not closed");
    }

    /// <summary>
    /// The complementary positive, and the F8 core: a SECURE project's revoked grantees are removed from the
    /// project's OWN container — exactly once, with exactly their emails (contact grants AND every active member of
    /// an organization grant) — and the closure reports 200 with the removed count. A grantee with no permission on a
    /// fully read container is the healthy broker-only answer, not a failure.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_RemovesExactlyTheRevokedGranteesFromItsOwnContainer()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(email => email == OtherContactEmail ? NotFound(email) : Removed, calls);

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

        OkBody(result).SpeContainerMembersRemoved.Should().Be(2,
            "two revoked grantees held a permission and lost it; the third held none");
        calls.Should().ContainSingle("ONE paged read and sweep of the container, not one per grantee");
        calls[0].ContainerId.Should().Be(OwnContainer, "the server-derived container — the project's own");
        calls[0].Emails.Should().BeEquivalentTo(new[] { ContactEmail, OtherContactEmail, MemberEmail },
            "exactly the revoked grantees: both contacts and the revoked organization's active member — never "
            + "every permission with a user identity, which would strip the project's INTERNAL users too");
    }

    /// <summary>A grantee contact <see cref="ArrangeGranteeIdentities"/> gives no email (#2103).</summary>
    private static readonly Guid NoEmailContactId = Guid.Parse("77777777-2103-4000-8000-000000000001");

    /// <summary>
    /// #2103: a revoked grantee with no <c>emailaddress1</c> has nothing on the container to clear — the single-grant
    /// revoke's decision, shared. The closure completes with a 200. It used to answer 500
    /// <c>container_not_cleared</c> + "Retry the closure", which a permanent missing email made impossible to satisfy.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_AGranteeWithNoEmail_HasNothingToClear_AndTheClosureCompletes()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(NoEmailContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

        var body = OkBody(result);
        body.AccessRecordsRevoked.Should().Be(1);
        body.SpeContainerMembersRemoved.Should().Be(0);
        calls.Should().BeEmpty("no grantee has a key to match, so the container is not asked");
        table.ActiveRows.Should().BeEmpty();
    }

    /// <summary>
    /// #2103: a no-email grantee beside one with an email — the one with an email is still removed (by that email,
    /// and only that one), and the no-email grantee no longer fails the closure.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_ANoEmailGranteeBesideAnother_RemovesTheOtherAndCompletes()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(NoEmailContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

        OkBody(result).SpeContainerMembersRemoved.Should().Be(1);
        calls.Should().ContainSingle();
        calls[0].Emails.Should().BeEquivalentTo(new[] { ContactEmail });
    }

    /// <summary>
    /// The negative case #2103 must not widen: an email that could not be READ is transient, not "no email" — the
    /// grantee may hold a permission under it, so the closure still reports container_not_cleared.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_WhenAGranteesEmailCannotBeRead_StillReportsContainerNotCleared()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        client.Setup(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
                "contacts", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var confirmations = new List<bool>();
        var spe = SpeAnswering(_ => Removed, calls, confirmations: confirmations);

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

        Problem(result).ProblemDetails.Extensions["reasonCode"].Should()
            .Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        // #2104 round 2 (F1): no key could be matched, so the container is only asked whether ANY contact permission
        // remains — and this one does not say none does.
        calls.Should().ContainSingle().Which.Emails.Should().BeEmpty();
        confirmations.Should().Equal(true);
    }

    /// <summary>Makes every revoked organization larger than one closure may sweep.</summary>
    private static void ArrangeOversizedOrganization(Mock<DataverseWebApiClient> client) =>
        client.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                ExternalOrganizationMembership.EntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, ExternalOrganizationMembership.MaxMembersPerSweep + 1)
                .Select(_ => new ExternalOrganizationMembership.ContactOrganizationRow { ContactId = Guid.NewGuid() })
                .ToList());

    /// <summary>
    /// #2103 follow-up: a revoked organization too large to sweep is NON-retryable — a retry meets the same bound — so
    /// the closure names it (<c>organization_too_large</c>) and tells an administrator what to do, never "Retry the
    /// closure". Still fail-closed (500) and still reports the grants it revoked.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_WhenARevokedOrganizationIsTooLargeToSweep_IsANamedNonRetryableOutcome()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var problem = Problem(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureOrganizationTooLargeReason);
        ProjectClosureEndpoint.ClosureOrganizationTooLargeReason.Should().Be("sdap.closure.incomplete.organization_too_large");
        problem.ProblemDetails.Detail.Should().NotContain("Retry the closure")
            .And.Contain("will not change this")
            .And.Contain("administrator");
        problem.ProblemDetails.Extensions["accessRecordsRevoked"].Should().Be(1);
        calls.Should().ContainSingle("the container is still asked whether any contact permission remains (#2104 round 2, F1)")
            .Which.Emails.Should().BeEmpty("no member of the oversized organization was swept");
    }

    /// <summary>
    /// #2104 round 2 (F1): an organization of more than 200 members revoked EARLIER used to make every later closure of the
    /// project answer <c>organization_too_large</c> for good — the bound was checked before the container was read. When
    /// the container's complete read leaves no permission a contact grant could account for, every member is confirmed
    /// absent: the closure completes.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_OversizedOrganizationRevokedEarlier_ContainerHoldsNoContactPermission_Returns200()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId).StateCode = 1;
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var confirmations = new List<bool>();
        var body = OkBody(await CloseProject(client,
            spe: SpeAnswering(_ => Removed, calls, containerState: SpeContainerContactPermissionState.Clean, confirmations: confirmations),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        body.AccessRecordsRevoked.Should().Be(1);
        body.SpeContainerMembersRemoved.Should().Be(1, "the active contact grantee's permission was removed");
        calls.Should().ContainSingle().Which.Emails.Should().Equal(ContactEmail);
        confirmations.Should().Equal(new[] { true }, "the oversized organization can only be confirmed absent by the container");
    }

    /// <summary>
    /// #2104 round 2 (F1), the negative: the same earlier oversized organization, but the container still holds a
    /// permission a contact grant could account for — its members are not confirmed absent, so the closure still names
    /// the non-retryable <c>organization_too_large</c>.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_OversizedOrganizationRevokedEarlier_WithAContactPermissionLeft_StaysTooLarge()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId).StateCode = 1;
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var problem = Problem(await CloseProject(client,
            spe: SpeAnswering(_ => Removed, new(), containerState: SpeContainerContactPermissionState.ContactPermissionRemains),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureOrganizationTooLargeReason);
        problem.ProblemDetails.Extensions["organizationsTooLarge"].Should().Be(1);
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
    }

    /// <summary>
    /// #2104 round 2 (F1): the confirmation is a fact about the container, so it also covers grantees revoked earlier that
    /// could not be READ (a transient Dataverse fault): with no contact permission left, nobody can hold one.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_EarlierGrantsUnreadable_ButTheContainerHoldsNoContactPermission_Returns200()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.RevokedQueryFailure = new InvalidOperationException("Dataverse unavailable");
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var body = OkBody(await CloseProject(client,
            spe: SpeAnswering(_ => Removed, new(), containerState: SpeContainerContactPermissionState.Clean),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        body.SpeContainerMembersRemoved.Should().Be(1);
    }

    /// <summary>
    /// #2104 round 2 (F1): a closure whose grantees all resolved asks for no container-wide confirmation — the extra marker
    /// read is spent only where it can change the answer.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_WhenEveryGranteeResolved_DoesNotAskForTheContainerConfirmation()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var confirmations = new List<bool>();
        OkBody(await CloseProject(client, spe: SpeAnswering(_ => Removed, new(), confirmations: confirmations),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        confirmations.Should().Equal(false);
    }

    /// <summary>
    /// #2103 follow-up, mixed: an oversized organization AND a grantee whose removal failed transiently. A retry still
    /// cannot finish the closure, so the non-retryable code wins — and the detail counts the other grantee too.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_TooLargeOrganizationBesideATransientFailure_StaysNonRetryableAndCountsTheOther()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var spe = SpeAnswering(_ => GraphError, new());

        var problem = Problem(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureOrganizationTooLargeReason);
        problem.ProblemDetails.Detail.Should().Contain("1 other grantee(s) could also not be confirmed removed");
    }

    /// <summary>
    /// #2104 (pass-2 F4), corrected by round 4 (F1): the container removal THROWS after an organization was found too
    /// large to sweep. The throw means the container's own answer — the only thing that could confirm the oversized
    /// organization's members absent — was never established, and a retry may establish it. So the closure is the
    /// RETRYABLE container_not_cleared, never organization_too_large's "Retrying the closure will not change this" (which
    /// pass-2 F4 pinned here, and which was false). The counts survive: both grantees are still uncleared.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_WhenTheRemovalThrowsAfterATooLargeOrganization_IsRetryableNotCleared()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var spe = new Mock<SpeContainerMembershipService>(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);
        spe.Setup(s => s.RemoveMembershipsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SpeMembershipKey>>(), It.IsAny<DataverseWebApiClient>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph unreachable"));

        var problem = Problem(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        problem.ProblemDetails.Detail.Should().Contain("Retry the closure").And.NotContain("will not change");
        problem.ProblemDetails.Extensions["organizationsTooLarge"].Should().Be(0,
            "a retry may yet prove the oversized organization's members absent, so it is not reported as unfixable by one");
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(2,
            "the oversized organization and the contact whose removal threw");
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
    }

    /// <summary>
    /// #2104 round 4 (F1), the closure side of the revoke's rule: an oversized organization (or, below, a revoked history
    /// over the read bound) can be confirmed absent only by the container's own answer. When that answer is Unknown — a
    /// read, marker or internal-user lookup fault, or an incomplete read — the closure is the RETRYABLE
    /// container_not_cleared, never a non-retryable leaf saying a retry cannot help.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_OversizedOrganization_WhenTheContainerAnswerIsUnknown_IsRetryableNotCleared()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);
        var confirmations = new List<bool>();

        var problem = Problem(await CloseProject(client,
            spe: SpeAnswering(_ => Removed, new(), containerState: SpeContainerContactPermissionState.Unknown, confirmations: confirmations),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        problem.ProblemDetails.Detail.Should().Contain("Retry the closure").And.NotContain("will not change");
        problem.ProblemDetails.Extensions["organizationsTooLarge"].Should().Be(0);
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
        confirmations.Should().Equal(new[] { true }, "the container was asked, and could not answer");
    }

    /// <summary>#2104 round 4 (F1): the same rule for a revoked history over the read bound — an Unknown container answer is
    /// the retryable container_not_cleared, not too_many_grants.</summary>
    [Fact]
    public async Task CloseProject_SecureProject_RevokedGrantsOverTheBound_WhenTheContainerAnswerIsUnknown_IsRetryableNotCleared()
    {
        var table = new FakeGrantTable();
        for (var i = 0; i <= ProjectClosureEndpoint.MaxRevokedGrantsPerSweep; i++)
        {
            table.SeedContactGrant(ContactId, ProjectId).StateCode = 1;
        }

        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var problem = Problem(await CloseProject(client,
            spe: SpeAnswering(_ => Removed, new(), containerState: SpeContainerContactPermissionState.Unknown),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        problem.ProblemDetails.Detail.Should().Contain("Retry the closure").And.NotContain("will not change");
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // #2104 round 3 (item 1): an ambiguous or unverifiable match is NON-retryable — permission_ambiguous.
    // ─────────────────────────────────────────────────────────────────────────────

    private static SpeContainerMembershipResult Ambiguous(string email) =>
        new(false, null, $"{SpeContainerMembershipService.AmbiguousMatchError}: 2 permissions on the container match user '{email}'.");

    private static SpeContainerMembershipResult Unverifiable(string email) =>
        new(false, null, $"{SpeContainerMembershipService.UnverifiableHolderError}: the permission matching user '{email}' names no object id.");

    /// <summary>
    /// #2104 round 3: a revoked grantee's match was ambiguous (two eligible permissions) or unverifiable (no holder object
    /// id), so nothing was removed for them — and a retry reads the same permissions and refuses again. The closure names
    /// it (permission_ambiguous), tells an administrator to resolve the container's permissions in the SPE admin app,
    /// never says "Retry the closure", and names no person or address. Still fail-closed: a 500, grants revoked.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseProject_SecureProject_AnAmbiguousOrUnverifiableMatch_IsANamedNonRetryableOutcome(bool unverifiable)
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var spe = SpeAnswering(email => unverifiable ? Unverifiable(email) : Ambiguous(email), new());

        var problem = Problem(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError, "still fail-closed — never a 200");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePermissionAmbiguousReason);
        ProjectClosureEndpoint.ClosurePermissionAmbiguousReason.Should()
            .Be("sdap.closure.incomplete.permission_ambiguous", "a stable code clients switch on");
        problem.ProblemDetails.Detail.Should().NotContain("Retry the closure")
            .And.Contain("1 revoked grantee(s) match more than one permission")
            .And.Contain("Retrying the closure will not change this")
            .And.Contain("administrator must resolve the container's permissions in the SPE admin app")
            .And.NotContain(ContactEmail, "the detail reaches the caller's UI and names no PII")
            .And.NotContain(ContactId.ToString());
        problem.ProblemDetails.Extensions["accessRecordsRevoked"].Should().Be(1);
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(1);
        table.ActiveRows.Should().BeEmpty();
    }

    /// <summary>
    /// #2104 round 3, mixed causes: one grantee ambiguous, another's delete failed transiently. A retry could clear only
    /// the transient one, so the non-retryable code wins (the rule organization_too_large already follows) and the detail
    /// counts the other grantee.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_AnAmbiguousMatchBesideATransientFailure_StaysNonRetryableAndCountsTheOther()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var spe = SpeAnswering(email => email == ContactEmail ? Ambiguous(email) : GraphError, new());

        var problem = Problem(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePermissionAmbiguousReason);
        problem.ProblemDetails.Detail.Should().Contain("1 revoked grantee(s) match more than one permission")
            .And.Contain("1 other grantee(s) could also not be confirmed removed")
            .And.NotContain(OtherContactEmail);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(2);
    }

    /// <summary>
    /// #2104 round 3, the negative: a transient failure alone (the internal-user lookup faulted) is not an ambiguity — a
    /// retry can succeed — so it stays the retryable container_not_cleared.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_AnInternalUserCheckFailure_StaysTheRetryableContainerNotCleared()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var spe = SpeAnswering(_ => new SpeContainerMembershipResult(
            false, null, $"{SpeContainerMembershipService.InternalUserCheckFailedError}: could not be checked."), new());

        var problem = Problem(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        problem.ProblemDetails.Detail.Should().Contain("Retry the closure");
    }

    /// <summary>
    /// #2104 round 3, two non-retryable causes: an oversized organization AND an ambiguous match. The earlier leaf
    /// (organization_too_large) keeps the code, and its detail names the ambiguous match too — whichever code wins, every
    /// non-retryable cause reaches the administrator.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_TooLargeOrganizationAndAnAmbiguousMatch_NamesBoth()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var problem = Problem(await CloseProject(client, spe: SpeAnswering(Ambiguous, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureOrganizationTooLargeReason);
        problem.ProblemDetails.Detail.Should().Contain("1 revoked organization(s) have more than")
            .And.Contain("1 revoked grantee(s) match more than one permission")
            .And.NotContain("other grantee(s) could also not be confirmed removed",
                "both unresolved grantees are named by their own non-retryable cause");
    }

    /// <summary>
    /// #2104 round 3: a partial revocation keeps its own (retryable) code — rows remain ACTIVE and a retry does help those
    /// — but its detail carries the ambiguous match's non-retryable instruction for that part.
    /// </summary>
    [Fact]
    public async Task CloseProject_PartialRevocationWithAnAmbiguousMatch_CarriesTheNonRetryableInstruction()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var stubborn = table.SeedContactGrant(OtherContactId, ProjectId);
        table.FailDeactivationFor.Add(stubborn.Id);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var problem = Problem(await CloseProject(client,
            spe: SpeAnswering(email => email == ContactEmail ? Ambiguous(email) : Removed, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        problem.ProblemDetails.Detail.Should().Contain("1 revoked grantee(s) match more than one permission")
            .And.Contain("Retrying the closure will not change that part");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // #2104 (pass-1 F1 / pass-2 F3): "Retry the closure" must be TRUE. A retry finds the grants already inactive, so
    // the container step must still run over the grantees revoked earlier.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The retry that works: the first closure revokes the grant but cannot clear the container (500
    /// container_not_cleared, "Retry the closure"); the retry finds no ACTIVE grant, still sweeps the grantee revoked
    /// earlier, and — now that the container answers — completes with 200.
    /// </summary>
    [Fact]
    public async Task CloseProject_RetryAfterContainerNotCleared_ClearsTheEarlierRevokedGranteeAndAnswers200()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        var resolver = TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer);

        var containerUp = false;
        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => containerUp ? Removed : GraphError, calls);

        var first = Problem(await CloseProject(client, spe: spe, resolver: resolver));
        first.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        first.ProblemDetails.Detail.Should().Contain("Retry the closure");
        table.ActiveRows.Should().BeEmpty("precondition: the grant was revoked by the first call");

        containerUp = true;
        var retry = OkBody(await CloseProject(client, spe: spe, resolver: resolver));

        retry.AccessRecordsRevoked.Should().Be(0, "nothing was active any more");
        retry.SpeContainerMembersRemoved.Should().Be(1, "the earlier revoked grantee was removed by the retry");
        calls.Should().HaveCount(2, "the retry swept the container again — it did not answer 200 without looking");
        calls[1].ContainerId.Should().Be(OwnContainer);
        calls[1].Emails.Should().BeEquivalentTo(new[] { ContactEmail });
    }

    /// <summary>
    /// The retry that still fails: with no ACTIVE grant left, a container that still cannot be cleared is still a
    /// 500 container_not_cleared — never the false 200 a retry used to return.
    /// </summary>
    [Fact]
    public async Task CloseProject_RetryWhenTheContainerStillCannotBeCleared_StaysIncomplete()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        var resolver = TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer);
        var spe = SpeAnswering(_ => GraphError, new());

        Problem(await CloseProject(client, spe: spe, resolver: resolver));
        var retry = Problem(await CloseProject(client, spe: spe, resolver: resolver));

        retry.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        retry.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        retry.ProblemDetails.Extensions["accessRecordsRevoked"].Should().Be(0);
        retry.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(1);
        retry.ProblemDetails.Detail.Should().Contain("had no active external grants left to revoke");
    }

    /// <summary>
    /// #2104: a grantee revoked EARLIER by a single-grant revoke (whose own container step may have failed) is cleared
    /// by the closure too, beside the grantees this call revokes — in the one sweep.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_AlsoClearsGranteesRevokedBeforeTheClosure()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId).StateCode = 1;
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var body = OkBody(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        body.AccessRecordsRevoked.Should().Be(1);
        body.AffectedContactIds.Should().Equal(ContactId);
        calls.Should().ContainSingle();
        calls[0].Emails.Should().BeEquivalentTo(new[] { ContactEmail, OtherContactEmail });
        table.Tops.Should().Contain(ProjectClosureEndpoint.MaxRevokedGrantsPerSweep + 1,
            "the revoked-rows read is bounded, so a truncated page can never read as a complete one");
    }

    /// <summary>
    /// #2104: the earlier revoked grants could not be read — whoever they are, they are not confirmed off the
    /// container, so the closure is not complete even though every ACTIVE grantee was cleared.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_WhenTheEarlierRevokedGrantsCannotBeRead_ReportsContainerNotCleared()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.RevokedQueryFailure = new InvalidOperationException("Dataverse unavailable");
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var problem = Problem(await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().BeNull("the earlier grantees could not be counted");
        calls.Should().ContainSingle("the active grantee was still swept").Which.Emails.Should().Equal(ContactEmail);
    }

    /// <summary>
    /// #2104: a project with more revoked grants than one closure reads is not swept on a partial list — the read
    /// reports itself incomplete and the closure is not complete. #2104 round 2 (F2): a retry reads the same rows and
    /// meets the same bound, so it is the NAMED, non-retryable <c>too_many_grants</c> with an administrator instruction,
    /// never <c>container_not_cleared</c>'s "Retry the closure".
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_WhenTheRevokedGrantsExceedTheReadBound_IsANamedNonRetryableOutcome()
    {
        var table = new FakeGrantTable();
        for (var i = 0; i <= ProjectClosureEndpoint.MaxRevokedGrantsPerSweep; i++)
        {
            table.SeedContactGrant(ContactId, ProjectId).StateCode = 1;
        }

        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var confirmations = new List<bool>();

        var problem = Problem(await CloseProject(client, spe: SpeAnswering(_ => Removed, calls, confirmations: confirmations),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureTooManyGrantsReason);
        ProjectClosureEndpoint.ClosureTooManyGrantsReason.Should().Be("sdap.closure.incomplete.too_many_grants");
        problem.ProblemDetails.Detail.Should().Be(
            $"Project {ProjectId} had no active external grants left to revoke, but the project has more than "
            + $"{ProjectClosureEndpoint.MaxRevokedGrantsPerSweep} revoked external grants, more than one closure may read, "
            + "and its SPE container could not be confirmed to hold no permission of theirs, so those grantees may retain "
            + "file access. Retrying the closure will not change this. An administrator must remove those grantees' "
            + "permissions from the project's container in the SPE admin app (the container's Permissions).",
            "round 4: the sentence continues the clause's \"but\" in lower case — not \"…, but The project has…\"");
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().BeNull("the revoked grantees could not be counted");
        calls.Should().ContainSingle("the truncated list is not swept; the container is only asked whether a contact "
            + "permission remains").Which.Emails.Should().BeEmpty();
        confirmations.Should().Equal(true);
    }

    /// <summary>
    /// #2104 round 2 (F1 x F2): the same over-bound revoked history, but the container's complete read leaves no
    /// permission a contact grant could account for — so every revoked grantee, whoever they are, is confirmed absent and
    /// the closure completes.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_RevokedGrantsOverTheBound_ButTheContainerHoldsNoContactPermission_Returns200()
    {
        var table = new FakeGrantTable();
        for (var i = 0; i <= ProjectClosureEndpoint.MaxRevokedGrantsPerSweep; i++)
        {
            table.SeedContactGrant(ContactId, ProjectId).StateCode = 1;
        }

        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var body = OkBody(await CloseProject(client,
            spe: SpeAnswering(_ => Removed, new(), containerState: SpeContainerContactPermissionState.Clean),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        body.AccessRecordsRevoked.Should().Be(0);
    }

    /// <summary>
    /// #2104 round 2 (F2): the ACTIVE grants are read as one bounded page ($top = bound + 1). A project with more is
    /// never revoked on a truncated list: nothing is deactivated, the container is not touched, and the answer is the
    /// named, non-retryable <c>too_many_grants</c> — a retry reads the same rows.
    /// </summary>
    [Fact]
    public async Task CloseProject_WhenTheActiveGrantsExceedTheReadBound_RevokesNothingAndIsANamedNonRetryableOutcome()
    {
        var table = new FakeGrantTable();
        for (var i = 0; i <= ProjectClosureEndpoint.MaxActiveGrantsPerClosure; i++)
        {
            table.SeedContactGrant(ContactId, ProjectId);
        }

        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();

        var problem = Problem(await CloseProject(client, spe: SpeAnswering(_ => Removed, calls),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureTooManyGrantsReason);
        problem.ProblemDetails.Extensions["accessRecordsRevoked"].Should().Be(0);
        problem.ProblemDetails.Detail.Should().NotContain("Retry the closure")
            .And.Contain("No grants were revoked")
            .And.Contain("will not change that")
            .And.Contain("administrator");
        table.Tops.Should().StartWith(ProjectClosureEndpoint.MaxActiveGrantsPerClosure + 1,
            "the active-rows read is bounded, so a truncated page can never read as the whole set");
        table.ActiveRows.Should().HaveCount(ProjectClosureEndpoint.MaxActiveGrantsPerClosure + 1,
            "nothing is revoked on a list known to be truncated");
        calls.Should().BeEmpty("the container step never runs");
    }

    /// <summary>#2104 round 2 (F2): exactly the bound is still one complete page, and closes normally.</summary>
    [Fact]
    public async Task CloseProject_WithExactlyTheBoundOfActiveGrants_ClosesNormally()
    {
        var table = new FakeGrantTable();
        for (var i = 0; i < ProjectClosureEndpoint.MaxActiveGrantsPerClosure; i++)
        {
            table.SeedContactGrant(ContactId, ProjectId);
        }

        var body = OkBody(await CloseProject(table.BuildMock()));

        body.AccessRecordsRevoked.Should().Be(ProjectClosureEndpoint.MaxActiveGrantsPerClosure);
        table.ActiveRows.Should().BeEmpty();
    }

    /// <summary>
    /// #2104 round 2 (F2): a partial revocation whose container step also met the revoked-rows bound carries that
    /// part's non-retryable instruction, beside the retry the stuck row needs.
    /// </summary>
    [Fact]
    public async Task CloseProject_PartialRevocationWithTooManyRevokedGrants_CarriesTheNonRetryableInstruction()
    {
        var table = new FakeGrantTable();
        var stubborn = table.SeedContactGrant(ContactId, ProjectId);
        table.FailDeactivationFor.Add(stubborn.Id);
        for (var i = 0; i <= ProjectClosureEndpoint.MaxRevokedGrantsPerSweep; i++)
        {
            table.SeedContactGrant(OtherContactId, ProjectId).StateCode = 1;
        }

        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var problem = Problem(await CloseProject(client, spe: SpeAnswering(_ => Removed, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
        problem.ProblemDetails.Detail.Should().Contain("Retry the closure")
            .And.Contain($"more than {ProjectClosureEndpoint.MaxRevokedGrantsPerSweep} revoked external grants")
            .And.Contain("will not change that part")
            .And.Contain("administrator");
    }

    /// <summary>
    /// #2104 round 2 (F1): a partial revocation whose container is proven clean reports it cleared with nothing left
    /// over: no oversized organization and no uncleared grantee beside <c>containerCleared: true</c>, which would
    /// contradict it.
    /// </summary>
    [Fact]
    public async Task CloseProject_PartialRevocation_OversizedOrganizationButTheContainerProvenClean_ReportsNothingUncleared()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId).StateCode = 1;
        var stubborn = table.SeedContactGrant(ContactId, ProjectId);
        table.FailDeactivationFor.Add(stubborn.Id);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var problem = Problem(await CloseProject(client,
            spe: SpeAnswering(_ => Removed, new(), containerState: SpeContainerContactPermissionState.Clean),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(true);
        problem.ProblemDetails.Extensions["organizationsTooLarge"].Should().Be(0);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(0);
        problem.ProblemDetails.Detail.Should().Contain("confirmed removed");
    }

    /// <summary>
    /// #2104: a revoked project whose container cannot be determined, retried: the grantees revoked earlier may still
    /// be on it, so the retry is not a 200. A project that never had a grant has nobody to clear, wherever its container
    /// is: 200.
    /// </summary>
    [Fact]
    public async Task CloseProject_NoActiveGrants_ContainerUndetermined_IsIncompleteOnlyWhenGranteesWereRevokedEarlier()
    {
        var undetermined = TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, ownContainerId: null);

        var revokedTable = new FakeGrantTable();
        revokedTable.SeedContactGrant(ContactId, ProjectId).StateCode = 1;
        var withHistory = await CloseProject(revokedTable.BuildMock(), spe: SpeAnswering(_ => Removed, new()),
            resolver: undetermined);

        Problem(withHistory).ProblemDetails.Extensions["reasonCode"].Should()
            .Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);

        var neverGranted = await CloseProject(new FakeGrantTable().BuildMock(), spe: SpeAnswering(_ => Removed, new()),
            resolver: undetermined);

        OkBody(neverGranted).AccessRecordsRevoked.Should().Be(0);
    }

    /// <summary>
    /// #2104: the same organization revoked earlier AND revoked by this call is swept — and counted — once. Two copies
    /// would query its members twice and report two oversized organizations where there is one.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_AnOrganizationRevokedEarlierAndNow_IsSweptOnce()
    {
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId).StateCode = 1;
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var problem = Problem(await CloseProject(client, spe: SpeAnswering(_ => Removed, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureOrganizationTooLargeReason);
        problem.ProblemDetails.Extensions["organizationsTooLarge"].Should().Be(1);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(1);
    }

    /// <summary>
    /// #2104: a revoked row naming neither a contact nor an organization (its grantee's record was deleted, clearing the
    /// lookup) identifies nobody, so no container permission can be attributed to it — the #2103 "no key" answer, not
    /// an unresolved grantee that would fail every closure of the project for good.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProject_ARevokedGrantNamingNobody_IsNotAnUnresolvedGrantee()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(ContactId, ProjectId).StateCode = 1;
        var orphan = table.SeedContactGrant(ContactId, ProjectId);
        orphan.ContactId = null;
        orphan.StateCode = 1;
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var body = OkBody(await CloseProject(client, spe: SpeAnswering(_ => Removed, calls),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        body.AccessRecordsRevoked.Should().Be(1);
        calls.Should().ContainSingle().Which.Emails.Should().Equal(ContactEmail);
    }

    /// <summary>
    /// #2104: a SECURE project that never had a grant closes cleanly — the revoked-rows read finds nobody, so the
    /// container is not asked.
    /// </summary>
    [Fact]
    public async Task CloseProject_SecureProjectWithNoGrantsEver_Returns200AndNeverAsksTheContainer()
    {
        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();

        var result = await CloseProject(new FakeGrantTable().BuildMock(), spe: SpeAnswering(_ => Removed, calls),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

        OkBody(result).AccessRecordsRevoked.Should().Be(0);
        calls.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // #2104 (pass-2 F2): a partial revocation must not swallow what the container step found.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A row could not be deactivated (partial_revocation, retryable) AND a revoked organization is too large to sweep
    /// (not retryable). The partial answer carries the container step's counts and the oversized organization's
    /// administrator instruction — a retry fixes the row, not the organization.
    /// </summary>
    [Fact]
    public async Task CloseProject_PartialRevocationWithATooLargeOrganization_CarriesTheNonRetryableInstruction()
    {
        var table = new FakeGrantTable();
        var stubborn = table.SeedContactGrant(ContactId, ProjectId);
        table.SeedOrganizationGrant(OrganizationId, ProjectId);
        table.FailDeactivationFor.Add(stubborn.Id);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        ArrangeOversizedOrganization(client);

        var problem = Problem(await CloseProject(client, spe: SpeAnswering(_ => Removed, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
        problem.ProblemDetails.Extensions["organizationsTooLarge"].Should().Be(1);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(1, "the oversized organization; the contact was removed");
        problem.ProblemDetails.Detail.Should().Contain("Retry the closure")
            .And.Contain("will not change that part")
            .And.Contain("administrator");
    }

    /// <summary>
    /// A row could not be deactivated AND a revoked grantee could not be cleared from the container: the partial answer
    /// says so, with the count, instead of implying the container is fine.
    /// </summary>
    [Fact]
    public async Task CloseProject_PartialRevocationWithAGranteeNotCleared_CarriesTheContainerState()
    {
        var table = new FakeGrantTable();
        var stubborn = table.SeedContactGrant(ContactId, ProjectId);
        table.SeedContactGrant(OtherContactId, ProjectId);
        table.FailDeactivationFor.Add(stubborn.Id);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var problem = Problem(await CloseProject(client,
            spe: SpeAnswering(email => email == OtherContactEmail ? GraphError : Removed, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        problem.ProblemDetails.Extensions["containerCleared"].Should().Be(false);
        problem.ProblemDetails.Extensions["granteesNotCleared"].Should().Be(1);
        problem.ProblemDetails.Extensions["organizationsTooLarge"].Should().Be(0);
        problem.ProblemDetails.Detail.Should().Contain("could not be cleared of 1 revoked grantee(s)");
    }

    /// <summary>
    /// The partial answer on a secure project whose container WAS cleared says so (true), and on a non-secure project —
    /// which owns no container — reports the step as not applicable (null), never a "cleared" it did not perform.
    /// </summary>
    [Fact]
    public async Task CloseProject_PartialRevocation_ReportsTheContainerAsClearedOrNotApplicable()
    {
        var secureTable = new FakeGrantTable();
        var stubborn = secureTable.SeedContactGrant(ContactId, ProjectId);
        secureTable.FailDeactivationFor.Add(stubborn.Id);
        var secureClient = secureTable.BuildMock();
        ArrangeGranteeIdentities(secureClient);

        var secure = Problem(await CloseProject(secureClient, spe: SpeAnswering(_ => Removed, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer)));
        secure.ProblemDetails.Extensions["containerCleared"].Should().Be(true);
        secure.ProblemDetails.Detail.Should().Contain("confirmed removed");

        var plainTable = new FakeGrantTable();
        var plainStubborn = plainTable.SeedContactGrant(ContactId, ProjectId);
        plainTable.FailDeactivationFor.Add(plainStubborn.Id);

        var plain = Problem(await CloseProject(plainTable.BuildMock()));
        plain.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosurePartialRevocationReason);
        plain.ProblemDetails.Extensions["containerCleared"].Should().BeNull();
    }

    /// <summary>
    /// F8 (S-39): the client's <c>containerId</c> is GONE from the contract. A request body that still carries one —
    /// another record's container — deserializes (unknown members are skipped) and is IGNORED: the only container the
    /// sweep ever sees is the one the server derived from the authorized project.
    /// </summary>
    [Fact]
    public async Task CloseProject_AClientSuppliedContainerIdIsIgnored_TheDerivedContainerIsTheOnlyOneTouched()
    {
        const string victimContainer = "b!some-other-matters-container";
        var request = System.Text.Json.JsonSerializer.Deserialize<CloseProjectRequest>(
            $"{{\"projectId\":\"{ProjectId}\",\"containerId\":\"{victimContainer}\"}}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        request.Should().NotBeNull();
        request!.ProjectId.Should().Be(ProjectId);

        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        await ProjectClosureEndpoint.Handle(
            request, client.Object, spe.Object,
            GrantPolicyTestDoubles.RealInvalidationOver(Mock.Of<ITenantCache>(), AuthenticatedContext()),
            TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer),
            AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None);

        calls.Select(c => c.ContainerId).Should().Equal(OwnContainer);
        calls.Select(c => c.ContainerId).Should().NotContain(victimContainer);
    }

    /// <summary>
    /// A NON-secure project's derived container is the SHARED business-unit container. Clearing grantees from it
    /// could strip access other records rely on, so the container step is SKIPPED — never called with the shared
    /// container — and the closure still reports 200 (the grants were revoked).
    /// </summary>
    [Fact]
    public async Task CloseProject_NonSecureProject_NeverTouchesTheSharedBusinessUnitContainer()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForNonSecureRecord("sprk_project", ProjectId));

        OkBody(result).AccessRecordsRevoked.Should().Be(1);
        calls.Should().BeEmpty("a non-secure project's container is the shared business-unit container");
    }

    /// <summary>
    /// Task 166 r1 (verifier item 8): a NON-secure project filed under a SECURE matter. The resolver's CONTENT answer
    /// for it is the matter's own container (task 155), and task 166 swept that — removing the revoked grantees from
    /// the MATTER's container, where a grant on the matter may still entitle them. The closure now cleans only a
    /// container the project ITSELF owns, so here it touches none and still revokes every grant.
    /// </summary>
    [Fact]
    public async Task CloseProject_ANonSecureProjectUnderASecureMatter_NeverTouchesTheMattersContainer()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var result = await CloseProject(client, spe: spe,
            resolver: TestRecordContainerResolver.ForNonSecureRecordUnderSecureMatter(
                "sprk_project", ProjectId, Guid.Parse("66666666-1660-4000-8000-000000000001")));

        OkBody(result).AccessRecordsRevoked.Should().Be(1);
        calls.Should().BeEmpty("the secure matter's container belongs to the matter, not to this project");
        table.ActiveRows.Should().BeEmpty();
    }

    /// <summary>
    /// "We could not tell which container" is not "nothing to clean": a SECURE project with no container
    /// (the resolver's FailClosed refusal) and a resolver fault both report container_not_cleared — and the grant
    /// deactivation still ran.
    /// </summary>
    [Theory]
    [InlineData("secure-without-container")]
    [InlineData("resolver-problem")]
    [InlineData("resolver-fault")]
    [InlineData("secure-flag-absent")] // task 166 r1: an unreadable flag is never read as "not secure"
    public async Task CloseProject_WhenTheContainerCannotBeDetermined_ReportsIncompleteAndStillRevokes(string shape)
    {
        var resolver = shape switch
        {
            "secure-without-container" => TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, ownContainerId: null),
            "secure-flag-absent" => TestRecordContainerResolver.ForRecordWithNoSecureFlag("sprk_project", ProjectId),
            "resolver-problem" => TestRecordContainerResolver.Throwing(new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
                "container_ownership_indeterminate", "Indeterminate", "test", 409)),
            _ => TestRecordContainerResolver.Throwing(new TimeoutException("metadata timed out")),
        };

        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);

        var calls = new List<(string ContainerId, IReadOnlyCollection<string> Emails)>();
        var spe = SpeAnswering(_ => Removed, calls);

        var result = await CloseProject(client, spe: spe, resolver: resolver);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ProjectClosureEndpoint.ClosureContainerNotClearedReason);
        calls.Should().BeEmpty("no container was established, so none may be swept");
        table.ActiveRows.Should().BeEmpty("the Dataverse grant deactivation still ran");
    }

    /// <summary>
    /// Cache invalidation runs even when the container step runs — that step only ever removes access, so it must
    /// not be skipped or reordered behind the SPE call.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithAContainerStep_StillInvalidatesContactCaches()
    {
        var table = new FakeGrantTable();
        table.SeedContactGrant(ContactId, ProjectId);
        var client = table.BuildMock();
        ArrangeGranteeIdentities(client);
        var cache = new Mock<ITenantCache>();

        await CloseProject(client, cache.Object, spe: SpeAnswering(_ => Removed, new()),
            resolver: TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, OwnContainer));

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
    /// Task 137 (C5): closing a project with an ORGANIZATION grant clears every ACTIVE member's cached grant set —
    /// members used to wait out the 60-second TTL because an organization grant names no contact. 205 members, past
    /// the revoke path's 200-member bound, are paged to completion.
    /// </summary>
    [Fact]
    public async Task CloseProject_WithAnOrganizationGrant_InvalidatesEveryActiveMember()
    {
        var organizationId = Guid.Parse("0a0a0a0a-0000-0000-0000-0000000000f1");
        var table = new FakeGrantTable();
        table.SeedOrganizationGrant(organizationId, ProjectId);
        var client = table.BuildMock();
        var cache = new Mock<ITenantCache>();
        var members = Enumerable.Range(1, 205).Select(i => Guid.Parse($"dddddddd-0000-0000-0000-{i:D12}")).ToArray();
        var participations = GrantPolicyTestDoubles.RealInvalidationOver(cache.Object, AuthenticatedContext());
        participations.PageSize = 100;
        participations.Members[organizationId] = members;

        await CloseProject(client, participations: participations);

        foreach (var member in members)
        {
            cache.Verify(
                c => c.RemoveAsync(
                    TenantId, ExternalParticipationService.ExternalAccessResource,
                    member.ToString(), ExternalParticipationService.CacheVersion,
                    It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
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

    /// <summary>#2104: the revoked-rows read is the same project lookup, inactive rows only.</summary>
    [Fact]
    public void BuildRevokedProjectGrantsFilter_ScopesToTheProjectAndInactiveRowsOnly()
    {
        ProjectClosureEndpoint.BuildRevokedProjectGrantsFilter(ProjectId)
            .Should().Be($"_sprk_project_value eq {ProjectId} and statecode eq 1");
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
