using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// The drafting half of <see cref="RuleSetService"/> (ebilling-r1 task 075, FR-27; keyed per Procedure Version by task 116, model 7.2):
/// <c>GetDraftSourceAsync</c> and <c>AddDraftsAsync</c>.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees the review (076) and the job rely on — drafting runs only for a Draft version of a live Billing
/// Procedure with exactly one Servable source file; model output is a proposal that is validated before it is stored, a refusal is listed and
/// never dropped, a drafted rule is Proposed / Off / in force nowhere and belongs to its version (publish state Draft); the version's
/// not-converted memo is written first and its Yes/No marker LAST (EB-D-211); a repeated run adds nothing twice; version 2 drafts although
/// version 1 did. Reuses the 076 harness (real validation, describer and orchestration over a fake store that evaluates the real queries).
/// </remarks>
public sealed class RuleSetServiceDraftTests
{
    private static readonly Guid CallerOid = Guid.NewGuid();

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static DraftedRule Increment(string quote = "Time must be billed in tenth-hour increments.", string? location = "Section 4.2") =>
        Drafted("TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""", "Flag", quote, location); // task 073: TimeIncrement may not reject (FR-26)

    // 073 review F3: an eligible type with Reject, so the drafted Reject -> "billing-reject" mapping stays asserted.
    private static DraftedRule Age(string quote = "Time older than 90 days before the invoice date will be rejected.", string? location = "Section 4.2") =>
        Drafted("InvoiceAge", """{"type":"InvoiceAge","maxAgeDays":90}""", "Reject", quote, location);

    private static DraftedRule Cap(int hours, string quote) =>
        Drafted("DailyHoursCap", $$"""{"type":"DailyHoursCap","maxHoursPerDay":{{hours}}}""", "Flag", quote, "Section 5");

    private static DraftedRule Drafted(string type, string body, string consequence, string quote, string? location)
    {
        var (headline, template) = BillingRuleDraftTranslator.Wording(Enum.Parse<RuleType>(type));
        return new DraftedRule(type, body, template, headline, consequence, quote, location);
    }

    private static IReadOnlyList<NotConvertedClause> None => Array.Empty<NotConvertedClause>();

