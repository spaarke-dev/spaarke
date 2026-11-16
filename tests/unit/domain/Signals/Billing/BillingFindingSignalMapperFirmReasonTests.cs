using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060 design 3.2; uac-r2 C1, C3, C4, O1, O2): the mapper copies the cited version's firm description onto
/// <see cref="SignalWriteRequest.FirmReason"/> only after <see cref="FirmDescriptionRules"/> passed with that version's own rule set and
/// policy names. A text that fails is dropped with a code-only Warning and changes nothing else: the sentence, the headline, the evidence and
/// the fingerprint are those of the same group without a description, and the held fallback is decided without it. The stored Signal plan
/// never carries the text (C1).
/// </summary>
public class BillingFindingSignalMapperFirmReasonTests
{
    private const string HeldPrefix = "The rejection rule would reject this invoice: ";
    private const string FirmText = "Expenses of this kind are not reimbursed.";
    private static readonly Guid InvoiceRowId = Guid.Parse("130e0000-0000-4000-8000-0000000d00f1");
    private static readonly BillingSignalSubjects Subjects = new(InvoiceRowId, "INV-130", new Dictionary<int, Guid> { [0] = Id("line-row:130e:0") });

    private static BillingRuleCandidate ExpenseRule(string? firmDescription, string? template = "Expense {{line.expenseCode}} is not allowed.") =>
        Version("BILL-EXP", RuleType.ExpenseRule, template: template) with
        {
            Consequence = (int)BillingConsequence.Reject,
            Severity = SignalWriter.SeverityCritical,
            FirmDescription = firmDescription,
            PolicyName = "Expense Codes",
            RuleSetName = "Acme Billing Guidelines",
        };

    private static BillingFinding Finding(BillingRuleCandidate rule) => new()
    {
        RuleType = RuleType.ExpenseRule,
        PolicyId = rule.PolicyId,
        PolicyCode = rule.PolicyCode,
        PolicyVersionId = rule.PolicyVersionId,
        Subject = BillingSubject.ForLine(0),
        ReasonCode = BillingReasonCodes.ExpenseCodeNotReimbursable,
        ValuesRead = [new BillingValueRead { Name = "line.expenseCode", Value = "E102" }],
        FactValues = new Dictionary<string, string> { ["line.expenseCode"] = "E102" },
    };

