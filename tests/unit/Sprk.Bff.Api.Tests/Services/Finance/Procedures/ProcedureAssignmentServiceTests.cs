using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// <see cref="ProcedureAssignmentService"/> (ebilling-r1 task 089, EB-D-148; procedure model 5.1, 6.2, 6.3, 6.5 I-A1, 15 answer 2).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the invariants the BFF owns for a record's overrides (no plugin, ADR-002): every row is owned through the
/// resolver with the right parents and the caller as RequestedBy, one row per (record, subject), a change is one all-or-nothing transaction
/// in the record's own record-scoped set, and a removal closes only a changed rule's findings, counted for the caller. Dataverse is the
/// <see cref="RuleSetServiceTests.Harness"/> fake (real query evaluation, an all-or-nothing transaction); the rule validation, the Signal
/// SELECTION and <see cref="AffectedInvoiceCounter"/> are real; only the Signal close and the caller's rights probe are substituted.
/// </remarks>
public sealed class ProcedureAssignmentServiceTests
{
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid SecureTeamId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly Guid CallerUserId = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid FirmId = Guid.NewGuid();
    private static readonly Guid OtherFirmId = Guid.NewGuid();
    private const string Matter = "sprk_matter";
    private const string Token = "caller-token";
    private const string CapBody = """{"type":"DailyHoursCap","maxHoursPerDay":10}""";

    private sealed class World
    {
        public readonly RuleSetServiceTests.Harness H = new();
        public readonly Mock<SignalClosureService> Closure;
        public readonly Mock<CallerRecordAccessProbe> Probe = new(
            MockBehavior.Strict, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);

        public readonly HashSet<Guid> Readable = new();
        public readonly List<Guid> SelectedPolicies = new();
        public readonly List<Guid> ClosedPolicies = new();
        public readonly List<bool> ClosedWithBillingSubject = new();
        public bool RightsFault;

