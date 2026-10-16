using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Finance.Coding;

/// <summary>
/// Pure domain-logic tests (ADR-038 §2 path #6 - no mocks, no DI, no I/O) for <see cref="LineCodingGuard"/>, the server-side
/// control of FR-22 / D-46 (ebilling-r1 task 047): what the model returned for a line, checked against the line's resolved code
/// set, becomes what may be stored. The guard is the only thing standing between a model that invents or copies a code and a
/// coded invoice line, so each rule has a case that fails when the rule is removed.
/// </summary>
public class LineCodingGuardTests
{
    private static readonly Guid SetId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ResolvedCodeSet Set(CodeSetSource source = CodeSetSource.MatterType) => new(
        SetId,
        "UTBMS Litigation",
        source,
        taskCodes: [new("L110", "Fact Investigation", null), new("L120", "Analysis/Strategy", null)],
        activityCodes: [new("A101", "Plan and prepare for", null), new("A103", "Draft/revise", null)],
        expenseCodes: [new("E101", "Copying", null)]);

    private static InvoiceLineToCode Fee(string? narrative = "Reviewed and summarised the lease", string? task = null, string? activity = null) => new()
    {
        LineKey = "F1",
        LineType = InvoiceLineType.Fee,
        Narrative = narrative,
        BilledTaskCode = task,
        BilledActivityCode = activity,
    };

    private static InvoiceLineToCode Expense(string? expenseCode = null) => new()
    {
        LineKey = "X1",
        LineType = InvoiceLineType.Expense,
        Narrative = "Photocopying",
        BilledExpenseCode = expenseCode,
    };

    private static InvoiceLineCoding Returned(
        string key = "F1",
        string? task = null,
        string? activity = null,
        string? expense = null,
        decimal confidence = 0.8m,
        IReadOnlyList<InvoiceLineObservation>? observations = null) => new()
    {
        LineKey = key,
        TaskCode = task,
        ActivityCode = activity,
        ExpenseCode = expense,
        Confidence = confidence,
        Observations = observations ?? [],
    };

    private static InvoiceLineObservation Observation(InvoiceLineObservationType type, string quote, decimal confidence = 0.7m) =>
        new() { Type = type, Confidence = confidence, QuotedText = quote };

    // ── The closed-set rule (FR-22) ─────────────────────────────────────────────

    [Fact]
    public void CodesInTheSet_AreKept_InTheSetsOwnSpelling_WithTheModelsConfidence()
    {
        var result = LineCodingGuard.Apply(Fee(), Returned(task: " l110 ", activity: "A103", confidence: 0.83m), Set());

        result.Task.Code.Should().Be("L110", "the stored code is the set's spelling, not the model's");
        result.Activity.Code.Should().Be("A103");
        result.Expense.Code.Should().BeNull();
        result.RefusedCodes.Should().BeEmpty();
        result.Confidence.Should().Be(0.83m);
        result.Status.Should().Be(LineCodingStatus.Coded);
    }

    [Fact]
    public void ATaskCodeOutsideTheSet_IsRefused_NotStored_AndNeverCorrectedToANearCode()
    {
        // L210 is a real UTBMS litigation code, but it is not in this line's resolved set.
        var result = LineCodingGuard.Apply(Fee(), Returned(task: "L210", activity: "A101"), Set());

        result.Task.Code.Should().BeNull();
        result.Activity.Code.Should().Be("A101", "the valid activity code of the same line is unaffected");
        result.RefusedCodes.Should().ContainSingle().Which.Should().Be(
            new RefusedCode(BillingCodeKind.Task, "L210", CodeRefusalReason.OutsideCodeSet));
    }

    [Fact]
    public void AnActivityCodeOutsideTheSet_IsRefused()
    {
        var result = LineCodingGuard.Apply(Fee(), Returned(task: "L110", activity: "A999"), Set());

        result.Activity.Code.Should().BeNull();
        result.Task.Code.Should().Be("L110");
        result.RefusedCodes.Should().ContainSingle().Which.Should().Be(
            new RefusedCode(BillingCodeKind.Activity, "A999", CodeRefusalReason.OutsideCodeSet));
    }

