using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095a (owner choice B, 2026-11-03; uac-r2 binding) - <c>sprk_invoicesubmission.sprk_sourceemaildocument</c>, the lookup to the
/// .eml an invoice-submission attempt was read from, is a CITATION. It is not a filing, not an ownership parent, not a regarding column. The column
/// points at a <c>sprk_document</c>, and a lookup to <c>sprk_document</c> is, by default, read as a filing parent (<c>RecordOwnershipContext.ParentsOf</c>
/// keeps every <see cref="EntityReference"/> whose target is an ownership parent). If a family-edge, lineage, ownership or regarding registry learned
/// of the column, or the resolver read it as a parent, then the readers of the .eml (the sender's mailbox team) could gain or lose access to the
/// invoice, or the other way round. Three guards hold it out (the pattern of <see cref="CommunicationBatchLinkNotFilingGuardTests"/>):
/// <list type="number">
/// <item><b>Registries.</b> No row of <c>RecordFamilyEdges</c>, <c>SecureChildLineage</c>, <c>SecureRootInheritance</c>, <c>CoreAncestorResolver</c>,
/// <c>RegardingFieldMap</c>, the communication service's regarding map or <c>RecordOwnershipResolver.OwnershipParentEntities</c> names the column. The
/// registries are read by the 151c-2 guard's own reader, so the two guards cannot drift apart.</item>
/// <item><b>Behaviour.</b> <c>RecordOwnershipResolver.IsCitation("sprk_invoicesubmission", "sprk_sourceemaildocument")</c> is true, and a submission
/// carrying the reference has no ownership parent (control: its <c>sprk_document</c> lookup is a parent).</item>
/// <item><b>Source.</b> No file of the BFF outside the four that write, read or register the column (comments stripped) names it - in particular
/// <c>ExternalInvoiceVisibility</c>, the external endpoints, the filters and every filing or lineage file.</item>
/// </list>
/// Each guard ships with must-fire and must-not-fire controls over synthetic data. Source and static tables only.
/// </summary>
public class SubmissionSourceEmailLinkNotFilingGuardTests
{
    private const string Submission = "sprk_invoicesubmission";
    private const string Column = "sprk_sourceemaildocument";
    private const string Document = "sprk_document";

