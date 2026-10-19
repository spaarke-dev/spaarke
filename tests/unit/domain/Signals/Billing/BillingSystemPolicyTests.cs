using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 049, D-123 / D-125: the SubmissionIntegrity system billing rule type. It is a Billing member that is flag-only, never
/// reject-capable, never authorable (refused at save, at the billing gate and at Ontology authoring), never offered to the drafting
/// Action and never evaluated from a body; its four reasons each have their own message and title, and a finding built by
/// <see cref="BillingSystemFindings"/> maps through <see cref="BillingFindingSignalMapper"/> unchanged to a Flag Signal.
/// </summary>
public class BillingSystemPolicyTests
{
    private static readonly Guid VersionId = Guid.Parse("04900000-0000-4000-8000-000000000125");
    private static readonly Guid PolicyId = Guid.Parse("04900000-0000-4000-8000-000000000124");
    private static readonly Guid InvoiceRowId = Guid.Parse("04900000-0000-4000-8000-0000000000e1");
    private static readonly BillingSystemPolicyVersion System = new(PolicyId, BillingRuleCatalogue.SystemPolicyCode, VersionId);

    private static readonly string[] SystemReasons =
    [
        BillingReasonCodes.SubmissionInvoiceNumberMismatch,
        BillingReasonCodes.CodingRefusedCode,
        BillingReasonCodes.CodingFirmCodeMismatch,
        BillingReasonCodes.CodingCodeSetNeedsReview,
    ];

