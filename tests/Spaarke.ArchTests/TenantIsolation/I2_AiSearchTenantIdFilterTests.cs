using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Tenant-isolation invariant <b>I2</b> (spec.md FR-29 / design.md §4D I2):
/// every AI Search query issued from BFF services must carry an unconditional
/// <c>tenantId eq</c> filter — regardless of index, regardless of query shape.
///
/// <para>
/// <b>Severity</b>: CATASTROPHIC. A missing <c>tenantId</c> filter on an AI
/// Search query returns documents from ALL customers' indexed content — a
/// legal firm's motion drafts could be returned to a different firm (§4D I2
/// rationale; the primary threat model r1 protects against).
/// </para>
///
/// <para>
/// <b>Scan shape</b>: file-level enumeration of <c>src/server/**/*.cs</c>
/// files that directly invoke <c>SearchClient.SearchAsync&lt;T&gt;(...)</c> —
/// i.e., the underlying <see href="https://learn.microsoft.com/dotnet/api/azure.search.documents.searchclient.searchasync"/>
/// from the Azure Search SDK. For each such file we assert that the file
/// contains the substring <c>tenantId eq </c> somewhere (i.e., the OData
/// filter that scopes results to a specific tenant is authored in the same
/// file that issues the search).
/// </para>
///
/// <para>
/// <b>Why file-level rather than call-site-level</b>: the SearchOptions
/// object is often built in a helper method above the call site, or via a
/// SearchOptions {…} initializer that includes a Filter string composed by
/// helper functions. A per-call-site Roslyn tracer would produce false
/// positives on legitimate helper-composed filters. The file-level check is
/// a coarser but reliable proxy: it fails on any file that calls SearchClient
/// without also authoring a <c>tenantId eq</c> filter string. If a helper
/// method (in a different file) builds the filter, the calling file needs to
/// either reproduce the substring in a comment / pass-through call OR be
/// added to the documented exclusion list below with a reason.
/// </para>
///
/// <para>
/// <b>Baseline compliant references</b> (per spec.md FR-29): filters are
/// authored inline in each service that queries AI Search — e.g.,
/// <c>RecordSearchService</c>, <c>ReferenceRetrievalService</c>,
/// <c>RagService</c> (multiple filter-building methods),
/// <c>FilesIndexIngestDocumentSource</c>, <c>SessionFilesCleanupJob</c>.
/// The exclusion list below covers pass-through wrappers and one documented
/// global-index case that both need reviewer visibility every time they are
/// changed.
/// </para>
/// </summary>
public class I2_AiSearchTenantIdFilterTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private const string ScanRelDir = "src/server";

    /// <summary>
    /// Recognizes a direct invocation of <c>SearchClient.SearchAsync&lt;T&gt;(...)</c>
    /// — the generic-typed Azure Search SDK method. Excluded by construction:
    /// facade calls like <c>ragService.SearchAsync(...)</c> that route through
    /// higher-level services (their compliance is enforced separately by
    /// scanning the underlying implementation).
    /// </summary>
    /// <remarks>
    /// The pattern requires the generic type-parameter angle-brackets to
    /// avoid matching same-named facade methods on wrapper types like
    /// <c>IRagService.SearchAsync</c>, <c>IInsightsAi.SearchAsync</c>, or
    /// <c>IRecordSearchService.SearchAsync</c> — all of which are non-generic
    /// facades that delegate to a generic SearchClient call INSIDE a
    /// scanned file. The pattern additionally requires the character after
    /// the <c>&lt;</c> to be a letter or underscore — this excludes matches
    /// on <c>.SearchAsync&lt;/c&gt;</c> (the closing tag of an XML doc-comment
    /// <c>&lt;c&gt;</c> element, which was the source of a false positive on
    /// <c>RecallSessionFileHandler.cs</c>).
    /// </remarks>
    private static readonly Regex SearchClientSearchAsyncGeneric = new(
        @"\.SearchAsync\s*<[A-Za-z_]",
        RegexOptions.Compiled);

    /// <summary>
    /// Substring the containing file MUST include somewhere for compliance —
    /// the shape of a tenant-scoping OData filter clause.
    /// </summary>
    private const string RequiredFilterSubstring = "tenantId eq ";

    /// <summary>
    /// Files under <see cref="ScanRelDir"/> excluded from the I2 predicate.
    /// Each entry is a documented reviewer-visible exception; adding to the
    /// list is a code-review sign-off event, not a silent bypass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ResilientSearchClient.cs</b> — pure pass-through wrapper around
    /// <c>SearchClient.SearchAsync&lt;T&gt;</c>: it adds retry/timeout but
    /// does not author the SearchOptions.Filter. The filter is owned by the
    /// caller. Excluding this file scopes the I2 predicate to the callers
    /// that actually author queries.
    /// </para>
    /// <para>
    /// <b>InternalIndexProvider.cs</b> — queries the
    /// <c>spaarke-rag-references</c> index for citation verification. This
    /// index is a Spaarke-owned canonical references corpus (case-law,
    /// statute references, etc.) that is intentionally global rather than
    /// per-tenant: the same references are relevant to every tenant. Reviewer
    /// obligation on future changes: if the references index ever becomes
    /// per-tenant, remove this waiver and enforce the filter. Referenced as a
    /// candidate baseline finding in the task-064 deviations note (see
    /// <c>projects/customer-provisioning-orchestration-r1/notes/task-064-deviations.md</c>).
    /// </para>
    /// <para>
    /// <b>ReferenceIndexingService.cs</b> — <c>DeleteForSourceAsync</c> uses a
    /// <c>schemaMapper.BuildSourceFilter(sourceId)</c> helper to build the
    /// filter (sourceId-scoped deletes during re-indexing). The mapper (in a
    /// different file) is the filter author; the call-site file does not
    /// contain <c>tenantId eq</c>. Reviewer obligation: verify the schema
    /// mapper for each target index composes the filter with tenantId
    /// scoping when the index is per-tenant. Filed as a baseline audit finding
    /// for task 065 — the schema mapper's exact filter shape needs the audit
    /// sweep to confirm.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> ExcludedFileRelPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/ResilientSearchClient.cs",
        "src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/InternalIndexProvider.cs",
        "src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs",
    };

    [Fact(DisplayName = "FR-29/§4D I2: every SearchClient.SearchAsync<T> caller has a 'tenantId eq' filter in the same file")]
    public void SearchClientCallers_HaveTenantIdFilter()
    {
        var serverRoot = Path.Combine(RepoRoot, ScanRelDir);
        Assert.True(
            Directory.Exists(serverRoot),
            $"{ScanRelDir} directory not found at '{serverRoot}'. The I2 ArchTest cannot run without it.");

        var offenders = new List<string>();
        foreach (var file in EnumerateProductionCsFiles(serverRoot))
        {
            var text = File.ReadAllText(file);

            // Cheap first filter: file must reference SearchClient.SearchAsync<T>.
            var searchMatch = SearchClientSearchAsyncGeneric.Match(text);
            if (!searchMatch.Success) continue;

            var rel = RelPath(file);
            if (ExcludedFileRelPaths.Contains(rel)) continue;

            // Compliance: file mentions a tenantId eq filter somewhere.
            if (text.Contains(RequiredFilterSubstring, StringComparison.Ordinal)) continue;

            // For the failure diagnostic, cite the FIRST SearchAsync<T> line found
            // — that is what the developer needs to fix (or move into a helper the
            // scanner can see).
            var lineNumber = LineNumberFor(text, searchMatch.Index);
            offenders.Add(
                $"{rel}:{lineNumber} — SearchClient.SearchAsync<T>(...) call site with no " +
                $"'{RequiredFilterSubstring.TrimEnd()}' filter substring in the file. " +
                $"Fix (in order of preference): (a) add the tenant filter to the SearchOptions.Filter here; " +
                $"(b) if the filter is authored by a helper in another file, in-line a comment referencing " +
                $"the helper so this scan finds '{RequiredFilterSubstring.TrimEnd()}'; " +
                $"(c) if the index is intentionally global (rare — citation-verification only), " +
                $"add this file to the documented ExcludedFileRelPaths list in I2_AiSearchTenantIdFilterTests.cs " +
                $"with a Reason citing the design doc or spec FR. " +
                $"Reference: spec.md FR-29 / design.md §4D I2.");
        }

        Assert.True(
            offenders.Count == 0,
            "§4D I2 violation: SearchClient.SearchAsync<T>(...) call site(s) without a tenantId eq filter " +
            "in the containing file. Cross-tenant search bleed is CATASTROPHIC (legal privilege leak per §4D). " +
            "Scope every AI Search query to the calling tenant.\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    // -----------------------------------------------------------------------
    // Negative controls
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-29 negative control: the SearchAsync-generic pattern matches a real call and skips non-generic facades")]
    public void SearchAsyncGenericPattern_FlagsAndSkipsAsExpected()
    {
        // Real generic call — flagged (needs a matching tenantId eq check to pass).
        Assert.Matches(SearchClientSearchAsyncGeneric,
            "var r = await searchClient.SearchAsync<KnowledgeDocument>(\"*\", opts, ct);");

        // Facade / wrapper method — NOT flagged (routes through underlying implementation
        // that is separately scanned by name).
        Assert.DoesNotMatch(SearchClientSearchAsyncGeneric,
            "var r = await ragService.SearchAsync(query, opts, ct);");
        Assert.DoesNotMatch(SearchClientSearchAsyncGeneric,
            "var r = await insightsAi.SearchAsync(request, ct);");

        // XML doc-comment closing tag — MUST NOT trigger (source of a real
        // false positive on RecallSessionFileHandler.cs where a doc-comment
        // referenced <c>RagService.SearchAsync</c> as a reference callout).
        Assert.DoesNotMatch(SearchClientSearchAsyncGeneric,
            "/// (see <c>RagService.SearchAsync</c> lines 218-224) and ANDs the tenant filter");
    }

    [Fact(DisplayName = "FR-29 positive control: the required filter substring exactly matches the baseline compliant shape")]
    public void TenantIdFilterSubstring_MatchesCompliantShape()
    {
        // Shape as it appears throughout RagService / RecordSearchService / etc.
        Assert.Contains(RequiredFilterSubstring, "tenantId eq '{value}'");
        Assert.Contains(RequiredFilterSubstring, "$\"tenantId eq '{tenantId}'\"");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<string> EnumerateProductionCsFiles(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static int LineNumberFor(string source, int charIndex)
    {
        if (charIndex < 0 || charIndex > source.Length) return 1;
        int line = 1;
        for (int i = 0; i < charIndex; i++)
        {
            if (source[i] == '\n') line++;
        }
        return line;
    }

    private static string RelPath(string fullPath)
        => fullPath.Replace(RepoRoot + Path.DirectorySeparatorChar, string.Empty)
                   .Replace(Path.DirectorySeparatorChar, '/');

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
