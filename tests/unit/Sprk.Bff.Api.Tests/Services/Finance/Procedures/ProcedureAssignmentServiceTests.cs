using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// <see cref="ProcedureAssignmentService"/> (ebilling-r1 task 089b, EB-D-148; procedure model 5.1, 6.2, 6.3, 6.5 I-A1).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the invariants the BFF owns for a record's overrides (no plugin, ADR-002): every row is owned through the
/// resolver by its own question with the core record as its one parent and the caller as RequestedBy; one row per (record, Procedure) and one
/// per (record, rule), by the read and by the derived id; a refusal writes nothing. Dataverse is the <see cref="RuleSetServiceTests.Harness"/>
/// fake (real query evaluation, an all-or-nothing transaction that faults a duplicate primary key); the owner is the resolver substitute,
/// except in the two tests that run the REAL resolver over <see cref="OwnershipDirectory"/>.
/// </remarks>
public sealed class ProcedureAssignmentServiceTests
{
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly Guid CallerUserId = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid FirmId = Guid.NewGuid();
    private const string Matter = "sprk_matter";
    private const string Assignment = "sprk_procedureassignment";

    private sealed class World
    {
        public readonly RuleSetServiceTests.Harness H = new();

        public World()
        {
            H.Seed(new Entity("sprk_organization", FirmId), new Entity(Matter, MatterId));
            H.OwnerAnswer = RecordOwnerResolution.Owned(TeamId) with { CreatedByPerson = CallerUserId };

            // 089b review 2 F1: the fake used to answer any column set on sprk_organization, so a $select naming sprk_name (which production
            // refuses) passed. Refuse a column the table does not have, as Dataverse does.
            H.AfterQuery = q =>
            {
                var unknown = q.EntityName == "sprk_organization" ? q.ColumnSet.Columns.Except(OrganizationColumns).ToArray() : Array.Empty<string>();
                if (unknown.Length > 0)
                {
                    throw new InvalidOperationException($"sprk_organization has no column {string.Join(", ", unknown)} (its primary name is sprk_organizationname).");
                }
            };
        }

        /// <summary>The columns of <c>sprk_organization</c> a query here may name (the key and the primary name; Entities/sprk_Organization/Entity.xml).</summary>
        private static readonly string[] OrganizationColumns = { "sprk_organizationid", "sprk_organizationname" };

        public ProcedureAssignmentService Service(IRecordOwnershipResolver? ownership = null) =>
            new(H.Entities.Object, ownership ?? H.Ownership.Object, NullLogger<ProcedureAssignmentService>.Instance);

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

        /// <summary>An in-force rule in a published Billing set of the given scope (client default unless told).</summary>
        public Guid Rule(string code = "BILL-A", int scope = (int)RuleSetScope.ClientDefault, string type = "DailyHoursCap", string? body = null,
            bool withMatterLookup = false)
        {
            var set = H.RuleSet(RuleSetService.RuleSetPublished, scope: scope);
            if (withMatterLookup)
            {
                H.Row("sprk_ruleset", set)[Matter] = new EntityReference(Matter, MatterId);
            }

            return body is null
                ? H.Rule(set, code, ProposalState.Accepted, current: true, type: type).Policy
                : H.Rule(set, code, ProposalState.Accepted, current: true, type: type, body: body).Policy;
        }

        /// <summary>A policy that belongs to NO rule set (<c>sprk_ruleset</c> empty), with its in-force version: the shape the system policy has
        /// in production (<c>Seed-BillingSystemPolicy.ps1</c>: "the system policy belongs to no rule set").</summary>
        public Guid RuleWithoutSet(string code, string type)
        {
            var policy = H.Rule(H.RuleSet(RuleSetService.RuleSetPublished), code, ProposalState.Accepted, current: true, type: type).Policy;
            H.Row("sprk_policy", policy).Attributes.Remove("sprk_ruleset");
            return policy;
        }

        public List<Entity> Rows => H.Rows.GetValueOrDefault(Assignment) ?? new List<Entity>();
    }

