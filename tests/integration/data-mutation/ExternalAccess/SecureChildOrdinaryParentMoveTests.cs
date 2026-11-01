// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/ExternalAccess/**`
//   - Justification: UAC task 194 (#2339, owner round 128 item 3; rule I-6) — the children of an ORDINARY record reassigned
//     to another business unit follow it: SecureChildReconciler.ReconcileOrdinaryParentMoveAsync and the
//     SecureChildReconciliationJob pass that drives it. Pins the must-fire move (grandchildren included, read back), the
//     must-not-fire cases (Secure-team-owned, user-owned, non-default team, already matching, switch off), the never-release
//     rule and the multi-parent primary-parent answer, and the document whose file follows in the same run.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL reconciler, resolver, job, relocator and container resolver over
// task 148's world (SecureChildShareWorld) and task 166's pointer world.

using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using RefilePassRoundOneTests = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefilePassRoundOneTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

public class SecureChildOrdinaryParentMoveTests
{
    private const string Matter = "sprk_matter";
    private const string Project = "sprk_project";

    private static readonly Guid GeneralBu = SecureChildShareWorld.GeneralBu;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureBu = SecureChildShareWorld.SecureBu;

    /// <summary>Business unit B, where the matter was reassigned, and its default team.</summary>
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-000000194001");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-000000194002");

    /// <summary>An owner team in business unit A that is NOT its default team.</summary>
    private static readonly Guid NonDefaultTeamA = Guid.Parse("0a000000-0000-4000-8000-000000194003");

    /// <summary>Task 178's shape: another owner team in the Secure Record business unit.</summary>
    private static readonly Guid SecureFamilyTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000194004");

    private static readonly Guid MatterId = Guid.Parse("1b000000-0000-4000-8000-000000194010");
    private static readonly Guid EventId = Guid.Parse("1b000000-0000-4000-8000-000000194011");
    private static readonly Guid TodoId = Guid.Parse("1b000000-0000-4000-8000-000000194012");
    private static readonly Guid DocumentId = Guid.Parse("1b000000-0000-4000-8000-000000194013");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0)
        .Team(NonDefaultTeamA, GeneralBu, "Spaarke Paralegals", isDefault: false, teamType: 0)
        .Team(SecureFamilyTeam, SecureBu, "Secure Matter Owners", isDefault: false, teamType: 0);

    private readonly ConditionalOwnerWebApi _webApi;

    /// <summary>The share table the reconciler's synchronizer reads and writes (194b fix round R: a held row's mirror).</summary>
    private readonly FakeRecordShareTable _shares = new();

    /// <summary>A user the secure matter was shared with: its mirror sits on every isolated child.</summary>
    private static readonly Guid Sharee = Guid.Parse("5e000000-0000-4000-8000-000000194501");

    private const int CollaborateMask = 1 | 2 | 4 | 16;

    public SecureChildOrdinaryParentMoveTests() => _webApi = new ConditionalOwnerWebApi(() => _world);

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, _shares, _webApi);

    /// <summary>The matter, ordinary, reassigned from business unit A (the general unit) to B: owned by B's default team.</summary>
    private void MatterMovedToB(Guid? id = null) =>
        _world.Add(Matter, id ?? MatterId, ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false));

    /// <summary>A child row owned by <paramref name="owner"/>, filed through the lookups given (in this order).</summary>
    private void Row(string table, Guid id, EntityReference owner, params (string Column, string Target, Guid TargetId)[] lookups)
    {
        var columns = lookups.Select(l => (l.Column, (object)new EntityReference(l.Target, l.TargetId)))
            .Prepend((owner.LogicalName == "team" ? "owningteam" : "owninguser", owner))
            .Append(("versionnumber", (object)1L))
            .ToArray();
        _world.Add(table, id, columns);
    }

    private static EntityReference TeamRef(Guid id) => new("team", id);

    /// <summary>The event under the matter, a To Do under the event (a grandchild), a document under the matter — all in A.</summary>
    private void ChildrenStillInA()
    {
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));
        Row("sprk_todo", TodoId, TeamRef(GeneralTeam), ("sprk_regardingevent", "sprk_event", EventId));
        Row("sprk_document", DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Must fire
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "194 must fire: a matter moved A to B — its default-team children AND grandchild are re-owned to B's default team, conditionally, read back")]
    public async Task ChildrenOfAMovedMatter_AreReownedToTheNewBusinessUnitsDefaultTeam()
    {
        MatterMovedToB();
        ChildrenStillInA();

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        report.ChildrenReowned.Should().Be(3);
        report.OrdinaryWritesAttempted.Should().Be(3);
        foreach (var (table, id) in new[] { ("sprk_event", EventId), ("sprk_todo", TodoId), ("sprk_document", DocumentId) })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(TeamB), $"{table} follows its matter to B");
            report.Changes.Should().ContainSingle(c => c.Id == id).Which.Should().Be(
                new SecureChildRowChange(table, id, DataversePrincipalRef.Team(GeneralTeam), TeamB, SecureChildRowOutcome.Changed, null),
                "the previous owner is the reversal evidence");
        }

        _webApi.ConditionalWrites.Select(w => w.Id).Should().BeEquivalentTo(new[] { EventId, TodoId, DocumentId },
            "every move is the guarded If-Match re-own");
        _webApi.ConditionalWrites.Single(w => w.Id == TodoId).Should().Be(("sprk_todos", TodoId, "W/\"1\""));
    }

    [Fact(DisplayName = "194 must fire: descendants with NO core stamp, reached only through a lineage hop (a file version and a To Do under a document), follow the document to B")]
    public async Task DescendantsReachedOnlyThroughALineageHop_FollowTheirParent()
    {
        var fileVersion = Guid.NewGuid();
        var todoOnDocument = Guid.NewGuid();
        MatterMovedToB();
        Row("sprk_document", DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_fileversion", fileVersion, TeamRef(GeneralTeam), ("sprk_document", "sprk_document", DocumentId));
        Row("sprk_todo", todoOnDocument, TeamRef(GeneralTeam), ("sprk_regardingdocument", "sprk_document", DocumentId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_fileversion", fileVersion).Should().Be(DataversePrincipalRef.Team(TeamB),
            "a file version carries no core stamp; the walk reaches it through its document, and it follows the document's new unit");
        _world.OwnerOf("sprk_todo", todoOnDocument).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_document", DocumentId).Should().Be(DataversePrincipalRef.Team(TeamB));
        report.ChildrenReowned.Should().Be(3);
    }

    [Fact(DisplayName = "194 F4-b must fire: rows filed ONLY under a service request moved to B follow it, with their own descendants")]
    public async Task ChildrenFiledOnlyUnderAMovedServiceRequest_FollowIt()
    {
        var (request, todo, document, fileVersion) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_servicerequest", request, ("owningteam", new EntityReference("team", TeamB)));
        Row("sprk_todo", todo, TeamRef(GeneralTeam), ("sprk_regardingservicerequest", "sprk_servicerequest", request));
        Row("sprk_document", document, TeamRef(GeneralTeam), ("sprk_relatedservicerequest", "sprk_servicerequest", request));
        Row("sprk_fileversion", fileVersion, TeamRef(GeneralTeam), ("sprk_document", "sprk_document", document));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            "sprk_servicerequest", request, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_fileversion", fileVersion).Should().Be(DataversePrincipalRef.Team(TeamB));
        report.ChildrenReowned.Should().Be(3);
    }

    [Fact(DisplayName = "194 F4-b must not fire: a service request owned in the Secure Record business unit is left alone")]
    public async Task AServiceRequestOwnedInTheSecureBusinessUnit_IsNotApplicable()
    {
        var (request, todo) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_servicerequest", request, ("owningteam", new EntityReference("team", SecureFamilyTeam)));
        Row("sprk_todo", todo, TeamRef(GeneralTeam), ("sprk_regardingservicerequest", "sprk_servicerequest", request));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            "sprk_servicerequest", request, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.NotApplicable);
        _world.OwnerWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "194 F4-b: the SECURE walk is unchanged — it never follows a service-request lookup, and a service request is no secure root")]
    public async Task TheSecureWalk_NeverFollowsAServiceRequestLookup()
    {
        var (request, onlyUnderRequest, underBoth) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var secureMatter = Guid.NewGuid();
        _world.SecureRoot(Matter, secureMatter)
            .Add("sprk_servicerequest", request, ("owningteam", new EntityReference("team", GeneralTeam)));
        Row("sprk_event", onlyUnderRequest, TeamRef(GeneralTeam), ("sprk_regardingservicerequest", "sprk_servicerequest", request));
        Row("sprk_todo", underBoth, TeamRef(GeneralTeam),
            ("sprk_regardingmatter", Matter, secureMatter), ("sprk_regardingservicerequest", "sprk_servicerequest", request));

        // The secure pass of a matter reads its Assign cascade over the Web API; this matter has none.
        var secure = SecureChildShareWorld.ReconcilerOver(
            () => _world, new FakeRecordShareTable(), new Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient());
        var report = await secure.ReconcileAsync(
            Matter, secureMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_todo", underBoth).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "secure-if-any, as before");
        _world.OwnerOf("sprk_event", onlyUnderRequest).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "the secure walk reaches nothing through a service request");
        SecureChildLineage.Children.Values.SelectMany(t => t.Lookups.Values).Should().NotContain("sprk_servicerequest",
            "the service request stays out of the secure family's lineage");
        await FluentActions.Invoking(() => secure.ReconcileAsync("sprk_servicerequest", request,
                SecureChildReconcileMode.Apply, SecureChildPassTrigger.Sweep, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "194 F1-b must fire: a budget's buckets follow the budget when its matter moves to B")]
    public async Task ABudgetsBuckets_FollowAnOrdinaryMove()
    {
        var (budget, bucket) = (Guid.NewGuid(), Guid.NewGuid());
        MatterMovedToB();
        Row("sprk_budget", budget, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_budgetbucket", bucket, TeamRef(GeneralTeam), ("sprk_budget", "sprk_budget", budget));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_budget", budget).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_budgetbucket", bucket).Should().Be(DataversePrincipalRef.Team(TeamB));
    }

    [Fact(DisplayName = "194 must fire (ebilling-r1): an invoice and every invoice-family row filed only under it follow its matter to B")]
    public async Task TheInvoiceFamily_FollowsAnOrdinaryMove_ThroughTheInvoice()
    {
        var (invoice, line, submission, approval) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (externalIdentifier, correction) = (Guid.NewGuid(), Guid.NewGuid());
        MatterMovedToB();
        Row("sprk_invoice", invoice, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        // No core lookup on any family row: the walk reaches them only through the invoice (the correction also through its line).
        Row("sprk_billingevent", line, TeamRef(GeneralTeam), ("sprk_invoice", "sprk_invoice", invoice));
        Row("sprk_invoicesubmission", submission, TeamRef(GeneralTeam), ("sprk_invoice", "sprk_invoice", invoice));
        Row("sprk_invoiceapproval", approval, TeamRef(GeneralTeam), ("sprk_invoice", "sprk_invoice", invoice));
        Row("sprk_externalidentifier", externalIdentifier, TeamRef(GeneralTeam), ("sprk_regardinginvoice", "sprk_invoice", invoice));
        Row("sprk_invoicecorrection", correction, TeamRef(GeneralTeam),
            ("sprk_invoice", "sprk_invoice", invoice), ("sprk_invoiceline", "sprk_billingevent", line));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        foreach (var (table, id) in new[]
                 {
                     ("sprk_invoice", invoice), ("sprk_billingevent", line), ("sprk_invoicesubmission", submission),
                     ("sprk_invoiceapproval", approval), ("sprk_externalidentifier", externalIdentifier),
                     ("sprk_invoicecorrection", correction),
                 })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(TeamB), $"{table} follows its invoice's matter to B");
        }

        report.ChildrenReowned.Should().Be(6);
    }

    [Fact(DisplayName = "194 must fire: report-only plans every move, the grandchild through its planned parent, and writes nothing")]
    public async Task ReportOnly_PlansEveryMove_AndWritesNothing()
    {
        MatterMovedToB();
        ChildrenStillInA();

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.ReportOnly, maxWrites: 200, CancellationToken.None);

        report.Changes.Where(c => c.Outcome == SecureChildRowOutcome.WouldChange).Select(c => c.Id)
            .Should().BeEquivalentTo(new[] { EventId, TodoId, DocumentId });
        report.Changes.Should().OnlyContain(c => c.TargetTeamId == TeamB);
        _world.OwnerWrites.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "194: the write cap — past it nothing is written, the row is reported with the cap detail and the pass is incomplete")]
    public async Task PastTheWriteCap_NothingIsWritten_AndThePassIsIncomplete()
    {
        MatterMovedToB();
        ChildrenStillInA();

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 1, CancellationToken.None);

        report.OrdinaryWritesAttempted.Should().Be(1);
        _webApi.ConditionalWrites.Should().HaveCount(1);
        report.Status.Should().Be(SecureChildReconcileStatus.Incomplete);
        report.Changes.Where(c => c.Outcome == SecureChildRowOutcome.Failed)
            .Should().NotBeEmpty().And.OnlyContain(c => c.Detail == SecureChildReconciler.OrdinaryMoveCapDetail);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Must not fire
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "194 must not fire / never release: a child owned in the Secure Record business unit is never decided or written, even when every parent is ordinary")]
    [InlineData("named")]
    [InlineData("family")]
    [InlineData("default")]
    public async Task ASecureTeamOwnedChild_IsNeverTouched(string which)
    {
        var owner = which switch
        {
            "named" => _world.OwnerTeam,
            "family" => SecureFamilyTeam,
            _ => SecureChildShareWorld.SecureDefaultTeam,
        };
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(owner), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(owner), "this pass never releases isolation");
        _world.OwnerWrites.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
        report.Changes.Should().BeEmpty("the secure pass owns it; this pass neither moves nor reports it");
        report.Status.Should().Be(SecureChildReconcileStatus.Completed);
    }

    [Fact(DisplayName = "194 never release: a mixed tree — the ordinary children move, the Secure-team-owned child under them stays isolated")]
    public async Task AMixedTree_MovesOnlyTheOrdinaryRows()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));
        Row("sprk_todo", TodoId, TeamRef(_world.OwnerTeam), ("sprk_regardingevent", "sprk_event", EventId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam));
        _world.OwnerWrites.Select(w => w.Id).Should().Equal(EventId);
        report.Status.Should().Be(SecureChildReconcileStatus.Completed);
    }

    [Fact(DisplayName = "194 must not fire: an ordinary child the rule gives the Secure team (also filed under a secure project) is reported and left to the secure pass")]
    public async Task AChildTheRuleGivesTheSecureTeam_IsReported_NotMoved()
    {
        var secureProject = Guid.NewGuid();
        MatterMovedToB();
        _world.SecureRoot(Project, secureProject);
        Row("sprk_todo", TodoId, TeamRef(GeneralTeam),
            ("sprk_regardingmatter", Matter, MatterId), ("sprk_regardingproject", Project, secureProject));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "only the secure pass moves a row INTO isolation");
        report.Changes.Should().ContainSingle(c => c.Id == TodoId).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.TargetTeamId == _world.OwnerTeam
            && c.Detail!.Contains("Secure Record owner team"));
        _world.OwnerWrites.Should().BeEmpty();
    }

    // ── UAC task 194b V-fix 3: a copy's canonical (sprk_document.sprk_canonicaldocument) is a Secure-only parent ──────────
    // The reconciler asks the writers' question (RecordOwnershipContext.SecureOnlyParents): the canonical joins secure-if-any
    // only and is never the copy's primary, so an ordinary canonical's move never takes the copy with it (task 146's case).

    [Fact(DisplayName = "194b V-fix 3 must not fire: an UNFILED copy of a document under the moved matter takes the unfiled answer — it is never moved to B with its canonical")]
    public async Task AnUnfiledCopyOfACanonicalUnderTheMovedMatter_IsNotMoved()
    {
        var copy = Guid.NewGuid();
        MatterMovedToB();
        Row("sprk_document", DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_document", copy, TeamRef(GeneralTeam), ("sprk_canonicaldocument", "sprk_document", DocumentId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_document", DocumentId).Should().Be(DataversePrincipalRef.Team(TeamB), "the canonical follows its matter");
        _world.OwnerOf("sprk_document", copy).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "the canonical is not the copy's primary: an unfiled copy keeps the unfiled answer, never B's business unit");
        _world.OwnerWrites.Select(w => w.Id).Should().Equal(DocumentId);
        report.Changes.Should().NotContain(c => c.Id == copy && c.TargetTeamId == TeamB);
        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
    }

    [Fact(DisplayName = "194b V-fix 3 must not fire: a copy filed under matter A, whose canonical is under the moved matter, stays with A")]
    public async Task ACopyFiledUnderAnotherMatter_OfACanonicalUnderTheMovedMatter_StaysWithItsOwnMatter()
    {
        var matterA = Guid.NewGuid();
        var copy = Guid.NewGuid();
        MatterMovedToB();
        _world.Add(Matter, matterA, ("owningteam", TeamRef(GeneralTeam)), ("sprk_issecure", false));
        Row("sprk_document", DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_document", copy, TeamRef(GeneralTeam),
            ("sprk_canonicaldocument", "sprk_document", DocumentId), ("sprk_matter", Matter, matterA));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_document", copy).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "its primary is matter A");
        _world.OwnerWrites.Select(w => w.Id).Should().Equal(DocumentId);
        report.Changes.Should().NotContain(c => c.Id == copy && c.TargetTeamId == TeamB);
    }

    [Fact(DisplayName = "194b V-fix 3 must not fire: a copy filed under the moved matter whose canonical is under a SECURE matter is reported for the secure pass, never given B")]
    public async Task ACopyUnderTheMovedMatter_OfASecureCanonical_IsLeftToTheSecurePass()
    {
        var secureMatter = Guid.NewGuid();
        var secureCanonical = Guid.NewGuid();
        var copy = Guid.NewGuid();
        MatterMovedToB();
        _world.SecureRoot(Matter, secureMatter)
            .SecureChild("sprk_document", secureCanonical, ("sprk_matter", Matter, secureMatter));
        Row("sprk_document", copy, TeamRef(GeneralTeam),
            ("sprk_matter", Matter, MatterId), ("sprk_canonicaldocument", "sprk_document", secureCanonical));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_document", copy).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "only the secure pass moves a row INTO isolation");
        report.Changes.Should().ContainSingle(c => c.Id == copy).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.TargetTeamId == _world.OwnerTeam
            && c.Detail!.Contains("Secure Record owner team"));
        _world.OwnerWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "194 must not fire: a user-owned child is reported (its rule answer is in another business unit), never written")]
    public async Task AUserOwnedChild_IsReported_NotMoved()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, new EntityReference("systemuser", SecureChildShareWorld.SomeUser),
            ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().ContainSingle().Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.TargetTeamId == TeamB && c.Detail!.Contains("owned by a user"));
        report.Status.Should().Be(SecureChildReconcileStatus.Completed, "a reported row is not unfinished work");
    }

    [Fact(DisplayName = "194 must not fire: a child owned by a team that is not a business unit's default team is reported, never written")]
    public async Task ANonDefaultTeamOwnedChild_IsReported_NotMoved()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(NonDefaultTeamA), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(NonDefaultTeamA));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().ContainSingle().Which.Detail.Should().Contain("not a business unit's default team");
    }

    [Fact(DisplayName = "194 must not fire: children that already match their moved matter are AlreadyCorrect — nothing written, nothing listed")]
    public async Task ChildrenThatAlreadyMatch_AreNotWritten()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(TeamB), ("sprk_regardingmatter", Matter, MatterId));
        Row("sprk_todo", TodoId, TeamRef(TeamB), ("sprk_regardingevent", "sprk_event", EventId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed);
        report.Tables.Sum(t => t.AlreadyCorrect).Should().Be(2);
        report.Changes.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Theory(DisplayName = "194 must not fire: a secure, isolated or unreadable-flag root is NotApplicable — nothing read below it, nothing written")]
    [InlineData("secure")]
    [InlineData("flagged-not-isolated")]
    [InlineData("masked-flag")]
    public async Task ASecureRoot_IsNotApplicable(string kind)
    {
        _ = kind switch
        {
            "secure" => _world.SecureRoot(Matter, MatterId),
            "flagged-not-isolated" => _world.FlaggedNotIsolatedRoot(Matter, MatterId),
            _ => _world.MaskedFlagNotIsolatedRoot(Matter, MatterId),
        };
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.NotApplicable);
        _world.OwnerWrites.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Multi-parent: the resolver's primary parent decides
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "194b multi-parent: a child filed under the moved matter (B) and a project in A takes its table's primary (the matter), whatever order its lookups were read in")]
    [InlineData(true)]  // the matter is the first lookup read
    [InlineData(false)] // the project is the first lookup read: before 194b the child stayed in A
    public async Task AMultiParentChild_TakesItsTablesPrimary_WhateverTheAttributeOrder(bool matterFirst)
    {
        // 194 pinned the attribute order as the primary; that was the defect (49 of 52 planned moves on dev were wrong). An
        // event's primary order (SecureChildLineage) names sprk_regardingmatter before sprk_regardingproject.
        var projectInA = Guid.NewGuid();
        MatterMovedToB();
        _world.OrdinaryRoot(Project, projectInA);
        var lookups = new[] { ("sprk_regardingmatter", Matter, MatterId), ("sprk_regardingproject", Project, projectInA) };
        Row("sprk_event", EventId, TeamRef(GeneralTeam), matterFirst ? lookups : lookups.Reverse().ToArray());

        await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(TeamB), "the matter is the event's primary");
        _world.OwnerWrites.Should().HaveCount(1);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // 194b: one deterministic ownership question per existing row, the writer's (RecordOwnershipContext.ForExistingRowAsync)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The record-type catalog row naming <c>sprk_matter</c> (a Signal's or Decision Record's core type).</summary>
    private static readonly Guid CatalogMatter = Guid.Parse("4b000000-0000-4000-8000-000000194401");

    /// <summary>A Signal or Decision Record row: <paramref name="lookups"/> in the order given (the attribute order Dataverse
    /// happens to return), its core pair naming <paramref name="core"/> (a matter; none when null), its regarding pair id the
    /// subject's.</summary>
    private void OntologyRow(
        string table, Guid id, EntityReference owner, Guid? core, Guid subject, params (string Column, string Target, Guid TargetId)[] lookups)
    {
        _world.Add("sprk_recordtype_ref", CatalogMatter, ("sprk_recordlogicalname", "sprk_matter"));
        Row(table, id, owner, core is { } c ? lookups.Append(("sprk_corerecordtype", "sprk_recordtype_ref", CatalogMatter)).ToArray() : lookups);
        if (core is { } coreId)
            _world.Set(table, id, "sprk_corerecordid", coreId.ToString("D"));
        _world.Set(table, id, "sprk_regardingrecordid", subject.ToString("D"));
    }

    [Fact(DisplayName = "194b must fire: a Signal whose user-owned To Do (root unit, filed to the B matter) is read BEFORE its core matter is already correct at B's team — never a move")]
    public async Task ASignalWhoseToDoComesFirst_IsAlreadyCorrect_AtItsCoresTeam()
    {
        MatterMovedToB();
        _world.UserOwnedChild("sprk_todo", TodoId, ("sprk_regardingmatter", Matter, MatterId));
        var signal = Guid.NewGuid();
        OntologyRow("sprk_signal", signal, TeamRef(TeamB), core: MatterId, subject: TodoId,
            ("sprk_regardingtodo", "sprk_todo", TodoId), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerWrites.Should().NotContain(w => w.Id == signal, "SignalWriter gives the core matter's team, and so does the reconciler");
        report.Tables.Single(t => t.Table == "sprk_signal").AlreadyCorrect.Should().Be(1);
    }

    [Fact(DisplayName = "194b never-flip: decide the Signal, re-own its To Do to B's team, decide again — the same answer, no write either time")]
    public async Task ASignal_NeverFlips_WhenItsSubjectIsReowned()
    {
        MatterMovedToB();
        _world.UserOwnedChild("sprk_todo", TodoId, ("sprk_regardingmatter", Matter, MatterId));
        var signal = Guid.NewGuid();
        OntologyRow("sprk_signal", signal, TeamRef(TeamB), core: MatterId, subject: TodoId,
            ("sprk_regardingtodo", "sprk_todo", TodoId), ("sprk_matter", Matter, MatterId));

        await Reconciler().ReconcileOrdinaryParentMoveAsync(Matter, MatterId, SecureChildReconcileMode.Apply, 200, CancellationToken.None);
        _world.Add("sprk_todo", TodoId, ("owningteam", TeamRef(TeamB)), ("sprk_regardingmatter", new EntityReference(Matter, MatterId)),
            ("versionnumber", 2L));
        await Reconciler().ReconcileOrdinaryParentMoveAsync(Matter, MatterId, SecureChildReconcileMode.Apply, 200, CancellationToken.None);

        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerWrites.Should().NotContain(w => w.Id == signal);
    }

    [Theory(DisplayName = "194b must fire: an .eml document of a user-owned communication filed to the B matter is already correct at B's team, and its attachment does not cascade")]
    [InlineData(true)]  // it also names the matter (after the communication in its attributes): the primary order decides
    [InlineData(false)] // it names only the communication: its user-owned FILED communication is looked through (I-6)
    public async Task AnEmlDocumentOfAUserOwnedFiledCommunication_IsAlreadyCorrect_AndItsAttachmentStays(bool namesTheMatter)
    {
        MatterMovedToB();
        var (communication, eml, attachment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.UserOwnedChild("sprk_communication", communication, ("sprk_regardingmatter", Matter, MatterId));
        var emlLookups = new[] { ("sprk_relatedcommunication", "sprk_communication", communication), ("sprk_matter", Matter, MatterId) };
        Row("sprk_document", eml, TeamRef(TeamB), namesTheMatter ? emlLookups : emlLookups.Take(1).ToArray());
        Row("sprk_document", attachment, TeamRef(TeamB), ("sprk_parentdocument", "sprk_document", eml));

        await Reconciler().ReconcileOrdinaryParentMoveAsync(Matter, MatterId, SecureChildReconcileMode.Apply, 200, CancellationToken.None);

        _world.OwnerOf("sprk_document", eml).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_document", attachment).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerWrites.Should().NotContain(w => w.Id == eml || w.Id == attachment);
    }

    [Fact(DisplayName = "194b must fire: an analysis of a root-unit document, also regarding the B matter, keeps its document's team (the writers ask about the document first)")]
    public async Task AnAnalysis_FollowsItsDocument_NotItsRegardingMatter()
    {
        MatterMovedToB();
        var (document, analysis) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_document", document, ("owningteam", TeamRef(GeneralTeam)));
        Row("sprk_analysis", analysis, TeamRef(GeneralTeam),
            ("sprk_regardingmatter", Matter, MatterId), ("sprk_documentid", "sprk_document", document));

        await Reconciler().ReconcileOrdinaryParentMoveAsync(Matter, MatterId, SecureChildReconcileMode.Apply, 200, CancellationToken.None);

        _world.OwnerOf("sprk_analysis", analysis).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerWrites.Should().NotContain(w => w.Id == analysis);
    }

    [Theory(DisplayName = "194b must not fire: a Signal or Decision Record with NO core record about a PERSONAL To Do (the To Do job keeps it) is reported, never moved")]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    public async Task ANoCoreRow_IsReported_NotMoved(string table)
    {
        MatterMovedToB();
        Row("sprk_todo", TodoId, TeamRef(GeneralTeam)); // personal: filed under nothing, no assignee — the job's Fallback
        var row = Guid.NewGuid();
        // Reached from the matter through its typed core lookup only (no stamp: a live stamp would make it the re-file rule's).
        OntologyRow(table, row, TeamRef(GeneralTeam), core: null, subject: TodoId,
            ("sprk_regardingtodo", "sprk_todo", TodoId), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf(table, row).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerWrites.Should().NotContain(w => w.Id == row);
        report.Changes.Should().ContainSingle(c => c.Id == row)
            .Which.Should().Match<SecureChildRowChange>(c => c.Outcome == SecureChildRowOutcome.Untouched
                                                             && c.Detail!.Contains("TodoAssigneeOwnerReconciliationJob"));
    }

    [Fact(DisplayName = "194b F1-c must fire: a no-core Signal about an EVENT whose filing moved (now under the B matter) follows its event to B — its writer decided it over its subject")]
    public async Task ANoCoreSignalAboutAnEvent_FollowsTheEventsFiling()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));
        var signal = Guid.NewGuid();
        OntologyRow("sprk_signal", signal, TeamRef(GeneralTeam), core: null, subject: EventId, ("sprk_regardingevent", "sprk_event", EventId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(TeamB));
        report.Changes.Should().ContainSingle(c => c.Id == signal).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    [Fact(DisplayName = "194b F1-c must fire: a no-core Signal whose To Do is now FILED under the B matter (cored: the job leaves it) follows its To Do to B")]
    public async Task ANoCoreSignalWhoseToDoIsCored_FollowsItsToDo()
    {
        MatterMovedToB();
        Row("sprk_todo", TodoId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));
        var signal = Guid.NewGuid();
        OntologyRow("sprk_signal", signal, TeamRef(GeneralTeam), core: null, subject: TodoId, ("sprk_regardingtodo", "sprk_todo", TodoId));

        await Reconciler().ReconcileOrdinaryParentMoveAsync(Matter, MatterId, SecureChildReconcileMode.Apply, 200, CancellationToken.None);

        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(TeamB));
    }

    [Fact(DisplayName = "194b F1-d must fire: the ordinary pass REPORTS a Decision Record held by the Secure team whose stored lookups are all ordinary — never moved")]
    public async Task AnIsolatedDecisionRecordWithOrdinaryLookups_IsReported_ByTheOrdinaryPass()
    {
        MatterMovedToB();
        var record = Guid.NewGuid();
        OntologyRow("sprk_decisionrecord", record, TeamRef(SecureChildShareWorld.SecureTeam), core: MatterId, subject: MatterId,
            ("sprk_matter", Matter, MatterId), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        _world.OwnerWrites.Should().NotContain(w => w.Id == record);
        report.Changes.Should().ContainSingle(c => c.Id == record)
            .Which.Should().Match<SecureChildRowChange>(c => c.Outcome == SecureChildRowOutcome.Untouched && c.Detail!.Contains("F1-d"));
    }

    private int SecureBusinessUnitReads() => _world.Queries.Count(q => q.EntityName == "businessunit");

    private int DefaultTeamReadsOf(Guid businessUnit) => _world.Queries.Count(q =>
        q.EntityName == "team"
        && q.Criteria.Conditions.Any(c => c.AttributeName == "isdefault" && Equals(c.Values.Single(), true))
        && q.Criteria.Conditions.Any(c => c.AttributeName == "businessunitid" && Equals(c.Values.Single(), businessUnit)));

    [Fact(DisplayName = "194b V-fix (verifier B): one pass over N rows reads the Secure Record business unit and each business unit's default team once for the pass, not per row; the next pass reads them again")]
    public async Task OnePass_ReadsTheSecureBusinessUnitAndEachDefaultTeamOnce()
    {
        const int rows = 12;
        MatterMovedToB();
        for (var i = 0; i < rows; i++)
            Row("sprk_event", Guid.NewGuid(), TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.ReportOnly, maxWrites: 200, CancellationToken.None);

        report.Changes.Count(c => c.Table == "sprk_event").Should().Be(rows);
        // The pass's own setup reads it twice (the Secure Record owner team, then the business unit itself): fixed per
        // pass. Every row's owner question reuses the answer, so twelve rows add nothing (uncached: 2 + 12).
        SecureBusinessUnitReads().Should().Be(2, "the pass's own read is reused by every owner question");
        DefaultTeamReadsOf(BuB).Should().Be(1, "every row's answer is B's default team: read once for the pass");

        await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.ReportOnly, maxWrites: 200, CancellationToken.None);

        SecureBusinessUnitReads().Should().Be(4, "the cache lives for one pass: the next pass sees a change");
        DefaultTeamReadsOf(BuB).Should().Be(2);
    }

    /// <summary>A Signal about <paramref name="eventId"/>, owned by the general team (its subject lookup repeats its pair id).</summary>
    private void SignalAbout(Guid signal, Guid eventId)
    {
        Row("sprk_signal", signal, TeamRef(GeneralTeam), ("sprk_regardingevent", "sprk_event", eventId));
        _world.Set("sprk_signal", signal, "sprk_regardingrecordid", eventId.ToString("D"));
    }

    private static string StepsListing(params Guid[] resolved) =>
        "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"resolvedSignalIds\":["
        + string.Join(",", resolved.Select(s => "\"" + s.ToString("D") + "\"")) + "],\"steps\":[]}";

    /// <summary>An ORDINARY Decision Record (the general team) whose core is the moved matter, listing <paramref name="signals"/>.</summary>
    private Guid OrdinaryDecisionRecordResolving(params Guid[] signals)
    {
        var record = Guid.NewGuid();
        OntologyRow("sprk_decisionrecord", record, TeamRef(GeneralTeam), core: MatterId, subject: MatterId,
            ("sprk_matter", Matter, MatterId), ("sprk_regardingmatter", Matter, MatterId));
        _world.Set("sprk_decisionrecord", record, "sprk_steps", StepsListing(signals));
        return record;
    }

    [Fact(DisplayName = "194b V-fix must fire (verifier A): an ordinary Decision Record whose listed Signal cannot be read is FAILED by the ordinary pass — no ordinary owner on part of its question")]
    public async Task AnOrdinaryDecisionRecordWithAFaultedSignalRead_Fails_NotChanged()
    {
        MatterMovedToB();
        var otherEvent = Guid.NewGuid();
        Row("sprk_event", otherEvent, TeamRef(GeneralTeam)); // the unread Signal's subject (could have been secure)
        var signal = Guid.NewGuid();
        SignalAbout(signal, otherEvent);
        var record = OrdinaryDecisionRecordResolving(signal);
        _world.FailingIdListReadsOf("sprk_signal", signal);

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "not moved to B on part of its question");
        _world.OwnerWrites.Should().NotContain(w => w.Id == record);
        report.Changes.Should().ContainSingle(c => c.Id == record)
            .Which.Should().Match<SecureChildRowChange>(c => c.Outcome == SecureChildRowOutcome.Failed && c.Detail!.Contains("incomplete"));
        report.Status.Should().Be(SecureChildReconcileStatus.Incomplete, "the root is carried and asked again");
    }

    [Fact(DisplayName = "194b V-fix must not fire: the same Decision Record with its whole question naming a SECURE subject is reported for the secure pass, not failed and not moved")]
    public async Task AnOrdinaryDecisionRecordWhoseWholeQuestionIsSecure_IsReported_ForTheSecurePass()
    {
        MatterMovedToB();
        var secureMatter = Guid.NewGuid();
        var secureEvent = Guid.NewGuid();
        _world.SecureRoot(Matter, secureMatter).SecureChild("sprk_event", secureEvent, ("sprk_regardingmatter", Matter, secureMatter));
        var signal = Guid.NewGuid();
        SignalAbout(signal, secureEvent);
        var record = OrdinaryDecisionRecordResolving(signal);

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerWrites.Should().NotContain(w => w.Id == record);
        report.Changes.Should().ContainSingle(c => c.Id == record)
            .Which.Should().Match<SecureChildRowChange>(c => c.Outcome == SecureChildRowOutcome.Untouched
                                                             && c.Detail!.Contains("only the secure pass"));
    }

    [Fact(DisplayName = "194b V-fix must not fire: the same Decision Record with its whole question ordinary follows its matter to B")]
    public async Task AnOrdinaryDecisionRecordWhoseWholeQuestionIsOrdinary_FollowsItsMatter()
    {
        MatterMovedToB();
        var signal = Guid.NewGuid();
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));
        SignalAbout(signal, EventId);
        var record = OrdinaryDecisionRecordResolving(signal);

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(TeamB));
        report.Changes.Should().ContainSingle(c => c.Id == record).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    [Fact(DisplayName = "194b F1-d must fire: an Unsecure HOLDS a Decision Record whose question is incomplete in isolation, takes its mirror off and reports it held, while the Signal beside it is released")]
    public async Task AnUnsecure_HoldsADecisionRecord_AndReleasesTheSignal()
    {
        var unsecured = Guid.NewGuid();
        _world.FlaggedNotIsolatedRoot(Matter, unsecured);
        MatterMovedToB();
        var (record, signal) = (Guid.NewGuid(), Guid.NewGuid());
        // No sprk_steps: the Signals it resolved are not known, so its question is incomplete (fix round R).
        OntologyRow("sprk_decisionrecord", record, TeamRef(SecureChildShareWorld.SecureTeam), core: unsecured, subject: unsecured,
            ("sprk_matter", Matter, unsecured), ("sprk_regardingmatter", Matter, unsecured));
        OntologyRow("sprk_signal", signal, TeamRef(SecureChildShareWorld.SecureTeam), core: unsecured, subject: unsecured,
            ("sprk_matter", Matter, unsecured), ("sprk_regardingmatter", Matter, unsecured));
        _shares.Seed("sprk_decisionrecord", record, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _shares.Seed("sprk_signal", signal, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var report = await Reconciler().ReconcileAsync(
            Matter, unsecured, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        _world.OwnerWrites.Should().NotContain(w => w.Id == record);
        report.Changes.Should().ContainSingle(c => c.Id == record)
            .Which.Should().Match<SecureChildRowChange>(c => c.Outcome == SecureChildRowOutcome.Untouched && c.Detail!.Contains("F1-d"));
        _shares.MaskOf("sprk_decisionrecord", record, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the held record's mirror of the unsecured matter's sharees comes off, as a released record's does");
        report.HeldInIsolation.Should().ContainSingle().Which.Should().Match<SecureChildRowChange>(
            h => h.Id == record && h.Detail!.Contains("incomplete"));
        report.MirrorRemovalsIncomplete.Should().Be(0, "the held row's mirror came off whole");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "only a Decision Record is held");
        _shares.MaskOf("sprk_signal", signal, DataversePrincipalRef.User(Sharee)).Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // R4b-2b (F1): a Decision Record outside the root's lineage, reached through a Signal it resolved
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The ordinary matter M1 and its event E1: the core and subject of a Decision Record outside matter X's lineage.</summary>
    private static readonly Guid M1 = Guid.Parse("1b000000-0000-4000-8000-000000194601");
    private static readonly Guid E1 = Guid.Parse("1b000000-0000-4000-8000-000000194602");

    /// <summary>Matter X's event E2, and Signal B about it (closed during the review: listed, never linked).</summary>
    private static readonly Guid E2 = Guid.Parse("1b000000-0000-4000-8000-000000194603");
    private static readonly Guid SignalB = Guid.Parse("1b000000-0000-4000-8000-000000194604");

    private static string StepsListingIn(string idFormat, Guid signal) =>
        "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"resolvedSignalIds\":[\"" + signal.ToString(idFormat)
        + "\"],\"steps\":[]}";

    /// <summary>The Decision Record about E1 (core M1) that resolved Signal B, owned by <paramref name="owner"/>; its
    /// <c>sprk_steps</c> lists B in <paramref name="idFormat"/> (the writer's form is <c>D</c>).</summary>
    private Guid DecisionRecordAboutE1Resolving(Guid owner, string idFormat = "D")
    {
        _world.Add(Matter, M1, ("owningteam", TeamRef(GeneralTeam)), ("sprk_issecure", false));
        Row("sprk_event", E1, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, M1));
        var record = Guid.Parse("1b000000-0000-4000-8000-000000194605");
        OntologyRow("sprk_decisionrecord", record, TeamRef(owner), core: M1, subject: E1,
            ("sprk_matter", Matter, M1), ("sprk_regardingevent", "sprk_event", E1));
        _world.Set("sprk_decisionrecord", record, "sprk_steps", StepsListingIn(idFormat, SignalB));
        return record;
    }

    /// <summary>E2 filed under matter X and Signal B about E2, both owned by <paramref name="owner"/>.</summary>
    private void E2UnderXWithSignalB(Guid x, Guid owner)
    {
        Row("sprk_event", E2, TeamRef(owner), ("sprk_regardingmatter", Matter, x));
        Row("sprk_signal", SignalB, TeamRef(owner), ("sprk_regardingevent", "sprk_event", E2));
        _world.Set("sprk_signal", SignalB, "sprk_regardingrecordid", E2.ToString("D"));
    }

    [Fact(DisplayName = "194b R4b-2b F1 must fire: Make Secure on X moves IN a Decision Record outside X's lineage whose resolved Signal is about X's event (secure-if-any, D-123)")]
    public async Task MakeSecure_MovesInADecisionRecord_ThatResolvedASignalAboutTheSecureRecordsEvent()
    {
        var x = Guid.NewGuid();
        _world.SecureRoot(Matter, x);
        E2UnderXWithSignalB(x, GeneralTeam);
        var record = DecisionRecordAboutE1Resolving(GeneralTeam);

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _world.OwnerOf("sprk_event", E2).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "its question asks about E2, the subject of the Signal it resolved, and E2 is now Secure");
        report.Changes.Should().ContainSingle(c => c.Id == record).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    [Fact(DisplayName = "194b R4b-2b F1 must fire: an Unsecure of X releases a Decision Record isolated only by X's event, the subject of a Signal it resolved")]
    public async Task AnUnsecure_ReleasesADecisionRecord_IsolatedOnlyByTheUnsecuredRecordsEvent()
    {
        var x = Guid.NewGuid();
        _world.FlaggedNotIsolatedRoot(Matter, x);
        E2UnderXWithSignalB(x, SecureChildShareWorld.SecureTeam);
        var record = DecisionRecordAboutE1Resolving(SecureChildShareWorld.SecureTeam);

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_event", E2).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "its whole question is ordinary once E2 is: released to its core's team (criterion 3)");
        report.Changes.Should().ContainSingle(c => c.Id == record).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
        report.HeldInIsolation.Should().NotContain(h => h.Id == record);
    }

    [Fact(DisplayName = "194b R4b-2b F1 must fire: a Decision Record the text match cannot see (its steps list the Signal in another form) is reached through the Signal's own link")]
    public async Task MakeSecure_ReachesADecisionRecord_ThroughTheSignalsLink()
    {
        var x = Guid.NewGuid();
        _world.SecureRoot(Matter, x);
        E2UnderXWithSignalB(x, GeneralTeam);
        var record = DecisionRecordAboutE1Resolving(GeneralTeam, idFormat: "N");
        _world.Set("sprk_signal", SignalB, "sprk_decisionrecord", new EntityReference("sprk_decisionrecord", record));

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        report.Changes.Should().ContainSingle(c => c.Id == record).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    [Fact(DisplayName = "194b R4b-2b F1 must not fire: a Decision Record that resolved no Signal of the pass is not read into it")]
    public async Task MakeSecure_LeavesADecisionRecord_ThatResolvedNoSignalOfThePass()
    {
        var x = Guid.NewGuid();
        _world.SecureRoot(Matter, x);
        E2UnderXWithSignalB(x, GeneralTeam);
        var record = DecisionRecordAboutE1Resolving(GeneralTeam);
        _world.Set("sprk_decisionrecord", record, "sprk_steps", StepsListingIn("D", Guid.NewGuid()));

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        report.Changes.Should().NotContain(c => c.Id == record);
        report.Tables.Where(t => t.Table == "sprk_decisionrecord").Sum(t => t.Examined).Should().Be(0,
            "only a record that resolved a Signal of the pass is read into it");
    }

    private int DecisionRecordTextMatchReads() => _world.Queries.Count(q =>
        q.EntityName == "sprk_decisionrecord" && SecureChildShareWorld.HasLike(q.Criteria));

    [Fact(DisplayName = "R4b-2c F3a must not fire: a Decision Record whose steps mention the Signal OUTSIDE resolvedSignalIds is read in by the text match, decided by its own question, and stays put")]
    public async Task MakeSecure_AnOverMatchedDecisionRecord_IsDecidedByItsOwnQuestion_AndStays()
    {
        var x = Guid.NewGuid();
        _world.SecureRoot(Matter, x);
        E2UnderXWithSignalB(x, GeneralTeam);
        var record = DecisionRecordAboutE1Resolving(GeneralTeam);
        _world.Set("sprk_decisionrecord", record, "sprk_steps",
            "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"resolvedSignalIds\":[],\"steps\":[{\"note\":\"see signal "
            + SignalB.ToString("D") + "\"}]}");

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Tables.Where(t => t.Table == "sprk_decisionrecord").Sum(t => t.Examined).Should().Be(1,
            "the text match over-reads it into the pass");
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "its own question (core M1, subject E1, no resolved Signal) is ordinary");
        _world.OwnerWrites.Should().NotContain(w => w.Id == record);
        report.Changes.Should().NotContain(c => c.Id == record
            && (c.Outcome == SecureChildRowOutcome.Changed || c.Outcome == SecureChildRowOutcome.Refused
                || c.Outcome == SecureChildRowOutcome.Failed));
    }

    [Fact(DisplayName = "R4b-2c F3b: the ORDINARY-parent-move pass reads in a Decision Record outside the matter's lineage that resolved a Signal of the pass, and keeps it at its core's team")]
    public async Task TheOrdinaryPass_ReadsInADecisionRecord_ThatResolvedASignalOfThePass()
    {
        MatterMovedToB();
        E2UnderXWithSignalB(MatterId, GeneralTeam);
        var record = DecisionRecordAboutE1Resolving(GeneralTeam);

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Tables.Where(t => t.Table == "sprk_decisionrecord").Sum(t => t.Examined).Should().Be(1,
            "the discovery runs in the ordinary pass too");
        _world.OwnerOf("sprk_event", E2).Should().Be(DataversePrincipalRef.Team(TeamB), "E2 follows its matter");
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "its core M1 is its primary: the move of a resolved Signal's subject never moves it to B");
        _world.OwnerWrites.Should().NotContain(w => w.Id == record);
        report.Changes.Should().NotContain(c => c.Id == record && c.Outcome != SecureChildRowOutcome.AlreadyCorrect);
    }

    [Theory(DisplayName = "R4b-2c F3c must fire: a fault in the Decision Record steps read or the Signal-link read fails the pass — nothing written")]
    [InlineData("steps")]
    [InlineData("link")]
    public async Task MakeSecure_AFaultInTheDecisionRecordDiscovery_FailsThePass_AndWritesNothing(string which)
    {
        var x = Guid.NewGuid();
        _world.SecureRoot(Matter, x);
        E2UnderXWithSignalB(x, GeneralTeam);
        var record = DecisionRecordAboutE1Resolving(GeneralTeam, idFormat: which == "link" ? "N" : "D");
        _world.Set("sprk_signal", SignalB, "sprk_decisionrecord", new EntityReference("sprk_decisionrecord", record));
        if (which == "steps")
            _world.FailingLikeReadsOf("sprk_decisionrecord");
        else
            _world.FailingIdListReadsOf("sprk_decisionrecord", record);

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Failed, which);
        _world.OwnerWrites.Should().BeEmpty("no row is decided on a walk that could not be read whole");
        _world.OwnerOf("sprk_event", E2).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
    }

    [Fact(DisplayName = "R4b-2c F3d must fire: a Signal linked under a Decision Record the discovery found is walked below it, and decided by its own question")]
    public async Task MakeSecure_ASignalLinkedUnderAFoundDecisionRecord_IsWalked_AndDecidedByItsOwnQuestion()
    {
        var x = Guid.NewGuid();
        _world.SecureRoot(Matter, x);
        E2UnderXWithSignalB(x, GeneralTeam);
        var record = DecisionRecordAboutE1Resolving(GeneralTeam);
        // Reached ONLY through its link to the found record (sprk_signal.sprk_decisionrecord, a lineage lookup); its subject is
        // the ordinary E1. The link files nothing for ownership (a Decision Record is no ownership parent), so its own question
        // decides it.
        var signalC = Guid.NewGuid();
        Row("sprk_signal", signalC, TeamRef(GeneralTeam),
            ("sprk_decisionrecord", "sprk_decisionrecord", record), ("sprk_regardingevent", "sprk_event", E1));
        _world.Set("sprk_signal", signalC, "sprk_regardingrecordid", E1.ToString("D"));

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        report.Tables.Where(t => t.Table == "sprk_signal").Sum(t => t.Examined).Should().Be(2,
            "Signal B, and Signal C below the found Decision Record");
        _world.OwnerOf("sprk_signal", signalC).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "its own question (subject E1) is ordinary");
        _world.OwnerWrites.Should().NotContain(w => w.Id == signalC);
    }

    [Fact(DisplayName = "R4b-2c F3e: past 200 Signals the steps read is chunked — each read names at most 200 Signals, and a Decision Record that resolved one of the second chunk is found")]
    public async Task MakeSecure_FindsADecisionRecord_PastTheFirstChunkOfSignals()
    {
        var x = Guid.NewGuid();
        _world.SecureRoot(Matter, x);
        Row("sprk_event", E2, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, x));
        var signals = Enumerable.Range(0, SecureChildShareSynchronizer.DescendantConditionsPerQuery + 50)
            .Select(_ => Guid.NewGuid()).ToList();
        foreach (var signal in signals)
        {
            Row("sprk_signal", signal, TeamRef(GeneralTeam), ("sprk_regardingevent", "sprk_event", E2));
            _world.Set("sprk_signal", signal, "sprk_regardingrecordid", E2.ToString("D"));
        }

        var record = DecisionRecordAboutE1Resolving(GeneralTeam);
        _world.Set("sprk_decisionrecord", record, "sprk_steps", StepsListingIn("D", signals[^1]));

        var report = await Reconciler().ReconcileAsync(
            Matter, x, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        DecisionRecordTextMatchReads().Should().Be(2, "250 Signals: two chunks");
        _world.Queries.Where(q => q.EntityName == "sprk_decisionrecord" && SecureChildShareWorld.HasLike(q.Criteria))
            .Select(q => q.Criteria.Filters.Sum(f => f.Conditions.Count))
            .Should().OnlyContain(n => n <= SecureChildShareSynchronizer.DescendantConditionsPerQuery);
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "it resolved the last Signal, in the second chunk");
        report.Changes.Should().ContainSingle(c => c.Id == record).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    [Fact(DisplayName = "194b R4b-2b F3 must fire: an Unsecure RELEASES a Decision Record whose question is complete and all ordinary")]
    public async Task AnUnsecure_ReleasesACompleteAllOrdinaryDecisionRecord()
    {
        var unsecured = Guid.NewGuid();
        _world.FlaggedNotIsolatedRoot(Matter, unsecured);
        var ordinaryEvent = Guid.NewGuid();
        Row("sprk_event", ordinaryEvent, TeamRef(GeneralTeam));
        var signal = Guid.NewGuid();
        SignalAbout(signal, ordinaryEvent);
        var record = Guid.NewGuid();
        OntologyRow("sprk_decisionrecord", record, TeamRef(SecureChildShareWorld.SecureTeam), core: unsecured, subject: unsecured,
            ("sprk_matter", Matter, unsecured), ("sprk_regardingmatter", Matter, unsecured));
        _world.Set("sprk_decisionrecord", record, "sprk_steps", StepsListing(signal));
        _shares.Seed("sprk_decisionrecord", record, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var report = await Reconciler().ReconcileAsync(
            Matter, unsecured, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "a whole question leaves isolation as any other row's does; only an incomplete one is held");
        report.Changes.Should().ContainSingle(c => c.Id == record).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
        report.HeldInIsolation.Should().BeEmpty();
        _shares.MaskOf("sprk_decisionrecord", record, DataversePrincipalRef.User(Sharee)).Should().BeNull();
    }

    [Fact(DisplayName = "194b K3 must fire: an Unsecure never takes a no-core row the To Do job keeps OUT of isolation — held and reported with the job's rule")]
    public async Task AnUnsecure_HoldsANoCoreRowTheToDoJobKeeps()
    {
        var unsecured = Guid.NewGuid();
        _world.FlaggedNotIsolatedRoot(Matter, unsecured);
        Row("sprk_todo", TodoId, TeamRef(GeneralTeam)); // personal: the job keeps its rows
        var signal = Guid.NewGuid();
        OntologyRow("sprk_signal", signal, TeamRef(SecureChildShareWorld.SecureTeam), core: null, subject: TodoId,
            ("sprk_regardingtodo", "sprk_todo", TodoId), ("sprk_matter", Matter, unsecured));

        _shares.Seed("sprk_signal", signal, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var report = await Reconciler().ReconcileAsync(
            Matter, unsecured, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        _world.OwnerWrites.Should().NotContain(w => w.Id == signal);
        report.Changes.Should().ContainSingle(c => c.Id == signal)
            .Which.Should().Match<SecureChildRowChange>(c => c.Outcome == SecureChildRowOutcome.Untouched
                                                             && c.Detail!.Contains("TodoAssigneeOwnerReconciliationJob"));
        _shares.MaskOf("sprk_signal", signal, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "fix round R (A2): a row held by the To Do job's rule loses the unsecured matter's mirror too");
        report.HeldInIsolation.Should().ContainSingle().Which.Id.Should().Be(signal);
        report.MirrorRemovalsIncomplete.Should().Be(0, "the held row's mirror came off whole");
    }

    [Fact(DisplayName = "194b F1-b must fire: Make Secure on R moves a Signal filed under R INTO isolation even when its core's catalog read faults (a 429) — its stored parents are secure")]
    public async Task MakeSecure_MovesASignalIn_WhenItsQuestionCannotBeAsked()
    {
        var secured = Guid.NewGuid();
        _world.SecureRoot(Matter, secured);
        var signal = Guid.NewGuid();
        OntologyRow("sprk_signal", signal, TeamRef(GeneralTeam), core: secured, subject: secured,
            ("sprk_matter", Matter, secured), ("sprk_regardingmatter", Matter, secured));
        _world.FailingRowReadsOf("sprk_recordtype_ref", CatalogMatter);

        var report = await Reconciler().ReconcileAsync(
            Matter, secured, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        report.Changes.Should().ContainSingle(c => c.Id == signal).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    [Fact(DisplayName = "194b F1-b must not fire: an undetermined Signal whose stored parents are ordinary is NOT moved out by an Unsecure — it fails, left as it is")]
    public async Task AnUnsecure_DoesNotMoveAnUndeterminedSignalOut()
    {
        var unsecured = Guid.NewGuid();
        _world.FlaggedNotIsolatedRoot(Matter, unsecured);
        var signal = Guid.NewGuid();
        OntologyRow("sprk_signal", signal, TeamRef(SecureChildShareWorld.SecureTeam), core: unsecured, subject: unsecured,
            ("sprk_matter", Matter, unsecured), ("sprk_regardingmatter", Matter, unsecured));
        _world.FailingRowReadsOf("sprk_recordtype_ref", CatalogMatter);

        var report = await Reconciler().ReconcileAsync(
            Matter, unsecured, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        report.Changes.Should().ContainSingle(c => c.Id == signal).Which.Outcome.Should().Be(SecureChildRowOutcome.Failed);
    }

    [Theory(DisplayName = "194b must fire: an Unsecure takes a Signal OUT to its core's business-unit team, whatever order its lookups were read in")]
    [InlineData(true)]  // its To Do (owned in B) is read first: before 194b the Signal went OUT to B's team
    [InlineData(false)]
    public async Task AnUnsecure_ReleasesASignal_ToItsCoresTeam_WhateverTheAttributeOrder(bool todoFirst)
    {
        var unsecured = Guid.NewGuid();
        _world.FlaggedNotIsolatedRoot(Matter, unsecured); // mid-unsecure: moved to the general unit's team, flag cleared last
        MatterMovedToB();
        Row("sprk_todo", TodoId, TeamRef(TeamB), ("sprk_regardingmatter", Matter, MatterId));
        var signal = Guid.NewGuid();
        var lookups = new[] { ("sprk_regardingtodo", "sprk_todo", TodoId), ("sprk_matter", Matter, unsecured) };
        OntologyRow("sprk_signal", signal, TeamRef(SecureChildShareWorld.SecureTeam), core: unsecured, subject: TodoId,
            todoFirst ? lookups : lookups.Reverse().ToArray());

        var report = await Reconciler().ReconcileAsync(
            Matter, unsecured, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        // (This harness's Web API double does not answer the root's platform-cascade rows; the pass is incomplete for them only.)
        report.Changes.Should().NotContain(c => c.Id == signal && c.Outcome != SecureChildRowOutcome.Changed);
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "its core (the unsecured matter) is its primary, as SignalWriter asks");
    }

    [Theory(DisplayName = "194b F1-a must fire: a To Do regarding the B matter, created from an UNFILED user-owned email, is already correct at B's team — the email is its carrier, not its record")]
    [InlineData(true)]  // the email lookup read first
    [InlineData(false)]
    public async Task AToDoRegardingTheMovedMatter_CreatedFromAnUnfiledEmail_IsAlreadyCorrect(bool emailFirst)
    {
        MatterMovedToB();
        var communication = Guid.NewGuid();
        _world.UserOwnedChild("sprk_communication", communication); // filed under nothing: its creator's (general) unit
        var lookups = new[] { ("sprk_regardingcommunication", "sprk_communication", communication), ("sprk_regardingmatter", Matter, MatterId) };
        Row("sprk_todo", TodoId, TeamRef(TeamB), emailFirst ? lookups : lookups.Reverse().ToArray());
        _world.Set("sprk_todo", TodoId, "sprk_regardingrecordid", MatterId.ToString("D")); // the Office writer's pair: the record

        await Reconciler().ReconcileOrdinaryParentMoveAsync(Matter, MatterId, SecureChildReconcileMode.Apply, 200, CancellationToken.None);

        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(TeamB),
            "OfficeService owns it by its record regarding (the matter), then the document or email");
        _world.OwnerWrites.Should().NotContain(w => w.Id == TodoId);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The job: the switch, the window, and the document's file in the same run
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "194: the switch defaults to off, under SecureChild:Reconciliation:OrdinaryMovesWritesEnabled; the move cap is 200")]
    public void TheSwitchesAndCaps()
    {
        SecureChildReconciliationJob.OrdinaryMovesWritesEnabledConfigKey.Should().Be("SecureChild:Reconciliation:OrdinaryMovesWritesEnabled");
        SecureChildReconciliationJob.MaxOrdinaryMovesPerRunConfigKey.Should().Be("SecureChild:Reconciliation:MaxOrdinaryMovesPerRun");
        SecureChildReconciliationJob.DefaultMaxOrdinaryMovesPerRun.Should().Be(200);
    }

    [Fact(DisplayName = "194 must not fire: with the switch off the job writes nothing and reports the planned moves")]
    public async Task TheJob_WithTheSwitchOff_ReportsAndWritesNothing()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var harness = new JobHarness(_world, _webApi);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: null);

        ordinary.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        ordinary.GetProperty("rootsListed").GetInt32().Should().Be(1);
        ordinary.GetProperty("wouldMove").GetInt32().Should().Be(3);
        ordinary.GetProperty("moved").GetInt32().Should().Be(0);
        _world.OwnerWrites.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "194 must fire: with the switch on the job moves the children of a reassigned matter; one reassigned long before the watermark is found by the catch-up sweep (F4-a)")]
    public async Task TheJob_WithTheSwitchOn_MovesTheChildren_AndTheSweepFindsAStaleMove()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var staleMatter = Guid.NewGuid();
        var staleEvent = Guid.NewGuid();
        MatterMovedToB(staleMatter); // reassigned long ago: outside every watermark window
        Row("sprk_event", staleEvent, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, staleMatter));
        var harness = new JobHarness(_world, _webApi);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: "true");

        ordinary.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeWrite);
        ordinary.GetProperty("rootsListed").GetInt32().Should().Be(1, "only the recent change is in the watermark window");
        ordinary.GetProperty("moved").GetInt32().Should().Be(4);
        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_event", staleEvent).Should().Be(DataversePrincipalRef.Team(TeamB),
            "the catch-up sweep walks every ordinary root once, so a move made before this feature is corrected too");
        var sweep = ordinary.GetProperty("sweep");
        sweep.GetProperty("ran").GetBoolean().Should().BeTrue();
        sweep.GetProperty("complete").GetBoolean().Should().BeTrue();
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "194 F4-a: the catch-up sweep's cursor advances a window a run and resumes; once complete, only the watermark decides")]
    public async Task TheSweepCursor_AdvancesAndResumes()
    {
        // Three stale matters, none in any watermark window, one root a run (MaxRootsPerRun = 1).
        var stale = new[] { Guid.Parse("2b000000-0000-4000-8000-000000194101"), Guid.Parse("2b000000-0000-4000-8000-000000194102"),
            Guid.Parse("2b000000-0000-4000-8000-000000194103") };
        var events = stale.Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < stale.Length; i++)
        {
            MatterMovedToB(stale[i]);
            Row("sprk_event", events[i], TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, stale[i]));
        }

        var harness = new JobHarness(_world, _webApi);

        var moved = new List<int>();
        for (var run = 0; run < 50; run++)
        {
            var (_, ordinary) = await harness.RunAsync(ordinaryWrites: "true", maxRoots: "1");
            moved.Add(ordinary.GetProperty("moved").GetInt32());
            ordinary.GetProperty("sweep").GetProperty("roots").GetInt32().Should().BeLessThanOrEqualTo(1, "one root a window");
            if (ordinary.GetProperty("sweep").GetProperty("complete").GetBoolean())
                break;
        }

        moved.Count.Should().BeGreaterThan(3, "the sweep needs a run per root, so it resumed across runs");
        moved.Sum().Should().Be(3, "each stale matter's event is moved exactly once, one window at a time");
        moved.Should().OnlyContain(m => m <= 1, "one root a run");
        foreach (var e in events)
            _world.OwnerOf("sprk_event", e).Should().Be(DataversePrincipalRef.Team(TeamB));
        harness.Last.GetProperty("ordinaryMoves").GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue();

        // Complete: the next run sweeps nothing.
        var (_, after) = await harness.RunAsync(ordinaryWrites: "true", maxRoots: "1");
        after.GetProperty("sweep").GetProperty("ran").GetBoolean().Should().BeFalse();
        after.GetProperty("rootsInRun").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "194 F4-a must not fire: a report-only sweep reports the stale move and writes nothing; turning writes on sweeps again")]
    public async Task AReportOnlySweep_WritesNothing_AndWritesOnSweepsAgain()
    {
        var staleMatter = Guid.NewGuid();
        var staleEvent = Guid.NewGuid();
        MatterMovedToB(staleMatter);
        Row("sprk_event", staleEvent, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, staleMatter));
        var harness = new JobHarness(_world, _webApi);

        var (_, reportOnly) = await harness.RunAsync(ordinaryWrites: null);
        var (_, reportOnlyAgain) = await harness.RunAsync(ordinaryWrites: null);

        reportOnly.GetProperty("wouldMove").GetInt32().Should().Be(1);
        reportOnly.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue();
        reportOnlyAgain.GetProperty("sweep").GetProperty("ran").GetBoolean().Should().BeFalse("one report of every root a mode");
        _world.OwnerWrites.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
        _world.OwnerOf("sprk_event", staleEvent).Should().Be(DataversePrincipalRef.Team(GeneralTeam));

        var (_, writing) = await harness.RunAsync(ordinaryWrites: "true");

        writing.GetProperty("sweep").GetProperty("ran").GetBoolean().Should().BeTrue("a report-only cover never stands in for a writing one");
        writing.GetProperty("moved").GetInt32().Should().Be(1);
        _world.OwnerOf("sprk_event", staleEvent).Should().Be(DataversePrincipalRef.Team(TeamB));
    }

    [Fact(DisplayName = "194 F4-b must fire: the job lists a reassigned service request and its children follow it")]
    public async Task TheJob_ListsAReassignedServiceRequest()
    {
        var (request, todo) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_servicerequest", request, ("owningteam", new EntityReference("team", TeamB)));
        _world.Modified("sprk_servicerequest", request, DateTime.UtcNow);
        Row("sprk_todo", todo, TeamRef(GeneralTeam), ("sprk_regardingservicerequest", "sprk_servicerequest", request));
        var harness = new JobHarness(_world, _webApi);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: "true");

        ordinary.GetProperty("rootsListed").GetInt32().Should().Be(1);
        _world.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(TeamB));
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "194 must not fire: the recent-changes emergency stop also stops the ordinary moves")]
    public async Task TheEmergencyStop_StopsTheOrdinaryMoves()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var harness = new JobHarness(_world, _webApi);

        var (_, ordinary) = await harness.RunAsync(ordinaryWrites: "true", recentWrites: false);

        ordinary.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        _world.OwnerWrites.Should().BeEmpty();
    }

    [Theory(DisplayName = "R4b-4 A-F1 must fire: a fault reading a hard-deny row's replaced default team fails that row only — the job run finishes, the settle and the cursors run")]
    [InlineData(null)]
    [InlineData("true")]
    public async Task TheJob_WhenAHardDenyRowsReplacedDefaultTeamCannotBeRead_FailsThatRowAndFinishesTheRun(string? ordinaryWrites)
    {
        // Unit C under the customer's top unit (which holds the restricted-content owner team); the matter moved there.
        var buC = Guid.NewGuid();
        var teamC = Guid.NewGuid();
        var author = Guid.NewGuid();
        var authorOwned = Guid.NewGuid();
        var defaultOwned = Guid.NewGuid();
        _world.Add("businessunit", buC, ("name", "Arbitration"),
                ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.CustomerBu)))
            .Team(teamC, buC, "Arbitration", isDefault: true, teamType: 0)
            .User(author, GeneralBu);
        _world.Add(Matter, MatterId, ("owningteam", new EntityReference("team", teamC)), ("sprk_issecure", false));
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        Row("sprk_policy", authorOwned, new EntityReference("systemuser", author), ("sprk_matter", Matter, MatterId));
        Row("sprk_policy", defaultOwned, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        _world.FailingRowReadsOf("team", teamC);
        var harness = new JobHarness(_world, _webApi);

        var (_, ordinary) = await harness.RunAsync(ordinaryWrites);

        ordinary.GetProperty("failed").GetInt32().Should().Be(1, "the user-owned policy whose owner could not be decided");
        ordinary.GetProperty(ordinaryWrites is null ? "wouldMove" : "moved").GetInt32().Should().Be(1,
            "the default-team policy beside it is still decided");
        ordinary.GetProperty("incompleteRoots").GetArrayLength().Should().Be(1);
        ordinary.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue("the sweep cursor is saved");
        if (ordinaryWrites is not null)
            ordinary.GetProperty("carriedRoots").GetInt32().Should().Be(1, "a writing run carries the incomplete root");
        harness.Last.TryGetProperty("makeSecureRelocations", out _).Should().BeTrue("the settle after the ordinary moves runs");
    }

    [Fact(DisplayName = "194 must fire: a capped run carries the rest and the next run finishes them")]
    public async Task ACappedRun_CarriesTheRest()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var harness = new JobHarness(_world, _webApi);

        var (first, run1) = await harness.RunAsync(ordinaryWrites: "true", maxMoves: "2");
        _world.Modified(Matter, MatterId, SecureChildShareWorld.RecordsLastModified); // the next run finds it only through the carry
        var (second, run2) = await harness.RunAsync(ordinaryWrites: "true", maxMoves: "2");

        run1.GetProperty("moved").GetInt32().Should().Be(2);
        run1.GetProperty("capReached").GetBoolean().Should().BeTrue();
        run1.GetProperty("carriedRoots").GetInt32().Should().Be(1);
        first.Success.Should().BeTrue("a capped root is carried, not failed: " + first.ErrorMessage);
        run2.GetProperty("retriedRoots").GetInt32().Should().Be(1);
        run2.GetProperty("moved").GetInt32().Should().Be(1);
        run2.GetProperty("carriedRoots").GetInt32().Should().Be(0);
        second.Success.Should().BeTrue(second.ErrorMessage);
    }

    [Fact(DisplayName = "194b must fire: past the 60 s time slice the RECENT roots not reached are carried, none dropped, and the next run walks them")]
    public async Task PastTheTimeSlice_RecentRootsAreCarried_AndWalkedNextRun()
    {
        var matters = new[] { Guid.Parse("3b000000-0000-4000-8000-000000194201"), Guid.Parse("3b000000-0000-4000-8000-000000194202"),
            Guid.Parse("3b000000-0000-4000-8000-000000194203") };
        var events = matters.Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < matters.Length; i++)
        {
            MatterMovedToB(matters[i]);
            _world.Modified(Matter, matters[i], DateTime.UtcNow.AddMinutes(i - 10));
            Row("sprk_event", events[i], TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, matters[i]));
        }

        // Every clock read is 35 s after the last: the first root is walked at 35 s, the slice is spent at 70 s.
        var time = new RefilePassRoundOneTests.SteppingTime { Step = TimeSpan.FromSeconds(35) };
        var harness = new JobHarness(_world, _webApi, time: time);

        var (first, run1) = await harness.RunAsync(ordinaryWrites: "true");
        foreach (var matter in matters)
            _world.Modified(Matter, matter, SecureChildShareWorld.RecordsLastModified); // found next run only through the carry
        time.Step = TimeSpan.Zero;
        var (second, run2) = await harness.RunAsync(ordinaryWrites: "true");

        SecureChildReconciliationJob.OrdinaryTimeSlice.Should().Be(TimeSpan.FromSeconds(60));
        run1.GetProperty("timeSliceReached").GetBoolean().Should().BeTrue();
        run1.GetProperty("moved").GetInt32().Should().Be(1, "only the first root was reached inside the slice");
        run1.GetProperty("carriedRoots").GetInt32().Should().Be(2, "the roots not reached are carried, never dropped");
        first.Success.Should().BeTrue("a root the slice stops is carried, not failed: " + first.ErrorMessage);
        run2.GetProperty("timeSliceReached").GetBoolean().Should().BeFalse();
        run2.GetProperty("retriedRoots").GetInt32().Should().Be(2);
        run2.GetProperty("moved").GetInt32().Should().Be(2);
        run2.GetProperty("carriedRoots").GetInt32().Should().Be(0);
        foreach (var e in events)
            _world.OwnerOf("sprk_event", e).Should().Be(DataversePrincipalRef.Team(TeamB));
        second.Success.Should().BeTrue(second.ErrorMessage);
    }

    [Fact(DisplayName = "194b must fire: past the time slice a SWEPT root not reached holds the sweep cursor; later runs walk it, each once")]
    public async Task PastTheTimeSlice_TheSweepCursorIsHeld_AndLaterRunsWalkTheRest()
    {
        var stale = new[] { Guid.Parse("3c000000-0000-4000-8000-000000194301"), Guid.Parse("3c000000-0000-4000-8000-000000194302"),
            Guid.Parse("3c000000-0000-4000-8000-000000194303") };
        var events = stale.Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < stale.Length; i++)
        {
            MatterMovedToB(stale[i]); // reassigned long ago: reached only by the catch-up sweep
            Row("sprk_event", events[i], TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, stale[i]));
        }

        var time = new RefilePassRoundOneTests.SteppingTime { Step = TimeSpan.FromSeconds(35) };
        var harness = new JobHarness(_world, _webApi, time: time);

        var (_, run1) = await harness.RunAsync(ordinaryWrites: "true");
        run1.GetProperty("timeSliceReached").GetBoolean().Should().BeTrue();
        run1.GetProperty("moved").GetInt32().Should().BeLessThan(3, "the slice stopped the sweep before every root");
        run1.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeFalse("the cursor is held, not moved past");

        time.Step = TimeSpan.Zero;
        var moved = run1.GetProperty("moved").GetInt32();
        for (var run = 0; run < 10 && !harness.Last.GetProperty("ordinaryMoves").GetProperty("sweep").GetProperty("complete").GetBoolean(); run++)
            moved += (await harness.RunAsync(ordinaryWrites: "true")).Ordinary.GetProperty("moved").GetInt32();

        moved.Should().Be(3, "each stale matter's event is moved exactly once: none was dropped, none walked twice");
        foreach (var e in events)
            _world.OwnerOf("sprk_event", e).Should().Be(DataversePrincipalRef.Team(TeamB));
    }

    // ── UAC task 194b R4-3 (B4): no carried root starves; the report-only cursor ─────────────────────────────────────

    private static readonly Guid[] StarvedMatters =
    [
        Guid.Parse("3d000000-0000-4000-8000-000000194401"), Guid.Parse("3d000000-0000-4000-8000-000000194402"),
        Guid.Parse("3d000000-0000-4000-8000-000000194403"),
    ];

    /// <summary>Three ordinary matters moved to B, changed in id order; the first has <paramref name="firstEvents"/> events in A,
    /// the others one each. Returns each matter's events.</summary>
    private Guid[][] ThreeMovedMatters(int firstEvents)
    {
        var events = new Guid[StarvedMatters.Length][];
        for (var i = 0; i < StarvedMatters.Length; i++)
        {
            MatterMovedToB(StarvedMatters[i]);
            _world.Modified(Matter, StarvedMatters[i], DateTime.UtcNow.AddMinutes(i - 10));
            events[i] = Enumerable.Range(0, i == 0 ? firstEvents : 1).Select(_ => Guid.NewGuid()).ToArray();
            foreach (var e in events[i])
                Row("sprk_event", e, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, StarvedMatters[i]));
        }

        return events;
    }

    [Fact(DisplayName = "194b R4-3 must fire: a carried root behind one that never finishes in a run is reached within N runs (least-recently-attempted first, at least one root a run)")]
    public async Task ACarriedRootStarvedByASliceAndACap_IsReachedWithinNRuns()
    {
        // The first matter needs four runs at one move a run; every clock read is past the slice, so each run walks only
        // the root it must. In (table, id) order the first matter would take every run until it finished.
        var events = ThreeMovedMatters(firstEvents: 4);
        var time = new RefilePassRoundOneTests.SteppingTime { Step = TimeSpan.FromSeconds(70) };
        var harness = new JobHarness(_world, _webApi, time: time);

        var (_, run1) = await harness.RunAsync(ordinaryWrites: "true", maxMoves: "1");
        foreach (var matter in StarvedMatters)
            _world.Modified(Matter, matter, SecureChildShareWorld.RecordsLastModified); // found from now on only through the carry
        for (var run = 2; run <= StarvedMatters.Length; run++)
            await harness.RunAsync(ordinaryWrites: "true", maxMoves: "1");

        run1.GetProperty("moved").GetInt32().Should().Be(1, "the slice is spent before the first root, which is walked anyway");
        run1.GetProperty("carriedRoots").GetInt32().Should().Be(3);
        for (var i = 0; i < StarvedMatters.Length; i++)
            events[i].Should().Contain(e => _world.OwnerOf("sprk_event", e) == DataversePrincipalRef.Team(TeamB),
                $"matter {i + 1} is reached within {StarvedMatters.Length} runs");
    }

    [Fact(DisplayName = "194b R4-3 must fire: the report-only cursor persists across runs: each report-only run resumes after the root the last one reached, and wraps around")]
    public async Task TheReportOnlyCursor_PersistsAcrossRuns_AndWrapsAround()
    {
        ThreeMovedMatters(firstEvents: 1);
        var time = new RefilePassRoundOneTests.SteppingTime { Step = TimeSpan.FromSeconds(70) };
        var harness = new JobHarness(_world, _webApi, time: time);
        static string Key(Guid matter) => $"{Matter}:{matter:D}";
        static string[] RootsOf(JsonElement run) =>
            run.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("root").GetString()!).Distinct().ToArray();

        var runs = new List<JsonElement>();
        for (var run = 0; run < 4; run++)
            runs.Add((await harness.RunAsync(ordinaryWrites: null)).Ordinary);

        runs.Select(RootsOf).Should().SatisfyRespectively(
            r => r.Should().Equal(Key(StarvedMatters[0])),
            r => r.Should().Equal(Key(StarvedMatters[1])),
            r => r.Should().Equal(Key(StarvedMatters[2])),
            r => r.Should().Equal(new[] { Key(StarvedMatters[0]) }, "after the last root it wraps to the first"));
        runs[0].GetProperty("reportCursor").GetString().Should().Be(Key(StarvedMatters[0]));
        runs[1].GetProperty("reportCursor").GetString().Should().Be(Key(StarvedMatters[1]));
        _world.OwnerWrites.Should().BeEmpty("report-only");
    }

    [Fact(DisplayName = "194b R4-3 must not fire: a report-only run that reaches every root leaves no cursor")]
    public async Task AReportOnlyRunThatReachesEveryRoot_LeavesNoCursor()
    {
        ThreeMovedMatters(firstEvents: 1);
        var harness = new JobHarness(_world, _webApi, time: new RefilePassRoundOneTests.SteppingTime());

        var (_, run) = await harness.RunAsync(ordinaryWrites: null);

        run.GetProperty("timeSliceReached").GetBoolean().Should().BeFalse();
        run.GetProperty("wouldMove").GetInt32().Should().Be(3);
        run.GetProperty("reportCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "194b R4-3 must not fire: an empty carry set (nothing changed, nothing carried) is a no-op")]
    public async Task AnEmptyCarrySet_IsANoOp()
    {
        var time = new RefilePassRoundOneTests.SteppingTime { Step = TimeSpan.FromSeconds(70) };
        var harness = new JobHarness(_world, _webApi, time: time);

        var (result, run) = await harness.RunAsync(ordinaryWrites: "true");

        run.GetProperty("retriedRoots").GetInt32().Should().Be(0);
        run.GetProperty("rootsInRun").GetInt32().Should().Be(0);
        run.GetProperty("carriedRoots").GetInt32().Should().Be(0);
        run.GetProperty("timeSliceReached").GetBoolean().Should().BeFalse();
        run.GetProperty("reportCursor").ValueKind.Should().Be(JsonValueKind.Null);
        _world.OwnerWrites.Should().BeEmpty();
        result.Success.Should().BeTrue(result.ErrorMessage);
        SecureChildReconciliationJob.OrderLeastRecentlyAttempted([], new Dictionary<(string, Guid), long>()).Should().BeEmpty();
    }

    [Fact(DisplayName = "194 must fire: a re-owned document's file moves to the new business unit's container in the same run (the re-file pass)")]
    public async Task TheJob_ReownsTheDocument_AndItsFileFollows()
    {
        // The pointer world: the document is filed under the plain matter, whose container (derived from its owning business
        // unit) is CustomerA1Container; its file still sits in CustomerBContainer, the container of the unit it left.
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);

        // The job's world: the same matter, reassigned to B; the document still owned by A's default team.
        _world.Add("businessunit", T.CustomerA, ("sprk_containerid", T.CustomerAContainer));
        MatterMovedToB(T.PlainMatter);
        _world.Modified(Matter, T.PlainMatter, DateTime.UtcNow);
        Row("sprk_document", T.DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, T.PlainMatter));
        _world.Set("sprk_document", T.DocumentId, "sprk_graphdriveid", T.CustomerBContainer);
        var harness = new JobHarness(_world, _webApi, rig);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: "true");
        var refile = harness.Last.GetProperty("refileRelocations");

        ordinary.GetProperty("moved").GetInt32().Should().Be(1);
        _world.OwnerOf("sprk_document", T.DocumentId).Should().Be(DataversePrincipalRef.Team(TeamB));
        R.Ids(refile, "movedDocuments").Should().Equal(T.DocumentId);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "194 F1-a (existing A3): a USER-owned document directly under a moved matter is not re-owned, and its file still moves")]
    public async Task TheJob_MovesTheFileOfAUserOwnedDocumentDirectlyUnderAMovedMatter()
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);

        _world.Add("businessunit", T.CustomerA, ("sprk_containerid", T.CustomerAContainer));
        MatterMovedToB(T.PlainMatter);
        _world.Modified(Matter, T.PlainMatter, DateTime.UtcNow);
        Row("sprk_document", T.DocumentId, new EntityReference("systemuser", SecureChildShareWorld.SomeUser),
            ("sprk_matter", Matter, T.PlainMatter));
        _world.Set("sprk_document", T.DocumentId, "sprk_graphdriveid", T.CustomerBContainer);
        var harness = new JobHarness(_world, _webApi, rig);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: "true");
        var refile = harness.Last.GetProperty("refileRelocations");

        ordinary.GetProperty("moved").GetInt32().Should().Be(0, "a user-owned row is reported, not re-owned");
        _world.OwnerOf("sprk_document", T.DocumentId).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        R.Ids(refile, "movedDocuments").Should().Equal(new[] { T.DocumentId },
            "the re-file pass lists every document filed directly under a changed matter, whatever its owner (A3)");
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    /// <summary>F1-a: the pointer world — the document filed (for its derivation) under the plain matter, whose container is
    /// CustomerA1Container; its file still in CustomerBContainer. The job world says how it is filed.</summary>
    private T.Rig MisplacedFileRig()
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        _world.Add("businessunit", T.CustomerA, ("sprk_containerid", T.CustomerAContainer));
        return new T.Rig(pointer);
    }

    private static EntityReference SomeUser => new("systemuser", SecureChildShareWorld.SomeUser);

    [Fact(DisplayName = "194 F1-a must fire: a USER-owned document under an EVENT of a moved matter has its file moved (handed to the re-file pass)")]
    public async Task TheJob_MovesTheFileOfAUserOwnedDocumentUnderAnEventOfAMovedMatter()
    {
        var rig = MisplacedFileRig();
        MatterMovedToB(T.PlainMatter);
        _world.Modified(Matter, T.PlainMatter, DateTime.UtcNow);
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, T.PlainMatter));
        Row("sprk_document", T.DocumentId, SomeUser, ("sprk_relatedevent", "sprk_event", EventId));
        _world.Set("sprk_document", T.DocumentId, "sprk_graphdriveid", T.CustomerBContainer);
        var harness = new JobHarness(_world, _webApi, rig);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: "true");
        var refile = harness.Last.GetProperty("refileRelocations");

        _world.OwnerOf("sprk_document", T.DocumentId).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "a user-owned document is reported, not re-owned");
        ordinary.GetProperty("documentsHandedOff").GetInt32().Should().Be(1);
        R.Ids(refile, "movedDocuments").Should().Equal(new[] { T.DocumentId },
            "below the matter's direct children only the hand-off reaches it (the re-file pass's A3 lists direct children)");
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "194 F1-a back-pressure: with the carry nearly full a swept root is deferred and the sweep HOLDS on it; the next run hands off the rest and moves the file — nothing lost")]
    public async Task WithTheCarryNearlyFull_ASweptRootIsDeferred_AndCompletesOnALaterRun()
    {
        // Stale: the matter is found only by the catch-up sweep. Two documents; the one with a misplaced file sorts LAST, so
        // it is the one left over when only one fits.
        var rig = MisplacedFileRig();
        var firstDocument = Guid.Parse("1c000000-0000-4000-8000-0000000194f1");
        MatterMovedToB(T.PlainMatter);
        Row("sprk_document", firstDocument, SomeUser, ("sprk_matter", Matter, T.PlainMatter));
        Row("sprk_document", T.DocumentId, SomeUser, ("sprk_matter", Matter, T.PlainMatter));
        _world.Set("sprk_document", T.DocumentId, "sprk_graphdriveid", T.CustomerBContainer);
        var harness = new JobHarness(_world, _webApi, rig);
        harness.SeedRefileCarry(SecureChildReconciliationJob.MaxCarriedRows - 1);

        var (_, run1) = await harness.RunAsync(ordinaryWrites: "true");

        run1.GetProperty("documentsHandedOff").GetInt32().Should().Be(1, "only one place was free");
        run1.GetProperty("deferredRoots").EnumerateArray().Select(r => r.GetString()).Should()
            .Equal($"{Matter}:{T.PlainMatter:D}");
        run1.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeFalse("the sweep holds on the deferred root");
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer, "its document was not handed off yet");

        var (result2, run2) = await harness.RunAsync(ordinaryWrites: "true");

        run2.GetProperty("deferredRoots").GetArrayLength().Should().Be(0);
        run2.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue();
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container, "the deferred document was handed off and moved");
        result2.Success.Should().BeTrue(result2.ErrorMessage);
    }

    [Fact(DisplayName = "194 F1-a back-pressure: a RECENT root deferred for want of room is carried and completes on a later run")]
    public async Task WithTheCarryNearlyFull_ARecentRootIsCarried_AndCompletesOnALaterRun()
    {
        var rig = MisplacedFileRig();
        var firstDocument = Guid.Parse("1c000000-0000-4000-8000-0000000194f2");
        MatterMovedToB(T.PlainMatter);
        _world.Modified(Matter, T.PlainMatter, DateTime.UtcNow);
        Row("sprk_event", EventId, SomeUser, ("sprk_regardingmatter", Matter, T.PlainMatter));
        Row("sprk_document", firstDocument, SomeUser, ("sprk_relatedevent", "sprk_event", EventId));
        Row("sprk_document", T.DocumentId, SomeUser, ("sprk_relatedevent", "sprk_event", EventId));
        _world.Set("sprk_document", T.DocumentId, "sprk_graphdriveid", T.CustomerBContainer);
        var harness = new JobHarness(_world, _webApi, rig);
        harness.SeedRefileCarry(SecureChildReconciliationJob.MaxCarriedRows - 1);

        var (_, run1) = await harness.RunAsync(ordinaryWrites: "true");
        _world.Modified(Matter, T.PlainMatter, SecureChildShareWorld.RecordsLastModified); // found next run only through the carry

        run1.GetProperty("deferredRoots").GetArrayLength().Should().Be(1);
        run1.GetProperty("carriedRoots").GetInt32().Should().Be(1, "a deferred root is carried, never dropped");
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer);

        var (result2, run2) = await harness.RunAsync(ordinaryWrites: "true");

        run2.GetProperty("retriedRoots").GetInt32().Should().Be(1);
        run2.GetProperty("deferredRoots").GetArrayLength().Should().Be(0);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        result2.Success.Should().BeTrue(result2.ErrorMessage);
    }

    [Fact(DisplayName = "194 F1-a must not fire: in a mixed tree a Secure-owned document, and one the rule answers Secure, are never handed off")]
    public async Task InAMixedTree_SecureDocumentsAreNeverHandedOff()
    {
        var (ordinaryDocument, isolatedDocument, familyDocument, secureAnswerDocument) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var secureMatter = Guid.NewGuid();
        MatterMovedToB();
        _world.SecureRoot(Matter, secureMatter);
        Row("sprk_document", ordinaryDocument, SomeUser, ("sprk_matter", Matter, MatterId));
        Row("sprk_document", isolatedDocument, TeamRef(_world.OwnerTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_document", familyDocument, TeamRef(SecureFamilyTeam), ("sprk_matter", Matter, MatterId));
        // Filed under the moved matter AND a Secure matter: the rule answers Secure (secure-if-any), and its container
        // answer is the Secure matter's.
        Row("sprk_document", secureAnswerDocument, TeamRef(GeneralTeam),
            ("sprk_matter", Matter, MatterId), ("sprk_relatedmatter", Matter, secureMatter));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.OrdinaryHandOffDocumentIds.Should().Equal(new[] { ordinaryDocument },
            "never a document held in isolation or the Secure Record business unit, nor one whose answer is a Secure record's");
        _world.OwnerOf("sprk_document", isolatedDocument).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam));
        _world.OwnerOf("sprk_document", secureAnswerDocument).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Doubles
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The REAL job over this world, with the conditional Web API double (and, when given, task 166's relocator).</summary>
    private sealed class JobHarness
    {
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly SecureChildReconciliationJob _job;

        public JobHarness(SecureChildShareWorld world, ConditionalOwnerWebApi webApi, T.Rig? rig = null, TimeProvider? time = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => world).Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), webApi));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => world, new FakeRecordShareTable()));
            if (rig is not null)
            {
                services.AddScoped(_ => rig.Relocator);
                services.AddScoped(_ => rig.Resolver);
            }

            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => world));
            var provider = services.BuildServiceProvider();
            foreach (var (key, value) in SecureChildShareWorld.Configuration().AsEnumerable())
                _configuration[key] = value;
            _job = new SecureChildReconciliationJob(
                provider.GetRequiredService<IServiceScopeFactory>(), time ?? TimeProvider.System, _configuration,
                NullLogger<SecureChildReconciliationJob>.Instance);
        }

        public JsonElement Last { get; private set; }

        /// <summary>
        /// F1-a back-pressure: fills the re-file pass's carried rows (per-instance state, no other seam) with
        /// <paramref name="count"/> ids no row has, as earlier runs' carries would. The re-file pass drops them in its next run
        /// (a carried id it cannot read back has nothing to move).
        /// </summary>
        public void SeedRefileCarry(int count)
        {
            var field = typeof(SecureChildReconciliationJob).GetField(
                "_refilePending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var carry = (HashSet<Guid>)field.GetValue(_job)!;
            for (var i = 0; i < count; i++)
                carry.Add(Guid.NewGuid());
        }

        public async Task<(JobRunResult Result, JsonElement Ordinary)> RunAsync(
            string? ordinaryWrites, bool recentWrites = true, string? maxMoves = null, string? maxRoots = null)
        {
            _configuration[SecureChildReconciliationJob.OrdinaryMovesWritesEnabledConfigKey] = ordinaryWrites;
            _configuration[SecureChildReconciliationJob.RecentChangesWritesEnabledConfigKey] = recentWrites ? null : "false";
            _configuration[SecureChildReconciliationJob.MaxOrdinaryMovesPerRunConfigKey] = maxMoves;
            if (maxRoots is not null)
                _configuration[SecureChildReconciliationJob.MaxRootsPerRunConfigKey] = maxRoots;
            var result = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
                CancellationToken.None);
            using var doc = JsonDocument.Parse(result.ResultJson!);
            Last = doc.RootElement.Clone();
            return (result, Last.GetProperty("ordinaryMoves"));
        }
    }

    /// <summary>
    /// The conditional owner write of the Web API (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>) over the world's rows
    /// and their <c>versionnumber</c> (as task 183's tests answer it): a stale version is Dataverse's 412, a missing row its
    /// 404; a write that lands bumps the version and, as Dataverse does, the row's <c>modifiedon</c>.
    /// </summary>
    internal sealed class ConditionalOwnerWebApi : DataverseWebApiClient
    {
        private readonly Func<SecureChildShareWorld> _world;

        public ConditionalOwnerWebApi(Func<SecureChildShareWorld> world)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
            => _world = world;

        public List<(string EntitySet, Guid Id, string ETag)> ConditionalWrites { get; } = new();

        public override Task UpdateIfMatchAsync(
            string entitySetName, Guid id, object entity, string etag, CancellationToken cancellationToken = default)
        {
            ConditionalWrites.Add((entitySetName, id, etag));
            var table = SecureChildLineage.Children.Values.Single(t => t.EntitySet == entitySetName).LogicalName;
            if (!_world().Has(table, id))
                throw new KeyNotFoundException($"{entitySetName}({id}) was not found; nothing was created.");
            if (etag != $"W/\"{_world().ValueOf<long>(table, id, "versionnumber")}\"")
                throw new System.Data.DBConcurrencyException($"{entitySetName}({id}) changed since it was read.");
            var fields = (IDictionary<string, object>)entity;
            fields.Keys.Should().Equal(new[] { "ownerid@odata.bind" }, "the re-own writes the owner and nothing else");
            var bind = (string)fields["ownerid@odata.bind"];
            var team = Guid.Parse(bind["/teams(".Length..^1]);
            _world().Update(table, id, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", team) });
            _world().Set(table, id, "versionnumber", _world().ValueOf<long>(table, id, "versionnumber") + 1);
            _world().Modified(table, id, DateTime.UtcNow);
            return Task.CompletedTask;
        }
    }

    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }

    // ── Owner decision 2026-10-29: the children of a FILED person-owned work assignment follow its matter ──────────────

    private static readonly Guid CatalogWorkAssignment = Guid.Parse("4b000000-0000-4000-8000-000000194c02");

    /// <summary>A work assignment owned by <paramref name="owner"/>: filed under the root (the matter by default,
    /// <paramref name="rootTable"/> otherwise) by its typed lookup, through an invoice (<paramref name="throughAnInvoice"/>) or an
    /// event (<paramref name="throughAnEvent"/>) of the root, or (<paramref name="filed"/> false) under nothing.</summary>
    private Guid WorkAssignmentUnderTheMatter(
        EntityReference owner, bool throughAnInvoice = false, bool filed = true,
        string rootTable = Matter, Guid? rootId = null, bool throughAnEvent = false)
    {
        var root = rootId ?? MatterId;
        var workAssignment = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (owner.LogicalName == "team" ? "owningteam" : "owninguser", owner), ("sprk_issecure", false),
        };
        if (filed && throughAnInvoice)
        {
            var invoice = Guid.NewGuid();
            Row("sprk_invoice", invoice, TeamRef(GeneralTeam), (rootTable, rootTable, root));
            columns.Add(("sprk_regardinginvoice", new EntityReference("sprk_invoice", invoice)));
        }
        else if (filed && throughAnEvent)
        {
            var intermediateEvent = Guid.NewGuid();
            Row("sprk_event", intermediateEvent, TeamRef(GeneralTeam), ("sprk_regarding" + rootTable["sprk_".Length..], rootTable, root));
            columns.Add(("sprk_regardingevent", new EntityReference("sprk_event", intermediateEvent)));
        }
        else if (filed)
        {
            columns.Add(("sprk_regarding" + rootTable["sprk_".Length..], new EntityReference(rootTable, root)));
        }

        _world.Add("sprk_workassignment", workAssignment, columns.ToArray());
        return workAssignment;
    }

    /// <summary>The root (<see cref="Matter"/> or <see cref="Project"/>), ordinary, reassigned to business unit B.</summary>
    private Guid RootMovedToB(string rootTable)
    {
        var id = rootTable == Matter ? MatterId : Guid.NewGuid();
        _world.Add(rootTable, id, ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false));
        return id;
    }

    [Theory(DisplayName = "Owner decision 2026-10-29 must fire: the MATTER's or PROJECT's move re-owns the rows under a FILED person-owned work assignment (typed, or through an invoice or an event of the root) — a default-team-owned Decision Record and event move to its unit; a user-owned row is reported, not moved")]
    [InlineData(Matter, "typed")]
    [InlineData(Matter, "invoice")]
    [InlineData(Matter, "event")]
    [InlineData(Project, "typed")]
    [InlineData(Project, "invoice")]
    [InlineData(Project, "event")]
    public async Task ChildrenOfAFiledPersonOwnedWorkAssignment_FollowItsRoot_AUserOwnedRowIsReported(string rootTable, string filing)
    {
        var rootId = RootMovedToB(rootTable);
        var someone = new EntityReference("systemuser", SecureChildShareWorld.SomeUser);
        var (record, userEvent) = (Guid.NewGuid(), Guid.NewGuid());
        var workAssignment = WorkAssignmentUnderTheMatter(someone,
            throughAnInvoice: filing == "invoice", throughAnEvent: filing == "event", rootTable: rootTable, rootId: rootId);
        _world.Add("sprk_recordtype_ref", CatalogWorkAssignment, ("sprk_recordlogicalname", "sprk_workassignment"));
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));
        Row("sprk_event", userEvent, someone, ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));
        Row("sprk_decisionrecord", record, TeamRef(GeneralTeam),
            ("sprk_workassignment", "sprk_workassignment", workAssignment),
            ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment),
            ("sprk_corerecordtype", "sprk_recordtype_ref", CatalogWorkAssignment));
        _world.Set("sprk_decisionrecord", record, "sprk_corerecordid", workAssignment.ToString("D"));
        _world.Set("sprk_decisionrecord", record, "sprk_regardingrecordid", workAssignment.ToString("D"));
        _world.Set("sprk_decisionrecord", record, "sprk_steps", JsonSerializer.Serialize(new
        {
            reviewId = Guid.NewGuid().ToString("D"), resolvedSignalIds = Array.Empty<string>(), steps = Array.Empty<object>(),
        }));

        // Driven from the ROOT's pass: the work assignment did not change, so its own pass never runs.
        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            rootTable, rootId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(TeamB),
            "the work assignment belongs to its matter: its default-team child follows the matter to B");
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(TeamB),
            "a Decision Record whose core is the work assignment follows the matter too");
        _world.OwnerOf("sprk_event", userEvent).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        _world.OwnerWrites.Should().NotContain(w => w.Id == userEvent);
        _world.OwnerWrites.Should().NotContain(w => w.Id == workAssignment, "the work assignment is a hop, never a row of the pass");
        report.Changes.Should().ContainSingle(c => c.Id == userEvent).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.TargetTeamId == TeamB && c.Detail!.Contains("owned by a user"));
    }

    [Theory(DisplayName = "Owner decision 2026-10-29 must not fire: the matter's move never reaches the rows under a TEAM-owned work assignment (it keeps its own unit) or an UNFILED person-owned one")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChildrenOfATeamOwnedOrUnfiledWorkAssignment_AreNotReachedByTheMattersMove(bool teamOwned)
    {
        MatterMovedToB();
        var workAssignment = teamOwned
            ? WorkAssignmentUnderTheMatter(TeamRef(GeneralTeam))
            : WorkAssignmentUnderTheMatter(new EntityReference("systemuser", SecureChildShareWorld.SomeUser), filed: false);
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        report.Tables.Where(t => t.Table == "sprk_event").Sum(t => t.Examined).Should().Be(0,
            teamOwned ? "a team-owned work assignment keeps its own unit: its rows are not the matter's to move"
                      : "a work assignment filed under nothing is not under the matter");
        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerWrites.Should().BeEmpty();
    }

    /// <summary>An ordinary matter owned by <paramref name="team"/> (its business unit's default team).</summary>
    private Guid OrdinaryMatterOwnedBy(Guid team)
    {
        var matter = Guid.NewGuid();
        _world.Add(Matter, matter, ("owningteam", TeamRef(team)), ("sprk_issecure", false));
        return matter;
    }

    /// <summary>A person-owned work assignment with the filing lookups given (none typed when no matter or project is named).</summary>
    private Guid PersonOwnedWorkAssignment(params (string Column, string Target, Guid TargetId)[] filing)
    {
        var workAssignment = Guid.NewGuid();
        Row("sprk_workassignment", workAssignment, new EntityReference("systemuser", SecureChildShareWorld.SomeUser), filing);
        _world.Set("sprk_workassignment", workAssignment, "sprk_issecure", false);
        return workAssignment;
    }

    [Fact(DisplayName = "Owner decision 2026-10-29 must not fire: a work assignment whose OWN matter is M2 (unit B) and that is also filed through an invoice of M1 — M1's pass leaves its default-team row in B (already correct, no write), never M1's unit")]
    public async Task ARowUnderAWorkAssignmentOfAnotherMatter_ReachedThroughAnInvoiceOfTheMovedMatter_StaysWithItsOwnMatter()
    {
        // M1 (the walked root) is in the general unit; M2, the work assignment's own (typed) matter, is in B.
        var m1 = OrdinaryMatterOwnedBy(GeneralTeam);
        var m2 = OrdinaryMatterOwnedBy(TeamB);
        var invoice = Guid.NewGuid();
        Row("sprk_invoice", invoice, TeamRef(GeneralTeam), ("sprk_matter", Matter, m1));
        var workAssignment = PersonOwnedWorkAssignment(
            ("sprk_regardingmatter", Matter, m2), ("sprk_regardinginvoice", "sprk_invoice", invoice));
        Row("sprk_event", EventId, TeamRef(TeamB), ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, m1, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        var events = report.Tables.Should().ContainSingle(t => t.Table == "sprk_event").Subject;
        events.Examined.Should().Be(1, "the work assignment is reached through M1's invoice, so its row is decided");
        events.AlreadyCorrect.Should().Be(1,
            "the row asks its own question: the work assignment's own matter M2 is its primary, so B is already right");
        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(TeamB),
            $"never M1's unit ({GeneralTeam:D}): the walked root does not decide a hop's rows");
        _world.OwnerWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "Owner decision 2026-10-29 must not fire: a work assignment with no typed parent, filed through an invoice of M1 (general unit) and an event of M2 (moved to B) — M2's pass gives its rows the resolver's primary (the invoice's matter M1), never M2's unit")]
    public async Task RowsUnderAWorkAssignmentFiledThroughTwoIntermediates_FollowTheResolversPrimary_NotTheWalkedRoot()
    {
        // The resolver's look-through names the invoice's matter before the event's (IntermediateFilingColumns: invoice, then
        // event), both under the work assignment's matter column: M1 is its primary.
        var m1 = OrdinaryMatterOwnedBy(GeneralTeam);
        var m2 = OrdinaryMatterOwnedBy(TeamB);
        var (invoice, intermediateEvent) = (Guid.NewGuid(), Guid.NewGuid());
        Row("sprk_invoice", invoice, TeamRef(GeneralTeam), ("sprk_matter", Matter, m1));
        Row("sprk_event", intermediateEvent, TeamRef(TeamB), ("sprk_regardingmatter", Matter, m2));
        var workAssignment = PersonOwnedWorkAssignment(
            ("sprk_regardinginvoice", "sprk_invoice", invoice), ("sprk_regardingevent", "sprk_event", intermediateEvent));
        var (inA, inB) = (Guid.NewGuid(), Guid.NewGuid());
        Row("sprk_todo", inA, TeamRef(GeneralTeam), ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));
        Row("sprk_todo", inB, TeamRef(TeamB), ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, m2, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        report.Tables.Should().ContainSingle(t => t.Table == "sprk_todo").Which.Examined.Should().Be(2,
            "the work assignment is reached through M2's event, so both its rows are decided");
        _world.OwnerOf("sprk_todo", inA).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            $"its primary is M1 (the invoice's matter): already right, never moved to M2's unit ({TeamB:D})");
        _world.OwnerWrites.Should().NotContain(w => w.Id == inA);
        _world.OwnerOf("sprk_todo", inB).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "a default-team row out of step with its primary follows the primary, not the walked root");
        _world.OwnerWrites.Should().NotContain(w => w.Id == workAssignment, "the work assignment is a hop, never a row of the pass");
    }

    private static IEnumerable<object> ConditionValuesOf(FilterExpression filter) =>
        filter.Conditions.SelectMany(c => c.Values).Concat(filter.Filters.SelectMany(ConditionValuesOf));

    [Fact(DisplayName = "Owner decision 2026-10-29: the work-assignment hop reads only the root's own — a person-owned work assignment filed under an UNRELATED matter's invoice is never listed and that invoice never read; a team-owned one under the root is not listed either")]
    public async Task TheWorkAssignmentHop_ReadsNoWorkAssignmentOrIntermediateOutsideTheRoot()
    {
        MatterMovedToB();
        var someone = new EntityReference("systemuser", SecureChildShareWorld.SomeUser);
        var related = WorkAssignmentUnderTheMatter(someone, throughAnInvoice: true);
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingworkassignment", "sprk_workassignment", related));
        var teamOwned = WorkAssignmentUnderTheMatter(TeamRef(GeneralTeam), throughAnInvoice: true);

        var unrelatedMatter = OrdinaryMatterOwnedBy(GeneralTeam);
        var unrelatedInvoice = Guid.NewGuid();
        Row("sprk_invoice", unrelatedInvoice, TeamRef(GeneralTeam), ("sprk_matter", Matter, unrelatedMatter));
        var unrelated = PersonOwnedWorkAssignment(("sprk_regardinginvoice", "sprk_invoice", unrelatedInvoice));
        var before = _world.Queries.Count;

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(TeamB),
            "the root's own person-owned work assignment is still reached through its invoice");

        var pass = _world.Queries.Skip(before).ToList();
        pass.Where(q => q.EntityName == "sprk_invoice").SelectMany(q => ConditionValuesOf(q.Criteria))
            .Should().NotContain(unrelatedInvoice, "an invoice outside the root is never read");
        var hopQueries = pass.Where(q => q.EntityName == "sprk_workassignment").ToList();
        hopQueries.Should().NotBeEmpty();
        foreach (var query in hopQueries)
        {
            query.PageInfo = null; // replayed whole: every row the query names
            _world.Answer(query).Entities.Select(e => e.Id).Should()
                .NotContain(unrelated, "a work assignment filed under another matter's invoice is not the root's to list")
                .And.NotContain(teamOwned, "a team-owned work assignment is left out by the query itself");
        }
    }

    [Theory(DisplayName = "Owner decision 2026-10-29 must not fire: a person-owned work assignment linked to the root ONLY through its polymorphic pair (no typed or intermediate lookup) is not the root's — its default-team row is not examined and nothing is written")]
    [InlineData(Matter)]
    [InlineData(Project)]
    public async Task APersonOwnedWorkAssignment_LinkedToTheRootOnlyByItsPair_IsNotVisited(string rootTable)
    {
        var rootId = RootMovedToB(rootTable);
        var catalogRoot = Guid.NewGuid();
        _world.Add("sprk_recordtype_ref", catalogRoot, ("sprk_recordlogicalname", rootTable));
        var workAssignment = PersonOwnedWorkAssignment(("sprk_regardingrecordtype", "sprk_recordtype_ref", catalogRoot));
        _world.Set("sprk_workassignment", workAssignment, "sprk_regardingrecordid", rootId.ToString("D"));
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));
        var before = _world.Queries.Count;

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            rootTable, rootId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        report.Tables.Where(t => t.Table == "sprk_event").Sum(t => t.Examined).Should().Be(0,
            "a pair alone files nothing: the work assignment is not a hop, so its row is not examined");
        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerWrites.Should().BeEmpty();
        foreach (var query in _world.Queries.Skip(before).Where(q => q.EntityName == "sprk_workassignment").ToList())
        {
            query.PageInfo = null; // replayed whole: every row the query names
            _world.Answer(query).Entities.Select(e => e.Id).Should()
                .NotContain(workAssignment, "the hop lists work assignments by their typed columns only, never by the pair");
        }
    }

    [Fact(DisplayName = "Owner decision 2026-10-29: more than 200 level-1 invoices and events under the root — a person-owned work assignment filed through EACH of them (so through one in the last chunk of the hop's filing conditions) is found, and its default-team row moves")]
    public async Task TheWorkAssignmentHop_ChunksTheLevelOneInvoicesAndEvents_AndFindsOneInTheLastChunk()
    {
        MatterMovedToB();
        // 120 invoices + 130 events + the typed column = 251 filing conditions: two chunks of DescendantConditionsPerQuery (200).
        // The order the walk reads the level-1 rows in is the data source's, so a work assignment is filed through EVERY
        // intermediate: whichever lies in the last chunk, its row is the one a loop that stops after the first chunk misses.
        // The intermediates are already owned by B (the pass writes nothing for them); only the work assignments' rows move.
        var intermediates = new List<(string Table, string Column, Guid Id)>();
        for (var i = 0; i < 120; i++)
            intermediates.Add(("sprk_invoice", "sprk_regardinginvoice", Guid.NewGuid()));
        for (var i = 0; i < 130; i++)
            intermediates.Add(("sprk_event", "sprk_regardingevent", Guid.NewGuid()));
        var todos = new List<Guid>();
        foreach (var (table, column, id) in intermediates)
        {
            Row(table, id, TeamRef(TeamB), (table == "sprk_invoice" ? "sprk_matter" : "sprk_regardingmatter", Matter, MatterId));
            var workAssignment = PersonOwnedWorkAssignment((column, table, id));
            var todo = Guid.NewGuid();
            todos.Add(todo);
            Row("sprk_todo", todo, TeamRef(GeneralTeam), ("sprk_regardingworkassignment", "sprk_workassignment", workAssignment));
        }

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 2000, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        todos.Where(t => _world.OwnerOf("sprk_todo", t) != DataversePrincipalRef.Team(TeamB)).Should().BeEmpty(
            "every work assignment is found, including those filed through an intermediate past the first 200 filing conditions");
        _world.OwnerWrites.Should().HaveCount(todos.Count);
    }
}
