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
                "the matter's sharee sees it — the matter's rights without Share (task 149's mirror)");
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
    /// Owner round 31 item 1 (task 158 r1): a work assignment under a readably SECURE matter and, by its pair, a project whose
    /// flag cannot be read is NOT secured: the person it would be secured for must not be walled off ANY secure record it is
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
        FilePair(_fixture, "sprk_workassignment", workAssignment, unreadable, RecordTypeRef(_fixture, "sprk_project"));
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
