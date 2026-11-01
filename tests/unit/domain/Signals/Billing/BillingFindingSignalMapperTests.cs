using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 072: <see cref="BillingFindingSignalMapper"/>. Every mapped request passes the writer's template and fact checks
/// (<see cref="SignalWriter.RenderSentence"/>), whatever tokens a finding lacks; an invoice-level Duplicate with the default template
/// is written; a withheld Duplicate (D-112) puts no withheld value into any field of the written Signal (071 fix round 2).
/// </summary>
public class BillingFindingSignalMapperTests
{
    private static readonly Guid InvoiceRowId = Guid.Parse("07200000-0000-4000-8000-0000000000e1");
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static BillingSignalSubjects SubjectsFor(BillingEvaluationRequest request) => new(
        InvoiceRowId,
        "INV-100",
        request.Lines.ToDictionary(l => l.Index, l => Id("line-row:" + l.Index)));

    private static IReadOnlyList<BillingSignalMapping> MapAll(BillingEvaluationRequest request, BillingEvaluationResult result) =>
        BillingFindingSignalMapper.MapAll(result, SubjectsFor(request));

    public static TheoryData<string> TemplateChoices => new() { "blank", "default wording" };

    [Theory]
    [MemberData(nameof(TemplateChoices))]
    public void EveryFindingOfEveryType_MapsToARequestTheWriterAccepts(string templates)
    {
        var (request, loaded) = EverythingWrong(20);
        loaded = loaded with
        {
            Rules = loaded.Rules.Select(r => r with
            {
                MessageTemplate = templates == "blank" ? null : BillingRuleDraftTranslator.Wording(Enum.Parse<RuleType>(r.RuleType!)).Template,
            }).ToList(),
        };
        var result = Evaluator().Evaluate(request, loaded);
        result.Findings.Select(f => f.RuleType).Distinct().Should().HaveCount(13);

        var mappings = MapAll(request, result);
        mappings.SelectMany(m => m.Findings).Should().BeEquivalentTo(result.Findings, "every finding is carried by exactly one request");
        foreach (var mapping in mappings)
        {
            var sent = mapping.Request;
            var finding = mapping.Findings.First(f => f.PolicyVersionId == sent.PolicyVersionId);
            var render = () => SignalWriter.RenderSentence(sent.MessageTemplate, sent.FactValues);
            render.Should().NotThrow($"{finding.RuleType} {finding.ReasonCode} must render");
            sent.FactValues.Keys.Should().BeSubsetOf(BillingRuleCatalogue.TemplateFactFields(finding.RuleType));
            sent.ShortHeadline.Should().NotBeNullOrWhiteSpace();
            sent.ShortHeadline.Length.Should().BeLessThanOrEqualTo(200);
            sent.PolicyCode.Length.Should().BeLessThanOrEqualTo(50);
            sent.Lane.Should().Be(SignalWriter.LaneDecide, "a policy with no lane is a Decide item");
            sent.PolicyVersionId.Should().Be(finding.PolicyVersionId);
            if (finding.Subject.Kind == BillingSubjectKind.Invoice)
            {
                sent.SubjectEntityLogicalName.Should().Be("sprk_invoice");
                sent.SubjectId.Should().Be(InvoiceRowId);
            }
            else
            {
                sent.SubjectEntityLogicalName.Should().Be("sprk_billingevent");
                sent.SubjectId.Should().Be(Id("line-row:" + finding.Subject.LineIndex));
            }
        }
    }

