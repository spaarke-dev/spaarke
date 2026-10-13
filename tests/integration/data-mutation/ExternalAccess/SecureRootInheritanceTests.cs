using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 158 (owner round 6, 2026-10-02: "a work assignment (or project) filed under a SECURE matter
/// or project is itself secure — yes") — the TRANSITION, the SAFETY NET and the way BACK, driven through the REAL routes
/// (<c>/provision-project</c>, <c>/unsecure-project</c>), the REAL <see cref="SecureRootInheritance"/>, the REAL
/// <see cref="SecureRootInheritanceJob"/>, the REAL secure-child reconciler and share synchronizer, over the provisioning
/// fixture's Dataverse and the <see cref="SecureChildShareWorld"/> it keeps in step with every record it moves.
/// </summary>
/// <remarks>
/// <para>"Secure" is asserted on the four facts provisioning establishes — <c>sprk_issecure</c>, the NAMED owner team, the
/// record's OWN container and the creator's share — never on a log line. A contact granted on such a record then gets what
/// FR-22 gives a secure root, direct grants only: FR-22 reads the record's own flag (ExternalParticipationService), and its
/// suppression is pinned for a secure WORK ASSIGNMENT root by <c>UnifiedEvaluatorSeamTests</c> criterion 4 (the
/// <c>RootEntityTypes</c> theory) — no new code, so no new evaluator test.</para>
/// <para>The writers (create and re-file through each BFF path) are <see cref="SecureRootInheritanceWriterTests"/>.</para>
/// </remarks>
[Trait("status", "task-158-uac-r2")]
public class SecureRootInheritanceTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";

    internal static readonly Guid Creator = ProvisionProjectTestFixture.CallerSystemUserId;
    internal static readonly Guid SecureTeam = ProvisionProjectTestFixture.SecureOwnerTeamId;
    internal static readonly Guid Colleague = Guid.Parse("b0000000-0000-0000-0000-0000000158b2");
    internal static readonly Guid Outsider = Guid.Parse("e0000000-0000-0000-0000-0000000158e5");
    internal static readonly Guid AppUser = Guid.Parse("a0000000-0000-0000-0000-0000000158a9");

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureRootInheritanceTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _fixture.SystemUsers[Outsider] = (false, false);
        _fixture.SystemUsers[AppUser] = (false, true);
        _job = new SecureRootInheritanceJobRunner(_fixture);
    }

    private SecureChildShareWorld World => _fixture.ChildWorld;

    private static int Mask(string rightsCsv) => RecordShareLevels.MaskForRightsCsv(rightsCsv);

    private static int MirrorOf(string rightsCsv) => RecordShareLevels.ChildMirrorMask(Mask(rightsCsv));

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    // ── The world ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A matter already provisioned: flagged, owned by the named team, its own container, shared to its creator.</summary>
    internal static void SecureMatter(ProvisionProjectTestFixture fixture, Guid id, string? name = null, params Guid[] sharees)
    {
        fixture.SeedMatter(id, owningTeamId: SecureTeam, containerId: $"b!matter-{id:N}", isSecure: true);
        fixture.SeedShare(id, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        foreach (var sharee in sharees)
            fixture.SeedShare(id, DataversePrincipalRef.User(sharee), ProvisionProjectEndpoint.CollaboratorAccessRights);
        if (name is not null)
            fixture.ChildWorld.Set("sprk_matter", id, "sprk_mattername", name);
    }

    /// <summary>A project already provisioned, as <see cref="SecureMatter"/>.</summary>
    internal static void SecureProject(ProvisionProjectTestFixture fixture, Guid id, params Guid[] sharees)
    {
        fixture.SeedProject(id, owningTeamId: SecureTeam, containerId: $"b!project-{id:N}", isSecure: true);
        fixture.SeedShare(id, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        foreach (var sharee in sharees)
            fixture.SeedShare(id, DataversePrincipalRef.User(sharee), ProvisionProjectEndpoint.CollaboratorAccessRights);
    }

    /// <summary>An ordinary work assignment (owned by its creator, not flagged) filed under a parent by a typed lookup.</summary>
    internal static void FiledWorkAssignment(
        ProvisionProjectTestFixture fixture, Guid id, string column, string parentTable, Guid parentId,
        Guid? createdBy = null, Guid? createdByPerson = null, string? name = null)
    {
        fixture.SeedWorkAssignment(id, isSecure: false, createdBy: createdBy, createdByPerson: createdByPerson);
        fixture.ChildWorld.Set("sprk_workassignment", id, column, new EntityReference(parentTable, parentId));
        if (name is not null)
            fixture.ChildWorld.Set("sprk_workassignment", id, "sprk_name", name);
    }

    /// <summary>
    /// Task 175 (round 87): the access record task 158 now writes when it secures a record by inheritance — its Secure comes
    /// from <paramref name="parents"/> (inherited, not its own), so it follows them out when they are un-secured. A record
    /// without one is never loosened (the backfill rule).
    /// </summary>
    internal static void InheritedAccessRecord(
        ProvisionProjectTestFixture fixture, string table, Guid id, params (string Table, Guid Id)[] parents) =>
        fixture.ChildWorld.Set(table, id, AccessInheritance.Column, new AccessInheritance(
            parents.Select(p => $"{p.Table}:{p.Id:D}").ToList(), true, Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.Standard,
            false, null).Serialize());

    /// <summary>A <c>sprk_recordtype_ref</c> row standing for <paramref name="logicalName"/>.</summary>
    internal static Guid RecordTypeRef(ProvisionProjectTestFixture fixture, string logicalName)
    {
        var id = Guid.NewGuid();
        fixture.ChildWorld.Add("sprk_recordtype_ref", id, ("sprk_recordlogicalname", logicalName));
        return id;
    }

    /// <summary>Sets a row's polymorphic pair: the text id (in <paramref name="format"/>) and the type.</summary>
    internal static void FilePair(ProvisionProjectTestFixture fixture, string table, Guid id, Guid parentId, Guid typeRef, string format = "D")
    {
        fixture.ChildWorld.Set(table, id, "sprk_regardingrecordid", parentId.ToString(format));
        fixture.ChildWorld.Set(table, id, "sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", typeRef));
    }

    /// <summary>The four facts provisioning establishes, read back from the fixture's Dataverse.</summary>
    private void ShouldBeSecure(Guid id, string because)
    {
        _fixture.IsSecureOf(id).Should().BeTrue($"{because}: sprk_issecure");
        _fixture.OwningTeamOf(id).Should().Be(SecureTeam, $"{because}: the named owner team");
        _fixture.ContainerIdOf(id).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId, $"{because}: its own container");
        _fixture.ShareMaskOf(id, Creator).Should().Be(Mask(ProvisionProjectEndpoint.CreatorAccessRights),
            $"{because}: the person who created it is shared (S5: someone can always open it)");
        _fixture.SomeoneCanOpen(id).Should().BeTrue();
    }

    private void ShouldBeUntouched(Guid id, string because)
    {
        _fixture.IsSecureOf(id).Should().BeFalse(because);
        _fixture.OwningTeamOf(id).Should().BeNull(because);
        _fixture.OwningUserOf(id).Should().Be(Creator, because);
        _fixture.Updates.Should().NotContain(u => u.RecordId == id, because);
        _fixture.SharesOn(id).Should().BeEmpty(because);
    }

    // ── (3) A parent BECOMING secure: the transition ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC 3, through the real route: provisioning a matter secures every work assignment filed under it by its TYPED lookup
    /// and every project filed under it by the PAIR (its id in the brace format a writer may store), each through
    /// provisioning's own steps, and gives each the matter's sharee (task 149's mirror: no Share). The decoys — a work
    /// assignment under an ordinary matter, one whose pair names an EVENT, and a project whose pair names the matter's id
    /// with an INVOICE type — are never written.
    /// </summary>
    [Fact]
    public async Task ProvisioningAMatter_SecuresTheWorkAssignmentsAndProjectsFiledUnderIt_AndGivesThemItsSharee()
    {
        var matter = Guid.NewGuid();
        var ordinaryMatter = Guid.NewGuid();
        var (workAssignment, project) = (Guid.NewGuid(), Guid.NewGuid());
        var (underOrdinary, underEvent, invoiceTyped) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: true); // flagged, not yet provisioned (owned by its creator)
        _fixture.SeedMatter(ordinaryMatter, isSecure: false);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"), format: "B");

        FiledWorkAssignment(_fixture, underOrdinary, "sprk_regardingmatter", "sprk_matter", ordinaryMatter);
        _fixture.SeedWorkAssignment(underEvent, isSecure: false);
        FilePair(_fixture, "sprk_workassignment", underEvent, Guid.NewGuid(), RecordTypeRef(_fixture, "sprk_event"));
        _fixture.SeedProject(invoiceTyped, isSecure: false);
        FilePair(_fixture, "sprk_project", invoiceTyped, matter, RecordTypeRef(_fixture, "sprk_invoice"));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute,
            new { recordType = "matter", recordId = matter, sharePrincipalIds = new[] { Colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ShouldBeSecure(matter, "the matter itself");
        ShouldBeSecure(workAssignment, "a work assignment filed under the matter by its typed lookup");
        ShouldBeSecure(project, "a project filed under the matter by its pair");
        foreach (var filed in new[] { workAssignment, project })
        {
            _fixture.ShareMaskOf(filed, Colleague).Should().Be(MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights),
                "the matter's sharee sees it — the matter's rights, Share included since owner round 91 (task 149's mirror)");
        }

        ShouldBeUntouched(underOrdinary, "filed under an ORDINARY matter");
        ShouldBeUntouched(underEvent, "its pair names an event — not this rule");
        ShouldBeUntouched(invoiceTyped, "its pair names the matter's id but an invoice TYPE — the type decides");
        _fixture.CreatedContainerDisplayNames.Should().HaveCount(3, "one container each: the matter and its two filed records");

        var filedRecords = (await JsonOf(response)).GetProperty("filedRecords");
        filedRecords.GetProperty("status").GetString().Should().Be("Completed");
        filedRecords.GetProperty("secured").GetInt32().Should().Be(2);
        filedRecords.GetProperty("remaining").GetInt32().Should().Be(0);
        filedRecords.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("recordId").GetGuid())
            .Should().BeEquivalentTo(new[] { workAssignment, project }, "the invoice-typed project is not filed under the matter");
    }

    /// <summary>
    /// AC 3, cascading: a project filed under the matter is secured, and ITS provisioning (provisioning's own Step 8)
    /// secures the work assignment filed under the project — down the whole tree in one call.
    /// </summary>
    [Fact]
    public async Task ProvisioningAMatter_CascadesThroughAProjectFiledUnderIt_ToTheWorkAssignmentFiledUnderThatProject()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: true);
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingproject", "sprk_project", project);

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(ProvisionRoute, new { recordType = "matter", recordId = matter, sharePrincipalIds = new[] { Colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ShouldBeSecure(project, "filed under the matter");
        ShouldBeSecure(workAssignment, "filed under the project, which became secure inside the matter's call");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights),
            "the work assignment was secured (inside the project's provisioning) BEFORE the project was given the matter's " +
            "sharee — the project passes it on in the same call, never waiting for the job");
    }

    /// <summary>
    /// The bound on nested provisionings (<see cref="SecureRootInheritance.MaxNestedProvisioning"/>): a record filed four
    /// levels below the record provisioned is left to the job — the call answers <c>children_incomplete</c> naming it
    /// (<c>sdap.inherit.too_deep</c>), never a success — and one job run secures it.
    /// </summary>
    [Fact]
    public async Task AChainDeeperThanTheNestingBound_IsLeftToTheJob_AndTheNextRunSecuresIt()
    {
        var matter = Guid.NewGuid();
        var chain = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var workAssignment = Guid.NewGuid();
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        var projectType = RecordTypeRef(_fixture, "sprk_project");
        _fixture.SeedMatter(matter, isSecure: true);
        for (var i = 0; i < chain.Length; i++)
        {
            _fixture.SeedProject(chain[i], isSecure: false);
            FilePair(_fixture, "sprk_project", chain[i], i == 0 ? matter : chain[i - 1], i == 0 ? matterType : projectType);
        }

        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingproject", "sprk_project", chain[^1]);

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(ProvisionRoute, new { recordType = "matter", recordId = matter });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(response)).GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        foreach (var project in chain)
            ShouldBeSecure(project, "within the nesting bound");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse("four inherited provisionings deep: left to the job");
        _fixture.Logs.Entries.Should().Contain(e => e.Message.Contains(SecureRootInheritance.ReasonTooDeep),
            "the deepest provisioning names the record it left to the job, and why");

        var run = await RunJobAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        ShouldBeSecure(workAssignment, "the job reaches it as a record filed under a now-secure project");
    }

    /// <summary>
    /// AC 3 on a record ALREADY provisioned (task 148's "completes the pass" branch): a work assignment filed under it
    /// later, outside the BFF, is secured by calling provisioning again — 200 <c>childrenOnly</c> reporting it — and a third
    /// call has nothing to do (409 <c>already_provisioned</c>, nothing written).
    /// </summary>
    [Fact]
    public async Task ProvisioningAnAlreadySecureMatterAgain_SecuresWhatWasFiledUnderItSince_ThenHasNothingToDo()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        var client = _fixture.CreateAuthenticatedClient();

        var second = await client.PostAsJsonAsync(ProvisionRoute, new { recordType = "matter", recordId = matter });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var body = await JsonOf(second);
        body.GetProperty("childrenOnly").GetBoolean().Should().BeTrue();
        body.GetProperty("filedRecords").GetProperty("secured").GetInt32().Should().Be(1);
        ShouldBeSecure(workAssignment, "filed under a secure matter");

        var updates = _fixture.Updates.Count;
        var third = await client.PostAsJsonAsync(ProvisionRoute, new { recordType = "matter", recordId = matter });

        third.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonOf(third)).GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
        _fixture.Updates.Should().HaveCount(updates, "nothing was left to do");
    }

    /// <summary>
    /// ADR-003 on the transition: a filed record that cannot be secured (created by the application with no recorded
    /// person — nobody to secure it for) makes the call <c>children_incomplete</c> listing it with its own reason, never a
    /// success. It is left exactly as it was — never moved to a team nobody is in (S5).
    /// </summary>
    [Fact]
    public async Task AFiledRecordThatCannotBeSecured_MakesTheCallIncomplete_AndIsLeftAsItWas()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: true);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter, createdBy: AppUser);
        _fixture.CreatorPersonColumnExists = true; // the column exists, and names nobody

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(ProvisionRoute, new { recordType = "matter", recordId = matter });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("filedRecordsRemaining").GetInt32().Should().Be(1);
        var outstanding = problem.GetProperty("filedRecords").EnumerateArray().Single();
        outstanding.GetProperty("recordId").GetGuid().Should().Be(workAssignment);
        outstanding.GetProperty("outcome").GetString().Should().Be("refused");

        ShouldBeSecure(matter, "the matter's own steps stand");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
        _fixture.OwningTeamOf(workAssignment).Should().BeNull("never moved to the memberless team without a person to see it");
        _fixture.SomeoneCanOpen(workAssignment).Should().BeTrue();
    }

    // ── Fail closed: a parent that cannot be read ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC negative (owner round 17 item 3): a work assignment whose only parent's flag reads EMPTY is never secured or
    /// shared — and never treated as "not secure": it is unverifiable (incomplete, so it is retried) and nothing is written.
    /// </summary>
    [Fact]
    public async Task AParentWhoseFlagIsEmpty_IsUnverifiable_AndNothingIsWritten()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        var inheritance = InheritanceInScope(out var scope);
        using (scope)
        {
            World.Set("sprk_matter", matter, "sprk_issecure", null);

            var result = await inheritance.SecureIfFiledUnderSecureAsync("sprk_workassignment", workAssignment, "t158", CancellationToken.None);

            result.Outcome.Should().Be(SecureRootInheritOutcome.Unverifiable);
            result.ReasonCode.Should().Be(SecureRootInheritance.ReasonParentUnverifiable);
            result.IsComplete.Should().BeFalse("an unread flag is never 'not secure'");
            ShouldBeUntouched(workAssignment, "nothing is written on an unverifiable parent");
        }
    }

    /// <summary>
    /// Owner round 31 item 1 (task 158 r1): a work assignment under a readably SECURE matter and a project whose flag cannot
    /// be read (both typed lookups — since #1616 a work assignment's pair names a parent only by repeating one) is NOT secured: the person it would be secured for must not be walled off ANY secure record it is
    /// filed under, and the unreadable one cannot be checked — so the provisioning refuses before its first write
    /// (<c>creator_no_access_unverifiable</c>), nothing is written, and the result is incomplete so the job retries (pre-r1
    /// this record was secured with its sharees held — that no longer holds once the creator's walls are checked first).
    /// </summary>
    [Fact]
    public async Task ASecureParentBesideAnUnreadableOne_IsNotSecured_BecauseTheCreatorsWallsCannotBeChecked()
    {
        var (matter, unreadable, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, unreadable, Colleague);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", unreadable));
        World.FailingRowReadsOf("sprk_project", unreadable);
        var inheritance = InheritanceInScope(out var scope);
        using (scope)
        {
            var result = await inheritance.SecureIfFiledUnderSecureAsync("sprk_workassignment", workAssignment, "t158", CancellationToken.None);

            result.Outcome.Should().Be(SecureRootInheritOutcome.Failed);
            result.ReasonCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable);
            result.IsComplete.Should().BeFalse("retried by the job");
            ShouldBeUntouched(workAssignment, "refused before provisioning's first write");
        }
    }

    // ── #1616: a work assignment's pair is plain text — it names a parent only by repeating a typed lookup ─────────────────

    /// <summary>
    /// #1616 (the bypass): a user with Write on a work assignment sets its pair, through the Web API, to a SECURE matter they
    /// may not append to — no typed lookup names it, so Dataverse checked nothing. The pair is never a filing on its own: the
    /// work assignment is undetermined (never secured under that matter, never given its sharees, never told it is secure),
    /// and nothing is written. Before the fix it was isolated under the matter's team and shared to the matter's sharees.
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentWhosePairAloneNamesASecureMatter_IsNotSecured_AndNothingIsWritten()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        _fixture.SeedWorkAssignment(workAssignment, isSecure: false);
        FilePair(_fixture, "sprk_workassignment", workAssignment, matter, RecordTypeRef(_fixture, "sprk_matter"));
        var inheritance = InheritanceInScope(out var scope);
        using (scope)
        {
            var result = await inheritance.SecureIfFiledUnderSecureAsync("sprk_workassignment", workAssignment, "t1616", CancellationToken.None);

            result.Outcome.Should().NotBe(SecureRootInheritOutcome.Secured);
            result.Outcome.Should().NotBe(SecureRootInheritOutcome.AlreadySecure);
            result.IsComplete.Should().BeFalse("undetermined is never 'not filed under a secure record'");
            ShouldBeUntouched(workAssignment, "a pair no typed lookup repeats files it under nothing");
        }

        await RunJobAsync();

        ShouldBeUntouched(workAssignment, "the job does not secure it either");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "the matter's sharee is never given it");
    }

    /// <summary>
    /// #1616, the legitimate shapes still file: the Create Work Assignment wizard's typed matter lookup PLUS the same pair is
    /// secured under the secure matter; and a PROJECT's pair (its only filing, BFF-managed) still files it under the matter.
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentWithItsTypedMatterAndTheSamePair_AndAProjectFiledByItsPair_AreBothSecured()
    {
        var (matter, workAssignment, project) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        FilePair(_fixture, "sprk_workassignment", workAssignment, matter, matterType);
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, matter, matterType);

        var run = await RunJobAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        ShouldBeSecure(workAssignment, "filed by its typed matter and the same pair");
        ShouldBeSecure(project, "filed by its pair (a project's only filing)");
    }

    /// <summary>
    /// #1616: a work assignment typed under an ORDINARY matter whose pair names a DIFFERENT, secure matter is not secured —
    /// the disagreeing pair makes it undetermined (never "filed under the ordinary matter only", never "under the secure
    /// one"); nothing is written.
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentWhosePairDisagreesWithItsTypedMatter_IsNotSecured()
    {
        var (secure, ordinary, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secure, null, Colleague);
        _fixture.SeedMatter(ordinary, isSecure: false);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        FilePair(_fixture, "sprk_workassignment", workAssignment, secure, RecordTypeRef(_fixture, "sprk_matter"));

        await RunJobAsync();

        ShouldBeUntouched(workAssignment, "the pair alone never files it under the secure matter");
    }

    /// <summary>
    /// #1616 fix round F-C: a work assignment TYPED under a SECURE matter whose pair names a DIFFERENT, ordinary matter (a
    /// bypass, or a pair left beside a typed re-file) is still secured under the typed matter — the typed lookup Dataverse
    /// AppendTo-checked is a filing whatever the text pair says, and the pair names nothing the creator's No Access walls are
    /// asked about. What the unrepeated pair still holds: the typed matter's sharees (never mirrored on a guess). Before the
    /// fix round the creator No Access check answered "unverifiable" on every run and the work assignment was never secured
    /// (master secures it).
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentTypedUnderASecureMatter_WhosePairNamesAnotherOrdinaryMatter_IsSecured_ButItsShareesAreHeld()
    {
        var (secure, ordinary, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secure, null, Colleague);
        _fixture.SeedMatter(ordinary, isSecure: false);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", secure);
        FilePair(_fixture, "sprk_workassignment", workAssignment, ordinary, RecordTypeRef(_fixture, "sprk_matter"));

        await RunJobAsync();

        ShouldBeSecure(workAssignment, "filed under the secure matter by its typed lookup");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0,
            "the pair no typed lookup repeats leaves what it is filed under undetermined for the sharee mirror (fail closed)");
    }

    /// <summary>
    /// #1616 fix round F-C, the walls still hold: the same shape with the record's creator on the SECURE typed matter's No
    /// Access list is never secured for them — the walls are decided over the typed matter, not skipped.
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentTypedUnderASecureMatter_WhosePairNamesAnotherMatter_StillHonoursTheTypedMattersNoAccessList()
    {
        var (secure, ordinary, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secure, null, Colleague);
        _fixture.SeedMatter(ordinary, isSecure: false);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", secure);
        FilePair(_fixture, "sprk_workassignment", workAssignment, ordinary, RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);
        var inheritance = InheritanceInScope(out var scope);
        using (scope)
        {
            var result = await inheritance.SecureIfFiledUnderSecureAsync("sprk_workassignment", workAssignment, "t1616c", CancellationToken.None);

            result.Outcome.Should().NotBe(SecureRootInheritOutcome.Secured);
            _fixture.IsSecureOf(workAssignment).Should().BeFalse("its creator is walled off the secure matter it is filed under");
        }
    }

    /// <summary>
    /// #1616 fix round K-a: the child-direction listing applies the readers' one rule — a work assignment whose pair ALONE
    /// names the matter is a candidate (not Confirmed), never "filed under" it; a work assignment with the typed lookup and
    /// the same pair, and a project filed by its BFF-managed pair, are Confirmed. Before the fix round the pair-only work
    /// assignment was listed as Confirmed, and the No Access enforcer and the assigned-access reconciliation acted on it.
    /// </summary>
    [Fact]
    public async Task TheChildDirectionListing_ConfirmsOnlyTheFilingsTheReadersHonour()
    {
        var (matter, pairOnly, typedAndPair, project) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.SeedWorkAssignment(pairOnly, isSecure: false);
        FilePair(_fixture, "sprk_workassignment", pairOnly, matter, matterType);
        FiledWorkAssignment(_fixture, typedAndPair, "sprk_regardingmatter", "sprk_matter", matter);
        FilePair(_fixture, "sprk_workassignment", typedAndPair, matter, matterType);
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, matter, matterType);
        var inheritance = InheritanceInScope(out var scope);
        using (scope)
        {
            var listed = await inheritance.ListFiledRootsAsync(new[] { ("sprk_matter", matter) }, CancellationToken.None);

            listed.Should().ContainSingle(r => r.Id == pairOnly).Which.Confirmed.Should().BeFalse(
                "a work assignment's pair alone files it under nothing");
            listed.Should().ContainSingle(r => r.Id == typedAndPair).Which.Confirmed.Should().BeTrue();
            listed.Should().ContainSingle(r => r.Id == project).Which.Confirmed.Should().BeTrue(
                "a project's pair is its (BFF-managed) filing");
        }
    }

    // ── The sharees (task 149's mechanism, add-only on a root) ────────────────────────────────────────────────────────

    /// <summary>
    /// A record filed under TWO secure parents is given only the principals shared on BOTH, at the lower rights (task 149's
    /// intersection rule), never one shared on just one of them.
    /// </summary>
    [Fact]
    public async Task UnderTwoSecureParents_OnlyThePrincipalSharedOnBoth_IsGiven()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague, Outsider);
        SecureProject(_fixture, project, Colleague);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", project));

        var run = await RunJobAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        ShouldBeSecure(workAssignment, "filed under two secure records");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights));
        _fixture.ShareMaskOf(workAssignment, Outsider).Should().Be(0, "shared on the matter only — the intersection excludes them");
    }

    /// <summary>
    /// The mirror onto a ROOT never narrows or revokes a share made on the record itself (a direct View Only share stays), a
    /// parent's LATER sharee reaches it on the next run — and (owner round 30) an inherited share an operator NARROWED on the
    /// record outside the BFF is recorded Declined and is never raised again while the parent share persists.
    /// </summary>
    [Fact]
    public async Task TheMirrorOntoARoot_KeepsDirectShares_AddsALaterSharee_AndNeverReRaisesOneAnOperatorNarrowed()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        (await RunJobAsync()).Success.Should().BeTrue();
        // The operator narrows the Colleague's inherited share on the record itself (Read + Delete), outside the BFF.
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), "ReadAccess,DeleteAccess");
        var direct = Guid.NewGuid();
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(direct), RecordShareLevels.ViewOnlyRights);
        var later = Guid.NewGuid();
        _fixture.SeedShare(matter, DataversePrincipalRef.User(later), ProvisionProjectEndpoint.CollaboratorAccessRights);
        var revokes = _fixture.Revokes.Count;

        var run = await RunJobAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mask("ReadAccess,DeleteAccess"),
            "the operator's narrowing on the record stands — never raised again while the matter's share persists");
        _fixture.InheritedLedger.InheritedRowsOf(workAssignment)
            .Single(r => r.SystemUserId == Colleague).State.Should().Be(Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessState.Declined);
        _fixture.ShareMaskOf(workAssignment, direct).Should().Be(Mask(RecordShareLevels.ViewOnlyRights), "never revoked");
        _fixture.ShareMaskOf(workAssignment, later).Should().Be(MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights),
            "the parent's later sharee reaches it within one run");
        _fixture.Revokes.Should().HaveCount(revokes);
    }

    /// <summary>
    /// Owner R3/R4 ("immediate on save; the job only a safety net"): sharing a user on a secure matter through the BFF
    /// (<c>/share-user</c>) gives them its SECURE filed work assignment in the same call (task 149's mirror, add-only). A
    /// filed work assignment that is not secure yet is NOT secured by a share — that is not the act that secures it; the
    /// job does.
    /// </summary>
    [Fact]
    public async Task SharingAUserOnASecureMatter_GivesThemItsSecureFiledRecordsNow_ButSecuresNothing()
    {
        var (matter, secured, notYet) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureFiledWorkAssignment(secured, matter);
        FiledWorkAssignment(_fixture, notYet, "sprk_regardingmatter", "sprk_matter", matter);
        var users = new InternalUserShareTests.FakeSystemUsers();
        users.SeedPerson(Colleague, "Colleague");
        using var scope = _fixture.Services.CreateScope();

        var result = await InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("matter", matter, Colleague, ExternalAccessLevel.Collaborate),
            scope.ServiceProvider.GetRequiredService<IDataverseRecordShareService>(), users.Client,
            scope.ServiceProvider.GetRequiredService<ExternalParticipationService>(), new Mock<ITenantCache>().Object,
            new InternalUserShareTests.StubCallerRightsProbe(
                AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete
                | AccessRights.Share),
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(), SecureChildShareWorld.NobodyWalled(),
            scope.ServiceProvider.GetRequiredService<SecureRootInheritance>(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertNotifier(),
            new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("tid", "00000000-0000-0000-0000-0000000000cc"),
                    new Claim("oid", "66666666-6666-6666-6666-666666666666"),
                }, "test")),
                TraceIdentifier = "trace-158",
            },
            NullLogger<Program>.Instance, CancellationToken.None);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        _fixture.ShareMaskOf(secured, Colleague).Should().Be(MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights),
            "the matter's new sharee sees its secure filed record at once");
        _fixture.IsSecureOf(notYet).Should().BeFalse("a share never provisions a filed record — the job does");
        _fixture.SharesOn(notYet).Should().BeEmpty();
        _fixture.Updates.Should().NotContain(u => u.RecordId == notYet);
    }

    /// <summary>
    /// Passing sharees on is bounded like the provisionings (<see cref="SecureRootInheritance.MaxNestedProvisioning"/>): a
    /// matter's new sharee reaches three levels of secure projects filed one under another in the same call; the fourth is
    /// left to the job, whose next run gives it.
    /// </summary>
    [Fact]
    public async Task PassingShareesOn_IsBounded_AndTheJobCompletesTheDeeperLevels()
    {
        var matter = Guid.NewGuid();
        var chain = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        var projectType = RecordTypeRef(_fixture, "sprk_project");
        SecureMatter(_fixture, matter);
        for (var i = 0; i < chain.Length; i++)
        {
            SecureProject(_fixture, chain[i]);
            FilePair(_fixture, "sprk_project", chain[i], i == 0 ? matter : chain[i - 1], i == 0 ? matterType : projectType);
        }

        _fixture.SeedShare(matter, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CollaboratorAccessRights);
        var inheritance = InheritanceInScope(out var scope);
        using (scope)
            await inheritance.PassSharesOnAsync("sprk_matter", matter, "t158", CancellationToken.None);

        var mirror = MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights);
        chain.Take(3).Should().OnlyContain(p => _fixture.ShareMaskOf(p, Colleague) == mirror, "within the bound, in the same call");
        _fixture.ShareMaskOf(chain[3], Colleague).Should().Be(0, "past the bound: left to the job");

        (await RunJobAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(chain[3], Colleague).Should().Be(mirror, "the job completes it");
    }

    // ── (4) The safety net ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC 4: a work assignment and a project filed under a secure matter OUTSIDE the BFF (a wizard, a form, an import) are
    /// secured by ONE run; a second run changes nothing.
    /// </summary>
    [Fact]
    public async Task TheJob_SecuresRecordsFiledOutOfBand_InOneRun_AndASecondRunChangesNothing()
    {
        var (matter, workAssignment, project) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        var ordinary = Guid.NewGuid();
        _fixture.SeedWorkAssignment(ordinary, isSecure: false); // filed under nothing

        var first = await RunJobAsync();

        first.Success.Should().BeTrue(first.ErrorMessage);
        ShouldBeSecure(workAssignment, "filed out of band under a secure matter");
        ShouldBeSecure(project, "filed out of band under a secure matter by its pair");
        ShouldBeUntouched(ordinary, "filed under nothing");
        using (var doc = JsonDocument.Parse(first.ResultJson!))
            doc.RootElement.GetProperty("secured").GetInt32().Should().Be(2);

        var writes = (_fixture.Updates.Count, _fixture.Grants.Count, _fixture.Modifies.Count, _fixture.Revokes.Count,
            _fixture.CreatedContainerDisplayNames.Count);

        var second = await RunJobAsync();

        second.Success.Should().BeTrue(second.ErrorMessage);
        (_fixture.Updates.Count, _fixture.Grants.Count, _fixture.Modifies.Count, _fixture.Revokes.Count,
            _fixture.CreatedContainerDisplayNames.Count).Should().Be(writes, "the second run has nothing to do");
        using (var doc = JsonDocument.Parse(second.ResultJson!))
        {
            doc.RootElement.GetProperty("secured").GetInt32().Should().Be(0);
            doc.RootElement.GetProperty("alreadySecure").GetInt32().Should().Be(2);
        }
    }

    /// <summary>AC 4: a scan that cannot complete is a FAILED run (it throws) — nothing is decided on part of it.</summary>
    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    public async Task TheJob_WhenItsScanFails_FailsTheRun_AndWritesNothing(string failingTable)
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.FailingQueriesOf(failingTable);

        var run = async () => await RunJobAsync();

        await run.Should().ThrowAsync<InvalidOperationException>();
        ShouldBeUntouched(workAssignment, "a failed scan decides nothing");
    }

    /// <summary>AC 4: a scan larger than its page ceiling fails the run rather than decide on part of it.</summary>
    [Fact]
    public async Task TheJob_WhenTheFiledRecordsDoNotFitItsPage_FailsTheRun()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.EndlessPagesOf("sprk_workassignment");

        var run = async () => await RunJobAsync();

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nothing is decided*");
        ShouldBeUntouched(workAssignment, "a partial scan decides nothing");
    }

    /// <summary>AC 4: a list of the secure parents larger than the job's page ceiling fails the run.</summary>
    [Fact]
    public async Task TheJob_WhenTheSecureParentsDoNotFitItsPages_FailsTheRun()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.EndlessPagesOf("sprk_matter");

        var run = async () => await RunJobAsync();

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nothing is decided*");
        ShouldBeUntouched(workAssignment, "a partial scan decides nothing");
    }

    /// <summary>
    /// ADR-003 in the job: a record listed under a secure matter whose row then cannot be read is reported unverifiable —
    /// the run is NOT a success — and nothing is written to it.
    /// </summary>
    [Fact]
    public async Task TheJob_WhenAParentCannotBeRead_ReportsTheRecord_AndWritesNothing()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.FailingRowReadsOf("sprk_matter", matter);

        var run = await RunJobAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D")).And.Contain(SecureRootInheritance.ReasonParentUnverifiable);
        using (var doc = JsonDocument.Parse(run.ResultJson!))
            doc.RootElement.GetProperty("unverifiable").GetInt32().Should().Be(1);
        ShouldBeUntouched(workAssignment, "an unread parent is never 'not secure', and never a reason to write");
    }

    /// <summary>
    /// A record filed under a matter FLAGGED secure whose own provisioning has not completed (not isolated) is secured —
    /// the flag is what it inherits — but no sharee is mirrored from a parent that is not isolated yet; the run is not a
    /// success until that parent completes.
    /// </summary>
    [Fact]
    public async Task UnderAParentFlaggedButNotIsolated_TheRecordIsSecured_ButNoShareeIsGivenYet()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: true); // flagged; still owned by its creator
        _fixture.SeedShare(matter, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CollaboratorAccessRights);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);

        var run = await RunJobAsync();

        run.Success.Should().BeFalse("its parent's sharees are held");
        ShouldBeSecure(workAssignment, "filed under a record flagged secure");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "a parent that is not isolated passes on no sharee");
    }

    /// <summary>
    /// The run's bound: at most <see cref="SecureRootInheritanceJob.MaxProvisioningsPerRun"/> records are PROVISIONED per
    /// run (each creates a container); the rest are deferred, counted — the run is NOT a success (task 158 r1) — and secured
    /// by the next run, which continues from the cursor.
    /// </summary>
    [Fact]
    public async Task TheJob_ProvisionsAtMostItsBoundPerRun_AndTheNextRunSecuresTheRest()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var filed = Enumerable.Range(0, SecureRootInheritanceJob.MaxProvisioningsPerRun + 1).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in filed)
            FiledWorkAssignment(_fixture, id, "sprk_regardingmatter", "sprk_matter", matter);

        var first = await RunJobAsync();

        first.Success.Should().BeFalse("a deferred record is not done: the run is not a success (task 158 r1)");
        first.ErrorMessage.Should().Contain("deferred");
        using (var doc = JsonDocument.Parse(first.ResultJson!))
        {
            doc.RootElement.GetProperty("secured").GetInt32().Should().Be(SecureRootInheritanceJob.MaxProvisioningsPerRun);
            doc.RootElement.GetProperty("deferred").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("resumeAfter").GetString().Should().NotBeNullOrEmpty("the next run continues after it");
        }

        filed.Count(id => _fixture.IsSecureOf(id) == true).Should().Be(SecureRootInheritanceJob.MaxProvisioningsPerRun);

        var second = await RunJobAsync();

        second.Success.Should().BeTrue(second.ErrorMessage);
        filed.Should().OnlyContain(id => _fixture.IsSecureOf(id) == true);
    }

    // ── The way back: task 175 (owner round 84) — a filed record follows its parent out of secure; locked while filed ──────

    /// <summary>A work assignment already secured under <paramref name="matter"/> (as the rule leaves it).</summary>
    internal static void SecureFiledWorkAssignment(
        ProvisionProjectTestFixture fixture, Guid id, Guid matter, Guid? createdBy = null, string? name = null)
    {
        fixture.SeedWorkAssignment(id, owningTeamId: SecureTeam, containerId: $"b!wa-{id:N}", isSecure: true, createdBy: createdBy);
        fixture.SeedShare(id, DataversePrincipalRef.User(createdBy ?? Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        fixture.ChildWorld.Set("sprk_workassignment", id, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
        if (name is not null)
            fixture.ChildWorld.Set("sprk_workassignment", id, "sprk_name", name);
    }

    private void SecureFiledWorkAssignment(Guid id, Guid matter, Guid? createdBy = null, string? name = null) =>
        SecureFiledWorkAssignment(_fixture, id, matter, createdBy, name);

    private Task<HttpResponseMessage> UnsecureAsync(string recordType, Guid recordId, params (string Type, Guid Id)[] alsoUnsecure) =>
        _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new
        {
            recordType,
            recordId,
            alsoUnsecure = alsoUnsecure.Length == 0
                ? null
                : alsoUnsecure.Select(a => new { recordType = a.Type, recordId = a.Id }).ToArray(),
        });

    /// <summary>
    /// Task 175 AC 1 (owner round 84, REPLACING round 6 item 4's "stay secure"): unsecuring the matter un-secures the work
    /// assignment filed only under it, in the same call — flag cleared, owned by its business unit's team (D-11), its shares
    /// revoked — and reports it. Not related, though their pair names the matter's id: one typed as an INVOICE (stays secure,
    /// untouched), one whose type cannot be read (not provably filed here: left to the job, untouched, not listed).
    /// </summary>
    [Fact]
    public async Task UnsecuringTheParent_UnsecuresTheWorkAssignmentFiledOnlyUnderIt_AndReportsIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureFiledWorkAssignment(workAssignment, matter, name: "Due diligence");
        InheritedAccessRecord(_fixture, "sprk_workassignment", workAssignment, ("sprk_matter", matter));

        var (invoiceTyped, typeUnreadable) = (Guid.NewGuid(), Guid.NewGuid());
        SecureProject(_fixture, invoiceTyped);
        FilePair(_fixture, "sprk_project", invoiceTyped, matter, RecordTypeRef(_fixture, "sprk_invoice"));
        SecureProject(_fixture, typeUnreadable);
        var unreadableType = RecordTypeRef(_fixture, "sprk_matter");
        FilePair(_fixture, "sprk_project", typeUnreadable, matter, unreadableType);
        World.FailingRowReadsOf("sprk_recordtype_ref", unreadableType);

        var response = await UnsecureAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(matter).Should().BeFalse();
        _fixture.IsSecureOf(workAssignment).Should().BeFalse("round 84: the child follows its parent out of secure");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureChildShareWorld.GeneralTeam,
            "its parent's business unit's team (D-11) — never the memberless Secure team, never a user");
        _fixture.SharesOn(workAssignment).Should().BeEmpty("its explicit shares are revoked, as /unsecure-project does");
        foreach (var untouched in new[] { invoiceTyped, typeUnreadable })
        {
            _fixture.IsSecureOf(untouched).Should().BeTrue("not provably filed under the matter");
            _fixture.Updates.Should().NotContain(u => u.RecordId == untouched);
        }

        var body = await JsonOf(response);
        body.GetProperty("relatedSecureRecords").EnumerateArray().Should().BeEmpty("nothing filed under it is still secure");
        var outcome = body.GetProperty("relatedRecordsUnsecured").EnumerateArray().Single();
        outcome.GetProperty("recordId").GetGuid().Should().Be(workAssignment);
        outcome.GetProperty("outcome").GetString().Should().Be("unsecured");
    }

    /// <summary>
    /// Task 175 (round 84): <c>alsoUnsecure</c> asks for nothing more — every record filed under the matter follows it, with
    /// no F3 per related record (the caller created one and not the other; both follow). One filed under ANOTHER matter is
    /// reported <c>not_related</c> and stays secure.
    /// </summary>
    [Fact]
    public async Task AlsoUnsecure_IsSubsumed_EveryRecordFiledUnderTheMatterFollowsIt_AndAnUnrelatedOneIsReported()
    {
        var (matter, otherMatter) = (Guid.NewGuid(), Guid.NewGuid());
        var (mine, theirs, elsewhere) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureMatter(_fixture, otherMatter);
        SecureFiledWorkAssignment(mine, matter);
        SecureFiledWorkAssignment(theirs, matter, createdBy: Outsider);
        InheritedAccessRecord(_fixture, "sprk_workassignment", mine, ("sprk_matter", matter));
        InheritedAccessRecord(_fixture, "sprk_workassignment", theirs, ("sprk_matter", matter));
        SecureFiledWorkAssignment(elsewhere, otherMatter);

        var response = await UnsecureAsync("matter", matter, ("workassignment", elsewhere));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(mine).Should().BeFalse();
        _fixture.IsSecureOf(theirs).Should().BeFalse("no F3 per related record: it follows its parent (round 84)");
        _fixture.IsSecureOf(elsewhere).Should().BeTrue("not filed under this matter");
        _fixture.Updates.Should().NotContain(u => u.RecordId == elsewhere);

        var body = await JsonOf(response);
        var outcomes = body.GetProperty("relatedRecordsUnsecured").EnumerateArray()
            .ToDictionary(o => o.GetProperty("recordId").GetGuid(), o => (
                Outcome: o.GetProperty("outcome").GetString(),
                Code: o.TryGetProperty("reasonCode", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null));
        outcomes[mine].Should().Be(("unsecured", (string?)null));
        outcomes[theirs].Should().Be(("unsecured", (string?)null));
        outcomes[elsewhere].Should().Be(("refused", (string?)UnsecureProjectEndpoint.ReasonNotRelated));
    }

    /// <summary>
    /// Task 175 AC 4 (round 87: the parent sets a floor): un-securing a work assignment filed under a SECURE matter is refused
    /// 409 <c>access_follows_parent</c> naming the matter, nothing written. Once the matter is not secure the work assignment
    /// has followed it (its secure was inherited), and the same call is no longer refused.
    /// </summary>
    [Fact]
    public async Task UnsecuringAFiledRecord_UnderASecureParent_IsRefusedNamingIt_AndNotOnceTheParentIsOrdinary()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, name: "Project Falcon");
        SecureFiledWorkAssignment(workAssignment, matter);
        InheritedAccessRecord(_fixture, "sprk_workassignment", workAssignment, ("sprk_matter", matter));

        var refused = await UnsecureAsync("workassignment", workAssignment);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonOf(refused);
        problem.GetProperty("reasonCode").GetString().Should().Be(AccessFollowsParent.ReasonCode);
        problem.GetProperty("parentRecordType").GetString().Should().Be("matter");
        problem.GetProperty("parentRecordId").GetGuid().Should().Be(matter);
        problem.GetProperty("parentName").GetString().Should().Be("Project Falcon");
        problem.GetProperty("detail").GetString().Should().Contain("Project Falcon");
        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
        _fixture.Updates.Should().NotContain(u => u.RecordId == workAssignment, "refused before any write");

        (await UnsecureAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.IsSecureOf(workAssignment).Should().BeFalse("it followed the matter");

        var again = await UnsecureAsync("workassignment", workAssignment);
        again.StatusCode.Should().Be(HttpStatusCode.OK, "round 87: its parent is no longer secure, so no floor stops it (it is already not secure)");
    }

    /// <summary>
    /// ADR-003 on the way back: what the work assignment is filed under cannot be read — 500 <c>parent_unverifiable</c>,
    /// nothing written (an unreadable parent is never "no parent").
    /// </summary>
    [Fact]
    public async Task UnsecuringARelatedRecordWhoseParentCannotBeRead_IsRefused_AndNothingIsWritten()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureFiledWorkAssignment(workAssignment, matter);
        World.FailingRowReadsOf("sprk_matter", matter);

        var response = await UnsecureAsync("workassignment", workAssignment);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(response)).GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonParentUnverifiable);
        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
        _fixture.Updates.Should().NotContain(u => u.RecordId == workAssignment);
    }

    // ── #1630 (owner Option A, 2026-10-11): filed under an INVOICE or EVENT of a secure matter or project ─────────────────

    /// <summary>An invoice belonging to <paramref name="matter"/> (its typed <c>sprk_matter</c> — its core-ancestor stamp).</summary>
    internal static Guid InvoiceOf(ProvisionProjectTestFixture fixture, Guid matter)
    {
        var id = Guid.NewGuid();
        fixture.ChildWorld.Add("sprk_invoice", id, ("sprk_matter", new EntityReference("sprk_matter", matter)));
        return id;
    }

    /// <summary>An event filed regarding <paramref name="parentTable"/> <paramref name="parentId"/> by its typed lookup.</summary>
    internal static Guid EventOf(ProvisionProjectTestFixture fixture, string parentTable, Guid parentId)
    {
        var id = Guid.NewGuid();
        fixture.ChildWorld.Add("sprk_event", id,
            ("sprk_regarding" + parentTable["sprk_".Length..], new EntityReference(parentTable, parentId)));
        return id;
    }

    /// <summary>
    /// #1630: a work assignment filed under an INVOICE of a secure matter (the wizard's shape: the typed
    /// <c>sprk_regardinginvoice</c> AND the same pair) and one filed under an EVENT of a secure project are secured by one job
    /// run — flag, named team, own container, creator share — and given the matter's / project's sharee, exactly as if
    /// filed directly under it. Decoys never written: a work assignment under an invoice of an ORDINARY matter, and one
    /// under an event rooted at a (secure) WORK ASSIGNMENT — not the parent rule. Before #1630 the first two were never
    /// listed, never isolated.
    /// </summary>
    [Fact]
    public async Task TheJob_SecuresAWorkAssignmentFiledUnderAnInvoiceOfASecureMatter_OrAnEventOfASecureProject_AndGivesThemTheSharee()
    {
        var (matter, project, ordinaryMatter) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project, Colleague);
        _fixture.SeedMatter(ordinaryMatter, isSecure: false);
        var invoice = InvoiceOf(_fixture, matter);
        var projectEvent = EventOf(_fixture, "sprk_project", project);

        var (underInvoice, underEvent) = (Guid.NewGuid(), Guid.NewGuid());
        FiledWorkAssignment(_fixture, underInvoice, "sprk_regardinginvoice", "sprk_invoice", invoice);
        FilePair(_fixture, "sprk_workassignment", underInvoice, invoice, RecordTypeRef(_fixture, "sprk_invoice"));
        FiledWorkAssignment(_fixture, underEvent, "sprk_regardingevent", "sprk_event", projectEvent);
        FilePair(_fixture, "sprk_workassignment", underEvent, projectEvent, RecordTypeRef(_fixture, "sprk_event"));

        var secureWorkAssignment = Guid.NewGuid();
        SecureFiledWorkAssignment(secureWorkAssignment, matter);
        var (underOrdinaryInvoice, underWorkAssignmentEvent) = (Guid.NewGuid(), Guid.NewGuid());
        FiledWorkAssignment(_fixture, underOrdinaryInvoice, "sprk_regardinginvoice", "sprk_invoice", InvoiceOf(_fixture, ordinaryMatter));
        FiledWorkAssignment(_fixture, underWorkAssignmentEvent, "sprk_regardingevent", "sprk_event",
            EventOf(_fixture, "sprk_workassignment", secureWorkAssignment));

        var run = await RunJobAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        ShouldBeSecure(underInvoice, "filed under an invoice of a secure matter");
        ShouldBeSecure(underEvent, "filed under an event of a secure project");
        foreach (var filed in new[] { underInvoice, underEvent })
        {
            _fixture.ShareMaskOf(filed, Colleague).Should().Be(MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights),
                "the secure parent's sharee sees it, as if it were filed directly under that parent");
        }

        ShouldBeUntouched(underOrdinaryInvoice, "its invoice belongs to an ORDINARY matter");
        ShouldBeUntouched(underWorkAssignmentEvent, "its event is rooted at a work assignment — not the parent rule");

        using var scope = _fixture.Services.CreateScope();
        var inheritance = scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();
        var answer = await inheritance.FindSecureParentsAsync("sprk_workassignment", underInvoice, CancellationToken.None);
        answer.IsKnown.Should().BeTrue();
        answer.SecureParents.Should().ContainSingle().Which.Should().Match<SecureFilingParent>(p =>
            p.Table == "sprk_matter" && p.Id == matter && p.Name == null,
            "the parent reached through the invoice keeps its name to itself (task 174 F1-d)");
        var listed = await inheritance.ListFiledRootsAsync(new[] { ("sprk_matter", matter), ("sprk_project", project) }, CancellationToken.None);
        listed.Where(r => r.Id == underInvoice || r.Id == underEvent).Should().HaveCount(2).And.OnlyContain(r => r.Confirmed,
            "the child-direction listing agrees with the decision side: typed lookups, so Confirmed");
        listed.Should().NotContain(r => r.Id == underOrdinaryInvoice || r.Id == underWorkAssignmentEvent);
    }

    /// <summary>
    /// #1630, the cascade: provisioning a matter secures, in the same call, the work assignment filed under ITS invoice —
    /// provisioning's own Step 8 listing reaches it — and gives it the matter's sharee.
    /// </summary>
    [Fact]
    public async Task ProvisioningAMatter_SecuresTheWorkAssignmentFiledUnderItsInvoice()
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: true); // flagged, not yet provisioned
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardinginvoice", "sprk_invoice", InvoiceOf(_fixture, matter));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute,
            new { recordType = "matter", recordId = matter, sharePrincipalIds = new[] { Colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ShouldBeSecure(workAssignment, "filed under an invoice of the matter that became secure");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(MirrorOf(ProvisionProjectEndpoint.CollaboratorAccessRights));
        var filedRecords = (await JsonOf(response)).GetProperty("filedRecords");
        filedRecords.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("recordId").GetGuid())
            .Should().Equal(workAssignment);
    }

    /// <summary>
    /// #1630, the way back (task 175): unsecuring the matter un-secures the work assignment filed only under its invoice, in
    /// the same call — the cascade's listing reaches it — and reports it.
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_UnsecuresTheWorkAssignmentFiledOnlyUnderItsInvoice()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var workAssignment = Guid.NewGuid();
        _fixture.SeedWorkAssignment(workAssignment, owningTeamId: SecureTeam, containerId: $"b!wa-{workAssignment:N}", isSecure: true);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardinginvoice",
            new EntityReference("sprk_invoice", InvoiceOf(_fixture, matter)));
        InheritedAccessRecord(_fixture, "sprk_workassignment", workAssignment, ("sprk_matter", matter));

        var response = await UnsecureAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(workAssignment).Should().BeFalse("it follows the matter its invoice belongs to out of secure");
        _fixture.SharesOn(workAssignment).Should().BeEmpty();
        var outcome = (await JsonOf(response)).GetProperty("relatedRecordsUnsecured").EnumerateArray().Single();
        outcome.GetProperty("recordId").GetGuid().Should().Be(workAssignment);
        outcome.GetProperty("outcome").GetString().Should().Be("unsecured");
    }

    /// <summary>
    /// #1630, fail closed (ADR-003): a work assignment filed under an invoice that cannot be read is UNVERIFIABLE — never "not
    /// secure" — through the one-record walk and the batched walk alike, and the decision writes nothing to it.
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentFiledUnderAnInvoiceThatCannotBeRead_IsUnverifiable_AndNothingIsWritten()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var invoice = InvoiceOf(_fixture, matter);
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardinginvoice", "sprk_invoice", invoice);
        World.FailingRowReadsOf("sprk_invoice", invoice);
        World.FailingIdListReadsOf("sprk_invoice", invoice);
        var dataverse = SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object;

        var one = await SecureRootInheritance.ReadSecureParentsAsync(
            dataverse, NullLogger.Instance, "sprk_workassignment", workAssignment, CancellationToken.None);
        var many = await SecureRootInheritance.ReadSecureParentsOfManyAsync(
            dataverse, NullLogger.Instance, "sprk_workassignment", new[] { workAssignment, Guid.NewGuid() }, CancellationToken.None);

        foreach (var answer in new[] { one, many[workAssignment] })
        {
            answer.IsKnown.Should().BeFalse("an unread intermediate is never 'filed under nothing'");
            answer.Unverifiable.Should().Contain("sprk_invoice");
            answer.HasSecureParent.Should().BeFalse();
        }

        var inheritance = InheritanceInScope(out var scope);
        using (scope)
        {
            var result = await inheritance.SecureIfFiledUnderSecureAsync("sprk_workassignment", workAssignment, "t1630", CancellationToken.None);
            result.Outcome.Should().Be(SecureRootInheritOutcome.Unverifiable);
        }

        ShouldBeUntouched(workAssignment, "an unread intermediate is never a reason to write");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The host's REAL inheritance, in a scope the caller disposes.</summary>
    private SecureRootInheritance InheritanceInScope(out IServiceScope scope)
    {
        scope = _fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();
    }

    /// <summary>One run of the REAL job — the same job instance across a test, as the scheduler's singleton (its cursor).</summary>
    private Task<JobRunResult> RunJobAsync() => _job.RunAsync();

    private readonly SecureRootInheritanceJobRunner _job;
}

