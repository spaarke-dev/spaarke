using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Finance;
using Sprk.Bff.Api.Tests.Services.Finance.Procedures;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// ISS-038 (ebilling-r1): per-record visibility uses both rule sets' flags (see uac-r2 thread). A record-scoped rule set holds every changed rule
/// of one record (and firm) and takes its "visible to firms" flag from the first rule changed there, so the clause of a record rule is shown to a
/// firm only when the record set AND the base rule's own rule set are visible (<see cref="RuleSetClauseVisibility.VisibleToFirms"/>). The base is
/// the one the invoice's snapshot slot names.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, regression): the sequence tests drive the REAL <see cref="ProcedureAssignmentService.ChangeRuleAsync"/> and the REAL
/// <see cref="RuleSetService.GetFirmClauseAsync"/> (and, for the projection, the REAL <see cref="FirmFindingsProjection"/>) over the shared
/// <see cref="RuleSetServiceTests.Harness"/> fake store, which evaluates the real queries. Fail-before: with the record set's flag deciding alone,
/// the must-fire tests show the second rule's clause.
/// </remarks>
public sealed class Iss038_RecordRuleFirmClauseBaseSetTests
{
    private const string Matter = "sprk_matter";
    private const string CallerToken = "caller-token"; // the token ProcedureAssignmentServiceTests.World's rights probe answers
    private const string RecordLabel = "Billing instructions for this record";

    private const string VisibleQuote = "Travel is billed at half the hourly rate.";
    private const string VisibleSection = "Section 7.1";
    private const string InternalQuote = "INTERNAL-ONLY reviewer threshold text.";
    private const string InternalSection = "INTERNAL-ONLY Section 99";

    private const string EditedBody = """{"type":"DailyHoursCap","maxHoursPerDay":12}""";

    /// <summary>The submission id of <see cref="FirmFindingsProjectionTests"/>'s world: its invoice id derives from it.</summary>
    private static readonly Guid ProjectionSubmissionId = Guid.Parse("20000000-0000-4000-8000-0000000000b1");

    private sealed record ChangedRules(
        ProcedureAssignmentServiceTests.World World, Guid Firm, RecordRuleView Visible, RecordRuleView Internal);

    /// <summary>
    /// The sequence: on one record, for one firm, a rule whose base set is visible is changed first (so the record set is created visible), then a
    /// rule whose base set is hidden is changed (its record rule lands in the same record set). Both through the real change.
    /// </summary>
    private static async Task<ChangedRules> ChangeVisibleThenInternalAsync()
    {
        var w = new ProcedureAssignmentServiceTests.World();
        var matter = Guid.NewGuid();
        var firm = Guid.NewGuid();
        w.H.Seed(new Entity(Matter, matter), new Entity("sprk_organization", firm));

        var visibleBase = w.Rule("BILL-VISIBLE");
        SetClause(w.H, visibleBase, VisibleQuote, VisibleSection);
        var internalBase = w.Rule("BILL-INTERNAL");
        SetClause(w.H, internalBase, InternalQuote, InternalSection);
        w.H.Row("sprk_ruleset", SetOf(w.H, internalBase))["sprk_visibletofirms"] = false;

        var service = w.Service();
        var first = await service.ChangeRuleAsync(Matter, matter, Change(visibleBase, firm), Guid.NewGuid(), CallerToken, default);
        first.Kind.Should().Be(RuleSetOutcomeKind.Done, first.Detail);
        var second = await service.ChangeRuleAsync(Matter, matter, Change(internalBase, firm), Guid.NewGuid(), CallerToken, default);
        second.Kind.Should().Be(RuleSetOutcomeKind.Done, second.Detail);

        // The state this regression is about: one record set, created visible by the first rule, now holding both record rules.
        second.Value!.RecordRule.RuleSetId.Should().Be(first.Value!.RecordRule.RuleSetId);
        w.H.Row("sprk_ruleset", first.Value.RecordRule.RuleSetId).GetAttributeValue<bool>("sprk_visibletofirms").Should().BeTrue();
        w.H.Row("sprk_policyversion", second.Value.RecordRule.VersionId).GetAttributeValue<string>("sprk_clausereference")
            .Should().Be(InternalQuote, "the record rule copies its base version's clause");

        return new ChangedRules(w, firm, first.Value.RecordRule, second.Value.RecordRule);
    }

