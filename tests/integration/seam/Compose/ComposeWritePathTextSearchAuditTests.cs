// Task 034 (spaarkeai-compose-r4, NFR-02 / invariant I-7) — the CODE AUDIT the task explicitly requires:
// a static, source-level confirmation that the compose SAVE (write) path contains ZERO text-search API
// usage, backing the dynamic proof in ComposePatchEngineSaveSeamTests (an edit lands at its intended
// paraId via ≥3 corpus docs). A dynamic through-the-wire test can show an op landed correctly; it cannot
// by itself prove the ABSENCE of a text-search fallback elsewhere in the method. This file reads the
// actual production source text of the two write-path files and asserts none of the banned
// content-locating APIs (`.IndexOf(`, `.Contains(`, `.StartsWith(`, `.EndsWith(` — the shapes a
// text-search-based target locator would use) appear in the methods that resolve WHERE an edit lands.
//
// SCOPE (per task 034's explicit "DocxAnnotationWriter's push-annotations use is out of scope, task 036"
// note): this audits ComposeShadowPatchEngine.cs (the single byte-author, task 030/031) and the
// ComposeService.SaveAsync + ResolveSaveBaselineAsync slice (the save-path orchestration, task 032's
// cutover) — the ACTUAL write path. DocxAnnotationWriter.cs (the retired text-search root cause,
// `LocateTarget`) is deliberately NOT touched here; it remains wired ONLY to the push-annotations surface
// (ComposeService.PushAnnotationsAsync, well outside the SaveAsync/ResolveSaveBaselineAsync slice this
// file extracts) and its retirement is task 036's scope, not this task's.
//
// This is a SOURCE-TEXT audit, not a reflection-based test on the class-under-test's own internals (the
// ADR-038 B8 ban targets reflection over PRIVATE MEMBERS to assert behavior a public-surface test could
// exercise instead; this test asserts a structural/architectural absence-of-API-usage property no runtime
// assertion can observe — same genre as the NetArchTest fitness functions already established in
// tests/Spaarke.ArchTests/, scoped here to sit alongside its sibling through-the-wire proof per the
// task's declared output path).

using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeWritePathTextSearchAuditTests
{
    private static readonly string[] BannedTextSearchApis =
    {
        ".IndexOf(",
        ".Contains(",
        ".StartsWith(",
        ".EndsWith(",
    };

    [Fact(DisplayName = "Code audit: ComposeShadowPatchEngine.cs (the single byte-author) contains ZERO text-search API usage")]
    public void ComposeShadowPatchEngine_Source_ContainsNoTextSearchApiUsage()
    {
        var source = ReadRepoFile("src", "server", "api", "Sprk.Bff.Api", "Services", "Compose", "ComposeShadowPatchEngine.cs");

        // The engine resolves every anchor by w14:paraId dictionary lookup (see
        // PatchSession.BuildParaIdIndex / PatchSession.Resolve) and run-local (runIndex, offset) math —
        // it never scans a paragraph's or the document's text content to locate an edit target.
        foreach (var banned in BannedTextSearchApis)
        {
            source.Should().NotContain(banned,
                $"ComposeShadowPatchEngine.cs (the single byte-author, I-7) must contain zero '{banned}' usage — " +
                "every anchor is resolved by paraId dictionary lookup + run-local offset math, never by " +
                "scanning document/run text for a content match");
        }
    }

    [Fact(DisplayName = "Code audit: ComposeService's save-path slice (SaveAsync + ResolveSaveBaselineAsync) contains ZERO text-search API usage and never calls the retired DocxAnnotationWriter")]
    public void ComposeService_SavePathSlice_ContainsNoTextSearchApiUsage_AndDoesNotInvokeRetiredWriter()
    {
        var fullSource = ReadRepoFile("src", "server", "api", "Sprk.Bff.Api", "Services", "Compose", "ComposeService.cs");

        // Extract exactly the write-path slice: from the SaveAsync method signature up to (but not
        // including) the NEXT method after its ResolveSaveBaselineAsync helper (ResolveRevisionAuthor).
        // Marker strings, not line numbers, so this audit stays correct across incidental reformatting —
        // if either marker disappears (the methods were renamed/moved), the test fails loudly rather than
        // silently auditing the wrong (or an empty) slice.
        const string sliceStartMarker = "public async Task<SaveComposeDocumentResult> SaveAsync(";
        const string sliceEndMarker = "private static string ResolveRevisionAuthor(HttpContext httpContext)";

        var startIndex = fullSource.IndexOf(sliceStartMarker, StringComparison.Ordinal);
        var endIndex = fullSource.IndexOf(sliceEndMarker, StringComparison.Ordinal);

        startIndex.Should().BeGreaterThanOrEqualTo(0,
            "the SaveAsync method signature marker must be found in ComposeService.cs — if this fails, " +
            "the method was renamed/moved and this audit's slice must be updated");
        endIndex.Should().BeGreaterThan(startIndex,
            "the ResolveRevisionAuthor method signature marker (the method immediately after " +
            "ResolveSaveBaselineAsync) must be found AFTER SaveAsync in ComposeService.cs — if this fails, " +
            "the method was renamed/moved and this audit's slice must be updated");

        var savePathSlice = fullSource[startIndex..endIndex];

        // SaveAsync resolves the baseline (ResolveSaveBaselineAsync) and hands it + the operation log to
        // ComposeShadowPatchEngine.Apply — ID-anchored, zero text-search, per its own remarks (see the
        // "E1 KEYSTONE" comment block immediately above SaveAsync in ComposeService.cs).
        foreach (var banned in BannedTextSearchApis)
        {
            savePathSlice.Should().NotContain(banned,
                $"ComposeService.cs's SaveAsync + ResolveSaveBaselineAsync slice (the save-path orchestration) " +
                $"must contain zero '{banned}' usage — the baseline resolution + operation-log application are " +
                "both ID/version-anchored, never a text scan of document content");
        }

        // The retired text-search writer (DocxAnnotationWriter.LocateTarget — the R3 interior-location
        // 422 root cause per corpus-manifest.md row 1) must NOT be reachable from the save path. It
        // remains wired ONLY to PushAnnotationsAsync (task 036 scope, out of this task's bounds).
        savePathSlice.Should().NotContain("_annotationWriter",
            "the save path (SaveAsync/ResolveSaveBaselineAsync) must never invoke the retired " +
            "DocxAnnotationWriter — that text-search writer is wired only to the push-annotations surface " +
            "(ComposeService.PushAnnotationsAsync), which task 036 (not this task) is scoped to retire");
    }

    /// <summary>Resolves the repo root the same way <c>ComposeCorpusFixtureLocator</c> does (walk up from
    /// the test assembly's output directory looking for the canonical
    /// <c>src/server/api/Sprk.Bff.Api/Program.cs</c> marker) and reads the requested file's full source
    /// text. A source-text audit tool operating on production files at TEST time — categorically distinct
    /// from a banned production-code text-search over DOCUMENT CONTENT at RUNTIME.</summary>
    private static string ReadRepoFile(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs");
            if (File.Exists(marker))
            {
                var target = Path.Combine(new[] { dir.FullName }.Concat(relativeSegments).ToArray());
                if (!File.Exists(target))
                {
                    throw new InvalidOperationException(
                        $"Resolved repo root '{dir.FullName}' but the audited file '{target}' does not exist.");
                }

                return File.ReadAllText(target);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root from test base directory '{AppContext.BaseDirectory}'.");
    }
}
