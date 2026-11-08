using System.Reflection;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 151c-2 (EB-D-182, EB-D-184; agreed with unified-access-control-r2) - <c>sprk_communication.sprk_billingexport</c>, the AP
/// email's link to its export batch, is a PLAIN REFERENCE. It is not a <c>sprk_regarding*</c> column, not a filing, not an ownership parent. If a
/// family-edge, lineage, ownership or regarding registry learned of it, a cascading change of the batch's team, or the ownership resolver reading the
/// row's lookups, could change who owns or can read the email. Two guards hold it out:
/// <list type="number">
/// <item><b>Registries.</b> No row of <c>RecordFamilyEdges</c>, <c>SecureChildLineage</c>, <c>SecureRootInheritance</c>, <c>CoreAncestorResolver</c>,
/// <c>RegardingFieldMap</c>, the communication service's <c>RegardingLookupMap</c> or <c>RecordOwnershipResolver.OwnershipParentEntities</c> names the
/// column on <c>sprk_communication</c>, nor makes the batch table an ownership parent (which would make the owner resolver read the lookup as a parent:
/// <c>RecordOwnershipContext.ParentsOf</c> keeps every <see cref="EntityReference"/> whose target is an ownership parent). A behavioural check pins that a
/// communication carrying the reference has no ownership parent.</item>
/// <item><b>Source.</b> Outside the two files that write it, no code (comments stripped) in the BFF folders that hold those registries and resolvers
/// names the table, the column or the <see cref="Sprk.Bff.Api.Services.Communication.Models.ServerSendOptions"/> constants.</item>
/// </list>
/// Each guard ships with must-fire and must-not-fire controls over synthetic data. Source and static tables only.
/// </summary>
/// <remarks>
/// <para><b>What the source scan skips, and why the risk is low.</b> It reads only the folders in <see cref="GuardedFolders"/> and skips the rest of the
/// BFF: <c>Services/Ai</c>, <c>Services/Signals</c>, <c>Services/Workspace</c>, <c>Services/Finance</c>, <c>Infrastructure/Dataverse</c>,
/// <c>Api/Filters</c> and the top-level <c>Api/*.cs</c> files. (<c>Services/Dataverse/RecordCreation</c> is inside the scanned <c>Services/Dataverse</c>.)
/// Inside the scanned folders, <c>Services/Communication/CommunicationService.cs</c> and <c>Models/ServerSendOptions.cs</c> are allowed to name the
/// link, because they write it. The risk of the skipped code is low because every scanner of a row's lookups that decides ownership, filing or
/// isolation filters through <c>RecordOwnershipResolver.OwnershipParentEntities</c> (<c>IsOwnershipParent</c>): a reference to a table that is not in it
/// is never read as a parent. The registry guard pins that the batch table is not in it, and the behavioural check pins that a communication carrying the
/// reference has no ownership parent. The source scan is the second line: it catches a registry or resolver that starts to name the link by name.</para>
/// <para>Each registry read has a minimum size (<see cref="MinimumRows"/>), so a registry that read as empty fails instead of passing vacuously.</para>
/// </remarks>
public class CommunicationBatchLinkNotFilingGuardTests
{
    private const string Communication = "sprk_communication";
    private const string Batch = "sprk_billingexport";

    /// <summary>One registry row: where it was read from, the child table, the lookup column (may be empty) and the table it names.</summary>
    internal sealed record Row(string Registry, string Table, string Column, string Target);