    // ------------------------------------------------------------------------------------------------------------------
    // 1. Registries
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The rows that put the column in a registry: any row whose lookup column is <c>sprk_sourceemaildocument</c>, on any table (the column has one
    /// legitimate home, and none of the registries lists it).
    /// </summary>
    internal static IReadOnlyList<CommunicationBatchLinkNotFilingGuardTests.Row> Offenders(IEnumerable<CommunicationBatchLinkNotFilingGuardTests.Row> rows) =>
        rows.Where(r => string.Equals(r.Column, Column, StringComparison.OrdinalIgnoreCase)).ToList();

    [Fact(DisplayName = "095a guard: sprk_invoicesubmission.sprk_sourceemaildocument is in no family-edge, lineage, ownership, filing or regarding registry")]
    public void TheSourceEmailLink_IsInNoRegistry()
    {
        var rows = CommunicationBatchLinkNotFilingGuardTests.RealRegistryRows();

        // Sanity floor: the registries really were read (and the submission's own filing lookups are in them, so a read that lost the table would fail).
        Assert.Empty(CommunicationBatchLinkNotFilingGuardTests.UndersizedRegistries(rows));
        Assert.Contains(rows, r => r.Registry == "RecordFamilyEdges.All" && r.Table == Submission);
        Assert.Contains(rows, r => r.Registry == "RecordOwnershipResolver.OwnershipParentEntities" && r.Target == Document);

        var offenders = Offenders(rows);

        Assert.True(offenders.Count == 0,
            "sprk_invoicesubmission.sprk_sourceemaildocument is a citation of the source .eml (task 095a), not a filing: it must not enter a family-edge, "
            + "lineage, filing-column, ownership-parent or regarding registry, or the .eml's readers could gain or lose access to the invoice. "
            + "Offending rows:\n  " + string.Join("\n  ", offenders.Select(o => $"{o.Registry}: {o.Table}.{o.Column} -> {o.Target}")));
    }

    [Fact(DisplayName = "095a guard control (must fire): a synthetic row naming the column in any registry shape is reported")]
    public void RegistryGuard_FiresOnEachShape()
    {
        var real = CommunicationBatchLinkNotFilingGuardTests.RealRegistryRows();
        Assert.Empty(Offenders(real));

        var seeded = new[]
        {
            new CommunicationBatchLinkNotFilingGuardTests.Row("RecordFamilyEdges.All", Submission, Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("SecureChildLineage.Children", Submission, Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("SecureRootInheritance.TypedFilingColumns", Submission, Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("SecureRootInheritance.IntermediateFilingColumns", Submission, Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("CoreAncestorResolver.IntermediateRootColumns", Submission, Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("CoreAncestorResolver.StampSourceColumns", Submission, Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("CoreAncestorResolver.CoreAncestorLookups", "(stamp)", Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("RegardingFieldMap.All", Submission, Column, Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("SecureChildLineage.Children", Submission.ToUpperInvariant(), Column.ToUpperInvariant(), "x"),
        };

        foreach (var row in seeded)
        {
            var found = Offenders(real.Append(row));
            Assert.Equal(row, Assert.Single(found));
        }
    }

    [Fact(DisplayName = "095a guard control (must not fire): the submission's own filing lookups and neighbouring columns are not reported")]
    public void RegistryGuard_DoesNotFireOnOtherRows()
    {
        var neighbours = new[]
        {
            new CommunicationBatchLinkNotFilingGuardTests.Row("RecordFamilyEdges.All", Submission, "sprk_document", Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("RecordFamilyEdges.All", Submission, "sprk_regardingmatter", "sprk_matter"),
            new CommunicationBatchLinkNotFilingGuardTests.Row("SecureChildLineage.Children", Submission, "sprk_resubmissionof", Submission),
            new CommunicationBatchLinkNotFilingGuardTests.Row("SecureChildLineage.Children", "sprk_storedfile", "sprk_sourcedocument", Document),
            new CommunicationBatchLinkNotFilingGuardTests.Row("RecordOwnershipResolver.OwnershipParentEntities", "(parent)", "", Document),
        };

        Assert.Empty(Offenders(neighbours));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 2. Behaviour: the owner resolver does not read the reference as a parent
    // ------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "095a guard: the column is a registered citation, and a submission carrying it has no ownership parent")]
    public void OwnerResolver_DoesNotReadTheSourceEmailLinkAsAParent()
    {
        Assert.True(RecordOwnershipResolver.IsCitation(Submission, Column),
            "sprk_invoicesubmission.sprk_sourceemaildocument must stay in RecordOwnershipResolver.CitationLookups: it points at a sprk_document, an ownership parent.");
        Assert.True(RecordOwnershipResolver.IsOwnershipParent(Document), "premise: a lookup to sprk_document would be read as a filing parent");

        var row = new Entity(Submission) { Id = Guid.NewGuid() };
        row[Column] = new EntityReference(Document, Guid.NewGuid());

        Assert.Empty(RecordOwnershipContext.ParentsOf(row));
        Assert.Empty(RecordOwnershipContext.OrderedParentsOf(row));
        Assert.Empty(RecordOwnershipContext.SecureOnlyParentsOf(row));
    }

    // Review known limit (a): the re-file (ResolveReparentOwnerAsync) reads the WHOLE row and derives its parents by column through
    // OwnershipParentColumnsOf, which did not apply the citation rule every other derivation applies; it would have filed the submission under the .eml.
    [Fact(DisplayName = "095a guard: the re-file's column read (OwnershipParentColumnsOf, the whole row) leaves the citation out, and keeps the submission's own sprk_document lookup (control)")]
    public void ReparentColumnRead_LeavesTheSourceEmailLinkOut_AndKeepsARealParent()
    {
        var citedEml = Guid.NewGuid();
        var filedDocument = Guid.NewGuid();
        var row = new Entity(Submission) { Id = Guid.NewGuid() };
        row[Column] = new EntityReference(Document, citedEml);
        row["sprk_document"] = new EntityReference(Document, filedDocument);
        row["sprk_someotherlink"] = new EntityReference(Document, Guid.NewGuid());

        var byColumn = RecordOwnershipResolver.OwnershipParentColumnsOf(row);

        Assert.DoesNotContain(Column, byColumn.Keys);
        Assert.DoesNotContain(byColumn.Values, p => p.RecordId == citedEml);
        // Controls (must not fire): a real filing lookup and an unregistered lookup of the same shape ARE read, so the exclusion above is the registration's.
        Assert.Equal(filedDocument, byColumn["sprk_document"].RecordId);
        Assert.True(byColumn.ContainsKey("sprk_someotherlink"));
        // And the same column name on another table is not excluded (the citation is per table).
        var elsewhere = new Entity("sprk_somechildtable") { Id = Guid.NewGuid() };
        elsewhere[Column] = new EntityReference(Document, citedEml);
        Assert.True(RecordOwnershipResolver.OwnershipParentColumnsOf(elsewhere).ContainsKey(Column));
    }

    [Fact(DisplayName = "095a guard control (must fire): the same read DOES keep the submission's own sprk_document lookup, and an unregistered lookup to a document, so the check above can fail")]
    public void OwnerResolver_ControlKeepsARealParent_AndAnUnregisteredDocumentLookup()
    {
        var document = Guid.NewGuid();
        var row = new Entity(Submission) { Id = Guid.NewGuid() };
        row["sprk_document"] = new EntityReference(Document, document);
        row[Column] = new EntityReference(Document, Guid.NewGuid());

        var parent = Assert.Single(RecordOwnershipContext.ParentsOf(row));
        Assert.Equal(Document, parent.EntityLogicalName);
        Assert.Equal(document, parent.RecordId);
        Assert.False(RecordOwnershipResolver.IsCitation(Submission, "sprk_document"));

        // Only the registration holds the column out: the same shape under another name is a parent.
        var other = new Entity(Submission) { Id = Guid.NewGuid() };
        other["sprk_someotherlink"] = new EntityReference(Document, Guid.NewGuid());
        Assert.Single(RecordOwnershipContext.ParentsOf(other));
        Assert.False(RecordOwnershipResolver.IsCitation(Submission, "sprk_someotherlink"));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 3. Source scan
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The identifiers and literals that mean "the source-email link": the column name and the constant that holds it.</summary>
    private static readonly string[] Tokens = ["\"sprk_sourceemaildocument\"", "SourceEmailDocumentColumn"];

    /// <summary>
    /// The files that may name the column (path relative to the BFF project): the constant (the service), the one write (the operation), the one
    /// read (the route) and the explicit citation registration (the resolver). The view, the external view and the visibility code are NOT among them.
    /// </summary>
    private static readonly string[] AllowedFiles =
    [
        "Services/Finance/Submission/InvoiceSubmissionService.cs",
        "Services/Finance/Submission/SubmitInvoiceFromEmailOperation.cs",
        "Api/Finance/InvoiceSubmissionEndpoints.cs",
        "Services/Dataverse/RecordOwnershipResolver.cs",
    ];

    /// <summary>Files (path relative to the BFF project, forward slashes) whose code, comments stripped, names the column.</summary>
    internal static IReadOnlyList<string> FilesNamingTheLink(IEnumerable<(string RelativePath, string Text)> files) =>
        files
            .Where(f => !AllowedFiles.Contains(f.RelativePath, StringComparer.OrdinalIgnoreCase))
            .Where(f => f.Text.Replace("\r\n", "\n").Split('\n').Select(SourceScan.StripLineComment)
                .Any(line => Tokens.Any(t => line.Contains(t, StringComparison.Ordinal))))
            .Select(f => f.RelativePath)
            .ToList();

    private static IEnumerable<(string RelativePath, string Text)> RealBffFiles()
    {
        var root = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            yield return (Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText(file));
        }
    }

    [Fact(DisplayName = "095a guard: only the service, the operation, the route and the citation registration name the source-email link (not the view, the external endpoints, ExternalInvoiceVisibility or any filing file)")]
    public void NoOtherCodeNamesTheSourceEmailLink()
    {
        var files = RealBffFiles().ToList();
        // Sanity floor: the BFF really was scanned, including the files the guard exists to protect.
        Assert.True(files.Count >= 500, $"Only {files.Count} source files scanned; the repository root is probably wrong.");
        foreach (var protectedFile in new[]
        {
            "Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs",
            "Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs",
            "Api/Filters/FinanceAuthorizationFilter.cs",
            "Services/Dataverse/CoreAncestorResolver.cs",
        })
        {
            Assert.Contains(files, f => f.RelativePath == protectedFile);
        }

        // The allowed files really do name it (a renamed constant would make the allow-list vacuous).
        foreach (var allowed in AllowedFiles)
        {
            var text = files.Single(f => f.RelativePath == allowed).Text;
            Assert.True(Tokens.Any(t => text.Contains(t.Trim('"'), StringComparison.Ordinal)), $"{allowed} no longer names the link; update the allow-list.");
        }

        var offenders = FilesNamingTheLink(files);

        Assert.True(offenders.Count == 0,
            "sprk_invoicesubmission.sprk_sourceemaildocument is a citation of the source .eml (task 095a): only InvoiceSubmissionService (the constant), "
            + "SubmitInvoiceFromEmailOperation (the write), InvoiceSubmissionEndpoints (the by-email read) and RecordOwnershipResolver (its citation "
            + "registration) may name it. The view, an external endpoint, ExternalInvoiceVisibility or a filing/lineage file that does can let the link "
            + "reach an external response or decide access. Files:\n  " + string.Join("\n  ", offenders));
    }

    [Fact(DisplayName = "095a source guard control (must fire): code in a protected file naming the column or its constant is reported, each token alone")]
    public void SourceGuard_FiresOnEachToken()
    {
        foreach (var line in new[]
        {
            "var c = row[\"sprk_sourceemaildocument\"];",
            "var c = InvoiceSubmissionService.SourceEmailDocumentColumn;",
        })
        {
            foreach (var path in new[]
            {
                "Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs", "Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs",
                "Services/Finance/Submission/InvoiceSubmissionView.cs",
            })
            {
                var found = FilesNamingTheLink([(path, "class X {\r\n    " + line + "\r\n}")]);
                Assert.Equal(path, Assert.Single(found));
            }
        }
    }

    [Fact(DisplayName = "095a source guard control (must not fire): a comment, the four allowed files, and an unrelated name are not reported")]
    public void SourceGuard_DoesNotFireOnCommentsAllowedFilesOrNeighbours()
    {
        const string comment = "class X {\n    // sprk_sourceemaildocument is a citation\n    /// <c>\"sprk_sourceemaildocument\"</c> SourceEmailDocumentColumn\n}";
        const string code = "class X { var x = InvoiceSubmissionService.SourceEmailDocumentColumn; var y = \"sprk_sourceemaildocument\"; }";

        Assert.Empty(FilesNamingTheLink(
        [
            ("Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs", comment),
            ("Services/Finance/Submission/InvoiceSubmissionService.cs", code),
            ("Services/Finance/Submission/SubmitInvoiceFromEmailOperation.cs", code),
            ("Api/Finance/InvoiceSubmissionEndpoints.cs", code),
            ("Services/Dataverse/RecordOwnershipResolver.cs", code),
            ("Services/Dataverse/CoreAncestorResolver.cs", "class X { var y = \"sprk_sourcedocument\"; var z = \"sprk_sourceemail\"; }"),
        ]));
    }
}
