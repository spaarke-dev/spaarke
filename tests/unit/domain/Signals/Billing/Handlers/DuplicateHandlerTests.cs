using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 9 Duplicate (design.md 6.1; spec FR-36; D-88). Maintain-class (ADR-038): pure logic, no mocks.
/// The fact set's invoice is "INV-100", this submission is INV-100.2 (submission id 07100000-...-b1).
/// </summary>
public class DuplicateHandlerTests
{
    private static readonly BillingRuleDefinition InvoiceNumberOnly = Rule("""{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""");
    private static readonly BillingRuleDefinition LinesOnly = Rule("""{"type":"Duplicate","matchFields":["timekeeper","lineDate","hours","narrative"]}""");
    private static readonly BillingRuleDefinition Both = Rule("""{"type":"Duplicate","matchFields":["firmInvoiceNumber","timekeeper","lineDate","hours"]}""");

    private static BillingFactSet WithPriors(BillingFactSet facts, params BillingPriorSubmission[] priors) => facts with { PriorSubmissions = priors };

    private static BillingFactSet Header(params BillingPriorSubmission[] priors) => WithPriors(Facts(Fee(0)), priors);

    // ── Invoice level ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MustFire_WhenTheFirmAlreadySubmittedThisInvoiceNumber_OnTheInvoice()
    {
        var finding = Run(InvoiceNumberOnly, Header(Prior("INV-100.1", "INV-100", 1))).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateInvoiceNumber);
        finding.Subject.Should().Be(BillingSubject.ForInvoice());
        finding.SuggestedAdjustment.Should().BeNull();
        finding.Value("duplicateOf.trackingNumber").Value.Should().Be("INV-100.1");
        finding.FactValues.Should().Contain("invoice.firmInvoiceNumber", "INV-100").And.Contain("duplicateOf.trackingNumber", "INV-100.1");
        finding.ReadFields.Should().Equal(new FieldRef(null, BillingFieldNames.FirmInvoiceNumber));
    }

    [Fact]
    public void MustNotFire_WhenThereIsNoEarlierSubmission_OrADifferentNumber()
    {
        Run(InvoiceNumberOnly, Header()).Should().BeEmpty();
        Run(InvoiceNumberOnly, Header(Prior("INV-101.1", "INV-101", 1))).Should().BeEmpty();
    }

    [Theory]
    [InlineData(BillingPriorStatus.Rejected)]
    [InlineData(BillingPriorStatus.ReturnedToFirm)]
    public void MustNotFire_ForARejectedOrReturnedEarlierAttempt_OfTheSameNumber_FR36(BillingPriorStatus status)
    {
        Run(InvoiceNumberOnly, Header(Prior("INV-100.1", "INV-100", 1, status))).Should().BeEmpty();
    }

    [Fact]
    public void AnAcceptedAttempt_IsStillADuplicate_AndTheRejectedOneBeforeItIsNotCited()
    {
        var priors = new[]
        {
            Prior("INV-100.1", "INV-100", 1, BillingPriorStatus.Rejected),
            Prior("INV-100.3", "INV-100", 3, BillingPriorStatus.Active),
        };

        Run(InvoiceNumberOnly, Header(priors)).Single().Value("duplicateOf.trackingNumber").Value.Should().Be("INV-100.3");
    }

    [Fact]
    public void WhenSeveralActiveAttemptsExist_TheLowestNumberedIsTheOriginal_WhateverTheOrderGiven()
    {
        var priors = new[] { Prior("INV-100.5", "INV-100", 5), Prior("INV-100.3", "INV-100", 3), Prior("INV-100.4", "INV-100", 4) };

        Run(InvoiceNumberOnly, Header(priors)).Single().Value("duplicateOf.trackingNumber").Value.Should().Be("INV-100.3");
    }

    [Fact]
    public void TheNumberIsComparedWithoutRegardToCaseOrWhitespace()
    {
        Run(InvoiceNumberOnly, Header(Prior("inv-100.1", "  inv-100 ", 1))).Should().ContainSingle();
        Run(InvoiceNumberOnly, Header(Prior("INV-100.1", "INV  -100", 1))).Should().BeEmpty("an inner difference is a different number");
    }

    [Fact]
    public void TheSubmissionItself_IsNeverItsOwnDuplicate()
    {
        var self = Invoice().SubmissionId;
        var prior = Prior("INV-100.2", "INV-100", 2) with { SubmissionId = self };

        Run(InvoiceNumberOnly, Header(prior)).Should().BeEmpty();
    }

    [Fact]
    public void TheInvoiceFactsSubmissionId_IsARequiredNonNullableGuid_K1()
    {
        // Without it Duplicate cannot exclude the submission's own earlier attempt row. A nullable, optional id let a fact set skip
        // that exclusion silently; 072 always has the id, so the type now demands it.
        var property = typeof(BillingInvoiceFacts).GetProperty(nameof(BillingInvoiceFacts.SubmissionId))!;

        property.PropertyType.Should().Be(typeof(Guid));
        property.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false).Should().BeTrue();
    }

    [Fact]
    public void AnInvoiceWithNoNumber_IsNotCompared()
    {
        var facts = Header(Prior("INV-100.1", null, 1), Prior("INV-100.2", "", 2)) with { Invoice = Invoice() with { FirmInvoiceNumber = "  " } };

        Run(InvoiceNumberOnly, facts).Should().BeEmpty();
    }

    [Fact]
    public void TheInvoiceNumberCheck_RunsOnlyWhenTheRuleListsIt()
    {
        Run(LinesOnly, Header(Prior("INV-100.1", "INV-100", 1))).Should().BeEmpty();
    }

    [Fact]
    public void ALowConfidenceInvoiceNumber_IsFlagged()
    {
        var facts = Header(Prior("INV-100.1", "INV-100", 1)) with
        {
            Invoice = Invoice() with { FieldConfidence = Confidence().With(BillingFieldNames.FirmInvoiceNumber, 0.6m) },
        };

        Run(InvoiceNumberOnly, facts).Single().ReadsLowConfidenceValue.Should().BeTrue();
    }

    // ── Line level ───────────────────────────────────────────────────────────────────────────────────────────────

    private static BillingLineFacts Entry(int index, decimal hours = 1.5m, string narrative = "Draft motion", Guid? who = null) =>
        Fee(index) with { Hours = hours, Narrative = narrative, Timekeeper = Matched(who) };

    [Fact]
    public void MustFire_OnTheRepeatedLine_NotOnTheFirstOne_WithinTheInvoice()
    {
        var facts = Facts(Entry(0), Entry(1, hours: 2m), Entry(2));

        var finding = Run(LinesOnly, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateOfEarlierLine);
        finding.Subject.Should().Be(BillingSubject.ForLine(2));
        finding.Value("duplicateOf.lineIndex").Value.Should().Be("0");
        finding.FactValues.Should().Contain("duplicateOf.trackingNumber", "INV-100.2", "the line repeats a line of this very submission");
    }

    [Fact]
    public void MustNotFire_WhenAnyConfiguredFieldDiffers()
    {
        var facts = Facts(
            Entry(0),
            Entry(1, hours: 1.6m),
            Entry(2, narrative: "Draft a different motion"),
            Entry(3) with { Date = LineDate.AddDays(1) },
            Entry(4, who: Guid.Parse("07100000-0000-4000-8000-0000000000e9")));

        Run(LinesOnly, facts).Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheConfiguredFieldsAreCompared()
    {
        var hoursOnly = Rule("""{"type":"Duplicate","matchFields":["hours"]}""");
        var facts = Facts(Entry(0), Entry(1, narrative: "Something else") with { TimekeeperName = "Omar Haddad" });

        Run(hoursOnly, facts).Should().ContainSingle().Which.Subject.Should().Be(BillingSubject.ForLine(1));
    }

    [Fact]
    public void ALineMissingAConfiguredField_IsNotCompared()
    {
        var facts = Facts(Entry(0) with { Narrative = null }, Entry(1) with { Narrative = null }, Entry(2) with { Date = null }, Entry(3) with { Date = null });

        Run(LinesOnly, facts).Should().BeEmpty("two missing narratives are not evidence that the lines match");
    }

    [Fact]
    public void NarrativesCompareWithoutRegardToCaseOrWhitespace_AndHoursWithoutTrailingZeros()
    {
        var facts = Facts(Entry(0, hours: 1.50m, narrative: "Draft  motion"), Entry(1, hours: 1.5m, narrative: " DRAFT MOTION "));

        Run(LinesOnly, facts).Should().ContainSingle();
    }

    [Fact]
    public void TheTimekeeperComparesByRosterMatch_WhenBothLinesHaveOne_SoTwoSpellingsAreOnePerson()
    {
        var jane = Guid.Parse("07100000-0000-4000-8000-0000000000e1");
        var facts = Facts(
            Entry(0, who: jane) with { TimekeeperName = "Jane Doe" },
            Entry(1, who: jane) with { TimekeeperName = "J. Doe" });

        Run(LinesOnly, facts).Should().ContainSingle();
    }

    [Fact]
    public void UnmatchedTimekeepers_CompareByBilledName()
    {
        var unmatched = Unmatched(TimekeeperUnmatchedReason.NotOnRoster);
        var same = Facts(Entry(0) with { Timekeeper = unmatched, TimekeeperName = "Pat Kim" }, Entry(1) with { Timekeeper = unmatched, TimekeeperName = "PAT KIM" });
        var different = Facts(Entry(0) with { Timekeeper = unmatched, TimekeeperName = "Pat Kim" }, Entry(1) with { Timekeeper = unmatched, TimekeeperName = "Pam Kim" });

        Run(LinesOnly, same).Should().ContainSingle();
        Run(LinesOnly, different).Should().BeEmpty();
    }

    [Fact]
    public void AnAmountMatchField_ComparesTheLineAmount()
    {
        var amountRule = Rule("""{"type":"Duplicate","matchFields":["lineDate","amount"]}""");
        var facts = Facts(Fee(0) with { Amount = 100.0m }, Fee(1) with { Amount = 100.00m }, Fee(2) with { Amount = 100.01m });

        Run(amountRule, facts).Should().ContainSingle().Which.Subject.Should().Be(BillingSubject.ForLine(1));
    }

    [Fact]
    public void MustFire_ForALineThatRepeatsALineOfAnEarlierSubmission_AndCitesIt()
    {
        var earlier = Prior("INV-099.1", "INV-099", 1, BillingPriorStatus.Active, new BillingPriorLine
        {
            Date = LineDate, TimekeeperName = "Jane Doe", TimekeeperId = Matched().TimekeeperId, Hours = 1.5m, Narrative = "draft motion",
        });
        var facts = WithPriors(Facts(Entry(0)), earlier);

        var finding = Run(LinesOnly, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateOfPriorSubmissionLine);
        finding.FactValues.Should().Contain("duplicateOf.trackingNumber", "INV-099.1");
    }

    [Theory]
    [InlineData(BillingPriorStatus.Rejected)]
    [InlineData(BillingPriorStatus.ReturnedToFirm)]
    public void LinesOfARejectedOrReturnedEarlierAttempt_AreNotDuplicates_FR36(BillingPriorStatus status)
    {
        var earlier = Prior("INV-099.1", "INV-099", 1, status, new BillingPriorLine
        {
            Date = LineDate, TimekeeperId = Matched().TimekeeperId, TimekeeperName = "Jane Doe", Hours = 1.5m, Narrative = "Draft motion",
        });

        Run(LinesOnly, WithPriors(Facts(Entry(0)), earlier)).Should().BeEmpty();
    }

    [Fact]
    public void ARepeatOfAPriorLine_IsReportedAgainstThePriorAttempt_AheadOfAnEarlierLineOfTheSameInvoice()
    {
        var earlier = Prior("INV-099.1", "INV-099", 1, BillingPriorStatus.Active, new BillingPriorLine
        {
            Date = LineDate, TimekeeperId = Matched().TimekeeperId, TimekeeperName = "Jane Doe", Hours = 1.5m, Narrative = "Draft motion",
        });
        var facts = WithPriors(Facts(Entry(0), Entry(1)), earlier);

        Run(LinesOnly, facts).Select(f => f.ReasonCode).Should().Equal(
            BillingReasonCodes.DuplicateOfPriorSubmissionLine, BillingReasonCodes.DuplicateOfPriorSubmissionLine);
    }

    [Fact]
    public void TheLineLevelFinding_ReadsTheConfiguredFieldsAndTheTemplateFields()
    {
        var facts = Facts(Entry(0), Entry(4));

        Run(LinesOnly, facts).Single().ReadFields.Should().Equal(
            new FieldRef(4, BillingFieldNames.TimekeeperAsBilled), new FieldRef(4, BillingFieldNames.Date),
            new FieldRef(4, BillingFieldNames.Hours), new FieldRef(4, BillingFieldNames.Narrative),
            new FieldRef(null, BillingFieldNames.FirmInvoiceNumber));
    }

    // ── Both levels ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARuleWithBothLevels_ReportsTheInvoiceFirst_ThenTheLines_AndTheBodyOrderDoesNotMatter()
    {
        var reordered = Rule("""{"type":"Duplicate","matchFields":["hours","lineDate","timekeeper","firmInvoiceNumber"]}""");
        var facts = WithPriors(Facts(Entry(0), Entry(1)), Prior("INV-100.1", "INV-100", 1));

        var findings = Run(Both, facts);

        findings.Select(f => f.ReasonCode).Should().Equal(BillingReasonCodes.DuplicateInvoiceNumber, BillingReasonCodes.DuplicateOfEarlierLine);
        Run(reordered, facts).Select(f => f.ReasonCode).Should().Equal(findings.Select(f => f.ReasonCode));
        findings[0].Subject.Kind.Should().Be(BillingSubjectKind.Invoice);
        findings[1].Subject.Kind.Should().Be(BillingSubjectKind.Line);
    }

    [Fact]
    public void FactValues_OfTheLineFinding_AreWithinTheTypesFieldSet()
    {
        var facts = Facts(Entry(0), Entry(1));

        Run(LinesOnly, facts).Single().FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.Duplicate));
    }

    // ── D-112: a prior across the secure wall is flagged, never described ────────────────────────────────────────

    private static readonly Guid RootA = Guid.Parse("07100000-0000-4000-8000-00000000a0a1");
    private static readonly Guid RootB = Guid.Parse("07100000-0000-4000-8000-00000000a0b2");
    private const string PriorTracking = "SECRET-777.1";
    private const string PriorNarrative = "Privileged strategy memo on the Orion acquisition";

    private static BillingPriorSubmission WalledInvoicePrior(IReadOnlySet<Guid>? roots) =>
        Prior(PriorTracking, "INV-100", 1) with { SecureRoots = roots };

    private static BillingPriorSubmission WalledLinePrior(IReadOnlySet<Guid>? roots) =>
        Prior(PriorTracking, "INV-099", 1, BillingPriorStatus.Active, new BillingPriorLine
        {
            Date = LineDate, TimekeeperId = Matched().TimekeeperId, TimekeeperName = "Jane Doe", Hours = 1.5m, Narrative = PriorNarrative,
        }) with { SecureRoots = roots };

    private static BillingLineFacts MatchingLine() => Entry(0, narrative: PriorNarrative);

    private static BillingFactSet InvoiceUnder(IReadOnlySet<Guid>? roots, BillingFactSet facts) =>
        facts with { Invoice = facts.Invoice with { SecureRoots = roots } };

    /// <summary>Substitutes the finding's token values into the message the composer would use for it.</summary>
    private static string Render(BillingFinding finding)
    {
        var text = finding.DetailWithheld
            ? BillingRuleCatalogue.WithheldDuplicateTemplate
            : "Invoice {{invoice.firmInvoiceNumber}}, the line dated {{line.date}} for {{line.timekeeperName}}, duplicates {{duplicateOf.trackingNumber}}.";
        return finding.FactValues.Aggregate(text, (t, kv) => t.Replace("{{" + kv.Key + "}}", kv.Value, StringComparison.Ordinal));
    }

    /// <summary>Everything a reader of the finding could see: every value, every token value, the rendered message.</summary>
    private static string Everything(BillingFinding finding) => string.Join(
        "|", finding.ValuesRead.Select(v => $"{v.Name}={v.Value}").Concat(finding.FactValues.Select(kv => $"{kv.Key}={kv.Value}")).Append(Render(finding)));

    private static void ShouldBeWithheld(BillingFinding finding)
    {
        finding.DetailWithheld.Should().BeTrue();
        finding.FactValues.Should().BeEmpty("no token: not the tracking number, not a line value");
        finding.ValuesRead.Select(v => v.Name).Should().NotContain("duplicateOf.trackingNumber").And.NotContain("duplicateOf.lineIndex");
        Render(finding).Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate).And.Contain("matches an earlier submission");
        var everything = Everything(finding);
        everything.Should().NotContain(PriorTracking).And.NotContain("SECRET").And.NotContain(PriorNarrative)
            .And.NotContain("Jane Doe").And.NotContain("2026-03-02").And.NotContain("1.5");
    }

    private static void ShouldShowDetail(BillingFinding finding, string tracking)
    {
        finding.DetailWithheld.Should().BeFalse();
        finding.FactValues.Should().Contain("duplicateOf.trackingNumber", tracking);
        Render(finding).Should().Contain(tracking);
    }

    public static TheoryData<string, bool> DetailShownCases => new()
    {
        // case, shown. Roots of the prior / roots of the invoice are named by the case; see ResolveRoots.
        { "ordinary prior, ordinary invoice", true },
        { "same secure root", true },
        { "ordinary prior on a secure invoice", true },
        { "secure prior on an ordinary invoice", false },
        { "two different secure roots", false },
        { "prior under {A,B}, invoice under {A}", false },
        { "prior under {A}, invoice under {A,B}", true },
        { "prior unresolved", false },
        { "invoice unresolved", false },
        { "both unresolved", false },
    };

    private static (IReadOnlySet<Guid>? Prior, IReadOnlySet<Guid>? Invoice) ResolveRoots(string name) => name switch
    {
        "ordinary prior, ordinary invoice" => (Ordinary, Ordinary),
        "same secure root" => (Roots(RootA), Roots(RootA)),
        "ordinary prior on a secure invoice" => (Ordinary, Roots(RootA)),
        "secure prior on an ordinary invoice" => (Roots(RootA), Ordinary),
        "two different secure roots" => (Roots(RootA), Roots(RootB)),
        "prior under {A,B}, invoice under {A}" => (Roots(RootA, RootB), Roots(RootA)),
        "prior under {A}, invoice under {A,B}" => (Roots(RootA), Roots(RootA, RootB)),
        "prior unresolved" => (null, Roots(RootA)),
        "invoice unresolved" => (Roots(RootA), null),
        "both unresolved" => (null, null),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Theory]
    [MemberData(nameof(DetailShownCases))]
    public void InvoiceLevel_TheFindingAlwaysFires_AndCitesThePriorOnlyWhenItsWallIsWithinTheInvoicesWall_D112(string name, bool shown)
    {
        var (priorRoots, invoiceRoots) = ResolveRoots(name);
        var facts = InvoiceUnder(invoiceRoots, Header(WalledInvoicePrior(priorRoots)));

        var finding = Run(InvoiceNumberOnly, facts).Should().ContainSingle(name).Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateInvoiceNumber);
        finding.Subject.Should().Be(BillingSubject.ForInvoice());
        if (shown) ShouldShowDetail(finding, PriorTracking); else ShouldBeWithheld(finding);
    }

    [Theory]
    [MemberData(nameof(DetailShownCases))]
    public void LineLevel_TheFindingAlwaysFires_AndCitesThePriorOnlyWhenItsWallIsWithinTheInvoicesWall_D112(string name, bool shown)
    {
        var (priorRoots, invoiceRoots) = ResolveRoots(name);
        var facts = InvoiceUnder(invoiceRoots, WithPriors(Facts(MatchingLine()), WalledLinePrior(priorRoots)));

        var finding = Run(LinesOnly, facts).Should().ContainSingle(name).Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateOfPriorSubmissionLine);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        if (shown) ShouldShowDetail(finding, PriorTracking); else ShouldBeWithheld(finding);
    }

    [Fact]
    public void AWithheldFinding_StillListsEveryFieldItRead_SoTask073SeesTheirConfidence_D112()
    {
        var facts = InvoiceUnder(Roots(RootB), WithPriors(Facts(MatchingLine()), WalledLinePrior(Roots(RootA))));
        var shown = InvoiceUnder(Roots(RootA), WithPriors(Facts(MatchingLine()), WalledLinePrior(Roots(RootA))));

        var withheld = Run(LinesOnly, facts).Single();
        var visible = Run(LinesOnly, shown).Single();

        withheld.ReadFields.Should().Equal(visible.ReadFields);
        withheld.ValuesRead.Where(v => v.IsExtracted).Select(v => v.Confidence).Should().OnlyContain(c => c == 0.95m);
        withheld.ValuesRead.Where(v => v.Name.StartsWith("line.", StringComparison.Ordinal)).Should().OnlyContain(v => v.Value == null);
    }

    [Fact]
    public void WhenTheMatchExistsInAPriorTheViewerMaySeeAndOneAcrossTheWall_TheVisibleOneIsCited_D112()
    {
        var hidden = Prior("HIDDEN-5.1", "INV-100", 1) with { SecureRoots = Roots(RootB) };
        var visible = Prior("INV-100.4", "INV-100", 4) with { SecureRoots = Ordinary };
        var facts = InvoiceUnder(Roots(RootA), Header(hidden, visible));

        ShouldShowDetail(Run(InvoiceNumberOnly, facts).Single(), "INV-100.4");

        var hiddenLine = WalledLinePrior(Roots(RootB)) with { TrackingNumber = "HIDDEN-5.1" };
        var visibleLine = WalledLinePrior(Ordinary) with { TrackingNumber = "INV-099.4", Sequence = 4 };
        var lineFacts = InvoiceUnder(Roots(RootA), WithPriors(Facts(MatchingLine()), hiddenLine, visibleLine));

        ShouldShowDetail(Run(LinesOnly, lineFacts).Single(), "INV-099.4");
    }

    [Fact]
    public void ALineThatRepeatsAnEarlierLineOfTheSameInvoice_IsUnchangedByTheWall_D112()
    {
        var facts = InvoiceUnder(null, Facts(Entry(0), Entry(1)));

        var finding = Run(LinesOnly, facts).Should().ContainSingle().Subject;

        ShouldShowDetail(finding, "INV-100.2");
        finding.Value("duplicateOf.lineIndex").Value.Should().Be("0");
    }

    [Fact]
    public void ThePriorRootsAndTheInvoiceRoots_AreRequiredMembers_SoACallerMustStateOrdinaryUnresolvedOrTheRoots_D112()
    {
        foreach (var type in new[] { typeof(BillingInvoiceFacts), typeof(BillingPriorSubmission) })
        {
            var property = type.GetProperty("SecureRoots")!;
            property.PropertyType.Should().Be(typeof(IReadOnlySet<Guid>));
            property.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false).Should().BeTrue();
        }
    }

    // ── D-176 / #2118: a finding that cites a prior submission carries that prior's invoice id, as evidence only ─────────

    private static readonly Guid EarlierInvoice = Guid.Parse("0ABCDEF0-1234-4567-89AB-CDEF01234567");
    private const string EarlierInvoiceText = "0abcdef0-1234-4567-89ab-cdef01234567";

    private static BillingPriorSubmission WithEarlierInvoice(BillingPriorSubmission prior) => prior with { InvoiceId = EarlierInvoice };

    private static void ShouldCiteTheEarlierInvoiceAsEvidenceOnly(BillingFinding finding)
    {
        finding.Value("duplicateOf.invoiceId").Value.Should().Be(EarlierInvoiceText, "a lowercase GUID in the D format");
        finding.ValuesRead.Single(v => v.Name == "duplicateOf.invoiceId").ExtractedField.Should().BeNull("a reference, like the tracking number");
        finding.FactValues.Keys.Should().NotContain("duplicateOf.invoiceId", "not a template fact: it is not in the fact snapshot");
        finding.FactValues.Values.Should().NotContain(v => v.Contains(EarlierInvoiceText, StringComparison.Ordinal));
        Render(finding).Should().NotContain(EarlierInvoiceText, "nor in the rendered sentence");
        BillingRuleCatalogue.TemplateFactFields(RuleType.Duplicate).Should().NotContain("duplicateOf.invoiceId");
    }

    [Fact]
    public void InvoiceLevel_AVisiblePrior_CarriesTheEarlierInvoiceId_AsAReference_D176()
    {
        var finding = Run(InvoiceNumberOnly, Header(WithEarlierInvoice(Prior("INV-100.1", "INV-100", 1)))).Should().ContainSingle().Subject;

        ShouldCiteTheEarlierInvoiceAsEvidenceOnly(finding);
        finding.Value("duplicateOf.trackingNumber").Value.Should().Be("INV-100.1");
    }

    [Fact]
    public void LineLevel_DuplicateOfPriorSubmissionLine_CarriesTheEarlierInvoiceId_AsAReference_D176()
    {
        var earlier = WithEarlierInvoice(Prior("INV-099.1", "INV-099", 1, BillingPriorStatus.Active, new BillingPriorLine
        {
            Date = LineDate, TimekeeperName = "Jane Doe", TimekeeperId = Matched().TimekeeperId, Hours = 1.5m, Narrative = "draft motion",
        }));

        var finding = Run(LinesOnly, WithPriors(Facts(Entry(0)), earlier)).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateOfPriorSubmissionLine);
        ShouldCiteTheEarlierInvoiceAsEvidenceOnly(finding);
    }

    [Fact]
    public void LineLevel_DuplicateOfEarlierLine_CarriesNoEarlierInvoiceId_D176()
    {
        // The earlier line is on this very invoice, so there is no other invoice to cite, even when a prior has an id.
        var facts = WithPriors(Facts(Entry(0), Entry(1)), WithEarlierInvoice(Prior("INV-100.1", "INV-100", 1)));

        var finding = Run(LinesOnly, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateOfEarlierLine);
        finding.ValuesRead.Select(v => v.Name).Should().NotContain("duplicateOf.invoiceId");
        Everything(finding).Should().NotContain(EarlierInvoiceText);
    }

    [Fact]
    public void WithheldFindings_CarryNoEarlierInvoiceId_AtInvoiceOrLineLevel_D176()
    {
        var hiddenInvoicePrior = WithEarlierInvoice(WalledInvoicePrior(Roots(RootB)));
        var hiddenLinePrior = WithEarlierInvoice(WalledLinePrior(Roots(RootB)));

        var invoiceLevel = Run(InvoiceNumberOnly, InvoiceUnder(Roots(RootA), Header(hiddenInvoicePrior))).Should().ContainSingle().Subject;
        var lineLevel = Run(LinesOnly, InvoiceUnder(Roots(RootA), WithPriors(Facts(MatchingLine()), hiddenLinePrior))).Should().ContainSingle().Subject;

        foreach (var finding in new[] { invoiceLevel, lineLevel })
        {
            ShouldBeWithheld(finding);
            finding.ValuesRead.Select(v => v.Name).Should().NotContain("duplicateOf.invoiceId");
            Everything(finding).Should().NotContain(EarlierInvoiceText);
        }
    }

    [Fact]
    public void APriorWithNoInvoiceId_AddsNoReference_NeverAnEmptyOne_D176()
    {
        var earlier = Prior("INV-099.1", "INV-099", 1, BillingPriorStatus.Active, new BillingPriorLine
        {
            Date = LineDate, TimekeeperName = "Jane Doe", TimekeeperId = Matched().TimekeeperId, Hours = 1.5m, Narrative = "draft motion",
        });
        var invoiceLevel = Run(InvoiceNumberOnly, Header(Prior("INV-100.1", "INV-100", 1))).Should().ContainSingle().Subject;
        var lineLevel = Run(LinesOnly, WithPriors(Facts(Entry(0)), earlier)).Should().ContainSingle().Subject;

        invoiceLevel.ValuesRead.Select(v => v.Name).Should().NotContain("duplicateOf.invoiceId");
        lineLevel.ValuesRead.Select(v => v.Name).Should().NotContain("duplicateOf.invoiceId");

        var emptyId = Prior("INV-100.1", "INV-100", 1) with { InvoiceId = Guid.Empty };
        Run(InvoiceNumberOnly, Header(emptyId)).Single().ValuesRead.Select(v => v.Name).Should().NotContain("duplicateOf.invoiceId");
    }

    [Fact]
    public void TheWithheldVariant_NamesNoTokenAtAll_AndOnlyDuplicateHasOne_D112()
    {
        BillingRuleCatalogue.WithheldTemplateFactFields(RuleType.Duplicate).Should().BeEmpty();
        BillingRuleCatalogue.WithheldDuplicateTemplate.Should().NotContain("{{").And.Contain("matches an earlier submission");
        BillingRuleCatalogue.TemplateFactFields(RuleType.Duplicate).Should().Contain("duplicateOf.trackingNumber");

        var act = () => BillingRuleCatalogue.WithheldTemplateFactFields(RuleType.InvoiceAge);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
