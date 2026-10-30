using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.RecordCreation;

/// <summary>
/// The invoice-family pre-write rule's registration (field-mapping-server-write-path-r1 task 017, the ebilling-r1 reconcile;
/// task 012 notes, "Invoice-family reconciliation recipe"): every <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/>
/// table has a registry entry whose pre-write rules contain <see cref="RecordTableRules.InvoiceFamilyRule"/>, so no create
/// through the unified service skips ebilling's FR-05 core-record guard. Pure registry data, no I/O — hence
/// <c>tests/unit/domain/**</c> (ADR-038 §2 path 6).
/// </summary>
/// <remarks>
/// <para><b>The ratchet is retired to zero.</b> Task 013's baseline (<c>{sprk_invoice, sprk_billingevent}</c>, the family
/// tables registered without a rule) expired at task 017: the rule is now registered on all six, so the baseline machinery is
/// gone and the ratchet is "no family table in the registry lacks a pre-write rule". The family is the guard's own key set,
/// not a copy.</para>
/// <para><b>Enforced by:</b> these tests. Must-fire controls: a family table registered without a rule, a family table whose
/// entry lost the invoice-family rule, and a family table with no entry are each reported; must-not-fire: the registry as it
/// is.</para>
/// </remarks>
public class RecordTableRulesPreWriteRatchetTests
{
    private static IReadOnlyCollection<string> InvoiceFamilyTables => InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys.ToArray();

    // ── The 012 recipe: every family table carries THE rule ──────────────────────────────────────────────────────────

    [Fact]
    public void EveryInvoiceFamilyTable_HasARegistryEntry_WhosePreWriteRulesContainTheInvoiceFamilyRule()
    {
        InvoiceFamilyTables.Should().HaveCount(6, "the guard's six family tables (InvoiceFamilyCoreRecordGuard.CoreRecordLookups)");

        TablesWithoutTheInvoiceFamilyRule(RecordTableRules.All).Should().BeEmpty(
            "a family table whose create does not run the invoice-family rule skips ebilling's FR-05 guard (task 017)");
    }

    [Fact]
    public void Recipe_FiresOnATableThatLostTheRule_AndOnATableWithNoEntry()
    {
        var registry = RecordTableRules.All
            .Where(r => !string.Equals(r.Table, "sprk_invoiceapproval", StringComparison.OrdinalIgnoreCase))
            .Select(r => string.Equals(r.Table, "sprk_invoice", StringComparison.OrdinalIgnoreCase) ? r with { PreWriteRules = [] } : r)
            .ToList();

        TablesWithoutTheInvoiceFamilyRule(registry).Should().BeEquivalentTo(["sprk_invoice", "sprk_invoiceapproval"],
            "a table that lost the rule and a table with no entry are both reported");
    }

    [Fact]
    public void Recipe_FiresOnATableWhoseOnlyRuleIsAnotherRule()
    {
        var other = new PreWriteRule(new NamedRule("Fixture", "A fixture rule."), (_, _) => Task.FromResult<PreWriteRefusal?>(null));
        var registry = RecordTableRules.All
            .Select(r => string.Equals(r.Table, "sprk_billingevent", StringComparison.OrdinalIgnoreCase) ? r with { PreWriteRules = [other] } : r)
            .ToList();

        TablesWithoutTheInvoiceFamilyRule(registry).Should().Equal("sprk_billingevent");
    }

    // ── The ratchet (baseline retired): no family table in the registry has no pre-write rule ─────────────────────────

    [Fact]
    public void NoInvoiceFamilyTableInTheRegistry_IsWithoutAPreWriteRule()
    {
        UnguardedInvoiceFamilyTables(RecordTableRules.All).Should().BeEmpty(
            "the task 013 baseline expired at task 017: register the invoice-family rule instead of adding a family table without it");
    }

    [Fact]
    public void Ratchet_FiresOnAFamilyTableWithoutARule_AndNotOnOneWithIt()
    {
        var withoutRule = RecordTableRules.Get("sprk_invoicesubmission") with { PreWriteRules = [] };
        var registry = RecordTableRules.All
            .Where(r => !string.Equals(r.Table, withoutRule.Table, StringComparison.OrdinalIgnoreCase))
            .Append(withoutRule);

        UnguardedInvoiceFamilyTables(registry).Should().Equal("sprk_invoicesubmission");
    }

    private static IReadOnlyList<string> TablesWithoutTheInvoiceFamilyRule(IEnumerable<RecordTableRule> registry)
    {
        var byTable = registry.ToDictionary(r => r.Table, StringComparer.OrdinalIgnoreCase);
        return InvoiceFamilyTables
            .Where(t => !byTable.TryGetValue(t, out var rule) || !rule.PreWriteRules.Contains(RecordTableRules.InvoiceFamilyRule))
            .ToList();
    }

    private static IReadOnlyList<string> UnguardedInvoiceFamilyTables(IEnumerable<RecordTableRule> registry) =>
        registry
            .Where(r => InvoiceFamilyTables.Contains(r.Table, StringComparer.OrdinalIgnoreCase) && r.PreWriteRules.Count == 0)
            .Select(r => r.Table)
            .ToList();
}