/// <summary>
/// ONE instance of the REAL <see cref="SecureRootInheritanceJob"/> (the scheduler registers it as a singleton, so its progress
/// cursor survives between runs — task 158 r1): each run lists the parents and their filed records through the fixture's
/// <see cref="SecureChildShareWorld"/> (as the job's own <c>IGenericEntityService</c> would) and secures through the host's
/// inheritance (the fixture's provisioning).
/// </summary>
internal sealed class SecureRootInheritanceJobRunner
{
    private readonly ProvisionProjectTestFixture _fixture;
    private readonly SecureRootInheritanceJob _job;
    private readonly ServiceProvider _provider;
    private IServiceScope? _hostScope;

    public SecureRootInheritanceJobRunner(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        var services = new ServiceCollection();
        services.AddSingleton(_ => SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object);
        services.AddScoped(_ => _hostScope!.ServiceProvider.GetRequiredService<SecureRootInheritance>());
        _provider = services.BuildServiceProvider();
        _job = new SecureRootInheritanceJob(
            _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<SecureRootInheritanceJob>.Instance);
    }

    public async Task<JobRunResult> RunAsync()
    {
        using var hostScope = _fixture.Services.CreateScope();
        _hostScope = hostScope;
        try
        {
            return await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "t158", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
                CancellationToken.None);
        }
        finally
        {
            _hostScope = null;
        }
    }
}
