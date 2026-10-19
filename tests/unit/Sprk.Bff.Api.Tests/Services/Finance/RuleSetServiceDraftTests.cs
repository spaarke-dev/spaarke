using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// The drafting half of <see cref="RuleSetService"/> (ebilling-r1 task 075, FR-27): <c>GetDraftSourceAsync</c> and <c>AddDraftsAsync</c>.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees the review (076) and the job rely on — model output is a proposal that is validated before it is
/// stored, a refusal is listed and never dropped, a drafted rule is Proposed / Off / in force nowhere, the completion marker is written last,
/// a repeated run adds nothing twice, and an Ontology policy or rule set is never read or written. Reuses the 076 harness (real validation,
/// describer and orchestration over a fake store that evaluates the real queries).
/// </remarks>
public sealed class RuleSetServiceDraftTests
{
    private const string Drive = "b!procedurelibrary";
    private static readonly Guid SourceId = Guid.NewGuid();
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

    private static Guid RuleSetWithSource(Harness h, string? notConverted = null, int status = RuleSetService.RuleSetDraft, string drive = Drive)
    {
        var id = h.RuleSet(status: status, notConverted: notConverted);
        h.Row("sprk_ruleset", id)["sprk_sourcedocument"] = new EntityReference("sprk_document", SourceId);
        h.Documents.Setup(d => d.GetDocumentAsync(SourceId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity { Id = SourceId.ToString(), Name = "guidelines", GraphDriveId = drive, GraphItemId = "item-1", FileName = "guidelines.pdf" });
        return id;
    }

    private static IReadOnlyList<NotConvertedClause> None => Array.Empty<NotConvertedClause>();

    // ── GetDraftSourceAsync ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDraftSource_ReturnsTheProcedureLibraryDocument_OfADraftRuleSet()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);

        var outcome = await h.Service().GetDraftSourceAsync(id, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value.Should().Be(new DraftSource(id, "Acme guidelines", SourceId, Drive, "item-1", "guidelines.pdf", AlreadyDrafted: false));
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RuleDomains.PolicyDomainOntology)]
    public async Task GetDraftSource_ARuleSetOfAnotherDomain_IsNotFound(int? domain)
    {
        var h = new Harness();
        var id = h.RuleSet(domain: domain);

        var outcome = await h.Service().GetDraftSourceAsync(id, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeNotFound);
    }