    private static RuleChangeRequest Change(Guid policy, Guid firm) => new(policy, "agreed with the client", firm, EditedBody, null, null, null);

    private static Guid SetOf(RuleSetServiceTests.Harness h, Guid policy) =>
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_ruleset").Id;

    private static void SetClause(RuleSetServiceTests.Harness h, Guid policy, string quote, string section)
    {
        var version = h.Row("sprk_policyversion", h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id);
        version["sprk_clausereference"] = quote;
        version["sprk_clauselocation"] = section;
    }

    private static string FindingsJudgedOn(ChangedRules c) => RuleSetServiceTests.FindingsWithRecordRules(
        [(c.Visible.VersionId, c.Visible.BasedOnVersionId), (c.Internal.VersionId, c.Internal.BasedOnVersionId)]);

    private static void ShouldCarryNothingOf(FirmClauseText shown)
    {
        shown.Should().Be(FirmClauseText.None);
        var text = JsonSerializer.Serialize(shown);
        text.Should().NotContain(InternalQuote).And.NotContain(InternalSection).And.NotContain(RecordLabel);
    }

    // ── Must-fire: the sequence ──

    [Fact(DisplayName = "ISS-038 must-fire: a rule changed second on the same record and firm shows no clause when its base set is hidden; the first still shows")]
    public async Task GetFirmClause_SecondRecordRuleWhoseBaseSetIsHidden_ShowsNothing()
    {
        var c = await ChangeVisibleThenInternalAsync();
        var invoice = c.World.H.Invoice(FindingsJudgedOn(c), firm: c.Firm);
        var rules = c.World.H.Service();

        var internalShown = await rules.GetFirmClauseAsync(c.Internal.VersionId, invoice, default);
        var visibleShown = await rules.GetFirmClauseAsync(c.Visible.VersionId, invoice, default);

        ShouldCarryNothingOf(internalShown);
        visibleShown.Should().Be(new FirmClauseText(VisibleQuote, VisibleSection, RecordLabel), "the control: the same set, a visible base");
    }

    [Fact(DisplayName = "ISS-038 must-fire: the firm outcome of an invoice judged on both record rules cites only the one whose base set is visible")]
    public async Task FirmFindingsProjection_InvoiceJudgedOnBothRecordRules_CitesOnlyTheVisibleBase()
    {
        var c = await ChangeVisibleThenInternalAsync();
        var world = new FirmFindingsProjectionTests.World();
        var invoiceId = world.Invoices.Single().Id;
        invoiceId.Should().Be(InvoiceSubmissionPersister.InvoiceIdFor(ProjectionSubmissionId), "the clause read finds the invoice's own submission");

        // The clause decision's store: the invoice (of the firm) and its own submission, judged on both record rules.
        c.World.H.Seed(new Entity("sprk_invoice", invoiceId) { ["sprk_vendororg"] = new EntityReference("sprk_organization", c.Firm) });
        c.World.H.AddSubmission(invoiceId, ProjectionSubmissionId, FindingsJudgedOn(c));
        var rules = c.World.H.Service();

        // The projection's store: the record rules' versions, policies and record set as written by the change.
        foreach (var record in new[] { c.Visible, c.Internal })
        {
            world.Versions.Add(c.World.H.Row("sprk_policyversion", record.VersionId));
            world.Policies.Add(c.World.H.Row("sprk_policy", record.PolicyId));
        }

        world.RuleSets.Add(c.World.H.Row("sprk_ruleset", c.Visible.RuleSetId));
        world.Clause = version => rules.GetFirmClauseAsync(version, invoiceId, default).GetAwaiter().GetResult();
        world.AddReducedWithCitation(1, "Rate reduced.", c.Visible.VersionId, "Firm reason one.");
        world.AddReducedWithCitation(2, "Hours reduced.", c.Internal.VersionId, "Firm reason two.");

        var outcome = await world.Projection().GetForInvoiceAsync(invoiceId, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        outcome.Items[0].Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FirmOutcomeCitation(c.World.H.Row("sprk_policy", c.Visible.PolicyId).GetAttributeValue<string>("sprk_name"), RecordLabel, 1, VisibleSection, VisibleQuote),
            "the control: the visible base's record rule is cited");
        outcome.Items[1].Citations.Should().BeEmpty("the hidden base's record rule is not cited at all");
        var text = JsonSerializer.Serialize(outcome);
        text.Should().NotContain(InternalQuote).And.NotContain(InternalSection);
        world.ClauseCalls.Should().BeEquivalentTo(new[] { c.Visible.VersionId, c.Internal.VersionId });
    }

