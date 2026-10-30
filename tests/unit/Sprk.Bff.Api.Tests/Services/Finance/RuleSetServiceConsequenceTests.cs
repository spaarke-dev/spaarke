using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 073 (spec FR-26, I-5, D-89) through <see cref="RuleSetService"/>: a rule whose type may not carry its consequence
/// (Reject on TimeIncrement, DailyHoursCap, NonBillableActivity, AiObservation or ApprovedTimekeeper; Suggest adjustment on a type that
/// suggests none) cannot be accepted, edited into, drafted or published. A refused publish writes nothing, and it reaches the admin
/// as a 400 with the stable <c>ruleset.publish_invalid</c> code and the reason <c>consequence_not_allowed</c>. Reuses the 076 harness
/// (real validation over a fake store that evaluates the real queries).
/// </summary>
public sealed class RuleSetServiceConsequenceTests
{
    private const int Reject = (int)BillingConsequence.Reject;
    private const int Flag = (int)BillingConsequence.Flag;
    private const int Suggest = (int)BillingConsequence.SuggestAdjustment;
    private const string Drive = "b!procedurelibrary";
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly DateOnly Effective = new(2026, 11, 1);

    public static TheoryData<RuleType> RejectRefusedTypes => new()
    {
        RuleType.TimeIncrement, RuleType.DailyHoursCap, RuleType.NonBillableActivity, RuleType.AiObservation, RuleType.ApprovedTimekeeper,
    };

    public static TheoryData<RuleType> RejectAllowedTypes => new()
    {
        RuleType.ApprovedRate, RuleType.ExpenseRule, RuleType.InvoiceAge, RuleType.CoreRecordWindow, RuleType.Duplicate, RuleType.RequiredData,
    };

    public static TheoryData<RuleType> EveryBillingType => new(BillingRuleCatalogue.Types);

    /// <summary>A rule of <paramref name="type"/> with its catalogue-valid body and default wording, stored with <paramref name="consequence"/>.</summary>
    private static (Guid Policy, Guid Version) Stored(Harness h, Guid set, RuleType type, int? consequence, ProposalState state, string? code = null)
    {
        var (policy, version) = h.Rule(
            set, code ?? "BILL-" + type, state, type: type.ToString(), body: Domain.Signals.Billing.BillingEvaluationKit.Bodies[type], consequence: consequence);
        h.Row("sprk_policyversion", version)["sprk_messagetemplate"] = BillingRuleDraftTranslator.Wording(type).Template;
        return (policy, version);
    }

    // ── Publish ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RejectRefusedTypes))]
    public async Task Publish_AnAcceptedRuleWhoseTypeMayNotReject_IsRefused_ForTheWholeSet_AndWritesNothing(RuleType type)
    {
        var h = new Harness();
        var set = h.RuleSet();
        Stored(h, set, RuleType.InvoiceAge, Reject, ProposalState.Accepted, "BILL-GOOD");
        var (bad, _) = Stored(h, set, type, Reject, ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("ruleset.publish_invalid");
        outcome.Errors.Should().ContainSingle().Which.Should().Match<RuleSetError>(
            e => e.PolicyId == bad && e.Field == "consequence" && e.Reason == OntologyWriterFailureReason.ConsequenceNotAllowed);
        h.Writes.Should().BeEmpty("one refused rule publishes nothing, not even the good one");
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetDraft);
    }

