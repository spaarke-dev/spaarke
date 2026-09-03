using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 (2026-09-01) — the shared upload adapter's target route must exist on the
/// server, and must be the RECORD-KEYED one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this exists to prevent.</b> <c>bffUploadServiceAdapter.uploadFile</c> posted a multipart
/// form to <c>/api/documents/upload</c> — a route the BFF <b>does not serve at any group prefix</b>. The
/// external-spa Document Upload page is routed (<c>external-spa/src/App.tsx</c> → <c>/upload</c>) and
/// shipped, so <b>every upload an external user attempted returned 404</b> for the life of the feature.
/// Nothing caught it: the TypeScript compiler cannot see server routes, the C# compiler cannot see client
/// string literals, and no test spanned the two. It survived a full technical-debt sweep as well — the
/// sweep did not find it either.
/// </para>
/// <para>
/// <b>Why a fitness function and not a unit test.</b> This is an AGREEMENT bug between two files in two
/// languages, exactly the shape <see cref="SpeAdminClientRouteAgreementTests"/> was written for (task 092,
/// which found two live 404s behind shipped buttons). That guard is scoped to <c>speApiClient.ts</c> and the
/// <c>/spe</c> prefix, which is precisely why this adapter slipped past it. This is the same instrument
/// aimed at the upload adapter. Per ADR-038 Amendment A1, <c>tests/Spaarke.ArchTests/**</c> is a
/// deletion-protected KEEP path and its tests name the invariant rather than a method-under-test.
/// </para>
/// <para>
/// <b>Rule 2 is the one that matters most.</b> Making the 404 disappear is easy and there is a WRONG way to
/// do it: <c>PUT /api/drives/{driveId}/upload</c> exists and accepts a <b>caller-supplied drive id</b>. That
/// is the client-named-container defect this entire project exists to remove (task 083's census; the same
/// shape as tasks 073/076/085 and issue #858). So Rule 2 fails the build if the adapter is ever repointed at
/// a drive-keyed or container-keyed upload route. A guard that only checked "does the URL resolve" would
/// happily bless the insecure fix.
/// </para>
/// <para><b>Maintenance.</b> If the record-keyed upload route is intentionally renamed, update
/// <see cref="RecordKeyedUploadRoute"/> here in the same PR — that is the point of the guard, not an
/// obstacle to it. If the adapter legitimately needs a second route, add it to the allow-list in Rule 2
/// WITH a reason and an ADR/task citation, per this path's authoring rules.</para>
/// </remarks>
public class ClientUploadRouteAgreementTests
{
    private static string RepoRoot => FindRepoRoot();

    private static string AdapterFile => Path.Combine(
        RepoRoot, "src", "client", "shared", "Spaarke.UI.Components", "src", "utils", "adapters",
        "bffUploadServiceAdapter.ts");