    // ── The other cases, over seeded rows ──

    private static (RuleSetServiceTests.Harness H, Guid RecordSet) RecordSetWorld(bool recordSetVisible = true)
    {
        var h = new RuleSetServiceTests.Harness();
        var recordSet = h.RuleSet(RuleSetService.RuleSetPublished, visible: recordSetVisible, scope: RecordRuleSet.ScopeRecord);
        h.Row("sprk_ruleset", recordSet)[Matter] = new EntityReference(Matter, Guid.NewGuid());
        return (h, recordSet);
    }

    private static Guid BaseVersion(RuleSetServiceTests.Harness h, bool visible) =>
        h.Rule(h.RuleSet(RuleSetService.RuleSetPublished, visible: visible), "BASE-" + Guid.NewGuid().ToString("N")[..6], ProposalState.Accepted, current: true).Version;

    private static Guid RecordVersion(RuleSetServiceTests.Harness h, Guid recordSet) =>
        h.Rule(recordSet, "BRR-" + Guid.NewGuid().ToString("N")[..6], ProposalState.Accepted, current: true, quote: InternalQuote, location: InternalSection).Version;

    [Fact(DisplayName = "ISS-038: a record rule whose base version, policy or rule set cannot be read, or whose slot is absent, shows nothing")]
    public async Task GetFirmClause_RecordRuleWithAMissingBase_ShowsNothing()
    {
        var (h, recordSet) = RecordSetWorld();
        var gone = RecordVersion(h, recordSet);
        var policyGone = RecordVersion(h, recordSet);
        var setGone = RecordVersion(h, recordSet);
        var noSlot = RecordVersion(h, recordSet);
        var control = RecordVersion(h, recordSet);

        var basePolicyGone = BaseVersion(h, visible: true);
        h.Rows["sprk_policy"].RemoveAll(p => p.Id == h.Row("sprk_policyversion", basePolicyGone).GetAttributeValue<EntityReference>("sprk_policy").Id);
        var baseSetGone = BaseVersion(h, visible: true);
        var goneSet = SetOfVersion(h, baseSetGone);
        h.Rows["sprk_ruleset"].RemoveAll(s => s.Id == goneSet);
        var visibleBase = BaseVersion(h, visible: true);

        var invoice = h.Invoice(RuleSetServiceTests.FindingsWithRecordRules(
            [(gone, Guid.NewGuid()), (policyGone, basePolicyGone), (setGone, baseSetGone), (control, visibleBase)], noSlot));
        var rules = h.Service();

        (await rules.GetFirmClauseAsync(control, invoice, default)).Quote.Should().Be(InternalQuote, "the control: a readable visible base shows");
        ShouldCarryNothingOf(await rules.GetFirmClauseAsync(gone, invoice, default));
        ShouldCarryNothingOf(await rules.GetFirmClauseAsync(policyGone, invoice, default));
        ShouldCarryNothingOf(await rules.GetFirmClauseAsync(setGone, invoice, default));
        ShouldCarryNothingOf(await rules.GetFirmClauseAsync(noSlot, invoice, default));
    }

    [Fact(DisplayName = "ISS-038: both sets visible shows the clause with the neutral label; the record set hidden shows nothing")]
    public async Task GetFirmClause_RecordRule_ShowsOnlyWhenBothSetsAreVisible()
    {
        var (h, visibleSet) = RecordSetWorld(recordSetVisible: true);
        var both = RecordVersion(h, visibleSet);
        var hiddenSet = h.RuleSet(RuleSetService.RuleSetPublished, visible: false, scope: RecordRuleSet.ScopeRecord);
        h.Row("sprk_ruleset", hiddenSet)[Matter] = new EntityReference(Matter, Guid.NewGuid());
        var recordHidden = RecordVersion(h, hiddenSet);
        var visibleBase = BaseVersion(h, visible: true);

        var invoice = h.Invoice(RuleSetServiceTests.FindingsWithRecordRules([(both, visibleBase), (recordHidden, visibleBase)]));
        var rules = h.Service();

        (await rules.GetFirmClauseAsync(both, invoice, default)).Should().Be(new FirmClauseText(InternalQuote, InternalSection, RecordLabel));
        ShouldCarryNothingOf(await rules.GetFirmClauseAsync(recordHidden, invoice, default));
    }

