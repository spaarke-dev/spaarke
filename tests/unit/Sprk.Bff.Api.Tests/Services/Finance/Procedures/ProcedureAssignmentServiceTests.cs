using System.Runtime.CompilerServices;
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
    private const string Token = "caller-token";

    private sealed class World
    {
        public readonly RuleSetServiceTests.Harness H = new();

        // Task 089c: the removal's close and count, as ProcedureServiceTests substitutes them. The Signal SELECTION is real (over the fake
        // store); the close marks what that selection returns resolved; the caller's rights probe answers from Readable.
        public readonly Mock<SignalClosureService> Closure;
        public readonly Mock<CallerRecordAccessProbe> Probe = new(
            MockBehavior.Strict, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);
        public readonly HashSet<Guid> Readable = new();
        public readonly List<Guid> SelectedPolicies = new();
        public readonly List<Guid> ClosedPolicies = new();
        public bool RightsFault;
        public Exception? CloseFault;

        public World()
        {
            H.Seed(new Entity("sprk_organization", FirmId), new Entity(Matter, MatterId));
            H.OwnerAnswer = RecordOwnerResolution.Owned(TeamId) with { CreatedByPerson = CallerUserId };

            var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)); // never called: the close is substituted
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
                    if (CloseFault is not null) throw CloseFault;
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
            new(H.Entities.Object, ownership ?? H.Ownership.Object, H.Service(), Closure.Object,
                new AffectedInvoiceCounter(H.Entities.Object, Probe.Object, NullLogger<AffectedInvoiceCounter>.Instance),
                NullLogger<ProcedureAssignmentService>.Instance);

        private Task<IReadOnlyList<OpenPolicySignal>> SelectReal(Guid policyId, bool withBillingSubject, CancellationToken ct) =>
            new SignalClosureService((SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)), H.Entities.Object,
                TimeProvider.System, NullLogger<SignalClosureService>.Instance).SelectOpenSignalsOfPolicyAsync(policyId, withBillingSubject, ct);

        public Guid Invoice(int statuscode = InvoiceStatusCodes.InReview)
        {
            var i = new Entity("sprk_invoice", Guid.NewGuid()) { ["statuscode"] = new OptionSetValue(statuscode), ["statecode"] = new OptionSetValue(0) };
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

        public IReadOnlyList<Entity> RowsOf(string table) => H.Rows.GetValueOrDefault(table) ?? new List<Entity>();

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

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089c: change rule (model 6.3)
    // ═══════════════════════════════════════════════════════════════════════════

    private const string EditedBody = """{"type":"DailyHoursCap","maxHoursPerDay":12}""";
    private static readonly Guid OtherFirmId = Guid.NewGuid();

    private static RuleChangeRequest Change(Guid policy, Guid? firm = null, string? body = EditedBody, string? headline = null,
        string? consequence = null, string? reason = "agreed with the client") =>
        new(policy, reason, firm, body, null, headline, consequence);

    private static (Guid Set, Guid Policy) RecordRuleOf(Guid basePolicy, Guid? firm = null)
    {
        var set = ProcedureAssignmentService.RecordRuleSetIdFor(Matter, MatterId, firm);
        return (set, ProcedureAssignmentService.RecordRulePolicyIdFor(set, basePolicy));
    }

    private static Guid CurrentVersionOf(World w, Guid policy) =>
        w.H.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id;

    [Fact(DisplayName = "089c: a change writes one Exclude row and one Published record rule in a record-scoped set; set, policy and version each owned by their own question (parents [core], [set], [policy])")]
    public async Task Change_WritesTheExcludeRowAndAPublishedRecordRule_EachOwnedByItsOwnQuestion()
    {
        var w = new World();
        var policy = w.Rule();
        var baseVersion = CurrentVersionOf(w, policy);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var (setId, ruleId) = RecordRuleOf(policy);

        // The Exclude row: the SAME derived id a plain Exclude of this rule has, so the two collide.
        var row = w.Rows.Should().ContainSingle().Subject;
        row.Id.Should().Be(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_excludedpolicy", policy)).And.Be(outcome.Value!.Override.Id);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModeExcluded);
        row.GetAttributeValue<EntityReference>("sprk_excludedpolicy").Id.Should().Be(policy);
        row.GetAttributeValue<string>("sprk_name").Should().Be("Rule BILL-A (Changed rule)");
        outcome.Value.Override.Kind.Should().Be("RuleChanged");

        // The record-scoped set.
        var set = w.H.Row("sprk_ruleset", setId);
        set.GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be(RecordRuleSet.ScopeRecord);
        set.GetAttributeValue<EntityReference>(Matter).Id.Should().Be(MatterId);
        set.Contains("sprk_firm").Should().BeFalse();
        set.Contains("sprk_procedure").Should().BeFalse();
        set.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
        set.GetAttributeValue<OptionSetValue>("sprk_domain").Value.Should().Be(RuleDomains.PolicyDomainBilling);

        // The record rule: on, its one version in force, Published, based on the rule's version in force.
        var rule = w.H.Row("sprk_policy", ruleId);
        var code = rule.GetAttributeValue<string>("sprk_policycode");
        code.Should().Be("BRR-" + ruleId.ToString("N")).And.HaveLength(36);
        code[4..].Should().MatchRegex("^[0-9a-f]{32}$");
        rule.GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        rule.GetAttributeValue<EntityReference>("sprk_ruleset").Id.Should().Be(setId);
        var versionId = RuleSetService.VersionIdFor(ruleId, 1);
        rule.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(versionId);
        var version = w.H.Row("sprk_policyversion", versionId);
        version.GetAttributeValue<string>("sprk_rulebody").Should().Be(EditedBody);
        version.GetAttributeValue<string>("sprk_shortheadline").Should().Be("Daily hours cap", "an unedited member is the base version's");
        version.GetAttributeValue<EntityReference>("sprk_basedonversion").Id.Should().Be(baseVersion);
        version.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Accepted);
        version.GetAttributeValue<OptionSetValue>("sprk_publishstate").Value.Should().Be(RuleSetService.PublishStatePublished);
        outcome.Value.RecordRule.Should().Be(new RecordRuleView(setId, ruleId, code, versionId, 1, baseVersion, "Record", null));

        // Each row's own question, with the caller as RequestedBy: set [core], policy [set], version [policy], Exclude row [core].
        var contexts = w.OwnerContexts;
        contexts.Select(c => c.ChildEntityLogicalName).Should().Equal("sprk_ruleset", "sprk_policy", Assignment, "sprk_policyversion");
        contexts[0].Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the firm is no ownership parent");
        contexts[1].Parents.Should().Equal(new[] { new RecordOwnershipParent("sprk_ruleset", setId) });
        contexts[2].Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the rule is a citation");
        contexts[3].Parents.Should().Equal(new[] { new RecordOwnershipParent("sprk_policy", ruleId) }, "the base version is no ownership parent");
        contexts.Should().OnlyContain(c => c.RequestedBy!.ObjectId == CallerOid && c.CallerObjectId == CallerOid);
        foreach (var (table, id) in new[] { ("sprk_ruleset", setId), ("sprk_policy", ruleId), ("sprk_policyversion", versionId) })
        {
            ShouldBeOwnedByTheResolver(w.H.Row(table, id), TeamId, CallerUserId);
        }

        // Stage 3 is one transaction: the Exclude row, the version and the conditional switch-on.
        w.H.Transactions.Should().HaveCount(3);
        w.H.Transactions[2].Select(r => r.GetType()).Should().Equal(typeof(CreateRequest), typeof(CreateRequest), typeof(UpdateRequest));
        ((UpdateRequest)w.H.Transactions[2][2]).ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);

        // The base rule is untouched.
        w.H.Row("sprk_policy", policy).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        CurrentVersionOf(w, policy).Should().Be(baseVersion);
    }

    [Fact(DisplayName = "089c: a change for one firm writes a Firm record set of that firm, and the Exclude row names it")]
    public async Task Change_ForAFirm_UsesAFirmRecordSet()
    {
        var w = new World();
        var policy = w.Rule();

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var (setId, _) = RecordRuleOf(policy, FirmId);
        var set = w.H.Row("sprk_ruleset", setId);
        set.GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be(RecordRuleSet.ScopeFirmRecord);
        set.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        w.Rows.Single().GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        outcome.Value!.RecordRule.Scope.Should().Be("FirmRecord");
    }

    [Fact(DisplayName = "089c: a second change of the record reuses its set")]
    public async Task Change_ASecondChange_ReusesTheRecordsSet()
    {
        var w = new World();
        var first = w.Rule("BILL-A");
        var second = w.Rule("BILL-B");

        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(first), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        var again = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(second), CallerOid, default);

        again.Kind.Should().Be(RuleSetOutcomeKind.Done, again.Detail);
        var recordSets = w.RowsOf("sprk_ruleset").Where(RecordRuleSet.IsRecordScoped).ToList();
        recordSets.Should().ContainSingle().Which.Id.Should().Be(again.Value!.RecordRule.RuleSetId);
        w.RowsOf("sprk_policy").Count(p => p.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == recordSets[0].Id).Should().Be(2);
        w.Rows.Should().HaveCount(2);
    }

    [Fact(DisplayName = "089c must-fire: when the last write fails, neither the Exclude row nor a rule in force is left, and a retry reuses the set and policy")]
    public async Task Change_WhenTheLastWriteFails_LeavesNeither_AndARetryReusesTheRows()
    {
        var w = new World();
        var policy = w.Rule();
        var (setId, ruleId) = RecordRuleOf(policy);
        w.H.FailTransactionRequest = r => r is CreateRequest { Target.LogicalName: "sprk_policyversion" };

        var failed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);

        failed.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        failed.StatusCode.Should().Be(500);
        failed.ErrorCode.Should().Be("assignment.write_failed");
        w.Rows.Should().BeEmpty("the Exclude row rolled back with the version");
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse("no record rule is in force");
        w.H.Row("sprk_policy", ruleId).Contains("sprk_currentversion").Should().BeFalse();
        w.RowsOf("sprk_policyversion").Should().NotContain(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == ruleId);
        w.H.Row("sprk_policy", policy).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue("the base rule still checks the record");

        w.H.FailTransactionRequest = null;
        var retried = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done, retried.Detail);
        w.RowsOf("sprk_ruleset").Count(s => s.Id == setId).Should().Be(1);
        w.RowsOf("sprk_policy").Count(p => p.Id == ruleId).Should().Be(1);
        retried.Value!.RecordRule.VersionNumber.Should().Be(1);
    }

    [Fact(DisplayName = "089c must-fire: a rule of one firm's set changed for another firm, or for every firm, is refused 400 and writes nothing")]
    public async Task Change_AFirmNamedRule_ForAnotherFirm_Is400()
    {
        var w = new World();
        w.H.Seed(new Entity("sprk_organization", OtherFirmId));
        var set = w.H.RuleSet(RuleSetService.RuleSetPublished, scope: (int)RuleSetScope.Firm, firm: FirmId);
        var policy = w.H.Rule(set, "BILL-F", ProposalState.Accepted, current: true).Policy;

        var other = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: OtherFirmId), CallerOid, default);
        var all = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);

        foreach (var refused in new[] { other, all })
        {
            refused.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
            refused.ErrorCode.Should().Be("assignment.invalid");
            refused.Errors.Should().ContainSingle().Which.Field.Should().Be("firmId");
        }

        w.H.Transactions.Should().BeEmpty();
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: FirmId), CallerOid, default))
            .Kind.Should().Be(RuleSetOutcomeKind.Done, "its own firm is accepted (must not fire)");
    }

    [Fact(DisplayName = "089c must-fire: an invalid edit is refused by PolicyVersionValidator and writes nothing")]
    public async Task Change_AnInvalidEdit_IsRefusedByTheValidator()
    {
        var w = new World();
        var policy = w.Rule();

        var badBody = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, body: """{"type":"DailyHoursCap"}"""), CallerOid, default);
        var badTemplate = await w.Service().ChangeRuleAsync(Matter, MatterId,
            new RuleChangeRequest(policy, "x", null, null, "{{line.nope}}", null, null), CallerOid, default);

        badBody.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        badBody.Errors.Should().Contain(e => e.Field == "ruleBody");
        badTemplate.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        badTemplate.Errors.Should().Contain(e => e.Field == "messageTemplate");
        w.H.Transactions.Should().BeEmpty();
        w.H.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Change_WithoutAReasonOrAnEditOrAValidConsequence_Is400()
    {
        var w = new World();
        var policy = w.Rule();

        var noReason = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, reason: " "), CallerOid, default);
        var noEdit = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, body: null), CallerOid, default);
        var badConsequence = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, consequence: "Maybe"), CallerOid, default);

        noReason.Errors.Should().ContainSingle().Which.Field.Should().Be("reason");
        noEdit.Errors.Should().ContainSingle().Which.Field.Should().Be("edit");
        badConsequence.Errors.Should().ContainSingle().Which.Field.Should().Be("consequence");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c must-fire: a plain Exclude and a change of the same rule are one row per (record, rule): either second one is 409")]
    public async Task Change_AndExclude_OfTheSameRule_Collide()
    {
        var w = new World();
        var excluded = w.Rule("BILL-A");
        var changed = w.Rule("BILL-B");

        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(excluded, "x", null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(changed), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var changeAfterExclude = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(excluded), CallerOid, default);
        var excludeAfterChange = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(changed, "x", null), CallerOid, default);

        changeAfterExclude.ErrorCode.Should().Be("assignment.duplicate");
        excludeAfterChange.ErrorCode.Should().Be("assignment.duplicate");
        w.Rows.Should().HaveCount(2);
    }

    [Fact(DisplayName = "089c: a record rule, the system rule and a rule with no version in force cannot be changed")]
    public async Task Change_RefusesARecordRule_TheSystemRule_AndARuleNotInForce()
    {
        var w = new World();
        var recordRule = w.Rule("BILL-R", withMatterLookup: true);
        var system = w.RuleWithoutSet(BillingRuleCatalogue.SystemPolicyCode, "DailyHoursCap");
        var set = w.H.RuleSet(RuleSetService.RuleSetPublished);
        var drafted = w.H.Rule(set, "BILL-D", ProposalState.Proposed, current: false).Policy;

        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(recordRule), CallerOid, default)).ErrorCode.Should().Be("assignment.rule_not_found");
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(system), CallerOid, default)).ErrorCode.Should().Be("assignment.system_rule");
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(drafted), CallerOid, default)).ErrorCode.Should().Be("assignment.rule_not_in_force");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c must-fire: a row at the record rule's id that is not this record's rule of this base rule is refused 409, never reused")]
    public async Task Change_WhenTheDerivedPolicyIsAnotherRulesRow_Is409()
    {
        var w = new World();
        var policy = w.Rule("BILL-A");
        var other = w.Rule("BILL-B");
        var (setId, ruleId) = RecordRuleOf(policy);

        // A row already at the record rule's id, in the right set, whose version is based on ANOTHER rule's version.
        w.H.Seed(new Entity("sprk_ruleset", setId)
        {
            ["sprk_scope"] = new OptionSetValue(RecordRuleSet.ScopeRecord),
            [Matter] = new EntityReference(Matter, MatterId),
            ["sprk_domain"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
        });
        w.H.Seed(new Entity("sprk_policy", ruleId)
        {
            ["sprk_ruleset"] = new EntityReference("sprk_ruleset", setId),
            ["sprk_policycode"] = ProcedureAssignmentService.RecordRuleCode(ruleId),
            ["sprk_enabled"] = false,
            ["sprk_domain"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
        });
        w.H.Seed(new Entity("sprk_policyversion", Guid.NewGuid())
        {
            ["sprk_policy"] = new EntityReference("sprk_policy", ruleId),
            ["sprk_versionnumber"] = 1,
            ["sprk_basedonversion"] = new EntityReference("sprk_policyversion", CurrentVersionOf(w, other)),
        });

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.record_rule_conflict");
        w.Rows.Should().BeEmpty();
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
    }

    [Fact(DisplayName = "089c: a read fault before the write is a stable 503 assignment.read_failed, and nothing is written")]
    public async Task Change_WhenAReadFaults_Is503()
    {
        var w = new World();
        var policy = w.Rule();
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == "sprk_ruleset" && q.ColumnSet.Columns.Contains("sprk_procedure")) throw new HttpRequestException("simulated fault");
        };

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);

        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be("assignment.read_failed");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c must-fire: the legacy rule-set service answers a record-scoped set as not found on its read, list, edit and publish")]
    public async Task RuleSetService_RefusesARecordScopedSet()
    {
        var w = new World();
        var policy = w.Rule();
        var changed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);
        var (setId, ruleId) = (changed.Value!.RecordRule.RuleSetId, changed.Value.RecordRule.PolicyId);
        var ruleSets = w.H.Service();

        (await ruleSets.GetAsync(setId, default)).StatusCode.Should().Be(404);
        (await ruleSets.ListAsync(default)).Value!.Select(s => s.Id).Should().NotContain(setId).And.NotBeEmpty();
        (await ruleSets.EditAsync(setId, ruleId, new ProposalEdit(null, null, null, "x", null, null, null, CallerOid), default)).StatusCode.Should().Be(404);
        (await ruleSets.PublishAsync(setId, new DateOnly(2027, 1, 1), default)).StatusCode.Should().Be(404);
        (await ruleSets.SetVisibilityAsync(setId, false, default)).StatusCode.Should().Be(404);
        w.H.Row("sprk_policyversion", changed.Value.RecordRule.VersionId).GetAttributeValue<string>("sprk_shortheadline").Should().Be("Daily hours cap");
    }

    [Theory(DisplayName = "089c (089a K1): a rule set has one home, checked before every rule-set create")]
    [InlineData(null, false, false, null, true)]                                    // legacy
    [InlineData(null, false, true, null, true)]                                     // a Procedure's set
    [InlineData(RecordRuleSet.ScopeRecord, false, false, "sprk_matter", true)]      // record
    [InlineData(RecordRuleSet.ScopeFirmRecord, true, false, "sprk_project", true)]  // firm record
    [InlineData(RecordRuleSet.ScopeRecord, false, true, "sprk_matter", false)]      // a Procedure and a record
    [InlineData(null, false, false, "sprk_matter", false)]                          // a core lookup without the scope
    [InlineData(RecordRuleSet.ScopeRecord, false, false, null, false)]              // the scope without a core lookup
    [InlineData(RecordRuleSet.ScopeRecord, true, false, "sprk_matter", false)]      // Record with a firm
    [InlineData(RecordRuleSet.ScopeFirmRecord, false, false, "sprk_matter", false)] // Firm record without one
    public void RecordRuleSet_RequireOneHome(int? scope, bool firm, bool procedure, string? core, bool accepted)
    {
        var set = new Entity("sprk_ruleset", Guid.NewGuid());
        if (scope is { } s) set["sprk_scope"] = new OptionSetValue(s);
        if (firm) set["sprk_firm"] = new EntityReference("sprk_organization", FirmId);
        if (procedure) set["sprk_procedure"] = new EntityReference("sprk_procedure", Guid.NewGuid());
        if (core is not null) set[core] = new EntityReference(core, Guid.NewGuid());

        var act = () => RecordRuleSet.RequireOneHome(set);

        if (accepted) act.Should().NotThrow();
        else act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordRuleSet_RequireOneHome_RefusesTwoCoreLookups()
    {
        var set = new Entity("sprk_ruleset", Guid.NewGuid())
        {
            ["sprk_scope"] = new OptionSetValue(RecordRuleSet.ScopeRecord),
            ["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
            ["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid()),
        };

        FluentActions.Invoking(() => RecordRuleSet.RequireOneHome(set)).Should().Throw<InvalidOperationException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089c: remove, with preview
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>A changed rule with open findings of its record rule on three In Review invoices, two of which the caller can Read.</summary>
    private static async Task<(World W, Guid Override, Guid RecordRule, Guid BasePolicy, Guid BaseSignal)> ChangedWithFindingsAsync()
    {
        var w = new World();
        var policy = w.Rule();
        var changed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);
        var ruleId = changed.Value!.RecordRule.PolicyId;
        var (a, b, hidden) = (w.Invoice(), w.Invoice(), w.Invoice());
        w.Readable.UnionWith(new[] { a, b });
        foreach (var invoice in new[] { a, b, hidden })
        {
            w.Signal(ruleId, invoice);
        }

        var baseSignal = w.Signal(policy, a);
        return (w, changed.Value.Override.Id, ruleId, policy, baseSignal);
    }

    [Fact(DisplayName = "089c: the removal preview of a changed rule shows the warning and the caller-scoped count (3 affected, 2 readable), and writes nothing")]
    public async Task Preview_AChangedRule_ShowsTheWarningAndTheCallerScopedCount()
    {
        var (w, overrideId, ruleId, _, _) = await ChangedWithFindingsAsync();
        var writes = w.H.Writes.Count;
        var transactions = w.H.Transactions.Count;

        var outcome = await w.Service().PreviewRemovalAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Should().Be(new OverrideRemovalPreview(overrideId, "RuleChanged", true,
            "Removing this override will close the rule it changed and remove pending decisions on active invoices.",
            "2 open invoices you can see are affected.", 2));
        w.SelectedPolicies.Should().Equal(new[] { ruleId }, "the preview selects through the close's own method, for the record rule only");
        w.H.Writes.Should().HaveCount(writes);
        w.H.Transactions.Should().HaveCount(transactions);
        w.ClosedPolicies.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c must-fire: when the rights read faults, the preview shows the warning with no count")]
    public async Task Preview_WhenTheRightsReadFaults_ShowsNoCount()
    {
        var (w, overrideId, _, _, _) = await ChangedWithFindingsAsync();
        w.RightsFault = true;

        var outcome = await w.Service().PreviewRemovalAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Value!.Warning.Should().NotBeNullOrEmpty();
        outcome.Value.AffectedText.Should().BeNull();
        outcome.Value.VisibleOpenInvoices.Should().BeNull();
    }

    [Fact(DisplayName = "089c: a plain Exclude's or a Procedure row's preview has no warning and no count")]
    public async Task Preview_APlainRow_HasNoWarning()
    {
        var w = new World();
        var excluded = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(w.Rule(), "x", null), CallerOid, default);
        var assigned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), CallerOid, default);

        var plain = await w.Service().PreviewRemovalAsync(Matter, MatterId, excluded.Value!.Id, Token, default);
        var procedure = await w.Service().PreviewRemovalAsync(Matter, MatterId, assigned.Value!.Id, Token, default);

        plain.Value.Should().Be(new OverrideRemovalPreview(excluded.Value.Id, "RuleExcluded", false, null, null, null));
        procedure.Value.Should().Be(new OverrideRemovalPreview(assigned.Value.Id, "Procedure", false, null, null, null));
        w.SelectedPolicies.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c: a plain removal deletes the row and closes nothing")]
    public async Task Remove_APlainExclude_DeletesTheRow_AndClosesNothing()
    {
        var w = new World();
        var excluded = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(w.Rule(), "x", null), CallerOid, default);

        var outcome = await w.Service().RemoveAsync(Matter, MatterId, excluded.Value!.Id, Token, default);

        outcome.Value.Should().Be(new OverrideRemoved(excluded.Value.Id, "RuleExcluded", false, true, null, null, null));
        w.Rows.Should().BeEmpty("deleted, not deactivated: I-A1's duplicate check ignores state");
        w.H.Transactions.Last().Should().ContainSingle().Which.Should().BeOfType<DeleteRequest>();
        w.ClosedPolicies.Should().BeEmpty();
        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(excluded.Value.PolicyId, "x", null), CallerOid, default))
            .Kind.Should().Be(RuleSetOutcomeKind.Done, "the subject can be overridden again");
    }

    [Fact(DisplayName = "089c: a changed rule's removal deletes the row, switches the record rule Off in the same transaction, and closes its open findings through 081's shared close")]
    public async Task Remove_AChangedRule_SwitchesTheRecordRuleOff_AndClosesItsFindings()
    {
        var (w, overrideId, ruleId, basePolicy, baseSignal) = await ChangedWithFindingsAsync();

        var outcome = await w.Service().RemoveAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Value.Should().Be(new OverrideRemoved(overrideId, "RuleChanged", true, true, "2 open invoices you can see are affected.", 2, null));
        w.Rows.Should().BeEmpty();
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        w.H.Transactions.Last().Select(r => r.GetType()).Should().Equal(typeof(DeleteRequest), typeof(UpdateRequest));
        w.ClosedPolicies.Should().Equal(ruleId);
        w.RowsOf("sprk_signal").Where(s => s.GetAttributeValue<EntityReference>("sprk_policy").Id == ruleId)
            .Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusResolved);
        w.H.Row("sprk_signal", baseSignal).GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value
            .Should().Be(SignalWriter.SignalStatusOpen, "the base rule's findings are not this removal's");
        w.H.Row("sprk_policy", basePolicy).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
    }

    [Fact(DisplayName = "089c: when the close fails after the commit, the removal is still done, the rule is Off, and the result says the close is incomplete")]
    public async Task Remove_WhenTheCloseFails_IsStillDone()
    {
        var (w, overrideId, ruleId, _, _) = await ChangedWithFindingsAsync();
        w.CloseFault = new InvalidOperationException("simulated close fault");

        var outcome = await w.Service().RemoveAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.RuleSwitchedOff.Should().BeTrue();
        outcome.Value.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().NotBeNullOrEmpty();
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
    }

    [Fact(DisplayName = "089c: a removed record rule needed again reuses its policy row with a new version, never a second row")]
    public async Task ChangeAgain_AfterARemoval_ReusesThePolicyRow()
    {
        var w = new World();
        var policy = w.Rule();
        var first = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);
        (await w.Service().RemoveAsync(Matter, MatterId, first.Value!.Override.Id, Token, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var again = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, body: CapBodyOf(14)), CallerOid, default);

        again.Kind.Should().Be(RuleSetOutcomeKind.Done, again.Detail);
        var ruleId = first.Value.RecordRule.PolicyId;
        again.Value!.RecordRule.PolicyId.Should().Be(ruleId);
        again.Value.RecordRule.VersionNumber.Should().Be(2);
        w.RowsOf("sprk_policy").Count(p => p.Id == ruleId).Should().Be(1);
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        CurrentVersionOf(w, ruleId).Should().Be(RuleSetService.VersionIdFor(ruleId, 2));
    }

    private static string CapBodyOf(int hours) => $$"""{"type":"DailyHoursCap","maxHoursPerDay":{{hours}}}""";

    [Fact(DisplayName = "089c: an unknown override, or another record's, is 404 assignment.override_not_found and nothing is written")]
    public async Task Remove_AnUnknownOrAnotherRecordsOverride_Is404()
    {
        var w = new World();
        var otherMatter = Guid.NewGuid();
        w.H.Seed(new Entity(Matter, otherMatter));
        var excluded = await w.Service().ExcludeRuleAsync(Matter, otherMatter, new(w.Rule(), "x", null), CallerOid, default);
        var transactions = w.H.Transactions.Count;

        var unknown = await w.Service().RemoveAsync(Matter, MatterId, Guid.NewGuid(), Token, default);
        var others = await w.Service().RemoveAsync(Matter, MatterId, excluded.Value!.Id, Token, default);
        var preview = await w.Service().PreviewRemovalAsync(Matter, MatterId, excluded.Value.Id, Token, default);

        foreach (var refused in new[] { unknown.ErrorCode, others.ErrorCode, preview.ErrorCode })
        {
            refused.Should().Be("assignment.override_not_found");
        }

        w.H.Transactions.Should().HaveCount(transactions);
        w.Rows.Should().ContainSingle();
        (await w.Service().SubjectOfOverrideAsync(Matter, MatterId, excluded.Value.Id, default)).Should().BeNull();
    }

    [Fact(DisplayName = "089c: the subject of an override is the Procedure or the rule it names")]
    public async Task SubjectOfOverride_IsTheProcedureOrTheRule()
    {
        var w = new World();
        var procedure = w.Procedure();
        var policy = w.Rule();
        var assigned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);
        var changed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, default);

        (await w.Service().SubjectOfOverrideAsync(Matter, MatterId, assigned.Value!.Id, default)).Should().Be(new OverrideSubject("sprk_procedures", procedure));
        (await w.Service().SubjectOfOverrideAsync(Matter, MatterId, changed.Value!.Override.Id, default)).Should().Be(new OverrideSubject("sprk_policies", policy));
    }

    [Fact(DisplayName = "089c: a read fault before a removal is a stable 503 and nothing is written")]
    public async Task Remove_WhenAReadFaults_Is503()
    {
        var w = new World();
        var excluded = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(w.Rule(), "x", null), CallerOid, default);
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == Assignment) throw new HttpRequestException("simulated fault");
        };

        var removed = await w.Service().RemoveAsync(Matter, MatterId, excluded.Value!.Id, Token, default);
        var previewed = await w.Service().PreviewRemovalAsync(Matter, MatterId, excluded.Value.Id, Token, default);

        removed.StatusCode.Should().Be(503);
        previewed.StatusCode.Should().Be(503);
        w.Rows.Should().ContainSingle();
    }
}