    private static void ShouldBeOwnedByTheResolver(Entity row, Guid team, Guid creator)
    {
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", team));
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id.Should().Be(creator, "the creator person is the caller");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Procedure-level: assign, pin, exclude
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_WritesOneRow_OwnedByItsOwnQuestion_WithTheCoreRecordAsItsOneParent_AndTheCallerAsRequester()
    {
        var w = new World();
        var procedure = w.Procedure();

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, "  client asked  "), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Mode.Should().Be("FollowLatest");
        outcome.Value.Kind.Should().Be("Procedure");
        var row = w.Rows.Should().ContainSingle().Subject;
        row.Id.Should().Be(outcome.Value.Id).And.Be(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_procedure", procedure));
        row.GetAttributeValue<EntityReference>(Matter).Id.Should().Be(MatterId);
        row.GetAttributeValue<EntityReference>("sprk_procedure").Id.Should().Be(procedure);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModeFollowLatest);
        row.GetAttributeValue<string>("sprk_reason").Should().Be("client asked");
        row.GetAttributeValue<string>("sprk_name").Should().Be("Acme OCG (Follow latest)");
        row.Contains("sprk_excludedpolicy").Should().BeFalse();
        ShouldBeOwnedByTheResolver(row, TeamId, CallerUserId);

        var context = w.OwnerContexts.Should().ContainSingle().Subject;
        context.ChildEntityLogicalName.Should().Be(Assignment, "the row's own question: a hard-deny table");
        context.Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the Procedure is a citation, not a parent");
        context.RequestedBy!.ObjectId.Should().Be(CallerOid);
        context.CallerObjectId.Should().Be(CallerOid);
        w.H.Transactions.Should().ContainSingle().Which.Should().ContainSingle().Which.Should().BeOfType<CreateRequest>();
    }