    [Fact(DisplayName = "ISS-038: a set that is not record-scoped is decided by its own flag alone, as before, and reads no base")]
    public async Task GetFirmClause_NonRecordSet_IsDecidedByItsOwnFlagAlone()
    {
        var h = new RuleSetServiceTests.Harness();
        var on = h.Rule(h.RuleSet(RuleSetService.RuleSetPublished, visible: true), "BILL-ON", ProposalState.Accepted, current: true,
            quote: VisibleQuote, location: VisibleSection).Version;
        var off = h.Rule(h.RuleSet(RuleSetService.RuleSetPublished, visible: false), "BILL-OFF", ProposalState.Accepted, current: true).Version;
        var invoice = h.Invoice(RuleSetServiceTests.FindingsWithRecordRules([], on, off));
        var rules = h.Service();

        h.Queries.Clear();
        (await rules.GetFirmClauseAsync(on, invoice, default)).Should().Be(new FirmClauseText(VisibleQuote, VisibleSection, null));
        h.Queries.Count(q => q.EntityName == "sprk_policyversion").Should().Be(1, "only the version itself is read: no base lookup");
        (await rules.GetFirmClauseAsync(off, invoice, default)).Should().Be(FirmClauseText.None);
    }

    // ── The base set's Procedure type ──

    private const string BaseSetName = "INTERNAL-BASE-SET-NAME";
    private const string BaseProcedureName = "INTERNAL-BASE-PROCEDURE-NAME";