    [Fact]
    public async Task Publish_ALineLevelDuplicateWithReject_IsRefused_AndWritesNothing_OwnerQuestion3()
    {
        // 073 review, owner question 3: matchFields without firmInvoiceNumber compare lines only, and a duplicate line never rejects.
        var h = new Harness();
        var set = h.RuleSet();
        var (bad, version) = h.Rule(
            set, "BILL-DUP-LINES", ProposalState.Accepted, type: nameof(RuleType.Duplicate),
            body: """{"type":"Duplicate","matchFields":["timekeeper","lineDate","hours"]}""", consequence: Reject);
        h.Row("sprk_policyversion", version)["sprk_messagetemplate"] = BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template;

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Should().Match<RuleSetError>(
            e => e.PolicyId == bad && e.Field == "consequence" && e.Reason == OntologyWriterFailureReason.ConsequenceNotAllowed
                 && e.Message.Contains("firmInvoiceNumber"));
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RejectAllowedTypes))]
    public async Task Publish_AnAcceptedRejectRule_OfAnEligibleType_Publishes(RuleType type)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (_, version) = Stored(h, set, type, Reject, ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Published.Should().ContainSingle().Which.VersionId.Should().Be(version);
    }

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public async Task Publish_PublishesExactlyTheConsequencesTheCatalogueAllows(RuleType type)
    {
        foreach (var consequence in new[] { Reject, Flag, Suggest })
        {
            var h = new Harness();
            var set = h.RuleSet();
            Stored(h, set, type, consequence, ProposalState.Accepted);

            var outcome = await h.Service().PublishAsync(set, Effective, default);

            var allowed = BillingRuleCatalogue.IsConsequenceAllowed(type, consequence);
            outcome.Kind.Should().Be(allowed ? RuleSetOutcomeKind.Done : RuleSetOutcomeKind.Invalid, $"{type} with {(BillingConsequence)consequence}");
            if (!allowed)
            {
                h.Writes.Should().BeEmpty();
            }
        }
    }

    [Fact]
    public async Task Publish_AProposedRuleWithAnUnallowedConsequence_DoesNotBlockThePublish_BecauseItIsNotPublished()
    {
        // Only accepted rules go into force; a proposal nobody accepted is counted and left alone (it cannot be accepted as it stands).
        var h = new Harness();
        var set = h.RuleSet();
        var (_, good) = Stored(h, set, RuleType.InvoiceAge, Reject, ProposalState.Accepted, "BILL-GOOD");
        Stored(h, set, RuleType.AiObservation, Reject, ProposalState.Proposed, "BILL-PROPOSED");

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Published.Should().ContainSingle().Which.VersionId.Should().Be(good);
        outcome.Value.NotPublishedProposed.Should().Be(1);
    }

    [Fact]
    public async Task APublishRefusedForAConsequence_ReachesTheAdminAs400_WithTheStableCode_TheReason_AndTheRuleThatCausedIt()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (bad, _) = Stored(h, set, RuleType.AiObservation, Reject, ProposalState.Accepted);
        var real = await h.Service().PublishAsync(set, Effective, default);
        await using var host = await RuleSetEndpointsTests.Host.StartAsync();
        host.Service.Setup(s => s.PublishAsync(set, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(real);

        var response = await host.Client.SendAsync(RateCardImportEndpointsTests.Req(
            "POST", $"/api/finance/rulesets/{set}/publish", content: JsonContent.Create(new { effectiveDate = "2026-11-01" })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body["errorCode"]!.GetValue<string>().Should().Be("ruleset.publish_invalid");
        var error = body["errors"]!.AsArray().Should().ContainSingle().Subject!;
        error["policyId"]!.GetValue<Guid>().Should().Be(bad);
        error["field"]!.GetValue<string>().Should().Be("consequence");
        error["reason"]!.GetValue<string>().Should().Be("consequence_not_allowed");
        error["message"]!.GetValue<string>().Should().Contain("AiObservation").And.Contain("I-5");
        h.Writes.Should().BeEmpty();
    }

    // ── Accept and edit ──────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RejectRefusedTypes))]
    public async Task Accept_ARuleStoredWithAConsequenceItsTypeMayNotCarry_IsRefused_ButItCanStillBeRejected(RuleType type)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = Stored(h, set, type, Reject, ProposalState.Proposed);
        var service = h.Service();

        var accepted = await service.DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);

        accepted.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        accepted.Errors.Should().ContainSingle(e => e.Field == "consequence" && e.Reason == "consequence_not_allowed");
        h.Writes.Should().BeEmpty();
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);

        var declined = await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: "This rule cannot reject.");
        declined.Kind.Should().Be(RuleSetOutcomeKind.Done, "a draft the admin cannot accept can still be turned down");
    }

    [Fact]
    public async Task Edit_IntoAConsequenceTheTypeMayNotCarry_IsRefused_AndChangesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = Stored(h, set, RuleType.AiObservation, Flag, ProposalState.Accepted);
        var service = h.Service();

        foreach (var consequence in new[] { "Reject", "SuggestAdjustment" })
        {
            var outcome = await service.EditAsync(set, policy, new ProposalEdit(null, null, null, null, consequence, null, null, CallerOid), default);

            outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid, consequence);
            outcome.Errors.Should().ContainSingle(e => e.Field == "consequence" && e.Reason == "consequence_not_allowed");
        }

        h.Writes.Should().BeEmpty();
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_consequence").Value.Should().Be(Flag);
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Accepted);
    }

    [Fact]
    public async Task Edit_IntoAConsequenceTheTypeMayCarry_IsAccepted()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = Stored(h, set, RuleType.InvoiceAge, Flag, ProposalState.Proposed);

        var outcome = await h.Service().EditAsync(set, policy, new ProposalEdit(null, null, null, null, "Reject", null, null, CallerOid), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Proposal.Consequence.Should().Be("Reject");
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_consequence").Value.Should().Be(Reject);
    }

    // ── Drafting (075): a model's proposal is validated before it is stored ──────────────────────────────────────

    private static Guid RuleSetWithSource(Harness h)
    {
        var id = h.RuleSet();
        h.Row("sprk_ruleset", id)["sprk_sourcedocument"] = new EntityReference("sprk_document", SourceId);
        h.Documents.Setup(d => d.GetDocumentAsync(SourceId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Spaarke.Dataverse.DocumentEntity { Id = SourceId.ToString(), Name = "guidelines", GraphDriveId = Drive, GraphItemId = "item-1", FileName = "guidelines.pdf" });
        return id;
    }

    private static DraftedRule Drafted(RuleType type, string consequence, string quote)
    {
        var (headline, template) = BillingRuleDraftTranslator.Wording(type);
        return new DraftedRule(type.ToString(), Domain.Signals.Billing.BillingEvaluationKit.Bodies[type], template, headline, consequence, quote, "Section 1");
    }

    // 073 review F1-b: there is no route to create a rule by hand, so a drafted rule whose ONLY problem is a consequence its type may not
    // carry is kept as a Proposed Flag rule with a note, not dropped into the not-converted list.

    [Theory]
    [InlineData(RuleType.ApprovedTimekeeper, "Reject", "D-89")]
    [InlineData(RuleType.AiObservation, "Reject", "I-5")]
    [InlineData(RuleType.TimeIncrement, "Reject", "FR-26")]
    [InlineData(RuleType.TimeIncrement, "SuggestAdjustment", "design.md 6.1")]
    public async Task AddDrafts_AProposalWhoseOnlyProblemIsTheConsequence_IsStoredAsAProposedFlagRule_WithANote_F1b(
        RuleType type, string drafted, string citation)
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var kept = Drafted(RuleType.InvoiceAge, "Reject", "Invoices older than 90 days will be rejected.");
        var lowered = Drafted(type, drafted, "Invoices that break this will be returned.");

        var outcome = await h.Service().AddDraftsAsync(id, new[] { kept, lowered }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(2, "the clause is not lost");
        outcome.Value.NotConverted.Should().BeEmpty();
        var row = h.Rows["sprk_policyversion"].Single(v => v.GetAttributeValue<string>("sprk_clausereference") == "Invoices that break this will be returned.");
        row.GetAttributeValue<OptionSetValue>("sprk_consequence").Value.Should().Be(Flag);
        row.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);
        var note = row.GetAttributeValue<string>("sprk_proposedaction");
        note.Should().StartWith("billing-flag").And.Contain($"drafted as {drafted}").And.Contain("lowered to Flag").And.Contain(citation);
        note.Length.Should().BeLessThanOrEqualTo(400, "sprk_proposedaction is text 400");

        // The untouched Reject rule keeps its code, and the lowered rule passes the review's own validation (it can be accepted).
        h.Rows["sprk_policyversion"].Single(v => v.GetAttributeValue<string>("sprk_clausereference") == "Invoices older than 90 days will be rejected.")
            .GetAttributeValue<string>("sprk_proposedaction").Should().Be("billing-reject");
        var detail = await h.Service().GetAsync(id, CancellationToken.None);
        detail.Value!.Proposals.Should().OnlyContain(p => p.ValidationErrors.Count == 0);
    }

    [Fact]
    public async Task AddDrafts_ALineLevelDuplicateDraftedAsReject_IsLoweredToFlag_OwnerQuestion3()
    {
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var (headline, template) = BillingRuleDraftTranslator.Wording(RuleType.Duplicate);
        var lines = new DraftedRule(
            nameof(RuleType.Duplicate), """{"type":"Duplicate","matchFields":["timekeeper","lineDate","hours"]}""", template, headline,
            "Reject", "Duplicate entries will be rejected.", "Section 2");

        var outcome = await h.Service().AddDraftsAsync(id, new[] { lines }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(1);
        var row = h.Rows["sprk_policyversion"].Should().ContainSingle().Subject;
        row.GetAttributeValue<OptionSetValue>("sprk_consequence").Value.Should().Be(Flag);
        row.GetAttributeValue<string>("sprk_proposedaction").Should().Contain("drafted as Reject").And.Contain("firmInvoiceNumber");
    }

    [Fact]
    public async Task AddDrafts_AProposalWithAnInvalidBody_IsStillNotConverted_EvenWhenItsConsequenceIsAlsoDisallowed()
    {
        // Lowering applies only when the consequence is the ONLY problem; an invalid body is a real refusal (body-first precedence).
        var h = new Harness();
        var id = RuleSetWithSource(h);
        var (headline, template) = BillingRuleDraftTranslator.Wording(RuleType.TimeIncrement);
        var broken = new DraftedRule(
            nameof(RuleType.TimeIncrement), """{"type":"TimeIncrement","incrementHours":"a tenth"}""", template, headline, "Reject",
            "Time must be billed in tenths.", "Section 4");

        var outcome = await h.Service().AddDraftsAsync(id, new[] { broken }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);

        outcome.Value!.Stored.Should().Be(0);
        h.Rows.GetValueOrDefault("sprk_policyversion").Should().BeNullOrEmpty();
        outcome.Value.NotConverted.Should().ContainSingle().Which.Reason.Should().StartWith("Not stored: the drafted rule was not valid").And.Contain("ruleBody");
    }

    [Fact]
    public void TheLoweredNote_IsCappedAtTheColumnLength()
    {
        var note = RuleSetService.LoweredConsequenceNote(BillingConsequence.Reject, new string('x', 1_000));

        note.Should().StartWith("billing-flag: drafted as Reject, lowered to Flag.").And.EndWith("...");
        note.Length.Should().Be(400);
    }
}
