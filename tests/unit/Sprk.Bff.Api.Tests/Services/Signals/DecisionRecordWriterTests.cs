using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 040: the Decision Record writer. The Dataverse boundary is the SDK's own <see cref="IOrganizationServiceAsync2"/> behind
/// <see cref="OntologyWriterDataverseClient"/>'s test constructor; ownership is uac-r2's <see cref="IRecordOwnershipResolver"/>.
/// </summary>
/// <remarks>
/// ADR-038 pairing: every column, option value and lookup asserted here was read from the live spaarkedev1 schema on 2026-10-10
/// (describe of sprk_decisionrecord): sprk_recordclass Judgement 100000000 / Routine 100000001 / Dismissal 100000002;
/// sprk_decisionoutcome Authorized 100000000 / Denied 100000001 / Dismissed 100000002; sprk_matter, sprk_project,
/// sprk_workassignment, sprk_corerecordtype (-> sprk_recordtype_ref), sprk_corerecordid, sprk_steps, sprk_followons,
/// sprk_gatetier, sprk_actioncode, sprk_action (nullable), sprk_confirmedby, sprk_decidedon, sprk_privilegeflagged.
/// </remarks>
public sealed class DecisionRecordWriterTests
{
    private static readonly Guid Review = Guid.NewGuid();
    private static readonly Guid PolicyVersion = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid ItemOwner = Guid.NewGuid();
    private static readonly Guid SecureTeam = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid WorkAssignmentId = Guid.NewGuid();
    private static readonly Guid ServiceRequestId = Guid.NewGuid();
    private static readonly Guid TypeRef = Guid.NewGuid();
    private static readonly Guid SignalA = Guid.NewGuid();
    private static readonly Guid SignalB = Guid.NewGuid();
    private static readonly Guid TodoId = Guid.NewGuid();

    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public Mock<IOrganizationServiceAsync2> Org { get; } = new();
        public Mock<IRecordOwnershipResolver> Ownership { get; } = new();
        public List<Entity> Created { get; } = [];
        public Guid NewId { get; } = Guid.NewGuid();
        public Guid? ReadBackTeam { get; set; }
        public DecisionRecordWriter Writer { get; }

        public Harness(RecordOwnerResolution? ownership = null)
        {
            Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Callback<Entity, CancellationToken>((e, _) => Created.Add(e))
                .ReturnsAsync(NewId);
            Org.Setup(o => o.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new Entity("sprk_decisionrecord")
                {
                    ["owningteam"] = new EntityReference("team", ReadBackTeam ?? SecureTeam),
                });
            Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ownership ?? RecordOwnerResolution.Owned(Guid.NewGuid()));

