using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 094 (procedure-and-file-model 9.1; owner K, EB-D-158): a Submit Invoice COPIES its source document into a
/// Stored File. It never links the source to the invoice (<c>sprk_document.sprk_invoice</c>), re-owns it or mirrors its shares;
/// 050a's <c>InvoiceDocumentRefile</c>, which did, is retired. This guard scans the Submit Invoice code for those writes.
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> The Submit Invoice services (<c>Services/Finance/Submission</c>), its route and its job handler. Step A (the
/// unfiled communication filed AS THE CALLER) goes through the communication re-file core in <c>CommunicationEndpoints</c>, which
/// is held by <see cref="RefileWriterAfterRefileGuardTests"/>, not here.</para>
/// <para><b>Why text, not IL.</b> The link is a column name (a string) on an <c>Entity</c>; IL sees an indexer call, not which
/// column. The patterns are the write shapes: an assignment to the persister's <c>DocumentInvoice</c> column, an owner
/// assignment, an <c>AssignRequest</c>, a share request, or a re-file or share-mirror seam. A read of the link (the 050a
/// refusal of a document linked to another invoice) is not a write and does not match.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/>.</para>
/// </remarks>
public class SubmitInvoiceSourceUntouchedGuardTests
{
    private static readonly Regex[] Forbidden =
    [
        new(@"\[\s*(InvoiceSubmissionPersister\.)?DocumentInvoice\s*\]\s*=(?!=)", RegexOptions.Compiled),
        new(@"\[\s*""sprk_invoice""\s*\]\s*=(?!=)\s*new\s+EntityReference\(\s*""sprk_invoice""", RegexOptions.Compiled),
        new(@"\[\s*""ownerid""\s*\]\s*=(?!=)", RegexOptions.Compiled),
        new(@"\bnew\s+(AssignRequest|GrantAccessRequest|ModifyAccessRequest|RevokeAccessRequest)\b", RegexOptions.Compiled),
        new(@"\.(ReparentAsync|AfterRefileAsync|ReconcileAsync|ReconcileOrdinaryParentMoveAsync|ReownOrdinaryRowAsync)\s*\(", RegexOptions.Compiled),
    ];

    private static IEnumerable<string> ScannedFiles()
    {
        var api = Path.Combine(SourceRoot(), "src", "server", "api", "Sprk.Bff.Api");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(api, "Services", "Finance", "Submission"), "*.cs", SearchOption.AllDirectories))
        {
            yield return file;
        }

        yield return Path.Combine(api, "Api", "Finance", "InvoiceSubmissionEndpoints.cs");
        yield return Path.Combine(api, "Services", "Jobs", "Handlers", "InvoiceSubmissionProcessingJobHandler.cs");
    }

    internal static IReadOnlyList<string> Violations(string source) =>
        source.Split('\n')
            .Select((line, i) => (Line: line, Number: i + 1))
            .Where(l => !l.Line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(l => Forbidden.Any(r => r.IsMatch(l.Line)))
            .Select(l => $"{l.Number}: {l.Line.Trim()}")
            .ToList();

    [Fact(DisplayName = "094: no Submit Invoice code writes sprk_document.sprk_invoice, re-owns a record or mirrors shares")]
    public void SubmitInvoice_NeverLinksReownsOrSharesTheSource()
    {
        var files = ScannedFiles().ToList();
        Assert.True(files.Count > 10, "the scan must reach the Submission folder");

        var found = files
            .SelectMany(f => Violations(File.ReadAllText(f)).Select(v => $"{Path.GetFileName(f)}:{v}"))
            .ToList();

        // A Submit Invoice copies its source and never links, re-owns or shares it (task 094; owner K).
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    [Fact(DisplayName = "094 guard controls: each write shape is found; a read of the link, a comment and a non-owner column are not")]
    public void Detector_MustFireAndMustNotFire()
    {
        Assert.Single(Violations("document[DocumentInvoice] = new EntityReference(\"sprk_invoice\", id);"));
        Assert.Single(Violations("document[InvoiceSubmissionPersister.DocumentInvoice] = invoiceRef;"));
        Assert.Single(Violations("row[\"sprk_invoice\"] = new EntityReference(\"sprk_invoice\", id);"));
        Assert.Single(Violations("row[\"ownerid\"] = new EntityReference(\"team\", team);"));
        Assert.Single(Violations("await svc.ExecuteAsync(new AssignRequest { Target = t });"));
        Assert.Single(Violations("var r = new GrantAccessRequest();"));
        Assert.Single(Violations("await _owners.ReparentAsync(context, ct);"));
        Assert.Single(Violations("await _reconciler.AfterRefileAsync(table, id, ct);"));

        Assert.Empty(Violations("if (document.GetAttributeValue<EntityReference>(DocumentInvoice) is { Id: var linked })"));
        Assert.Empty(Violations("internal const string DocumentInvoice = \"sprk_invoice\";"));
        Assert.Empty(Violations("// document[DocumentInvoice] = x; was 050a's re-file"));
        Assert.Empty(Violations("if (row[DocumentInvoice] == null) return;"));
        // The submission's own invoice lookup is not the source document's link.
        Assert.Empty(Violations("submission[\"sprk_invoice\"] = new EntityReference(InvoiceEntity, id);"));
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) && Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
