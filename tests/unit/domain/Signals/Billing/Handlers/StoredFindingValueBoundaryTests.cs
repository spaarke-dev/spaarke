using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 130c fix round 3 (G): the value boundary between the handlers and the firm-facing projection of a rejected
/// submission's stored findings. The projection shows a value only when its stored entry names a <c>field</c> (a value extracted from the
/// firm's own invoice) that the AI did not assign; a value the handler read from the system's own tables carries no field. This holds
/// today because each handler leaves the field off its reference values. The test drives the REAL handler of every reject-capable
/// reason of the catalogue (<see cref="BillingRuleCatalogue.RejectCapableReasons"/>) with reference data present, stores each finding
/// through <see cref="InvoiceSubmissionPersister.FindingEntryJson"/> and checks the stored shape, so a handler that attaches a field to a
/// reference value, or a new reject-capable reason with no fixture, fails here. Maintain-class (ADR-038): pure logic, no mocks.
/// </summary>
public class StoredFindingValueBoundaryTests
{
    private static readonly DateOnly Open = new(2026, 1, 10);
    private static readonly DateOnly Close = new(2026, 3, 20);

    /// <summary>
    /// One reject-capable reason driven to a finding: the rule body, the facts (reference data present, set up so the handler fires) and the
    /// reference values the finding must carry. The reference names are listed so a handler that stops writing one cannot pass silently.
    /// </summary>
    private sealed record Fixture(string Body, Func<BillingFactSet> Facts, string[] ReferenceNames);

    private const string CoreBody = """{"type":"CoreRecordWindow","graceDays":0}""";
    private const string ExpenseBody = """{"type":"ExpenseRule","nonReimbursableCodes":["E109"],"maxAmountPerItem":500}""";

    private static BillingFactSet WithCore(BillingCoreRecordFacts core, DateOnly lineDate) =>
        Facts(Fee(0) with { Date = lineDate }) with { CoreRecord = core };

    private static readonly string[] CoreReferences = ["coreRecord.openDate", "coreRecord.closeDate", "coreRecord.closed", "coreRecord.billingStatus"];

    /// <summary>One fixture per (type, reason) of the catalogue's reject-capable set; <see cref="TheFixtures_CoverExactlyTheCataloguesRejectCapableReasons"/> keeps it in step.</summary>
    private static readonly IReadOnlyDictionary<(RuleType Type, string Reason), Fixture> Fixtures = new Dictionary<(RuleType, string), Fixture>
    {
        [(RuleType.ApprovedRate, BillingReasonCodes.RateAboveApproved)] = new(
            """{"type":"ApprovedRate","tolerancePercent":0}""",
            () => Facts(Fee(2) with { BilledRate = 650m, ApprovedRate = Rate(575m) }),
            ["approvedRate", "approvedRate.limit", "rateCard.id", "rateCard.rateId"]),
        [(RuleType.ExpenseRule, BillingReasonCodes.ExpenseCodeNotReimbursable)] = new(
            ExpenseBody, () => Facts(Expense(0) with { BilledExpenseCode = "E109" }), []),
        [(RuleType.ExpenseRule, BillingReasonCodes.ExpenseAmountOverCap)] = new(
            ExpenseBody, () => Facts(Expense(0) with { Amount = 900m }), []),
        [(RuleType.InvoiceAge, BillingReasonCodes.InvoiceTooOld)] = new(
            """{"type":"InvoiceAge","maxAgeDays":90}""", () => Facts(Fee(0) with { Date = InvoiceDate.AddDays(-91) }), []),
        [(RuleType.CoreRecordWindow, BillingReasonCodes.BeforeCoreRecordOpened)] = new(
            CoreBody, () => WithCore(new BillingCoreRecordFacts { OpenDate = Open, CloseDate = Close }, Open.AddDays(-1)), CoreReferences),
        [(RuleType.CoreRecordWindow, BillingReasonCodes.AfterCoreRecordClosed)] = new(
            CoreBody, () => WithCore(new BillingCoreRecordFacts { OpenDate = Open, CloseDate = Close }, Close.AddDays(1)), CoreReferences),
        [(RuleType.CoreRecordWindow, BillingReasonCodes.CoreRecordClosed)] = new(
            CoreBody, () => WithCore(new BillingCoreRecordFacts { OpenDate = Open, IsClosed = true }, Open.AddDays(5)), CoreReferences),
        [(RuleType.CoreRecordWindow, BillingReasonCodes.CoreRecordClosedToBilling)] = new(
            CoreBody,
            () => WithCore(new BillingCoreRecordFacts { OpenDate = Open, CloseDate = Close, BillingStatus = CoreRecordBillingStatus.Closed }, Open.AddDays(5)),
            CoreReferences),
        [(RuleType.Duplicate, BillingReasonCodes.DuplicateInvoiceNumber)] = new(
            """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""",
            () => Facts(Fee(0)) with { PriorSubmissions = [Prior("INV-100.1", "INV-100", 1)] },
            ["duplicateOf.trackingNumber"]),
        [(RuleType.RequiredData, BillingReasonCodes.RequiredFieldMissing)] = new(
            """{"type":"RequiredData","fields":["firmInvoiceNumber","narrative","taskCode"]}""",
            () => Facts(Fee(0) with { Narrative = null, BilledTaskCode = null }) with { Invoice = Invoice() with { FirmInvoiceNumber = null } },
            []),
    };

