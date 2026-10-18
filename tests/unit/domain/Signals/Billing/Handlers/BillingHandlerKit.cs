using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// Shared builders for the billing handler tests (ebilling-r1 task 071). A rule is obtained the only way production obtains one,
/// through the billing gate, so a test cannot hand a handler a rule the gate would refuse. <see cref="Run"/> asserts, for every
/// finding of every test, the contract that holds for all eleven types: policy, version and clause are carried, the template values
/// are within the type's fact-field set (task 070), every extracted value names its field, and a second run gives equal findings.
/// </summary>
internal static class BillingHandlerKit
{
    internal static readonly Guid PolicyId = Guid.Parse("07100000-0000-4000-8000-000000000001");
    internal static readonly Guid VersionId = Guid.Parse("07100000-0000-4000-8000-000000000002");
    internal const string PolicyCode = "BILL-071";
    internal const string Clause = "4.2 Rates are fixed for the engagement.";
    internal const string ClauseLocation = "OCG section 4.2, page 7";
    internal const decimal Threshold = 0.90m;

    internal static readonly DateOnly InvoiceDate = new(2026, 3, 31);
    internal static readonly DateOnly LineDate = new(2026, 3, 2);

    /// <summary>A decimal from invariant text. Theory data uses text: a double literal would round-trip through floating point.</summary>
    internal static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    internal static BillingRuleOrigin Origin { get; } = new(PolicyId, Clause, ClauseLocation);

