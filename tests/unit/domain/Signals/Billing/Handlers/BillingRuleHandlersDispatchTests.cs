using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, the closed dispatch (spec FR-24, D-27). Maintain-class (ADR-038): pure logic, no mocks. The enumeration
/// test is the guard that catches a new <see cref="RuleType"/> member: a billing member with no handler, no sample body or no
/// case in the dispatch fails here, and a non-billing member must be refused rather than evaluated.
/// </summary>
public class BillingRuleHandlersDispatchTests
{
    /// <summary>One valid body per billing type. A new billing member added without a body fails the enumeration test.</summary>
    private static readonly IReadOnlyDictionary<RuleType, string> SampleBodies = new Dictionary<RuleType, string>
    {
        [RuleType.ApprovedTimekeeper] = """{"type":"ApprovedTimekeeper"}""",
        [RuleType.ApprovedRate] = """{"type":"ApprovedRate","tolerancePercent":0}""",
        [RuleType.TimeIncrement] = """{"type":"TimeIncrement","incrementHours":0.5}""",
        [RuleType.DailyHoursCap] = """{"type":"DailyHoursCap","maxHoursPerDay":12}""",
        [RuleType.NonBillableActivity] = """{"type":"NonBillableActivity","codes":["A101"]}""",
        [RuleType.ExpenseRule] = """{"type":"ExpenseRule","nonReimbursableCodes":["E109"],"maxAmountPerItem":500}""",
        [RuleType.InvoiceAge] = """{"type":"InvoiceAge","maxAgeDays":90}""",
        [RuleType.CoreRecordWindow] = """{"type":"CoreRecordWindow","graceDays":0}""",
        [RuleType.Duplicate] = """{"type":"Duplicate","matchFields":["firmInvoiceNumber","timekeeper","lineDate","hours"]}""",
        [RuleType.RequiredData] = """{"type":"RequiredData","fields":["taskCode"]}""",
        [RuleType.AiObservation] = """{"type":"AiObservation","observation":"blockBilling","minConfidence":0.85}""",
        [RuleType.AddedFee] = """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E101","E109"],"capAmount":100}""",
        [RuleType.Discount] = """{"type":"Discount","discountPercent":10}""",
    };