    /// <summary>A visible base rule set that belongs to a Procedure of <paramref name="procedureType"/> (no type column when null); its version.</summary>
    private static Guid BaseVersionOfProcedure(RuleSetServiceTests.Harness h, int? procedureType)
    {
        var procedure = h.Procedure(BaseProcedureName);
        if (procedureType is { } type)
        {
            h.Row("sprk_procedure", procedure)["sprk_proceduretype"] = new OptionSetValue(type);
        }

        var set = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, procedure: procedure);
        h.Row("sprk_ruleset", set)["sprk_name"] = BaseSetName;
        return h.Rule(set, "BASE-" + Guid.NewGuid().ToString("N")[..6], ProposalState.Accepted, current: true).Version;
    }

    private static void ShouldCarryNothingOfTheBase(string text)
    {
        text.Should().NotContain(InternalQuote).And.NotContain(InternalSection).And.NotContain(RecordLabel)
            .And.NotContain(BaseSetName).And.NotContain(BaseProcedureName);
    }

    [Theory(DisplayName = "ISS-038 must-fire: a record rule whose base rule set belongs to an Internal Standard Procedure, or to one whose type is not readable, shows no clause")]
    [InlineData((int)ProcedureType.InternalStandard, false)]
    [InlineData(null, false)]
    [InlineData((int)ProcedureType.Guideline, true)]
    public async Task GetFirmClause_RecordRuleWhoseBaseBelongsToAProcedure_NeedsAReadableTypeThatIsNotInternalStandard(int? baseProcedureType, bool shown)
    {
        var (h, recordSet) = RecordSetWorld();
        var record = RecordVersion(h, recordSet);
        var baseVersion = BaseVersionOfProcedure(h, baseProcedureType);
        var invoice = h.Invoice(RuleSetServiceTests.FindingsWithRecordRules([(record, baseVersion)]));

        var clause = await h.Service().GetFirmClauseAsync(record, invoice, default);

        if (shown)
        {
            clause.Should().Be(new FirmClauseText(InternalQuote, InternalSection, RecordLabel), "the control: a readable base type that is not Internal Standard");
        }
        else
        {
            ShouldCarryNothingOf(clause);
        }
    }

    [Fact(DisplayName = "ISS-038 must-fire: a record rule whose base rule set belongs to an Internal Standard Procedure is not cited in the firm outcome")]
    public async Task FirmFindingsProjection_RecordRuleWhoseBaseIsInternalStandard_CitesNothing()
    {
        var (h, recordSet) = RecordSetWorld();
        var record = RecordVersion(h, recordSet);
        var baseVersion = BaseVersionOfProcedure(h, (int)ProcedureType.InternalStandard);
        var world = new FirmFindingsProjectionTests.World();
        var invoiceId = world.Invoices.Single().Id;
        h.Seed(new Entity("sprk_invoice", invoiceId) { ["sprk_vendororg"] = new EntityReference("sprk_organization", Guid.NewGuid()) });
        h.AddSubmission(invoiceId, ProjectionSubmissionId, RuleSetServiceTests.FindingsWithRecordRules([(record, baseVersion)]));
        var rules = h.Service();

        world.Versions.Add(h.Row("sprk_policyversion", record));
        world.Policies.Add(h.Row("sprk_policy", h.Row("sprk_policyversion", record).GetAttributeValue<EntityReference>("sprk_policy").Id));
        world.RuleSets.Add(h.Row("sprk_ruleset", recordSet));
        world.Clause = version => rules.GetFirmClauseAsync(version, invoiceId, default).GetAwaiter().GetResult();
        world.AddReducedWithCitation(1, "Rate reduced.", record, "Firm reason one.");

        var outcome = await world.Projection().GetForInvoiceAsync(invoiceId, CancellationToken.None);

        outcome!.Items.Should().ContainSingle().Which.Citations.Should().BeEmpty("no clause, no section, no rule name or label reaches the firm");
        ShouldCarryNothingOfTheBase(JsonSerializer.Serialize(outcome));
        world.ClauseCalls.Should().BeEquivalentTo(new[] { record });
    }

    [Fact(DisplayName = "ISS-038 must-fire: a record rule shows no clause when the read of its base Procedure's type faults, and the log carries only the invoice and version ids")]
    public async Task GetFirmClause_WhenTheBaseProcedureTypeReadFaults_ShowsNothing()
    {
        var (h, recordSet) = RecordSetWorld();
        var record = RecordVersion(h, recordSet);
        var baseVersion = BaseVersionOfProcedure(h, (int)ProcedureType.Guideline);
        var invoice = h.Invoice(RuleSetServiceTests.FindingsWithRecordRules([(record, baseVersion)]));
        var rules = h.Service();
        (await rules.GetFirmClauseAsync(record, invoice, default)).Quote.Should().Be(InternalQuote, "the control: without the fault it shows");
        h.Logs.Clear();
        h.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_procedure"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse fault on " + BaseProcedureName));

        var clause = await rules.GetFirmClauseAsync(record, invoice, default);

        ShouldCarryNothingOf(clause);
        h.Logs.Where(l => l.Level >= LogLevel.Information).Should().OnlyContain(l => l.Exception == null);
        h.Logs.Should().OnlyContain(l => !l.Message.Contains(BaseProcedureName) && !l.Message.Contains(BaseSetName));
    }

    [Fact(DisplayName = "ISS-038: a record rule whose base cannot be used is logged once at Information with the invoice and version ids only")]
    public async Task GetFirmClause_RecordRuleWithNoUsableBase_LogsOneInformationEntryWithIdsOnly()
    {
        var (h, recordSet) = RecordSetWorld();
        var noSlot = RecordVersion(h, recordSet);
        var internalBase = RecordVersion(h, recordSet);
        var internalStandardBase = BaseVersionOfProcedure(h, (int)ProcedureType.InternalStandard);
        var usable = RecordVersion(h, recordSet);
        var visibleBase = BaseVersion(h, visible: true);
        var invoice = h.Invoice(RuleSetServiceTests.FindingsWithRecordRules([(internalBase, internalStandardBase), (usable, visibleBase)], noSlot));
        var rules = h.Service();
        h.Logs.Clear();

        (await rules.GetFirmClauseAsync(usable, invoice, default)).Quote.Should().Be(InternalQuote, "the control: a usable base logs nothing");
        h.Logs.Should().BeEmpty();

        ShouldCarryNothingOf(await rules.GetFirmClauseAsync(noSlot, invoice, default));
        var entry = h.Logs.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain(invoice.ToString("D")).And.Contain(noSlot.ToString("D"));
        entry.Message.Should().NotContain(InternalQuote).And.NotContain(InternalSection).And.NotContain(BaseSetName);
        h.Logs.Clear();

        ShouldCarryNothingOf(await rules.GetFirmClauseAsync(internalBase, invoice, default));
        h.Logs.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    // ── The helper and the snapshot reader ──

    [Theory(DisplayName = "ISS-038: VisibleToFirms needs the record set AND every base set visible; a non-record set needs its own flag only")]
    [InlineData(true, new[] { true }, true)]
    [InlineData(true, new[] { true, true }, true)]
    [InlineData(true, new[] { false }, false)]
    [InlineData(true, new[] { true, false }, false)]
    [InlineData(true, new bool[0], false)]
    [InlineData(false, new[] { true }, false)]
    public void VisibleToFirms_RecordScopedSet_NeedsBothFlags(bool recordFlag, bool[] baseFlags, bool expected)
    {
        var record = Set(recordFlag, recordScoped: true);
        var bases = baseFlags.Select(f => (Entity?)Set(f, recordScoped: false)).ToList();

        RuleSetClauseVisibility.VisibleToFirms(record, bases).Should().Be(expected);
    }

    [Fact(DisplayName = "ISS-038: VisibleToFirms fails closed on an unreadable base, a record-scoped base and a missing flag; a non-record set ignores bases")]
    public void VisibleToFirms_FailsClosed()
    {
        var record = Set(true, recordScoped: true);
        RuleSetClauseVisibility.VisibleToFirms(record, [null]).Should().BeFalse("a base that could not be read");
        RuleSetClauseVisibility.VisibleToFirms(record, [Set(true, recordScoped: true)]).Should().BeFalse("a base is never itself record-scoped");
        RuleSetClauseVisibility.VisibleToFirms(record, [new Entity("sprk_ruleset", Guid.NewGuid())]).Should().BeFalse("a base with no flag is off");
        RuleSetClauseVisibility.VisibleToFirms(new Entity("sprk_ruleset", Guid.NewGuid()), []).Should().BeFalse("a set with no flag is off");

        RuleSetClauseVisibility.VisibleToFirms(Set(true, recordScoped: false), []).Should().BeTrue("a non-record set: its own flag");
        RuleSetClauseVisibility.VisibleToFirms(Set(true, recordScoped: false), [Set(false, recordScoped: false)]).Should().BeTrue("bases are ignored");
        RuleSetClauseVisibility.VisibleToFirms(Set(false, recordScoped: false), []).Should().BeFalse();
    }

    [Fact(DisplayName = "ISS-038: the snapshot reader returns each record rule's base versions; an unreadable slot reads as Guid.Empty, the snapshot still reads")]
    public void ProcedureSnapshotRead_RecordRuleSlots_AreReturnedPerVersion()
    {
        var record = Guid.NewGuid();
        var damaged = Guid.NewGuid();
        var baseVersion = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var findings = System.Text.Json.Nodes.JsonNode.Parse(RuleSetServiceTests.FindingsWithRecordRules([(record, baseVersion), (damaged, Guid.NewGuid())], plain))!.AsObject();
        var rules = findings[ProcedureSnapshot.Member]!["resolutions"]![0]!["rules"]!.AsArray();
        rules.Single(r => (string?)r!["policyVersionId"] == damaged.ToString("D"))!["recordRule"]!["basePolicyVersionId"] = "not-a-guid";

        var snapshot = ProcedureSnapshot.Read(findings);

        snapshot.Should().NotBeNull();
        snapshot!.AppliedPolicyVersionIds.Should().Contain(new[] { record, damaged, plain });
        snapshot.RecordRuleBaseVersionIds[record].Should().Equal(baseVersion);
        snapshot.RecordRuleBaseVersionIds[damaged].Should().Equal(Guid.Empty);
        snapshot.RecordRuleBaseVersionIds.Should().NotContainKey(plain);
    }

    private static Entity Set(bool visible, bool recordScoped)
    {
        var set = new Entity("sprk_ruleset", Guid.NewGuid()) { ["sprk_visibletofirms"] = visible };
        if (recordScoped)
        {
            set["sprk_scope"] = new OptionSetValue(RecordRuleSet.ScopeRecord);
            set[Matter] = new EntityReference(Matter, Guid.NewGuid());
        }

        return set;
    }

    private static Guid SetOfVersion(RuleSetServiceTests.Harness h, Guid version) =>
        SetOf(h, h.Row("sprk_policyversion", version).GetAttributeValue<EntityReference>("sprk_policy").Id);
}
