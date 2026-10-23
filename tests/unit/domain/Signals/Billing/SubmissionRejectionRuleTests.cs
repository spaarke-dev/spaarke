using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 073: <see cref="SubmissionRejectionRule"/> (spec FR-26, I-5, NFR-06; D-89, D-112) and the catalogue's
/// reject-eligibility it rests on. A submission is rejected only when a finding's consequence is Reject, every extracted field the
/// finding read has a combined confidence at or above the threshold, and the type is reject-eligible. Each condition is shown false
/// on its own, with the other two true, so a test cannot pass because a different condition held the finding.
/// </summary>
public class SubmissionRejectionRuleTests
{
    private const int Reject = (int)BillingConsequence.Reject;
    private const int Flag = (int)BillingConsequence.Flag;
    private const int Suggest = (int)BillingConsequence.SuggestAdjustment;

    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid InvoiceRowId = Guid.Parse("07300000-0000-4000-8000-0000000000e1");

    /// <summary>The types a finding can reject on, ignoring the narrowing of ApprovedRate (reason) and Duplicate (level).</summary>
    public static readonly RuleType[] RejectingTypes =
    [
        RuleType.ApprovedRate, RuleType.ExpenseRule, RuleType.InvoiceAge, RuleType.CoreRecordWindow, RuleType.Duplicate, RuleType.RequiredData,
    ];

    /// <summary>FR-26: the types that may never reject (TimeIncrement, DailyHoursCap, NonBillableActivity, AiObservation).</summary>
    public static readonly RuleType[] NeverRejectingTypes =
    [
        RuleType.TimeIncrement, RuleType.DailyHoursCap, RuleType.NonBillableActivity, RuleType.AiObservation,
    ];

    public static TheoryData<RuleType> Rejecting => new(RejectingTypes);

    public static TheoryData<RuleType> NeverRejecting => new(NeverRejectingTypes);

    public static TheoryData<RuleType> EveryBillingType => new(BillingRuleCatalogue.Types);

    // ── Builders ─────────────────────────────────────────────────────────────────────────────────────────────────

    // The arithmetic cap is kept at or below the reference cap (046's default 0.50 when that is lower), so a case is about the threshold and
    // the reference cap it names, not about the arithmetic cap's half of the invariant (073 review F1-c).
    private static EbillingSettings Settings(decimal threshold = 0.90m, decimal cap = 0.80m) =>
        new() { ReliabilityThreshold = threshold, ReferenceMissingCap = cap, ArithmeticFailCap = Math.Min(0.50m, cap) };

    private static readonly string[] FieldNames =
    [
        BillingFieldNames.Rate, BillingFieldNames.Hours, BillingFieldNames.Date, BillingFieldNames.Amount, BillingFieldNames.Narrative,
    ];

    private static BillingSubject SubjectOf(RuleType type) => type == RuleType.Duplicate ? BillingSubject.ForInvoice() : BillingSubject.ForLine(0);

    private static FieldRef Ref(BillingSubject subject, int n) => new(subject.LineIndex, FieldNames[n]);

    private static BillingValueRead Extracted(FieldRef field, decimal? confidence, string? value = null) => new()
    {
        Name = (field.LineIndex is null ? "invoice." : "line.") + field.Field,
        Value = value ?? "v-" + field.Field,
        ExtractedField = field,
        Confidence = confidence,
        BelowReliabilityThreshold = confidence is null || confidence < Threshold,
    };

    private static BillingValueRead ReferenceValue(string name, string value) => new() { Name = name, Value = value };

    private static List<BillingValueRead> Reads(BillingSubject subject, params decimal?[] confidences) =>
        confidences.Select((c, i) => Extracted(Ref(subject, i), c)).ToList();

    /// <summary>The reason that lets the type reject (ApprovedRate only rejects on a rate above the approved rate, 071 judgment call 2).</summary>
    private static string ReasonOf(RuleType type) => type switch
    {
        RuleType.ApprovedTimekeeper => BillingReasonCodes.TimekeeperNotOnRoster,
        RuleType.ApprovedRate => BillingReasonCodes.RateAboveApproved,
        RuleType.TimeIncrement => BillingReasonCodes.TimeNotAMultipleOfIncrement,
        RuleType.DailyHoursCap => BillingReasonCodes.DailyHoursOverCap,
        RuleType.NonBillableActivity => BillingReasonCodes.NonBillableCode,
        RuleType.ExpenseRule => BillingReasonCodes.ExpenseAmountOverCap,
        RuleType.InvoiceAge => BillingReasonCodes.InvoiceTooOld,
        RuleType.CoreRecordWindow => BillingReasonCodes.BeforeCoreRecordOpened,
        RuleType.Duplicate => BillingReasonCodes.DuplicateInvoiceNumber,
        RuleType.RequiredData => BillingReasonCodes.RequiredFieldMissing,
        RuleType.AiObservation => BillingReasonCodes.AiBlockBilling,
        _ => "ontology.reason",
    };

    private static BillingFinding Finding(
        RuleType type, IReadOnlyList<BillingValueRead>? values = null, string? reason = null, BillingSubject? subject = null,
        Guid? version = null, bool withheld = false)
    {
        var at = subject ?? SubjectOf(type);
        return new BillingFinding
        {
            RuleType = type,
            PolicyId = Id("policy:" + type),
            PolicyCode = "BILL-" + type,
            PolicyVersionId = version ?? Id("version:" + type),
            Subject = at,
            ReasonCode = reason ?? ReasonOf(type),
            ValuesRead = values ?? Reads(at, 0.95m, 0.95m),
            FactValues = new Dictionary<string, string>(),
            DetailWithheld = withheld,
        };
    }

    private static SubmissionDecision Decide(BillingFinding finding, int? consequence, EbillingSettings? settings) =>
        SubmissionRejectionRule.Decide([finding], _ => consequence, settings);

    private static FindingDecision Only(SubmissionDecision decision) => decision.Findings.Should().ContainSingle().Subject;

    private static void ShouldHold(SubmissionDecision decision, string reason)
    {
        decision.Outcome.Should().Be(SubmissionOutcome.Accepted);
        decision.DecidingFindings.Should().BeEmpty();
        var held = Only(decision);
        held.Rejects.Should().BeFalse();
        held.HeldBecause.Should().Be(reason);
    }

    private sealed class FakeSettings(EbillingSettings? current) : IEbillingSettingsReader
    {
        public EbillingSettings? Current { get; set; } = current;

        public Exception? Failure { get; set; }

        public Task<EbillingSettings> GetAsync(CancellationToken ct = default) =>
            Failure is { } failure ? Task.FromException<EbillingSettings>(failure) : Task.FromResult(Current!);
    }

    private static SubmissionRejectionRule Sut(FakeSettings settings, ILogger<SubmissionRejectionRule>? logger = null) =>
        new(settings, logger ?? new CapturingLogger<SubmissionRejectionRule>());

    private static BillingEvaluationResult ResultOf(IEnumerable<BillingFinding> findings, params BillingRuleCandidate[] rules) => new()
    {
        Findings = findings.ToArray(),
        RulesByVersion = rules.ToDictionary(r => r.PolicyVersionId),
        Refusals = [],
        Skips = [],
        Facts = Facts(),
    };

    // ── The three conditions ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void AllThreeConditionsTrue_Rejects(RuleType type)
    {
        var decision = Decide(Finding(type), Reject, Settings());

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        decision.ReliabilityThreshold.Should().Be(0.90m);
        var rejecting = decision.DecidingFindings.Should().ContainSingle().Subject;
        rejecting.Rejects.Should().BeTrue();
        rejecting.HeldBecause.Should().BeNull();
        rejecting.UnreliableFields.Should().BeEmpty();
        rejecting.Consequence.Should().Be(BillingConsequence.Reject);
    }

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void AConfidenceExactlyAtTheThreshold_Rejects_AndJustBelowItDoesNot(RuleType type)
    {
        var subject = SubjectOf(type);

        Decide(Finding(type, Reads(subject, 0.90m, 0.90m)), Reject, Settings(0.90m)).Outcome
            .Should().Be(SubmissionOutcome.Rejected, "the test is confidence >= threshold");

        ShouldHold(
            Decide(Finding(type, Reads(subject, 0.90m, 0.8999999m)), Reject, Settings(0.90m)),
            SubmissionHoldReasons.ReadFieldBelowThreshold);
    }

