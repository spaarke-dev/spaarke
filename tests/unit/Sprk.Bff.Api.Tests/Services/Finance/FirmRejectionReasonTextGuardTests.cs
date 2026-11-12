using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 130d: the guard on the closed, firm-safe rejection text table (<see cref="FirmRejectionReasonText.Table"/>). The table text is
/// partner-visible, so the table must cover exactly the catalogue's reject-capable reason codes
/// (<see cref="BillingRuleCatalogue.RejectCapableReasons"/>, read here from the catalogue and never from a copy of the list), every entry must be
/// plain static text, and none may hold a format placeholder. A reason code added to the catalogue without an entry fails here, so a firm is
/// never shown the generic fallback because someone forgot a row. Maintain-class (ADR-038): pure logic, no mocks. Enforced by: this guard (CI).
/// </summary>
public sealed class FirmRejectionReasonTextGuardTests
{
    /// <summary>Every reject-capable reason code of every Billing type, read from the catalogue.</summary>
    private static IReadOnlySet<string> CatalogueCodes() =>
        Enum.GetValues<RuleType>()
            .Where(t => RuleDomains.IsBilling(t) && !BillingRuleCatalogue.IsSystemType(t))
            .SelectMany(t => BillingRuleCatalogue.RejectCapableReasons(t))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>What is wrong with <paramref name="table"/> against the <paramref name="catalogue"/> (empty when nothing is).</summary>
    private static List<string> Violations(IReadOnlySet<string> catalogue, IReadOnlyDictionary<string, string> table)
    {
        var problems = new List<string>();
        problems.AddRange(catalogue.Where(code => !table.TryGetValue(code, out var text) || string.IsNullOrWhiteSpace(text))
            .Select(code => $"{code}: reject-capable but has no non-blank entry"));
        problems.AddRange(table.Keys.Where(code => !catalogue.Contains(code)).Select(code => $"{code}: has an entry but is not reject-capable"));
        problems.AddRange(table.Where(e => e.Value.Contains('{') || e.Value.Contains('}')).Select(e => $"{e.Key}: holds a format placeholder"));
        problems.AddRange(table.Where(e => e.Value.Length > FirmRejectionFindings.MaxValueTextLength).Select(e => $"{e.Key}: longer than the shown limit"));
        return problems;
    }

    [Fact(DisplayName = "130d guard: the rejection text table covers exactly the catalogue's reject-capable reason codes, with plain static text")]
    public void TheTable_CoversExactlyTheCataloguesRejectCapableReasons()
    {
        CatalogueCodes().Should().NotBeEmpty();
        Violations(CatalogueCodes(), FirmRejectionReasonText.Table).Should().BeEmpty();
    }

    [Fact(DisplayName = "130d guard: the catalogue has the ten codes the table was written for (a change here is reviewed, not silent)")]
    public void TheCatalogue_HasTheReviewedTenCodes()
    {
        CatalogueCodes().Should().BeEquivalentTo(
        [
            BillingReasonCodes.RateAboveApproved, BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap,
            BillingReasonCodes.InvoiceTooOld, BillingReasonCodes.BeforeCoreRecordOpened, BillingReasonCodes.AfterCoreRecordClosed,
            BillingReasonCodes.CoreRecordClosed, BillingReasonCodes.CoreRecordClosedToBilling, BillingReasonCodes.DuplicateInvoiceNumber,
            BillingReasonCodes.RequiredFieldMissing,
        ]);
    }

    [Fact(DisplayName = "130d guard must-fire: a synthetic catalogue with an extra reject-capable code fails")]
    public void MustFire_AnExtraCatalogueCode_Fails()
    {
        var catalogue = CatalogueCodes().Append("synthetic.new_reason").ToHashSet(StringComparer.Ordinal);

        Violations(catalogue, FirmRejectionReasonText.Table).Should().ContainSingle().Which.Should().StartWith("synthetic.new_reason").And.Contain("no non-blank entry");
    }

    [Fact(DisplayName = "130d guard must-fire: a table code that is not reject-capable, a blank entry, a placeholder and an over-long entry each fail")]
    public void MustFire_ExtraBlankPlaceholderAndLongEntries_Fail()
    {
        var table = new Dictionary<string, string>(FirmRejectionReasonText.Table, StringComparer.Ordinal)
        {
            ["approved_timekeeper.unmatched"] = "Not reject-capable.",
            [BillingReasonCodes.InvoiceTooOld] = "   ",
            [BillingReasonCodes.CoreRecordClosed] = "The matter {matterName} is closed.",
            [BillingReasonCodes.RequiredFieldMissing] = new string('a', FirmRejectionFindings.MaxValueTextLength + 1),
        };

        var problems = Violations(CatalogueCodes(), table);

        problems.Should().Contain(p => p.StartsWith("approved_timekeeper.unmatched") && p.Contains("not reject-capable"));
        problems.Should().Contain(p => p.StartsWith(BillingReasonCodes.InvoiceTooOld) && p.Contains("no non-blank entry"));
        problems.Should().Contain(p => p.StartsWith(BillingReasonCodes.CoreRecordClosed) && p.Contains("placeholder"));
        problems.Should().Contain(p => p.StartsWith(BillingReasonCodes.RequiredFieldMissing) && p.Contains("longer"));
        problems.Should().HaveCount(4);
    }

    [Fact(DisplayName = "130d guard must-not-fire: a catalogue and a table that agree, in any order, report nothing")]
    public void MustNotFire_AMatchingCatalogueAndTable_ReportsNothing()
    {
        var catalogue = new HashSet<string>(["a.one", "a.two"], StringComparer.Ordinal);
        var table = new Dictionary<string, string> { ["a.two"] = "Second.", ["a.one"] = "First sentence. Second sentence." };

        Violations(catalogue, table).Should().BeEmpty();
    }

    [Fact(DisplayName = "130d guard: no entry quotes a value, a name, an amount, a date, a clause or a section (static plain English only)")]
    public void EveryEntry_IsStaticPlainText()
    {
        foreach (var (code, text) in FirmRejectionReasonText.Table)
        {
            text.Should().NotBeNullOrWhiteSpace(code);
            text.Should().Be(text.Trim(), code);
            text.Should().MatchRegex(@"^[A-Z][A-Za-z ,.\-']+[.]$", $"{code}: plain sentences only, no digits, quotes, placeholders or markup");
            text.Count(c => c == '.').Should().BeInRange(1, 2, $"{code}: one or two sentences");
            text.Should().NotContain(code, "the raw code is not part of the text");
        }

        FirmRejectionReasonText.GenericSentence.Should().MatchRegex(@"^[A-Z][A-Za-z ,.\-']+[.]$");
    }
}
