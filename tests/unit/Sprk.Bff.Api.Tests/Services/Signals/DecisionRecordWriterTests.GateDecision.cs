using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology task 042 (FR-23; main-session decision "A+C"): <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/>, the additive entry
/// point that records an AUTOMATED communications-gate decision (authorize and deny) through the same content, filing, owner and create
/// code as a review.
/// </summary>
/// <remarks>
/// ADR-038 pairing: every column, option value and lookup asserted here was read from the LIVE spaarkedev1 schema (read-only describe of
/// <c>sprk_decisionrecord</c>, 2026-10-14): <c>sprk_confirmedby</c>, <c>sprk_policyversion</c>, <c>sprk_gatesessionid</c> and
/// <c>sprk_action</c> are NULLABLE (a row without them is creatable); <c>sprk_recordclass</c> Judgement 100000000;
/// <c>sprk_decisionoutcome</c> Authorized 100000000 / Denied 100000001; <c>sprk_regardingcommunication</c>, <c>sprk_regardingmatter</c>,
/// <c>sprk_regardingproject</c>, <c>sprk_matter</c>, <c>sprk_project</c>, <c>sprk_corerecordtype</c>, <c>sprk_corerecordid</c> all exist.
/// The opt-in live seam test in <c>DecisionRecordWriterSeamTests</c> writes both paths against that schema.
/// </remarks>
public sealed partial class DecisionRecordWriterTests
{
    private static readonly Guid GateEvaluation = Guid.NewGuid();
    private static readonly Guid GateRule = Guid.NewGuid();
    private static readonly Guid GateProject = Guid.NewGuid();
    private static readonly Guid GateWorkAssignment = Guid.NewGuid();

    private static GateDecisionRecordRequest Gate(
        bool authorize = true, Guid? direct = null, IReadOnlyDictionary<string, object?>? facts = null, string reason = "rule-matched-confidence-met") =>
        new(CommunicationId, GateEvaluation, authorize, reason,
            ProposedActionCode: "communication-ri-action", ProposedActionLabel: "Create a review task and notify the communication's owner",
            facts ?? new Dictionary<string, object?>
            {
                ["correlationId"] = GateEvaluation,
                ["communicationId"] = CommunicationId,
                ["matchedRuleId"] = GateRule,
                ["matchedRulePriority"] = 100,
                ["confidence"] = 0.9,
                ["threshold"] = 0.7,
            },
            PrivilegeFlagged: false, DirectFiledUnderId: direct);

    /// <summary>A communication filed under <paramref name="columns"/> (regarding lookups), in a directory whose roots are ordinary.</summary>
    private static Harness FiledCommunication(params (string Column, string Entity, Guid Id)[] columns)
    {
        var directory = Standard();
        foreach (var (_, entity, id) in columns)
            directory.WithOrdinaryRoot(entity, id);
        directory.WithRecord("sprk_communication", CommunicationId, ChildBu);
        var h = new Harness(realResolver: directory.Resolver());
        var row = new Entity("sprk_communication", CommunicationId);
        foreach (var (column, entity, id) in columns)
            row[column] = new EntityReference(entity, id);
        h.Rows[("sprk_communication", CommunicationId)] = row;
        return h;
    }

