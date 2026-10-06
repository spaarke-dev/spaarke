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

    /// <summary>The membership seam: answers RemoveMembershipsAsync per email from <paramref name="answer"/> and
    /// records every (container, emails) it was called with.</summary>
    private static Mock<SpeContainerMembershipService> SpeAnswering(
        Func<string, SpeContainerMembershipResult> answer,
        List<(string ContainerId, IReadOnlyCollection<string> Emails)> calls)
    {
        var spe = new Mock<SpeContainerMembershipService>(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);
        spe.Setup(s => s.RemoveMembershipsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string containerId, IReadOnlyCollection<string> emails, CancellationToken _) =>
            {
                calls.Add((containerId, emails.ToList()));
                var results = new Dictionary<string, SpeContainerMembershipResult>(StringComparer.OrdinalIgnoreCase);
                foreach (var email in emails)
                {
                    results[email] = answer(email);
                }

                return (IReadOnlyDictionary<string, SpeContainerMembershipResult>)results;
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
    // grantees this closure revoked are removed — through the same email-keyed RemoveMembershipsAsync the
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
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
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