    private static BillingSignalMapping Map(BillingRuleCandidate rule, SubmissionEvaluationKind kind = SubmissionEvaluationKind.AutoRejected, ILogger? logger = null)
    {
        var finding = Finding(rule);
        var rules = new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule };
        IReadOnlyList<FindingDecision> decisions =
        [
            new() { Index = 0, Finding = finding, Consequence = BillingConsequence.Reject, Rejects = true, ValuesRead = finding.ValuesRead },
        ];
        return BillingFindingSignalMapper.MapAll([finding], rules, Subjects, decisions, kind, logger).Single();
    }

    private static string Sentence(BillingSignalMapping m) => SignalWriter.RenderSentence(m.Request.MessageTemplate, m.Request.FactValues);

    [Fact(DisplayName = "130e-1 must-fire: a valid firm description of the cited version reaches the request, trimmed")]
    public void AValidDescription_ReachesTheRequest()
    {
        Map(ExpenseRule("  " + FirmText + " ")).Request.FirmReason.Should().Be(FirmText);
        Map(ExpenseRule(FirmText), SubmissionEvaluationKind.Blocked).Request.FirmReason.Should().Be(FirmText, "a held group that fits takes it too");
    }

    [Theory(DisplayName = "130e-1 must-not-fire: no description (null, empty, blank) gives no firm reason and no log")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t ")]
    public void NoDescription_GivesNoFirmReason(string? description)
    {
        var logger = new CapturingLogger<BillingFindingSignalMapperFirmReasonTests>();

        Map(ExpenseRule(description), logger: logger).Request.FirmReason.Should().BeNull();
        logger.Entries.Should().BeEmpty();
    }

    [Theory(DisplayName = "130e-1 C3/C4: an invalid or name-bearing description is dropped with a code-only Warning; the sentence, headline, evidence and fingerprint are unchanged")]
    [InlineData("Not under the Acme Billing Guidelines.", FirmDescriptionRules.NamesTheRule)]
    [InlineData("See EXPENSE CODES.", FirmDescriptionRules.NamesTheRule)]
    [InlineData("Rate {{line.rate}} too high.", FirmDescriptionRules.Braces)]
    [InlineData("<b>No.</b>", FirmDescriptionRules.Markup)]
    [InlineData("No\u202E.", FirmDescriptionRules.InvisibleCharacter)]
    public void AnInvalidDescription_IsDropped_AndChangesNothingElse(string description, string reason)
    {
        var logger = new CapturingLogger<BillingFindingSignalMapperFirmReasonTests>();
        var baseline = Map(ExpenseRule(null));

        var dropped = Map(ExpenseRule(description), logger: logger);

        dropped.Request.FirmReason.Should().BeNull();
        dropped.Request.Should().BeEquivalentTo(baseline.Request, "the Signal is kept as it would be without a description");
        dropped.Fingerprint.Should().Be(baseline.Fingerprint);
        SignalWriter.PreflightRefusal(dropped.Request).Should().BeNull("the mapper never hands the writer a text it would refuse");
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(FirmDescriptionRules.DroppedLogCode).And.Contain(reason).And.NotContain(description);
    }

    [Fact(DisplayName = "130e-1 O2 must-fire: under a Blocked outcome an invalid description never decides the held fallback: the deciding reason is still cited")]
    public void AnInvalidDescription_NeverTakesTheHeldFallback()
    {
        var held = Map(ExpenseRule("Rate {x} too high."), SubmissionEvaluationKind.Blocked);

        Sentence(held).Should().Be(HeldPrefix + "Expense E102 is not allowed.");
        Sentence(held).Should().NotBe(BillingFindingSignalMapper.HeldSentenceNoValue);
        held.Request.FirmReason.Should().BeNull();
    }

    [Fact(DisplayName = "130e-1 O2: the held fallback request (the reason would not fit the sentence) carries no firm reason, even a valid one")]
    public void TheHeldFallbackRequest_CarriesNoFirmReason()
    {
        var tooLong = new string('a', 2000 - HeldPrefix.Length + 1);

        var held = Map(ExpenseRule(FirmText, template: tooLong), SubmissionEvaluationKind.Blocked);

        Sentence(held).Should().Be(BillingFindingSignalMapper.HeldSentenceNoValue);
        held.Request.FirmReason.Should().BeNull();
    }

    [Fact(DisplayName = "130e-1 O1: the firm reason is in neither the fingerprint, the sentence, the headline, the facts nor the evidence")]
    public void TheFirmReason_IsInNoKeyOrSentence()
    {
        var without = Map(ExpenseRule(null));
        var with = Map(ExpenseRule(FirmText));

        with.Fingerprint.Should().Be(without.Fingerprint);
        with.Request.Should().BeEquivalentTo(without.Request, o => o.Excluding(r => r.FirmReason));
        Sentence(with).Should().NotContain(FirmText);
        with.Request.EvidenceRefs!.Should().NotContain(e => (e.Text ?? string.Empty).Contains(FirmText) || e.Ref.Contains(FirmText));
    }

    [Fact(DisplayName = "130e-1 C1: the stored Signal plan entry never carries the firm reason, and a firmReason planted in an entry is not read back")]
    public void ThePlan_NeverCarriesTheFirmReason()
    {
        var mapping = Map(ExpenseRule(FirmText));
        mapping.Request.FirmReason.Should().Be(FirmText);

        var entry = InvoiceSubmissionPersister.SignalPlanEntryJson(mapping);

        entry.ToJsonString().Should().NotContain(FirmText);
        entry.ContainsKey("firmReason").Should().BeFalse();

        entry["firmReason"] = "PLANTED text.";
        InvoiceSubmissionAfterSteps.StoredFindings.ParsePlanEntry(entry).Request.FirmReason.Should().BeNull("the replay sets it from the version (BillingFirmReasonReplay)");
    }
}
