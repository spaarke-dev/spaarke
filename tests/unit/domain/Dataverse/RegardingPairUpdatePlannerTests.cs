using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using static Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 045b: the update planner (design §2.2; owner rulings Q1, Q2, Q4, Q5, Q7) every
/// generic update path runs before its write — over each pair table, with the path's own reader faked. Maintain-class
/// (ADR-038, <c>tests/unit/domain/**</c>): the planner's rules are the regression net for "an update leaves the ADR-024 pair
/// whole and in step".
/// </summary>
public sealed class RegardingPairUpdatePlannerTests
{
    private static readonly Guid MatterType = Guid.Parse("a0450b00-0000-4000-8000-000000000001");
    private static readonly Guid MatterA = Guid.Parse("a0450b00-0000-4000-8000-0000000000a1");
    private static readonly Guid MatterB = Guid.Parse("a0450b00-0000-4000-8000-0000000000b1");
    private static readonly Guid Invoice = Guid.Parse("a0450b00-0000-4000-8000-0000000000c1");
    private static readonly Guid Row = Guid.Parse("a0450b00-0000-4000-8000-0000000000f1");

    private static string MatterNameColumn => RegardingRecordType.GetPrimaryNameField("sprk_matter")!;

    /// <summary>The path's reader, faked: the row, the catalog and the regarded record, each with the status it fails with.</summary>
    private sealed class Reads
    {
        public RegardingPairRow? Row { get; set; }
        public int RowStatus { get; set; } = 404;
        public Dictionary<Guid, RegardingPairType> Types { get; } = new();
        public int TypeStatus { get; set; } = 404;
        public Dictionary<Guid, Dictionary<string, string?>> Records { get; } = new();
        public int RowReads { get; private set; }
        public int TypeReads { get; private set; }

        public RegardingPairReaders Readers => new(
            _ =>
            {
                RowReads++;
                return Task.FromResult(Row is { } row
                    ? RegardingPairRead<RegardingPairRow>.Ok(row)
                    : new RegardingPairRead<RegardingPairRow>(null, RowStatus, null));
            },
            (id, _) =>
            {
                TypeReads++;
                return Task.FromResult(Types.TryGetValue(id, out var type)
                    ? RegardingPairRead<RegardingPairType>.Ok(type)
                    : new RegardingPairRead<RegardingPairType>(null, TypeStatus, null));
            },
            (_, id, _, _) => Task.FromResult(Records.TryGetValue(id, out var values)
                ? RegardingPairRead<IReadOnlyDictionary<string, string?>>.Ok(values)
                : new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, RecordStatus, null)));

        /// <summary>What a record read answers for a record not in <see cref="Records"/>.</summary>
        public int RecordStatus { get; set; } = 404;