    // Condition 1 false alone: the type, the confidence and the settings all allow a rejection.

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void TheConsequenceNotReject_IsHeld_WhateverTheOtherConditions(RuleType type)
    {
        foreach (var consequence in new int?[] { Flag, Suggest })
        {
            ShouldHold(Decide(Finding(type), consequence, Settings()), SubmissionHoldReasons.ConsequenceNotReject);
        }
    }

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void AMissingOrUnknownConsequence_FailsClosed_AsAcceptedPlusFlag(RuleType type)
    {
        foreach (var consequence in new int?[] { null, 0, 7, -1, int.MaxValue, 100000003 })
        {
            var decision = Decide(Finding(type), consequence, Settings());
            ShouldHold(decision, SubmissionHoldReasons.ConsequenceUnknown);
            Only(decision).Consequence.Should().BeNull($"{consequence?.ToString() ?? "null"} is not one of the three defined consequences");
        }
    }

    [Fact]
    public void AFindingWhoseVersionIsNotInTheEvaluation_HasNoConsequence_AndFailsClosed()
    {
        var finding = Finding(RuleType.InvoiceAge);
        var known = Version("BILL-OTHER", RuleType.InvoiceAge) with { Consequence = Reject };

        var decision = SubmissionRejectionRule.Decide(
            [finding], versionId => versionId == known.PolicyVersionId ? known.Consequence : null, Settings());

        ShouldHold(decision, SubmissionHoldReasons.ConsequenceUnknown);
    }