    [Fact]
    public async Task GetDraftSource_ARetiredRuleSet_Is409()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h, status: RuleSetService.RuleSetRetired);

        var outcome = await h.Service().GetDraftSourceAsync(id, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeRetired);
    }

    [Fact]
    public async Task GetDraftSource_WithNoSourceDocument_Is422()
    {
        var h = new Harness();
        var id = h.RuleSet();

        var outcome = await h.Service().GetDraftSourceAsync(id, CancellationToken.None);

        outcome.StatusCode.Should().Be(422);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeSourceMissing);
    }

    [Fact]
    public async Task GetDraftSource_ADocumentPointingAtAnotherContainer_IsRefused_SoTheJobNeverFollowsThePointer()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h, drive: "b!someone-elses-container");

        var outcome = await h.Service().GetDraftSourceAsync(id, CancellationToken.None);

        outcome.StatusCode.Should().Be(422);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeSourceMissing);
        outcome.Value.Should().BeNull("no drive or item id of the foreign pointer leaves the service");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"text":"x","location":null,"reason":"y"}]""")]
    public async Task GetDraftSource_AFinishedRun_IsMarkedAlreadyDrafted_EvenWhenItsListIsEmpty(string notConverted)
    {
        var h = new Harness();
        var id = RuleSetWithSource(h, notConverted);

        var outcome = await h.Service().GetDraftSourceAsync(id, CancellationToken.None);

        outcome.Value!.AlreadyDrafted.Should().BeTrue();
    }

    // ── AddDraftsAsync: the row shape ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AddDrafts_StoresEachRuleAsAProposedOffBillingRule_InForceNowhere_AndWritesTheMarkerLast()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var rules = new[] { Age(), Cap(10, "Max ten hours a day.") };

        var outcome = await h.Service().AddDraftsAsync(id, rules, None, CallerOid, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Stored.Should().Be(2);
        var policies = h.Rows["sprk_policy"];
        policies.Should().HaveCount(2);
        foreach (var policy in policies)
        {
            policy.GetAttributeValue<OptionSetValue>("sprk_domain").Value.Should().Be(RuleDomains.PolicyDomainBilling);
            policy.GetAttributeValue<EntityReference>("sprk_ruleset").Id.Should().Be(id);
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

        // The marker is the last write, and it is on the rule set.
        var last = h.Writes.Last();
        (last.Kind, last.Entity, last.Id).Should().Be((Write.Update, "sprk_ruleset", id));
        last.Fields.Should().ContainKey("sprk_notconverted");
        h.Writes.Count(w => w.Entity == "sprk_ruleset").Should().Be(1);
        h.Writes.Where(w => w.Kind == Write.Create).Select(w => w.Entity).Should().Equal(
            "sprk_policy", "sprk_policyversion", "sprk_policy", "sprk_policyversion");
    }

    [Fact]
    public async Task AddDrafts_TheRulesAreReadableByTheReviewAndAcceptableAsDrafted()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var service = h.Service();
        await service.AddDraftsAsync(id, new[] { Increment(), Cap(10, "Max ten.") }, None, CallerOid, CancellationToken.None);

        var detail = await service.GetAsync(id, CancellationToken.None);

        detail.Kind.Should().Be(RuleSetOutcomeKind.Done);
        detail.Value!.RuleSet.RuleCount.Should().Be(2);
        detail.Value.NotConverted.Should().NotBeNull("the marker is an empty list, which is still present");
        detail.Value.Proposals.Should().OnlyContain(p => p.State == "Proposed" && !p.InForce && !p.HasPublishedVersion);
        detail.Value.Proposals.Should().OnlyContain(p => p.ValidationErrors.Count == 0, "the drafted wording and rows satisfy the review's own validation");
        detail.Value.Proposals.Select(p => p.ClauseQuote).Should().BeEquivalentTo("Time must be billed in tenth-hour increments.", "Max ten.");

        // The version token the page was shown with the rule set read (task 077): the decision names it.
        var shown = detail.Value.Proposals[0];
        shown.RowVersion.Should().NotBeNullOrEmpty("a read carries the token the page sends back with Accept and Reject");
        var accepted = await service.DecideAsync(id, shown.PolicyId, accept: true, long.Parse(shown.RowVersion!), CancellationToken.None);
        accepted.Kind.Should().Be(RuleSetOutcomeKind.Done);
        accepted.Value!.State.Should().Be("Accepted");
    }

    // ── AddDraftsAsync: a proposal is validated, a refusal is surfaced ──────────────────────────

    [Fact]
    public async Task AddDrafts_ARuleThatFailsValidateForSave_IsListedAsNotConverted_NeverStored_AndNeverDropped()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var bad = Drafted("DailyHoursCap", """{"type":"DailyHoursCap","maxHoursPerDay":99}""", "Flag", "Up to 99 hours.", "Section 9");
        var good = Increment();

        var outcome = await h.Service().AddDraftsAsync(id, new[] { bad, good }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(1);
        h.Rows["sprk_policy"].Should().ContainSingle();
        outcome.Value.NotConverted.Should().ContainSingle();
        var refusal = outcome.Value.NotConverted[0];
        refusal.Text.Should().Be("Up to 99 hours.");
        refusal.Location.Should().Be("Section 9");
        refusal.Reason.Should().StartWith("Not stored: the drafted rule was not valid").And.Contain("ruleBody");

        // And the marker carries it.
        using var stored = JsonDocument.Parse((string)h.Writes.Last().Fields["sprk_notconverted"]);
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
        var id = RuleSetWithSource(h);
        var rule = Increment() with { Consequence = consequence };

        var outcome = await h.Service().AddDraftsAsync(id, new[] { rule }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        h.Rows.ContainsKey("sprk_policy").Should().BeFalse();
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("consequence");
    }

    [Fact]
    public async Task AddDrafts_ABodyOfAnOntologyRuleType_IsRefused_ByTheBillingDomainValidation()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var rule = Increment() with { RuleType = "Existence", RuleBody = """{"type":"Existence","subject":"x"}""" };

        var outcome = await h.Service().AddDraftsAsync(id, new[] { rule }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        h.Rows.ContainsKey("sprk_policy").Should().BeFalse();
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("ruleType");
    }

    [Fact]
    public async Task AddDrafts_ATemplateNamingAFieldTheTypeDoesNotSupply_IsRefused()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var rule = Increment() with { MessageTemplate = "Billed by {{line.secretField}}." };

        var outcome = await h.Service().AddDraftsAsync(id, new[] { rule }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("messageTemplate");
    }

    [Fact]
    public async Task AddDrafts_AClauseQuoteOverTheColumnSize_IsListed_NotTruncated()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);

        var outcome = await h.Service().AddDraftsAsync(id, new[] { Increment(quote: new string('q', 4001)) }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().Contain("4000");
    }

    [Fact]
    public async Task AddDrafts_ALongClauseLocation_IsCutToTheColumnSize()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);

        await h.Service().AddDraftsAsync(id, new[] { Increment(location: new string('l', 300)) }, None, CallerOid, CancellationToken.None);

        h.Rows["sprk_policyversion"].Single().GetAttributeValue<string>("sprk_clauselocation").Should().HaveLength(200);
    }

    [Fact]
    public async Task AddDrafts_TheModelsOwnNotConvertedList_IsKeptBeforeTheRefusals()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var fromModel = new[] { new NotConvertedClause("Staff sensibly.", "Section 9.3", "No offered rule type checks this.") };
        var bad = Drafted("DailyHoursCap", """{"type":"DailyHoursCap","maxHoursPerDay":99}""", "Flag", "99h", null);

        var outcome = await h.Service().AddDraftsAsync(id, new[] { bad }, fromModel, CallerOid, CancellationToken.None);

        outcome.Value!.NotConverted.Select(n => n.Text).Should().Equal("Staff sensibly.", "99h");
    }

    [Fact]
    public async Task AddDrafts_NoRulesAndNothingNotConverted_StillWritesTheMarker_AsAnEmptyList()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);

        var outcome = await h.Service().AddDraftsAsync(id, Array.Empty<DraftedRule>(), None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        h.Writes.Should().ContainSingle().Which.Fields["sprk_notconverted"].Should().Be("[]");
        h.Ownership.Verify(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never,
            "no row is written, so no owner is resolved");
        (await h.Service().GetDraftSourceAsync(id, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeTrue();
    }

    // ── AddDraftsAsync: idempotency ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddDrafts_AfterTheMarker_Is409AlreadyDrafted_AndWritesNothing()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h, notConverted: "[]");

        var outcome = await h.Service().AddDraftsAsync(id, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeAlreadyDrafted);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_ARepeatedRule_InOneRun_IsStoredOnce()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);

        var outcome = await h.Service().AddDraftsAsync(id, new[] { Increment(), Increment() }, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(1);
        outcome.Value.AlreadyPresent.Should().Be(1);
        h.Rows["sprk_policy"].Should().ContainSingle();
    }

    [Fact]
    public async Task AddDrafts_ARunThatFailedMidWay_IsCompletedByTheRepeat_WithNoRuleStoredTwice()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var rules = new[] { Increment(), Cap(10, "Max ten."), Cap(8, "Max eight.") };
        var service = h.Service();

        // The second rule's version cannot be created: its policy is removed again, the marker is not written.
        var versionCreates = 0;
        h.FailCreateWhen = e => e.LogicalName == "sprk_policyversion" && ++versionCreates == 2;
        var failed = await service.AddDraftsAsync(id, rules, None, CallerOid, CancellationToken.None);

        failed.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        failed.StatusCode.Should().Be(500);
        failed.ErrorCode.Should().Be(RuleSetService.CodeWriteFailed);
        h.Rows["sprk_policy"].Should().ContainSingle("the second rule's policy has no version and was removed");
        h.Writes.Where(w => w.Entity == "sprk_policy" && w.Kind == Write.Delete).Should().ContainSingle();
        h.Writes.Should().NotContain(w => w.Entity == "sprk_ruleset", "the marker is written only when every rule is stored");
        (await service.GetDraftSourceAsync(id, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeFalse();

        // The repeat (a redelivery) skips the stored rule and adds the other two.
        h.FailCreateWhen = null;
        var repeat = await service.AddDraftsAsync(id, rules, None, CallerOid, CancellationToken.None);

        repeat.Value!.Stored.Should().Be(2);
        repeat.Value.AlreadyPresent.Should().Be(1);
        h.Rows["sprk_policy"].Select(p => p.GetAttributeValue<string>("sprk_policycode")).Should().OnlyHaveUniqueItems().And.HaveCount(3);
        h.Rows["sprk_policyversion"].Should().HaveCount(3);
        (await service.GetDraftSourceAsync(id, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeTrue();
    }

    [Fact]
    public async Task AddDrafts_AVersionFailure_RemovesItsPolicy_AndStoresNoMarker()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        h.FailCreateWhen = e => e.LogicalName == "sprk_policyversion";

        var outcome = await h.Service().AddDraftsAsync(id, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(500);
        h.Rows["sprk_policy"].Should().BeEmpty("a policy with no version would count as a rule and show nowhere");
        h.Writes.Should().NotContain(w => w.Entity == "sprk_ruleset");
    }

    [Fact]
    public async Task AddDrafts_MoreRulesThanTheRunMaximum_ListsTheRest_ForTheAdministrator()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var rules = Enumerable.Range(0, RuleSetService.MaxDraftedRules + 1)
            .Select(i => Increment(quote: $"Clause {i}.")).ToList();

        var outcome = await h.Service().AddDraftsAsync(id, rules, None, CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(RuleSetService.MaxDraftedRules);
        outcome.Value.NotConverted.Should().ContainSingle().Which.Text.Should().Be($"Clause {RuleSetService.MaxDraftedRules}.");
    }

    // ── AddDraftsAsync: domain, state, owner ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(RuleDomains.PolicyDomainOntology)]
    public async Task AddDrafts_ARuleSetOfAnotherDomain_IsNotFound_AndNothingIsWritten(int? domain)
    {
        var h = new Harness();
        var id = h.RuleSet(domain: domain);

        var outcome = await h.Service().AddDraftsAsync(id, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(404);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_ARetiredRuleSet_Is409_AndNothingIsWritten()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h, status: RuleSetService.RuleSetRetired);

        var outcome = await h.Service().AddDraftsAsync(id, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeRetired);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_WhenTheOwnerCannotBeResolved_RefusesAndWritesNothing()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        h.OwnerAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team");

        var outcome = await h.Service().AddDraftsAsync(id, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddDrafts_NeverTouchesAnOntologyPolicyOfTheSameName()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var ontology = new Entity("sprk_policy", Guid.NewGuid());
        ontology["sprk_policycode"] = "ONT-1";
        ontology["sprk_ruleset"] = new EntityReference("sprk_ruleset", id); // even one wrongly linked to this set
        h.Seed(ontology);

        await h.Service().AddDraftsAsync(id, new[] { Increment() }, None, CallerOid, CancellationToken.None);

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
