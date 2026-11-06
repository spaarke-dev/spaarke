using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T> / LogEntry (internal, same assembly)
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
public sealed partial class DecisionRecordWriterTests
{
    private static readonly Guid Review = Guid.NewGuid();
    private static readonly Guid PolicyVersion = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
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

    /// <summary>Adversarial pass 2 F1: the ontology writer's own application user, as WhoAmI on its connection answers. Dataverse stamps it in
    /// <c>createdby</c> on every row the writer creates.</summary>
    internal static readonly Guid WriterUser = Guid.Parse("3121bf1b-9fbf-f111-aaaf-0022482913fc");

    internal sealed class Harness
    {
        public Mock<IOrganizationServiceAsync2> Org { get; } = new();
        public Mock<IRecordOwnershipResolver> Ownership { get; } = new();
        public List<Entity> Created { get; } = [];
        public Guid NewId { get; } = Guid.NewGuid();
        public Guid? ReadBackTeam { get; set; }
        public CapturingLogger<DecisionRecordWriter> Log { get; } = new();
        public DecisionRecordWriter Writer { get; }

        /// <summary>Task 142: the rows the core-ancestor resolver reads (an unscripted subject reads as a row with no root columns).</summary>
        public Mock<IGenericEntityService> Entities { get; } = new();
        public Dictionary<(string Table, Guid Id), Entity> Rows { get; } = new();
        public Exception? RowReadFault { get; set; }

        /// <summary>Fix round 2 (K4): how many times the core-ancestor resolver's column-probe delegate was called (a read the
        /// <c>Entities</c> mock does not see).</summary>
        public int ProbeCalls;

        /// <summary>Task 142: the record-type catalog the shared regarding builder types the pair from (spaarkedev1's rows).</summary>
        public Tests.TestInfrastructure.RecordTypeCatalogStub RecordTypes { get; } = new(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1);

        /// <summary>Every ownership question the mock resolver was asked, in order.</summary>
        public List<RecordOwnershipContext> OwnerAsks { get; } = [];

        /// <summary>Task 149: Decision Records that already exist in Dataverse (found by the gate's "already recorded" lookup).</summary>
        public List<Entity> Seeded { get; } = [];

        /// <summary>Task 149: every "already recorded" lookup the writer made, as queried.</summary>
        public List<QueryExpression> Lookups { get; } = [];

        /// <summary>Task 149: when set, the lookup throws it (a Dataverse fault on the read).</summary>
        public Exception? LookupFault { get; set; }

        /// <summary>Adversarial pass 2 F1: when set, WhoAmI on the writer's connection throws it.</summary>
        public Exception? WhoAmIFault { get; set; }

        /// <summary>Adversarial pass 2 F1: how many times WhoAmI was asked on the writer's connection.</summary>
        public int WhoAmICalls;

        /// <summary>A row the writer created, as Dataverse stores it: <c>createdby</c> is the writer's own user (no impersonation).</summary>
        private static Entity AsStoredByTheWriter(Entity created, Guid id)
        {
            var stored = Clone(created, id);
            stored["createdby"] = new EntityReference("systemuser", WriterUser);
            return stored;
        }

        private static Entity Clone(Entity source, Guid id)
        {
            var copy = new Entity(source.LogicalName, id);
            foreach (var attribute in source.Attributes)
                copy[attribute.Key] = attribute.Value;
            return copy;
        }

        /// <summary>The query's conditions, evaluated on a row: an equality on a lookup or a value, a null test, and a LIKE with a leading and trailing %.</summary>
        private static bool Satisfies(QueryExpression query, Entity row) => query.Criteria.Conditions.All(condition =>
        {
            // The primary key is the row's Id, not an attribute (task 149 D-144: the writer reads the communication's ownerid by key).
            object? actual = condition.AttributeName == row.LogicalName + "id"
                ? row.Id
                : row.Contains(condition.AttributeName) ? row[condition.AttributeName] : null;
            if (condition.Operator == ConditionOperator.Null)
            {
                return actual is null;
            }

            var expected = condition.Values.Single();
            return condition.Operator switch
            {
                ConditionOperator.Equal => actual is EntityReference reference ? reference.Id.Equals(expected) : Equals(actual, expected),
                ConditionOperator.Like => actual is string text && expected is string pattern
                    && pattern.StartsWith('%') && pattern.EndsWith('%')
                    && text.Contains(pattern.Trim('%'), StringComparison.Ordinal),
                _ => throw new NotSupportedException($"Test harness: {condition.Operator} is not evaluated."),
            };
        });