    // Condition 2 false alone: Reject, an eligible type and a usable threshold.

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void OneReadFieldBelowTheThreshold_Holds_AndNamesTheField(RuleType type)
    {
        var subject = SubjectOf(type);

        var decision = Decide(Finding(type, Reads(subject, 0.99m, 0.89m, 0.99m)), Reject, Settings());

        ShouldHold(decision, SubmissionHoldReasons.ReadFieldBelowThreshold);
        Only(decision).UnreliableFields.Should().Equal(Ref(subject, 1));
    }

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void OneReadFieldWithNoConfidence_Holds_NotTrustedAsReliable(RuleType type)
    {
        var subject = SubjectOf(type);

        var decision = Decide(Finding(type, Reads(subject, 0.99m, null, 0.99m)), Reject, Settings());

        ShouldHold(decision, SubmissionHoldReasons.ReadFieldConfidenceUnknown);
        Only(decision).UnreliableFields.Should().Equal(Ref(subject, 1));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    [InlineData("5")]
    public void AConfidenceOutsideZeroToOne_IsUnknown_NotReliable(string confidence)
    {
        var finding = Finding(RuleType.ExpenseRule, Reads(BillingSubject.ForLine(0), 0.99m, D(confidence)));

        ShouldHold(Decide(finding, Reject, Settings(0.90m)), SubmissionHoldReasons.ReadFieldConfidenceUnknown);
        // Even a threshold the bad number "exceeds" does not let it through.
        ShouldHold(Decide(finding, Reject, Settings(0.50m, 0.40m)), SubmissionHoldReasons.ReadFieldConfidenceUnknown);
    }

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void AFindingThatListsNoReadField_Holds_NoEvidenceIsNotReliability(RuleType type)
    {
        var finding = Finding(type, [ReferenceValue("approvedRate", "575.00"), ReferenceValue("approvedRate.limit", "575.00")]);

        finding.ReadFields.Should().BeEmpty();
        ShouldHold(Decide(finding, Reject, Settings()), SubmissionHoldReasons.NoReadFields);
    }

    [Fact]
    public void ReferenceValuesAreNotJudged_OnlyTheExtractedFieldsTheFindingListsAre()
    {
        // 071: a reference value has no extraction confidence and is not a read field; a finding with one reliable extracted
        // field and any number of reference values can reject.
        var values = new List<BillingValueRead>
        {
            Extracted(new FieldRef(0, BillingFieldNames.Rate), 0.97m),
            ReferenceValue("approvedRate", "575.00"),
            ReferenceValue("approvedRate.basis", "Timekeeper"),
        };

        Decide(Finding(RuleType.ApprovedRate, values), Reject, Settings()).Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Fact]
    public void TheListedFieldsAreUsedAsStated_AFieldTheFindingDoesNotListIsNeverInferred()
    {
        // The finding reads only Rate. Hours would be low if it were read, but the finding does not list it, and the decider does not
        // work out which fields an ApprovedRate rule "should" have read (071: read fields are the finding's own).
        var finding = Finding(RuleType.ApprovedRate, [Extracted(new FieldRef(0, BillingFieldNames.Rate), 0.97m)]);

        finding.ReadFields.Should().Equal(new FieldRef(0, BillingFieldNames.Rate));
        Decide(finding, Reject, Settings()).Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Fact]
    public void TheSameFieldOfTwoLines_IsJudgedPerLine_ABadLineDoesNotBorrowAGoodOnesConfidence()
    {
        // The credit-line test appends Hours/Amount reads (071 fix round 2); the same field name on a different line is a different
        // field. A reliable field on line 1 does not cover an unreliable one on line 0.
        var values = new List<BillingValueRead>
        {
            Extracted(new FieldRef(0, BillingFieldNames.Hours), 0.40m),
            Extracted(new FieldRef(1, BillingFieldNames.Hours), 0.99m),
        };

        var decision = Decide(Finding(RuleType.RequiredData, values, subject: BillingSubject.ForLine(0)), Reject, Settings());

        ShouldHold(decision, SubmissionHoldReasons.ReadFieldBelowThreshold);
        Only(decision).UnreliableFields.Should().Equal(new FieldRef(0, BillingFieldNames.Hours));
    }

    [Fact]
    public void AFieldReadTwiceWithTwoConfidences_MustPassOnEveryValue()
    {
        var field = new FieldRef(0, BillingFieldNames.Amount);
        var values = new List<BillingValueRead> { Extracted(field, 0.99m), Extracted(field, 0.60m) };

        var decision = Decide(Finding(RuleType.ExpenseRule, values), Reject, Settings());

        ShouldHold(decision, SubmissionHoldReasons.ReadFieldBelowThreshold);
        Only(decision).UnreliableFields.Should().Equal(field);
    }

    // Condition 3 false alone: Reject, perfect confidence, usable threshold.

    [Theory]
    [MemberData(nameof(NeverRejecting))]
    public void ANonEligibleType_WithReject_AndPerfectConfidence_NeverRejects(RuleType type)
    {
        var finding = Finding(type, Reads(SubjectOf(type), 1m, 1m, 1m));

        ShouldHold(Decide(finding, Reject, Settings()), SubmissionHoldReasons.TypeNotRejectEligible);
    }

    [Fact]
    public void ANonBillingType_WithReject_NeverRejects()
    {
        foreach (var type in new[] { RuleType.Threshold, RuleType.Switch, RuleType.Existence, (RuleType)42 })
        {
            ShouldHold(Decide(Finding(type, Reads(BillingSubject.ForLine(0), 1m)), Reject, Settings()), SubmissionHoldReasons.TypeNotRejectEligible);
        }
    }

    // ── D-89: ApprovedTimekeeper holds ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void D89_ApprovedTimekeeperWithReject_AtThreshold080_NeverRejects()
    {
        // The must-fire control: a lower threshold (0.80) with a confident read of the timekeeper, and a Reject rule on a type FR-26
        // calls reject-eligible. D-89 holds by type, not by the 046 cap staying under the threshold.
        var finding = Finding(RuleType.ApprovedTimekeeper, [
            Extracted(new FieldRef(0, BillingFieldNames.TimekeeperAsBilled), 0.85m),
            Extracted(new FieldRef(0, BillingFieldNames.Date), 0.99m),
        ]);

        ShouldHold(Decide(finding, Reject, Settings(0.80m, 0.70m)), SubmissionHoldReasons.TypeHoldsOnly);
        ShouldHold(Decide(finding, Reject, Settings(0.50m, 0.40m)), SubmissionHoldReasons.TypeHoldsOnly);
    }

    [Fact]
    public void D89_ThroughTheRealHandler_AnApprovedTimekeeperFinding_NeverRejects_EvenWhenTheStoredRowSaysReject()
    {
        var request = Request(Fee(0) with { Timekeeper = Unmatched(TimekeeperUnmatchedReason.NotOnRoster) });
        var flag = Version("BILL-TK", RuleType.ApprovedTimekeeper) with { Consequence = Flag };
        var result = Evaluator().Evaluate(request, Loaded(flag));
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.RuleType.Should().Be(RuleType.ApprovedTimekeeper);

        // A stored Reject (written around the publish check) and a threshold of 0.80.
        ShouldHold(
            SubmissionRejectionRule.Decide(result.Findings, _ => Reject, Settings(0.80m, 0.70m)),
            SubmissionHoldReasons.TypeHoldsOnly);
    }

    [Fact]
    public void D89_ACappedReferenceMissingField_At046sCap_CannotSupportARejection_WhenTheSettingsAreValid()
    {
        // 046: a roster miss caps a field at ReferenceMissingCap (0.80); with the cap below the threshold it never reaches it.
        var settings = Settings(0.90m, 0.80m);
        var capped = CombinedConfidenceCalculator.Calculate(
            new FieldSignals(1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Missing), settings).Combined;
        capped.Should().Be(0.80m);
        var finding = Finding(RuleType.ApprovedRate, [Extracted(new FieldRef(0, BillingFieldNames.TimekeeperAsBilled), capped)]);

        ShouldHold(Decide(finding, Reject, settings), SubmissionHoldReasons.ReadFieldBelowThreshold);
    }

    [Theory]
    [InlineData("0.80", "0.80")]
    [InlineData("0.80", "0.85")]
    [InlineData("0.85", "0.85")]
    [InlineData("0.85", "0.95")]
    public void D89_WhenTheCapIsNotBelowTheThreshold_NothingRejects_EvenAFieldAtTheCap(string threshold, string cap)
    {
        // The settings break the invariant: a reference-missing field (capped) would reach the threshold. The rule refuses to act.
        var settings = Settings(D(threshold), D(cap));
        var capped = CombinedConfidenceCalculator.Calculate(
            new FieldSignals(1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Missing), settings).Combined;
        capped.Should().BeGreaterThanOrEqualTo(D(threshold), "the field at its cap reaches the threshold in this configuration");
        settings.RejectionInvariantProblem.Should().NotBeNull();

        var rate = Finding(RuleType.ApprovedRate, [Extracted(new FieldRef(0, BillingFieldNames.Rate), capped)]);
        var timekeeper = Finding(RuleType.ApprovedTimekeeper, [Extracted(new FieldRef(0, BillingFieldNames.TimekeeperAsBilled), capped)]);

        ShouldHold(Decide(rate, Reject, settings), SubmissionHoldReasons.SettingsInvalid);
        ShouldHold(Decide(timekeeper, Reject, settings), SubmissionHoldReasons.TypeHoldsOnly);
    }

    // ApprovedRate with no approved rate to compare against holds (071 judgment call 2).

    [Theory]
    [InlineData(BillingReasonCodes.RateUnresolved)]
    [InlineData(BillingReasonCodes.RateTimekeeperUnmatched)]
    [InlineData(BillingReasonCodes.RateLineDateMissing)]
    [InlineData(BillingReasonCodes.RateNoRateInForce)]
    [InlineData(BillingReasonCodes.RateAmbiguous)]
    public void ApprovedRate_WithNoApprovedRate_Holds_EvenWithReliableReadsAndReject(string reason)
    {
        var finding = Finding(RuleType.ApprovedRate, reason: reason);

        ShouldHold(Decide(finding, Reject, Settings()), SubmissionHoldReasons.ReferenceMissingHolds);
    }

    [Fact]
    public void ApprovedRate_AboveTheApprovedRate_Rejects_TheControlForTheHolds()
    {
        Decide(Finding(RuleType.ApprovedRate, reason: BillingReasonCodes.RateAboveApproved), Reject, Settings())
            .Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    // Duplicate rejects at invoice level only.

    [Theory]
    [InlineData(BillingReasonCodes.DuplicateOfPriorSubmissionLine)]
    [InlineData(BillingReasonCodes.DuplicateOfEarlierLine)]
    public void ALineLevelDuplicate_NeverRejects(string reason)
    {
        var line = BillingSubject.ForLine(2);
        var finding = Finding(RuleType.Duplicate, Reads(line, 0.99m, 0.99m), reason, line);

        ShouldHold(Decide(finding, Reject, Settings()), SubmissionHoldReasons.DuplicateAtLineLevel);
    }

    [Fact]
    public void AnInvoiceLevelDuplicate_Rejects()
    {
        var invoice = BillingSubject.ForInvoice();
        var finding = Finding(RuleType.Duplicate, Reads(invoice, 0.99m), BillingReasonCodes.DuplicateInvoiceNumber, invoice);

        Decide(finding, Reject, Settings()).Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    // ── 073 review F-ALLOW: only an allowlisted reason code may reject ───────────────────────────────────────────

    /// <summary>Every (type, reason) pair the catalogue lists as reject-capable.</summary>
    public static TheoryData<RuleType, string> ListedReasons()
    {
        var data = new TheoryData<RuleType, string>();
        foreach (var type in BillingRuleCatalogue.Types)
        {
            foreach (var reason in BillingRuleCatalogue.RejectCapableReasons(type).Order(StringComparer.Ordinal))
            {
                data.Add(type, reason);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void AnUnlistedReasonCode_UnderARejectVersion_WithReliableReads_DoesNotReject(RuleType type)
    {
        // A reason code added later (a new sub-reason) is hold-by-default until it is listed on purpose.
        var finding = Finding(type, Reads(SubjectOf(type), 0.99m, 0.99m), type.ToString().ToLowerInvariant() + ".a_new_reason");

        // ApprovedRate already holds every reason but a rate above the approved one, under its own, more specific code.
        ShouldHold(
            Decide(finding, Reject, Settings()),
            type == RuleType.ApprovedRate ? SubmissionHoldReasons.ReferenceMissingHolds : SubmissionHoldReasons.ReasonNotRejectCapable);

        // 072 part E's billing.currency.* reasons are off the allowlist too, but flag-only by reason code, which is checked first.
        var currency = Finding(type, Reads(SubjectOf(type), 0.99m, 0.99m), BillingEvaluationReasons.CurrencyMissing);
        BillingRuleCatalogue.IsRejectCapableReason(type, BillingEvaluationReasons.CurrencyMissing).Should().BeFalse();
        ShouldHold(Decide(currency, Reject, Settings()), SubmissionHoldReasons.ReasonFlagOnly);
    }

    [Fact]
    public void AForeignCurrencyFinding_IsFlagOnly_ItsReasonIsNotListed_D115()
    {
        var invoice = BillingSubject.ForInvoice();
        var finding = Finding(RuleType.RequiredData, Reads(invoice, 0.99m), BillingReasonCodes.CurrencyNotUsd, invoice);

        // Two independent guards: it is off the allowlist, and (072 part E) it is flag-only by reason code, which is checked first.
        BillingRuleCatalogue.IsRejectCapableReason(RuleType.RequiredData, BillingReasonCodes.CurrencyNotUsd).Should().BeFalse();
        BillingRuleCatalogue.IsFlagOnlyReason(BillingReasonCodes.CurrencyNotUsd).Should().BeTrue();
        ShouldHold(Decide(finding, Reject, Settings()), SubmissionHoldReasons.ReasonFlagOnly);
    }

    [Theory]
    [MemberData(nameof(ListedReasons))]
    public void AListedReasonCode_StillRejects_TheControl(RuleType type, string reason)
    {
        var finding = Finding(type, Reads(SubjectOf(type), 0.99m, 0.99m), reason);

        Decide(finding, Reject, Settings()).Outcome.Should().Be(SubmissionOutcome.Rejected, $"{type} {reason} is on the allowlist");
    }

    [Fact]
    public void TheAllowlist_IsEmptyForEveryTypeThatMayNotReject_AndNonEmptyForEveryTypeThatMay()
    {
        foreach (var type in BillingRuleCatalogue.Types)
        {
            if (BillingRuleCatalogue.IsConsequenceAllowed(type, Reject))
            {
                BillingRuleCatalogue.RejectCapableReasons(type).Should().NotBeEmpty(type.ToString());
            }
            else
            {
                BillingRuleCatalogue.RejectCapableReasons(type).Should().BeEmpty(type.ToString());
            }
        }

        ((Action)(() => BillingRuleCatalogue.RejectCapableReasons(RuleType.Threshold))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [MemberData(nameof(ListedReasons))]
    public void EveryAllowlistedReasonCode_IsARealCode_EmittedByItsTypesHandler(RuleType type, string reason)
    {
        // The guard that keeps the allowlist from rotting: the code is a BillingReasonCodes constant, and the type's own handler source
        // references that constant (a renamed or removed code fails here, not silently in production).
        var constant = typeof(BillingReasonCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && (string?)f.GetRawConstantValue() == reason)
            .Select(f => f.Name)
            .ToArray();
        constant.Should().ContainSingle($"'{reason}' must be exactly one BillingReasonCodes constant");

        var handler = File.ReadAllText(HandlerSource(type));
        handler.Should().Contain($"BillingReasonCodes.{constant[0]}", $"{type}Handler must emit {reason}");
    }

    private static string HandlerSource(RuleType type)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var handlers = Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals", "Billing", "Handlers");
            if (Directory.Exists(handlers))
            {
                return Path.Combine(handlers, $"{type}Handler.cs");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }

    // ── The settings ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void UnreadableSettings_NothingRejects(RuleType type)
    {
        var decision = Decide(Finding(type), Reject, settings: null);

        ShouldHold(decision, SubmissionHoldReasons.SettingsReadFailed);
        decision.ReliabilityThreshold.Should().BeNull();
    }

    [Theory]
    [InlineData("0.90", "0.90", true)]
    [InlineData("0.90", "0.95", true)]
    [InlineData("0.80", "0.80", true)]
    [InlineData("0.00", "0.00", true)]
    [InlineData("1.01", "0.50", true)]
    [InlineData("-0.50", "-0.70", true)]
    [InlineData("0.90", "0.80", false)]
    [InlineData("0.80", "0.79", false)]
    [InlineData("1.00", "0.99", false)]
    [InlineData("0.01", "0.00", false)]
    public void TheRejectionInvariant_IsReferenceMissingCapBelowAThresholdInZeroExclusiveToOne(string threshold, string cap, bool broken)
    {
        var settings = Settings(D(threshold), D(cap));

        (settings.RejectionInvariantProblem is not null).Should().Be(broken);
        var reliable = Math.Min(1m, settings.ReliabilityThreshold);
        var decision = Decide(Finding(RuleType.RequiredData, Reads(BillingSubject.ForLine(0), reliable, reliable)), Reject, settings);
        decision.Outcome.Should().Be(broken ? SubmissionOutcome.Accepted : SubmissionOutcome.Rejected);
        if (broken)
        {
            ShouldHold(decision, SubmissionHoldReasons.SettingsInvalid);
            decision.ReliabilityThreshold.Should().BeNull("a threshold that breaks the invariant is not applied");
        }
    }

    [Fact]
    public void TheDefaultSettings_KeepTheInvariant()
    {
        new EbillingSettings().RejectionInvariantProblem.Should().BeNull();
    }

    [Fact]
    public async Task AChangedThreshold_AppliesToTheNextDecision_WithoutARestart()
    {
        var settings = new FakeSettings(Settings(0.90m, 0.80m));
        var rule = Sut(settings);
        var version = Version("BILL-AGE", RuleType.InvoiceAge) with { Consequence = Reject };
        var finding = Finding(RuleType.InvoiceAge, Reads(BillingSubject.ForLine(0), 0.85m, 0.85m), version: version.PolicyVersionId);
        var evaluation = ResultOf([finding], version);

        var strict = await rule.DecideAsync(evaluation, CancellationToken.None);
        settings.Current = Settings(0.80m, 0.70m);
        var relaxed = await rule.DecideAsync(evaluation, CancellationToken.None);
        settings.Current = Settings(0.99m, 0.80m);
        var strictest = await rule.DecideAsync(evaluation, CancellationToken.None);

        strict.Outcome.Should().Be(SubmissionOutcome.Accepted);
        strict.ReliabilityThreshold.Should().Be(0.90m);
        relaxed.Outcome.Should().Be(SubmissionOutcome.Rejected, "0.85 reaches the lowered threshold of 0.80");
        relaxed.ReliabilityThreshold.Should().Be(0.80m);
        strictest.Outcome.Should().Be(SubmissionOutcome.Accepted);
    }

    [Fact]
    public async Task TheDecisionUsesTheThresholdNow_NotTheFlagTheEvaluationSetEarlier()
    {
        // BelowReliabilityThreshold was set against whatever threshold the evaluation read; the decision re-compares the confidence.
        var settings = new FakeSettings(Settings(0.70m, 0.60m));
        var version = Version("BILL-AGE", RuleType.InvoiceAge) with { Consequence = Reject };
        var values = Reads(BillingSubject.ForLine(0), 0.75m, 0.75m);
        values.Should().OnlyContain(v => v.BelowReliabilityThreshold, "flagged against the 0.90 the evaluation used");
        var finding = Finding(RuleType.InvoiceAge, values, version: version.PolicyVersionId);

        var decision = await Sut(settings).DecideAsync(ResultOf([finding], version), CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Fact]
    public async Task ASettingsReadThatFails_IsLogged_AndNothingRejects()
    {
        var settings = new FakeSettings(null) { Failure = new InvalidOperationException("Dataverse is down") };
        var logger = new CapturingLogger<SubmissionRejectionRule>();
        var version = Version("BILL-AGE", RuleType.InvoiceAge) with { Consequence = Reject };
        var finding = Finding(RuleType.InvoiceAge, version: version.PolicyVersionId);

        var decision = await Sut(settings, logger).DecideAsync(ResultOf([finding], version), CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.SettingsReadFailed);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains("could not be read"));
    }

    [Fact]
    public async Task ACancelledSettingsRead_IsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var settings = new FakeSettings(null) { Failure = new OperationCanceledException(cts.Token) };

        var act = () => Sut(settings).DecideAsync(ResultOf([]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SettingsThatBreakTheInvariant_AreLogged_AndNothingRejects()
    {
        var logger = new CapturingLogger<SubmissionRejectionRule>();
        var version = Version("BILL-AGE", RuleType.InvoiceAge) with { Consequence = Reject };
        var finding = Finding(RuleType.InvoiceAge, version: version.PolicyVersionId);

        var decision = await Sut(new FakeSettings(Settings(0.80m, 0.90m)), logger).DecideAsync(ResultOf([finding], version), CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.SettingsInvalid);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("cannot back a rejection"));
    }

    [Fact]
    public void ASettingsReadFailure_AndInvalidSettings_HoldUnderTwoDistinctReasons_K1()
    {
        // 073 review K1: a failed read is transient (049 may retry); a broken invariant lasts until the row is fixed. Both never reject.
        var finding = Finding(RuleType.InvoiceAge);

        ShouldHold(Decide(finding, Reject, settings: null), SubmissionHoldReasons.SettingsReadFailed);
        ShouldHold(Decide(finding, Reject, Settings(0.80m, 0.90m)), SubmissionHoldReasons.SettingsInvalid);
        ShouldHold(Decide(finding, Reject, Settings() with { ArithmeticFailCap = 0.95m }), SubmissionHoldReasons.SettingsInvalid);
        SubmissionHoldReasons.SettingsReadFailed.Should().Be("settings_read_failed");
        SubmissionHoldReasons.SettingsInvalid.Should().Be("settings_invalid");
    }

    // ── AiObservation (I-5) and stored Rejects that a type may not carry ─────────────────────────────────────────

    [Fact]
    public async Task AnAiObservation_WithAStoredRejectRow_NeverRejects_AndTheStoredRowIsLogged()
    {
        var logger = new CapturingLogger<SubmissionRejectionRule>();
        var version = Version("BILL-AI", RuleType.AiObservation) with { Consequence = Reject };
        var line = BillingSubject.ForLine(0);
        var first = Finding(RuleType.AiObservation, Reads(line, 1m, 1m), version: version.PolicyVersionId);
        var second = Finding(RuleType.AiObservation, Reads(BillingSubject.ForLine(1), 1m, 1m), subject: BillingSubject.ForLine(1), version: version.PolicyVersionId);

        var decision = await Sut(new FakeSettings(Settings()), logger).DecideAsync(ResultOf([first, second], version), CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Accepted);
        decision.Findings.Should().OnlyContain(f => !f.Rejects && f.HeldBecause == SubmissionHoldReasons.TypeNotRejectEligible);
        decision.Findings.Should().OnlyContain(f => f.Consequence == BillingConsequence.Reject, "the decision reports the consequence it was given");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("BILL-AiObservation") && e.Message.Contains("2 finding"),
            "one warning per policy version, not per finding");
    }

    [Fact]
    public async Task AnApprovedTimekeeper_WithAStoredRejectRow_IsLogged()
    {
        var logger = new CapturingLogger<SubmissionRejectionRule>();
        var version = Version("BILL-TK", RuleType.ApprovedTimekeeper) with { Consequence = Reject };
        var finding = Finding(RuleType.ApprovedTimekeeper, version: version.PolicyVersionId);

        var decision = await Sut(new FakeSettings(Settings()), logger).DecideAsync(ResultOf([finding], version), CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.TypeHoldsOnly);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("BILL-ApprovedTimekeeper"));
    }

    [Fact]
    public async Task AnOrdinaryFlag_LogsNothing()
    {
        var logger = new CapturingLogger<SubmissionRejectionRule>();
        var version = Version("BILL-AI", RuleType.AiObservation) with { Consequence = Flag };
        var finding = Finding(RuleType.AiObservation, version: version.PolicyVersionId);

        var decision = await Sut(new FakeSettings(Settings()), logger).DecideAsync(ResultOf([finding], version), CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.ConsequenceNotReject);
        logger.Entries.Should().BeEmpty();
    }

    // ── Several findings ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARejectingFindingAmongFlags_RejectsTheSubmission_AndTheDecidingFindingsAreOnlyThatOne()
    {
        var reject = Finding(RuleType.ExpenseRule, subject: BillingSubject.ForLine(1), values: Reads(BillingSubject.ForLine(1), 0.97m));
        var flag = Finding(RuleType.TimeIncrement, subject: BillingSubject.ForLine(0));
        var lowConfidence = Finding(RuleType.InvoiceAge, subject: BillingSubject.ForLine(2), values: Reads(BillingSubject.ForLine(2), 0.40m));
        var duplicateOfLine = Finding(RuleType.Duplicate, subject: BillingSubject.ForLine(3), reason: BillingReasonCodes.DuplicateOfEarlierLine);
        var consequences = new Dictionary<Guid, int>
        {
            [reject.PolicyVersionId] = Reject,
            [flag.PolicyVersionId] = Flag,
            [lowConfidence.PolicyVersionId] = Reject,
            [duplicateOfLine.PolicyVersionId] = Reject,
        };

        var decision = SubmissionRejectionRule.Decide([flag, reject, lowConfidence, duplicateOfLine], id => consequences[id], Settings());

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        decision.Findings.Select(f => f.Index).Should().Equal(0, 1, 2, 3);
        decision.Findings.Select(f => f.Finding).Should().Equal(flag, reject, lowConfidence, duplicateOfLine);
        decision.Findings.Select(f => f.HeldBecause).Should().Equal(
            SubmissionHoldReasons.ConsequenceNotReject, null, SubmissionHoldReasons.ReadFieldBelowThreshold, SubmissionHoldReasons.DuplicateAtLineLevel);
        decision.DecidingFindings.Should().ContainSingle().Which.Finding.Should().Be(reject);
    }

    [Fact]
    public void NoFindings_IsAccepted()
    {
        var decision = SubmissionRejectionRule.Decide([], _ => Reject, Settings());

        decision.Outcome.Should().Be(SubmissionOutcome.Accepted);
        decision.Findings.Should().BeEmpty();
        decision.DecidingFindings.Should().BeEmpty();
        decision.ReliabilityThreshold.Should().Be(0.90m);
    }

    [Fact]
    public void TheSameInput_GivesTheSameDecision()
    {
        var findings = new[]
        {
            Finding(RuleType.ExpenseRule, subject: BillingSubject.ForLine(0)),
            Finding(RuleType.InvoiceAge, subject: BillingSubject.ForLine(1), values: Reads(BillingSubject.ForLine(1), 0.4m)),
        };

        var first = SubmissionRejectionRule.Decide(findings, _ => Reject, Settings());
        var second = SubmissionRejectionRule.Decide(findings, _ => Reject, Settings());

        second.Should().BeEquivalentTo(first, o => o.WithStrictOrdering());
    }

    // ── A withheld Duplicate (D-112) ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AWithheldDuplicate_ShowsNoValueText_HoweverTheFindingWasBuilt()
    {
        var invoice = BillingSubject.ForInvoice();
        var values = new List<BillingValueRead> { Extracted(new FieldRef(null, BillingFieldNames.FirmInvoiceNumber), 0.99m, "INV-100.1") };
        var finding = Finding(RuleType.Duplicate, values, BillingReasonCodes.DuplicateInvoiceNumber, invoice, withheld: true);

        var decision = Decide(finding, Reject, Settings());

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected, "withholding the detail does not stop the rejection");
        var rejecting = Only(decision);
        rejecting.ValuesRead.Should().OnlyContain(v => v.Value == null, "the withheld value text is never carried on");
        rejecting.ValuesRead.Single().ExtractedField.Should().Be(new FieldRef(null, BillingFieldNames.FirmInvoiceNumber));
        rejecting.ValuesRead.Single().Confidence.Should().Be(0.99m);
        JsonSerializer.Serialize(decision.Findings.Select(f => f.ValuesRead)).Should().NotContain("100.1");
    }

    [Fact]
    public void ADuplicateThatIsNotWithheld_KeepsItsValues()
    {
        var invoice = BillingSubject.ForInvoice();
        var finding = Finding(RuleType.Duplicate, [Extracted(new FieldRef(null, BillingFieldNames.FirmInvoiceNumber), 0.99m, "INV-100")],
            BillingReasonCodes.DuplicateInvoiceNumber, invoice);

        Only(Decide(finding, Reject, Settings())).ValuesRead.Should().ContainSingle().Which.Value.Should().Be("INV-100");
    }

    private static BillingSignalSubjects SubjectsFor(BillingEvaluationRequest request) => new(
        InvoiceRowId, "INV-100", request.Lines.ToDictionary(l => l.Index, l => Id("line-row:" + l.Index)));

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
    public async Task AWithheldDuplicate_ThroughEvaluateDecideMapAndWrite_PutsNoTrackingNumberAnywhere_D112()
    {
        // 071 round 2: a withheld Duplicate renders and stores only the withheld template and writes no tracking number anywhere.
        const string hiddenTrackingNumber = "INV-100.1";
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate, template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template)
            with { Consequence = Reject };
        var request = Request(Fee(0));
        var acrossTheWall = Prior(hiddenTrackingNumber, "INV-100", 1, BillingPriorStatus.Active,
            new BillingPriorLine { Date = LineDate, TimekeeperId = Matched().TimekeeperId, Hours = 1.0m }) with
        {
            SecureRoots = Roots(Id("secure-matter")),
        };
        var result = Evaluator().Evaluate(request, Loaded(rule) with { PriorSubmissions = [acrossTheWall] });
        result.Findings.Should().HaveCount(2).And.OnlyContain(f => f.DetailWithheld);

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected, "an invoice-level duplicate across the wall rejects; the detail is what is withheld");
        decision.DecidingFindings.Should().ContainSingle().Which.Finding.Subject.Kind.Should().Be(BillingSubjectKind.Invoice);
        decision.Findings.Single(f => f.Finding.Subject.Kind == BillingSubjectKind.Line).HeldBecause
            .Should().Be(SubmissionHoldReasons.DuplicateAtLineLevel);
        var decisionText = JsonSerializer.Serialize(decision);
        decisionText.Should().NotContain(hiddenTrackingNumber).And.NotContain("100.1");

        var subjects = SubjectsFor(request);
        var (writer, org) = Writer(subjects);
        foreach (var finding in decision.Findings.Select(d => d.Finding))
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
                text.Should().NotContain("100.1", $"column {column} must not carry any part of the withheld tracking number");
            }
        }
    }

    [Fact]
    public async Task AnInvoiceLevelDuplicate_WithTheDefaultTemplate_RejectsAndIsWritten_NotRefusedByTheWriter()
    {
        // 071 round 2: the default Duplicate template is chosen by subject, so an invoice-level Duplicate is written with its wording.
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate, """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""",
            template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template) with { Consequence = Reject };
        var request = Request(Fee(0));
        var result = Evaluator().Evaluate(request, Loaded(rule) with { PriorSubmissions = [Prior("INV-100.1", "INV-100", 1)] });

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        var rejecting = decision.DecidingFindings.Should().ContainSingle().Subject;
        var subjects = SubjectsFor(request);
        var mapping = BillingFindingSignalMapper.Map(rejecting.Finding, rule, subjects);
        var (writer, org) = Writer(subjects);

        var written = await writer.WriteAsync(mapping.Request);

        mapping.MessageSource.Should().Be(BillingMessageSource.InvoiceLevelDuplicate);
        org.Rows[written.SignalId].GetAttributeValue<string>("sprk_sentence").Should().Be("Invoice INV-100 duplicates INV-100.1.");
    }

    // ── D-115: the "rates not checked" finding never rejects ─────────────────────────────────────────────────────

    [Fact]
    public async Task RatesNotChecked_UnderAnExpenseRuleRejectVersion_NeverRejects_EvenWithTheCurrencyReadReliably_D115()
    {
        // Without the flag-only rule this finding would reject: ExpenseRule is reject-eligible, the version says Reject, and the one
        // field it read (Currency, EUR at 0.95) is reliable.
        var rule = Version("BILL-EXPENSE", RuleType.ExpenseRule) with { Consequence = Reject };
        var request = Request(Expense(0) with { ExpenseCode = "E101", Amount = 600m }) with { Currency = "EUR" };
        var result = Evaluator().Evaluate(request, Loaded(rule));
        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingEvaluationReasons.CurrencyNotUsd);

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.ReasonFlagOnly);
    }

    [Fact]
    public async Task ARejectCapableReason_OfTheSameMoneyPolicy_KeepsItsReject_AndOnlyItDecides_D115()
    {
        var rule = Version("BILL-EXPENSE", RuleType.ExpenseRule) with { Consequence = Reject };
        // D-117: the rejection judges the firm-billed code; with only the AI-assigned code the finding would hold (ai_assigned_input).
        var request = Request(Expense(0) with { ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m }) with { Currency = "EUR" };
        var result = Evaluator().Evaluate(request, Loaded(rule));
        result.Findings.Select(f => f.ReasonCode).Should().Equal(BillingEvaluationReasons.CurrencyNotUsd, BillingReasonCodes.ExpenseCodeNotReimbursable);

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        decision.DecidingFindings.Should().ContainSingle().Which.Finding.ReasonCode.Should().Be(BillingReasonCodes.ExpenseCodeNotReimbursable);
        decision.Findings.Single(f => f.Finding.ReasonCode == BillingEvaluationReasons.CurrencyNotUsd).HeldBecause
            .Should().Be(SubmissionHoldReasons.ReasonFlagOnly, "the rejection is never BECAUSE of the currency");
    }

    // ── Through the real evaluator ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARealEvaluation_WithAReliableOverBilledRate_AndAnApprovedRateRejectRule_RejectsTheSubmission()
    {
        var rule = Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Reject };
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m });
        var result = Evaluator().Evaluate(request, Loaded(rule));
        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.RateAboveApproved);

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        decision.DecidingFindings.Single().Finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.Rate));
    }

    // ── 071 round 3 (F4): a line with neither hours nor amount cannot be shown not to be a credit ─────────────────────

    [Fact]
    public async Task AnApprovedRateFinding_OnALineWithNeitherHoursNorAmount_IsHeld_NotRejected_071Round3()
    {
        // "(2.5) @ 650 = (1,625.00)": neither bracketed number parsed, the rate did. Judged as a charge, the finding must not reject.
        var rule = Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Reject };
        var request = Request(Fee(0) with { BilledRate = 650m, Hours = null, Amount = null });
        var result = Evaluator().Evaluate(request, Loaded(rule));
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.ReasonCode.Should().Be(BillingReasonCodes.RateAboveApproved);
        foreach (var field in new[] { BillingFieldNames.Hours, BillingFieldNames.Amount })
        {
            var reads = finding.ValuesRead.Where(v => v.ExtractedField == new FieldRef(0, field)).ToList();
            reads.Should().ContainSingle($"the credit test's {field} is listed although the line has none");
            reads[0].Value.Should().BeNull();
            reads[0].Confidence.Should().BeNull();
            reads[0].BelowReliabilityThreshold.Should().BeTrue();
        }

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().NotBe(SubmissionOutcome.Rejected);
        ShouldHold(decision, SubmissionHoldReasons.ReadFieldConfidenceUnknown);
        decision.Findings.Single().UnreliableFields.Should().BeEquivalentTo([new FieldRef(0, BillingFieldNames.Hours), new FieldRef(0, BillingFieldNames.Amount)]);
    }

    [Fact]
    public async Task OnlyOneOfHoursAndAmountAbsent_IsNotInvented_TheFindingStillRejects_071Round3()
    {
        // Negative case: an expense line has no hours. Its amount answers the credit test, so nothing is added and the Reject stands.
        var rule = ExpenseRejectRule("E109");
        var line = Expense(0) with { BilledExpenseCode = "E109", Hours = null, Amount = 60m };
        var result = Evaluator().Evaluate(Request(line), Loaded(rule));
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.ReadFields.Should().NotContain(new FieldRef(0, BillingFieldNames.Hours));

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Fact]
    public async Task AnApprovedRateFinding_OnAFeeLineWithHoursButNoAmount_IsHeld_NotRejected_A7Extension()
    {
        // 072 W review, A7 extension: "2.5 @ 650 = (1,625.00)" - the hours parsed and are positive, the bracketed amount did not. The
        // credit test (hours < 0 or amount < 0) has nothing to say about the amount, and the amount may be the negative one.
        var rule = Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Reject };
        var request = Request(Fee(0) with { BilledRate = 650m, Hours = 2.5m, Amount = null });
        var result = Evaluator().Evaluate(request, Loaded(rule));
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.ReasonCode.Should().Be(BillingReasonCodes.RateAboveApproved);
        var amount = finding.ValuesRead.Where(v => v.ExtractedField == new FieldRef(0, BillingFieldNames.Amount)).Should().ContainSingle(
            "the credit test's Amount is listed although the line has none").Subject;
        amount.Value.Should().BeNull();
        amount.Confidence.Should().BeNull();
        amount.BelowReliabilityThreshold.Should().BeTrue();
        finding.ValuesRead.Where(v => v.ExtractedField == new FieldRef(0, BillingFieldNames.Hours)).Should().ContainSingle()
            .Which.BelowReliabilityThreshold.Should().BeFalse("the hours were read reliably and stay a normal read");

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().NotBe(SubmissionOutcome.Rejected);
        ShouldHold(decision, SubmissionHoldReasons.ReadFieldConfidenceUnknown);
        decision.Findings.Single().UnreliableFields.Should().BeEquivalentTo([new FieldRef(0, BillingFieldNames.Amount)]);
    }

    [Fact]
    public async Task AnApprovedRateFinding_OnAFeeLineWithHoursAndAnAmount_StillRejects_A7Extension()
    {
        // Control: both parsed, nothing is added, the Reject stands.
        var rule = Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Reject };
        var request = Request(Fee(0) with { BilledRate = 650m, Hours = 2.5m, Amount = 1625m });
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    // ── 073 review F1-a: never reject on an AI-assigned input ────────────────────────────────────────────────────

    private static BillingRuleCandidate ExpenseRejectRule(string code) =>
        Version("BILL-EXP-" + code, RuleType.ExpenseRule, $$"""{"type":"ExpenseRule","nonReimbursableCodes":["{{code}}"]}""") with { Consequence = Reject };

    /// <summary>The F1-a scenario: the firm billed <paramref name="billed"/> (read at 0.97); the coding Action assigned E109 (its self-reported 0.92).</summary>
    private static BillingLineFacts BilledAndAssigned(string? billed) => Expense(0) with
    {
        ExpenseCode = "E109",
        BilledExpenseCode = billed,
        FieldConfidence = Confidence().With(BillingFieldNames.ExpenseCode, 0.92m).With(BillingFieldNames.BilledExpenseCode, billed is null ? null : 0.97m),
    };

    [Fact]
    public async Task ARealEvaluation_BilledE101_AssignedE109_ARuleOnE109_DoesNotReject_F1a_D117()
    {
        // D-117: ExpenseRule judges the code the firm billed, so a rule on the AI's E109 does not fire at all; nothing can reject.
        var result = Evaluator().Evaluate(Request(BilledAndAssigned("E101")), Loaded(ExpenseRejectRule("E109")));
        result.Findings.Should().BeEmpty();

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Accepted);
    }

    [Fact]
    public async Task ARealEvaluation_BilledE101_ARuleOnE101_FiresOnTheBilledCode_AndRejects_D117()
    {
        var result = Evaluator().Evaluate(Request(BilledAndAssigned("E101")), Loaded(ExpenseRejectRule("E101")));
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.BilledExpenseCode)).And.NotContain(new FieldRef(0, BillingFieldNames.ExpenseCode));

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Fact]
    public async Task ARealEvaluation_ABilledE109ReadReliably_WithReject_StillRejects_TheHoldDoesNotFire_F1a()
    {
        var result = Evaluator().Evaluate(Request(BilledAndAssigned("E109")), Loaded(ExpenseRejectRule("E109")));
        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.ExpenseCodeNotReimbursable);

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Fact]
    public async Task ARealEvaluation_NoBilledCode_TheFallbackOnTheAiAssignedE109_Holds_EvenAtHighCodingConfidence_F1a()
    {
        // The fallback finding reads the AI's E109 (0.92 clears the 0.90 threshold), but that is the model's self-report, not a D-47
        // combined score: hold, never reject.
        var result = Evaluator().Evaluate(Request(BilledAndAssigned(null)), Loaded(ExpenseRejectRule("E109")));
        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.ExpenseCodeNotReimbursable);

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.AiAssignedInput);
        Only(decision).UnreliableFields.Should().Equal(new FieldRef(0, BillingFieldNames.ExpenseCode));
    }

    [Fact]
    public async Task ARealEvaluation_ReadingOnlyExtractedFieldsWithCombinedScores_StillRejects_F1aControl()
    {
        // The must-not-fire control: an InvoiceAge finding reads the line date and the invoice date (extracted, D-47 scores) and nothing
        // the AI assigned, so the AI-assigned hold leaves it alone and it rejects.
        var rule = Version("BILL-AGE", RuleType.InvoiceAge, """{"type":"InvoiceAge","maxAgeDays":10}""") with { Consequence = Reject };
        var result = Evaluator().Evaluate(Request(Fee(0)), Loaded(rule));
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.ReadFields.Should().NotBeEmpty().And.OnlyContain(f => !BillingFieldNames.IsAiAssigned(f.Field));

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Theory]
    [InlineData(BillingFieldNames.TaskCode)]
    [InlineData(BillingFieldNames.ActivityCode)]
    [InlineData(BillingFieldNames.ExpenseCode)]
    [InlineData(BillingFieldNames.ObservationBlockBilling)]
    [InlineData("Observation.SomethingNew")]
    public void AFindingThatReadsAnAiAssignedField_Holds_WhateverItsConfidence(string aiField)
    {
        var line = BillingSubject.ForLine(0);
        var values = new List<BillingValueRead> { Extracted(new FieldRef(0, BillingFieldNames.Amount), 1m), Extracted(new FieldRef(0, aiField), 1m) };

        var decision = Decide(Finding(RuleType.ExpenseRule, values, subject: line), Reject, Settings());

        ShouldHold(decision, SubmissionHoldReasons.AiAssignedInput);
        Only(decision).UnreliableFields.Should().Equal(new FieldRef(0, aiField));
    }

    [Theory]
    [InlineData(BillingFieldNames.BilledTaskCode)]
    [InlineData(BillingFieldNames.BilledActivityCode)]
    [InlineData(BillingFieldNames.BilledExpenseCode)]
    [InlineData("ObservationLike")]
    public void TheFirmBilledCodes_AreExtracted_NotAiAssigned_AndCanReject(string field)
    {
        BillingFieldNames.IsAiAssigned(field).Should().BeFalse();
        var values = new List<BillingValueRead> { Extracted(new FieldRef(0, field), 0.99m) };

        Decide(Finding(RuleType.ExpenseRule, values, subject: BillingSubject.ForLine(0)), Reject, Settings())
            .Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Fact]
    public async Task ARealEvaluation_WithAMisreadRate_DoesNotReject_NFR06()
    {
        // The rate was read at 0.60: the over-billing may be a misread value, so the invoice is accepted with a Flag.
        var rule = Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Reject };
        var lowRate = Fee(0) with { BilledRate = 700m, Amount = 700m, FieldConfidence = Confidence().With(BillingFieldNames.Rate, 0.60m) };
        var result = Evaluator().Evaluate(Request(lowRate), Loaded(rule));
        result.Findings.Should().ContainSingle();

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.ReadFieldBelowThreshold);
        Only(decision).UnreliableFields.Should().Equal(new FieldRef(0, BillingFieldNames.Rate));
    }

    [Fact]
    public async Task ARealEvaluation_WithAMissingConfidence_DoesNotReject()
    {
        var rule = Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Reject };
        var unknown = Fee(0) with { BilledRate = 700m, Amount = 700m, FieldConfidence = Confidence().With(BillingFieldNames.Rate, null) };
        var result = Evaluator().Evaluate(Request(unknown), Loaded(rule));

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.ReadFieldConfidenceUnknown);
    }

    [Fact]
    public async Task ARealRequiredDataFinding_ForAFieldTheExtractionNeverFound_DoesNotReject_NFR06()
    {
        // 071: "an absent value is not reliably absent". The period start has no value and no confidence, so a Reject rule only Flags.
        var rule = Version("BILL-REQ", RuleType.RequiredData, """{"type":"RequiredData","fields":["billingPeriodStart"]}""") with { Consequence = Reject };
        var request = Request(Fee(0)) with
        {
            BillingPeriodStart = null,
            HeaderConfidence = Confidence().With(BillingFieldNames.BillingPeriodStart, null),
        };
        var result = Evaluator().Evaluate(request, Loaded(rule));
        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.RequiredFieldMissing);

        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);

        ShouldHold(decision, SubmissionHoldReasons.ReadFieldConfidenceUnknown);
    }

    [Fact]
    public async Task ARealRequiredDataFinding_ForAReliablyReadBlankField_Rejects_ButAForeignCurrency_OnlyFlags()
    {
        // ontology-r1 072 review F1: required_data.currency_not_usd is flag-only by reason code, whatever the version's consequence.
        // 073 review F-ALLOW also leaves it off the allowlist (AForeignCurrencyFinding_IsFlagOnly_ItsReasonIsNotListed_D115); the
        // flag-only check runs first, so that is the hold reason.
        var rule = Version("BILL-REQ", RuleType.RequiredData, """{"type":"RequiredData","fields":["billingPeriodStart"]}""") with { Consequence = Reject };
        var blank = Request(Fee(0)) with { BillingPeriodStart = null }; // the header confidence map still carries 0.95 for the field
        var foreign = Request(Fee(0)) with { Currency = "EUR" };

        var blankDecision = await Sut(new FakeSettings(Settings())).DecideAsync(Evaluator().Evaluate(blank, Loaded(rule)), CancellationToken.None);
        var foreignDecision = await Sut(new FakeSettings(Settings())).DecideAsync(Evaluator().Evaluate(foreign, Loaded(rule)), CancellationToken.None);

        blankDecision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        blankDecision.DecidingFindings.Single().Finding.ReasonCode.Should().Be(BillingReasonCodes.RequiredFieldMissing);
        foreignDecision.Outcome.Should().Be(SubmissionOutcome.Accepted, "a foreign currency read reliably is a Flag, never a rejection");
        var held = foreignDecision.Findings.Should().ContainSingle().Subject;
        held.Finding.ReasonCode.Should().Be(BillingReasonCodes.CurrencyNotUsd);
        held.HeldBecause.Should().Be(SubmissionHoldReasons.ReasonFlagOnly);
    }

    // ── The evaluation-time gate: a stored version whose consequence its type may not carry ──────────────────────

    [Theory]
    [InlineData(RuleType.AiObservation)]
    [InlineData(RuleType.TimeIncrement)]
    [InlineData(RuleType.DailyHoursCap)]
    [InlineData(RuleType.NonBillableActivity)]
    [InlineData(RuleType.ApprovedTimekeeper)]
    public async Task AStoredVersion_WhoseConsequenceIsReject_IsEvaluatedAsFlag_TheCheckSurvives_K2(RuleType type)
    {
        // 072 part W (073 review K2): the stored row is evaluated, not dropped; its consequence is read as Flag, so it cannot reject.
        var (request, loaded) = EverythingWrong(20);
        var stored = Version("BILL-" + type, type) with { Consequence = Reject };

        var result = Evaluator().Evaluate(request, loaded with { Rules = [stored] });

        result.Findings.Should().NotBeEmpty().And.OnlyContain(f => f.RuleType == type, "the check survives");
        result.Refusals.Should().BeEmpty("a downgrade is not a refusal");
        result.RulesByVersion[stored.PolicyVersionId].Consequence.Should().Be((int)BillingConsequence.Flag);
        var decision = await Sut(new FakeSettings(Settings())).DecideAsync(result, CancellationToken.None);
        decision.Outcome.Should().NotBe(SubmissionOutcome.Rejected);
        decision.Findings.Should().OnlyContain(f => f.HeldBecause == SubmissionHoldReasons.ConsequenceNotReject);
    }

    [Theory]
    [MemberData(nameof(Rejecting))]
    public void TheBillingGate_PassesAStoredReject_ForAnEligibleType(RuleType type)
    {
        var (request, loaded) = EverythingWrong(20);
        var stored = Version("BILL-" + type, type) with { Consequence = Reject };

        var result = Evaluator().Evaluate(request, loaded with { Rules = [stored] });

        result.Refusals.Should().BeEmpty();
        result.Findings.Should().Contain(f => f.RuleType == type);
        result.RulesByVersion[stored.PolicyVersionId].Consequence.Should().Be(Reject);
    }

    [Fact]
    public void TheBillingGate_DoesNotRefuseAVersionThatStatesNoConsequence()
    {
        // 072's fixtures carry no consequence. The decider treats a missing one as a Flag (fail closed), so the gate leaves it.
        var (request, loaded) = EverythingWrong(20);
        var result = Evaluator().Evaluate(request, loaded);

        result.Refusals.Should().BeEmpty();
        result.Findings.Select(f => f.RuleType).Distinct().Should().HaveCount(13);
        SubmissionRejectionRule.Decide(result.Findings, v => result.RulesByVersion[v].Consequence, Settings())
            .Outcome.Should().Be(SubmissionOutcome.Accepted, "no version states a consequence, so nothing is a Reject");
    }

    // ── The catalogue's eligibility (FR-26) ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void FR26_RejectEligibility_IsCataloguePositions_1_2_6_7_8_9_10()
    {
        var eligible = BillingRuleCatalogue.Types.ToArray();
        eligible.Should().HaveCount(13, "the eleven of FR-24 plus AddedFee (12) and Discount (13), task 115; the FR-26 list is unchanged");

        foreach (var type in eligible)
        {
            var position = BillingRuleCatalogue.CataloguePosition(type); // ApprovedTimekeeper = 1 ... AiObservation = 11, AddedFee 12, Discount 13
            BillingRuleCatalogue.IsRejectEligible(type).Should().Be(
                position is 1 or 2 or 6 or 7 or 8 or 9 or 10, $"{type} is catalogue position {position}");
        }
    }

    [Fact]
    public void Eligibility_IsNotDefinedForAnOntologyType()
    {
        foreach (var type in new[] { RuleType.Threshold, RuleType.Switch, RuleType.Existence })
        {
            ((Action)(() => BillingRuleCatalogue.IsRejectEligible(type))).Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public void RejectIsAllowed_ExactlyWhenTheTypeIsEligible_AndNotHoldOnly(RuleType type)
    {
        var expected = BillingRuleCatalogue.IsRejectEligible(type) && type != RuleType.ApprovedTimekeeper;

        BillingRuleCatalogue.IsConsequenceAllowed(type, Reject).Should().Be(expected);
        BillingRuleCatalogue.IsHoldOnly(type).Should().Be(type == RuleType.ApprovedTimekeeper);
    }

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public void FlagIsAlwaysAllowed_AndSuggestAdjustmentOnlyWhereAHandlerSuggestsOne(RuleType type)
    {
        BillingRuleCatalogue.IsConsequenceAllowed(type, Flag).Should().BeTrue();
        BillingRuleCatalogue.IsConsequenceAllowed(type, Suggest).Should().Be(type is RuleType.ApprovedRate or RuleType.NonBillableActivity);
    }

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public void AnUndefinedConsequence_IsNeverAllowed(RuleType type)
    {
        foreach (var value in new[] { -1, 0, 3, 99, 100000003, int.MaxValue })
        {
            BillingRuleCatalogue.IsConsequenceAllowed(type, value).Should().BeFalse($"{value} is not a consequence");
        }
    }

    [Fact]
    public void EveryTypeThatMayReject_IsEligible_SoNothingCanBeStoredAsRejectThatTheRuleWouldIgnore()
    {
        foreach (var type in BillingRuleCatalogue.Types)
        {
            if (BillingRuleCatalogue.IsConsequenceAllowed(type, Reject))
            {
                BillingRuleCatalogue.IsRejectEligible(type).Should().BeTrue(type.ToString());
                BillingRuleCatalogue.IsHoldOnly(type).Should().BeFalse(type.ToString());
            }
        }
    }

    [Theory]
    [InlineData(RuleType.AiObservation, "I-5")]
    [InlineData(RuleType.ApprovedTimekeeper, "D-89")]
    [InlineData(RuleType.TimeIncrement, "FR-26")]
    public void TheRefusalSentence_NamesTheRule_AndWhatTheTypeMayUse(RuleType type, string citation)
    {
        var sentence = BillingRuleCatalogue.ConsequenceRefusal(type, Reject);

        sentence.Should().Contain(citation).And.Contain("Flag").And.Contain(type.ToString());
    }

    // ── NFR-06 property: no rejection from a value that could have been misread ──────────────────────────────────

    [Fact]
    public void NFR06_NoCombinationOfAnUnreliableField_EverRejects_ForAnyTypeSubjectThresholdOrPosition()
    {
        var thresholds = new[] { 0.50m, 0.80m, 0.90m, 0.99m, 1.00m };
        var cases = 0;
        var controlsThatReject = 0;

        foreach (var type in BillingRuleCatalogue.Types)
        {
            foreach (var subject in new[] { BillingSubject.ForInvoice(), BillingSubject.ForLine(0) })
            {
                foreach (var threshold in thresholds)
                {
                    var settings = Settings(threshold, threshold / 2m);
                    var badValues = new decimal?[] { null, 0m, threshold - 0.0000001m, threshold / 2m, -0.1m, 1.5m };

                    for (var fields = 1; fields <= 4; fields++)
                    {
                        for (var badAt = 0; badAt < fields; badAt++)
                        {
                            foreach (var bad in badValues)
                            {
                                foreach (var others in new[] { threshold, 1m })
                                {
                                    var confidences = Enumerable.Range(0, fields).Select(i => i == badAt ? bad : others).ToArray();
                                    var finding = Finding(type, Reads(subject, confidences), subject: subject);

                                    var decision = Decide(finding, Reject, settings);

                                    cases++;
                                    decision.Outcome.Should().Be(
                                        SubmissionOutcome.Accepted,
                                        $"{type} at {subject.Kind}, threshold {threshold}, {fields} field(s), bad at {badAt} = {bad?.ToString() ?? "null"}");
                                    if (CanRejectWhenReliable(type, subject))
                                    {
                                        // Held on its read fields (not earlier, on its type), so the reviewer is told which field to correct.
                                        Only(decision).UnreliableFields.Should().Contain(Ref(subject, badAt));
                                    }
                                }
                            }

                            // The control: the same shape with every field exactly at the threshold can reject, for the types that may.
                            var good = Enumerable.Range(0, fields).Select(_ => (decimal?)threshold).ToArray();
                            var control = Decide(Finding(type, Reads(subject, good), subject: subject), Reject, settings);
                            controlsThatReject += control.Outcome == SubmissionOutcome.Rejected ? 1 : 0;
                            control.Outcome.Should().Be(
                                CanRejectWhenReliable(type, subject) ? SubmissionOutcome.Rejected : SubmissionOutcome.Accepted,
                                $"control: {type} at {subject.Kind}, threshold {threshold}, {fields} field(s)");
                        }
                    }
                }
            }
        }

        cases.Should().BeGreaterThan(10_000, "the grid must really run");
        controlsThatReject.Should().BeGreaterThan(0, "the grid is not vacuous: reliable inputs do reject where FR-26 allows it");
    }

    private static bool CanRejectWhenReliable(RuleType type, BillingSubject subject) =>
        RejectingTypes.Contains(type) && !(type == RuleType.Duplicate && subject.Kind != BillingSubjectKind.Invoice);
}
