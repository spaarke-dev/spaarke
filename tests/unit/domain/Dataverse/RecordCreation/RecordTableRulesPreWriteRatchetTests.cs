using FluentAssertions;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.RecordCreation;

/// <summary>
/// A RATCHET on the invoice-family pre-write rule (field-mapping-server-write-path-r1 task 013 fix round 2, ebilling-r1
/// review): an invoice-family table in the registry with no pre-write rule is a table whose create skips the ebilling FR-05
/// core-record guard. Today exactly two such tables exist, and the baseline may only shrink. Pure registry data, no I/O —
/// hence <c>tests/unit/domain/**</c> (ADR-038 §2 path 6).
/// </summary>
/// <remarks>
/// <para><b>Baseline expiry: task 017.</b> The invoice-family rule must be registered on every invoice-family table before
/// the chat create is re-pointed at the service (task 012 notes, "Invoice-family reconciliation recipe"). When it is,
/// <see cref="UnguardedBaseline"/> becomes empty and this test becomes "every invoice-family table has the rule".</para>
/// <para><b>Enforced by:</b> this test. <c>InvoiceFamilyCoreRecordGuard.CoreRecordLookups</c> (the source of truth for the
/// family) exists only on <c>origin/work/ebilling-r1</c>, not on this branch, so <see cref="InvoiceFamilyTables"/> copies its
/// six keys; the reconciliation replaces the copy with the real key set. A family table added to the registry WITHOUT a
/// rule is not in the baseline, so it fails the first test (must-fire control: the second test).</para>
/// </remarks>
public class RecordTableRulesPreWriteRatchetTests
{
    /// <summary>The six <c>InvoiceFamilyCoreRecordGuard.CoreRecordLookups</c> keys (task 012 notes, recipe step 2).</summary>
    private static readonly string[] InvoiceFamilyTables =
    [
        "sprk_invoice", "sprk_billingevent", "sprk_invoicesubmission", "sprk_invoiceapproval",
        "sprk_externalidentifier", "sprk_invoicecorrection",
    ];

    /// <summary>Known violations, to be worked down. EXPIRES at task 017 (register the invoice-family rule).</summary>
    private static readonly string[] UnguardedBaseline = ["sprk_invoice", "sprk_billingevent"];

    [Fact]
    public void InvoiceFamilyTablesWithNoPreWriteRule_AreExactlyTheKnownBaseline()
    {
        var unguarded = UnguardedInvoiceFamilyTables(RecordTableRules.All);

        unguarded.Should().BeEquivalentTo(UnguardedBaseline,
            "the baseline may only shrink: register the invoice-family rule (task 017) instead of adding a table without it");
    }

    [Fact]
    public void Ratchet_FiresOnANewInvoiceFamilyTableWithoutARule_AndNotOnOneWithIt()
    {
        var withoutRule = new RecordTableRule { Table = "sprk_invoicesubmission", Kind = RecordTableKind.Child };
        var withRule = new RecordTableRule
        {
            Table = "sprk_invoiceapproval",
            Kind = RecordTableKind.Child,
            PreWriteRules = [new PreWriteRule(new NamedRule("Fixture", "A fixture rule."), (_, _) => Task.FromResult<PreWriteRefusal?>(null))],
        };

        var unguarded = UnguardedInvoiceFamilyTables([.. RecordTableRules.All, withoutRule, withRule]);

        unguarded.Should().BeEquivalentTo(UnguardedBaseline.Append("sprk_invoicesubmission"),
            "an added family table with no rule is outside the baseline, and one with a rule is not counted");
    }

    private static IReadOnlyList<string> UnguardedInvoiceFamilyTables(IEnumerable<RecordTableRule> registry) =>
        registry
            .Where(r => InvoiceFamilyTables.Contains(r.Table, StringComparer.OrdinalIgnoreCase) && r.PreWriteRules.Count == 0)
            .Select(r => r.Table)
            .ToList();
}