            var client = new OntologyWriterDataverseClient(() => Org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);
            Writer = new DecisionRecordWriter(client, Ownership.Object, new FakeTimeProvider(Now), NullLogger<DecisionRecordWriter>.Instance);
        }

        public Entity Row => Created.Single();
    }

    private static RecordOwnerResolution Secure() => RecordOwnerResolution.Owned(SecureTeam) with { IsSecureOwner = true };

    private static DecisionRecordStep Taken(string code, string outcome = "Done") =>
        new(code, true, new Dictionary<string, object?> { ["note"] = "x" }, outcome);

    private static DecisionRecordStep Skipped(string code) => new(code, false, null, null);

    private static DecisionRecordCore Core(string entity, Guid id) => new(entity, id, TypeRef);

    private static DecisionRecordRequest Request(
        IReadOnlyList<DecisionRecordStep>? steps = null,
        IReadOnlyList<DecisionRecordFollowOn>? followOns = null,
        DecisionRecordCore? core = null,
        bool noCore = false,
        string? reason = null,
        bool denied = false,
        bool privilege = false,
        IReadOnlyList<Guid>? signals = null,
        IReadOnlyDictionary<string, object?>? facts = null,
        Guid? itemOwner = null) =>
        new(Review, PolicyVersion, noCore ? null : core ?? Core("sprk_matter", MatterId), noCore ? itemOwner ?? ItemOwner : itemOwner,
            User, null, steps ?? [Taken("mark-complete")], followOns ?? [], "tier-1", signals ?? [SignalA],
            facts ?? new Dictionary<string, object?> { ["variance"] = 12.5m, ["period"] = "2026-09" },
            "Partner", "approved offline", reason, denied, privilege);

    private static int Option(Entity e, string column) => e.GetAttributeValue<OptionSetValue>(column).Value;

    // ── Record class ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideLaneAuthorization_IsJudgement_AndReturnsTheIdForTheSignalLink()
    {
        var h = new Harness();
        var result = await h.Writer.WriteAsync(Request(steps: [Taken("send-budget-inquiry"), Skipped("revise-budget")]));

        result.RecordId.Should().Be(h.NewId);
        result.RecordClass.Should().Be(RecordedDecisionClass.Judgement);
        result.ResolvedSignalIds.Should().Equal(SignalA);
        Option(h.Row, "sprk_recordclass").Should().Be(100000000);
        Option(h.Row, "sprk_decisionoutcome").Should().Be(100000000);
    }

    [Fact]
    public async Task BareDoLaneCompletion_WritesARoutineRecord_NotNone()
    {
        var h = new Harness();
        var result = await h.Writer.WriteAsync(Request(steps: [Taken("mark-complete"), Skipped("reschedule")]));

        h.Created.Should().HaveCount(1);
        result.RecordClass.Should().Be(RecordedDecisionClass.Routine);
        Option(h.Row, "sprk_recordclass").Should().Be(100000001);
    }

    [Theory]
    [InlineData(true)]   // a Next step that sends email
    [InlineData(false)]  // a taken plan action that sends email
    public async Task DoLaneResolutionThatAlsoSendsEmail_IsJudgement(bool asNextStep)
    {
        var h = new Harness();
        var req = asNextStep
            ? Request(steps: [Taken("mark-complete")], followOns: [new("send-email", "sprk_communication", Guid.NewGuid())])
            : Request(steps: [Taken("send-reminder")]);

        (await h.Writer.WriteAsync(req)).RecordClass.Should().Be(RecordedDecisionClass.Judgement);
    }

    [Fact]
    public async Task Dismissal_IsDismissalClass_AndRecordsTheReason()
    {
        var h = new Harness();
        var result = await h.Writer.WriteAsync(Request(
            steps: [Skipped("mark-complete"), Skipped("reschedule")], reason: "not-mine - someone else's"));

        result.RecordClass.Should().Be(RecordedDecisionClass.Dismissal);
        Option(h.Row, "sprk_recordclass").Should().Be(100000002);
        Option(h.Row, "sprk_decisionoutcome").Should().Be(100000002);
        h.Row.GetAttributeValue<string>("sprk_reason").Should().Be("not-mine - someone else's");
        h.Row.Contains("sprk_actioncode").Should().BeFalse("nothing was taken");
    }

    [Fact]
    public async Task Dismissal_WithoutAReason_IsRefused_AndNothingIsWritten()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [Skipped("mark-complete")], reason: " "));

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Deny_SavesWithOutcomeDenied_AndANullAction()
    {
        var h = new Harness();
        await h.Writer.WriteAsync(Request(steps: [Skipped("approve-variance")], reason: "other - too high", denied: true));

        Option(h.Row, "sprk_decisionoutcome").Should().Be(100000001);
        h.Row.Contains("sprk_action").Should().BeFalse("sprk_action stays null on deny paths");
        h.Row.Contains("sprk_actioncode").Should().BeFalse();
    }

    [Fact]
    public async Task ADeniedReview_ThatAlsoTookAnAction_IsRefused()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [Taken("mark-complete")], denied: true));

        await act.Should().ThrowAsync<DecisionRecordRefusedException>();
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task CatalogActions_LeaveTheActionLookupNull()
    {
        var h = new Harness();
        await h.Writer.WriteAsync(Request(steps: [Taken("revise-budget")]));

        h.Row.Contains("sprk_action").Should().BeFalse();
        h.Row.GetAttributeValue<string>("sprk_actioncode").Should().Be("revise-budget");
    }

    // ── Mandatory snapshot ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReviewWithoutAFactSnapshot_IsRefused_BeforeAnyWrite(bool empty)
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(facts: empty ? new Dictionary<string, object?>() : null) with
        {
            FactValues = empty ? new Dictionary<string, object?>() : null!,
        });

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.FactSnapshotMissing);
        h.Created.Should().BeEmpty();
        h.Ownership.Verify(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheSnapshot_CarriesTheFacts_TheDeciderRole_AndTheBecauseText()
    {
        var h = new Harness();
        await h.Writer.WriteAsync(Request());

        using var doc = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_factsnapshot"));
        doc.RootElement.GetProperty("facts").GetProperty("period").GetString().Should().Be("2026-09");
        doc.RootElement.GetProperty("decidedBy").GetProperty("role").GetString().Should().Be("Partner");
        doc.RootElement.GetProperty("because").GetString().Should().Be("approved offline");
    }

    // ── Privilege flag (ADR-015) ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrivilegeFlag_IsCopiedForward_AndChangesNothingElse()
    {
        var flagged = new Harness();
        var plain = new Harness();
        await flagged.Writer.WriteAsync(Request(privilege: true));
        await plain.Writer.WriteAsync(Request(privilege: false));

        flagged.Row.GetAttributeValue<bool>("sprk_privilegeflagged").Should().BeTrue();
        plain.Row.GetAttributeValue<bool>("sprk_privilegeflagged").Should().BeFalse();
        flagged.Row.Attributes.Keys.Should().BeEquivalentTo(plain.Row.Attributes.Keys, "nothing branches on the flag");
        flagged.Row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value
            .Should().Be(plain.Row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value);
    }

    // ── One record per review ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ThreeOffered_TwoTaken_OneNextStep_WritesOneRecordListingAllThree()
    {
        var h = new Harness();
        var req = Request(
            steps: [Taken("send-budget-inquiry", "Sent"), Taken("revise-budget", "Revised"), Skipped("approve-variance")],
            followOns: [new("add-todo", "sprk_todo", TodoId)]);

        var result = await h.Writer.WriteAsync(req);

        h.Created.Should().HaveCount(1);
        result.RecordClass.Should().Be(RecordedDecisionClass.Judgement);
        h.Row.GetAttributeValue<string>("sprk_actioncode").Should().Be("send-budget-inquiry", "the first action taken");
        h.Row.GetAttributeValue<string>("sprk_proposedaction").Should().Be("Send budget inquiry; Revise budget; Approve variance");
        h.Row.GetAttributeValue<string>("sprk_gatetier").Should().Be("tier-1");

        using var steps = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_steps"));
        steps.RootElement.GetProperty("reviewId").GetString().Should().Be(Review.ToString("D"));
        var list = steps.RootElement.GetProperty("steps").EnumerateArray().ToList();
        list.Select(s => s.GetProperty("code").GetString()).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");
        list.Select(s => s.GetProperty("taken").GetBoolean()).Should().Equal(true, true, false);
        list.Select(s => s.GetProperty("outcome").GetString()).Should().Equal("Sent", "Revised", "Skipped");
        list[0].GetProperty("values").GetProperty("note").GetString().Should().Be("x");
        list[2].GetProperty("values").ValueKind.Should().Be(JsonValueKind.Null);

        using var followOns = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_followons"));
        followOns.RootElement.EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(TodoId);
    }

    [Fact]
    public async Task PrimaryAndAlsoResolveSignals_ShareTheOneRecord()
    {
        var h = new Harness();
        var result = await h.Writer.WriteAsync(Request(signals: [SignalA, SignalB]));

        h.Created.Should().HaveCount(1);
        result.ResolvedSignalIds.Should().Equal(SignalA, SignalB);
        using var steps = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_steps"));
        steps.RootElement.GetProperty("resolvedSignalIds").EnumerateArray().Select(e => e.GetGuid())
            .Should().Equal(SignalA, SignalB);
    }

    [Fact]
    public async Task TheWriter_NeverUpdatesOrDeletes()
    {
        var h = new Harness(Secure());
        await h.Writer.WriteAsync(Request());

        h.Org.Verify(o => o.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Org.Verify(o => o.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Refusals from the catalog ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("make-tea")]
    [InlineData("add-todo")]       // a Next-step creator is not an offered plan action
    public async Task AnActionTheCatalogCannotType_IsRefused_NeverDefaulted(string code)
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [Taken(code)]));

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ActionsThatExcludeEachOther_CannotBothBeTaken()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [Taken("revise-budget"), Taken("approve-variance")]));

        await act.Should().ThrowAsync<DecisionRecordRefusedException>();
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ATakenAction_WithoutAnOutcome_IsRefused()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [new DecisionRecordStep("mark-complete", true, null, null)]));

        await act.Should().ThrowAsync<DecisionRecordRefusedException>();
    }

    // ── Core record, lineage and ownership (D-33, D-34, D-36, D-39) ─────────────────────────────────────────────

    [Fact]
    public async Task SecureMatter_TheSecureTeamOwnsTheRecord_SetInTheCreate()
    {
        var h = new Harness(Secure());
        var result = await h.Writer.WriteAsync(Request(core: Core("sprk_matter", MatterId)));

        h.Row.GetAttributeValue<EntityReference>("ownerid").Should().Match<EntityReference>(
            o => o.LogicalName == "team" && o.Id == SecureTeam);
        result.SecureOwnerTeamId.Should().Be(SecureTeam);
        h.Row.Contains("owningbusinessunit").Should().BeFalse("it derives from the team");
        h.Row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        h.Ownership.Verify(r => r.ResolveOwnerAsync(
            It.Is<RecordOwnershipContext>(c => c.TargetEntityLogicalName == "sprk_matter" && c.TargetRecordId == MatterId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StandardMatter_KeepsTheWritersOwnership()
    {
        var h = new Harness(RecordOwnerResolution.Owned(Guid.NewGuid()));   // an ordinary team: not secure
        var result = await h.Writer.WriteAsync(Request());

        h.Row.Contains("ownerid").Should().BeFalse();
        result.SecureOwnerTeamId.Should().BeNull();
    }

    [Fact]
    public async Task ARefusedOwner_WritesNothing_AndFailsTheReview()
    {
        var h = new Harness(RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "flagged, not isolated"));
        var act = () => h.Writer.WriteAsync(Request());

        var ex = (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which;
        ex.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        ex.OwnerRefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task SecureProject_SetsProjectOnly_NotMatter_AndTheSecureTeamOwns()
    {
        var h = new Harness(Secure());
        await h.Writer.WriteAsync(Request(core: Core("sprk_project", ProjectId)));

        h.Row.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(ProjectId);
        h.Row.Contains("sprk_matter").Should().BeFalse();
        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam);
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(ProjectId.ToString("D"));
        h.Row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(TypeRef);
    }

    [Fact]
    public async Task WorkAssignmentCore_IsInTheGenericColumnsAndTheTypedLookup()
    {
        var h = new Harness();
        await h.Writer.WriteAsync(Request(core: Core("sprk_workassignment", WorkAssignmentId)));

        h.Row.GetAttributeValue<EntityReference>("sprk_workassignment").Id.Should().Be(WorkAssignmentId);
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(WorkAssignmentId.ToString("D"));
        h.Row.Contains("sprk_matter").Should().BeFalse();
    }

    [Fact]
    public async Task ServiceRequestCore_IsInTheGenericColumnsOnly()
    {
        var h = new Harness();
        await h.Writer.WriteAsync(Request(core: Core("sprk_servicerequest", ServiceRequestId)));

        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(ServiceRequestId.ToString("D"));
        h.Row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(TypeRef);
        h.Row.Contains("sprk_matter").Should().BeFalse();
        h.Row.Contains("sprk_project").Should().BeFalse();
        h.Row.Contains("sprk_workassignment").Should().BeFalse();
    }

    [Fact]
    public void TheLineageColumns_AreReadFromUac2sTable_ForTheThreeSecureRoots()
    {
        DecisionRecordWriter.LineageColumnByCoreEntity.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["sprk_matter"] = "sprk_matter",
            ["sprk_project"] = "sprk_project",
            ["sprk_workassignment"] = "sprk_workassignment",
        });
    }

    [Fact]
    public async Task ACoreOutsideTheAccessControlTaxonomy_IsRefused()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(core: Core("sprk_invoice", Guid.NewGuid())));

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.CoreUnsupported);
    }

    [Fact]
    public async Task NoCoreItem_IsOwnedByTheItemsOwner_WithBothCoreColumnsNull_AndNoResolverCall()
    {
        var h = new Harness();
        await h.Writer.WriteAsync(Request(noCore: true));

        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("systemuser");
        owner.Id.Should().Be(ItemOwner);
        h.Row.Contains("sprk_corerecordtype").Should().BeFalse();
        h.Row.Contains("sprk_corerecordid").Should().BeFalse();
        h.Row.Contains("sprk_matter").Should().BeFalse();
        h.Ownership.Verify(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoCoreItem_WithoutAnOwner_IsRefused()
    {
        var h = new Harness();
        var req = Request(noCore: true) with { ItemOwnerUserId = null };
        var act = () => h.Writer.WriteAsync(req);

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.OwnerMissing);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ASecureOwnerThatReadsBackWrong_StillReturnsTheId_SoTheRouteDoesNotRetryIntoADuplicate()
    {
        var h = new Harness(Secure()) { ReadBackTeam = Guid.NewGuid() };
        var result = await h.Writer.WriteAsync(Request());

        result.RecordId.Should().Be(h.NewId);
        h.Created.Should().HaveCount(1);
    }

    [Fact]
    public async Task ACreateFailure_Propagates_SoTheRouteWritesNoRecord()
    {
        var h = new Harness();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => h.Writer.WriteAsync(Request());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