    private static IEnumerable<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)> VersionWrites(Harness h) =>
        h.Writes.Where(w => w.Entity == ProcedureEntity.VersionEntity);

    // ── GetDraftSourceAsync ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDraftSource_ReturnsTheVersionsOneServableSourceFile_AndItsProceduresRuleSet_ReadingOnly()
    {
        var h = new Harness();
        var d = h.DraftVersion();

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value.Should().Be(new DraftSource(d.Version, d.Procedure, d.RuleSet, d.File, "guidelines.pdf", AlreadyDrafted: false));
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDraftSource_TheRouteForm_AVersionOfAnotherProcedure_IsTheUnknownId404_BeforeAnyOtherCheck()
    {
        var h = new Harness();
        var mine = h.DraftVersion();
        // Another Procedure's version that would fail every later check (Ontology, retired, not a Draft, no file): still the plain 404.
        var other = h.DraftVersion(area: RuleDomains.PolicyDomainOntology, procedureStatus: ProcedureEntity.StatusRetired,
            versionStatus: ProcedureEntity.VersionStatusActive, servableFiles: 0);

        var outcome = await h.Service().GetDraftSourceAsync(mine.Procedure, other.Version, CancellationToken.None);

        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be(ProcedureService.CodeVersionNotFound);
        outcome.Value.Should().BeNull();
        (await h.Service().GetDraftSourceAsync(mine.Procedure, mine.Version, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);
    }

    [Fact]
    public async Task GetDraftSource_AnUnknownVersion_Is404()
    {
        var h = new Harness();

        var outcome = await h.Service().GetDraftSourceAsync(Guid.NewGuid(), CancellationToken.None);

        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be(ProcedureService.CodeVersionNotFound);
    }

    [Theory]
    [InlineData(RuleDomains.PolicyDomainOntology)]
    [InlineData(null)]
    public async Task GetDraftSource_AProcedureWhoseAreaIsNotBilling_Is400_FailClosedOnAMissingArea(int? area)
    {
        var h = new Harness();
        var d = h.DraftVersion(area: area);

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.StatusCode.Should().Be(400);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeNotBilling);
    }

    [Fact]
    public async Task GetDraftSource_ARetiredProcedure_Is409()
    {
        var h = new Harness();
        var d = h.DraftVersion(procedureStatus: ProcedureEntity.StatusRetired);

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(ProcedureService.CodeRetired);
    }

    [Theory]
    [InlineData(ProcedureEntity.VersionStatusActive)]
    [InlineData(ProcedureEntity.VersionStatusSuperseded)]
    [InlineData(ProcedureEntity.VersionStatusRetired)]
    [InlineData(null)]
    public async Task GetDraftSource_AVersionThatIsNotADraft_Is409NotDraft_AMissingStatusIncluded(int? status)
    {
        var h = new Harness();
        var d = h.DraftVersion(versionStatus: status);

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeVersionNotDraft);
    }

    [Fact]
    public async Task GetDraftSource_AProcedureWithNoRuleSet_Is409RuleSetMissing()
    {
        var h = new Harness();
        var d = h.DraftVersion(withRuleSet: false);

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(ProcedureService.CodeRuleSetMissing);
    }

    [Fact]
    public async Task GetDraftSource_ARetiredRuleSet_Is409()
    {
        var h = new Harness();
        var d = h.DraftVersion(setStatus: RuleSetService.RuleSetRetired);

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeRetired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task GetDraftSource_NotExactlyOneServableSourceFile_Is422SourceMissing(int servable)
    {
        var h = new Harness();
        var d = h.DraftVersion(servableFiles: servable);

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.StatusCode.Should().Be(422);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeSourceMissing);
        outcome.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetDraftSource_OnlyTheVersionsServableProcedureSourceRowCounts()
    {
        var h = new Harness();
        var d = h.DraftVersion(servableFiles: 0);
        h.SourceFile(d.Version, StoredFileStatus.Removed, "old.pdf");
        h.SourceFile(d.Version, StoredFileStatus.NotYetServable, "staged.pdf");
        h.SourceFile(d.Version, StoredFileStatus.Servable, "export.csv", StoredFilePurpose.InvoicePdf);
        h.SourceFile(h.DraftVersion(servableFiles: 0).Version, StoredFileStatus.Servable, "another-version.pdf");

        (await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None)).ErrorCode.Should().Be(RuleSetService.CodeSourceMissing,
            "a Removed, staged, other-purpose or other-version row is not this version's source");

        var current = h.SourceFile(d.Version, StoredFileStatus.Servable, "current.pdf");
        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);
        outcome.Value!.FileId.Should().Be(current);
        outcome.Value.FileName.Should().Be("current.pdf");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public async Task GetDraftSource_AlreadyDrafted_ReadsTheVersionsYesNo_OnlyAnExplicitYes(bool? marker, bool expected)
    {
        var h = new Harness();
        // EB-D-211: the memo is written BEFORE the marker, so a memo alone (a run that died between the two writes) is not "drafted".
        var d = h.DraftVersion(drafted: marker, notConvertedList: "[]");

        var outcome = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        outcome.Value!.AlreadyDrafted.Should().Be(expected);
    }

    // ── AddDraftsAsync: the row shape ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AddDrafts_StoresEachRuleAsAProposedOffBillingRuleOfTheVersion_ThenTheMemo_ThenTheMarkerLast()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var id = d.RuleSet;
        var rules = new[] { Age(), Cap(10, "Max ten hours a day.") };

        var outcome = await h.Service().AddDraftsAsync(d.Version, rules, None, CallerOid, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Stored.Should().Be(2);
        var policies = h.Rows["sprk_policy"];
        policies.Should().HaveCount(2);
        foreach (var policy in policies)
        {
            policy.GetAttributeValue<OptionSetValue>("sprk_domain").Value.Should().Be(RuleDomains.PolicyDomainBilling);
            policy.GetAttributeValue<EntityReference>("sprk_ruleset").Id.Should().Be(id, "the drafts go into the Procedure's rule set");
            policy.GetAttributeValue<bool>("sprk_enabled").Should().BeFalse("a drafted rule is Off until published");
            policy.Contains("sprk_currentversion").Should().BeFalse("nothing is in force before a publish");
            policy.GetAttributeValue<OptionSetValue>("sprk_lane").Value.Should().Be((int)DecisionLane.Decide);
            policy.GetAttributeValue<string>("sprk_policycode").Should().StartWith($"BRD-{id:N}-").And.HaveLength(40);
        }

        policies.Select(p => p.GetAttributeValue<string>("sprk_policycode")).Should().Equal($"BRD-{id:N}-001", $"BRD-{id:N}-002");

        var first = h.Rows["sprk_policyversion"].First();
        first.GetAttributeValue<int>("sprk_versionnumber").Should().Be(1);
        first.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);
        first.GetAttributeValue<OptionSetValue>("sprk_ruletype").Value.Should().Be((int)RuleType.InvoiceAge);
        first.GetAttributeValue<OptionSetValue>("sprk_consequence").Value.Should().Be((int)BillingConsequence.Reject);
        first.GetAttributeValue<string>("sprk_clausereference").Should().Be("Time older than 90 days before the invoice date will be rejected.");
        first.GetAttributeValue<string>("sprk_clauselocation").Should().Be("Section 4.2");
        first.GetAttributeValue<string>("sprk_rulebody").Should().Be("""{"type":"InvoiceAge","maxAgeDays":90}""");
        first.GetAttributeValue<string>("sprk_proposedaction").Should().Be("billing-reject");
        var second = h.Rows["sprk_policyversion"].Skip(1).First();
        second.GetAttributeValue<OptionSetValue>("sprk_consequence").Value.Should().Be((int)BillingConsequence.Flag);
        second.GetAttributeValue<string>("sprk_proposedaction").Should().Be("billing-flag");
        first.GetAttributeValue<DateTime?>("sprk_inforcefrom").Should().NotBeNull();
        first.Contains("sprk_inforceto").Should().BeFalse();
        h.Rows["sprk_policyversion"].Should().OnlyContain(v =>
            v.GetAttributeValue<EntityReference>(RuleSetService.ProcedureVersionColumn).Id == d.Version
            && v.GetAttributeValue<OptionSetValue>(RuleSetService.PublishStateColumn).Value == RuleSetService.PublishStateDraft,
            "model 7.2: a drafted rule's version belongs to the Procedure Version it was drafted from, Draft until that version publishes");

        // EB-D-211: the memo, then the Yes/No marker, as the LAST two writes, both on the version; nothing on the rule set.
        var tail = h.Writes.TakeLast(2).ToList();
        (tail[0].Kind, tail[0].Entity, tail[0].Id).Should().Be((Write.Update, ProcedureEntity.VersionEntity, d.Version));
        tail[0].Fields.Keys.Should().Equal(RuleSetService.VersionNotConvertedListColumn);
        (tail[1].Kind, tail[1].Entity, tail[1].Id).Should().Be((Write.Update, ProcedureEntity.VersionEntity, d.Version));
        tail[1].Fields.Should().Equal(new Dictionary<string, object> { [RuleSetService.VersionDraftedColumn] = true });
        VersionWrites(h).Should().HaveCount(2);
        h.Writes.Should().NotContain(w => w.Entity == "sprk_ruleset", "the set carries no drafting marker any more");
        h.Writes.Where(w => w.Kind == Write.Create).Select(w => w.Entity).Should().Equal(
            "sprk_policy", "sprk_policyversion", "sprk_policy", "sprk_policyversion");
    }

    [Fact]
    public async Task AddDrafts_TheRulesAreReadableByTheReviewAndAcceptableAsDrafted()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        await service.AddDraftsAsync(d.Version, new[] { Increment(), Cap(10, "Max ten.") }, None, CallerOid, CancellationToken.None);

        var detail = await service.GetAsync(d.RuleSet, CancellationToken.None);

        detail.Kind.Should().Be(RuleSetOutcomeKind.Done);
        detail.Value!.RuleSet.RuleCount.Should().Be(2);
        detail.Value.NotConverted.Should().NotBeNull("the version's memo is an empty list, which is still present");
        detail.Value.Proposals.Should().OnlyContain(p => p.State == "Proposed" && !p.InForce && !p.HasPublishedVersion);
        detail.Value.Proposals.Should().OnlyContain(p => p.ValidationErrors.Count == 0, "the drafted wording and rows satisfy the review's own validation");
        detail.Value.Proposals.Select(p => p.ClauseQuote).Should().BeEquivalentTo("Time must be billed in tenth-hour increments.", "Max ten.");

        // The version token the page was shown with the rule set read (task 077): the decision names it.
        var shown = detail.Value.Proposals[0];
        shown.RowVersion.Should().NotBeNullOrEmpty("a read carries the token the page sends back with Accept and Reject");
        var accepted = await service.DecideAsync(d.RuleSet, shown.PolicyId, accept: true, long.Parse(shown.RowVersion!), CancellationToken.None);
        accepted.Kind.Should().Be(RuleSetOutcomeKind.Done);
        accepted.Value!.State.Should().Be("Accepted");
    }

    // ── AddDraftsAsync: a proposal is validated, a refusal is surfaced ──────────────────────────

    [Fact]
    public async Task AddDrafts_ARuleThatFailsValidateForSave_IsListedAsNotConverted_NeverStored_AndNeverDropped()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var bad = Drafted("DailyHoursCap", """{"type":"DailyHoursCap","maxHoursPerDay":99}""", "Flag", "Up to 99 hours.", "Section 9");
        var good = Increment();

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { bad, good }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(1);
        h.Rows["sprk_policy"].Should().ContainSingle();
        outcome.Value.NotConverted.Should().ContainSingle();
        var refusal = outcome.Value.NotConverted[0];
        refusal.Text.Should().Be("Up to 99 hours.");
        refusal.Location.Should().Be("Section 9");
        refusal.Reason.Should().StartWith("Not stored: the drafted rule was not valid").And.Contain("ruleBody");

        // And the version's memo carries it.
        var memo = (string)VersionWrites(h).Single(w => w.Fields.ContainsKey(RuleSetService.VersionNotConvertedListColumn))
            .Fields[RuleSetService.VersionNotConvertedListColumn];
        using var stored = JsonDocument.Parse(memo);
        stored.RootElement.GetArrayLength().Should().Be(1);
        stored.RootElement[0].GetProperty("text").GetString().Should().Be("Up to 99 hours.");
        stored.RootElement[0].GetProperty("location").GetString().Should().Be("Section 9");
    }

    [Theory]
    [InlineData("Maybe")]
    [InlineData("")]
    [InlineData("100000000")]
    public async Task AddDrafts_AnUnknownConsequence_IsListed_NotStored(string consequence)
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var rule = Increment() with { Consequence = consequence };

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { rule }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        h.Rows.ContainsKey("sprk_policy").Should().BeFalse();
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("consequence");
    }

    [Fact]
    public async Task AddDrafts_ABodyOfAnOntologyRuleType_IsRefused_ByTheBillingDomainValidation()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var rule = Increment() with { RuleType = "Existence", RuleBody = """{"type":"Existence","subject":"x"}""" };

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { rule }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        h.Rows.ContainsKey("sprk_policy").Should().BeFalse();
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("ruleType");
    }

    [Fact]
    public async Task AddDrafts_ATemplateNamingAFieldTheTypeDoesNotSupply_IsRefused()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var rule = Increment() with { MessageTemplate = "Billed by {{line.secretField}}." };

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { rule }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("messageTemplate");
    }

    [Fact]
    public async Task AddDrafts_AClauseQuoteOverTheColumnSize_IsListed_NotTruncated()
    {
        var h = new Harness();
        var d = h.DraftVersion();

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { Increment(quote: new string('q', 4001)) }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("4000");
    }

    [Fact]
    public async Task AddDrafts_ALongClauseLocation_IsCutToTheColumnSize()
    {
        var h = new Harness();
        var d = h.DraftVersion();

        await h.Service().AddDraftsAsync(d.Version, new[] { Increment(location: new string('l', 300)) }, None, CallerOid, CancellationToken.None);

        h.Rows["sprk_policyversion"].Single().GetAttributeValue<string>("sprk_clauselocation").Should().HaveLength(200);
    }

    [Fact]
    public async Task AddDrafts_TheModelsOwnNotConvertedList_IsKeptBeforeTheRefusals()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var fromModel = new[] { new NotConvertedClause("Staff sensibly.", "Section 9.3", "No offered rule type checks this.") };
        var bad = Drafted("DailyHoursCap", """{"type":"DailyHoursCap","maxHoursPerDay":99}""", "Flag", "99h", null);

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { bad }, fromModel, CallerOid, CancellationToken.None);

        outcome.Value!.NotConverted.Select(n => n.Text).Should().Equal("Staff sensibly.", "99h");
    }

    [Fact]
    public async Task AddDrafts_NoRulesAndNothingNotConverted_StillWritesAnEmptyMemo_ThenTheMarker()
    {
        var h = new Harness();
        var d = h.DraftVersion();

        var outcome = await h.Service().AddDraftsAsync(d.Version, Array.Empty<DraftedRule>(), None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        h.Writes.Select(w => (w.Entity, string.Join(",", w.Fields.Keys))).Should().Equal(
            (ProcedureEntity.VersionEntity, RuleSetService.VersionNotConvertedListColumn),
            (ProcedureEntity.VersionEntity, RuleSetService.VersionDraftedColumn));
        h.Writes[0].Fields[RuleSetService.VersionNotConvertedListColumn].Should().Be("[]");
        h.Ownership.Verify(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never,
            "no row is written, so no owner is resolved");
        (await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeTrue();
    }

    // ── AddDraftsAsync: idempotency (per version) ───────────────────────────────────────────────

    [Fact]
    public async Task AddDrafts_AfterTheMarker_Is409AlreadyDrafted_AndWritesNothing()
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, notConvertedList: "[]");

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeAlreadyDrafted);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_ARunThatDiedBetweenTheMemoAndTheMarker_IsNotDrafted_AndTheRepeatOverwritesTheMemo()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        var versionUpdates = 0;
        h.FailUpdateFor = e => e == ProcedureEntity.VersionEntity && ++versionUpdates == 2; // the marker write fails

        var failed = await service.AddDraftsAsync(d.Version, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        failed.StatusCode.Should().Be(500);
        h.Row(ProcedureEntity.VersionEntity, d.Version).Contains(RuleSetService.VersionNotConvertedListColumn).Should().BeTrue();
        h.Row(ProcedureEntity.VersionEntity, d.Version).Contains(RuleSetService.VersionDraftedColumn).Should().BeFalse();
        (await service.GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeFalse(
            "the memo alone is not the marker: the next delivery compensates and drafts again");

        h.FailUpdateFor = null;
        var repeat = await service.AddDraftsAsync(d.Version, new[] { Increment() }, new[] { new NotConvertedClause("x", null, "y") }, CallerOid, CancellationToken.None);

        repeat.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row(ProcedureEntity.VersionEntity, d.Version).GetAttributeValue<string>(RuleSetService.VersionNotConvertedListColumn).Should().Contain("\"x\"");
        h.Row(ProcedureEntity.VersionEntity, d.Version).GetAttributeValue<bool>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
    }

    [Fact]
    public async Task AddDrafts_Version2_DraftsAlthoughVersion1IsDrafted_AndAClauseVersion1HoldsIsProposedAgain()
    {
        var h = new Harness();
        var v1 = h.DraftVersion();
        var service = h.Service();
        (await service.AddDraftsAsync(v1.Version, new[] { Increment() }, None, CallerOid, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row(ProcedureEntity.VersionEntity, v1.Version)[ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusActive);
        var v2 = h.DraftVersion(procedure: v1.Procedure, number: 2);

        (await service.GetDraftSourceAsync(v2.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeFalse("version 2 has its own marker");
        var outcome = await service.AddDraftsAsync(v2.Version, new[] { Increment(), Cap(10, "Max ten.") }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(2, "the repeat check is per version: version 1's rule does not cover version 2's clause");
        outcome.Value.AlreadyPresent.Should().Be(0);
        h.Rows["sprk_policy"].Select(p => p.GetAttributeValue<string>("sprk_policycode")).Should().Equal(
            new[] { $"BRD-{v1.RuleSet:N}-001", $"BRD-{v1.RuleSet:N}-002", $"BRD-{v1.RuleSet:N}-003" }, "codes stay numbered per SET");
        h.Rows["sprk_policyversion"].Count(v => v.GetAttributeValue<EntityReference>(RuleSetService.ProcedureVersionColumn).Id == v2.Version).Should().Be(2);
        h.Row(ProcedureEntity.VersionEntity, v1.Version).GetAttributeValue<bool>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
        h.Row(ProcedureEntity.VersionEntity, v2.Version).GetAttributeValue<bool>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
    }

    [Fact]
    public async Task AddDrafts_ARepeatedRule_InOneRun_IsStoredOnce()
    {
        var h = new Harness();
        var d = h.DraftVersion();

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { Increment(), Increment() }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(1);
        outcome.Value.AlreadyPresent.Should().Be(1);
        h.Rows["sprk_policy"].Should().ContainSingle();
    }

    [Fact]
    public async Task AddDrafts_ARunThatFailedMidWay_IsCompletedByTheRepeat_WithNoRuleStoredTwice()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var rules = new[] { Increment(), Cap(10, "Max ten."), Cap(8, "Max eight.") };
        var service = h.Service();

        // The second rule's version cannot be created: its policy is removed again, the marker is not written.
        var versionCreates = 0;
        h.FailCreateWhen = e => e.LogicalName == "sprk_policyversion" && ++versionCreates == 2;
        var failed = await service.AddDraftsAsync(d.Version, rules, None, CallerOid, CancellationToken.None);

        failed.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        failed.StatusCode.Should().Be(500);
        failed.ErrorCode.Should().Be(RuleSetService.CodeWriteFailed);
        h.Rows["sprk_policy"].Should().ContainSingle("the second rule's policy has no version and was removed");
        h.Writes.Where(w => w.Entity == "sprk_policy" && w.Kind == Write.Delete).Should().ContainSingle();
        VersionWrites(h).Should().BeEmpty("the memo and the marker are written only when every rule is stored");
        (await service.GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeFalse();

        // The repeat (a redelivery) skips the stored rule and adds the other two.
        h.FailCreateWhen = null;
        var repeat = await service.AddDraftsAsync(d.Version, rules, None, CallerOid, CancellationToken.None);

        repeat.Value!.Stored.Should().Be(2);
        repeat.Value.AlreadyPresent.Should().Be(1);
        h.Rows["sprk_policy"].Select(p => p.GetAttributeValue<string>("sprk_policycode")).Should().OnlyHaveUniqueItems().And.HaveCount(3);
        h.Rows["sprk_policyversion"].Should().HaveCount(3);
        (await service.GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeTrue();
    }

    [Fact]
    public async Task AddDrafts_AVersionFailure_RemovesItsPolicy_AndStoresNoMarker()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        h.FailCreateWhen = e => e.LogicalName == "sprk_policyversion";

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(500);
        h.Rows["sprk_policy"].Should().BeEmpty("a policy with no version would count as a rule and show nowhere");
        VersionWrites(h).Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_MoreRulesThanTheRunMaximum_ListsTheRest_ForTheAdministrator()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var rules = Enumerable.Range(0, RuleSetService.MaxDraftedRules + 1)
            .Select(i => Increment(quote: $"Clause {i}.")).ToList();

        var outcome = await h.Service().AddDraftsAsync(d.Version, rules, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(RuleSetService.MaxDraftedRules);
        outcome.Value.NotConverted.Should().ContainSingle().Which.Text.Should().Be($"Clause {RuleSetService.MaxDraftedRules}.");
    }

    // ── AddDraftsAsync: the version's state, the owner ──────────────────────────────────────────

    public static TheoryData<string, int, string> Refusals => new()
    {
        { "unknown version", 404, ProcedureService.CodeVersionNotFound },
        { "Ontology Procedure", 400, RuleSetService.CodeNotBilling },
        { "retired Procedure", 409, ProcedureService.CodeRetired },
        { "Active version", 409, RuleSetService.CodeVersionNotDraft },
        { "no rule set", 409, ProcedureService.CodeRuleSetMissing },
        { "retired rule set", 409, RuleSetService.CodeRetired },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task AddDrafts_ARefusedTarget_IsRefusedWithItsCode_AndNothingIsWritten(string scenario, int status, string code)
    {
        var h = new Harness();
        var version = scenario switch
        {
            "unknown version" => Guid.NewGuid(),
            "Ontology Procedure" => h.DraftVersion(area: RuleDomains.PolicyDomainOntology).Version,
            "retired Procedure" => h.DraftVersion(procedureStatus: ProcedureEntity.StatusRetired).Version,
            "Active version" => h.DraftVersion(versionStatus: ProcedureEntity.VersionStatusActive).Version,
            "no rule set" => h.DraftVersion(withRuleSet: false).Version,
            _ => h.DraftVersion(setStatus: RuleSetService.RuleSetRetired).Version,
        };

        var outcome = await h.Service().AddDraftsAsync(version, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(status);
        outcome.ErrorCode.Should().Be(code);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_WhenTheOwnerCannotBeResolved_RefusesAndWritesNothing()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        h.OwnerAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team");

        var outcome = await h.Service().AddDraftsAsync(d.Version, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_NeverTouchesAnOntologyPolicyOfTheSameName()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var ontology = new Entity("sprk_policy", Guid.NewGuid());
        ontology["sprk_policycode"] = "ONT-1";
        ontology["sprk_ruleset"] = new EntityReference("sprk_ruleset", d.RuleSet); // even one wrongly linked to this set
        h.Seed(ontology);

        await h.Service().AddDraftsAsync(d.Version, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        h.Writes.Where(w => w.Entity == "sprk_policy" && w.Kind != Write.Create).Should().BeEmpty();
        h.Row("sprk_policy", ontology.Id).GetAttributeValue<string>("sprk_policycode").Should().Be("ONT-1");
        h.Rows["sprk_policy"].Should().HaveCount(2).And.Contain(ontology);
    }

    // ── The not-converted list as stored ────────────────────────────────────────────────────────

    [Fact]
    public void BuildNotConvertedJson_OfNothing_IsAnEmptyArray_NotEmptyText()
    {
        RuleSetService.BuildNotConvertedJson(Array.Empty<NotConvertedClause>()).Should().Be("[]");
    }

    [Fact]
    public void BuildNotConvertedJson_StaysUnderTheColumnSize_AndSaysHowManyWereLeftOut()
    {
        var clauses = Enumerable.Range(0, 400).Select(i => new NotConvertedClause(new string('x', 1000), $"S{i}", "No type fits.")).ToList();

        var json = RuleSetService.BuildNotConvertedJson(clauses);

        json.Length.Should().BeLessThan(100_000);
        using var doc = JsonDocument.Parse(json);
        var last = doc.RootElement.EnumerateArray().Last();
        last.GetProperty("reason").GetString().Should().Contain("more clause(s) were not listed");
        doc.RootElement.GetArrayLength().Should().BeLessThan(clauses.Count);
    }
}