    private static string OboEndpointsFile => Path.Combine(
        RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Api", "OBOEndpoints.cs");

    /// <summary>
    /// The record-keyed OBO upload route. <c>OBOEndpoints.cs</c>'s own header marks this
    /// "TARGET, record-keyed, GATED". It derives the container from the RECORD via
    /// <c>RecordContainerResolver.ResolveForRecordAsync</c> and fails closed for a secure record with no
    /// container of its own — so the caller cannot choose where bytes land.
    /// </summary>
    private const string RecordKeyedUploadRoute =
        "/api/obo/records/{entityLogicalName}/{recordId:guid}/files/{*path}";

    /// <summary>
    /// Upload routes the adapter must NEVER target, and why. Each takes a caller-supplied storage
    /// location, which is the defect class this project removes.
    /// </summary>
    private static readonly (string Fragment, string Why)[] ForbiddenUploadTargets =
    {
        ("/api/documents/upload",
            "no such server route — this is the original defect: every external-user upload 404'd"),
        ("/api/drives/",
            "drive-keyed: takes a CALLER-SUPPLIED driveId (task 083 census; the client-named-container "
            + "defect this project removes). Use the record-keyed route so the server derives the container."),
        ("/api/containers/",
            "container-keyed: same defect class as drive-keyed — the caller names the destination."),
    };

    [Fact(DisplayName = "Rule 1: the upload adapter's target route exists on the server")]
    public void UploadAdapterTargetRouteExistsOnServer()
    {
        var adapter = ReadRequired(AdapterFile, nameof(AdapterFile));
        var server = ReadRequired(OboEndpointsFile, nameof(OboEndpointsFile));

        // The adapter must reference the record-keyed shape. Compare on the STRUCTURE rather than the
        // literal string: the client builds the URL from template expressions
        // (`/api/obo/records/${entityName}/${entityId}/files/${path}`), so a textual equality check
        // against the server's route template would never match and the guard would be vacuous.
        var clientTargetsRecordKeyedUpload = Regex.IsMatch(
            adapter,
            @"/api/obo/records/\$\{[^}]+\}/\$\{[^}]+\}/files/",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(
            clientTargetsRecordKeyedUpload,
            "bffUploadServiceAdapter no longer builds a record-keyed OBO upload URL of the form "
            + "/api/obo/records/${entity}/${id}/files/... . If the upload contract moved, update "
            + $"{nameof(RecordKeyedUploadRoute)} in this guard in the SAME PR. If it moved to a route that "
            + "takes a caller-supplied container or drive, that is a security regression — see Rule 2.");

        // And the server must actually serve it. This half is what the original defect failed:
        // the client had a plausible-looking URL and there was no route behind it.
        Assert.True(
            server.Contains(RecordKeyedUploadRoute, StringComparison.Ordinal),
            $"The server no longer maps '{RecordKeyedUploadRoute}' in OBOEndpoints.cs, but "
            + "bffUploadServiceAdapter still calls that shape. Deleting or renaming a route while a client "
            + "still calls it produces a silent 404 behind a shipped button — the exact failure this guard "
            + "exists to catch (it shipped once already).");
    }

    [Fact(DisplayName = "Rule 2: the upload adapter never targets a caller-named storage location")]
    public void UploadAdapterNeverTargetsCallerNamedStorage()
    {
        var adapter = ReadRequired(AdapterFile, nameof(AdapterFile));

        // Scope to the uploadFile body. The invariant is about where BYTES GO, so scanning the whole file
        // over-reaches: on first run this rule flagged `getContainerIdForEntity`, which does
        // `GET /api/containers/{entity}/{id}` — a READ that asks the server which container a record uses.
        // That is the server-derives direction, not caller-named storage, and failing it would have been a
        // false positive of exactly the kind that gets a guard deleted rather than obeyed.
        // (Separately: that GET has no server route and no production caller — a dormant 404 recorded in
        // notes/tech-debt-sweep-VERIFICATION-2026-09-01.md. Out of scope for THIS rule by design.)
        var uploadBody = ExtractUploadFileBody(adapter);

        // Strip comments before scanning. The adapter documents the forbidden routes BY NAME in its own
        // explanatory comment ("Do NOT 'fix' this by pointing at PUT /api/drives/{driveId}/upload"), and a
        // guard that flags the documentation explaining the rule is a guard that gets deleted rather than
        // obeyed. Scan executable lines only.
        var executable = StripComments(uploadBody);

        var violations = ForbiddenUploadTargets
            .Where(t => executable.Contains(t.Fragment, StringComparison.Ordinal))
            .Select(t => $"  {t.Fragment} — {t.Why}")
            .ToList();

        Assert.True(
            violations.Count == 0,
            "bffUploadServiceAdapter targets an upload route that lets the CALLER name the storage "
            + "destination:\n" + string.Join("\n", violations)
            + "\n\nThe server must derive the container from a record it authorized. Use the record-keyed "
            + "route (see Rule 1). If a deviation is genuinely required, it needs an ADR-003 / §6.5 path-A "
            + "exception documented in the PR, not a quiet edit here.");
    }

    // ── Controls ────────────────────────────────────────────────────────────────────────────────────
    // Per this KEEP path's authoring rules: a negative control proving each detector FIRES on a seeded
    // violation, and a positive control proving it does NOT fire on the sanctioned shape. A detector
    // nobody has watched fail is a detector nobody knows works.

    [Fact(DisplayName = "Negative control: Rule 2's detector fires on the original defective URL")]
    public void Rule2_FiresOnTheOriginalDefectiveUrl()
    {
        const string seeded = "      const url = `${baseUrl}/api/documents/upload`;";
        var executable = StripComments(seeded);

        Assert.Contains("/api/documents/upload", executable, StringComparison.Ordinal);
        Assert.True(
            ForbiddenUploadTargets.Any(t => executable.Contains(t.Fragment, StringComparison.Ordinal)),
            "Rule 2 failed to flag the exact URL that shipped broken — the detector does not work.");
    }

    [Fact(DisplayName = "Negative control: Rule 2's detector fires on the drive-keyed 'easy fix'")]
    public void Rule2_FiresOnTheDriveKeyedEasyFix()
    {
        // The tempting one-line repair, which would resolve the 404 by reintroducing the very defect
        // this project removes. If this control ever passes, Rule 2 has stopped protecting anything.
        const string seeded = "      const url = `${baseUrl}/api/drives/${driveId}/upload`;";
        var executable = StripComments(seeded);

        Assert.True(
            ForbiddenUploadTargets.Any(t => executable.Contains(t.Fragment, StringComparison.Ordinal)),
            "Rule 2 did not flag a drive-keyed upload URL. The guard would bless the insecure fix.");
    }

    [Fact(DisplayName = "Positive control: Rule 2 does NOT fire on the sanctioned record-keyed URL")]
    public void Rule2_DoesNotFireOnTheSanctionedShape()
    {
        const string sanctioned =
            "      const url = `${baseUrl}/api/obo/records/${entityName}/${entityId}/files/${path}`;";
        var executable = StripComments(sanctioned);

        Assert.True(
            ForbiddenUploadTargets.All(t => !executable.Contains(t.Fragment, StringComparison.Ordinal)),
            "Rule 2 flagged the CORRECT record-keyed URL. A guard that fails the code it protects gets "
            + "deleted rather than obeyed — this control caught real defects in two sibling guards.");
    }

    [Fact(DisplayName = "Positive control: comment-stripping does not hide a real violation on the same line")]
    public void StripComments_DoesNotSwallowCodeBeforeATrailingComment()
    {
        // Guard the guard: if StripComments were too greedy it could blank an offending line that happens
        // to carry a trailing comment, silently disarming Rule 2.
        const string seeded = "  const url = `/api/drives/${d}/upload`; // legacy path";
        var executable = StripComments(seeded);

        Assert.Contains("/api/drives/", executable, StringComparison.Ordinal);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the body of <c>uploadFile</c> — from its signature to the terminator of that method — so
    /// Rule 2 asks "where do the BYTES go?" rather than "does this file mention a container route
    /// anywhere?". Fails loudly if the method cannot be located: a scoping helper that silently returns
    /// empty would disarm Rule 2 while the suite still reported green.
    /// </summary>
    private static string ExtractUploadFileBody(string adapter)
    {
        var start = adapter.IndexOf("uploadFile(", StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            "Could not find `uploadFile(` in bffUploadServiceAdapter.ts. If the method was renamed, update "
            + "this guard in the SAME PR — otherwise Rule 2 silently stops checking anything.");

        // The method terminator at object-literal indent: `\n    },`
        var end = adapter.IndexOf("\n    },", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = adapter.Length;
        }

        var body = adapter[start..end];
        Assert.True(
            body.Contains("const url", StringComparison.Ordinal),
            "Extracted an uploadFile body with no URL construction in it — the extraction boundary is "
            + "wrong, and Rule 2 would be scanning the wrong text.");
        return body;
    }

    /// <summary>Removes <c>//</c> line comments and <c>/* */</c> block comments so the scan sees code only.</summary>
    private static string StripComments(string source)
    {
        var noBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty,
            RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        // Line comments: drop from `//` to end of line. Not URL-safe in general (`https://`), but these
        // adapters build paths, not absolute URLs with schemes; the trailing-comment control above pins
        // the behaviour that matters.
        return Regex.Replace(noBlocks, @"//[^\r\n]*", string.Empty,
            RegexOptions.None, TimeSpan.FromSeconds(2));
    }

    private static string ReadRequired(string path, string label)
    {
        Assert.True(
            File.Exists(path),
            $"{label} not found at '{path}'. If the file MOVED, update this guard in the same PR; a guard "
            + "that silently stops finding its subject is worse than no guard — it reports green forever.");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