    private static PolicyVersionValidator Validator() =>
        new(new RuleBodySchemaValidator(),
            new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DateTimeOffset.UtcNow)),
            NullLogger<PolicyVersionValidator>.Instance);

    // ── The type ────────────────────────────────────────────────────────────────

    [Fact]
    public void SubmissionIntegrity_IsTheNextFreeOptionValue_AFlagOnlyBillingTypeOutsideTheAuthorableCatalogue()
    {
        ((int)RuleType.SubmissionIntegrity).Should().Be(100000014);
        RuleDomains.IsBilling(RuleType.SubmissionIntegrity).Should().BeTrue();
        BillingRuleCatalogue.IsSystemType(RuleType.SubmissionIntegrity).Should().BeTrue();
        BillingRuleCatalogue.Types.Should().NotContain(RuleType.SubmissionIntegrity);
        BillingRuleCatalogue.Types.Should().OnlyContain(t => !BillingRuleCatalogue.IsSystemType(t), "must-not-fire: no authorable type is a system type");

        BillingRuleCatalogue.AllowedConsequences(RuleType.SubmissionIntegrity).Should().BeEquivalentTo(new[] { BillingConsequence.Flag });
        BillingRuleCatalogue.IsConsequenceAllowed(RuleType.SubmissionIntegrity, (int)BillingConsequence.Reject).Should().BeFalse();
        BillingRuleCatalogue.IsConsequenceAllowed(RuleType.SubmissionIntegrity, (int)BillingConsequence.SuggestAdjustment).Should().BeFalse();
        BillingRuleCatalogue.IsConsequenceAllowed(RuleType.SubmissionIntegrity, (int)BillingConsequence.Flag).Should().BeTrue();
        BillingRuleCatalogue.IsRejectEligible(RuleType.SubmissionIntegrity).Should().BeFalse();
        BillingRuleCatalogue.IsHoldOnly(RuleType.SubmissionIntegrity).Should().BeFalse();
        BillingRuleCatalogue.RejectCapableReasons(RuleType.SubmissionIntegrity).Should().BeEmpty();
    }

    [Fact]
    public void AStrictEmptyBody_IsTheOnlyBodyThatPassesItsSchema()
    {
        var schema = new RuleBodySchemaValidator();

        schema.Validate(RuleType.SubmissionIntegrity, "{}").IsValid.Should().BeTrue();
        schema.Validate(RuleType.SubmissionIntegrity, """{"type":"SubmissionIntegrity"}""").IsValid.Should().BeFalse("additionalProperties is false");
        schema.Validate(RuleType.SubmissionIntegrity, """{"tolerancePercent":5}""").IsValid.Should().BeFalse();
    }

    // ── Never authorable ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("SubmissionIntegrity")]
    [InlineData("100000014")]
    public void ValidateForSave_AtSave_RefusesTheSystemType_ByNameAndByValue_WithTheSystemRuleSentence(string raw)
    {
        foreach (var domain in new RuleDomain?[] { RuleDomain.Billing, null })
        {
            var result = Validator().ValidateForSave(raw, "{}", null, domain, (int)BillingConsequence.Flag);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
            result.Errors.Should().ContainSingle().Which.Should().Contain("system rule").And.Contain("not authorable");
        }
    }

    [Theory]
    [InlineData("SubmissionIntegrity")]
    [InlineData("100000014")]
    public void ValidateForAuthoring_RefusesTheSystemType_AsUnsupported_NotWrongDomain(string raw)
    {
        var result = Validator().ValidateForAuthoring(raw, "{}", null);

        result.Validation.IsValid.Should().BeFalse();
        result.Validation.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
        result.Validation.Errors.Should().ContainSingle().Which.Should().Contain("system rule").And.Contain("not authorable");
        result.TemplateEligibleFields.Should().BeEmpty();
    }

    [Fact]
    public void TheBillingGate_RefusesTheSystemType_WithoutThrowing()
    {
        var gate = Validator();

        var prepared = gate.TryPrepareBillingRule(
            new PolicyVersionSnapshot(VersionId, BillingRuleCatalogue.SystemPolicyCode, "100000014", "{}", null, (int)BillingConsequence.Flag),
            out var definition);

        prepared.Should().BeFalse("a system rule is never evaluated from a body");
        definition.Should().BeNull();
    }

    // Must-not-fire controls: the refusal is for the system type only.
    [Fact]
    public void ExistingBillingTypes_AreStillAcceptedAtSave_AndTheirAuthoringRefusalIsStillWrongDomain()
    {
        Validator().ValidateForSave("ApprovedTimekeeper", """{"type":"ApprovedTimekeeper"}""", null, RuleDomain.Billing, (int)BillingConsequence.Flag)
            .IsValid.Should().BeTrue();
        Validator().ValidateForSave("TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""", null, RuleDomain.Billing, (int)BillingConsequence.Flag)
            .IsValid.Should().BeTrue();
        Validator().ValidateForAuthoring("ApprovedTimekeeper", """{"type":"ApprovedTimekeeper"}""", null).Validation.Reason
            .Should().Be(OntologyWriterFailureReason.RuleTypeWrongDomain);
        Validator().ValidateForAuthoring("Existence", """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[]}""", null)
            .Validation.IsValid.Should().BeTrue();
        Validator().ValidateForAuthoring(null, "{}", null).Validation.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
    }

    [Fact]
    public void TheDraftingAction_IsNotOfferedTheSystemType_AndCannotNameIt()
    {
        BillingRuleDraftTranslator.RuleTypeOptions().Select(o => o.RuleType).Should().NotContain(nameof(RuleType.SubmissionIntegrity));
        BillingRuleDraftTranslator.RuleTypeOptions().Should().HaveCount(11);
        BillingRuleDraftTranslator.TryParseType("SubmissionIntegrity", out _).Should().BeFalse();

        var (rules, refused) = BillingRuleDraftTranslator.Translate(
        [
            new BillingRuleProposal { RuleType = "SubmissionIntegrity", ParametersJson = "{}", Consequence = "flag", ClauseQuote = "Some clause" },
        ]);
        rules.Should().BeEmpty();
        refused.Should().ContainSingle();
    }

    // ── Never evaluated from a body ──────────────────────────────────────────────

    [Fact]
    public void ALoadedSystemRule_IsSkippedByTheEvaluator_NeitherCrashingNorRefusingNorHoldingTheInvoice()
    {
        var system = Version(BillingRuleCatalogue.SystemPolicyCode, RuleType.SubmissionIntegrity, "{}");

        var result = Evaluator().Evaluate(Request(), Loaded(system));

        result.Findings.Should().BeEmpty();
        result.Refusals.Should().BeEmpty();
    }

    [Fact]
    public void TheHandlerRegistry_HasNoHandlerForTheSystemType_AndRefusesItFailClosed()
    {
        BillingRuleHandlers.HasHandler(RuleType.SubmissionIntegrity).Should().BeFalse();
    }

    // ── The reasons ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheReasonCodes_AreTheDecidedNames()
    {
        BillingReasonCodes.SubmissionInvoiceNumberMismatch.Should().Be("submission.invoice_number_mismatch");
        BillingReasonCodes.CodingRefusedCode.Should().Be("coding.refused_code");
        BillingReasonCodes.CodingFirmCodeMismatch.Should().Be("coding.firm_code_mismatch");
        BillingReasonCodes.CodingCodeSetNeedsReview.Should().Be("coding.code_set_needs_review");
    }

    [Fact]
    public void EachReason_HasItsOwnFlagOnlyTemplateAndHeadline_NamingOnlyDeclaredTokens_AndNeverCallingAnythingInvalid()
    {
        var declared = BillingRuleCatalogue.TemplateFactFields(RuleType.SubmissionIntegrity);
        var texts = new List<string>();
        foreach (var reason in SystemReasons)
        {
            var template = BillingRuleCatalogue.ReasonTemplate(reason);
            var headline = BillingRuleCatalogue.ReasonHeadline(reason);

            template.Should().NotBeNullOrWhiteSpace(reason);
            headline.Should().NotBeNullOrWhiteSpace(reason);
            BillingRuleCatalogue.IsFlagOnlyReason(reason).Should().BeTrue(reason);
            BillingRuleCatalogue.IsRejectCapableReason(RuleType.SubmissionIntegrity, reason).Should().BeFalse(reason);
            SignalWriter.HasMalformedPlaceholder(template!).Should().BeFalse(reason);
            SignalWriter.ExtractTemplateTokens(template!).Should().OnlyContain(t => declared.Contains(t), reason);
            (template + " " + headline).ToLowerInvariant().Should().NotContain("invalid", "a reason claims only what was compared");
            texts.Add(template!);
            texts.Add(headline!);
        }

        texts.Should().OnlyHaveUniqueItems("each reason has its own sentence and title");
    }

    [Fact]
    public void TheInvoiceNumberTemplate_CitesBothValuesAsTokens()
    {
        SignalWriter.ExtractTemplateTokens(BillingRuleCatalogue.ReasonTemplate(BillingReasonCodes.SubmissionInvoiceNumberMismatch)!)
            .Should().BeEquivalentTo("submission.invoiceNumber", "invoice.invoiceNumber");
    }

    [Fact]
    public void AReasonOfAnotherType_IsNotFlagOnlyBecauseOfThisChange_MustNotFire()
    {
        BillingRuleCatalogue.IsFlagOnlyReason(BillingReasonCodes.DuplicateInvoiceNumber).Should().BeFalse();
        BillingRuleCatalogue.IsFlagOnlyReason(BillingReasonCodes.RateAboveApproved).Should().BeFalse();
        BillingRuleCatalogue.IsFlagOnlyReason(BillingReasonCodes.CurrencyNotUsd).Should().BeTrue("existing flag-only reasons stay flag-only");
    }

    // ── The factory and the mapper ──────────────────────────────────────────────

    public static TheoryData<string> Reasons => new(SystemReasons);

    private static BillingFinding FindingFor(string reason) => reason switch
    {
        BillingReasonCodes.SubmissionInvoiceNumberMismatch => BillingSystemFindings.InvoiceNumberMismatch(System, "INV-100", "INV-1OO", 0.91m),
        BillingReasonCodes.CodingRefusedCode => BillingSystemFindings.CodeRefused(System, 2, new RefusedCode(BillingCodeKind.Task, "L999", CodeRefusalReason.OutsideCodeSet)),
        BillingReasonCodes.CodingFirmCodeMismatch => BillingSystemFindings.FirmCodeMismatch(System, 1, BillingCodeKind.Activity, "A101", "A102"),
        BillingReasonCodes.CodingCodeSetNeedsReview => BillingSystemFindings.CodeSetNeedsReview(System, "UTBMS Litigation"),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static BillingRuleCandidate SystemRule(int? consequence = (int)BillingConsequence.Flag, int? lane = null, int? severity = null) => new()
    {
        PolicyId = PolicyId,
        PolicyCode = BillingRuleCatalogue.SystemPolicyCode,
        PolicyVersionId = VersionId,
        RuleSetId = Guid.Empty,
        Scope = RuleSetScope.ClientDefault,
        RuleType = "100000014",
        RuleBody = "{}",
        Consequence = consequence,
        Lane = lane,
        Severity = severity,
        IsCurrent = true,
        VersionNumber = 1,
    };

    private static BillingSignalSubjects Subjects() => new(
        InvoiceRowId, "INV-100", new Dictionary<int, Guid> { [1] = Id("line-row:1"), [2] = Id("line-row:2") });

    [Theory]
    [MemberData(nameof(Reasons))]
    public void ASystemFinding_MapsUnchanged_ToAFlagSignal_WithTheReasonsOwnHeadline_LaneDecide_SeverityInfo(string reason)
    {
        var finding = FindingFor(reason);

        // A stored Reject (written around the validators) and a Do lane / Warning severity are still a Flag note on the Decide lane.
        var mapping = BillingFindingSignalMapper.Map(
            finding, SystemRule((int)BillingConsequence.Reject, SignalWriter.LaneDo, SignalWriter.SeverityWarning), Subjects());

        mapping.Request.ShortHeadline.Should().Be(BillingRuleCatalogue.ReasonHeadline(reason));
        mapping.Request.Lane.Should().Be(SignalWriter.LaneDecide);
        mapping.Request.Severity.Should().Be(SignalWriter.SeverityInfo);
        mapping.Consequence.Should().Be(BillingConsequence.Flag);
        mapping.Fingerprint.Should().EndWith("|Flag").And.StartWith(reason);
        mapping.MessageSource.Should().Be(BillingMessageSource.ReasonTemplate);
        mapping.Request.PolicyCode.Should().Be(BillingRuleCatalogue.SystemPolicyCode);
        mapping.Request.PolicyVersionId.Should().Be(VersionId);

        // Every token of the message has a value, so the writer's own rendering succeeds.
        var sentence = SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues);
        sentence.Should().NotContain("{{");
    }

    [Fact]
    public void TheInvoiceNumberMismatch_IsInvoiceLevel_CitesBothValues_AndKeepsTheReadValuesConfidence()
    {
        var mapping = BillingFindingSignalMapper.Map(FindingFor(BillingReasonCodes.SubmissionInvoiceNumberMismatch), SystemRule(), Subjects());

        mapping.Request.SubjectEntityLogicalName.Should().Be("sprk_invoice");
        mapping.Request.SubjectId.Should().Be(InvoiceRowId);
        var sentence = SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues);
        sentence.Should().Contain("INV-100").And.Contain("INV-1OO");
        mapping.Request.EvidenceRefs!.Should().Contain(e => e.Kind == BillingFindingSignalMapper.FieldEvidenceKind && e.Confidence == 0.91);
    }

    [Fact]
    public void ALineLevelCodingFinding_IsOnTheLinesRow()
    {
        var mapping = BillingFindingSignalMapper.Map(FindingFor(BillingReasonCodes.CodingFirmCodeMismatch), SystemRule(), Subjects());

        mapping.Request.SubjectEntityLogicalName.Should().Be("sprk_billingevent");
        mapping.Request.SubjectId.Should().Be(Id("line-row:1"));
        SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues)
            .Should().Contain("A101").And.Contain("A102").And.Contain("activity");
    }

    [Fact]
    public void TheFactory_RefusesABlankValue_AndNeverCarriesATokenOutsideTheDeclaredSet()
    {
        var act = () => BillingSystemFindings.InvoiceNumberMismatch(System, "INV-100", " ");
        act.Should().Throw<ArgumentException>("a comparison with no value on one side is not a mismatch");

        var declared = BillingRuleCatalogue.TemplateFactFields(RuleType.SubmissionIntegrity);
        foreach (var reason in SystemReasons)
        {
            FindingFor(reason).FactValues.Keys.Should().OnlyContain(k => declared.Contains(k), reason);
            FindingFor(reason).RuleType.Should().Be(RuleType.SubmissionIntegrity);
        }
    }
}
