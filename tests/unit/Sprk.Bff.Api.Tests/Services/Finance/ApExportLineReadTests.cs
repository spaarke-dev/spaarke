using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// 164a re-check K3: the AP export's line read keeps the invoice column the added-line rule needs. <c>ExtractionProvenance.IsReviewerAdded</c> needs
/// <c>sprk_invoice</c> on the row; a reader that does not select it treats an added line as an ordinary billed line. The service's line read is private and
/// needs a full Dataverse world, so this pins the source text (the unit tests cannot observe the query).
/// </summary>
public sealed class ApExportLineReadTests
{
    [Fact(DisplayName = "164a K3: the AP export line query selects sprk_invoice, the column the reviewer-added rule reads")]
    public void ApExportLineQuery_SelectsTheInvoiceColumn()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Finance", "ApExportService.cs"));
        var method = source.IndexOf("ReadLinesAsync(Guid invoiceId", StringComparison.Ordinal);
        method.Should().BeGreaterThan(0, "the line read is still named ReadLinesAsync; update this test with it");
        var reviewerAdded = source.IndexOf("IsReviewerAdded(", method, StringComparison.Ordinal);
        reviewerAdded.Should().BeGreaterThan(method, "the line read still applies the reviewer-added rule");

        var columnSet = source.IndexOf("new ColumnSet(", method, StringComparison.Ordinal);
        columnSet.Should().BeGreaterThan(method).And.BeLessThan(reviewerAdded, "the query is built before the rows are read");
        var end = source.IndexOf("Criteria =", columnSet, StringComparison.Ordinal);
        var columns = source[columnSet..end];

        columns.Should().Match(c => c.Contains("LineInvoiceLookup") || c.Contains("\"sprk_invoice\""),
            "IsReviewerAdded needs the row's invoice; without it an added line is read as an ordinary billed line");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