    // ------------------------------------------------------------------------------------------------------------------
    // 1. Registries
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Every row of every registry that could file, order or isolate a communication, read from the real tables.</summary>
    internal static IReadOnlyList<Row> RealRegistryRows()
    {
        var rows = new List<Row>();

        rows.AddRange(RecordFamilyEdges.All.Select(e => new Row("RecordFamilyEdges.All", e.ChildTable, e.LookupColumn, e.ParentTable)));

        rows.AddRange(SecureChildLineage.Children.Values.SelectMany(t =>
            t.Lookups.Select(l => new Row("SecureChildLineage.Children", t.LogicalName, l.Key, l.Value))));

        rows.AddRange(SecureRootInheritance.TypedFilingColumns.SelectMany(t =>
            t.Value.Select(c => new Row("SecureRootInheritance.TypedFilingColumns", t.Key, c.Column, c.ParentTable))));
        rows.AddRange(SecureRootInheritance.IntermediateFilingColumns.SelectMany(t =>
            t.Value.Select(c => new Row("SecureRootInheritance.IntermediateFilingColumns", t.Key, c.Column, c.Intermediate))));

        rows.AddRange(CoreAncestorResolver.IntermediateRootColumns.SelectMany(t =>
            t.Value.Select(c => new Row("CoreAncestorResolver.IntermediateRootColumns", t.Key, c.Column, c.RootEntity))));
        rows.AddRange(CoreAncestorResolver.StampSourceColumns.SelectMany(t =>
            t.Value.Select(c => new Row("CoreAncestorResolver.StampSourceColumns", t.Key, c.Column, c.Intermediate))));
        rows.AddRange(CoreAncestorResolver.CoreAncestorLookups.Select(l =>
            new Row("CoreAncestorResolver.CoreAncestorLookups", "(stamp)", l.LookupAttribute, l.EntityType)));

        rows.AddRange(RegardingFieldMap.All.Select(r => new Row("RegardingFieldMap.All", Communication, r.RegardingField, r.EntityLogicalName)));

        rows.AddRange(RecordOwnershipResolver.OwnershipParentEntities.Select(e => new Row("RecordOwnershipResolver.OwnershipParentEntities", "(parent)", "", e)));

        rows.AddRange(RegardingLookupMapRows());

        return rows;
    }

    /// <summary>
    /// <c>CommunicationService.RegardingLookupMap</c> is a private static dictionary (entity type to lookup field and entity set), the table the send path
    /// maps an association with. Read by reflection so a future entry cannot slip past the guard.
    /// </summary>
    private static IEnumerable<Row> RegardingLookupMapRows()
    {
        var field = typeof(CommunicationService).GetField("RegardingLookupMap", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null, "CommunicationService.RegardingLookupMap was renamed or removed: update CommunicationBatchLinkNotFilingGuardTests.");
        var map = (System.Collections.IDictionary)field!.GetValue(null)!;
        Assert.True(map.Count >= 10, $"RegardingLookupMap read only {map.Count} entries; the reflection is probably broken.");
        foreach (System.Collections.DictionaryEntry entry in map)
        {
            var lookupField = entry.Value!.GetType().GetField("Item1")?.GetValue(entry.Value) as string ?? "";
            yield return new Row("CommunicationService.RegardingLookupMap", Communication, lookupField, (string)entry.Key);
        }
    }