        /// <param name="participantShares">Task 042b (D-169): the participant-share helper the writer calls after a gate create; null = a double over
        /// an empty world.</param>
        public Harness(RecordOwnerResolution? ownership = null, IRecordOwnershipResolver? realResolver = null, GateDecisionRecordSharing? participantShares = null)
        {
            Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Callback<Entity, CancellationToken>((e, _) => Created.Add(e))
                .ReturnsAsync(NewId);
            Org.Setup(o => o.ExecuteAsync(It.IsAny<WhoAmIRequest>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref WhoAmICalls);
                    return WhoAmIFault is { } fault
                        ? Task.FromException<OrganizationResponse>(fault)
                        : Task.FromResult<OrganizationResponse>(new WhoAmIResponse { Results = { ["UserId"] = WriterUser } });
                });
            Org.Setup(o => o.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new Entity("sprk_decisionrecord")
                {
                    ["owningteam"] = new EntityReference("team", ReadBackTeam ?? SecureTeam),
                });
            Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .Callback<RecordOwnershipContext, CancellationToken>((c, _) => OwnerAsks.Add(c))
                .ReturnsAsync(ownership ?? RecordOwnerResolution.Owned(Guid.NewGuid()));
            Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string table, Guid id, string[] _, CancellationToken _) => RowReadFault is { } fault
                    ? Task.FromException<Entity>(fault)
                    : Task.FromResult(Rows.TryGetValue((table, id), out var row) ? row : new Entity(table, id)));
            // Task 149: the gate decision's "is it already recorded" lookup, answered from what this harness has stored. The query's
            // conditions are EVALUATED (an equality on a lookup, a LIKE on a memo), so a lookup that asks the wrong question finds nothing
            // (or everything) instead of passing. A record this harness created is found by a later lookup, as Dataverse would.
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) =>
                {
                    Lookups.Add(query);
                    if (LookupFault is { } lookupFault)
                    {
                        return Task.FromException<EntityCollection>(lookupFault);
                    }

                    var stored = Created
                        .Select(c => AsStoredByTheWriter(c, c.Id == Guid.Empty ? NewId : c.Id))
                        .Concat(Seeded)
                        .Concat(Rows.Values)
                        .Where(row => row.LogicalName == query.EntityName && Satisfies(query, row))
                        .Take(query.TopCount ?? int.MaxValue)
                        .ToList();
                    return Task.FromResult(new EntityCollection(stored));
                });

            var client = new OntologyWriterDataverseClient(() => Org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);
            // The probe says every stamp column and every intermediate's root column exists (a document subject's links included).
            var coreAncestors = new CoreAncestorResolver(
                Entities.Object,
                (_, _) =>
                {
                    Interlocked.Increment(ref ProbeCalls);
                    return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                        CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute)
                            .Concat(CoreAncestorResolver.IntermediateRootColumns.Values.SelectMany(c => c.Select(x => x.Column))),
                        StringComparer.OrdinalIgnoreCase));
                },
                NullLogger<CoreAncestorResolver>.Instance);
            Writer = new DecisionRecordWriter(client, realResolver ?? Ownership.Object, coreAncestors, Entities.Object, RecordTypes, new FakeTimeProvider(Now), Log,
                participantShares ?? new GateSharingWorld().Sharing);
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
        DecisionRecordRef? subject = null,
        Guid? decider = null,
        Guid? assignee = null,
        IReadOnlyList<DecisionRecordRef>? signalSubjects = null) =>
        new(Review, PolicyVersion, noCore ? null : core ?? Core("sprk_matter", MatterId), noCore ? subject ?? new DecisionRecordRef("sprk_todo", TodoId) : subject,
            User, null, steps ?? [Taken("mark-complete")], followOns ?? [], "tier-1", signals ?? [SignalA],
            facts ?? new Dictionary<string, object?> { ["variance"] = 12.5m, ["period"] = "2026-09" },
            "Partner", "approved offline", reason, denied, privilege, DeciderContactId: decider, SignalSubjects: signalSubjects,
            AssigneeSystemUserId: assignee);

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
            steps: [Skipped("mark-complete"), Skipped("reschedule")], reason: "not-mine - someone else's") with
        { GateTier = null });

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
        var act = () => h.Writer.WriteAsync(Request(steps: [Skipped("mark-complete")], reason: " ") with { GateTier = null });

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Deny_SavesWithOutcomeDenied_AndANullAction()
    {
        var h = new Harness();
        await h.Writer.WriteAsync(Request(steps: [Skipped("approve-variance")], reason: "other - too high", denied: true) with { GateTier = null });

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
    public async Task AnUnnamedNextStep_ThatIsNotANextStepCreator_IsRefused()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [Skipped("send-budget-inquiry"), Skipped("revise-budget")], reason: "not-material - Not material")
            with { UnnamedNextSteps = ["revise-budget"] });

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnnamedNextStep_IsSomethingDone_JudgementAndAFollowOnWithoutAnId()
    {
        var h = new Harness();
        var result = await h.Writer.WriteAsync(Request(steps: [Skipped("send-budget-inquiry"), Skipped("revise-budget")])
            with { UnnamedNextSteps = ["send-email"] });

        result.RecordClass.Should().Be(RecordedDecisionClass.Judgement, "a notification went out; this is not a dismissal");
        using var followOns = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_followons"));
        var followOn = followOns.RootElement.EnumerateArray().Single();
        followOn.GetProperty("actionCode").GetString().Should().Be("send-email");
        followOn.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        h.Row.GetAttributeValue<string>("sprk_steps").Should().Contain($"\"reviewId\":\"{Review:D}\"", "the durable lookup still finds it");
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
    public async Task StandardMatter_IsOwnedByTheOrdinaryTeamTheResolverNames_NeverTheWritersOwnOwnership()
    {
        var team = Guid.NewGuid();
        var h = new Harness(RecordOwnerResolution.Owned(team));   // an ordinary team: not secure
        var result = await h.Writer.WriteAsync(Request());

        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(team, "D-129: records are owned by teams; the writer no longer keeps its own ownership for an ordinary core");
        h.Row.Contains("owningbusinessunit").Should().BeFalse("it derives from the owner");
        result.SecureOwnerTeamId.Should().BeNull("an ordinary team is not read back as a Secure-team owner");
    }

    [Fact]
    public async Task TheResolverIsAskedWithTheCoreFirst_ThenTheSubjectOfEverySignalTheRecordResolves_F1()
    {
        var other = Guid.NewGuid();
        var h = new Harness();
        RecordOwnershipContext? asked = null;
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .Callback<RecordOwnershipContext, CancellationToken>((c, _) => asked = c)
            .ReturnsAsync(RecordOwnerResolution.Owned(Guid.NewGuid()));

        await h.Writer.WriteAsync(Request(core: Core("sprk_matter", MatterId)) with
        {
            SignalSubjects = [new DecisionRecordRef("sprk_todo", TodoId), new DecisionRecordRef("sprk_document", other)],
        });

        asked.Should().NotBeNull();
        asked!.TargetEntityLogicalName.Should().Be("sprk_matter", "the core decides an ordinary record's business unit");
        asked.TargetRecordId.Should().Be(MatterId);
        asked.Parents.Should().Equal(
            new RecordOwnershipParent("sprk_matter", MatterId),
            new RecordOwnershipParent("sprk_todo", TodoId),
            new RecordOwnershipParent("sprk_document", other));
        asked.WhenUnfiled.Should().Be(UnfiledOwnership.ActingUserTeam, "KeepCreator is not requested");
    }

    [Fact]
    public async Task ASecureSubjectUnderAnOrdinaryCore_IsOwnedByTheSecureTeam_WithTheRealResolver_F1()
    {
        var project = Guid.NewGuid();
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithOrdinaryRoot("sprk_matter", MatterId)
            .WithSecureRoot("sprk_project", project)
            .WithRecord("sprk_todo", TodoId, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu,
                extra: new()
                {
                    ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                    ["sprk_regardingproject"] = new EntityReference("sprk_project", project),
                });
        var h = new Harness(realResolver: directory.Resolver());

        var withSubject = await h.Writer.WriteAsync(Request(core: Core("sprk_matter", MatterId)) with
        {
            SignalSubjects = [new DecisionRecordRef("sprk_todo", TodoId)],
        });

        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
        withSubject.SecureOwnerTeamId.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);

        // The same record WITHOUT the subjects (the old core-only question) is ordinary: this is the leak the subjects close.
        var coreOnly = new Harness(realResolver: directory.Resolver());
        await coreOnly.Writer.WriteAsync(Request(core: Core("sprk_matter", MatterId)));
        coreOnly.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.ChildTeam);
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

    /// <summary>
    /// uac-r2 task 182 (contract C4): the lineage row also holds the subject's regarding lookups, three of which target the
    /// same roots as the typed core columns. The map keeps only the typed column per root (the column named like its
    /// root); without that filter its static initializer meets a duplicate key and the writer cannot load.
    /// </summary>
    [Fact]
    public void TheLineageColumns_StayTheTypedCoreColumns_WhenTheRowAlsoHoldsRegardingLookupsToTheRoots()
    {
        var lookups = Sprk.Bff.Api.Services.Access.SecureChildLineage.Children["sprk_decisionrecord"].Lookups;
        lookups.Should().Contain(new Dictionary<string, string>
        {
            ["sprk_regardingmatter"] = "sprk_matter",
            ["sprk_regardingproject"] = "sprk_project",
            ["sprk_regardingworkassignment"] = "sprk_workassignment",
        });

        var map = () => DecisionRecordWriter.LineageColumnByCoreEntity;
        map.Should().NotThrow();
        foreach (var root in new[] { "sprk_matter", "sprk_project", "sprk_workassignment" })
            DecisionRecordWriter.LineageColumnByCoreEntity[root].Should().Be(root, $"{root} maps to its typed core column, never a regarding lookup");
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
    public async Task NoCoreItem_IsOwnedByTheTeamTheResolverNamesForTheSubject_WithBothCoreColumnsNull_D129()
    {
        var team = Guid.NewGuid();
        var h = new Harness(RecordOwnerResolution.Owned(team));   // an ordinary business-unit team, not secure
        var result = await h.Writer.WriteAsync(Request(noCore: true));

        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team", "records are owned by teams, never by a person");
        owner.Id.Should().Be(team, "the resolver's answer exactly as returned");
        result.SecureOwnerTeamId.Should().BeNull();
        h.Row.Contains("owningbusinessunit").Should().BeFalse("it derives from ownerid; the writer never sets it");
        h.Row.Contains("sprk_corerecordtype").Should().BeFalse();
        h.Row.Contains("sprk_corerecordid").Should().BeFalse();
        h.Row.Contains("sprk_matter").Should().BeFalse();
    }

    [Fact]
    public async Task NoCoreItem_AsksTheResolverWithTheSubjectAsTheOnlyParent_AndNeverOptsIntoKeepCreator()
    {
        var h = new Harness();
        RecordOwnershipContext? asked = null;
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .Callback<RecordOwnershipContext, CancellationToken>((c, _) => asked = c)
            .ReturnsAsync(RecordOwnerResolution.Owned(Guid.NewGuid()));

        await h.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId)));

        asked.Should().NotBeNull();
        asked!.TargetEntityLogicalName.Should().Be("sprk_todo");
        asked.TargetRecordId.Should().Be(TodoId);
        asked.Parents.Should().Equal(new RecordOwnershipParent("sprk_todo", TodoId));
        asked.WhenUnfiled.Should().Be(UnfiledOwnership.ActingUserTeam, "KeepCreator is not requested");
        asked.KeepCreatorUnlessTargetIsTeamOwned.Should().BeFalse();
    }

    [Fact]
    public async Task NoCoreItem_WithASecureSubject_IsOwnedByTheSecureRecordOwnersTeam_AndVerified_D123()
    {
        var h = new Harness(Secure());
        var result = await h.Writer.WriteAsync(Request(noCore: true));

        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam);
        result.SecureOwnerTeamId.Should().Be(SecureTeam);
        h.Org.Verify(o => o.RetrieveAsync("sprk_decisionrecord", h.NewId, It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()), Times.Once,
            "the Secure-team owner is read back like the core path");
    }

    [Fact]
    public async Task NoCoreItem_WithAUserOwnedSubject_TakesWhateverTheResolverAnswers_NeverAnOwnerTheWriterPicks()
    {
        // A personal To Do: the resolver is the only source of an owner. A team answer is used as-is ...
        var team = Guid.NewGuid();
        var answered = new Harness(RecordOwnerResolution.Owned(team));
        await answered.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId)));
        answered.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(team);

        // ... and a refusal writes nothing: the writer does not fall back to the user, the creator or itself.
        var refused = new Harness(RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "the To Do has no owning business unit"));
        var act = () => refused.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId)));
        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        refused.Created.Should().BeEmpty();
    }

    // ── D-134: a personal To Do's Decision Record is its Assigned To user's business-unit team's ────────────────────────────────

    private static readonly Guid AssigneeUser = Guid.Parse("d1340000-0000-4000-8000-000000000001");
    private static readonly Guid AssigneeOid = Guid.Parse("d1340000-0000-4000-8000-000000000002");

    /// <summary>A personal To Do (user-owned, filed under nothing) in the CHILD unit, and its Assigned To user in the GENERAL unit.</summary>
    private static Tests.TestInfrastructure.OwnershipDirectory PersonalToDoWorld() =>
        Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithUser(AssigneeUser, AssigneeOid, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu)
            .WithRecord("sprk_todo", TodoId, Tests.TestInfrastructure.OwnershipDirectory.ChildBu);

    [Fact]
    public async Task NoCoreItem_PersonalToDo_IsOwnedByItsAssignedToUsersBusinessUnitTeam_D134()
    {
        // D-130 left this to the REAL resolver with the To Do as the parent: the To Do's own unit's team (ChildTeam). D-134 replaces
        // that answer: the To Do's Assigned To user sits in ANOTHER unit (GeneralBu), and the record is that unit's default team's,
        // still the real resolver's answer (asked for the user's team, never a writer-picked owner).
        var h = new Harness(realResolver: PersonalToDoWorld().Resolver());

        var result = await h.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId), assignee: AssigneeUser));

        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.GeneralTeam, "the default owner team of the Assigned To user's business unit");
        h.Row.Contains("owningbusinessunit").Should().BeFalse("no owning business unit is set by the writer");
        result.SecureOwnerTeamId.Should().BeNull();
    }

    [Fact]
    public async Task NoCoreItem_PersonalToDo_WithASignalUnderASecureSubject_IsTheSecureTeams_NotTheAssignees_D134Item7()
    {
        // Item 7 (binding, F1/C9): the parents are resolved FIRST. One of the record's Signals is about a To Do filed under a SECURE
        // project, so the parents' answer is the Secure Record Owners team, and it stands: the assignee rule is never reached.
        var secureProject = Guid.NewGuid();
        var secureTodo = Guid.NewGuid();
        var directory = PersonalToDoWorld()
            .WithSecureRoot("sprk_project", secureProject)
            .WithRecord("sprk_todo", secureTodo, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu,
                extra: new() { ["sprk_regardingproject"] = new EntityReference("sprk_project", secureProject) });
        var h = new Harness(realResolver: directory.Resolver());

        var result = await h.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId), assignee: AssigneeUser,
            signalSubjects: [new DecisionRecordRef("sprk_todo", TodoId), new DecisionRecordRef("sprk_todo", secureTodo)]));

        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam,
            "a secure parent wins; the Assigned To user's team (GeneralTeam) must not be used");
        result.SecureOwnerTeamId.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
    }

    [Fact]
    public async Task CoredRecord_ToDoFiledUnderAnOrdinaryMatter_IsTheMattersTeam_TheAssigneeIgnored_D134()
    {
        // A core filing always takes precedence: a cored record never uses the assignee rule, even when handed one.
        var matter = Guid.NewGuid();
        var filedTodo = Guid.NewGuid();
        var directory = PersonalToDoWorld()
            .WithOrdinaryRoot("sprk_matter", matter)
            .WithRecord("sprk_todo", filedTodo, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) });
        var h = new Harness(realResolver: directory.Resolver());

        await h.Writer.WriteAsync(Request(core: Core("sprk_matter", matter), assignee: AssigneeUser,
            signalSubjects: [new DecisionRecordRef("sprk_todo", filedTodo)]));

        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.ChildTeam,
            "the matter's business-unit team, not the Assigned To user's (GeneralTeam)");
    }

    [Fact]
    public void ForUserTeam_RefusesAnEmptyUserId()
    {
        var act = () => RecordOwnershipContext.ForUserTeam(Guid.Empty);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("systemUserId");
    }

    [Fact]
    public async Task NoCoreItem_ToDoFiledUnderASecureMatter_TheRealResolverGivesTheSecureTeam_D123()
    {
        var matter = Guid.NewGuid();
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithSecureRoot("sprk_matter", matter)
            .WithRecord("sprk_todo", TodoId, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) });
        var h = new Harness(realResolver: directory.Resolver());

        var result = await h.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId)));

        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
        result.SecureOwnerTeamId.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
    }

    [Fact]
    public async Task NoCoreItem_WhenTheRealResolverCannotReadTheSubject_Refuses_AndWritesNothing_D130()
    {
        var h = new Harness(realResolver: Tests.TestInfrastructure.OwnershipDirectory.Standard().Resolver());   // the To Do row does not exist

        var act = () => h.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId)));

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task NoCoreItem_WhenTheResolverRefuses_WritesNothing_WithTheResolversCode()
    {
        var h = new Harness(RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "flagged, not isolated"));
        var act = () => h.Writer.WriteAsync(Request(noCore: true));

        var ex = (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which;
        ex.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        ex.OwnerRefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task NoCoreItem_WhenTheResolverAnswersNoTeam_WritesNothing_NeverLeavesTheWriterAsOwner()
    {
        var h = new Harness(RecordOwnerResolution.Unchanged("keeps its creator"));
        var act = () => h.Writer.WriteAsync(Request(noCore: true));

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task NoCoreItem_HandedAnOwnerThatIsNotATeam_WritesNothing_NeverLeavesTheWriterAsOwner()
    {
        // The commit route hands in an owner it resolved earlier; the writer still refuses one that names no team.
        var h = new Harness();
        var request = Request(noCore: true);
        var handed = new DecisionRecordOwner(
            RecordOwnerResolution.Unchanged("keeps its creator"), DecisionRecordWriter.OwnershipContextOf(request, []).Parents, null);
        var act = () => h.Writer.WriteAsync(request, handed, CancellationToken.None);

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task NoCoreItem_WhenTheResolverFaults_IsTheResolutionFailedReason_AndWritesNothing()
    {
        var h = new Harness();
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));
        var act = () => h.Writer.WriteAsync(Request(noCore: true));

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerResolutionFailed);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task NoCoreItem_WithoutASubject_IsRefused_AndPreflightSaysSoWithoutIO()
    {
        var h = new Harness();
        var req = Request(noCore: true) with { ItemSubject = null };
        var act = () => h.Writer.WriteAsync(req);

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.OwnerMissing);
        h.Created.Should().BeEmpty();
        h.Ownership.Verify(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never);
        var preflight = () => h.Writer.Preflight(req);
        preflight.Should().Throw<DecisionRecordRefusedException>().Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerMissing);
    }

    [Fact]
    public async Task TheDecidersLinkedContact_IsAssignedToInternal_AndConfirmedByStaysTheSystemUser_D129()
    {
        var contact = Guid.NewGuid();
        var h = new Harness();
        await h.Writer.WriteAsync(Request(decider: contact));

        var assigned = h.Row.GetAttributeValue<EntityReference>("sprk_assignedtointernal");
        assigned.LogicalName.Should().Be("contact");
        assigned.Id.Should().Be(contact);
        var confirmedBy = h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby");
        confirmedBy.LogicalName.Should().Be("systemuser");
        confirmedBy.Id.Should().Be(User);
    }

    [Fact]
    public async Task WithoutALinkedContact_AssignedToInternalIsLeftEmpty_AndTheRecordIsStillWritten()
    {
        var h = new Harness();
        var result = await h.Writer.WriteAsync(Request(decider: null));

        result.RecordId.Should().Be(h.NewId);
        h.Row.Contains("sprk_assignedtointernal").Should().BeFalse();
        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(User);
    }

    // ── Observability: the refusal / mismatch paths log and meter, and the tests prove it ─────────────────────────

    private static readonly AsyncLocal<Guid> MetricScope = new();

    /// <summary>Failure reasons metered on this async flow only (the Meter is process-global; same pattern as SignalWriterTests).</summary>
    private static (MeterListener Listener, List<string?> Reasons) ListenReasons(Guid scope, string instrumentName = "ontology.writer.failures")
    {
        var reasons = new List<string?>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == instrumentName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
                if (tag.Key == "reason") lock (reasons) reasons.Add(tag.Value as string);
        });
        listener.Start();
        return (listener, reasons);
    }

    [Fact]
    public async Task ASecureOwnerThatReadsBackWrong_StillReturnsTheId_LogsTheMismatch_AndMetersIt()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var h = new Harness(Secure()) { ReadBackTeam = Guid.NewGuid() };

        var result = await h.Writer.WriteAsync(Request());

        result.RecordId.Should().Be(h.NewId);
        h.Created.Should().HaveCount(1);
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.SecureOwnerMismatch && e.Level == LogLevel.Error);
        reasons.Should().Contain(DecisionRecordRefusalReason.SecureOwnerMismatch);
    }

    [Fact]
    public async Task AReadBackThatThrows_StillReturnsTheId_LogsTheException_AndMetersIt()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var h = new Harness(Secure());
        h.Org.Setup(o => o.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("read-back down"));

        var result = await h.Writer.WriteAsync(Request());

        result.RecordId.Should().Be(h.NewId);
        h.Log.Entries.Should().ContainSingle(e =>
            e.EventId == DecisionRecordEvents.SecureOwnerMismatch && e.Exception is InvalidOperationException);
        reasons.Should().Contain(DecisionRecordRefusalReason.SecureOwnerMismatch);
    }

    [Fact]
    public async Task ARefusal_LogsTheEventIdAndReason_MetersIt_AndLeaksNoFactValues()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var h = new Harness(RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "x"));

        var act = () => h.Writer.WriteAsync(Request());
        await act.Should().ThrowAsync<DecisionRecordRefusedException>();

        var entry = h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.WriteRefused).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Fields["Reason"].Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        entry.Message.Should().NotContain("2026-09").And.NotContain("12.5");
        reasons.Should().Contain(DecisionRecordRefusalReason.OwnerRefused);
    }

    // ── Create failure is the writer's one exception type ───────────────────────────────────────────────────────

    [Fact]
    public async Task ACreateFailure_IsWrapped_AsCreateFailed_WithTheInnerException_LoggedAndMetered()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var h = new Harness();
        var boom = new InvalidOperationException("boom");
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(boom);

        var act = () => h.Writer.WriteAsync(Request());

        var ex = (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which;
        ex.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
        ex.InnerException.Should().BeSameAs(boom);
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.WriteRefused);
        reasons.Should().Contain(DecisionRecordRefusalReason.CreateFailed);
    }

    [Theory]
    [InlineData("The user does not have the right: 0x80040299 Read Privilege Check For Owner failed")]
    [InlineData("access check 0x80040220")]
    [InlineData("The user does not have the Create right on sprk_decisionrecord")]   // no hex code, as SignalWriter classifies it
    public async Task APrivilegeFault_IsClassifiedAsDataverseAccessDenied(string message)
    {
        var h = new Harness();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException(message));

        var act = () => h.Writer.WriteAsync(Request());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.DataverseAccessDenied);
    }

    [Fact]
    public async Task AResolverFault_IsWrapped_AndNothingIsWritten()
    {
        var h = new Harness();
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("throttled"));

        var act = () => h.Writer.WriteAsync(Request());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.OwnerResolutionFailed);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowedOrWrapped()
    {
        var h = new Harness();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        var act = () => h.Writer.WriteAsync(Request());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ASecureAnswerWithNoTeam_IsRefused_NotWrittenAsOrdinary()
    {
        var h = new Harness(new RecordOwnerResolution(RecordOwnerOutcome.Unchanged, null, null, "x") { IsSecureOwner = true });
        var act = () => h.Writer.WriteAsync(Request());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        h.Created.Should().BeEmpty();
    }

    // ── Refused before any I/O: too large, empty, or asserting more than happened ───────────────────────────────

    [Theory]
    [InlineData("facts")]
    [InlineData("steps")]
    public async Task AnOversizeMemo_IsRefusedBeforeAnyIo(string which)
    {
        var h = new Harness();
        var big = new string('x', which == "facts" ? 100_001 : 1_048_577);
        var req = which == "facts"
            ? Request(facts: new Dictionary<string, object?> { ["k"] = big })
            : Request(steps: [new DecisionRecordStep("mark-complete", true, new Dictionary<string, object?> { ["note"] = big }, "Done")]);

        var act = () => h.Writer.WriteAsync(req);

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Created.Should().BeEmpty();
        h.Ownership.Verify(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AReviewThatOfferedNothing_IsRefused_BecauseProposedActionIsRequired()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [], followOns: [new("add-todo", "sprk_todo", TodoId)]));

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason
            .Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
    }

    [Fact]
    public async Task ADismissal_CannotCarryAGateTier()
    {
        var h = new Harness();
        var act = () => h.Writer.WriteAsync(Request(steps: [Skipped("mark-complete")], reason: "not-mine - x"));   // helper default tier-1

        await act.Should().ThrowAsync<DecisionRecordRefusedException>();
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ASkippedAction_CannotCarryAnOutcomeOrWrittenRows()
    {
        var h = new Harness();
        var withOutcome = () => h.Writer.WriteAsync(Request(steps: [Taken("revise-budget"), new("approve-variance", false, null, "Done")]));
        var withRows = () => h.Writer.WriteAsync(Request(steps: [Taken("revise-budget"),
            new("approve-variance", false, null, null, [new DecisionRecordRef("sprk_budget", Guid.NewGuid())])]));

        await withOutcome.Should().ThrowAsync<DecisionRecordRefusedException>();
        await withRows.Should().ThrowAsync<DecisionRecordRefusedException>();
        h.Created.Should().BeEmpty();
    }

    // ── Task 142: filed under its subject (contract C1/C2), every parent asked (C3), the deciding user recorded (#1783) ──────────

    private static readonly Guid CommunicationId = Guid.NewGuid();

    private static RecordOwnershipParent Parent(string table, Guid id) => new(table, id);

    private static CoreAncestorResolver StampColumnsResolver(IGenericEntityService entities) => new(
        entities,
        (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
            CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
        NullLogger<CoreAncestorResolver>.Instance);

    [Fact]
    public async Task A142_ACommunicationSubjectUnderASecureMatter_IsFiledUnderTheSubject_StampedWithTheMatter_AndSecureOwned()
    {
        // Acceptance 1: the subject lookup and the (untyped: no catalog row on spaarkedev1) pair from the shared builder, THEN the
        // core-ancestor stamp from the subject. A reversed order cannot write this row: the builder refuses a row that already carries
        // a regarding lookup (ADR-024), and the stamp column sprk_regardingmatter is one.
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithSecureRoot("sprk_matter", MatterId)
            .WithRecord("sprk_communication", CommunicationId, Tests.TestInfrastructure.OwnershipDirectory.SecureBu,
                owningTeam: Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        var h = new Harness(realResolver: directory.Resolver()) { ReadBackTeam = Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam };
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };

        var result = await h.Writer.WriteAsync(Request(core: Core("sprk_matter", MatterId), subject: new DecisionRecordRef("sprk_communication", CommunicationId)));

        h.Row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId, "filed under its subject");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId, "stamped with the subject's core");
        h.Row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(CommunicationId.ToString("D").ToLowerInvariant());
        h.Row.Contains("sprk_regardingrecordtype").Should().BeFalse("spaarkedev1 has no sprk_communication catalog row: the pair is untyped (D-133)");
        h.RecordTypes.Queried.Should().Equal("sprk_communication");
        h.Row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId, "the typed core column is decision context and stays populated (task 145)");
        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
        result.SecureOwnerTeamId.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
    }

    [Fact]
    public async Task A142_AnOrdinaryMatter_IsOwnedByItsBusinessUnitTeam_NeverAUser_AndFiledUnderTheMatterWithATypedPair()
    {
        // Acceptance 2.
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard().WithOrdinaryRoot("sprk_matter", MatterId);
        var h = new Harness(realResolver: directory.Resolver());

        var result = await h.Writer.WriteAsync(Request(core: Core("sprk_matter", MatterId)));

        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.ChildTeam);
        h.Row.Contains("owninguser").Should().BeFalse();
        result.SecureOwnerTeamId.Should().BeNull();
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Id
            .Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1["sprk_matter"]);
        h.Row.Contains("sprk_regardingproject").Should().BeFalse();
        h.Row.Contains("sprk_regardingcommunication").Should().BeFalse();
        h.Row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)
            .Should().BeEquivalentTo(new EntityReference("systemuser", User), "#1783 / D-135: sprk_createdbyperson is the deciding user");
    }

    [Fact]
    public async Task A142_TheOwnerIsAskedAboutEveryParent_TheCoresTheFilingDerivedToo_AndTheDecidingUserIsTheRequester()
    {
        var project = Guid.NewGuid();
        var signalSubject = Guid.NewGuid();
        var h = new Harness();
        h.Rows[("sprk_todo", TodoId)] = new Entity("sprk_todo", TodoId) { ["sprk_regardingproject"] = new EntityReference("sprk_project", project) };

        await h.Writer.WriteAsync(Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId),
            signalSubjects: [new DecisionRecordRef("sprk_document", signalSubject)]));

        var asked = h.OwnerAsks.Should().ContainSingle().Subject;
        asked.Parents.Should().Equal(Parent("sprk_todo", TodoId), Parent("sprk_project", project), Parent("sprk_document", signalSubject));
        asked.RequestedBy!.SystemUserId.Should().Be(User, "#1783 / D-135: the deciding user is the person who asked");
        asked.KeepCreatorUnlessTargetIsTeamOwned.Should().BeFalse("task 042: only a gate decision asks as the content of a communication; the review route is unchanged");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingtodo").Id.Should().Be(TodoId);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(project, "the stamp the derived core gave");
    }

    [Fact]
    public async Task A142_ACoreOnlyTheFilingDerives_MakesTheRecordSecure_UnderAnOrdinaryCore_F1()
    {
        // The communication's secure project is known here only through the filing's derivation (the directory has the subject as an
        // ordinary row with no lineage): the derived core must be a parent of the owner question, or the record is filed ordinary.
        var project = Guid.NewGuid();
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithOrdinaryRoot("sprk_matter", MatterId)
            .WithSecureRoot("sprk_project", project)
            .WithRecord("sprk_communication", CommunicationId, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu);
        var h = new Harness(realResolver: directory.Resolver()) { ReadBackTeam = Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam };
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingproject"] = new EntityReference("sprk_project", project),
        };

        await h.Writer.WriteAsync(Request(core: Core("sprk_matter", MatterId), subject: new DecisionRecordRef("sprk_communication", CommunicationId)));

        h.Row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(project);
        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
    }

    [Fact]
    public async Task A142_AnOwnerResolvedBeforeTheFirstWrite_IsUsed_WhenItAnsweredForTheSameParents()
    {
        var h = new Harness(Secure());
        var request = Request(core: Core("sprk_matter", MatterId));

        var owner = await h.Writer.ResolveOwnerAsync(request);
        await h.Writer.WriteAsync(request, owner, CancellationToken.None);

        h.OwnerAsks.Should().ContainSingle("the handed-in answer is the write's answer");
        owner.Resolution.OwningTeamId.Should().Be(SecureTeam);
        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam);
    }

    [Fact]
    public async Task A142_AHandedOwner_CarriesItsFiling_AndTheWriteAppliesItWithNoRead()
    {
        var h = new Harness(Secure());
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };
        var request = Request(core: Core("sprk_matter", MatterId), subject: new DecisionRecordRef("sprk_communication", CommunicationId));
        var owner = await h.Writer.ResolveOwnerAsync(request);
        Volatile.Read(ref h.ProbeCalls).Should().BeGreaterThan(0, "control: step 1f does probe, so a zero below is the handed path's, not a dead counter");
        h.Entities.Invocations.Clear();
        Volatile.Write(ref h.ProbeCalls, 0);
        h.RowReadFault = new TimeoutException("a read after the sends");

        await h.Writer.WriteAsync(request, owner, CancellationToken.None);

        h.Entities.Invocations.Should().BeEmpty("the step-1f filing is applied, not re-read");
        Volatile.Read(ref h.ProbeCalls).Should().Be(0, "the column probe is a read too: the handed filing is applied with none");
        h.RecordTypes.Queried.Should().ContainSingle();
        h.OwnerAsks.Should().ContainSingle();
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
    }

    [Fact]
    public async Task A142_AHandedFilingWhoseOwnerAnswersForOtherParents_IsRefused_NeverReadAgain()
    {
        // Fix round 2 (K1): the route's contract is that a handed filing answers the request; the writer must not quietly re-ask the
        // owner (a Dataverse read after the sends began). It fails closed, at Error, with no read and no write.
        var h = new Harness(Secure());
        var request = Request(core: Core("sprk_matter", MatterId));
        var stale = await h.Writer.ResolveOwnerAsync(request);
        h.OwnerAsks.Should().ContainSingle();
        h.Entities.Invocations.Clear();
        Volatile.Write(ref h.ProbeCalls, 0);
        var queried = h.RecordTypes.Queried.Count;

        var act = () => h.Writer.WriteAsync(request with { SignalSubjects = [new DecisionRecordRef("sprk_todo", TodoId)] }, stale, CancellationToken.None);

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().ContainSingle("the owner is not asked again");
        h.Entities.Invocations.Should().BeEmpty();
        Volatile.Read(ref h.ProbeCalls).Should().Be(0);
        h.RecordTypes.Queried.Count.Should().Be(queried, "no catalog re-read either");
        h.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("does not answer the request"));
    }

    [Fact]
    public async Task A142_AHandedFilingForAnotherSubject_IsRefused_NeverFiledAgain()
    {
        var h = new Harness(Secure());
        var first = Request(core: Core("sprk_matter", MatterId), subject: new DecisionRecordRef("sprk_communication", CommunicationId));
        var handed = await h.Writer.ResolveOwnerAsync(first);
        h.Entities.Invocations.Clear();
        Volatile.Write(ref h.ProbeCalls, 0);

        var other = Request(core: Core("sprk_matter", MatterId), subject: new DecisionRecordRef("sprk_communication", Guid.NewGuid()));
        var act = () => h.Writer.WriteAsync(other, handed, CancellationToken.None);

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
        h.Entities.Invocations.Should().BeEmpty();
        Volatile.Read(ref h.ProbeCalls).Should().Be(0);
        h.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("does not answer the request"));
    }

    [Fact]
    public async Task A142_AnOwnerHandedWithNoFiling_ForOtherParents_IsStillAskedAgain_ForNonRouteCallers()
    {
        // The no-handed-filing path (a caller that is not the commit route) keeps its behaviour.
        var h = new Harness(Secure());
        var request = Request(core: Core("sprk_matter", MatterId));
        var parentsOfAnotherQuestion = DecisionRecordWriter.OwnershipContextOf(request, []).Parents;
        var stale = new DecisionRecordOwner(Secure(), parentsOfAnotherQuestion, null);

        await h.Writer.WriteAsync(request with { SignalSubjects = [new DecisionRecordRef("sprk_todo", TodoId)] }, stale, CancellationToken.None);

        h.OwnerAsks.Should().HaveCount(1);
        h.OwnerAsks[0].Parents.Should().Contain(Parent("sprk_todo", TodoId));
        h.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("asking again"));
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task A142_ASubjectNoDecisionRecordLookupCarries_IsRefused_InPreflightAndWrite_AndNothingIsWritten()
    {
        var h = new Harness();
        var request = Request(noCore: true, subject: new DecisionRecordRef("sprk_contact", Guid.NewGuid()));

        h.Writer.Invoking(w => w.Preflight(request)).Should().Throw<DecisionRecordRefusedException>()
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnsupported);
        (await h.Writer.Invoking(w => w.WriteAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnsupported);
        (await h.Writer.Invoking(w => w.ResolveOwnerAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnsupported);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("access-denied")]
    [InlineData("throttle")]
    public async Task A142_WhenTheSubjectsCoresCannotBeRead_ItFailsClosed_AndNothingIsWritten(string fault)
    {
        // Fix round 2 (F3, uac-r2): only "does not exist" (0x80040217) is a permanent data condition. An access-denied (0x80040220)
        // or throttle (0x80072322) Dataverse fault is transient for this subject: FilingFailed (503), never the permanent
        // SubjectUnfileable (422), which would tell the user a retry cannot help.
        Exception read = fault switch
        {
            "access-denied" => new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220) }, "SecLib::AccessCheckEx failed"),
            "throttle" => new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80072322) }, "Number of requests exceeded the limit"),
            _ => new TimeoutException("no answer"),
        };
        var h = new Harness { RowReadFault = read };
        var request = Request(noCore: true, subject: new DecisionRecordRef("sprk_todo", TodoId));

        (await h.Writer.Invoking(w => w.WriteAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        (await h.Writer.Invoking(w => w.ResolveOwnerAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty("an owner is never asked about a parent list the filing could not complete");
    }

    [Theory]
    [InlineData("two-roots")]
    [InlineData("missing")]
    public async Task A142_ASubjectThatCannotBeFiledForGood_IsARefusal_NotAnOutage_AndNothingIsWritten(string condition)
    {
        // Adversarial pass 1 F1: a document whose matter and related matter name different records (or a subject that no longer
        // exists) is a data condition a retry never changes: decision_subject_unfileable, never decision_filing_failed (503).
        var document = Guid.NewGuid();
        var h = new Harness();
        if (condition == "two-roots")
        {
            h.Rows[("sprk_document", document)] = new Entity("sprk_document", document)
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_relatedmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
            };
        }
        else
        {
            h.RowReadFault = new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "sprk_document With Id = ... Does Not Exist");
        }

        var request = Request(core: Core("sprk_matter", MatterId), subject: new DecisionRecordRef("sprk_document", document));

        (await h.Writer.Invoking(w => w.ResolveOwnerAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnfileable);
        var ex = (await h.Writer.Invoking(w => w.WriteAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;
        ex.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnfileable);
        ex.Message.Should().NotContain(document.ToString("D")).And.NotContain(MatterId.ToString("D"), "the refusal names no record");
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    // ── ebilling-r1 task 026 (FR-05, FR-15, D-75, D-94 condition 3): an invoice or invoice-line subject ───────────────────

    [Theory]
    [InlineData("sprk_billingevent", "no-core")]
    [InlineData("sprk_billingevent", "two-roots")]
    [InlineData("sprk_invoice", "no-core")]
    [InlineData("sprk_invoice", "two-roots")]
    public async Task E026_AnInvoiceOrLineNotFiledUnderExactlyOneCore_IsUnfileable_AndNothingIsWritten(string table, string condition)
    {
        // Without the FR-05 check a line with no core lookup would be written with no stamp, and an invoice under a matter AND a
        // project would be stamped with both. A line's sprk_invoice is set in both cases and must never supply the core (D-75).
        var subjectId = Guid.NewGuid();
        var h = new Harness();
        var row = new Entity(table, subjectId);
        if (table == "sprk_billingevent") row["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
        if (condition == "two-roots")
        {
            row["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
            row["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid());
        }

        h.Rows[(table, subjectId)] = row;
        var request = Request(core: Core("sprk_matter", MatterId), subject: new DecisionRecordRef(table, subjectId));

        (await h.Writer.Invoking(w => w.ResolveOwnerAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnfileable);
        var ex = (await h.Writer.Invoking(w => w.WriteAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;
        ex.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnfileable);
        ex.Message.Should().NotContain(subjectId.ToString("D"), "the refusal names no record");
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("other-matter")]
    [InlineData("other-type")]
    [InlineData("no-core")]
    public async Task E026_ADecisionContextThatIsNotTheLinesOwnCore_IsUnfileable_AndNothingIsWritten(string condition)
    {
        // Fix-round-1 (ontology K3): the line is filed under a project; the request names its invoice's matter, another project's
        // record type, or no core at all. sprk_corerecordid would name a record the line does not belong to.
        var line = Guid.NewGuid();
        var project = Guid.NewGuid();
        var h = new Harness();
        h.Rows[("sprk_billingevent", line)] = new Entity("sprk_billingevent", line)
        {
            ["sprk_project"] = new EntityReference("sprk_project", project),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()),
        };
        var subject = new DecisionRecordRef("sprk_billingevent", line);
        var request = condition switch
        {
            "other-matter" => Request(core: Core("sprk_matter", MatterId), subject: subject),
            "other-type" => Request(core: Core("sprk_matter", project), subject: subject),
            _ => Request(noCore: true, subject: subject),
        };

        (await h.Writer.Invoking(w => w.ResolveOwnerAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnfileable);
        (await h.Writer.Invoking(w => w.WriteAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnfileable);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Fact]
    public void E026_TheCoreCheck_LeavesEverySubjectOutsideTheInvoiceFamilyAlone()
    {
        var request = Request(core: Core("sprk_matter", MatterId));
        var act = () => DecisionRecordWriter.RequireCoreIsTheInvoiceFamilySubjectsOwn(
            request, new DecisionRecordRef("sprk_communication", CommunicationId),
            [new CoreAncestorStamp("sprk_project", "sprk_regardingproject", Guid.NewGuid())]);

        act.Should().NotThrow("a communication's decision context may differ from its filing (task 142, unchanged)");
    }

    [Fact]
    public async Task E026_ALineOfAProject_IsFiledUnderTheLine_AndStampedWithItsOwnProject_NotItsInvoicesMatter()
    {
        var line = Guid.NewGuid();
        var project = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        var h = new Harness();
        h.Rows[("sprk_invoice", invoice)] = new Entity("sprk_invoice", invoice) { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) };
        h.Rows[("sprk_billingevent", line)] = new Entity("sprk_billingevent", line)
        {
            ["sprk_project"] = new EntityReference("sprk_project", project),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice),
        };

        await h.Writer.WriteAsync(Request(core: Core("sprk_project", project), subject: new DecisionRecordRef("sprk_billingevent", line)));

        h.Row.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Should().BeEquivalentTo(new EntityReference("sprk_billingevent", line));
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(project, "stamped with the line's own core");
        h.Row.Contains("sprk_regardingmatter").Should().BeFalse("the invoice's matter is never borrowed (D-75)");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Id
            .Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1["sprk_billingevent"]);
        h.Entities.Verify(e => e.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A142_WhenTheRecordTypeCatalogFaults_ItFailsClosed_AndNothingIsWritten()
    {
        var h = new Harness();
        var writer = new DecisionRecordWriter(
            new OntologyWriterDataverseClient(() => h.Org.Object, NullLogger<OntologyWriterDataverseClient>.Instance),
            h.Ownership.Object, StampColumnsResolver(h.Entities.Object), h.Entities.Object,
            new Tests.TestInfrastructure.RecordTypeCatalogStub((_, _) => throw new TimeoutException("catalog down")),
            new FakeTimeProvider(Now), h.Log, new GateSharingWorld().Sharing);

        (await writer.Invoking(w => w.WriteAsync(Request())).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
    }
}