    internal static PolicyVersionValidator Gate() =>
        new(new RuleBodySchemaValidator(),
            new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DateTimeOffset.UtcNow)),
            NullLogger<PolicyVersionValidator>.Instance);

    /// <summary>The rule for <paramref name="body"/> as the billing gate parses it. The body must carry its own <c>type</c>.</summary>
    internal static BillingRuleDefinition Rule(string body)
    {
        var type = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("type").GetString();
        Gate().TryPrepareBillingRule(new PolicyVersionSnapshot(VersionId, PolicyCode, type, body, null), out var rule)
            .Should().BeTrue($"the test body must be valid: {body}");
        return rule!;
    }

    // ── Facts ────────────────────────────────────────────────────────────────────────────────────────────────────

    internal static IReadOnlyDictionary<string, decimal> Confidence(decimal value = 0.95m) =>
        new[]
        {
            BillingFieldNames.Date, BillingFieldNames.TimekeeperAsBilled, BillingFieldNames.Hours, BillingFieldNames.Rate,
            BillingFieldNames.Amount, BillingFieldNames.Narrative, BillingFieldNames.TaskCode, BillingFieldNames.ActivityCode,
            BillingFieldNames.ExpenseCode, BillingFieldNames.BilledTaskCode, BillingFieldNames.BilledActivityCode,
            BillingFieldNames.BilledExpenseCode, BillingFieldNames.FirmInvoiceNumber, BillingFieldNames.InvoiceDate,
            BillingFieldNames.BillingPeriodStart, BillingFieldNames.BillingPeriodEnd, BillingFieldNames.Currency,
        }.ToDictionary(n => n, _ => value);

    internal static IReadOnlyDictionary<string, decimal> With(this IReadOnlyDictionary<string, decimal> map, string field, decimal? value)
    {
        var copy = map.ToDictionary(p => p.Key, p => p.Value);
        if (value is null) copy.Remove(field); else copy[field] = value.Value;
        return copy;
    }

    internal static TimekeeperResolution Matched(Guid? id = null) => new()
    {
        TimekeeperId = id ?? Guid.Parse("07100000-0000-4000-8000-0000000000a1"),
        Stage = TimekeeperMatchStage.Exact,
        MatchConfidence = 1m,
    };

    internal static TimekeeperResolution Unmatched(TimekeeperUnmatchedReason reason) => new() { Reason = reason };

    internal static RateResolution Rate(decimal approved) => new()
    {
        Status = RateResolutionStatus.Found,
        ApprovedRate = approved,
        RackRate = approved + 100m,
        RateCardId = Guid.Parse("07100000-0000-4000-8000-0000000000c1"),
        RateId = Guid.Parse("07100000-0000-4000-8000-0000000000c2"),
        Scope = RateCardScope.Firm,
        Basis = RateBasis.Timekeeper,
    };

    /// <summary>A clean fee line: a matched timekeeper, a rate at the approved rate, one hour, every confidence 0.95. Customise with <c>with</c>.</summary>
    internal static BillingLineFacts Fee(int index = 0) => new()
    {
        Index = index,
        LineType = InvoiceLineType.Fee,
        Date = LineDate,
        TimekeeperName = "Jane Doe",
        Timekeeper = Matched(),
        ApprovedRate = Rate(575m),
        Hours = 1.0m,
        BilledRate = 575m,
        Amount = 575m,
        Narrative = "Draft motion to compel",
        TaskCode = "L110",
        ActivityCode = "A101",
        BilledTaskCode = "L110",
        BilledActivityCode = "A101",
        FieldConfidence = Confidence(),
    };

    /// <summary>A clean expense line: a copying charge of 40.00.</summary>
    internal static BillingLineFacts Expense(int index = 0) => new()
    {
        Index = index,
        LineType = InvoiceLineType.Expense,
        Date = LineDate,
        Amount = 40m,
        Narrative = "Photocopies",
        ExpenseCode = "E101",
        BilledExpenseCode = "E101",
        FieldConfidence = Confidence(),
    };

    /// <summary>An ordinary record: no secure root above it (D-112).</summary>
    internal static readonly IReadOnlySet<Guid> Ordinary = new HashSet<Guid>();

    /// <summary>The secure roots above a record (D-112); each one is its own wall.</summary>
    internal static IReadOnlySet<Guid> Roots(params Guid[] roots) => new HashSet<Guid>(roots);

    internal static BillingInvoiceFacts Invoice() => new()
    {
        SubmissionId = Guid.Parse("07100000-0000-4000-8000-0000000000b1"),
        SecureRoots = Ordinary,
        TrackingNumber = "INV-100.2",
        FirmInvoiceNumber = "INV-100",
        InvoiceDate = InvoiceDate,
        BillingPeriodStart = new DateOnly(2026, 3, 1),
        BillingPeriodEnd = InvoiceDate,
        Currency = "USD",
        FieldConfidence = Confidence(),
    };

    internal static BillingFactSet Facts(params BillingLineFacts[] lines) => new()
    {
        Invoice = Invoice(),
        Lines = lines,
        ReliabilityThreshold = Threshold,
    };

    internal static BillingPriorSubmission Prior(
        string trackingNumber, string? firmInvoiceNumber, int sequence, BillingPriorStatus status = BillingPriorStatus.Active,
        params BillingPriorLine[] lines) => new()
    {
        SubmissionId = Guid.NewGuid(),
        SecureRoots = Ordinary,
        TrackingNumber = trackingNumber,
        FirmInvoiceNumber = firmInvoiceNumber,
        Sequence = sequence,
        Status = status,
        Lines = lines,
    };

    // ── Running a handler ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates <paramref name="rule"/> against <paramref name="facts"/> through the public dispatch, twice, and asserts the
    /// contract every finding of every type must meet. Returns the findings.
    /// </summary>
    internal static IReadOnlyList<BillingFinding> Run(BillingRuleDefinition rule, BillingFactSet facts)
    {
        var first = BillingRuleHandlers.Evaluate(rule, Origin, facts);
        var second = BillingRuleHandlers.Evaluate(rule, Origin, facts);
        second.Should().BeEquivalentTo(first, o => o.WithStrictOrdering(), "the same input gives the same findings in the same order");

        var tokens = BillingRuleCatalogue.TemplateFactFields(rule.RuleType);
        foreach (var finding in first)
        {
            finding.RuleType.Should().Be(rule.RuleType);
            finding.PolicyId.Should().Be(PolicyId);
            finding.PolicyCode.Should().Be(PolicyCode);
            finding.PolicyVersionId.Should().Be(VersionId);
            finding.ClauseReference.Should().Be(Clause);
            finding.ClauseLocation.Should().Be(ClauseLocation);
            finding.ReasonCode.Should().NotBeNullOrWhiteSpace();
            finding.ValuesRead.Should().NotBeEmpty();
            finding.FactValues.Keys.Should().BeSubsetOf(tokens, "a template may name only the fact fields task 070 defined for the type");
            finding.FactValues.Values.Any(string.IsNullOrWhiteSpace).Should().BeFalse("a null fact is not evidence (a withheld finding, D-112, has none at all)");

            if (finding.Subject.Kind == BillingSubjectKind.Invoice)
            {
                finding.Subject.LineIndex.Should().BeNull();
            }
            else
            {
                facts.Lines.Select(l => (int?)l.Index).Should().Contain(finding.Subject.LineIndex);
            }

            foreach (var value in finding.ValuesRead)
            {
                value.IsExtracted.Should().Be(value.ExtractedField.HasValue);
                if (!value.IsExtracted)
                {
                    value.Confidence.Should().BeNull("a reference value has no extraction confidence");
                    value.BelowReliabilityThreshold.Should().BeFalse();
                }
                else
                {
                    value.BelowReliabilityThreshold.Should().Be(
                        value.Confidence is null || value.Confidence < facts.ReliabilityThreshold,
                        "NFR-05: an unknown or below-threshold confidence is flagged");
                }
            }

            finding.ReadFields.Should().Equal(finding.ValuesRead.Where(v => v.IsExtracted).Select(v => v.ExtractedField!.Value));
            finding.ReadsLowConfidenceValue.Should().Be(finding.ValuesRead.Any(v => v.BelowReliabilityThreshold));
        }

        return first;
    }

    internal static BillingValueRead Value(this BillingFinding finding, string name) =>
        finding.ValuesRead.Single(v => v.Name == name);

    /// <summary>A copy of <paramref name="rule"/> with one parameter forced to <paramref name="value"/>, past the gate (a state the gate never produces).</summary>
    internal static BillingRuleDefinition Corrupt(BillingRuleDefinition rule, string property, object? value)
    {
        var field = typeof(BillingRuleDefinition).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"No backing field for {property}.");
        field.SetValue(rule, value);
        return rule;
    }
}