    [Fact]
    public void TheVersionsTemplate_IsUsed_WhenEveryTokenHasAValue()
    {
        var rule = Version("CD-INCREMENT", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":0.5}""",
            template: "{{line.hours}} hours is not a multiple of {{rule.incrementHours}}.");
        var request = Request(Fee(0) with { Hours = 0.3m });
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.MessageSource.Should().Be(BillingMessageSource.PolicyVersion);
        SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues)
            .Should().Be("0.3 hours is not a multiple of 0.5.");
    }

    [Fact]
    public void ATemplateNamingAValueTheFindingLacks_FallsBack_SoTheFindingIsNeverRefused()
    {
        // No billed timekeeper name: the ApprovedTimekeeper finding has no line.timekeeperName, which both the version's template and
        // the default wording name.
        var rule = Version("CD-TIMEKEEPER", RuleType.ApprovedTimekeeper,
            template: "{{line.timekeeperName}} is not approved on {{line.date}}.");
        var request = Request(Fee(0) with { TimekeeperName = null, Timekeeper = Unmatched(Sprk.Bff.Api.Services.Finance.Resolution.TimekeeperUnmatchedReason.NameMissing) });
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.MessageSource.Should().Be(BillingMessageSource.HeadlineOnly);
        SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues).Should().Be("Timekeeper not approved.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("The {{missingField}} must be on every invoice.")]
    public void ARequiredDataForeignCurrency_SaysTheCurrencyIsNotUsd_NotThatItIsMissing_OntologyF1(string? versionTemplate)
    {
        // ontology-r1 072 review F1: the type's default wording rendered "The required data currency is missing." although EUR was read.
        var rule = Version("CD-REQUIRED", RuleType.RequiredData, """{"type":"RequiredData","fields":["taskCode"]}""", template: versionTemplate);
        var request = Request(Fee(0)) with { Currency = "EUR" };
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.MessageSource.Should().Be(BillingMessageSource.ReasonTemplate);
        SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues)
            .Should().Be("The invoice currency is not USD.");
        // ontology-r1 F2: the title (sprk_name) must not say "Required data missing" for a currency that was read.
        mapping.Request.ShortHeadline.Should().Be(BillingRuleCatalogue.CurrencyNotUsdHeadline);
    }

    [Fact]
    public void AFlagOnlyReason_TakesTheFlagLevelLaneAndSeverity_NotTheHostRules_OntologyF2()
    {
        var rule = Version("CD-REQUIRED", RuleType.RequiredData, """{"type":"RequiredData","fields":["taskCode"]}""", lane: SignalWriter.LaneDo)
            with { Severity = SignalWriter.SeverityCritical, ShortHeadline = "Firm authored headline", Consequence = Reject };
        var request = Request(Fee(0)) with { Currency = "EUR" };
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var sent = MapAll(request, result).Should().ContainSingle().Subject.Request;

        sent.ShortHeadline.Should().Be(BillingRuleCatalogue.CurrencyNotUsdHeadline, "not the rule's authored headline");
        sent.Lane.Should().Be(SignalWriter.LaneDecide);
        sent.Severity.Should().Be(SignalWriter.SeverityInfo, "a flag-only note never ranks like a Critical breach");
    }

    [Fact]
    public void InAFlagTie_ARealBreachIsThePrimary_NotAFlagOnlyNote_OntologyWRecheckF2()
    {
        // currency_not_usd sorts before missing_field; both are Flag here, and the author's Do / Critical ranking belongs to the breach.
        var rule = Version("CD-REQUIRED", RuleType.RequiredData, """{"type":"RequiredData","fields":["billingPeriodStart"]}""", lane: SignalWriter.LaneDo)
            with { Severity = SignalWriter.SeverityCritical, ShortHeadline = "Firm authored headline", Consequence = (int)BillingConsequence.Flag };
        var request = Request(Fee(0)) with { BillingPeriodStart = null, Currency = "EUR" };
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.ReasonCodes.Should().Equal(BillingReasonCodes.CurrencyNotUsd, BillingReasonCodes.RequiredFieldMissing);
        mapping.Consequence.Should().Be(BillingConsequence.Flag);
        mapping.Request.ShortHeadline.Should().Be("Firm authored headline", "the missing field is the breach, not the currency note");
        mapping.Request.Lane.Should().Be(SignalWriter.LaneDo);
        mapping.Request.Severity.Should().Be(SignalWriter.SeverityCritical);
        mapping.Request.MessageTemplate.Should().NotBe(BillingRuleCatalogue.CurrencyNotUsdTemplate);
        mapping.Request.EvidenceRefs!.Should().Contain(e => e.Kind == BillingFindingSignalMapper.ReasonEvidenceKind
                                                            && e.Text == BillingRuleCatalogue.CurrencyNotUsdTemplate, "the note is still carried");
    }

    [Fact]
    public void ARealBreach_KeepsTheRulesHeadlineLaneAndSeverity_OntologyF2()
    {
        // Control for the test above: only a flag-only reason is moved to the Flag level.
        var rule = Version("CD-REQUIRED", RuleType.RequiredData, """{"type":"RequiredData","fields":["taskCode"]}""", lane: SignalWriter.LaneDo)
            with { Severity = SignalWriter.SeverityCritical, ShortHeadline = "Firm authored headline", Consequence = Reject };
        var request = Request(Fee(0) with { BilledTaskCode = null });
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var sent = MapAll(request, result).Should().ContainSingle().Subject.Request;

        sent.ShortHeadline.Should().Be("Firm authored headline");
        sent.Lane.Should().Be(SignalWriter.LaneDo);
        sent.Severity.Should().Be(SignalWriter.SeverityCritical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("blank")]
    [InlineData("default wording")]
    public void AClosedCoreRecord_SaysTheRecordIsClosedToBilling_AndNamesNoDates_F1a(string? templates)
    {
        // Closed matter, opened 2026-01-01, closing 2026-12-31, line dated 2026-03-01: the line is INSIDE both dates. The version's
        // template and the type's default wording say "outside the open date ... and close date ...", which is false here.
        var rule = Version("CD-WINDOW", RuleType.CoreRecordWindow, template: templates switch
        {
            "blank" => " ",
            "default wording" => BillingRuleDraftTranslator.Wording(RuleType.CoreRecordWindow).Template,
            _ => "The line dated {{line.date}} is outside {{coreRecord.openDate}} to {{coreRecord.closeDate}}.",
        }) with { Consequence = Reject };
        var request = Request(Fee(0) with { Date = new DateOnly(2026, 3, 1) });
        var loaded = Loaded(rule) with
        {
            CoreRecord = new BillingCoreRecordFacts
            {
                OpenDate = new DateOnly(2026, 1, 1), CloseDate = new DateOnly(2026, 12, 31), BillingStatus = CoreRecordBillingStatus.Closed,
            },
        };
        var result = Evaluator().Evaluate(request, loaded);
        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.CoreRecordClosedToBilling);

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.MessageSource.Should().Be(BillingMessageSource.ReasonTemplate);
        var sentence = SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues);
        sentence.Should().Be(BillingRuleCatalogue.ClosedToBillingTemplate);
        sentence.Should().NotContain("2026", "the line is inside the dates; a date in the sentence would say the opposite").And.NotContain("outside");
        mapping.Request.MessageTemplate.Should().NotContain("{{", "the reason's own message names no token");
        mapping.Request.ShortHeadline.Should().Be(BillingRuleCatalogue.ClosedToBillingHeadline, "the title must not be the date rule's either");
    }

    [Fact]
    public void AFinalCoreRecordWithNoCloseDate_SaysThereIsNoCloseDate_NotThatTheLineIsOutsideOne_F1a()
    {
        var rule = Version("CD-WINDOW", RuleType.CoreRecordWindow,
            template: BillingRuleDraftTranslator.Wording(RuleType.CoreRecordWindow).Template) with { Consequence = Reject };
        var request = Request(Fee(0));
        var loaded = Loaded(rule) with
        {
            CoreRecord = new BillingCoreRecordFacts { OpenDate = new DateOnly(2026, 1, 1), BillingStatus = CoreRecordBillingStatus.Final },
        };
        var result = Evaluator().Evaluate(request, loaded);
        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.CoreRecordClosed);

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.MessageSource.Should().Be(BillingMessageSource.ReasonTemplate);
        var sentence = SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues);
        sentence.Should().Be(BillingRuleCatalogue.NoCloseDateTemplate);
        sentence.Should().Contain("no close date").And.NotContain("outside").And.NotContain("2026");
        mapping.Request.ShortHeadline.Should().Be(BillingRuleCatalogue.NoCloseDateHeadline);
    }

    [Fact]
    public void TheDateReasons_StillUseTheVersionsTemplate_F1a()
    {
        // Control: before_open / after_close really are about the dates, so they keep the version's template.
        var rule = Version("CD-WINDOW", RuleType.CoreRecordWindow,
            template: BillingRuleDraftTranslator.Wording(RuleType.CoreRecordWindow).Template) with { Consequence = Reject };
        var request = Request(Fee(0) with { Date = new DateOnly(2026, 3, 2) });
        var loaded = Loaded(rule) with
        {
            CoreRecord = new BillingCoreRecordFacts
            {
                OpenDate = new DateOnly(2026, 3, 10), CloseDate = new DateOnly(2026, 12, 31), BillingStatus = CoreRecordBillingStatus.Open,
            },
        };
        var result = Evaluator().Evaluate(request, loaded);

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.BeforeCoreRecordOpened);
        mapping.MessageSource.Should().Be(BillingMessageSource.PolicyVersion);
        SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues).Should().Contain("2026-03-02").And.Contain("2026-03-10");
        mapping.Request.ShortHeadline.Should().Be(BillingRuleDraftTranslator.Wording(RuleType.CoreRecordWindow).Headline);
    }

    [Fact]
    public void AFindingOnALineWithNoRow_IsRefused()
    {
        var (request, loaded) = EverythingWrong(3);
        var result = Evaluator().Evaluate(request, loaded);
        var lineFinding = result.Findings.First(f => f.Subject.Kind == BillingSubjectKind.Line);

        var act = () => BillingFindingSignalMapper.Map(lineFinding, result.RulesByVersion[lineFinding.PolicyVersionId],
            new BillingSignalSubjects(InvoiceRowId, "INV-100", new Dictionary<int, Guid>()));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AFindingMappedWithAnotherVersion_IsRefused()
    {
        var (request, loaded) = EverythingWrong(3);
        var result = Evaluator().Evaluate(request, loaded);
        var finding = result.Findings[0];
        var other = result.RulesByVersion.Values.First(v => v.PolicyVersionId != finding.PolicyVersionId);

        var act = () => BillingFindingSignalMapper.Map(finding, other, SubjectsFor(request));

        act.Should().Throw<ArgumentException>();
    }

    // ── Through the real writer ──────────────────────────────────────────────────────────────────────────────────

    private static (SignalWriter Writer, SignalWriterTests.FakeOrganizationService Rows) Writer(BillingSignalSubjects subjects)
    {
        var sysadmin = new SignalWriterTests.FakeSysadminClient()
            .WithMatterBusinessUnit(SignalWriterTests.MatterId, SignalWriterTests.BusinessUnitId)
            .Stub(new Entity("sprk_invoice", subjects.InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", SignalWriterTests.MatterId) });
        foreach (var lineId in subjects.LineIds.Values)
        {
            sysadmin.Stub(new Entity("sprk_billingevent", lineId) { ["sprk_matter"] = new EntityReference("sprk_matter", SignalWriterTests.MatterId) });
        }

        var org = new SignalWriterTests.FakeOrganizationService();
        return (SignalWriterTests.Build(org, sysadmin, new FakeTimeProvider(Now)), org);
    }

    [Fact]
    public async Task AnInvoiceLevelDuplicate_WithTheDefaultTemplate_IsWritten_071FixRound2()
    {
        // The default Duplicate template names line tokens an invoice-level finding cannot have; the writer would refuse it.
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate, """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""",
            template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template);
        var request = Request(Fee(0));
        var loaded = Loaded(rule) with { PriorSubmissions = [Prior("INV-100.1", "INV-100", 1)] };
        var result = Evaluator().Evaluate(request, loaded);
        var subjects = SubjectsFor(request);
        var mapping = BillingFindingSignalMapper.Map(result.Findings.Should().ContainSingle().Subject, rule, subjects);
        var (writer, org) = Writer(subjects);

        var written = await writer.WriteAsync(mapping.Request);

        mapping.MessageSource.Should().Be(BillingMessageSource.InvoiceLevelDuplicate);
        var row = org.Rows[written.SignalId];
        row.GetAttributeValue<string>("sprk_sentence").Should().Be("Invoice INV-100 duplicates INV-100.1.");
        row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(InvoiceRowId);
    }

    private const string EarlierInvoiceText = "0abcdef0-1234-4567-89ab-cdef01234567";

    [Fact]
    public async Task AVisibleDuplicate_StoresTheEarlierInvoiceId_AsEvidenceOnly_NotInTheSnapshotOrTheSentence_D176()
    {
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate, """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""",
            template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template);
        var request = Request(Fee(0));
        var prior = Prior("INV-100.1", "INV-100", 1) with { InvoiceId = Guid.Parse(EarlierInvoiceText) };
        var result = Evaluator().Evaluate(request, Loaded(rule) with { PriorSubmissions = [prior] });
        var subjects = SubjectsFor(request);
        var mapping = BillingFindingSignalMapper.Map(result.Findings.Should().ContainSingle().Subject, rule, subjects);
        var (writer, org) = Writer(subjects);

        var written = await writer.WriteAsync(mapping.Request);

        mapping.Request.EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.ReferenceEvidenceKind && e.Ref == "duplicateOf.invoiceId")
            .Which.Text.Should().Be(EarlierInvoiceText);
        mapping.Request.EvidenceRefs!.Should().Contain(e => e.Ref == "duplicateOf.trackingNumber" && e.Text == "INV-100.1");
        mapping.Request.FactValues.Keys.Should().NotContain("duplicateOf.invoiceId");
        var row = org.Rows[written.SignalId];
        row.GetAttributeValue<string>("sprk_evidencerefs").Should().Contain(EarlierInvoiceText);
        row.GetAttributeValue<string>("sprk_factsnapshot").Should().NotContain(EarlierInvoiceText);
        row.GetAttributeValue<string>("sprk_sentence").Should().Be("Invoice INV-100 duplicates INV-100.1.");
    }

    [Fact]
    public void AWithheldDuplicate_StoresNoEarlierInvoiceId_InItsEvidence_D176()
    {
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate,
            template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template);
        var request = Request(Fee(0));
        var acrossTheWall = Prior("INV-100.1", "INV-100", 1, BillingPriorStatus.Active,
            new BillingPriorLine { Date = LineDate, TimekeeperId = Matched().TimekeeperId, Hours = 1.0m }) with
        {
            SecureRoots = Roots(Id("secure-matter")),
            InvoiceId = Guid.Parse(EarlierInvoiceText),
        };
        var result = Evaluator().Evaluate(request, Loaded(rule) with { PriorSubmissions = [acrossTheWall] });
        result.Findings.Should().HaveCount(2).And.OnlyContain(f => f.DetailWithheld);

        foreach (var finding in result.Findings)
        {
            var evidence = BillingFindingSignalMapper.Map(finding, rule, SubjectsFor(request)).Request.EvidenceRefs!;

            evidence.Should().NotContain(e => e.Ref == "duplicateOf.invoiceId");
            evidence.Should().NotContain(e => (e.Text ?? string.Empty).Contains(EarlierInvoiceText, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AWithheldDuplicate_StoresNoTrackingNumber_InAnyFieldOfTheWrittenSignal_D112()
    {
        const string hiddenTrackingNumber = "INV-100.1";
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate,
            template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template);
        var request = Request(Fee(0));
        var acrossTheWall = Prior(hiddenTrackingNumber, "INV-100", 1, BillingPriorStatus.Active,
            new BillingPriorLine { Date = LineDate, TimekeeperId = Matched().TimekeeperId, Hours = 1.0m }) with
        {
            SecureRoots = Roots(Id("secure-matter")),
        };
        var loaded = Loaded(rule) with { PriorSubmissions = [acrossTheWall] }; // the invoice is ordinary: the prior is hidden
        var result = Evaluator().Evaluate(request, loaded);
        result.Findings.Should().HaveCount(2).And.OnlyContain(f => f.DetailWithheld, "one invoice-level and one line-level match, both hidden");
        var subjects = SubjectsFor(request);
        var (writer, org) = Writer(subjects);

        foreach (var finding in result.Findings)
        {
            var mapping = BillingFindingSignalMapper.Map(finding, rule, subjects);
            mapping.MessageSource.Should().Be(BillingMessageSource.WithheldDuplicate);
            mapping.Request.MessageTemplate.Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
            mapping.Request.FactValues.Should().BeEmpty();

            var written = await writer.WriteAsync(mapping.Request);

            var row = org.Rows[written.SignalId];
            row.GetAttributeValue<string>("sprk_sentence").Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
            foreach (var (column, value) in row.Attributes)
            {
                var text = value switch
                {
                    EntityReference reference => $"{reference.LogicalName} {reference.Id} {reference.Name}",
                    null => string.Empty,
                    _ => value.ToString() ?? string.Empty,
                };
                text.Should().NotContain(hiddenTrackingNumber, $"column {column} must not carry the withheld tracking number");
                text.Should().NotContain("100.1", $"column {column} must not carry any part of it");
            }
        }
    }

    // ── One Signal per (policy, subject): ontology-r1's Signal dedupe option a (072 review A4) ─────────────────────

    private const int Reject = (int)BillingConsequence.Reject;

    /// <summary>The writer's dedupe key of a request (episode included), as <see cref="SignalWriter"/> builds it.</summary>
    private static string KeyOf(SignalWriteRequest request) =>
        SignalWriter.BuildDedupeKey(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId.ToString("D").ToLowerInvariant(), request.Episode);

    /// <summary>The guard: the dedupe keys sent more than once in one evaluation.</summary>
    private static List<string> RepeatedKeys(IEnumerable<SignalWriteRequest> requests) =>
        requests.GroupBy(KeyOf).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public async Task TwoReasonsOnOneLine_GiveOneRequestCarryingBoth_WithTheStrongestConsequence_A4()
    {
        var rule = Version("CD-EXPENSE", RuleType.ExpenseRule) with { Consequence = Reject };
        var request = Request(Expense(0) with { ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m }); // D-117: the billed code is judged
        var result = Evaluator().Evaluate(request, Loaded(rule));
        result.Findings.Should().HaveCount(2, "control: the code and the cap both fail on the one line");
        RepeatedKeys(result.Findings.Select(f => BillingFindingSignalMapper.Map(f, rule, SubjectsFor(request)).Request))
            .Should().ContainSingle("control: mapped one by one, the two findings share the writer's key and one would be dropped");

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.ReasonCodes.Should().Equal(BillingReasonCodes.ExpenseAmountOverCap, BillingReasonCodes.ExpenseCodeNotReimbursable);
        mapping.Consequence.Should().Be(BillingConsequence.Reject);
        mapping.Fingerprint.Should().Be("expense_rule.amount_over_cap,expense_rule.code_not_reimbursable|Reject");
        mapping.Findings.Should().BeEquivalentTo(result.Findings);
        var evidence = mapping.Request.EvidenceRefs!;
        evidence.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.FingerprintEvidenceKind).Which.Ref.Should().Be(mapping.Fingerprint);
        var reasons = evidence.Where(e => e.Kind == BillingFindingSignalMapper.ReasonEvidenceKind).ToList();
        reasons.Select(e => e.Ref).Should().Equal(mapping.ReasonCodes);
        // Each reason keeps its own text line. (Both render the type's one default wording here, so the texts are equal: known limit.)
        reasons.Should().HaveCount(2).And.OnlyContain(e => !string.IsNullOrWhiteSpace(e.Text) && e.Source == "sprk_policyversion:" + rule.PolicyVersionId.ToString("D").ToLowerInvariant());
        evidence.Where(e => e.Kind == BillingFindingSignalMapper.FieldEvidenceKind).Select(e => e.Ref)
            .Should().Contain(["line[0].BilledExpenseCode", "line[0].Amount"], "the union of the fields both reasons read");

        var subjects = SubjectsFor(request);
        var (writer, org) = Writer(subjects);
        var written = await writer.WriteAsync(mapping.Request);
        org.Rows.Should().ContainSingle();
        org.Rows[written.SignalId].GetAttributeValue<string>("sprk_evidencerefs")
            .Should().Contain(BillingReasonCodes.ExpenseAmountOverCap).And.Contain(BillingReasonCodes.ExpenseCodeNotReimbursable);
    }

    [Fact]
    public void AFlagOnlyReason_NeverRaisesTheMergedConsequence_AndNeverLowersARejectCapableOne_A4()
    {
        var rule = Version("CD-REQUIRED", RuleType.RequiredData, """{"type":"RequiredData","fields":["billingPeriodStart"]}""") with { Consequence = Reject };
        var both = Request(Fee(0)) with { BillingPeriodStart = null, Currency = "EUR" };
        var currencyOnly = Request(Fee(0)) with { Currency = "EUR" };

        var merged = MapAll(both, Evaluator().Evaluate(both, Loaded(rule))).Should().ContainSingle().Subject;
        var alone = MapAll(currencyOnly, Evaluator().Evaluate(currencyOnly, Loaded(rule))).Should().ContainSingle().Subject;

        merged.ReasonCodes.Should().Equal(BillingReasonCodes.CurrencyNotUsd, BillingReasonCodes.RequiredFieldMissing);
        merged.Consequence.Should().Be(BillingConsequence.Reject, "the missing field keeps its Reject");
        merged.Request.MessageTemplate.Should().NotBe(BillingRuleCatalogue.CurrencyNotUsdTemplate, "the message is the strongest reason's");
        merged.Request.EvidenceRefs!.Should().Contain(e => e.Kind == BillingFindingSignalMapper.ReasonEvidenceKind
                                                           && e.Text == BillingRuleCatalogue.CurrencyNotUsdTemplate);
        alone.Consequence.Should().Be(BillingConsequence.Flag, "a flag-only reason under a Reject version is a Flag");
        alone.Fingerprint.Should().Be("required_data.currency_not_usd|Flag");
    }

    [Fact]
    public async Task AWithheldReason_MergedWithAVisibleOne_LeaksNothingOfTheHiddenPrior_A4_D112()
    {
        // The real Duplicate handler sends at most one finding per subject; a merge with a withheld reason is built by hand, as a
        // future type (or a second Duplicate reason) could produce it. The visible reason names the same prior, and the line's own
        // date and amount equal the hidden prior's.
        const string hiddenTrackingNumber = "INV-100.1";
        var hiddenPriorId = Id("hidden-prior-submission");
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate, template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template) with { Consequence = Reject };
        BillingFinding Finding(string reason, bool withheld, IReadOnlyList<BillingValueRead> values, IReadOnlyDictionary<string, string> facts) => new()
        {
            RuleType = RuleType.Duplicate,
            PolicyId = rule.PolicyId,
            PolicyCode = rule.PolicyCode,
            PolicyVersionId = rule.PolicyVersionId,
            Subject = BillingSubject.ForLine(0),
            ReasonCode = reason,
            ValuesRead = values,
            FactValues = facts,
            DetailWithheld = withheld,
        };
        var dateField = new Sprk.Bff.Api.Services.Finance.Resolution.FieldRef(0, BillingFieldNames.Date);
        var amountField = new Sprk.Bff.Api.Services.Finance.Resolution.FieldRef(0, BillingFieldNames.Amount);
        var hidden = Finding(BillingReasonCodes.DuplicateOfPriorSubmissionLine, true,
            [new BillingValueRead { Name = "line.date", ExtractedField = dateField, Confidence = 0.95m },
             new BillingValueRead { Name = "line.amount", ExtractedField = amountField, Confidence = 0.95m }],
            new Dictionary<string, string>());
        var visible = Finding(BillingReasonCodes.DuplicateOfEarlierLine, false,
            [new BillingValueRead { Name = "line.date", Value = "2026-03-02", ExtractedField = dateField, Confidence = 0.95m },
             new BillingValueRead { Name = "line.amount", Value = "1234.56", ExtractedField = amountField, Confidence = 0.95m },
             new BillingValueRead { Name = "duplicateOf.trackingNumber", Value = hiddenTrackingNumber },
             new BillingValueRead { Name = "duplicateOf.submissionId", Value = hiddenPriorId.ToString() }],
            new Dictionary<string, string>
            {
                ["invoice.firmInvoiceNumber"] = "INV-100", ["line.date"] = "2026-03-02", ["line.timekeeperName"] = "Jane Partner",
                ["duplicateOf.trackingNumber"] = hiddenTrackingNumber,
            });
        var alone = BillingFindingSignalMapper.Map(visible, rule, SubjectsFor(Request(Fee(0))));
        SignalWriter.RenderSentence(alone.Request.MessageTemplate, alone.Request.FactValues)
            .Should().Contain(hiddenTrackingNumber, "control: on its own, the visible reason's text names the prior");
        var request = Request(Fee(0));
        var subjects = SubjectsFor(request);

        var mapping = BillingFindingSignalMapper.MapAll([visible, hidden], new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule }, subjects)
            .Should().ContainSingle().Subject;

        mapping.ReasonCodes.Should().Equal(BillingReasonCodes.DuplicateOfPriorSubmissionLine, BillingReasonCodes.DuplicateOfEarlierLine);
        mapping.MessageSource.Should().Be(BillingMessageSource.WithheldDuplicate);
        mapping.Request.MessageTemplate.Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
        mapping.Request.FactValues.Should().BeEmpty();
        mapping.Request.EvidenceRefs!.Where(e => e.Kind == BillingFindingSignalMapper.FieldEvidenceKind)
            .Should().NotBeEmpty("the fields read stay, with their confidence").And.OnlyContain(e => e.Text == null && e.Confidence != null);
        var sent = Json(mapping.Request);
        foreach (var leak in new[] { hiddenTrackingNumber, "100.1", "2026-03-02", "1234.56", hiddenPriorId.ToString(), "Jane Partner" })
        {
            sent.Should().NotContain(leak);
        }

        var (writer, org) = Writer(subjects);
        var written = await writer.WriteAsync(mapping.Request);
        foreach (var (column, value) in org.Rows[written.SignalId].Attributes)
        {
            var text = value switch
            {
                EntityReference reference => $"{reference.LogicalName} {reference.Id} {reference.Name}",
                null => string.Empty,
                _ => value.ToString() ?? string.Empty,
            };
            foreach (var leak in new[] { "100.1", "2026-03-02", "1234.56", hiddenPriorId.ToString(), "Jane Partner" })
            {
                text.Should().NotContain(leak, $"column {column} must not name the hidden prior");
            }
        }
    }

    [Fact]
    public void TheMapper_NeverSendsTwoRequestsWithOneDedupeKey_OverThe150LineFixture_MustFireGuard_A4()
    {
        var (request, loaded) = EverythingWrong(150);
        loaded = loaded with { Rules = loaded.Rules.Select(r => r with { Consequence = (int)BillingConsequence.Flag }).ToList() };
        var result = Evaluator().Evaluate(request, loaded);
        var subjects = SubjectsFor(request);
        var oneByOne = result.Findings.Select(f => BillingFindingSignalMapper.Map(f, result.RulesByVersion[f.PolicyVersionId], subjects).Request).ToList();

        var mapped = BillingFindingSignalMapper.MapAll(result, subjects);

        RepeatedKeys(oneByOne).Should().NotBeEmpty("must fire: mapped one finding per request, the fixture repeats keys");
        RepeatedKeys(mapped.Select(m => m.Request)).Should().BeEmpty("one request per (policy, subject)");
        mapped.Should().HaveCount(oneByOne.Select(KeyOf).Distinct().Count());
        mapped.SelectMany(m => m.Findings).Should().HaveCount(result.Findings.Count, "no finding is lost in the merge");
        foreach (var mapping in mapped)
        {
            var render = () => SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues);
            render.Should().NotThrow();
        }
    }

    [Fact]
    public void TheFingerprintAndTheRequest_DoNotDependOnTheOrderOfTheFindings_A4()
    {
        var (request, loaded) = EverythingWrong(20);
        var result = Evaluator().Evaluate(request, loaded);
        var subjects = SubjectsFor(request);
        var reordered = result.Findings.Reverse().ToList();
        var interleaved = result.Findings.Where((_, i) => i % 2 == 1).Concat(result.Findings.Where((_, i) => i % 2 == 0)).ToList();

        static Dictionary<string, string> ByKey(IEnumerable<BillingSignalMapping> mappings) =>
            mappings.ToDictionary(m => KeyOf(m.Request), m => m.Fingerprint + "\n" + Json(m.Request));

        var original = ByKey(BillingFindingSignalMapper.MapAll(result.Findings, result.RulesByVersion, subjects));

        original.Values.Should().Contain(v => v.Contains(',', StringComparison.Ordinal), "control: the fixture merges reasons somewhere");
        ByKey(BillingFindingSignalMapper.MapAll(reordered, result.RulesByVersion, subjects)).Should().Equal(original);
        ByKey(BillingFindingSignalMapper.MapAll(interleaved, result.RulesByVersion, subjects)).Should().Equal(original);
    }

    // ── D-115: rates and expense caps not checked ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null, "missing")]
    [InlineData("USD", 0.50, "unclear")]
    [InlineData("EUR", 0.95, "not USD")]
    public async Task RatesNotChecked_IsWrittenWithItsOwnMessage_AsAFlag_D115(string? currency, double? confidence, string said)
    {
        var rule = Version("CD-RATE", RuleType.ApprovedRate, template: "{{line.timekeeperName}} billed {{line.rate}}.", lane: SignalWriter.LaneDo)
            with { Consequence = Reject, Severity = SignalWriter.SeverityCritical, ShortHeadline = "Billed rate above the approved rate" };
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m }) with
        {
            Currency = currency,
            HeaderConfidence = Confidence().With(BillingFieldNames.Currency, (decimal?)confidence),
        };
        var result = Evaluator().Evaluate(request, Loaded(rule));
        var subjects = SubjectsFor(request);

        var mapping = BillingFindingSignalMapper.MapAll(result, subjects).Should().ContainSingle().Subject;
        var (writer, org) = Writer(subjects);
        var written = await writer.WriteAsync(mapping.Request);

        mapping.MessageSource.Should().Be(BillingMessageSource.ReasonTemplate);
        mapping.Consequence.Should().Be(BillingConsequence.Flag, "forced to Flag whatever the version's consequence");
        org.Rows[written.SignalId].GetAttributeValue<string>("sprk_sentence")
            .Should().Be($"Rates and expense caps were not checked: the invoice currency is {said}.");
        mapping.Request.SubjectEntityLogicalName.Should().Be("sprk_invoice");
        // ontology-r1 F2: the title says what happened, not that a rate was found over the approved rate; and ranks as a Flag.
        mapping.Request.ShortHeadline.Should().Be(BillingRuleCatalogue.RatesNotCheckedHeadline);
        mapping.Request.Lane.Should().Be(SignalWriter.LaneDecide);
        mapping.Request.Severity.Should().Be(SignalWriter.SeverityInfo);
        mapping.Request.EvidenceRefs!.Where(e => e.Kind == BillingFindingSignalMapper.ReferenceEvidenceKind && e.Ref == "skippedPolicyVersion")
            .Should().ContainSingle().Which.Text.Should().Be("CD-RATE:" + rule.PolicyVersionId.ToString("D").ToLowerInvariant());
    }

    [Fact]
    public void RatesNotChecked_MergedWithARejectCapableReasonOfTheSamePolicyAndSubject_KeepsTheReject_D115()
    {
        // The real handlers give a money policy no other invoice-level reason, so the second reason is built by hand: an invoice-level
        // ExpenseRule finding under the same Reject version.
        var rule = Version("CD-EXPENSE", RuleType.ExpenseRule) with { Consequence = Reject };
        var request = Request(Expense(0) with { ExpenseCode = "E101", Amount = 600m }) with { Currency = "EUR" };
        var result = Evaluator().Evaluate(request, Loaded(rule));
        var notChecked = result.Findings.Should().ContainSingle().Subject;
        var rejectCapable = notChecked with
        {
            ReasonCode = BillingReasonCodes.ExpenseCodeNotReimbursable,
            ValuesRead = [new BillingValueRead { Name = "invoice.firmInvoiceNumber", Value = "INV-100" }],
        };

        var withBoth = BillingFindingSignalMapper.MapAll([notChecked, rejectCapable], result.RulesByVersion, SubjectsFor(request)).Should().ContainSingle().Subject;
        var alone = BillingFindingSignalMapper.MapAll(result, SubjectsFor(request)).Should().ContainSingle().Subject;

        withBoth.Consequence.Should().Be(BillingConsequence.Reject, "the reject-capable reason keeps its Reject");
        withBoth.Request.ShortHeadline.Should().NotBe(BillingRuleCatalogue.RatesNotCheckedHeadline, "the primary reason is the Reject one, so the title is the rule's");
        withBoth.ReasonCodes.Should().Equal(BillingEvaluationReasons.CurrencyNotUsd, BillingReasonCodes.ExpenseCodeNotReimbursable);
        withBoth.Fingerprint.Should().Be("billing.currency.not_usd,expense_rule.code_not_reimbursable|Reject");
        alone.Consequence.Should().Be(BillingConsequence.Flag, "the currency reason alone never raises the consequence");
        alone.Fingerprint.Should().Be("billing.currency.not_usd|Flag");
    }

    [Fact]
    public void ADowngradedVersion_MapsToAFlagSignal_WithTheMatchingFingerprint_K2()
    {
        // An ApprovedTimekeeper version stored as Reject (written around publish) is evaluated as Flag; the Signal and its
        // fingerprint must say Flag too, or 049 would raise a Reject Signal 073 never decides on.
        var stored = Version("CD-TIMEKEEPER", RuleType.ApprovedTimekeeper) with { Consequence = (int)BillingConsequence.Reject };
        var request = Request(Fee(0) with { Timekeeper = Unmatched(Sprk.Bff.Api.Services.Finance.Resolution.TimekeeperUnmatchedReason.NotOnRoster) });
        var result = Evaluator().Evaluate(request, Loaded(stored));

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.Consequence.Should().Be(BillingConsequence.Flag);
        mapping.Fingerprint.Should().Be(BillingFindingSignalMapper.FingerprintOf(mapping.ReasonCodes, BillingConsequence.Flag)).And.EndWith("|Flag");
    }

    [Fact]
    public void AChangedReasonSet_OrAChangedConsequence_GivesAnotherFingerprint_A4()
    {
        var codes = new[] { BillingReasonCodes.ExpenseAmountOverCap, BillingReasonCodes.ExpenseCodeNotReimbursable };

        var both = BillingFindingSignalMapper.FingerprintOf(codes, BillingConsequence.Reject);

        BillingFindingSignalMapper.FingerprintOf(codes[..1], BillingConsequence.Reject).Should().NotBe(both);
        BillingFindingSignalMapper.FingerprintOf(codes, BillingConsequence.Flag).Should().NotBe(both);
        BillingFindingSignalMapper.FingerprintOf(codes, BillingConsequence.Reject).Should().Be(both);
    }
}