    [Fact]
    public async Task Gate_Authorize_WritesAJudgementRecordFiledUnderTheCommunication_WithNoHumanNoPolicyVersionNoSignal()
    {
        var h = FiledCommunication(("sprk_regardingmatter", "sprk_matter", MatterId));

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        var row = h.Row;
        result.RecordClass.Should().Be(RecordedDecisionClass.Judgement);
        result.ResolvedSignalIds.Should().BeEmpty();
        Option(row, "sprk_recordclass").Should().Be(100000000, "Judgement is set explicitly (D-17 amendment), not derived");
        Option(row, "sprk_decisionoutcome").Should().Be(100000000, "Authorized");
        row.GetAttributeValue<string>("sprk_actioncode").Should().Be("communication-ri-action", "free text, not a catalog code");
        row.Contains("sprk_action").Should().BeFalse("sprk_action is never set");
        row.GetAttributeValue<string>("sprk_proposedaction").Should().Be("Create a review task and notify the communication's owner");
        row.GetAttributeValue<string>("sprk_name").Should().StartWith("Judgement: ");
        row.GetAttributeValue<string>("sprk_reason").Should().Be("rule-matched-confidence-met");

        // No human, no policy version, no gate tier, no session gate, no Signal (the owner's closed set).
        row.Contains("sprk_confirmedby").Should().BeFalse("no human decided in enrichment; never the BFF identity or an owner (D-129)");
        row.Contains("sprk_policyversion").Should().BeFalse("the gate's rules are sprk_communicationrule rows");
        row.Contains("sprk_gatetier").Should().BeFalse();
        row.Contains("sprk_gatesessionid").Should().BeFalse("no SessionGate exists in enrichment (criterion 4 as amended)");
        row.Contains("sprk_assignedtointernal").Should().BeFalse();
        row.Contains(RecordCreatorPerson.Column).Should().BeFalse("nobody asked: no creator person");
        row.GetAttributeValue<bool>("sprk_privilegeflagged").Should().BeFalse();
        row.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(Now.UtcDateTime);

        // sprk_steps: the one offered step, taken, outcome Authorized; no Signal ids; no follow-ons.
        using var steps = JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps"));
        steps.RootElement.GetProperty("reviewId").GetString().Should().Be(GateDecisionKey.IdOf(CommunicationId, authorize: true).ToString("D"),
            "task 149: the record's key is the decision's (communication + outcome), never the per-run evaluation id");
        steps.RootElement.GetProperty("resolvedSignalIds").GetArrayLength().Should().Be(0);
        var step = steps.RootElement.GetProperty("steps").EnumerateArray().Single();
        step.GetProperty("code").GetString().Should().Be("communication-ri-action");
        step.GetProperty("taken").GetBoolean().Should().BeTrue();
        step.GetProperty("outcome").GetString().Should().Be("Authorized");
        row.GetAttributeValue<string>("sprk_followons").Should().Be("[]");

        // The correlation id and the matched rule id and priority are in the fact snapshot.
        var snapshot = row.GetAttributeValue<string>("sprk_factsnapshot");
        snapshot.Should().Contain(GateEvaluation.ToString("D")).And.Contain(GateRule.ToString("D")).And.Contain("\"matchedRulePriority\":100");
        snapshot.Should().Contain("automated: communications policy gate").And.NotContain("\"role\":null");

        // Filed under the communication, stamped with its matter, and the core context copied (D-34).
        row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId, "the stamp CoreAncestorResolver derived");
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1["sprk_matter"]);
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(MatterId.ToString("D").ToLowerInvariant());

        // Owned by the resolver's team over EVERY parent, never a person (D-129/D-130).
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", ChildTeam));
        h.Row.Contains("owninguser").Should().BeFalse();
    }

    [Fact]
    public async Task Gate_Deny_WritesAJudgementRecordWithOutcomeDenied_NoActionCode_AndTheStepNotTaken()
    {
        var h = FiledCommunication(("sprk_regardingmatter", "sprk_matter", MatterId));

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: false, reason: "rule-matched-confidence-below-threshold"));

        var row = h.Row;
        result.RecordClass.Should().Be(RecordedDecisionClass.Judgement, "a deny is Judgement too, not the Dismissal a review that took nothing derives");
        Option(row, "sprk_recordclass").Should().Be(100000000);
        Option(row, "sprk_decisionoutcome").Should().Be(100000001, "Denied");
        row.Contains("sprk_actioncode").Should().BeFalse("FR-01: a deny has no action");
        row.Contains("sprk_action").Should().BeFalse();
        row.GetAttributeValue<string>("sprk_proposedaction").Should().Be("Create a review task and notify the communication's owner",
            "what the gate proposed is still recorded");
        row.GetAttributeValue<string>("sprk_name").Should().Be("Judgement: denied (rule-matched-confidence-below-threshold)");
        row.GetAttributeValue<string>("sprk_reason").Should().Be("rule-matched-confidence-below-threshold");
        row.Contains("sprk_confirmedby").Should().BeFalse();
        row.Contains("sprk_policyversion").Should().BeFalse();

        using var steps = JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps"));
        var step = steps.RootElement.GetProperty("steps").EnumerateArray().Single();
        step.GetProperty("code").GetString().Should().Be("communication-ri-action");
        step.GetProperty("taken").GetBoolean().Should().BeFalse();
        step.GetProperty("outcome").GetString().Should().Be("Denied", "criterion 7: the proposed action as not taken with outcome Denied");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId, "queryable per matter on the deny path too");
    }

    [Fact]
    public async Task Gate_ACommunicationFiledUnderAProject_RecordsTheProjectAsCore_Criterion9()
    {
        var h = FiledCommunication(("sprk_regardingproject", "sprk_project", GateProject));

        await h.Writer.WriteGateDecisionAsync(Gate());

        h.Row.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(GateProject, "D-34: the record's sprk_project is the project");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(GateProject);
        h.Row.Contains("sprk_matter").Should().BeFalse();
        h.Row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1["sprk_project"]);
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(GateProject.ToString("D").ToLowerInvariant());
    }

    [Fact]
    public async Task Gate_AMatterAndAProject_MatterWinsOverProject_ButBothStayStamped_D37()
    {
        var h = FiledCommunication(("sprk_regardingmatter", "sprk_matter", MatterId), ("sprk_regardingproject", "sprk_project", GateProject));

        await h.Writer.WriteGateDecisionAsync(Gate());

        h.Row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        h.Row.Contains("sprk_project").Should().BeFalse("one core pair: matter over project");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(GateProject, "queryable per project through the stamp");
    }

    [Fact]
    public async Task Gate_TheRecordItIsDirectlyFiledUnder_DecidesBeforeMatterOverProject_D37()
    {
        var h = FiledCommunication(("sprk_regardingmatter", "sprk_matter", MatterId), ("sprk_regardingproject", "sprk_project", GateProject));

        await h.Writer.WriteGateDecisionAsync(Gate(direct: GateProject));

        h.Row.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(GateProject);
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(GateProject.ToString("D").ToLowerInvariant());
    }

    [Fact]
    public async Task Gate_ATieNeitherRuleDecides_StillRecordsTheDecision_WithoutACorePair_AndWarns()
    {
        var h = FiledCommunication(("sprk_regardingproject", "sprk_project", GateProject), ("sprk_regardingworkassignment", "sprk_workassignment", GateWorkAssignment));

        await h.Writer.WriteGateDecisionAsync(Gate());

        h.Row.Contains("sprk_corerecordtype").Should().BeFalse();
        h.Row.Contains("sprk_corerecordid").Should().BeFalse();
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(GateProject, "every stamp stays: the record is queryable by each");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingworkassignment").Id.Should().Be(GateWorkAssignment);
        h.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("D-37"));
    }

    private static readonly Guid CommunicationOwner = Guid.Parse("c0a1e000-0000-4000-8000-0000000000a1");
    private static readonly Guid BasicColleague = Guid.Parse("c0a1e000-0000-4000-8000-0000000000b2");

    /// <summary>Seeds the communication row the writer reads its owner from (the shared sysadmin reader): owned by <paramref name="ownerId"/>
    /// (a user), or by nobody / a team when given. A user owner comes back with its linked <c>isdisabled</c> = false, as Dataverse returns it
    /// for an active user (issue #1576: a user owner with no value is treated as disabled).</summary>
    private static void CommunicationOwnedBy(Harness h, EntityReference? owner)
    {
        var row = new Entity("sprk_communication", CommunicationId);
        if (owner is not null)
            row["ownerid"] = owner;
        if (owner is { LogicalName: "systemuser" })
            row["owner.isdisabled"] = new AliasedValue("systemuser", "isdisabled", false);
        h.Rows[("sprk_communication", CommunicationId)] = row;
    }

    private static EntityReference UserRef(Guid id) => new("systemuser", id);

    [Fact]
    public async Task Gate_AnUnfiledCommunication_IsOwnedByTheCommunicationsOwner_NotItsBusinessUnitsDefaultTeam_D144Amended()
    {
        // Task 149, D-144 AS AMENDED (owner ruling on uac-r2 F1). A default-team owner would show the communication's subject (through the
        // regarding lookup's display name), its privilege flag and the snapshot to every colleague in the unit. So an unfiled communication's
        // gate decision is owned by the communication's OWN owning user. REAL RecordOwnershipResolver over an in-memory directory: the first
        // ask (as content of the communication) answers Unchanged; the second (as a plain parent) is asked only for secure-if-any and its
        // ordinary default-team answer is discarded.
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var h = new Harness(realResolver: directory.Resolver());
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        result.AlreadyRecorded.Should().BeFalse();
        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.Should().BeEquivalentTo(UserRef(CommunicationOwner), "the communication's own owning principal");
        owner.LogicalName.Should().NotBe("team");
        owner.Id.Should().NotBe(ChildTeam, "NOT the owning business unit's default team (every colleague in the unit would read it)");
        h.Row.Contains("owninguser").Should().BeFalse();
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        result.SecureOwnerTeamId.Should().BeNull();
        h.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning && e.EventId.Id != 0, "a record that is written is not an alert");
    }

    private static readonly Guid CommunicationSharee = Guid.Parse("c0a1e000-0000-4000-8000-0000000000c3");

    /// <summary>Task 042b: the participant-share helper over its own in-memory Dataverse, with the unfiled communication owned by
    /// <see cref="CommunicationOwner"/> and shared Read with <see cref="CommunicationSharee"/> (not with <see cref="BasicColleague"/>).</summary>
    private static GateSharingWorld SharedCommunication() => new GateSharingWorld()
        .WithCommunication(CommunicationId, CommunicationOwner)
        .WithUser(CommunicationOwner).WithUser(CommunicationSharee).WithUser(BasicColleague)
        .Share(GateSharingWorld.Communication, CommunicationId, CommunicationSharee);

    [Fact]
    public async Task Gate_AnUnfiledCommunication_IsSharedReadWithTheCommunicationsSharees_D169_ButABasicColleagueWhoIsNoShareeGetsNoShareOrGrant()
    {
        // Task 042b INVERTS task 149's "gets no share or grant" (D-144 as amended): owner decision D-169 makes the record visible to whoever
        // the communication is associated with, i.e. its sharees. The exposure D-144 closed stays closed: a Basic colleague in the
        // communication's business unit (ChildBu, where the old default team sat) who is NOT a sharee is neither the owner nor shared. The
        // shares are the BFF app identity's (the POA seam), never the writer connection's; the writer still makes one create.
        var directory = Standard()
            .WithUser(BasicColleague, Guid.NewGuid(), ChildBu)
            .WithRecord("sprk_communication", CommunicationId, ChildBu);
        var world = SharedCommunication();
        var h = new Harness(realResolver: directory.Resolver(), participantShares: world.Sharing);
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        world.WithGateRecord(h.NewId, CommunicationId, CommunicationOwner); // the row as Dataverse holds it after the create (its owner is read back)

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.Id.Should().Be(CommunicationOwner).And.NotBe(BasicColleague);
        owner.Id.Should().NotBe(ChildTeam, "the colleague's team (the unit's default team) is not the owner either");
        world.RecordSharees(result.RecordId).Should().Equal(new[] { CommunicationSharee },
            "D-169: Read for the communication's sharee; none for the colleague or the owner");
        world.SharesOf(GateSharingWorld.Record, result.RecordId).Values.Should().OnlyContain(mask => mask == 1, "Read only");
        h.Created.Should().ContainSingle("one record is written; nothing is created for a share or an access row");
        h.Org.Invocations.Where(i => !(i.Method.Name == "ExecuteAsync" && i.Arguments[0] is Microsoft.Crm.Sdk.Messages.WhoAmIRequest))
            .Select(i => i.Method.Name).Distinct().Should().OnlyContain(
            name => name == "CreateAsync" || name == "RetrieveAsync",
            "no GrantAccess / ModifyAccess / Assign / Associate / Execute on the writer's connection (WhoAmI, adversarial pass 2 F1, only reads the writer's own identity)");

        // The shared sysadmin service (h.Entities) is the other way a share or grant could be made: it is only ever READ through.
        h.Entities.Invocations.Select(i => i.Method.Name).Distinct().Should().OnlyContain(
            name => name == "RetrieveAsync" || name == "RetrieveMultipleAsync",
            "the shared entity service makes no create, update, share or grant");
        h.Lookups.Should().OnlyContain(q => q.EntityName == "sprk_decisionrecord" || q.EntityName == "sprk_communication",
            "no query touches a share or access table");
        h.Created.Concat(h.Seeded).Should().NotContain(e => e.LogicalName == "principalobjectaccess" || e.LogicalName.Contains("share") || e.LogicalName.Contains("access"),
            "no share or grant row exists");
    }

    [Fact]
    public async Task Gate_ATeamOwnedRecord_TheSecureRecordUnitsCommunication_GetsNoParticipantShare_D169_D123()
    {
        // The Secure Record business unit's communication goes to the Secure team (task 149 K4); a team-owned record is never shared, even
        // when the communication has sharees.
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, SecureBu);
        var world = SharedCommunication();
        var h = new Harness(realResolver: directory.Resolver(), participantShares: world.Sharing) { ReadBackTeam = SecureNamedTeam };

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        result.SecureOwnerTeamId.Should().Be(SecureNamedTeam);
        world.RecordSharees(result.RecordId).Should().BeEmpty();
        world.Calls.Should().BeEmpty("the participant-share step is never reached for a team-owned record");
    }

    [Fact]
    public async Task Gate_AFailedParticipantShareRead_StillWritesTheRecord_OwnerVisible_AndNothingEscapes_D169()
    {
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var world = SharedCommunication();
        world.Shares.FailReadFor.Add(GateSharingWorld.Communication);
        var h = new Harness(realResolver: directory.Resolver(), participantShares: world.Sharing);
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        world.WithGateRecord(h.NewId, CommunicationId, CommunicationOwner);

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        result.RecordId.Should().Be(h.NewId, "the record is written");
        h.Row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(UserRef(CommunicationOwner), "and owner-visible");
        world.RecordSharees(result.RecordId).Should().BeEmpty("a failed read never widens");
        world.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == DecisionRecordEvents.ParticipantShareFailed);
    }

    [Fact]
    public async Task Gate_AnUnfiledCommunicationInABusinessUnitWithNoDefaultTeam_IsStillOwnedByTheCommunicationsOwner_D144Amended()
    {
        // The default team is no longer needed, so its absence is no longer a reason to skip.
        var directory = Standard(withDefaultTeams: false).WithRecord("sprk_communication", CommunicationId, ChildBu);
        var h = new Harness(realResolver: directory.Resolver());
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));

        await h.Writer.WriteGateDecisionAsync(Gate());

        h.Row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(UserRef(CommunicationOwner));
    }

    [Fact]
    public async Task Gate_AnUnfiledCommunicationOwnedInTheSecureRecordBusinessUnit_GoesToTheSecureTeam_NeverTheBusinessUnitDefaultTeam_D144()
    {
        // Task 149 K4 (pass-2 review): D-144's "owning business unit's DEFAULT team" must not leak a secure communication's decision
        // into the Secure Record BU's DEFAULT team (task 144: that team is a decoy and is never chosen). The second ask is the REAL
        // resolver's plain-parent answer, so the Secure Record business unit is answered with its NAMED owner team (secure-if-any).
        // The communication is unfiled (no regarding lookup) and owned by a user in the Secure Record BU (no owning team).
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, SecureBu);
        var h = new Harness(realResolver: directory.Resolver()) { ReadBackTeam = SecureNamedTeam };

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        var owner = h.Row.GetAttributeValue<EntityReference>("ownerid");
        owner.Id.Should().Be(SecureNamedTeam, "the Secure Record BU's NAMED owner team");
        owner.Id.Should().NotBe(SecureDefaultTeam, "the Secure Record BU's default team is a decoy that is never chosen (task 144)");
        owner.Id.Should().NotBe(ChildTeam).And.NotBe(GeneralTeam);
        result.SecureOwnerTeamId.Should().Be(SecureNamedTeam, "D-33 / D-123: a gate record about a communication held in the Secure Record BU is a secure child");
        h.Row.Contains("owninguser").Should().BeFalse();
    }

    [Fact]
    public async Task Gate_AnUnchangedAnswer_IsAskedAgainOnce_AsAPlainParent_OverTheSameParents_AndAnOrdinaryTeamAnswerIsDiscarded_D144Amended()
    {
        var h = new Harness();
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        var team = Guid.NewGuid();
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.Is<RecordOwnershipContext>(c => c.KeepCreatorUnlessTargetIsTeamOwned), It.IsAny<CancellationToken>()))
            .Callback<RecordOwnershipContext, CancellationToken>((c, _) => h.OwnerAsks.Add(c))
            .ReturnsAsync(RecordOwnerResolution.Unchanged("not team-owned and filed under nothing"));
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.Is<RecordOwnershipContext>(c => !c.KeepCreatorUnlessTargetIsTeamOwned), It.IsAny<CancellationToken>()))
            .Callback<RecordOwnershipContext, CancellationToken>((c, _) => h.OwnerAsks.Add(c))
            .ReturnsAsync(RecordOwnerResolution.Owned(team));

        await h.Writer.WriteGateDecisionAsync(Gate());

        h.OwnerAsks.Should().HaveCount(2, "one ask as content of the communication, one more as a plain parent, never a third");
        h.OwnerAsks[0].KeepCreatorUnlessTargetIsTeamOwned.Should().BeTrue();
        h.OwnerAsks[1].KeepCreatorUnlessTargetIsTeamOwned.Should().BeFalse();
        h.OwnerAsks[1].Parents.Should().Equal(h.OwnerAsks[0].Parents, "the same parents, so the business unit is the communication's");
        h.OwnerAsks[1].RequestedBy.Should().BeNull();
        h.Row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(UserRef(CommunicationOwner),
            "the second ask is only the secure-if-any check: an ordinary team it names is discarded, never applied");
        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().NotBe(team);
    }

    [Theory]
    [InlineData(RecordOwnerRefusal.SecureOwnerTeamUnresolved)]
    [InlineData(RecordOwnerRefusal.SecureParentNotIsolated)]
    [InlineData(RecordOwnerRefusal.SecureBusinessUnitAmbiguous)]
    [InlineData("fault")]
    public async Task Gate_WhenTheSecondAskRefusesOrFaults_NothingIsWritten_AndTheCommunicationsOwnerIsNeverRead_FailClosed_D144Amended(string answer)
    {
        // uac-r2 F3. The first ask answers Unchanged (an unfiled, user-owned communication); only the SECOND ask (the secure-if-any check)
        // answers differently. Only ParentUnresolved (the unit cannot be read: the by-design skip) and NoDefaultOwnerTeam (ordinary, the unit
        // has no default team) may lead to the communication's owner; every other refusal, and a fault, must refuse and write nothing. The
        // owner row is seeded, so an implementation that routed the refusal to the user owner would write a record and read the row.
        var h = new Harness();
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.Is<RecordOwnershipContext>(c => c.KeepCreatorUnlessTargetIsTeamOwned), It.IsAny<CancellationToken>()))
            .Callback<RecordOwnershipContext, CancellationToken>((c, _) => h.OwnerAsks.Add(c))
            .ReturnsAsync(RecordOwnerResolution.Unchanged("not team-owned and filed under nothing"));
        var second = h.Ownership.Setup(r => r.ResolveOwnerAsync(It.Is<RecordOwnershipContext>(c => !c.KeepCreatorUnlessTargetIsTeamOwned), It.IsAny<CancellationToken>()))
            .Callback<RecordOwnershipContext, CancellationToken>((c, _) => h.OwnerAsks.Add(c));
        if (answer == "fault")
            second.ThrowsAsync(new InvalidOperationException("dataverse"));
        else
            second.ReturnsAsync(RecordOwnerResolution.Refused(answer, "refused"));

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        var thrown = (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which;
        thrown.Reason.Should().Be(answer == "fault" ? DecisionRecordRefusalReason.OwnerResolutionFailed : DecisionRecordRefusalReason.OwnerRefused);
        if (answer != "fault")
            thrown.OwnerRefusalCode.Should().Be(answer);
        h.OwnerAsks.Should().HaveCount(2, "the second ask was really made, so its answer is what is being tested");
        h.Created.Should().BeEmpty();
        h.Lookups.Should().NotContain(q => q.EntityName == "sprk_communication", "the communication's owner row is never read after a refused or faulted secure check");
    }

    [Fact]
    public async Task Gate_AnUnfiledCommunicationWhoseOwnerIsDisabled_WritesNothing_IsASkipNotAFailure_AndReadsTheOwnerOnce_D144Amended()
    {
        // uac-r2 K2: Dataverse refuses to assign a record to a disabled user, a retry meets the same condition, and the dead-lettered job would
        // page the writer-failures alert. So it is a counted skip (Warning), decided from isdisabled returned with the owner row.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (failureListener, failures) = ListenReasons(scope);
        var (skipListener, skips) = ListenReasons(scope, "ontology.writer.skips");
        using var _ = failureListener;
        using var __ = skipListener;
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var h = new Harness(realResolver: directory.Resolver());
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        h.Rows[("sprk_communication", CommunicationId)]["owner.isdisabled"] = new AliasedValue("systemuser", "isdisabled", true);

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.GateSkippedOwnerDisabled);
        h.Created.Should().BeEmpty();
        failures.Should().BeEmpty("a disabled owner is by design, not a failure");
        skips.Should().Equal(DecisionRecordRefusalReason.GateSkippedOwnerDisabled);
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.GateDecisionSkippedOwnerDisabled).Which.Level.Should().Be(LogLevel.Warning);
        h.Log.Entries.Should().NotContain(e => e.EventId == DecisionRecordEvents.WriteRefused);
        var ownerReads = h.Lookups.Where(q => q.EntityName == "sprk_communication").ToList();
        ownerReads.Should().ContainSingle("isdisabled comes with the owner row: no second request");
        // The fixture seeds "owner.isdisabled" whatever the query asks for, so pin the link that produces that key: a renamed
        // alias or re-pointed link would leave the value missing in production, and every user-owned gate would then skip.
        var ownerLink = ownerReads[0].LinkEntities.Should().ContainSingle().Which;
        ownerLink.Columns.Columns.Should().Contain("isdisabled");
        ownerLink.EntityAlias.Should().Be("owner");
        ownerLink.LinkFromAttributeName.Should().Be("ownerid");
        ownerLink.LinkToEntityName.Should().Be("systemuser");
        ownerLink.LinkToAttributeName.Should().Be("systemuserid");
        ownerLink.JoinOperator.Should().Be(JoinOperator.LeftOuter);
    }

    [Fact]
    public async Task Gate_AnUnfiledCommunicationWhoseUserOwnerCarriesNoIsDisabledValue_IsTreatedAsDisabled_WritesNothing_Issue1576()
    {
        // Issue #1576 (fail closed): only an explicit isdisabled = false is an active owner. A user owner whose linked row comes back without
        // the value is not proof of an active user, so it takes the disabled skip, never an assignment.
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var h = new Harness(realResolver: directory.Resolver());
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        h.Rows[("sprk_communication", CommunicationId)].Attributes.Remove("owner.isdisabled");

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.GateSkippedOwnerDisabled);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_ATeamOwnedOrFiledCommunication_IsAskedOnce_TheSecondAskIsOnlyForUnchanged()
    {
        var h = new Harness();
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };

        await h.Writer.WriteGateDecisionAsync(Gate());

        h.OwnerAsks.Should().ContainSingle("an Owned answer is used as returned");
    }

    [Theory]
    [InlineData("noBusinessUnit")]
    [InlineData("noOwner")]
    [InlineData("ownerIsATeam")]
    [InlineData("rowMissing")]
    public async Task Gate_AnUnfiledCommunicationWithAMissingOrUnreadableOwner_WritesNothing_IsASkipNotAFailure_NeverFallsToATeam_D144Amended(string shape)
    {
        // Negative case of D-144 as amended: nothing written, counted on ontology.writer.skips and never on ontology.writer.failures.
        // REAL resolver. The owner is missing (no ownerid, no row), unreadable as a user (a team) or the unit cannot be read (noBusinessUnit,
        // which the secure check needs): in none of them does the record fall through to the unit's default team.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (failureListener, failures) = ListenReasons(scope);
        var (skipListener, skips) = ListenReasons(scope, "ontology.writer.skips");
        using var _ = failureListener;
        using var __ = skipListener;
        var directory = shape == "noBusinessUnit"
            ? Standard().WithRecord("sprk_communication", CommunicationId, owningBusinessUnit: null)
            : Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var h = new Harness(realResolver: directory.Resolver());
        switch (shape)
        {
            case "noBusinessUnit":
                CommunicationOwnedBy(h, UserRef(CommunicationOwner));
                break;
            case "noOwner":
                CommunicationOwnedBy(h, owner: null);
                break;
            case "ownerIsATeam":
                CommunicationOwnedBy(h, new EntityReference("team", ChildTeam));
                break;
        }

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        var thrown = (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which;
        thrown.Reason.Should().Be(DecisionRecordRefusalReason.GateSkippedNoUnitTeam);
        thrown.OwnerRefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        h.Created.Should().BeEmpty();
        failures.Should().BeEmpty("a by-design skip is not a failure: an alert on ontology.writer.failures stays unfiltered and quiet");
        skips.Should().Equal(DecisionRecordRefusalReason.GateSkippedNoUnitTeam);
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.GateDecisionSkippedNoUnitTeam).Which.Level.Should().Be(LogLevel.Warning);
        h.Log.Entries.Should().NotContain(e => e.EventId == DecisionRecordEvents.WriteRefused, "the skip is not logged as a refusal at Error");
        h.Log.Entries.Single(e => e.EventId == DecisionRecordEvents.GateDecisionSkippedNoUnitTeam).Message
            .Should().Contain(GateEvaluation.ToString("D")).And.Contain(thrown.OwnerRefusalCode!, "the run id and the resolver's refusal code, nothing else");
    }

    [Fact]
    public async Task Gate_AnOwnerRefusalForACoreRecord_IsStillARealRefusal_LoggedAtError_NotTheSkip()
    {
        // D-144's skip is scoped to what it names: a communication that stands alone. A communication filed under a matter whose business
        // unit cannot be read is a fail-closed refusal that needs attention (security fails closed), counted as a failure.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (failureListener, failures) = ListenReasons(scope);
        var (skipListener, skips) = ListenReasons(scope, "ontology.writer.skips");
        using var _ = failureListener;
        using var __ = skipListener;
        var h = new Harness(RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "no business unit"));
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        h.Created.Should().BeEmpty();
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.WriteRefused).Which.Level.Should().Be(LogLevel.Error);
        h.Log.Entries.Should().NotContain(e => e.EventId == DecisionRecordEvents.GateDecisionSkippedNoUnitTeam);
        failures.Should().Equal(DecisionRecordRefusalReason.OwnerRefused);
        skips.Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_ARefusalOtherThanNoUnitOrNoTeam_ForAStandAloneCommunication_IsStillARealRefusal()
    {
        var h = new Harness(RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "not isolated"));

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.WriteRefused).Which.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task Gate_ACommunicationUnderASecureMatter_IsASecureChild_OwnedByTheSecureTeam_AndTheOwnerIsAskedAboutEveryParent()
    {
        var directory = Standard()
            .WithSecureRoot("sprk_matter", MatterId)
            .WithRecord("sprk_communication", CommunicationId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        var h = new Harness(realResolver: directory.Resolver()) { ReadBackTeam = SecureNamedTeam };
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureNamedTeam);
        result.SecureOwnerTeamId.Should().Be(SecureNamedTeam, "D-33 / D-123: a gate record on a Secure matter is a secure child");
        h.Row.Contains(RecordCreatorPerson.Column).Should().BeFalse();
    }

    [Fact]
    public async Task Gate_TheOwnerIsAskedWithTheCoreFirst_ThenTheCommunication_ThenTheStamps_AndNoRequester()
    {
        var h = new Harness();
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };

        await h.Writer.WriteGateDecisionAsync(Gate());

        var asked = h.OwnerAsks.Should().ContainSingle().Subject;
        asked.Parents.Should().Equal(Parent("sprk_matter", MatterId), Parent("sprk_communication", CommunicationId));
        asked.RequestedBy.Should().BeNull("no person asked: a system decision");
        asked.KeepCreatorUnlessTargetIsTeamOwned.Should().BeTrue("a gate record is asked as content of the communication first: a filed one resolves from what it is filed under, and only an unfiled one (Unchanged) is asked again as a plain parent (D-144)");
    }

    [Fact]
    public async Task Gate_OneDecision_DerivesTheStampsOnce_AndTheCatalogIsReadOncePerType_AcrossDecisions()
    {
        // K2 (throughput, task 042 follow-up): the gate's DR write is awaited inline in enrichment on the shared app identity, so every
        // Dataverse request counts. BEFORE: 2 stamp derivations per decision (ResolveGateCoreAsync, then StampAsync) = 2 reads of the
        // communication's root columns and 3 column probes; 2 sprk_recordtype_ref queries per decision (the core's type, then the
        // communication's type through a builder created per call) = 4 over two decisions.
        var h = FiledCommunication(("sprk_regardingmatter", "sprk_matter", MatterId));
        var rootReads = 0;
        h.Entities.Setup(e => e.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref rootReads))
            .ReturnsAsync(h.Rows[("sprk_communication", CommunicationId)]);

        await h.Writer.WriteGateDecisionAsync(Gate());
        var afterFirst = (RootReads: rootReads, Probes: h.ProbeCalls, Types: h.RecordTypes.Queried.Count);
        // A second, DIFFERENT decision (a deny): the same communication and outcome would be found as already recorded (task 149).
        await h.Writer.WriteGateDecisionAsync(Gate(authorize: false) with { EvaluationId = Guid.NewGuid() });

        afterFirst.RootReads.Should().Be(1, "the stamps are derived once and reused by the filing step");
        rootReads.Should().Be(2, "one derivation per decision");
        afterFirst.Probes.Should().Be(2, "the communication's root columns and the host table's columns, each once");
        afterFirst.Types.Should().Be(2, "the core's type (sprk_matter) and the communication's type, one query each");
        // A FOUND row is cached (sprk_matter: read once for both decisions). The harness catalog has no sprk_communication row; a miss is not
        // remembered (uac-r2 re-check K2), so that one is read once per decision, as before the cache.
        h.RecordTypes.Queried.Count(t => t == "sprk_matter").Should().Be(1, "the second decision reads the found row from the cache, not Dataverse");
        h.RecordTypes.Queried.Count(t => t == "sprk_communication").Should().Be(2, "a miss is read again so a row added later is picked up");
        h.Created.Should().HaveCount(2);
    }

    [Fact]
    public async Task Gate_TheFirstCommunicationReadTimesOut_TheFilingStepDerivesAgain_AndTheRecordIsWrittenWithItsStamps()
    {
        // F2c: ResolveGateCoreAsync's derivation FAILS (a transient fault), so there is nothing to hand to the filing step; the filing
        // step derives again on its own (and would classify a second failure). The record is written, stamped, without the core pair.
        var h = FiledCommunication(("sprk_regardingmatter", "sprk_matter", MatterId));
        var reads = 0;
        var row = h.Rows[("sprk_communication", CommunicationId)];
        h.Entities.Setup(e => e.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref reads) == 1 ? Task.FromException<Entity>(new TimeoutException("dataverse")) : Task.FromResult(row));

        await h.Writer.WriteGateDecisionAsync(Gate());

        reads.Should().Be(2, "the first read faulted, the filing step's own derivation read it again");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId, "the stamp from the second derivation");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        h.Row.Contains("sprk_corerecordtype").Should().BeFalse("the core could not be decided on the failed first derivation");
    }

    [Fact]
    public async Task Gate_AnOwnerRefusal_WritesNothing_AndThrowsTheWritersOneException()
    {
        var h = new Harness(RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "nothing to own the record from"));

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_ACreateFailure_IsWrapped_NotLeaked()
    {
        var h = new Harness();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse"));

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        var thrown = await act.Should().ThrowAsync<DecisionRecordRefusedException>();
        thrown.Which.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
        thrown.Which.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Theory]
    [InlineData("noFacts")]
    [InlineData("emptyCommunication")]
    [InlineData("emptyEvaluation")]
    [InlineData("blankReason")]
    [InlineData("longActionCode")]
    [InlineData("blankLabel")]
    public async Task Gate_AnInvalidRequest_IsRefusedBeforeAnyRead_AndNothingIsWritten(string flaw)
    {
        var h = new Harness();
        var good = Gate();
        var bad = flaw switch
        {
            "noFacts" => good with { Facts = new Dictionary<string, object?>() },
            "emptyCommunication" => good with { CommunicationId = Guid.Empty },
            "emptyEvaluation" => good with { EvaluationId = Guid.Empty },
            "blankReason" => good with { Reason = " " },
            "longActionCode" => good with { ProposedActionCode = new string('x', 101) },
            _ => good with { ProposedActionLabel = "" },
        };

        var act = () => h.Writer.WriteGateDecisionAsync(bad);

        var reason = (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason;
        reason.Should().Be(flaw == "noFacts" ? DecisionRecordRefusalReason.FactSnapshotMissing : DecisionRecordRefusalReason.ReviewInvalid);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
        h.Entities.Invocations.Should().BeEmpty("validation runs before any Dataverse read");
    }

    [Fact]
    public async Task Gate_AFilingThatCannotBeDerived_IsARefusal_NotALostCall_AndNothingIsWritten()
    {
        var h = new Harness { RowReadFault = new TimeoutException("dataverse") };

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_TheReviewEntryPointIsUnchanged_AReviewStillNeedsAHumanAPolicyVersionAndASignal()
    {
        // WriteAsync keeps every refusal the gate path relaxes: the relaxation is in the new entry point, not in the shared content.
        var h = new Harness();
        var review = Request();

        foreach (var broken in new[]
                 {
                     review with { ConfirmedByUserId = Guid.Empty },
                     review with { PolicyVersionId = Guid.Empty },
                     review with { ResolvedSignalIds = [] },
                 })
        {
            var act = () => h.Writer.WriteAsync(broken);
            (await act.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        }

        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_AFilingThatLandsBetweenTheOwnerDecisionAndTheCreate_ReownsTheRecordAtWrite_D169_042bRound3()
    {
        // 042b round 3 F1-b: the owner is decided while the communication is unfiled (its user); the filing lands before the create, and its
        // AfterRefileAsync finds no gate record yet; the create lands user-owned. The at-write step sees the communication FILED and re-owns the
        // record as the filing would, instead of leaving it user-owned and unshared for good.
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var world = SharedCommunication();
        world.OwnerAnswers(RecordOwnerResolution.Owned(ChildTeam));
        var h = new Harness(realResolver: directory.Resolver(), participantShares: world.Sharing);
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        var reownsBeforeTheCreate = 0;
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity e, CancellationToken _) =>
            {
                world.Rows[(GateSharingWorld.Communication, CommunicationId)]["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());
                world.Sharing.ReownAfterFilingAsync(CommunicationId).GetAwaiter().GetResult(); // the filing's hook: no record yet
                reownsBeforeTheCreate = world.Calls.Count(c => c.StartsWith("assign:", StringComparison.Ordinal));
                h.Created.Add(e);
                world.WithGateRecord(h.NewId, CommunicationId, CommunicationOwner);
                return Task.FromResult(h.NewId);
            });

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        reownsBeforeTheCreate.Should().Be(0, "the filing's own hook found no record");
        h.Row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(UserRef(CommunicationOwner), "decided before the filing");
        world.Rows[(GateSharingWorld.Record, result.RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(ChildTeam,
            "re-owned at write to the resolver's team");
        world.RecordSharees(result.RecordId).Should().BeEmpty("a filed communication's record gets no participant share");
    }
}