    [Fact]
    public void AnExpenseCodeOutsideTheSet_IsRefused()
    {
        var result = LineCodingGuard.Apply(Expense(), Returned("X1", expense: "E999"), Set());

        result.Expense.Code.Should().BeNull();
        result.RefusedCodes.Should().ContainSingle().Which.Should().Be(
            new RefusedCode(BillingCodeKind.Expense, "E999", CodeRefusalReason.OutsideCodeSet));
    }

    [Fact]
    public void ACodeFromTheWrongList_IsRefused_ACodeIsOnlyAllowedInItsOwnKind()
    {
        // A101 is allowed - as an ACTIVITY code. Returned as a task code it is outside the allowed task codes.
        var result = LineCodingGuard.Apply(Fee(), Returned(task: "A101", activity: "L110"), Set());

        result.Task.Code.Should().BeNull();
        result.Activity.Code.Should().BeNull();
        result.RefusedCodes.Select(r => (r.Kind, r.Code)).Should().BeEquivalentTo(
            [(BillingCodeKind.Task, "A101"), (BillingCodeKind.Activity, "L110")]);
    }

    [Fact]
    public void ARefusedCode_ZeroesTheConfidence_BecauseItNoLongerDescribesWhatIsStored()
    {
        var result = LineCodingGuard.Apply(Fee(), Returned(task: "L110", activity: "A999", confidence: 0.95m), Set());

        result.Confidence.Should().Be(0m);
        result.Task.Code.Should().Be("L110");
    }