        public World()
        {
            H.Seed(new Entity("sprk_organization", FirmId), new Entity("sprk_organization", OtherFirmId), new Entity(Matter, MatterId));
            H.OwnerAnswer = RecordOwnerResolution.Owned(TeamId) with { CreatedByPerson = CallerUserId };
            var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter));
            Closure = new Mock<SignalClosureService>(MockBehavior.Loose, writer, H.Entities.Object, TimeProvider.System,
                NullLogger<SignalClosureService>.Instance) { CallBase = true };
            Closure.Setup(c => c.SelectOpenSignalsOfPolicyAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid policyId, bool withBillingSubject, CancellationToken ct) =>
                {
                    SelectedPolicies.Add(policyId);
                    return await SelectReal(policyId, withBillingSubject, ct);
                });
            Closure.Setup(c => c.ClosePolicySignalsRetiredAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid policyId, string _, bool withBillingSubject, CancellationToken ct) =>
                {
                    ClosedPolicies.Add(policyId);
                    ClosedWithBillingSubject.Add(withBillingSubject);
                    var open = await SelectReal(policyId, withBillingSubject, ct);
                    foreach (var s in open)
                    {
                        H.ConcurrentWrite("sprk_signal", s.Signal.Id, new Dictionary<string, object>
                        {
                            ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved),
                            ["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionPolicyRetired),
                        });
                    }

                    return new PolicySignalClosure(new SignalClosureOutcome(open.Count, 0, 0), open);
                });
            Probe.Setup(p => p.GetCallerSystemUserIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(CallerUserId);
            Probe.Setup(p => p.GetCallerRightsForRecordsOrUnknownAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyList<(string, Guid)>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? token, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken _) =>
                {
                    token.Should().Be(Token, "the rights are asked as the caller");
                    return targets.Select(t => RightsFault
                        ? (AccessRights?)null
                        : Readable.Contains(t.RecordId) ? AccessRights.Read : AccessRights.None).ToList();
                });
        }

        private Task<IReadOnlyList<OpenPolicySignal>> SelectReal(Guid policyId, bool withBillingSubject, CancellationToken ct) =>
            new SignalClosureService((SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)), H.Entities.Object,
                TimeProvider.System, NullLogger<SignalClosureService>.Instance).SelectOpenSignalsOfPolicyAsync(policyId, withBillingSubject, ct);

        public ProcedureAssignmentService Service() => new(
            H.Entities.Object, H.Service(), Closure.Object,
            new AffectedInvoiceCounter(H.Entities.Object, Probe.Object, NullLogger<AffectedInvoiceCounter>.Instance),
            H.Ownership.Object, NullLogger<ProcedureAssignmentService>.Instance);

        public IReadOnlyList<RecordOwnershipContext> OwnerContexts =>
            H.Ownership.Invocations.Where(i => i.Method.Name == nameof(IRecordOwnershipResolver.ResolveOwnerAsync))
                .Select(i => (RecordOwnershipContext)i.Arguments[0]).ToList();

        public Guid Procedure(int status = ProcedureEntity.StatusActive)
        {
            var p = new Entity("sprk_procedure", Guid.NewGuid())
            {
                ["sprk_name"] = "Acme OCG",
                ["sprk_status"] = new OptionSetValue(status),
            };
            H.Seed(p);
            return p.Id;
        }

        public Guid ProcedureVersion(Guid procedureId, int status)
        {
            var v = new Entity("sprk_procedureversion", Guid.NewGuid())
            {
                ["sprk_procedure"] = new EntityReference("sprk_procedure", procedureId),
                ["sprk_status"] = new OptionSetValue(status),
            };
            H.Seed(v);
            return v.Id;
        }

        /// <summary>A published, in-force base rule in a published client-default (or firm-wide) Billing set.</summary>
        public (Guid Set, Guid Policy, Guid Version) BaseRule(string code = "BILL-A", Guid? firm = null, string type = "DailyHoursCap", string body = CapBody)
        {
            var set = H.RuleSet(RuleSetService.RuleSetPublished, scope: firm is null ? (int)RuleSetScope.ClientDefault : (int)RuleSetScope.Firm, firm: firm);
            var (policy, version) = H.Rule(set, code, ProposalState.Accepted, current: true, type: type, body: body);
            return (set, policy, version);
        }

        public Guid Invoice()
        {
            var i = new Entity("sprk_invoice", Guid.NewGuid())
            {
                ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview),
                ["statecode"] = new OptionSetValue(0),
            };
            H.Seed(i);
            return i.Id;
        }

        public Guid Signal(Guid policyId, Guid invoice)
        {
            var s = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", policyId),
                ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen),
                ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice),
            };
            H.Seed(s);
            return s.Id;
        }

        public List<Entity> RowsOf(string table) => H.Rows.GetValueOrDefault(table) ?? new List<Entity>();
    }

    private static JsonObject Cap(int hours) => new() { ["type"] = "DailyHoursCap", ["maxHoursPerDay"] = hours };

    private static void ShouldBeOwnedByTheResolver(Entity row, Guid team)
    {
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", team));
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id.Should().Be(CallerUserId, "the creator person is the caller");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Procedure-level: assign, pin, exclude
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_WritesOneRow_OwnedThroughTheResolverWithTheCoreRecordAsParent_AndTheCallerAsRequester()
    {
        var w = new World();
        var procedure = w.Procedure();

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, "client asked"), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Mode.Should().Be("FollowLatest");
        var row = w.RowsOf("sprk_procedureassignment").Should().ContainSingle().Subject;
        row.GetAttributeValue<EntityReference>(Matter).Id.Should().Be(MatterId);
        row.GetAttributeValue<EntityReference>("sprk_procedure").Id.Should().Be(procedure);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModeFollowLatest);
        ShouldBeOwnedByTheResolver(row, TeamId);

        var context = w.OwnerContexts.Should().ContainSingle().Subject;
        context.Parents.Should().Equal(new RecordOwnershipParent(Matter, MatterId));
        context.RequestedBy!.ObjectId.Should().Be(CallerOid);
        w.H.Transactions.Should().ContainSingle();
    }

    [Fact]
    public async Task Assign_OnASecureRecord_IsOwnedByItsSecureTeam()
    {
        var w = new World();
        w.H.OwnerAnswer = RecordOwnerResolution.Owned(SecureTeamId) with { CreatedByPerson = CallerUserId };

        await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), "Excluded", null, "not for this matter"), CallerOid, default);

        ShouldBeOwnedByTheResolver(w.RowsOf("sprk_procedureassignment").Single(), SecureTeamId);
    }

    [Fact]
    public async Task Pin_NamesAVersionOfThatProcedureThatHasBeenInForce_AndRefusesAnyOther()
    {
        var w = new World();
        var procedure = w.Procedure();
        var superseded = w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusSuperseded);
        var draft = w.ProcedureVersion(procedure, 100000000);
        var otherProceduresVersion = w.ProcedureVersion(w.Procedure(), ProcedureEntity.VersionStatusActive);

        var pinned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, "Pinned", superseded, null), CallerOid, default);
        pinned.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.RowsOf("sprk_procedureassignment").Single().GetAttributeValue<EntityReference>("sprk_pinnedversion").Id.Should().Be(superseded);

        var w2 = new World();
        var p2 = w2.Procedure();
        foreach (var bad in new[] { w2.ProcedureVersion(p2, 100000000), w2.ProcedureVersion(w2.Procedure(), ProcedureEntity.VersionStatusActive) })
        {
            var refused = await w2.Service().AssignProcedureAsync(Matter, MatterId, new(p2, "Pinned", bad, null), CallerOid, default);
            refused.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
            refused.Errors.Should().ContainSingle(e => e.Field == "pinnedVersionId");
        }

        var noVersion = await w2.Service().AssignProcedureAsync(Matter, MatterId, new(p2, "Pinned", null, null), CallerOid, default);
        noVersion.Errors.Should().ContainSingle(e => e.Field == "pinnedVersionId");
        w2.H.Transactions.Should().BeEmpty();
        _ = (draft, otherProceduresVersion);
    }

    [Fact]
    public async Task Assign_ASecondRowForTheSameProcedureOnTheSameRecord_Is409Duplicate_ByTheReadOrByTheKey()
    {
        var w = new World();
        var procedure = w.Procedure();
        await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);

        var second = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, "Excluded", null, null), CallerOid, default);
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("assignment.duplicate");

        // A racing create the read cannot see yet: the derived id collides on the primary key, and the answer is the same 409.
        var w2 = new World();
        var p2 = w2.Procedure();
        w2.H.Seed(new Entity("sprk_procedureassignment",
            ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, ProcedureAssignmentEntity.ProcedureColumn, p2)));
        var raced = await w2.Service().AssignProcedureAsync(Matter, MatterId, new(p2, null, null, null), CallerOid, default);
        raced.ErrorCode.Should().Be("assignment.duplicate");
    }

    [Fact]
    public async Task Assign_ARetiredOrMissingProcedure_IsRefused_AndWritesNothing()
    {
        var w = new World();

        var retired = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(ProcedureEntity.StatusRetired), null, null, null), CallerOid, default);
        var missing = await w.Service().AssignProcedureAsync(Matter, MatterId, new(Guid.NewGuid(), null, null, null), CallerOid, default);

        retired.ErrorCode.Should().Be("assignment.procedure_retired");
        missing.ErrorCode.Should().Be("assignment.procedure_not_found");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_WhenTheOwnerIsRefused_WritesNothing()
    {
        var w = new World();
        w.H.OwnerAnswer = RecordOwnerResolution.Refused("owner.no_team", "no team");

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task TheCoreTableComesFromTheRoute_AndAnythingElseIsABug()
    {
        var w = new World();
        var act = () => w.Service().AssignProcedureAsync("sprk_invoice", MatterId, new(w.Procedure(), null, null, null), CallerOid, default);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Rule-level: exclude
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExcludeRule_WritesOneRowNamingTheRuleAndNoProcedure_ForOneFirm_WithTheReason()
    {
        var w = new World();
        var (_, policy, _) = w.BaseRule();

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "agreed with the client", FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        var row = w.RowsOf("sprk_procedureassignment").Should().ContainSingle().Subject;
        row.GetAttributeValue<EntityReference>("sprk_excludedpolicy").Id.Should().Be(policy);
        row.Contains("sprk_procedure").Should().BeFalse("a rule-level row never names a Procedure");
        row.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        row.GetAttributeValue<string>("sprk_reason").Should().Be("agreed with the client");
        ShouldBeOwnedByTheResolver(row, TeamId);
        w.OwnerContexts.Single().Parents.Should().Equal(new RecordOwnershipParent(Matter, MatterId));
        w.RowsOf("sprk_ruleset").Should().ContainSingle("a plain exclusion creates no record-scoped set");
    }

    [Fact]
    public async Task ExcludeRule_NeedsAReason_AndRefusesTheSystemRule()
    {
        var w = new World();
        var (_, policy, _) = w.BaseRule();
        var (_, system, _) = w.BaseRule("SYS-INTEGRITY", type: "SubmissionIntegrity", body: """{"type":"SubmissionIntegrity"}""");

        var noReason = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "  ", null), CallerOid, default);
        var systemRule = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(system, "why not", null), CallerOid, default);

        noReason.Errors.Should().ContainSingle(e => e.Field == "reason");
        systemRule.ErrorCode.Should().Be("assignment.system_rule");
        w.H.Transactions.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Rule-level: change
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChangeRule_WritesAnExcludeRowAndOnePublishedRecordRuleInARecordScopedSet_InOneTransaction()
    {
        var w = new World();
        var (_, basePolicy, baseVersion) = w.BaseRule();

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, new(basePolicy, Cap(12), "longer trial days", null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        var view = outcome.Value!;
        w.H.Transactions.Should().ContainSingle().Which.Select(r => r.GetType().Name + ":" + ((r as CreateRequest)?.Target.LogicalName ?? (r as UpdateRequest)?.Target.LogicalName))
            .Should().Equal("CreateRequest:sprk_ruleset", "CreateRequest:sprk_policy", "CreateRequest:sprk_policyversion", "UpdateRequest:sprk_policy",
                "CreateRequest:sprk_procedureassignment");

        var set = w.H.Row("sprk_ruleset", view.RecordRuleSetId!.Value);
        set.GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be(RecordScopedRuleSet.ScopeRecord);
        set.GetAttributeValue<EntityReference>(Matter).Id.Should().Be(MatterId);
        set.Contains("sprk_firm").Should().BeFalse();
        set.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
        RecordScopedRuleSet.IsRecordScoped(set).Should().BeTrue();

        var policy = w.H.Row("sprk_policy", view.RecordRulePolicyId!.Value);
        policy.GetAttributeValue<string>("sprk_policycode").Should().Be(view.RecordRuleCode).And.StartWith("BRR-");
        policy.GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        policy.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(view.RecordRuleVersionId!.Value);
        policy.GetAttributeValue<EntityReference>("sprk_ruleset").Id.Should().Be(set.Id);

        var version = w.H.Row("sprk_policyversion", view.RecordRuleVersionId!.Value);
        version.GetAttributeValue<string>("sprk_rulebody").Should().Contain("12");
        version.GetAttributeValue<EntityReference>("sprk_basedonversion").Id.Should().Be(baseVersion);
        version.GetAttributeValue<OptionSetValue>("sprk_publishstate").Value.Should().Be(RuleSetService.PublishStatePublished);
        version.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Accepted);

        var exclude = w.RowsOf("sprk_procedureassignment").Single();
        exclude.GetAttributeValue<EntityReference>("sprk_excludedpolicy").Id.Should().Be(basePolicy);
        foreach (var row in new[] { set, policy, version, exclude })
        {
            ShouldBeOwnedByTheResolver(row, TeamId);
        }

        // The set's parents are [core record]; created with the set, the policy and version take that same resolution.
        w.OwnerContexts.Should().ContainSingle().Which.Parents.Should().Equal(new RecordOwnershipParent(Matter, MatterId));
        w.H.Row("sprk_policy", basePolicy).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue("the base rule is untouched");
    }

    [Fact]
    public async Task ChangeRule_ForOneFirm_UsesAFirmRecordSet_AndASecondChangeReusesTheSet_WithThePolicyFiledUnderIt()
    {
        var w = new World();
        var (_, a, _) = w.BaseRule("BILL-A");
        var (_, b, _) = w.BaseRule("BILL-B");

        var first = await w.Service().ChangeRuleAsync(Matter, MatterId, new(a, Cap(12), "firm-specific", FirmId), CallerOid, default);
        var second = await w.Service().ChangeRuleAsync(Matter, MatterId, new(b, Cap(8), "firm-specific", FirmId), CallerOid, default);

        first.Value!.RecordRuleSetId.Should().Be(second.Value!.RecordRuleSetId!.Value, "one record-scoped set per record and firm");
        var set = w.H.Row("sprk_ruleset", first.Value.RecordRuleSetId!.Value);
        set.GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be(RecordScopedRuleSet.ScopeFirmRecord);
        set.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        w.H.Transactions[1].OfType<CreateRequest>().Select(c => c.Target.LogicalName).Should().NotContain("sprk_ruleset");

        // Second change: [core] for the Exclude row, [the stored set] for the record rule (its version takes the policy's answer).
        w.OwnerContexts.Skip(1).Select(c => c.Parents.Single()).Should().Equal(
            new RecordOwnershipParent(Matter, MatterId), new RecordOwnershipParent("sprk_ruleset", set.Id));
    }

    [Fact]
    public async Task ChangeRule_WhenALaterWriteFails_LeavesNeitherTheSetNorTheRuleNorTheExcludeRow()
    {
        var w = new World();
        var (_, basePolicy, _) = w.BaseRule();
        var policiesBefore = w.RowsOf("sprk_policy").Count;
        w.H.FailTransactionRequest = r => r is CreateRequest c && c.Target.LogicalName == "sprk_policyversion";

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, new(basePolicy, Cap(12), "x", null), CallerOid, default);

        outcome.ErrorCode.Should().Be("assignment.write_failed");
        w.RowsOf("sprk_ruleset").Should().ContainSingle("only the base rule's set");
        w.RowsOf("sprk_policy").Should().HaveCount(policiesBefore);
        w.RowsOf("sprk_procedureassignment").Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeRule_AFirmNamedBaseRule_ChangedForAnotherFirmOrEveryFirm_Is400()
    {
        var w = new World();
        var (_, firmRule, _) = w.BaseRule(firm: FirmId);

        var otherFirm = await w.Service().ChangeRuleAsync(Matter, MatterId, new(firmRule, Cap(12), "x", OtherFirmId), CallerOid, default);
        var everyFirm = await w.Service().ChangeRuleAsync(Matter, MatterId, new(firmRule, Cap(12), "x", null), CallerOid, default);
        var ownFirm = await w.Service().ChangeRuleAsync(Matter, MatterId, new(firmRule, Cap(12), "x", FirmId), CallerOid, default);

        otherFirm.ErrorCode.Should().Be("assignment.firm_mismatch");
        everyFirm.ErrorCode.Should().Be("assignment.firm_mismatch");
        ownFirm.Kind.Should().Be(RuleSetOutcomeKind.Done);
    }

    [Fact]
    public async Task ChangeRule_AnInvalidEditedRule_IsRefusedByTheRuleValidator_AndWritesNothing()
    {
        var w = new World();
        var (_, basePolicy, _) = w.BaseRule();

        var outcome = await w.Service().ChangeRuleAsync(
            Matter, MatterId, new(basePolicy, new JsonObject { ["type"] = "DailyHoursCap", ["maxHoursPerDay"] = "ten" }, "x", null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("assignment.invalid");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeRule_ARuleThatIsNotInForce_Is409RuleNotInForce_AndWritesNothing_ButAProcedureSetsRuleCanBeChanged()
    {
        var w = new World();
        var (_, switchedOff, _) = w.BaseRule("BILL-OFF");
        w.H.Row("sprk_policy", switchedOff)["sprk_enabled"] = false;
        var retiredSet = w.H.RuleSet(RuleSetService.RuleSetRetired, scope: (int)RuleSetScope.ClientDefault);
        var (inRetiredSet, _) = w.H.Rule(retiredSet, "BILL-RET", ProposalState.Accepted, current: true);

        foreach (var policy in new[] { switchedOff, inRetiredSet })
        {
            var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, new(policy, Cap(12), "x", null), CallerOid, default);
            outcome.ErrorCode.Should().Be("assignment.rule_not_in_force", "a change would revive a rule that is not in force on this record");
        }

        w.H.Transactions.Should().BeEmpty();

        // Must not fire: a Procedure set is Draft by design (in force through its Procedure Version), so its On rule can be changed.
        var procedureSet = w.H.RuleSet(RuleSetService.RuleSetDraft, scope: (int)RuleSetScope.ClientDefault);
        w.H.Row("sprk_ruleset", procedureSet)[RuleSetService.ProcedureColumn] = new EntityReference("sprk_procedure", w.Procedure());
        var (procedureRule, _) = w.H.Rule(procedureSet, "BILL-PROC", ProposalState.Accepted, current: true);
        (await w.Service().ChangeRuleAsync(Matter, MatterId, new(procedureRule, Cap(12), "x", null), CallerOid, default))
            .Kind.Should().Be(RuleSetOutcomeKind.Done);
    }

    [Fact]
    public async Task ARecordRule_IsNotABaseRule_AndARecordScopedSetIsHiddenFromTheRuleSetAdminReads()
    {
        var w = new World();
        var (_, basePolicy, _) = w.BaseRule();
        var changed = await w.Service().ChangeRuleAsync(Matter, MatterId, new(basePolicy, Cap(12), "x", null), CallerOid, default);

        var excludeTheRecordRule = await w.Service().ExcludeRuleAsync(
            Matter, MatterId, new(changed.Value!.RecordRulePolicyId, "x", null), CallerOid, default);
        excludeTheRecordRule.ErrorCode.Should().Be("assignment.rule_not_found");

        var list = await w.H.Service().ListAsync(default);
        list.Value!.Select(s => s.Id).Should().NotContain(changed.Value.RecordRuleSetId!.Value);
        (await w.H.Service().GetAsync(changed.Value.RecordRuleSetId!.Value, default)).Kind.Should().NotBe(RuleSetOutcomeKind.Done);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Remove and its preview
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RemovingAPlainOverride_DeletesTheRow_AndClosesNothing()
    {
        var w = new World();
        var (_, policy, _) = w.BaseRule();
        var excluded = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), CallerOid, default);

        var preview = await w.Service().PreviewRemoveAsync(Matter, MatterId, excluded.Value!.Id, Token, default);
        var removed = await w.Service().RemoveAsync(Matter, MatterId, excluded.Value.Id, Token, default);

        preview.Value!.ClosesRule.Should().BeFalse();
        preview.Value.Warning.Should().BeNull();
        removed.Value!.RulesSwitchedOff.Should().Be(0);
        w.RowsOf("sprk_procedureassignment").Should().BeEmpty();
        w.H.Transactions.Last().Should().ContainSingle().Which.Should().BeOfType<DeleteRequest>();
        w.ClosedPolicies.Should().BeEmpty();
        w.SelectedPolicies.Should().BeEmpty();
    }

    private static async Task<(World W, OverrideView Changed, Guid[] Invoices)> SceneChangedRule()
    {
        var w = new World();
        var (_, basePolicy, _) = w.BaseRule();
        var changed = (await w.Service().ChangeRuleAsync(Matter, MatterId, new(basePolicy, Cap(12), "x", null), CallerOid, default)).Value!;
        var invoices = new[] { w.Invoice(), w.Invoice(), w.Invoice() };
        foreach (var invoice in invoices)
        {
            w.Signal(changed.RecordRulePolicyId!.Value, invoice);
        }

        w.Readable.UnionWith(invoices.Take(2));
        return (w, changed, invoices);
    }

    [Fact]
    public async Task ThePreviewOfAChangedRuleOverride_ShowsTheWarningAndTheCallerScopedCount_AndWritesNothing()
    {
        var (w, changed, _) = await SceneChangedRule();
        var writes = w.H.Writes.Count;

        var preview = await w.Service().PreviewRemoveAsync(Matter, MatterId, changed.Id, Token, default);

        preview.Value!.ClosesRule.Should().BeTrue();
        preview.Value.Warning.Should().Be("Removing this override will close the rule it changed and remove pending decisions on active invoices.");
        preview.Value.AffectedText.Should().Be("2 open invoices you can see are affected.");
        preview.Value.VisibleOpenInvoices.Should().Be(2);
        w.H.Writes.Should().HaveCount(writes, "the preview writes nothing");
        w.ClosedPolicies.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePreview_WhenTheRightsReadFaults_ShowsTheWarningWithNoCount()
    {
        var (w, changed, _) = await SceneChangedRule();
        w.RightsFault = true;

        var preview = await w.Service().PreviewRemoveAsync(Matter, MatterId, changed.Id, Token, default);

        preview.Value!.Warning.Should().NotBeNullOrEmpty();
        preview.Value.AffectedText.Should().BeNull();
        preview.Value.VisibleOpenInvoices.Should().BeNull();
    }

    [Fact]
    public async Task RemovingAChangedRuleOverride_SwitchesTheRecordRuleOff_InTheSameTransaction_AndClosesItsSignalsThroughTheSharedMethod()
    {
        var (w, changed, invoices) = await SceneChangedRule();

        var removed = await w.Service().RemoveAsync(Matter, MatterId, changed.Id, Token, default);

        removed.Value!.RulesSwitchedOff.Should().Be(1);
        removed.Value.CloseComplete.Should().BeTrue();
        removed.Value.AffectedText.Should().Be("2 open invoices you can see are affected.");
        w.H.Transactions.Last().Select(r => r.GetType().Name).Should().Equal("DeleteRequest", "UpdateRequest");
        w.H.Row("sprk_policy", changed.RecordRulePolicyId!.Value).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        w.RowsOf("sprk_procedureassignment").Should().BeEmpty();
        w.ClosedPolicies.Should().Equal(changed.RecordRulePolicyId!.Value);
        w.ClosedWithBillingSubject.Should().Equal(true);
        w.RowsOf("sprk_signal").Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusResolved);
        _ = invoices;
    }

    [Fact]
    public async Task AnOverrideOfAnotherRecord_IsNotFound_ForPreviewRemoveOrTheRouteTarget()
    {
        var w = new World();
        var excluded = (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(w.BaseRule().Policy, "x", null), CallerOid, default)).Value!;
        var otherMatter = Guid.NewGuid();

        (await w.Service().PreviewRemoveAsync(Matter, otherMatter, excluded.Id, Token, default)).ErrorCode.Should().Be("assignment.not_found");
        (await w.Service().RemoveAsync(Matter, otherMatter, excluded.Id, Token, default)).ErrorCode.Should().Be("assignment.not_found");
        (await w.Service().TargetOfAsync(Matter, otherMatter, excluded.Id, default)).Should().BeNull();
        (await w.Service().TargetOfAsync(Matter, MatterId, excluded.Id, default)).Should().Be(new OverrideTarget("sprk_policies", excluded.PolicyId!.Value));
        w.RowsOf("sprk_procedureassignment").Should().ContainSingle();
    }
}
