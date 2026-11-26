using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b fix round 1 (F1): <see cref="RootInvoiceLinkRedaction"/>, the one set of column forms that can carry a root's
/// invoice link. The guard holds it to the solution source: every regarding column a root with an invoice link declares is in the set.
/// </summary>
/// <remarks>Maintain-class (ADR-038): a regarding column added to a root and missed here would show the invoice link the root redacts.</remarks>
[Trait("category", "external-access")]
public sealed class RootInvoiceLinkRedactionTests
{
    /// <summary>The prefix of the regarding pair and its resolver columns (ADR-024).</summary>
    private const string RegardingPrefix = "sprk_regardingrecord";

    // ── The guard ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every regarding column of <paramref name="declared"/> (a table's columns) that <paramref name="set"/> does not hold.</summary>
    private static IReadOnlyList<string> Missing(string table, IEnumerable<string> declared, IReadOnlySet<string> set) =>
        declared.Where(c => c.StartsWith(RegardingPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(c => RootInvoiceLinkRedaction.FormsOf(c).Any(form => !set.Contains(form)))
            .Select(c => $"NOT REDACTED: {table}.{c}")
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The logical names of the columns <paramref name="table"/> declares in the solution source.</summary>
    private static IReadOnlyList<string> DeclaredColumns(string table)
    {
        var entities = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        var folder = Directory.GetDirectories(entities)
            .SingleOrDefault(d => string.Equals(Path.GetFileName(d), table, StringComparison.OrdinalIgnoreCase));
        folder.Should().NotBeNull($"the solution source declares {table}");
        return XDocument.Load(Path.Combine(folder!, "Entity.xml")).Descendants("attribute")
            .Select(a => a.Element("LogicalName")?.Value)
            .OfType<string>()
            .ToList();
    }

    public static TheoryData<string> Roots
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var table in InvoiceLinks.Tables.Keys.Where(InvoiceLinks.IsRootWithInvoiceLink).Order(StringComparer.Ordinal))
                data.Add(table);
            return data;
        }
    }

    [Theory(DisplayName = "130-0b F1 guard: every regarding column a root with an invoice link declares is in its redaction set, in every form")]
    [MemberData(nameof(Roots))]
    public void EveryDeclaredRegardingColumn_IsInTheRedactionSet(string table)
    {
        var declared = DeclaredColumns(table);
        declared.Should().Contain(c => c.StartsWith(RegardingPrefix, StringComparison.OrdinalIgnoreCase), "the guard must see the regarding columns");

        Missing(table, declared, RootInvoiceLinkRedaction.ColumnsOf(table)).Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0b F1 guard must fire: a seeded regarding column is reported, and the old hand list misses the number and the record type")]
    public void Guard_MustFire_OnARegardingColumnOutsideTheSet()
    {
        var set = RootInvoiceLinkRedaction.ColumnsOf("sprk_workassignment");
        var seeded = DeclaredColumns("sprk_workassignment").Append("sprk_regardingrecordcode");

        Missing("sprk_workassignment", seeded, set).Should().Equal("NOT REDACTED: sprk_workassignment.sprk_regardingrecordcode");

        var oldHandList = new[] { "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl" }
            .SelectMany(RootInvoiceLinkRedaction.FormsOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Missing("sprk_workassignment", DeclaredColumns("sprk_workassignment"), oldHandList).Should().Equal(
            "NOT REDACTED: sprk_workassignment.sprk_regardingrecordnumber",
            "NOT REDACTED: sprk_workassignment.sprk_regardingrecordtype");
    }

    [Fact(DisplayName = "130-0b F1 guard must not fire: a column outside the regarding family is not required, and a table that is not a root with an invoice link has an empty set")]
    public void Guard_MustNotFire_OnOtherColumnsAndTables()
    {
        Missing("sprk_workassignment", new[] { "sprk_name", "sprk_description", "sprk_regardingmatter" }, RootInvoiceLinkRedaction.ColumnsOf("sprk_workassignment"))
            .Should().BeEmpty();
        RootInvoiceLinkRedaction.ColumnsOf("sprk_matter").Should().BeEmpty("a root with no invoice link redacts nothing");
        RootInvoiceLinkRedaction.ColumnsOf("sprk_todo").Should().BeEmpty("a child is hidden, not redacted");
        RootInvoiceLinkRedaction.ColumnsOf(null).Should().BeEmpty();
    }

    // ── The set ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "130-0b F1: the work assignment's set holds every form of the invoice link and of the regarding pair and resolver columns, and nothing else")]
    public void TheWorkAssignmentSet_HoldsTheLinkAndTheRegardingFamily()
    {
        var set = RootInvoiceLinkRedaction.ColumnsOf("sprk_workassignment");

        set.Should().Contain(new[]
        {
            "sprk_regardinginvoice", "_sprk_regardinginvoice_value", "sprk_regardinginvoicename", "sprk_regardinginvoiceyominame",
            "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
            "sprk_regardingrecordtype", "_sprk_regardingrecordtype_value", "sprk_regardingrecordtypename",
        });
        foreach (var column in CreateTimeFieldMapping.RegardingPairAndResolverColumns)
            set.Should().Contain(column, "the regarding columns come from the write path's list");
        set.Should().NotContain(new[] { "sprk_name", "sprk_description", "sprk_regardingmatter", "_sprk_regardingmatter_value" });
        set.Should().Contain("SPRK_REGARDINGRECORDNUMBER", "column names are matched without case");
    }

    [Theory(DisplayName = "130-0b F1: a key is matched by its column, so every annotation on a redacted column is redacted")]
    [InlineData("sprk_regardingrecordnumber", true)]
    [InlineData("_sprk_regardinginvoice_value@OData.Community.Display.V1.FormattedValue", true)]
    [InlineData("_sprk_regardingrecordtype_value@Microsoft.Dynamics.CRM.lookuplogicalname", true)]
    [InlineData("sprk_name", false)]
    [InlineData("sprk_name@OData.Community.Display.V1.FormattedValue", false)]
    public void IsRedacted_MatchesAnnotationsByTheirColumn(string key, bool redacted)
    {
        RootInvoiceLinkRedaction.IsRedacted("sprk_workassignment", key).Should().Be(redacted);
        RootInvoiceLinkRedaction.IsRedacted("sprk_todo", key).Should().BeFalse();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