    [Theory]
    [InlineData(InvoiceLineType.Expense, "L110", null, null, BillingCodeKind.Task)]
    [InlineData(InvoiceLineType.Expense, null, "A101", null, BillingCodeKind.Activity)]
    [InlineData(InvoiceLineType.Fee, null, null, "E101", BillingCodeKind.Expense)]
    public void ACodeKindTheLineTypeDoesNotTake_IsRefused_EvenWhenItIsInTheSet(
        InvoiceLineType lineType, string? task, string? activity, string? expense, BillingCodeKind refusedKind)
    {
        var line = new InvoiceLineToCode { LineKey = "K", LineType = lineType, Narrative = "Something" };

        var result = LineCodingGuard.Apply(line, Returned("K", task, activity, expense), Set());

        result.Task.Code.Should().BeNull();
        result.Activity.Code.Should().BeNull();
        result.Expense.Code.Should().BeNull();
        result.RefusedCodes.Should().ContainSingle().Which.Kind.Should().Be(refusedKind);
        result.RefusedCodes[0].Reason.Should().Be(CodeRefusalReason.NotApplicableToLineType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANullEmptyOrBlankCode_IsNoCode_NotARefusal(string? blank)
    {
        var result = LineCodingGuard.Apply(Fee(), Returned(task: blank, activity: blank), Set());

        result.Task.Code.Should().BeNull();
        result.Activity.Code.Should().BeNull();
        result.RefusedCodes.Should().BeEmpty();
        result.Confidence.Should().Be(0.8m);
    }

    // ── The code-set source is recorded per line (D-05, FR-22) ──────────────────

    [Theory]
    [InlineData(CodeSetSource.FirmCodes, false)]
    [InlineData(CodeSetSource.MatterType, false)]
    [InlineData(CodeSetSource.PracticeArea, false)]
    [InlineData(CodeSetSource.AiSuggestion, true)]
    [InlineData(CodeSetSource.SettingsDefault, false)]
    public void TheResolvedSetAndItsSource_AreRecordedOnTheLine_AndOnlyAnAiSuggestionNeedsReview(CodeSetSource source, bool needsReview)
    {
        var result = LineCodingGuard.Apply(Fee(), Returned(task: "L110"), Set(source));

        result.CodeSetId.Should().Be(SetId);
        result.CodeSetName.Should().Be("UTBMS Litigation");
        result.CodeSetSource.Should().Be(source);
        result.CodeSetNeedsReview.Should().Be(needsReview);
    }

    // ── Firm codes are retained, a mismatch is flagged (D-05) ───────────────────

    [Fact]
    public void AFirmCodeThatDiffersFromTheAiCode_IsRetained_AndFlaggedAsAMismatch()
    {
        var result = LineCodingGuard.Apply(Fee(task: "L120", activity: "A101"), Returned(task: "L110", activity: "A101"), Set());

        result.Task.Should().Be(new LineCodeValue("L110", "L120", Mismatch: true));
        result.Activity.Should().Be(new LineCodeValue("A101", "A101", Mismatch: false));
        result.HasFirmCodeMismatch.Should().BeTrue();
    }

    [Fact]
    public void AFirmCodeThatMatchesTheAiCode_ApartFromCaseAndPadding_IsNotAMismatch()
    {
        var result = LineCodingGuard.Apply(Fee(task: " l110"), Returned(task: "L110"), Set());

        result.Task.Mismatch.Should().BeFalse();
        result.Task.FirmCode.Should().Be("l110", "the firm's code is retained as billed (trimmed)");
        result.HasFirmCodeMismatch.Should().BeFalse();
    }

    [Fact]
    public void AFirmCode_WithNoAiCode_IsRetained_WithoutAMismatch()
    {
        var result = LineCodingGuard.Apply(Fee(task: "L120"), Returned(task: null), Set());

        result.Task.Should().Be(new LineCodeValue(null, "L120", Mismatch: false));
    }

    [Fact]
    public void AFirmCode_WhoseAiCodeWasRefused_IsRetained_WithoutAMismatch()
    {
        var result = LineCodingGuard.Apply(Fee(task: "L120"), Returned(task: "L999"), Set());

        result.Task.Should().Be(new LineCodeValue(null, "L120", Mismatch: false));
        result.RefusedCodes.Should().ContainSingle();
    }

    [Fact]
    public void AnAiCode_WithNoFirmCode_HasNoMismatch()
    {
        var result = LineCodingGuard.Apply(Fee(), Returned(task: "L110"), Set());

        result.Task.Should().Be(new LineCodeValue("L110", null, Mismatch: false));
    }

    [Fact]
    public void AFirmExpenseCode_IsComparedWithTheAiExpenseCode()
    {
        var result = LineCodingGuard.Apply(Expense("E112"), Returned("X1", expense: "E101"), Set());

        result.Expense.Should().Be(new LineCodeValue("E101", "E112", Mismatch: true));
    }

    // ── Observations carry evidence the reviewer can see ────────────────────────

    [Fact]
    public void AnObservation_WhoseQuoteIsInTheNarrative_IsKept()
    {
        var narrative = "Draft motion; call client; review exhibits";
        var returned = Returned(
            task: "L110",
            observations: [Observation(InvoiceLineObservationType.BlockBilling, "Draft motion; call client; review exhibits", 0.9m)]);

        var result = LineCodingGuard.Apply(Fee(narrative), returned, Set());

        result.Observations.Should().ContainSingle().Which.QuotedText.Should().Be(narrative);
        result.DroppedObservations.Should().Be(0);
    }

    [Fact]
    public void AnObservation_WhoseQuoteIsNotInTheNarrative_IsDropped_AndCounted()
    {
        var returned = Returned(
            task: "L110",
            observations: [Observation(InvoiceLineObservationType.VagueDescription, "attention to file")]);

        var result = LineCodingGuard.Apply(Fee("Drafted the complaint"), returned, Set());

        result.Observations.Should().BeEmpty("an observation without evidence in the narrative is not a finding input");
        result.DroppedObservations.Should().Be(1);
    }

    [Fact]
    public void TheQuoteIsMatchedIgnoringCaseAndWhitespaceRuns()
    {
        var returned = Returned(
            observations: [Observation(InvoiceLineObservationType.VagueDescription, "attention  to FILE")]);

        var result = LineCodingGuard.Apply(Fee("Attention\r\nto file"), returned, Set());

        result.Observations.Should().ContainSingle();
    }

    // InvoiceAi.ParseCodeLines passes a blank quotedText through as "" (one placeholder observation must not fail the whole chunk),
    // so this guard is where an evidence-free observation is dropped and counted.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnObservationWithAnEmptyQuote_IsDropped(string quote)
    {
        var returned = Returned(observations: [Observation(InvoiceLineObservationType.VagueDescription, quote)]);

        var result = LineCodingGuard.Apply(Fee("Attention to file"), returned, Set());

        result.Observations.Should().BeEmpty();
        result.DroppedObservations.Should().Be(1);
    }

    [Fact]
    public void TwoObservationsOfOneType_KeepTheMoreConfidentOne()
    {
        var returned = Returned(
            observations:
            [
                Observation(InvoiceLineObservationType.VagueDescription, "Attention", 0.6m),
                Observation(InvoiceLineObservationType.VagueDescription, "to file", 0.9m),
                Observation(InvoiceLineObservationType.BlockBilling, "Attention to file", 0.5m),
            ]);

        var result = LineCodingGuard.Apply(Fee("Attention to file"), returned, Set());

        result.Observations.Select(o => (o.Type, o.QuotedText)).Should().Equal(
            (InvoiceLineObservationType.BlockBilling, "Attention to file"),
            (InvoiceLineObservationType.VagueDescription, "to file"));
        result.DroppedObservations.Should().Be(1);
    }

    // ── A line that was not coded ───────────────────────────────────────────────

    [Theory]
    [InlineData(LineCodingStatus.NoNarrative)]
    [InlineData(LineCodingStatus.NotReturned)]
    public void AnUncodedLine_HasNoAiCodes_KeepsTheFirmCodes_AndHasNoMismatchOrConfidence(LineCodingStatus status)
    {
        var result = LineCodingGuard.Uncoded(Fee(narrative: null, task: "L120", activity: "A101"), status, Set());

        result.Status.Should().Be(status);
        result.Task.Should().Be(new LineCodeValue(null, "L120", false));
        result.Activity.Should().Be(new LineCodeValue(null, "A101", false));
        result.Expense.Should().Be(new LineCodeValue(null, null, false));
        result.Confidence.Should().Be(0m);
        result.Observations.Should().BeEmpty();
        result.RefusedCodes.Should().BeEmpty();
        result.CodeSetId.Should().Be(SetId);
        result.HasFirmCodeMismatch.Should().BeFalse();
    }

    // ── The resolved set's lookup ───────────────────────────────────────────────

    [Theory]
    [InlineData("L110", true, "L110")]
    [InlineData("l110", true, "L110")]
    [InlineData("  L110  ", true, "L110")]
    [InlineData("L11", false, "")]
    [InlineData("L1100", false, "")]
    [InlineData("", false, "")]
    [InlineData(null, false, "")]
    public void TryGetCode_MatchesWholeCodesOnly_IgnoringCaseAndPadding(string? code, bool expected, string canonical)
    {
        var found = Set().TryGetCode(BillingCodeKind.Task, code, out var actual);

        found.Should().Be(expected);
        actual.Should().Be(canonical);
    }

    [Theory]
    [InlineData("sprk_matter", true)]
    [InlineData("sprk_project", true)]
    [InlineData("sprk_workassignment", true)]
    [InlineData("sprk_invoice", false)]
    [InlineData("SPRK_MATTER", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CoreRecords_AreTheThreeCoreTables(string? entity, bool supported)
    {
        CodingCoreRecord.IsSupported(entity).Should().Be(supported);
    }
}