        public Reads KnowsTheMatterType()
        {
            Types[MatterType] = new RegardingPairType("sprk_matter", "sprk_matternumber");
            Records[MatterA] = new() { [MatterNameColumn] = "Acme v. Widget", ["sprk_matternumber"] = "M-0001" };
            Records[MatterB] = new() { [MatterNameColumn] = "Beta v. Gamma", ["sprk_matternumber"] = "M-0002" };
            return this;
        }
    }

    private static RegardingPairRow RowOf(string? id = null, Guid? type = null, params (string Column, Guid Id)[] lookups) =>
        new(id, type, lookups.ToDictionary(l => l.Column, l => l.Id, StringComparer.OrdinalIgnoreCase));

    private static Task<RegardingPairPlan> Plan(string table, Dictionary<string, object?> payload, Reads reads) =>
        PlanRegardingPairAsync(table, PairChangeOf(table, payload), reads.Readers, CancellationToken.None);

    private static Dictionary<string, object?> TypeOnly() => new()
    {
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})",
    };

    private static void ShouldHaveTheMattersDisplay(RegardingPairPlan plan, Guid matter, string name, string number)
    {
        plan.Columns["sprk_regardingrecordname"].Should().Be(name);
        plan.Columns["sprk_regardingrecordnumber"].Should().Be(number);
        plan.Columns["sprk_regardingrecordurl"].Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", matter.ToString("D")));
    }

    private static void ShouldClearThePair(RegardingPairPlan plan)
    {
        plan.Failed.Should().BeFalse();
        plan.Columns.Should().ContainKey(SecureRootInheritance.PairIdColumn).WhoseValue.Should().BeNull();
        plan.Columns.Should().ContainKey(SecureRootInheritance.PairTypeColumn).WhoseValue.Should().BeNull();
        foreach (var display in PairDisplayColumns)
            plan.Columns.Should().ContainKey(display).WhoseValue.Should().BeNull();
        plan.ClearsThePair.Should().BeTrue();
    }

    // ── Q1 (a): the 18 pair tables ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThePairTables_AreTheEighteen()
    {
        RegardingPairTables.Should().HaveCount(18);
        RegardingPairTables.Should().Contain(["sprk_todo", "sprk_event", "sprk_invoice", "sprk_workassignment", "sprk_project", "sprk_memo"]);
        RegardingPairTables.Should().NotContain("sprk_matter", "the matter's lone type column is no pair");
    }

    // ── Rule 2 (Q2 (a)): a type with no id after the write ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ATypeOnlyUpdate_FillsTheIdFromTheOneAgreeingLookup_AndDerivesTheDisplay()
    {
        var reads = new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();

        var plan = await Plan("sprk_workassignment", TypeOnly(), reads);

        plan.Failed.Should().BeFalse();
        plan.Columns[SecureRootInheritance.PairIdColumn].Should().Be(MatterA.ToString("D"));
        plan.Columns.Should().NotContainKey(SecureRootInheritance.PairTypeColumn, "the update's own type stands");
        ShouldHaveTheMattersDisplay(plan, MatterA, "Acme v. Widget", "M-0001");
        plan.ClearsThePair.Should().BeFalse();
    }

    public static TheoryData<string> PairTablesButTheProject() =>
        new(RegardingPairTables.Where(t => t != "sprk_project").OrderBy(t => t, StringComparer.Ordinal));

    /// <summary>Q1 (a): every pair table but the BFF-managed project — a type-only update naming the table of one of its
    /// agreement columns is filled from that column; a table with none has its type cleared.</summary>
    [Theory]
    [MemberData(nameof(PairTablesButTheProject))]
    public async Task ATypeOnlyUpdate_OnEveryPairTable_IsFilledFromItsAgreementColumn(string table)
    {
        var agreement = PairAgreementColumns(table);
        var reads = new Reads();
        if (agreement.Count == 0)
        {
            reads.Row = RowOf();
            reads.Types[MatterType] = new RegardingPairType("sprk_matter", null);
            ShouldClearThePair(await Plan(table, TypeOnly(), reads));
            return;
        }

        var (column, target) = agreement.First();
        reads.Row = RowOf(lookups: (column, MatterA));
        reads.Types[MatterType] = new RegardingPairType(target, null);
        reads.Records[MatterA] = new(); // the record the lookup names exists and is readable (a 404 would clear the display)

        var plan = await Plan(table, TypeOnly(), reads);

        plan.Failed.Should().BeFalse();
        plan.Columns[SecureRootInheritance.PairIdColumn].Should().Be(MatterA.ToString("D"), $"{table}.{column} names the {target}");
        plan.Columns["sprk_regardingrecordurl"].Should().Be(RegardingRecordType.BuildRecordUrl(target, MatterA.ToString("D")));
    }

    [Fact]
    public async Task ATypeOnlyUpdate_FillsFromALookupTheUpdateItselfSets()
    {
        var reads = new Reads { Row = RowOf() }.KnowsTheMatterType();
        var payload = TypeOnly();
        payload["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({MatterB:D})";

        var plan = await Plan("sprk_workassignment", payload, reads);

        plan.Columns[SecureRootInheritance.PairIdColumn].Should().Be(MatterB.ToString("D"));
    }

    [Fact]
    public async Task ATypeOnlyPush_NeverFillsFromALookupThePushMapped_OnlyFromOneTheRowKeeps()
    {
        // Q2 (as 045a's step 4½): a mapped lookup is never a fill candidate. The push's whole payload is mapped values.
        var payload = TypeOnly();
        payload["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({MatterB:D})";
        var change = PairChangeOf("sprk_workassignment", payload);

        var mappedOnly = await PlanRegardingPairAsync("sprk_workassignment", change,
            new Reads { Row = RowOf() }.KnowsTheMatterType().Readers, default, lookupsAreMapped: true);
        var kept = await PlanRegardingPairAsync("sprk_todo", PairChangeOf("sprk_todo", new Dictionary<string, object?>
        {
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})",
            ["sprk_RegardingContact@odata.bind"] = $"/contacts({Guid.NewGuid():D})",
        }),
            new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType().Readers, default, lookupsAreMapped: true);

        ShouldClearThePair(mappedOnly);
        kept.Columns[SecureRootInheritance.PairIdColumn].Should().Be(MatterA.ToString("D"), "a lookup the row keeps is no mapped value");
    }

    [Fact]
    public async Task ATypeOnlyUpdate_WithNoAgreeingLookup_ClearsTheTypeAndTheDisplay()
    {
        var reads = new Reads { Row = RowOf(lookups: ("sprk_regardingproject", MatterA)) }.KnowsTheMatterType();

        ShouldClearThePair(await Plan("sprk_workassignment", TypeOnly(), reads));
    }

    [Fact]
    public async Task ATypeOnlyUpdate_NeverFillsFromAStampColumn_WhileAnIntermediateIsBound()
    {
        // Q2: the to-do is filed under an invoice; its matter column is the I-1 stamp copied from the invoice. A matter type
        // with no id is not completed from the stamp.
        PairAgreementColumns("sprk_todo").Should().ContainKeys("sprk_regardingmatter", "sprk_regardinginvoice");
        var stamped = new Reads { Row = RowOf(lookups: [("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", MatterA)]) }
            .KnowsTheMatterType();
        var direct = new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();

        ShouldClearThePair(await Plan("sprk_todo", TypeOnly(), stamped));
        (await Plan("sprk_todo", TypeOnly(), direct)).Columns[SecureRootInheritance.PairIdColumn]
            .Should().Be(MatterA.ToString("D"), "the control: a matter column with no intermediate bound is a direct filing");
    }

    [Fact]
    public async Task ATypeOnlyPush_MappingAnIntermediate_NeverFillsFromTheStampColumnTheRowKeeps()
    {
        // Q2: the push maps the to-do under an invoice and sends a matter type with no id. The matter column the row keeps
        // becomes the invoice's I-1 stamp, so it is no candidate, though the mapped invoice itself is never one either.
        var payload = TypeOnly();
        payload["sprk_RegardingInvoice@odata.bind"] = $"/sprk_invoices({Invoice:D})";

        var plan = await PlanRegardingPairAsync("sprk_todo", PairChangeOf("sprk_todo", payload),
            new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType().Readers, default,
            lookupsAreMapped: true);

        ShouldClearThePair(plan);
    }

    [Fact]
    public async Task ATypeOnlyUpdate_WithTwoDifferentAgreeingRecords_ClearsThePair()
    {
        // A pair table with two agreement columns naming one target table (no stamp column among them, so rule 2 counts both).
        var stampColumns = CoreAncestorResolver.CoreAncestorLookups.Select(s => s.LookupAttribute).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var twice = RegardingPairTables.Where(t => t != "sprk_project")
            .SelectMany(t => PairAgreementColumns(t).Where(c => !stampColumns.Contains(c.Key)).GroupBy(c => c.Value)
                .Where(g => g.Count() >= 2).Select(g => (Table: t, Target: g.Key, Columns: g.Select(c => c.Key).ToArray())))
            .FirstOrDefault();
        twice.Table.Should().NotBeNull("the precondition: some pair table has two agreement columns naming one table");
        var type = Guid.NewGuid();
        var reads = new Reads { Row = RowOf(lookups: [(twice.Columns[0], MatterA), (twice.Columns[1], MatterB)]) };
        reads.Types[type] = new RegardingPairType(twice.Target, null);

        ShouldClearThePair(await Plan(twice.Table, new() { ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({type:D})" }, reads));
    }

    [Fact]
    public async Task ATypeOnlyUpdate_WhoseCatalogRowDoesNotExist_IsLeftAsSent_ForTheWritesOwnUniformNotFound()
    {
        var reads = new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)), TypeStatus = 404 };

        var plan = await Plan("sprk_workassignment", TypeOnly(), reads);

        plan.Should().BeSameAs(RegardingPairPlan.Nothing);
    }

    [Fact]
    public async Task ATypeOnlyUpdate_WhoseCatalogRowTheReaderMayNotRead_IsATypeNamingNoTable_AndIsCleared()
    {
        // As 045a's step 4½ (fix round 2): never stored type-only past a read that cannot tell the table.
        var reads = new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)), TypeStatus = 403 };

        ShouldClearThePair(await Plan("sprk_workassignment", TypeOnly(), reads));
    }

    [Fact]
    public async Task ATypeOnlyUpdate_WhoseCatalogReadFails_IsTheReadsFailure_AndNothingIsPlanned()
    {
        var reads = new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)), TypeStatus = 500 };

        var plan = await Plan("sprk_workassignment", TypeOnly(), reads);

        plan.Failed.Should().BeTrue();
        plan.FailedStatus.Should().Be(500);
        plan.Columns.Should().BeEmpty("never a half pair");
    }

    [Fact]
    public async Task AnUpdate_WhoseRowCannotBeRead_IsTheReadsFailure()
    {
        var reads = new Reads { RowStatus = 503 };

        var plan = await Plan("sprk_workassignment", TypeOnly(), reads);

        plan.Failed.Should().BeTrue();
        plan.FailedStatus.Should().Be(503);
    }

    // ── Rule 1: a cleared id takes its type and display columns with it ─────────────────────────────────────────────────

    [Fact]
    public async Task ClearingTheId_ClearsTheTypeAndTheDisplay()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType) }.KnowsTheMatterType();

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = null }, reads);

        plan.Columns.Should().ContainKey(SecureRootInheritance.PairTypeColumn).WhoseValue.Should().BeNull();
        foreach (var display in PairDisplayColumns)
            plan.Columns.Should().ContainKey(display).WhoseValue.Should().BeNull();
        plan.Columns.Should().NotContainKey(SecureRootInheritance.PairIdColumn, "the update's own clear stands");
    }

    [Fact]
    public async Task ClearingTheId_KeepsADisplayColumnTheUpdateSendsItself()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType) };

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = "", ["sprk_regardingrecordname"] = "kept" }, reads);

        plan.Columns.Should().NotContainKey("sprk_regardingrecordname");
        plan.Columns.Should().ContainKey("sprk_regardingrecordurl");
    }

    // ── Rule 3 (Q7 (b)): a moved pair's display columns are re-derived ──────────────────────────────────────────────────

    [Fact]
    public async Task MovingThePairId_ReDerivesTheDisplay_FromTheRowsType()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType) }.KnowsTheMatterType();

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = MatterB.ToString("D") }, reads);

        ShouldHaveTheMattersDisplay(plan, MatterB, "Beta v. Gamma", "M-0002");
        plan.Columns.Should().NotContainKeys(SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn);
    }

    [Fact]
    public async Task ReTypingThePairOverTheSameId_IsAMove_AndReDerivesTheDisplay()
    {
        // (project, X) → (matter, X) names another record: its display columns follow (045c coordinator finding).
        var projectType = Guid.NewGuid();
        var reads = new Reads { Row = RowOf(MatterB.ToString("D"), projectType) }.KnowsTheMatterType();

        var plan = await Plan("sprk_communication", TypeOnly(), reads);

        ShouldHaveTheMattersDisplay(plan, MatterB, "Beta v. Gamma", "M-0002");
        plan.Columns.Should().NotContainKeys(SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn);
    }

    [Fact]
    public async Task MovingThePair_ToARecordTheReaderCannotRead_LeavesTheNameAndNumberEmpty_NeverStale()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType) }.KnowsTheMatterType();
        var unreadable = Guid.NewGuid();

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = unreadable.ToString("D") }, reads);

        plan.Columns["sprk_regardingrecordname"].Should().BeNull();
        plan.Columns["sprk_regardingrecordnumber"].Should().BeNull();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task MovingThePair_WhenTheCatalogReadFails_IsTheReadsFailure_AndNothingIsPlanned(int status)
    {
        // Rule 3 as rule 2: only 403 and 404 clear the display; any other failed read writes nothing.
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType), TypeStatus = status };

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = MatterB.ToString("D") }, reads);

        plan.Failed.Should().BeTrue();
        plan.FailedStatus.Should().Be(status);
        plan.Columns.Should().BeEmpty();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task MovingThePair_WhenTheRecordReadFails_IsTheReadsFailure_AndNothingIsPlanned(int status)
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType), RecordStatus = status };
        reads.Types[MatterType] = new RegardingPairType("sprk_matter", "sprk_matternumber");

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = MatterB.ToString("D") }, reads);

        plan.Failed.Should().BeTrue();
        plan.FailedStatus.Should().Be(status);
        plan.Columns.Should().BeEmpty();
    }

    [Fact]
    public async Task MovingThePair_ToARecordTheReaderMayNotRead_ClearsEveryDisplayColumn()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType), RecordStatus = 403 };
        reads.Types[MatterType] = new RegardingPairType("sprk_matter", "sprk_matternumber");

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = MatterB.ToString("D") }, reads);

        plan.Failed.Should().BeFalse();
        foreach (var display in PairDisplayColumns)
            plan.Columns.Should().ContainKey(display).WhoseValue.Should().BeNull();
        plan.Outcome.Should().Contain("cleared").And.NotContain("re-derived");
    }

    [Fact]
    public async Task MovingThePair_WithEveryDisplayColumnSent_PlansNothing()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType) }.KnowsTheMatterType();

        var plan = await Plan("sprk_todo", new()
        {
            ["sprk_regardingrecordid"] = MatterB.ToString("D"),
            ["sprk_regardingrecordname"] = "n",
            ["sprk_regardingrecordnumber"] = "1",
            ["sprk_regardingrecordurl"] = "u",
        }, reads);

        plan.Should().BeSameAs(RegardingPairPlan.Nothing);
    }

    [Fact]
    public async Task ALongName_IsCutToTheColumnsLength()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType) }.KnowsTheMatterType();
        reads.Records[MatterB][MatterNameColumn] = new string('x', 250);

        var plan = await Plan("sprk_todo", new() { ["sprk_regardingrecordid"] = MatterB.ToString("D") }, reads);

        ((string)plan.Columns["sprk_regardingrecordname"]!).Should().HaveLength(InvoiceFamilyCoreRecordGuard.RegardingRecordNameMaxLength);
    }

    // ── Rule 4 (Q4 (b)): the pair follows the agreement lookup it repeated ─────────────────────────────────────────────

    [Fact]
    public async Task MovingTheLookupThePairRepeated_MovesThePairWithIt_OnAToDo()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType, ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();

        var plan = await Plan("sprk_todo", new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({MatterB:D})" }, reads);

        plan.Columns[SecureRootInheritance.PairIdColumn].Should().Be(MatterB.ToString("D"));
        plan.Columns.Should().NotContainKey(SecureRootInheritance.PairTypeColumn, "the type is unchanged, so it is not written again");
        ShouldHaveTheMattersDisplay(plan, MatterB, "Beta v. Gamma", "M-0002");
    }

    [Fact]
    public async Task ClearingTheLookupThePairRepeated_ClearsThePair()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType, ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();

        ShouldClearThePair(await Plan("sprk_event", new() { ["sprk_RegardingMatter@odata.bind"] = null }, reads));
    }

    [Fact]
    public async Task MovingTheLookupThePairRepeated_WhenTheCatalogReadFails_IsTheReadsFailure()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType, ("sprk_regardingmatter", MatterA)), TypeStatus = 403 };

        var plan = await Plan("sprk_todo", new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({MatterB:D})" }, reads);

        plan.Failed.Should().BeTrue("the work assignment's #1616 rule: the pair is never left behind unread");
    }

    // ── Must not fire: a whole, an empty and an id-only pair the update leaves alone ───────────────────────────────────

    [Fact]
    public async Task AnUpdateTouchingNeitherThePairNorAnAgreementLookup_ReadsNothing()
    {
        var reads = new Reads { RowStatus = 500 };

        var plan = await Plan("sprk_todo", new() { ["sprk_name"] = "Renamed", ["sprk_AssignedTo@odata.bind"] = $"/contacts({MatterA:D})" }, reads);

        plan.Should().BeSameAs(RegardingPairPlan.Nothing);
        reads.RowReads.Should().Be(0);
    }

    [Fact]
    public async Task AWholePairSentAsTheRowHoldsIt_PlansNothing()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), MatterType, ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();
        var payload = TypeOnly();
        payload["sprk_regardingrecordid"] = MatterA.ToString("D");

        (await Plan("sprk_todo", payload, reads)).Should().BeSameAs(RegardingPairPlan.Nothing);
    }

    [Fact]
    public async Task AnEmptyPair_IsLeftEmpty_WhenALookupMoves()
    {
        var reads = new Reads { Row = RowOf(lookups: ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();

        var plan = await Plan("sprk_todo", new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({MatterB:D})" }, reads);

        plan.Should().BeSameAs(RegardingPairPlan.Nothing);
        reads.TypeReads.Should().Be(0);
    }

    [Fact]
    public async Task AnIdOnlyPair_IsLeftAlone_WhenTheLookupItRepeatsMoves()
    {
        var reads = new Reads { Row = RowOf(MatterA.ToString("D"), null, ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();

        var plan = await Plan("sprk_todo", new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({MatterB:D})" }, reads);

        plan.Should().BeSameAs(RegardingPairPlan.Nothing);
    }

    [Fact]
    public async Task APairNotInStepWithTheMovedLookup_IsLeftAlone()
    {
        var reads = new Reads { Row = RowOf(MatterB.ToString("D"), MatterType, ("sprk_regardingmatter", MatterA)) }.KnowsTheMatterType();

        var plan = await Plan("sprk_todo", new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({Guid.NewGuid():D})" }, reads);

        plan.Should().BeSameAs(RegardingPairPlan.Nothing);
    }

    [Fact]
    public async Task TheProject_AndATableWithNoPair_AreNotPlanned()
    {
        var reads = new Reads { RowStatus = 500 };

        (await Plan("sprk_project", TypeOnly(), reads)).Should().BeSameAs(RegardingPairPlan.Nothing, "the project's pair is BFF-managed");
        (await Plan("sprk_matter", TypeOnly(), reads)).Should().BeSameAs(RegardingPairPlan.Nothing);
        reads.RowReads.Should().Be(0);
    }

    // ── The app-only paths (playbook Update Record, output mapping, push): the same planner, no added check ────────────

    private static Mock<IGenericEntityService> AppOnly(Entity? row, Exception? rowFault = null)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var rowSetup = entities.Setup(e => e.RetrieveAsync("sprk_workassignment", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (rowFault is not null)
            rowSetup.ThrowsAsync(rowFault);
        else
            rowSetup.ReturnsAsync(row!);
        var type = new Entity("sprk_recordtype_ref", MatterType)
        {
            ["sprk_recordlogicalname"] = "sprk_matter",
            [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber",
        };
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(type);
        entities.Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, Guid id, string[] _, CancellationToken _) =>
                new Entity(t, id) { [MatterNameColumn] = "Acme v. Widget", ["sprk_matternumber"] = "M-0001" });
        return entities;
    }

    private static Entity WorkAssignment(params (string Column, object Value)[] values)
    {
        var row = new Entity("sprk_workassignment", Row);
        foreach (var (column, value) in values)
            row[column] = value;
        return row;
    }

    [Fact]
    public async Task AppOnly_ATypeOnlyPayload_IsWrittenWhole_AndTheFillIsLogged()
    {
        var entities = AppOnly(WorkAssignment(("sprk_regardingmatter", new EntityReference("sprk_matter", MatterA))));
        var log = new PlanLog();
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})",
        };

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_workassignment", Row, payload, "UpdateRecord", log, default);

        written.Should().NotBeSameAs(payload);
        written["sprk_regardingrecordid"].Should().Be(MatterA.ToString("D"));
        written["sprk_RegardingRecordType@odata.bind"].Should().Be($"/sprk_recordtype_refs({MatterType:D})");
        written["sprk_regardingrecordname"].Should().Be("Acme v. Widget");
        written["sprk_name"].Should().Be("x");
        payload.Should().NotContainKey("sprk_regardingrecordid", "the caller's payload is not changed in place");
        log.Entries.Should().ContainSingle(e => e.EventId.Id == RegardingPairKeptWholeEvent.Id && e.Level == LogLevel.Information)
            .Which.Message.Should().NotContain(MatterA.ToString("D"), "the log never names the record the pair names");
    }

    [Fact]
    public async Task AppOnly_ClearingTheLookupThePairRepeated_ClearsThePairByItsBind_AndWarns()
    {
        var entities = AppOnly(WorkAssignment(
            ("sprk_regardingmatter", new EntityReference("sprk_matter", MatterA)),
            (SecureRootInheritance.PairIdColumn, MatterA.ToString("D")),
            (SecureRootInheritance.PairTypeColumn, new EntityReference("sprk_recordtype_ref", MatterType))));
        var log = new PlanLog();

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_workassignment", Row,
            new() { ["sprk_RegardingMatter@odata.bind"] = null }, "FIELD-MAPPING-PUSH", log, default);

        written.Should().ContainKey("sprk_RegardingRecordType@odata.bind").WhoseValue.Should().BeNull();
        written.Should().ContainKey("sprk_regardingrecordid").WhoseValue.Should().BeNull();
        written.Should().ContainKey("sprk_regardingrecordurl").WhoseValue.Should().BeNull();
        log.Entries.Should().ContainSingle(e => e.EventId.Id == RegardingPairKeptWholeEvent.Id && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AppOnly_ARowReadFault_RefusesWithAnExistingCode_AndNothingIsWritten()
    {
        var entities = AppOnly(null, new InvalidOperationException("transport"));

        var act = () => KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_workassignment", Row, TypeOnly(), "OUTPUT-MAPPING", null, default);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
    }

    [Fact]
    public async Task AppOnly_ARowThatDoesNotExist_LeavesThePayloadForTheWritesOwnNotFound()
    {
        var entities = AppOnly(null, new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "Does Not Exist"));
        var payload = TypeOnly();

        (await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_workassignment", Row, payload, "UpdateRecord", null, default))
            .Should().BeSameAs(payload);
    }

    [Fact]
    public async Task AppOnly_WithNoReader_RefusesARelevantPayload_AndPassesAnyOtherThrough()
    {
        var other = new Dictionary<string, object?> { ["sprk_name"] = "x" };

        (await KeepRegardingPairWholeAppOnlyAsync(null, "sprk_workassignment", Row, other, "UpdateRecord", null, default))
            .Should().BeSameAs(other);
        await FluentActions.Invoking(() => KeepRegardingPairWholeAppOnlyAsync(null, "sprk_workassignment", Row, TypeOnly(), "UpdateRecord", null, default))
            .Should().ThrowAsync<RecordOwnerUnresolvedException>();
    }

    [Fact]
    public void ThePairTypeNavigationProperties_MatchTheSolution()
    {
        // Web API navigation names are case-sensitive: each pair table's type is bound by the navigation property its
        // relationship to sprk_recordtype_ref declares (SpaarkeMaster), never one spelling for all (the invoice's is lower-case).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
            dir = dir.Parent;
        var relationships = Path.Combine(dir!.FullName, "src", "dataverse", "solutions", "SpaarkeMaster", "Other", "Relationships");
        var declared = Directory.EnumerateFiles(relationships, "*.xml")
            .SelectMany(f => System.Xml.Linq.XDocument.Load(f).Descendants("EntityRelationship"))
            .Where(r => string.Equals((string?)r.Element("ReferencedEntityName"), "sprk_recordtype_ref", StringComparison.OrdinalIgnoreCase)
                        && string.Equals((string?)r.Element("ReferencingAttributeName"), "sprk_regardingrecordtype", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                r => ((string)r.Element("ReferencingEntityName")!).ToLowerInvariant(),
                r => (string)r.Descendants("EntityRelationshipRole").Single(role => (string?)role.Element("RelationshipRoleType") == "1")
                    .Element("NavigationPropertyName")!);

        foreach (var table in RegardingPairTables)
        {
            declared.Should().ContainKey(table, $"{table} carries the pair's type lookup");
            RegardingRecordTypeNavigationPropertyOf(table).Should().Be(declared[table], $"{table}'s relationship declares it (case-sensitive)");
        }

        declared["sprk_invoice"].Should().Be("sprk_regardingrecordtype", "the control: the one lower-case spelling");
    }

    [Fact]
    public async Task AppOnly_AnInvoicesPairClear_IsWrittenByTheInvoicesOwnNavigationProperty()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_invoice", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", Row)
            {
                [SecureRootInheritance.PairIdColumn] = MatterA.ToString("D"),
                [SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", MatterType),
            });

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_invoice", Row,
            new() { ["sprk_regardingrecordid"] = null }, "UpdateRecord", null, default);

        written.Should().ContainKey("sprk_regardingrecordtype@odata.bind").WhoseValue.Should().BeNull();
        written.Should().NotContainKey("sprk_RegardingRecordType@odata.bind", "the invoice's navigation property is lower-case");
    }

    [Fact]
    public async Task AppOnly_AnInvoicesPairFollowingItsLookup_DoesNotWriteTheUnchangedType()
    {
        var matterColumn = PairAgreementColumns("sprk_invoice").First(c => c.Value == "sprk_matter").Key;
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_invoice", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", Row)
            {
                [matterColumn] = new EntityReference("sprk_matter", MatterA),
                [SecureRootInheritance.PairIdColumn] = MatterA.ToString("D"),
                [SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", MatterType),
            });
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
        entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterB, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", MatterB) { [MatterNameColumn] = "Beta v. Gamma" });

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_invoice", Row,
            new() { [matterColumn] = new EntityReference("sprk_matter", MatterB) }, "UpdateRecord", null, default);

        written[SecureRootInheritance.PairIdColumn].Should().Be(MatterB.ToString("D"));
        written.Keys.Should().NotContain(k => k.EndsWith("@odata.bind", StringComparison.Ordinal), "the type is unchanged, so it is not written");
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("record")]
    public async Task AppOnly_AMovesDisplayReadTimingOut_Refuses_AndNothingIsWritten(string faulting)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_todo", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_todo", Row)
            {
                [SecureRootInheritance.PairIdColumn] = MatterA.ToString("D"),
                [SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", MatterType),
            });
        var catalog = entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (faulting == "catalog")
            catalog.ThrowsAsync(new TimeoutException("socket"));
        else
            catalog.ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
        // The moved pair's display is read as the person the write runs for (F-045b-1): their read times out.
        var person = Guid.Parse("a0450b00-0000-4000-8000-0000000000aa");
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        var asPerson = new Mock<Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery>();
        asPerson.Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), person, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("socket"));

        var act = () => KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row,
            new() { ["sprk_regardingrecordid"] = MatterB.ToString("D") }, "UpdateRecord", null, default,
            movedDisplayRecord: MovedDisplayReader(asPerson.Object, entities.Object, person));

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
    }

    [Fact]
    public async Task AppOnly_APayloadTouchingNoPair_ReadsNothing()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var payload = new Dictionary<string, object?> { ["sprk_name"] = "x" };

        (await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_workassignment", Row, payload, "UpdateRecord", null, default))
            .Should().BeSameAs(payload);
        entities.Invocations.Should().BeEmpty();
    }

    // ── The caller wrapper (KeepRegardingPairWholeAsync) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CallerPath_ADisplayClearThePlannerAdds_IsWrittenAsNull_ButIsNoClearedColumn()
    {
        // An untyped pair moved: no type to re-derive the display from, so Q7 clears it. The clears are written, but the re-file
        // core reads ClearedColumns as candidate parent clears (RefileAsync), so the planner's text clears are not listed there.
        var user = new Mock<IDataverseUserClient>(MockBehavior.Strict);
        user.Setup(u => u.GetAsync($"sprk_events({Row:D})", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, System.Text.Json.JsonSerializer.SerializeToElement(
                new Dictionary<string, object?> { ["sprk_regardingrecordid"] = MatterA.ToString("D") })));
        user.Setup(u => u.GetAsync(It.Is<string>(p => p.Contains("ManyToOneRelationships")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, System.Text.Json.JsonSerializer.SerializeToElement(
                new { LogicalName = "sprk_event", ManyToOneRelationships = Array.Empty<object>() })));
        var item = new DataverseWriteItemMapper.MappedItem(
            $$"""{"sprk_regardingrecordid":"{{MatterB:D}}","sprk_regardingrecordnumber":null}""",
            ["sprk_regardingrecordid", "sprk_regardingrecordnumber"])
        {
            ClearedColumns = ["sprk_regardingrecordnumber"], // the caller's own display clear
        };

        var (prepared, refusal) = await KeepRegardingPairWholeAsync(user.Object, "sprk_event", "sprk_events", Row, item, null, default);

        refusal.Should().BeNull();
        using var body = System.Text.Json.JsonDocument.Parse(prepared.JsonBody);
        body.RootElement.GetProperty("sprk_regardingrecordname").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        body.RootElement.GetProperty("sprk_regardingrecordurl").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        prepared.ClearedColumns.Should().BeEquivalentTo(["sprk_regardingrecordnumber"],
            "only the caller's own clear is listed; the planner's display clears are not candidate parent clears");
    }

    private sealed class PlanLog : ILogger
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
