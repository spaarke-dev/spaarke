using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 171, owner round 72 item 1 (adversarial finding 4): every server file that WRITES a
/// document's SharePoint Embedded item id (<c>sprk_graphitemid</c>) also writes its field-secured copy
/// (<c>sprk_graphitemidbound</c>) through <c>Spaarke.Dataverse.DocumentPointerBinding</c>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The pointer check refuses a row whose item id differs from the copy, and — once
/// <c>DocumentPointer:ItemIdBoundBackfillComplete</c> is set — a row with no copy. A new writer that sets the item id and
/// forgets the copy therefore breaks every document it creates (they become unservable), which no unit test of that
/// writer would notice. This guard fails the build instead.</para>
/// <para><b>What counts as a write.</b> An indexer assignment of the item-id column — <c>["sprk_graphitemid"] = …</c> or
/// through one of the column's named constants (<c>GraphItemIdAttribute</c>, <c>ItemColumn</c>), on an entity, a
/// dictionary or <c>KeyAttributes</c>. Lookups (<c>{ GraphItemIdAttribute, id }</c> in a key collection, query
/// conditions, column sets) are not writes. A file that writes the column for ANOTHER table (a communication attachment)
/// passes if it also binds a document write — the check is per file, deliberately coarse, and the reviewer judges the
/// rest. MAINTAIN-class (tests/CLAUDE.md "Structural fitness functions").</para>
/// </remarks>
public class DocumentPointerBindingGuardTests
{
    private static readonly Regex WritesItemId = new(
        @"\[\s*(?:""sprk_graphitemid""|(?:\w+\.)?GraphItemIdAttribute|ItemColumn)\s*\]\s*=(?!=)",
        RegexOptions.Compiled);

    private const string BindingMarker = "DocumentPointerBinding";

    [Fact(DisplayName = "Task 171 (round 72 F4): every server file that writes sprk_graphitemid also writes the field-secured copy")]
    public void EveryItemIdWriterBindsTheCopy()
    {
        var writers = new List<string>();
        var violations = new List<string>();

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');
            if (relative.EndsWith("DocumentPointerBinding.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var sites = Scan(relative, text);
            if (sites.Count == 0)
            {
                continue;
            }

            writers.Add(relative);
            if (!text.Contains(BindingMarker, StringComparison.Ordinal))
            {
                violations.AddRange(sites);
            }
        }

        Assert.True(violations.Count == 0,
            "A file writes sprk_graphitemid without writing sprk_graphitemidbound. sprk_graphitemid cannot be field-secured "
            + "(it is in the alternate key sprk_graphitemid_uk), so the pointer check compares it with the BFF-only copy; a "
            + "writer that sets one without the other makes the document unservable. REMEDY: on the SAME write, set "
            + "[DocumentPointerBinding.BoundItemIdColumn] = <the same item id> (or call DocumentPointerBinding.Bind / "
            + "BindFields).\n\nOffending sites:\n  " + string.Join("\n  ", violations));

        // Non-vacuity: the known writers (Dataverse typed update, external upload, three communication archive/attachment
        // writers, Compose create-on-save, the relocator) are found. A count below that means the pattern drifted.
        Assert.True(writers.Count >= 7,
            $"Only {writers.Count} item-id writer file(s) found ({string.Join(", ", writers)}); there were 7 at round 72. "
            + "Check the pattern before trusting a pass.");
    }

    [Fact(DisplayName = "Negative control: the detector sees indexer writes of the item id and ignores lookups")]
    public void Detector_NegativeControl()
    {
        Assert.NotEmpty(Scan("A.cs", """entity["sprk_graphitemid"] = itemId;"""));
        Assert.NotEmpty(Scan("B.cs", """    [ItemColumn] = item,"""));
        Assert.NotEmpty(Scan("C.cs", """entity.KeyAttributes[ComposeService.GraphItemIdAttribute] = id;"""));
        Assert.Empty(Scan("D.cs", """new KeyAttributeCollection { { GraphItemIdAttribute, graphItemId } }"""));
        Assert.Empty(Scan("E.cs", """if (entity["sprk_graphitemid"] == null) { }"""));
        Assert.Empty(Scan("F.cs", """// ["sprk_graphitemid"] = x  (a comment)"""));
    }

    private static List<string> Scan(string relativeFile, string text)
    {
        var sites = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            if (WritesItemId.IsMatch(code))
            {
                sites.Add($"{relativeFile}:{i + 1}: {code.Trim()}");
            }
        }

        return sites;
    }
}