    [Theory]
    [InlineData("Excluded", ProcedureAssignmentEntity.ModeExcluded)]
    [InlineData("followlatest", ProcedureAssignmentEntity.ModeFollowLatest)]
    public async Task Assign_TakesTheModeByName(string mode, int stored)
    {
        var w = new World();

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), mode, null, null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.Rows.Single().GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(stored);
    }

    [Fact]
    public async Task Pin_NamesAVersionOfThatProcedureThatHasBeenInForce()
    {
        var w = new World();
        var procedure = w.Procedure();
        var superseded = w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusSuperseded);

        var pinned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, "Pinned", superseded, null), CallerOid, default);

        pinned.Kind.Should().Be(RuleSetOutcomeKind.Done);
        pinned.Value!.PinnedVersionId.Should().Be(superseded);
        var row = w.Rows.Single();
        row.GetAttributeValue<EntityReference>("sprk_pinnedversion").Id.Should().Be(superseded);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModePinned);
        w.OwnerContexts.Single().Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the pinned version is a citation");
    }

    [Fact]
    public async Task Pin_RefusesADraftVersion_AnotherProceduresVersion_NoVersion_OrAVersionOnAnUnpinnedMode_AndWritesNothing()
    {
        var w = new World();
        var procedure = w.Procedure();
        var draft = w.ProcedureVersion(procedure, 100000000);
        var otherProcedures = w.ProcedureVersion(w.Procedure(), ProcedureEntity.VersionStatusActive);
        var active = w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusActive);

        foreach (var request in new ProcedureAssignmentRequest[]
                 {
                     new(procedure, "Pinned", draft, null),
                     new(procedure, "Pinned", otherProcedures, null),
                     new(procedure, "Pinned", Guid.NewGuid(), null),
                     new(procedure, "Pinned", null, null),
                     new(procedure, "FollowLatest", active, null),
                 })
        {
            var refused = await w.Service().AssignProcedureAsync(Matter, MatterId, request, CallerOid, default);
            refused.Kind.Should().Be(RuleSetOutcomeKind.Invalid, $"{request}");
            refused.ErrorCode.Should().Be("assignment.invalid");
            refused.Errors.Should().ContainSingle(e => e.Field == "pinnedVersionId");
        }

        w.H.Transactions.Should().BeEmpty();
        w.OwnerContexts.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_NoProcedureOrAnUnknownMode_Is400_AndWritesNothing()
    {
        var w = new World();

        var noProcedure = await w.Service().AssignProcedureAsync(Matter, MatterId, new(null, null, null, null), CallerOid, default);
        var badMode = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), "Sometimes", null, null), CallerOid, default);
        var longReason = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, new string('x', 2001)), CallerOid, default);

        noProcedure.Errors.Should().ContainSingle(e => e.Field == "procedureId");
        badMode.Errors.Should().ContainSingle(e => e.Field == "mode");
        longReason.Errors.Should().ContainSingle(e => e.Field == "reason");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b I-A1 must-fire: a second row for the same Procedure on the same record is 409 assignment.duplicate, whatever its mode, and writes nothing")]
    public async Task Assign_ASecondRowForTheSameProcedureOnTheSameRecord_Is409Duplicate()
    {
        var w = new World();
        var procedure = w.Procedure();
        (await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var second = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, "Excluded", null, null), CallerOid, default);

        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("assignment.duplicate");
        w.Rows.Should().ContainSingle();
        w.H.Transactions.Should().ContainSingle("the read refuses before any write");
    }

    [Fact(DisplayName = "089b I-A1 must-fire: a racing create the read cannot see collides on the derived id and is the same 409")]
    public async Task Assign_ARaceTheReadCannotSee_CollidesOnTheDerivedId_AndIsTheSame409()
    {
        var w = new World();
        var procedure = w.Procedure();
        // The racing row is not yet visible to the read (it carries no matter lookup here), but it holds the derived id.
        w.H.Seed(new Entity(Assignment, ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, ProcedureAssignmentEntity.ProcedureColumn, procedure)));

        var raced = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);

        raced.StatusCode.Should().Be(409);
        raced.ErrorCode.Should().Be("assignment.duplicate");
        w.H.TransactionFaults.Should().ContainSingle().Which.ErrorCode.Should().Be(unchecked((int)0x80040237));
        w.Rows.Should().ContainSingle("only the racing row");
    }

    [Fact(DisplayName = "089b I-A1 must-not-fire: the same Procedure on another record, or another Procedure on the same record, is its own row")]
    public async Task Assign_TheSameProcedureOnAnotherRecord_OrAnotherProcedure_IsNotADuplicate()
    {
        var w = new World();
        var procedure = w.Procedure();
        var otherMatter = Guid.NewGuid();
        var project = Guid.NewGuid();

        var first = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);
        var otherRecord = await w.Service().AssignProcedureAsync(Matter, otherMatter, new(procedure, null, null, null), CallerOid, default);
        var otherTable = await w.Service().AssignProcedureAsync("sprk_project", project, new(procedure, null, null, null), CallerOid, default);
        var otherProcedure = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), CallerOid, default);

        new[] { first, otherRecord, otherTable, otherProcedure }.Should().OnlyContain(o => o.Kind == RuleSetOutcomeKind.Done);
        w.Rows.Select(r => r.Id).Should().OnlyHaveUniqueItems().And.HaveCount(4);
        w.Rows.Single(r => r.Contains("sprk_project")).GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(project);
    }

    [Fact]
    public async Task Assign_ARetiredOrMissingProcedure_IsRefused_AndWritesNothing()
    {
        var w = new World();

        var retired = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(ProcedureEntity.StatusRetired), null, null, null), CallerOid, default);
        var missing = await w.Service().AssignProcedureAsync(Matter, MatterId, new(Guid.NewGuid(), null, null, null), CallerOid, default);

        retired.StatusCode.Should().Be(409);
        retired.ErrorCode.Should().Be("assignment.procedure_retired");
        missing.StatusCode.Should().Be(404);
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
        outcome.OwnerRefusal!.RefusalCode.Should().Be("owner.no_team");
        w.H.Transactions.Should().BeEmpty();
        w.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_WhenTheTransactionGivesNoAnswer_SaysItMayHaveBeenSaved_WithTheId()
    {
        var w = new World();
        var procedure = w.Procedure();
        w.H.TransactionOutcomeUnknown = true;

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);

        outcome.StatusCode.Should().Be(500);
        outcome.ErrorCode.Should().Be("assignment.write_failed");
        outcome.Detail.Should().Contain("may or may not")
            .And.Contain(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_procedure", procedure).ToString());
    }

    [Fact]
    public async Task TheCoreTableComesFromTheRoute_AndAnythingElseIsABug()
    {
        var w = new World();
        var procedure = w.Procedure();

        var invoice = () => w.Service().AssignProcedureAsync("sprk_invoice", MatterId, new(procedure, null, null, null), CallerOid, default);
        var empty = () => w.Service().ExcludeRuleAsync(Matter, Guid.Empty, new(Guid.NewGuid(), "x", null), CallerOid, default);

        await invoice.Should().ThrowAsync<ArgumentException>();
        await empty.Should().ThrowAsync<ArgumentException>();
        w.H.Queries.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Rule-level: exclude
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExcludeRule_WritesOneRowNamingTheRuleAndNoProcedure_ForOneFirm_WithTheReason()
    {
        var w = new World();
        var policy = w.Rule();
        var setsBefore = w.H.Rows["sprk_ruleset"].Count;

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "agreed with the client", FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Kind.Should().Be("RuleExcluded");
        outcome.Value.PolicyId.Should().Be(policy);
        outcome.Value.FirmId.Should().Be(FirmId);
        var row = w.Rows.Should().ContainSingle().Subject;
        row.Id.Should().Be(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_excludedpolicy", policy));
        row.GetAttributeValue<EntityReference>("sprk_excludedpolicy").Id.Should().Be(policy);
        row.Contains("sprk_procedure").Should().BeFalse("a rule-level row never names a Procedure");
        row.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModeExcluded);
        row.GetAttributeValue<string>("sprk_reason").Should().Be("agreed with the client");
        ShouldBeOwnedByTheResolver(row, TeamId, CallerUserId);

        var context = w.OwnerContexts.Single();
        context.Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the rule and the firm are citations");
        context.RequestedBy!.ObjectId.Should().Be(CallerOid);
        w.H.Rows["sprk_ruleset"].Should().HaveCount(setsBefore, "an exclusion creates no record-scoped set");
    }

    [Fact]
    public async Task ExcludeRule_NeedsAReason_AndAFirmThatExists()
    {
        var w = new World();
        var policy = w.Rule();

        var noReason = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "  ", null), CallerOid, default);
        var noFirm = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", Guid.NewGuid()), CallerOid, default);
        var noPolicy = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(null, "x", null), CallerOid, default);

        noReason.Errors.Should().ContainSingle(e => e.Field == "reason");
        noFirm.Errors.Should().ContainSingle(e => e.Field == "firmId");
        noPolicy.Errors.Should().ContainSingle(e => e.Field == "policyId");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review 2 F1 must-fire: the firm existence check selects only sprk_organizationid (sprk_organization has no sprk_name), so an exclude naming a firm succeeds")]
    public async Task ExcludeRule_TheFirmCheck_SelectsOnlyTheKey_NeverSprkName()
    {
        var w = new World();
        var policy = w.Rule();

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var firmQuery = w.H.Queries.Where(q => q.EntityName == "sprk_organization").Should().ContainSingle().Subject;
        firmQuery.ColumnSet.Columns.Should().Equal(new[] { "sprk_organizationid" });
        firmQuery.ColumnSet.Columns.Should().NotContain("sprk_name");
    }

    [Theory(DisplayName = "089b review 2 K2 must-fire: a Dataverse read that faults before the write is the stable 503 assignment.read_failed, and nothing is written")]
    [InlineData("assign")]
    [InlineData("exclude")]
    public async Task APreWriteRead_ThatFaults_Is503ReadFailed_AndWritesNothing(string route)
    {
        var w = new World();
        var procedure = w.Procedure();
        var policy = w.Rule();
        w.H.AfterQuery = _ => throw new TimeoutException("simulated Dataverse read fault");

        var outcome = route == "assign"
            ? await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default)
            : await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be("assignment.read_failed");
        w.H.Transactions.Should().BeEmpty();
        w.OwnerContexts.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review F1 must-fire: the system rule, which belongs to NO rule set (as in production), is 409 system_rule, not 404 rule_not_found")]
    public async Task ExcludeRule_RefusesTheSystemRule_AndWritesNothing()
    {
        var w = new World();
        var system = w.RuleWithoutSet(BillingRuleCatalogue.SystemPolicyCode, "SubmissionIntegrity");

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(system, "why not", null), CallerOid, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.system_rule");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review F1: a SubmissionIntegrity rule inside a published set is still 409 system_rule (the version-type rule)")]
    public async Task ExcludeRule_ASubmissionIntegrityRuleInASet_IsStill409SystemRule()
    {
        var w = new World();
        var system = w.Rule("SYS-INTEGRITY", type: "SubmissionIntegrity", body: """{"type":"SubmissionIntegrity"}""");

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(system, "why not", null), CallerOid, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.system_rule");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review F1 must-not-fire: an ordinary rule with no rule set stays 404 rule_not_found")]
    public async Task ExcludeRule_AnOrdinaryRuleWithNoRuleSet_Is404RuleNotFound()
    {
        var w = new World();
        var ordinary = w.RuleWithoutSet("BILL-ORPHAN", "DailyHoursCap");

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(ordinary, "x", null), CallerOid, default);

        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be("assignment.rule_not_found");
        w.H.Transactions.Should().BeEmpty();
    }

    [Theory(DisplayName = "089b: a record-scoped set's rule (a record or firm-record scope, or any core lookup) is removed, not overridden: 404 rule_not_found")]
    [InlineData(100000002, false)] // sprk_scope Record (task 080)
    [InlineData(100000003, false)] // sprk_scope Firm record
    [InlineData((int)RuleSetScope.ClientDefault, true)]
    public async Task ExcludeRule_ARecordScopedSetsRule_Is404RuleNotFound(int scope, bool withMatterLookup)
    {
        var w = new World();
        var recordRule = w.Rule("BRR-1", scope: scope, withMatterLookup: withMatterLookup);

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(recordRule, "x", null), CallerOid, default);

        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be("assignment.rule_not_found");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExcludeRule_AnUnknownOrSwitchedOffRule_OrAnOntologyRule_Is404()
    {
        var w = new World();
        var inactive = w.Rule("BILL-OFF");
        w.H.Row("sprk_policy", inactive)["statecode"] = new OptionSetValue(1);
        var ontologySet = w.H.RuleSet(RuleSetService.RuleSetPublished, domain: null);
        var (ontology, _) = w.H.Rule(ontologySet, "ONT-1", ProposalState.Accepted, current: true, domain: null);

        foreach (var policy in new[] { Guid.NewGuid(), inactive, ontology })
        {
            var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), CallerOid, default);
            outcome.ErrorCode.Should().Be("assignment.rule_not_found");
        }

        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b I-A1 must-fire: one Exclude per (record, rule), firm or not: a second is 409 and writes nothing")]
    public async Task ExcludeRule_ASecondExcludeOfTheSameRule_ForAnyFirm_Is409Duplicate()
    {
        var w = new World();
        var policy = w.Rule();
        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", FirmId), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var everyFirm = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "y", null), CallerOid, default);

        everyFirm.StatusCode.Should().Be(409);
        everyFirm.ErrorCode.Should().Be("assignment.duplicate");
        w.Rows.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Ownership through the REAL resolver (EB-D-168: a hard-deny table)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "089b must-fire: an ordinary record's override row is owned by the restricted-content owner team, never a business unit's default team, with the caller as creator")]
    public async Task RealResolver_AnOrdinaryRecordsRow_IsOwnedByTheRestrictedContentTeam()
    {
        var w = new World();
        var directory = OwnershipDirectory.Standard()
            .WithRecord(Matter, MatterId, OwnershipDirectory.CustomerChildBu, owningTeam: OwnershipDirectory.CustomerChildTeam);

        var outcome = await w.Service(directory.Resolver())
            .AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), OwnershipDirectory.CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        var row = w.Rows.Single();
        ShouldBeOwnedByTheResolver(row, OwnershipDirectory.RestrictedContentTeam, OwnershipDirectory.CallerUserId);
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().NotBe(OwnershipDirectory.CustomerChildTeam);
    }

    [Fact(DisplayName = "089b must-fire: a Secure record's override row is owned by its Secure team")]
    public async Task RealResolver_ASecureRecordsRow_IsOwnedByItsSecureTeam()
    {
        var w = new World();
        var policy = w.Rule();
        var directory = OwnershipDirectory.Standard()
            .WithRecord(Matter, MatterId, OwnershipDirectory.SecureBu, isSecure: true, owningTeam: OwnershipDirectory.SecureNamedTeam);

        var outcome = await w.Service(directory.Resolver())
            .ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), OwnershipDirectory.CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        ShouldBeOwnedByTheResolver(w.Rows.Single(), OwnershipDirectory.SecureNamedTeam, OwnershipDirectory.CallerUserId);
    }
}