    /// <summary>A fact set on which every one of the eleven sample rules fires at least once.</summary>
    private static BillingFactSet EverythingWrong()
    {
        var first = Fee(0) with
        {
            Date = new DateOnly(2026, 3, 2), // before the core record opened; far older than 90 days against the invoice below
            TimekeeperName = "Unlisted Person",
            Timekeeper = Unmatched(TimekeeperUnmatchedReason.NotOnRoster),
            ApprovedRate = Rate(575m),
            BilledRate = 700m,
            Hours = 13.25m, // not a multiple of 0.5, over the 12-hour cap
            Amount = 9275m,
            Narrative = "Research, call, draft and review",
            ActivityCode = "A101",
            BilledTaskCode = null,
            Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.95m, QuotedText = "Research, call, draft and review" }],
        };

        return Facts(first, first with { Index = 1 }, Expense(2) with { ExpenseCode = "E109", Amount = 600m }) with
        {
            Invoice = Invoice() with { InvoiceDate = new DateOnly(2026, 8, 1), Currency = "EUR" },
            CoreRecord = new BillingCoreRecordFacts { OpenDate = new DateOnly(2026, 3, 10), CloseDate = new DateOnly(2026, 12, 31), IsClosed = false },
            PriorSubmissions = [Prior("INV-100.1", "INV-100", 1)],
        };
    }

    // ── The guard ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryRuleTypeMember_IsABillingTypeWithAHandlerAndASampleBody_OrIsRefusedFailClosed()
    {
        var facts = EverythingWrong();

        foreach (var type in Enum.GetValues<RuleType>())
        {
            if (RuleDomains.IsBilling(type) && !BillingRuleCatalogue.IsSystemType(type))
            {
                RuleDomains.DomainOf(type).Should().Be(RuleDomain.Billing);
                BillingRuleHandlers.HasHandler(type).Should().BeTrue($"billing rule type {type} must have a handler (a new member needs a case in BillingRuleHandlers.HandlerFor)");
                SampleBodies.Should().ContainKey(type, $"billing rule type {type} needs a sample body in this guard");

                var rule = Rule(SampleBodies[type]);
                rule.RuleType.Should().Be(type);
                var act = () => BillingRuleHandlers.Evaluate(rule, Origin, facts);
                act.Should().NotThrow($"{type} must evaluate a well-formed fact set");
            }
            else
            {
                // D-125: the system type is a Billing member with no handler on purpose (049 creates its findings directly).
                BillingRuleHandlers.HasHandler(type).Should().BeFalse($"{type} is not a handled billing type and must not be dispatched");
                var anyRule = Rule(SampleBodies[RuleType.ApprovedTimekeeper]);
                var refuse = () => BillingRuleHandlers.EvaluateAs(type, anyRule, Origin, facts);
                refuse.Should().Throw<BillingRuleRefusedException>().Which.ReasonCode.Should().Be(BillingRuleRefusedException.NoHandler);
            }
        }
    }

    [Fact]
    public void TheDispatch_CoversExactlyTheAuthorableBillingTypesOfTheCatalogue()
    {
        var handled = Enum.GetValues<RuleType>().Where(BillingRuleHandlers.HasHandler).ToArray();

        handled.Should().BeEquivalentTo(BillingRuleCatalogue.Types);
        handled.Should().HaveCount(13, "the eleven of task 071 plus AddedFee and Discount (task 115)");
        SampleBodies.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.Types);
    }

    [Theory]
    [InlineData(RuleType.Threshold)]
    [InlineData(RuleType.Switch)]
    [InlineData(RuleType.Existence)]
    [InlineData((RuleType)(-1))]
    [InlineData((RuleType)0)]
    [InlineData((RuleType)999)]
    [InlineData((RuleType)100000014)] // the next option value after the eleven: a billing type added without a handler
    [InlineData((RuleType)100000017)] // the next free option value after Discount (task 115)
    public void ATypeWithNoHandler_IsRefusedWithAStableCode_NeverEvaluatedAndNeverHolds(RuleType type)
    {
        var rule = Rule(SampleBodies[RuleType.ApprovedTimekeeper]);

        var act = () => BillingRuleHandlers.EvaluateAs(type, rule, Origin, Facts(Fee(0)));

        var refusal = act.Should().Throw<BillingRuleRefusedException>().Which;
        refusal.ReasonCode.Should().Be("billing.rule.no_handler");
        refusal.RuleType.Should().Be(type);
        BillingRuleHandlers.HasHandler(type).Should().BeFalse();
    }

    [Fact]
    public void ARuleWithAnOntologyType_IsRefusedByTheBillingGate_SoNoDefinitionOfItEverReachesTheDispatch()
    {
        var snapshot = new PolicyVersionSnapshot(VersionId, PolicyCode, "Threshold", """{"type":"Threshold"}""", null);

        Gate().TryPrepareBillingRule(snapshot, out var rule).Should().BeFalse();
        rule.Should().BeNull();
    }

    // ── Behaviour of the whole set ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OnAFactSetThatBreaksEveryRule_EachOfTheElevenTypesFiresAtLeastOnce_InAStableOrder()
    {
        var facts = EverythingWrong();

        foreach (var (type, body) in SampleBodies)
        {
            var findings = Run(Rule(body), facts);

            findings.Should().NotBeEmpty($"{type} must fire on the fact set that breaks every rule");
            findings.Should().OnlyContain(f => f.RuleType == type);
        }
    }

    [Fact]
    public void OnACleanFactSet_NoneOfTheElevenTypesFires()
    {
        var clean = Facts(Fee(0), Expense(1)) with
        {
            CoreRecord = new BillingCoreRecordFacts { OpenDate = new DateOnly(2026, 1, 1), CloseDate = new DateOnly(2026, 12, 31) },
        };

        foreach (var (type, body) in SampleBodies)
        {
            // The clean lines carry A101 and L110; the non-billable list names A101, so give that rule its own code.
            var rule = type == RuleType.NonBillableActivity ? Rule("""{"type":"NonBillableActivity","codes":["L999"]}""") : Rule(body);

            Run(rule, clean).Should().BeEmpty($"{type} must not fire on a clean invoice");
        }
    }

    [Fact]
    public void AnEmptyInvoice_IsJudgedWithoutAnyFinding_NotAnError()
    {
        var empty = Facts();

        foreach (var (type, body) in SampleBodies.Where(p => p.Key != RuleType.RequiredData))
        {
            Run(Rule(body), empty).Should().BeEmpty($"{type} has no line to judge");
        }
    }

    [Fact]
    public void EvaluateRefusesNullArguments()
    {
        var rule = Rule(SampleBodies[RuleType.ApprovedTimekeeper]);

        ((Action)(() => BillingRuleHandlers.Evaluate(null!, Origin, Facts()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => BillingRuleHandlers.Evaluate(rule, null!, Facts()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => BillingRuleHandlers.Evaluate(rule, Origin, null!))).Should().Throw<ArgumentNullException>();
    }

    // ── Missing parameters are refused ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RuleType.ApprovedRate, nameof(BillingRuleDefinition.TolerancePercent))]
    [InlineData(RuleType.TimeIncrement, nameof(BillingRuleDefinition.IncrementHours))]
    [InlineData(RuleType.DailyHoursCap, nameof(BillingRuleDefinition.MaxHoursPerDay))]
    [InlineData(RuleType.InvoiceAge, nameof(BillingRuleDefinition.MaxAgeDays))]
    [InlineData(RuleType.CoreRecordWindow, nameof(BillingRuleDefinition.GraceDays))]
    [InlineData(RuleType.AiObservation, nameof(BillingRuleDefinition.MinConfidence))]
    [InlineData(RuleType.Discount, nameof(BillingRuleDefinition.DiscountPercent))]
    [InlineData(RuleType.AddedFee, nameof(BillingRuleDefinition.CapAmount))]
    public void ARuleThatLacksTheParameterItsTypeNeeds_IsRefused_NotEvaluatedAsIfItHeld(RuleType type, string property)
    {
        var rule = Corrupt(Rule(SampleBodies[type]), property, null);

        var act = () => BillingRuleHandlers.Evaluate(rule, Origin, EverythingWrong());

        act.Should().Throw<BillingRuleRefusedException>().Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
    }

    [Theory]
    [InlineData(RuleType.NonBillableActivity, nameof(BillingRuleDefinition.Codes))]
    [InlineData(RuleType.Duplicate, nameof(BillingRuleDefinition.MatchFields))]
    [InlineData(RuleType.RequiredData, nameof(BillingRuleDefinition.Fields))]
    [InlineData(RuleType.AddedFee, nameof(BillingRuleDefinition.FeeCodes))]
    public void ARuleWithAnEmptyListItsTypeNeeds_IsRefused(RuleType type, string property)
    {
        var rule = Corrupt(Rule(SampleBodies[type]), property, Array.Empty<string>());

        var act = () => BillingRuleHandlers.Evaluate(rule, Origin, EverythingWrong());

        act.Should().Throw<BillingRuleRefusedException>().Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
    }

    [Fact]
    public void RequireAndRequireList_ReturnAPresentValue_AndRefuseAnAbsentOne()
    {
        var context = new HandlerContext(Rule(SampleBodies[RuleType.ApprovedTimekeeper]), Origin, Facts());

        BillingRuleHandlers.Require<decimal>(1.5m, context, "p").Should().Be(1.5m);
        BillingRuleHandlers.RequireList(["a"], context, "p").Should().Equal("a");

        ((Action)(() => BillingRuleHandlers.Require<decimal>(null, context, "p"))).Should().Throw<BillingRuleRefusedException>()
            .Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
        ((Action)(() => BillingRuleHandlers.RequireList([], context, "p"))).Should().Throw<BillingRuleRefusedException>()
            .Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
    }

    // ── The template-token contract (task 070) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AHandlerThatNamesATokenOutsideItsTypesFactFieldSet_Fails_NotSilentlyAcceptsIt()
    {
        var context = new HandlerContext(Rule(SampleBodies[RuleType.ApprovedTimekeeper]), Origin, Facts(Fee(0)));
        var values = new[] { HandlerContext.Reference("x", "y") };

        // line.hours is a TimeIncrement token, not an ApprovedTimekeeper one.
        var act = () => context.Finding(BillingSubject.ForLine(0), "x", values, new FactValues().Add("line.hours", "1"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*line.hours*fact-field set*");
    }

    [Fact]
    public void AnInTokenValue_IsAccepted_AndABlankOneIsLeftOut()
    {
        var context = new HandlerContext(Rule(SampleBodies[RuleType.ApprovedTimekeeper]), Origin, Facts(Fee(0)));
        var values = new[] { HandlerContext.Reference("x", "y") };

        var finding = context.Finding(
            BillingSubject.ForLine(0), "x", values, new FactValues().Add("line.date", "2026-03-02").Add("line.timekeeperName", "  ").Add("line.timekeeperName", null));

        finding.FactValues.Should().BeEquivalentTo(new Dictionary<string, string> { ["line.date"] = "2026-03-02" });
    }

    [Fact]
    public void EveryTypesFindings_AreBuiltFromItsOwnTokens_OnTheFactSetThatBreaksEveryRule()
    {
        // Run() already asserts FactValues.Keys is a subset of the type's token set for every finding; this asserts the
        // contract is not vacuous: each type's findings between them carry at least one token.
        var facts = EverythingWrong();

        foreach (var (type, body) in SampleBodies)
        {
            Run(Rule(body), facts).SelectMany(f => f.FactValues.Keys).Should().NotBeEmpty($"{type} findings must carry template values");
        }
    }
}