    /// <summary>
    /// The rows that put the batch link in a registry: any row naming <c>sprk_billingexport</c> as the column or the target of a
    /// <c>sprk_communication</c> lookup, and any row that makes the batch table an ownership parent or a stamp / regarding target (those would let the
    /// owner resolver or the regarding code read the reference as a parent).
    /// </summary>
    internal static IReadOnlyList<Row> Offenders(IEnumerable<Row> rows) =>
        rows.Where(r =>
                (string.Equals(r.Table, Communication, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(r.Column, Batch, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.Target, Batch, StringComparison.OrdinalIgnoreCase)))
                || (r.Registry is "RecordOwnershipResolver.OwnershipParentEntities" or "CoreAncestorResolver.CoreAncestorLookups"
                        or "RegardingFieldMap.All" or "CommunicationService.RegardingLookupMap"
                    && string.Equals(r.Target, Batch, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>Minimum rows each registry must yield (about half of what each held at task 151c-2), so an empty read cannot pass the guard.</summary>
    internal static readonly IReadOnlyDictionary<string, int> MinimumRows = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["RecordFamilyEdges.All"] = 80,
        ["SecureChildLineage.Children"] = 75,
        ["SecureRootInheritance.TypedFilingColumns"] = 2,
        ["SecureRootInheritance.IntermediateFilingColumns"] = 2,
        ["CoreAncestorResolver.IntermediateRootColumns"] = 19,
        ["CoreAncestorResolver.StampSourceColumns"] = 22,
        ["CoreAncestorResolver.CoreAncestorLookups"] = 4,
        ["RegardingFieldMap.All"] = 6,
        ["RecordOwnershipResolver.OwnershipParentEntities"] = 10,
        ["CommunicationService.RegardingLookupMap"] = 6,
    };

    /// <summary>The registries whose row count is below its minimum (empty read, renamed member).</summary>
    internal static IReadOnlyList<string> UndersizedRegistries(IEnumerable<Row> rows)
    {
        var counts = rows.GroupBy(r => r.Registry).ToDictionary(g => g.Key, g => g.Count());
        return MinimumRows
            .Where(m => !counts.TryGetValue(m.Key, out var n) || n < m.Value)
            .Select(m => $"{m.Key}: {(counts.TryGetValue(m.Key, out var have) ? have : 0)} rows, minimum {m.Value}")
            .ToList();
    }

    [Fact(DisplayName = "151c-2 guard: every registry the guard reads yielded at least its minimum number of rows (an empty read fails)")]
    public void EveryRegistry_WasReadAtItsMinimumSize()
    {
        var undersized = UndersizedRegistries(RealRegistryRows());

        Assert.True(undersized.Count == 0,
            "A registry read as (nearly) empty, so the batch-link guard would pass without looking. Registries:\n  " + string.Join("\n  ", undersized));
    }

    [Fact(DisplayName = "151c-2 guard control (must fire): a registry with too few rows is reported, each alone")]
    public void SizeGuard_FiresOnAnEmptyRegistry()
    {
        var real = RealRegistryRows();
        Assert.Empty(UndersizedRegistries(real));

        foreach (var registry in MinimumRows.Keys)
        {
            var without = real.Where(r => r.Registry != registry).ToList();
            var found = Assert.Single(UndersizedRegistries(without));
            Assert.StartsWith(registry + ":", found, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "151c-2 guard: sprk_communication.sprk_billingexport is in no family-edge, lineage, ownership or regarding registry")]
    public void TheBatchLink_IsInNoRegistry()
    {
        var rows = RealRegistryRows();
        // Sanity floor: the registries really were read (a broken read would pass the guard vacuously).
        Assert.Contains(rows, r => r.Registry == "RecordFamilyEdges.All" && r.Table == Communication);
        Assert.Contains(rows, r => r.Registry == "SecureChildLineage.Children" && r.Table == Communication && r.Column == "sprk_regardingmatter");
        Assert.Contains(rows, r => r.Registry == "RegardingFieldMap.All" && r.Column == "sprk_regardingmatter");
        Assert.Contains(rows, r => r.Registry == "CommunicationService.RegardingLookupMap" && r.Column == "sprk_regardingmatter");
        Assert.Contains(rows, r => r.Registry == "RecordOwnershipResolver.OwnershipParentEntities" && r.Target == "sprk_matter");

        var offenders = Offenders(rows);

        Assert.True(offenders.Count == 0,
            "sprk_communication.sprk_billingexport is a plain reference to the AP export batch (task 151c-2), not a filing: it must not enter a family-edge, "
            + "lineage, ownership-parent or regarding registry, or a change of the batch's owner or team could change who owns or can read the email. "
            + "Offending rows:\n  " + string.Join("\n  ", offenders.Select(o => $"{o.Registry}: {o.Table}.{o.Column} -> {o.Target}")));
    }

    [Fact(DisplayName = "151c-2 guard control (must fire): a synthetic row in any registry shape is reported")]
    public void RegistryGuard_FiresOnEachShape()
    {
        var real = RealRegistryRows();
        Assert.Empty(Offenders(real));

        var seeded = new[]
        {
            new Row("RecordFamilyEdges.All", Communication, Batch, Batch),
            new Row("SecureChildLineage.Children", Communication, Batch, Batch),
            new Row("RegardingFieldMap.All", Communication, "sprk_regardingbillingexport", Batch),
            new Row("CommunicationService.RegardingLookupMap", Communication, "sprk_regardingbillingexport", Batch),
            new Row("RecordOwnershipResolver.OwnershipParentEntities", "(parent)", "", Batch),
            new Row("CoreAncestorResolver.CoreAncestorLookups", "(stamp)", "sprk_regardingbillingexport", Batch),
            new Row("SecureChildLineage.Children", Communication.ToUpperInvariant(), Batch.ToUpperInvariant(), "x"),
        };

        foreach (var row in seeded)
        {
            var found = Offenders(real.Append(row));
            Assert.Equal(row, Assert.Single(found));
        }
    }

    [Fact(DisplayName = "151c-2 guard control (must not fire): other tables' batch lookups, and other communication lookups, are not reported")]
    public void RegistryGuard_DoesNotFireOnOtherRows()
    {
        var neighbours = new[]
        {
            // The invoice's own batch lookup is a different table's business (not in any registry today; the guard is about the communication).
            new Row("SecureChildLineage.Children", "sprk_invoice", Batch, Batch),
            new Row("SecureChildLineage.Children", "sprk_storedfile", Batch, Batch),
            new Row("SecureChildLineage.Children", Communication, "sprk_regardinginvoice", "sprk_invoice"),
            new Row("SecureChildLineage.Children", Communication, "sprk_communicationthread", "sprk_communicationthread"),
            new Row("RecordOwnershipResolver.OwnershipParentEntities", "(parent)", "", "sprk_billingevent"),
        };

        Assert.Empty(Offenders(neighbours));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 1b. Behaviour: the owner resolver does not read the reference as a parent
    // ------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "151c-2 guard: a communication carrying the batch reference has no ownership parent (the resolver reads EntityReferences)")]
    public void OwnerResolver_DoesNotReadTheBatchLinkAsAParent()
    {
        var row = new Entity(Communication) { Id = Guid.NewGuid() };
        row[Batch] = new EntityReference(Batch, Guid.NewGuid());

        Assert.Empty(RecordOwnershipContext.ParentsOf(row));
        Assert.Empty(RecordOwnershipContext.OrderedParentsOf(row));
        Assert.Empty(RecordOwnershipContext.SecureOnlyParentsOf(row));
        Assert.False(RecordOwnershipResolver.IsOwnershipParent(Batch));
    }

    [Fact(DisplayName = "151c-2 guard control (must fire): the same read DOES keep a real filing lookup, so the check above can fail")]
    public void OwnerResolver_ControlKeepsARealParent()
    {
        var matter = Guid.NewGuid();
        var row = new Entity(Communication) { Id = Guid.NewGuid() };
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter);
        row[Batch] = new EntityReference(Batch, Guid.NewGuid());

        var parent = Assert.Single(RecordOwnershipContext.ParentsOf(row));
        Assert.Equal("sprk_matter", parent.EntityLogicalName);
        Assert.Equal(matter, parent.RecordId);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 2. Source scan
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The BFF folders that hold the registries and resolvers above, relative to <c>src/server/api/Sprk.Bff.Api</c>.</summary>
    private static readonly string[] GuardedFolders =
    [
        "Services/Dataverse",
        "Services/Access",
        "Services/Communication",
        "Services/ExternalAccess",
        "Infrastructure/ExternalAccess",
        "Api/Records",
        "Api/ExternalAccess",
        "Api/FieldMappings",
    ];

    /// <summary>The identifiers and literals that mean "the batch link": the table, and the constants that name the column and its table.</summary>
    private static readonly string[] Tokens =
        ["\"sprk_billingexport\"", "BillingExportColumn", "BillingExportEntity", "BillingExportId", "ApExportService.BatchEntity"];

    /// <summary>The files that may use the tokens: the option that carries the reference, and the service that writes it.</summary>
    private static readonly string[] AllowedFiles =
    [
        "Services/Communication/Models/ServerSendOptions.cs",
        "Services/Communication/CommunicationService.cs",
    ];

    /// <summary>Files (path relative to the BFF project, forward slashes) whose code, comments stripped, names the batch link.</summary>
    internal static IReadOnlyList<string> FilesNamingTheBatchLink(IEnumerable<(string RelativePath, string Text)> files) =>
        files
            .Where(f => !AllowedFiles.Contains(f.RelativePath, StringComparer.OrdinalIgnoreCase))
            .Where(f => f.Text.Replace("\r\n", "\n").Split('\n').Select(SourceScan.StripLineComment)
                .Any(line => Tokens.Any(t => line.Contains(t, StringComparison.Ordinal))))
            .Select(f => f.RelativePath)
            .ToList();

    private static IEnumerable<(string RelativePath, string Text)> RealGuardedFiles()
    {
        var root = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        foreach (var folder in GuardedFolders)
        {
            var dir = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    continue;
                yield return (Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText(file));
            }
        }
    }

    [Fact(DisplayName = "151c-2 guard: no registry, resolver or filing code names the batch link (only ServerSendOptions and CommunicationService do)")]
    public void NoGuardedCodeNamesTheBatchLink()
    {
        var files = RealGuardedFiles().ToList();
        // Sanity floor: the folders really were scanned.
        Assert.True(files.Count >= 100, $"Only {files.Count} source files scanned; the folder list or the repository root is probably wrong.");
        Assert.Contains(files, f => f.RelativePath == "Services/Dataverse/RecordOwnershipResolver.cs");
        Assert.Contains(files, f => f.RelativePath == "Services/Communication/CommunicationService.cs");

        var offenders = FilesNamingTheBatchLink(files);

        Assert.True(offenders.Count == 0,
            "sprk_communication.sprk_billingexport is a plain reference (task 151c-2): only Services/Communication/Models/ServerSendOptions.cs and "
            + "CommunicationService.cs may name it. A registry, resolver or filing file that does can let the batch link decide who owns or can read an "
            + "email. Files:\n  " + string.Join("\n  ", offenders));
    }

    [Fact(DisplayName = "151c-2 source guard control (must fire): code in a guarded file naming the table, column or constants is reported, each token alone")]
    public void SourceGuard_FiresOnEachToken()
    {
        foreach (var line in new[]
        {
            "[\"sprk_communication\"] = (\"sprk_billingexport\", \"sprk_billingexports\"),",
            "var c = ServerSendOptions.BillingExportColumn;",
            "new EntityReference(ServerSendOptions.BillingExportEntity, id);",
            "if (options.BillingExportId is not null) { }",
            "var t = ApExportService.BatchEntity;",
        })
        {
            var found = FilesNamingTheBatchLink([("Services/Dataverse/RecordOwnershipResolver.cs", "class X {\r\n    " + line + "\r\n}")]);
            Assert.Equal("Services/Dataverse/RecordOwnershipResolver.cs", Assert.Single(found));
        }
    }

    [Fact(DisplayName = "151c-2 source guard control (must not fire): a comment, the two allowed files, and an unrelated name are not reported")]
    public void SourceGuard_DoesNotFireOnCommentsAllowedFilesOrNeighbours()
    {
        const string comment = "class X {\n    // sprk_billingexport (an export batch is not a codified table)\n    /// <c>\"sprk_billingexport\"</c> BillingExportId\n}";
        const string code = "class X { var x = ServerSendOptions.BillingExportColumn; }";

        Assert.Empty(FilesNamingTheBatchLink(
        [
            ("Services/Access/SecureChildLineage.cs", comment),
            ("Services/Communication/Models/ServerSendOptions.cs", code),
            ("Services/Communication/CommunicationService.cs", code),
            ("Services/Dataverse/CoreAncestorResolver.cs", "class X { var y = \"sprk_billingevent\"; var z = BillingExports; }"),
        ]));
    }
}