    /// <summary>Every (type, reason) the catalogue lets reject, read from the catalogue itself.</summary>
    private static IEnumerable<(RuleType Type, string Reason)> RejectCapable() =>
        Enum.GetValues<RuleType>()
            .Where(t => RuleDomains.IsBilling(t) && !BillingRuleCatalogue.IsSystemType(t))
            .SelectMany(t => BillingRuleCatalogue.RejectCapableReasons(t).OrderBy(r => r, StringComparer.Ordinal).Select(r => (t, r)));

    public static IEnumerable<object[]> RejectCapableCases() => RejectCapable().Select(c => new object[] { c.Type, c.Reason });

    /// <summary>The extracted-field names: every <see cref="BillingFieldNames"/> constant that names a field of the firm's invoice (not the observation prefix, not AI-assigned).</summary>
    private static readonly IReadOnlySet<string> ExtractedFieldNames = typeof(BillingFieldNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .Where(name => name != BillingFieldNames.ObservationPrefix && !BillingFieldNames.IsAiAssigned(name))
        .ToHashSet(StringComparer.Ordinal);

    [Fact(DisplayName = "130c fix 3 (G): the fixtures cover exactly the catalogue's reject-capable (type, reason) set; a new reason without a fixture fails")]
    public void TheFixtures_CoverExactlyTheCataloguesRejectCapableReasons()
    {
        RejectCapable().Should().NotBeEmpty();
        Fixtures.Keys.Select(k => $"{k.Type}:{k.Reason}").Should().BeEquivalentTo(
            RejectCapable().Select(c => $"{c.Type}:{c.Reason}"), "each reject-capable reason needs a fixture that drives its real handler");
    }

    [Fact(DisplayName = "130c fix 3 (G): the extracted-field set is the invoice's own fields, never the AI-assigned ones")]
    public void TheExtractedFieldSet_HoldsTheInvoicesFields_NotTheAiAssignedOnes()
    {
        ExtractedFieldNames.Should().Contain([
            BillingFieldNames.Rate, BillingFieldNames.Hours, BillingFieldNames.Amount, BillingFieldNames.Date, BillingFieldNames.BilledTaskCode,
            BillingFieldNames.BilledExpenseCode, BillingFieldNames.FirmInvoiceNumber, BillingFieldNames.InvoiceDate, BillingFieldNames.Narrative]);
        ExtractedFieldNames.Should().NotContain([
            BillingFieldNames.TaskCode, BillingFieldNames.ActivityCode, BillingFieldNames.ExpenseCode, BillingFieldNames.ObservationBlockBilling,
            BillingFieldNames.ObservationPrefix]);
    }

    [Theory(DisplayName = "130c fix 3 (G): a real reject-capable finding is stored with a field only on values extracted from the invoice, and none on a reference value")]
    [MemberData(nameof(RejectCapableCases))]
    public void ARealRejectCapableFinding_IsStoredWithAFieldOnlyOnExtractedValues(RuleType type, string reason)
    {
        Fixtures.TryGetValue((type, reason), out var fixture).Should().BeTrue($"{type} / {reason} is reject-capable in the catalogue and needs a fixture");

        var findings = Run(Rule(fixture!.Body), fixture.Facts());

        // Only the findings the projection could show: a reject-capable reason of the finding's own type.
        var shown = findings.Where(f => BillingRuleCatalogue.IsRejectCapableReason(f.RuleType, f.ReasonCode)).ToList();
        shown.Should().Contain(f => f.ReasonCode == reason, "the fixture must drive the real handler to a finding of this reason, or the test proves nothing");

        var entries = new List<JsonNode?>();
        foreach (var (finding, index) in shown.Select((f, i) => (f, i)))
        {
            var decision = new FindingDecision { Index = index, Finding = finding, Consequence = BillingConsequence.Reject, Rejects = true, ValuesRead = finding.ValuesRead };
            var entry = InvoiceSubmissionPersister.FindingEntryJson(index, finding, decision, scrub: false);
            entries.Add(entry);

            var stored = entry["valuesRead"]!.AsArray();
            stored.Count.Should().Be(finding.ValuesRead.Count);
            for (var i = 0; i < stored.Count; i++)
            {
                var read = finding.ValuesRead[i];
                var field = stored[i]!["field"]?.GetValue<string>();

                if (!string.IsNullOrWhiteSpace(field))
                {
                    ExtractedFieldNames.Should().Contain(field, $"a stored field must be a field of the firm's invoice ({read.Name})");
                    BillingFieldNames.IsAiAssigned(field).Should().BeFalse($"the AI's output is not the firm's invoice ({read.Name})");
                    read.IsExtracted.Should().BeTrue($"{read.Name} is stored with a field, so it must be an extracted value");
                }

                if (!read.IsExtracted)
                {
                    string.IsNullOrWhiteSpace(field).Should().BeTrue($"the reference value {read.Name} is read from the system's own tables and carries no field");
                }
            }

            // The reference values the fixture names carry no field, whatever the handler's own flag says about them.
            for (var i = 0; i < stored.Count; i++)
            {
                if (fixture.ReferenceNames.Contains(finding.ValuesRead[i].Name))
                {
                    string.IsNullOrWhiteSpace(stored[i]!["field"]?.GetValue<string>()).Should().BeTrue($"{finding.ValuesRead[i].Name} is reference data");
                }
            }
        }

        // The reference values the fixture expects are really there (reference data was present), so the checks above ran on them.
        var ofReason = shown.Where(f => f.ReasonCode == reason).ToList();
        foreach (var referenceName in fixture.ReferenceNames)
        {
            ofReason.Should().Contain(f => f.ValuesRead.Any(v => v.Name == referenceName && !v.IsExtracted), $"{referenceName} must be read as a reference value");
        }

        // End to end: the projection's own parse of the stored document shows no reference value, and a Duplicate shows none at all.
        var document = new JsonObject { ["kind"] = "AutoRejected", ["findings"] = new JsonArray(entries.ToArray()) }.ToJsonString();
        var parsed = FirmRejectionFindings.Parse(document, NullLogger.Instance, Guid.NewGuid());
        parsed.NotShown.Should().Be(0);
        parsed.Findings.Should().HaveCount(shown.Count);
        var referenceNames = shown.SelectMany(f => f.ValuesRead).Where(v => !v.IsExtracted).Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        parsed.Findings.SelectMany(f => f.ValuesRead).Select(v => v.Name).Should().NotIntersectWith(referenceNames);
        if (type == RuleType.Duplicate)
        {
            parsed.Findings.SelectMany(f => f.ValuesRead).Should().BeEmpty("a Duplicate quotes an earlier invoice and projects no value");
        }
    }
}
