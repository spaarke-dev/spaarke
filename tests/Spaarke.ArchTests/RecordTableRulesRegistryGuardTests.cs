using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Field-mapping-server-write-path-r1 task 012 (spec FR-11): every table on a server create path's fixed accept-list (the
/// browser create route and the Office quick-create) has an entry in the per-table rules registry (<see cref="RecordTableRules"/>). A create table with no entry has no declared protected
/// columns, owner exception, search-index or mapping policy, so the unified create service (task 013) would have nothing
/// to enforce for it.
/// </summary>
/// <remarks>
/// <para><b>The create tables.</b> Read from the compiled create paths, not from the registry (a set derived from the
/// registry would make the guard pass by construction): the browser create route's accept-list
/// (<see cref="ChildRecordEndpoints.CreateTables"/>) and the Office quick-create's Matter and Project, as the adapter
/// (<c>OfficeService.QuickCreateViaCreationServiceAsync</c>) names its table — <see cref="QuickCreateFieldRequirements.GetLogicalName"/>
/// (field mapping task 018c; the deleted Office creation core's constants before it). Its Invoice is on the route's list.
/// And the budget inquiry's service request (<c>BudgetInquiryExecutor.Table</c>, task 017 item 9).
/// The chat <c>dataverse.create_record</c> tool has no accept-list (any table but <c>sprk_document</c>), so
/// it is not covered. The unified create service (task 013) accepts exactly the registry's tables — an unregistered table
/// is refused (<c>record_creation.table_unsupported</c>) — so its accepted set needs no entry here.</para>
/// <para><b>Controls.</b> <see cref="Guard_MustFire_ACreateTableWithNoEntry_IsReported"/> adds a fixture table the registry
/// does not hold and requires the guard to report it, against the real registry and against a fixture lookup;
/// <see cref="Guard_MustNotFire_EveryEntryPresent_ReportsNothing"/> is the same detector over a fixture where every table
/// has an entry.</para>
/// </remarks>
public class RecordTableRulesRegistryGuardTests
{
    private const string FixtureTableWithNoEntry = "sprk_registryguardfixture_noentry";

    /// <summary>Every table on a server create path's fixed accept-list today.</summary>
    private static IReadOnlyList<string> CreateTables() =>
        ChildRecordEndpoints.CreateTables
            .Append(QuickCreateFieldRequirements.GetLogicalName(QuickCreateEntityType.Matter))
            .Append(QuickCreateFieldRequirements.GetLogicalName(QuickCreateEntityType.Project))
            // Field mapping task 017 (item 9): the budget inquiry's fixed table, which the service creates once 017c
            // re-points the inquiry.
            .Append(Sprk.Bff.Api.Services.Signals.Actions.BudgetInquiryExecutor.Table)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

    /// <summary>The detector: each create table <paramref name="hasEntry"/> does not know.</summary>
    private static IReadOnlyList<string> TablesWithoutAnEntry(IEnumerable<string> createTables, Func<string, bool> hasEntry) =>
        createTables.Where(t => !hasEntry(t)).ToList();

    [Fact]
    public void EveryCreateTable_HasARegistryEntry()
    {
        var tables = CreateTables();
        var missing = TablesWithoutAnEntry(tables, t => RecordTableRules.Find(t) is not null);

        Assert.True(tables.Count >= 13, $"The create-table set shrank to {tables.Count}; the guard would check too little.");
        Assert.True(missing.Count == 0,
            "These create tables have no entry in RecordTableRules (Services/RecordCreation/RecordTableRules.cs) — add one " +
            "with the table's root/child kind, protected columns and mapping policy: " + string.Join(", ", missing));
    }

    [Fact]
    public void Guard_MustFire_ACreateTableWithNoEntry_IsReported()
    {
        var withFixture = CreateTables().Append(FixtureTableWithNoEntry).ToList();

        Assert.Equal([FixtureTableWithNoEntry],
            TablesWithoutAnEntry(withFixture, t => RecordTableRules.Find(t) is not null));

        var registered = new HashSet<string>(["sprk_todo", "sprk_matter"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["sprk_event"],
            TablesWithoutAnEntry(["sprk_todo", "sprk_event", "SPRK_Matter"], registered.Contains));
    }

    [Fact]
    public void Guard_MustNotFire_EveryEntryPresent_ReportsNothing()
    {
        var registered = new HashSet<string>(["sprk_todo", "sprk_event", "sprk_matter"], StringComparer.OrdinalIgnoreCase);

        Assert.Empty(TablesWithoutAnEntry(["sprk_todo", "sprk_event", "SPRK_Matter"], registered.Contains));
    }
}
