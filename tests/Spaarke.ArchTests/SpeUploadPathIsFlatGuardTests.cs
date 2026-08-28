using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The forcing function for the 2026-08-28 flat-upload-path decision: <b>no server-constructed SPE upload
/// path may contain a folder separator.</b>
///
/// <para><b>Why a source scan and not (only) behavioural tests.</b> The rule has to hold at ELEVEN call
/// sites across nine files, and each one is a bare string interpolation buried in a large service method
/// whose collaborators are expensive to stand up. Eleven mock-heavy behavioural tests asserting one string
/// each would be exactly the shape <c>tests/CLAUDE.md</c> B15 bans (setup-to-assertion ratio) and would rot
/// within a release. One source rule covers all eleven AND every site added later — including sites nobody
/// thought to list, which is the same inversion
/// <see cref="SpeWriteSinkContainerProvenanceGuardTests"/> applies to container provenance.</para>
///
/// <para><b>What it does NOT replace.</b> Flatness is only half the contract. Removing a
/// <c>{prefix}/{id}/</c> path without folding the <c>{id}</c> into the FILE NAME is silent data loss —
/// <c>UploadSmallAsync</c> resolves to Graph's path-keyed simple PUT, which takes no
/// <c>@microsoft.graph.conflictBehavior</c> and overwrites unconditionally. A no-separator rule would happily
/// pass that version. Collision SURVIVAL is asserted behaviourally, and perturbation-verified, in
/// <c>tests/integration/data-mutation/SpeUploadPaths/SpeFlatUploadPathTests.cs</c>. Read the two together;
/// neither is sufficient alone, and that is a deliberate division of labour rather than duplication.</para>
///
/// <para><b>Scope.</b> Only files that actually call an SPE content-write sink are scanned, so an unrelated
/// <c>var urlPath = "a/b"</c> elsewhere in the BFF is not this rule's business. Within those files the rule
/// looks at string literals assigned to a <c>*Path</c> / <c>path</c> variable — which is the shape every one
/// of the eleven sites used, and the shape a regression would reintroduce.</para>
/// </summary>
public class SpeUploadPathIsFlatGuardTests
{
    private static readonly string BffRoot =
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    /// <summary>
    /// The upload sinks whose <c>path</c> argument Graph interprets as a folder path. Deliberately the
    /// PATH-taking sinks only: <c>ReplaceFileContentAsUserAsync</c> and <c>DeleteFileAsync</c> address an
    /// existing item by ID and cannot mint a folder, so they are out of scope.
    /// </summary>
    private static readonly Regex PathTakingSinkCall = new(
        @"(?<![A-Za-z0-9_])(?:UploadSmallAsync|UploadSmallAsUserAsync|CreateUploadSessionAsUserAsync)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// A string (plain or interpolated) assigned to a local. The path-shaped NAME filter is applied in code
    /// (<see cref="IsPathShapedName"/>) rather than in the pattern, because an earlier version encoded it as
    /// <c>[A-Za-z_]\w*[Pp]ath</c> — which requires at least one character BEFORE "Path" and therefore missed
    /// a bare <c>var path = …</c>, the exact shape <c>Services/Office/OfficeStorageUploader.cs</c> used. The
    /// negative control below caught that; the filter is now explicit and testable rather than smuggled into
    /// a character class.
    /// </summary>
    private static readonly Regex PathAssignment = new(
        @"(?:var|string\??)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<lit>\$?@?""(?:[^""\\]|\\.|"""")*"")",
        RegexOptions.Compiled);

    /// <summary>True for a local whose name marks it as an upload path: <c>path</c>, <c>spePath</c>,
    /// <c>uploadPath</c>, <c>stagingPath</c>, <c>attachmentPath</c>, …</summary>
    private static bool IsPathShapedName(string name)
        => name.EndsWith("path", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The OBO record/container-keyed routes legitimately accept a caller-supplied <c>{*path}</c> that MAY
    /// contain separators — the caller is addressing a location inside a container it already holds, which
    /// is a different contract from a server-constructed path. Those routes pass the route value straight
    /// through and never build a literal, so they are not matched by <see cref="PathAssignment"/> anyway;
    /// this exclusion is belt-and-suspenders, and it is by FILE because that whole file is the OBO surface.
    /// </summary>
    private static readonly IReadOnlySet<string> FilesWhoseCallerSuppliesThePath =
        new HashSet<string>(StringComparer.Ordinal) { "Api/OBOEndpoints.cs" };

    [Fact(DisplayName = "Flat-upload-path rule: no server-constructed SPE upload path contains a folder separator")]
    public void NoServerConstructedSpeUploadPathContainsAFolderSeparator()
    {
        var violations = ScanTree();

        Assert.True(
            violations.Count == 0,
            "SPE upload path(s) contain a folder separator. In SharePoint Embedded, uploading to a PATH "
            + "makes Graph implicitly CREATE every folder segment of it — that is how `communications`, "
            + "`emails`, `exports`, `chat-uploads`, `ai-prefill` and `analysis-outputs` appeared in SPE "
            + "Admin with nobody having clicked \"New Folder\". Files must land flat in the container root.\n\n"
            + "REMEDY — if the segment you are adding carries UNIQUENESS (a record id, a request id), fold "
            + "it into the FILE NAME instead: `$\"{id:N}_{fileName}\"`, never `$\"{prefix}/{id:N}/{fileName}\"`. "
            + "Do NOT simply delete the segment: UploadSmallAsync is Graph's path-keyed simple PUT, it "
            + "accepts no @microsoft.graph.conflictBehavior, and two uploads to one path are a SILENT "
            + "unconditional REPLACE — dropping the id is data loss, not cleanup. The precedent to copy is "
            + "Services/Email/EmailAttachmentProcessor.GenerateUniqueFileName.\n\n"
            + "If a deliberate folder is genuinely wanted, that is what the SPE Admin \"New Folder\" action "
            + "is for (Api/SpeAdmin/ContainerItemEndpoints.cs) — an explicit operator act, not a side "
            + "effect of an upload.\n\n"
            + "Offending path expressions:\n  " + string.Join("\n  ", violations));

        // Non-vacuity. Every assertion above would hold if the scanner matched nothing at all, so pin that
        // it is actually reading the sites. Eleven server-constructed path assignments existed after the
        // 2026-08-28 flattening; asserting >= 5 leaves room for legitimate churn while still failing loudly
        // if the regex or the file filter silently stops matching.
        Assert.True(
            InspectedPathExpressions().Count >= 5,
            $"The scanner inspected only {InspectedPathExpressions().Count} path expression(s) in files that "
            + "call an SPE upload sink. It should see roughly a dozen. A scanner that finds nothing makes "
            + "this whole rule vacuously green — check PathTakingSinkCall and PathAssignment against the "
            + "current tree before trusting a pass.");
    }

    // =================================================================================================
    // CONTROLS — mandatory for tests/Spaarke.ArchTests/** per tests/CLAUDE.md
    // =================================================================================================

    [Fact(DisplayName = "Flat-upload-path negative control: the detector fires on every folder shape the eleven sites used")]
    public void Detector_NegativeControl_FiresOnAFolderPrefixedUploadPath()
    {
        // A detector nobody has seen fail is a detector nobody knows works. Each case is a verbatim shape
        // that existed in the tree before 2026-08-28, fed as literal source text so these keep proving the
        // rule after the real code moves on.

        // Constant prefix (was Api/Ai/ChatWordExportEndpoints.cs).
        Assert.NotEmpty(ScanText("Api/Fake/A.cs", """
            var uploadPath = $"exports/{request.Filename}";
            var r = await speFileStore.UploadSmallAsUserAsync(ctx, driveId, uploadPath, s, ct);
            """));

        // Constant + derived GUID, LEADING SLASH (was Services/Communication/CommunicationService.cs).
        Assert.NotEmpty(ScanText("Services/Fake/B.cs", """
            var spePath = $"/communications/{communicationId:N}/{emlResult.FileName}";
            var h = await speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);
            """));

        // Three-level nesting (was Workers/Office/UploadFinalizationWorker.cs).
        Assert.NotEmpty(ScanText("Workers/Fake/C.cs", """
            var attachmentPath = $"/emails/attachments/{parentDocumentId:N}/{attachment.FileName}";
            var f = await speFileStore.UploadSmallAsync(driveId, attachmentPath, attachment.Content, ct);
            """));

        // A bare trailing slash is still a folder — the sneakiest regression shape.
        Assert.NotEmpty(ScanText("Services/Fake/D.cs", """
            var stagingPath = $"ai-prefill/{requestId}/";
            await speFileStore.UploadSmallAsUserAsync(httpContext, staging, stagingPath, buffer, ct);
            """));

        // The upload-session sink counts too: it reserves the destination item at a path.
        Assert.NotEmpty(ScanText("Api/Fake/E.cs", """
            var path = $"exports/{name}";
            var session = await speFileStore.CreateUploadSessionAsUserAsync(ctx, driveId, path, behavior, ct);
            """));
    }

    [Fact(DisplayName = "Flat-upload-path positive control: the detector does not fire on the sanctioned flat shapes, on prose, or on non-upload files")]
    public void Detector_PositiveControl_DoesNotFireOnTheSanctionedFlatShape()
    {
        // A guard that flags the code it protects gets deleted rather than obeyed. Five ways this rule
        // could wrongly push code away from the shape it exists to require:

        // (1) The sanctioned shape — id folded into the FILE NAME, no separator anywhere.
        Assert.Empty(ScanText("Services/Fake/F.cs", """
            var spePath = $"{communicationId:N}_{emlResult.FileName}";
            var h = await speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);
            """));

        // (2) A pass-through of a plain filename with no literal at all.
        Assert.Empty(ScanText("Api/Fake/G.cs", """
            var uploadPath = request.Filename;
            var r = await speFileStore.UploadSmallAsUserAsync(ctx, driveId, uploadPath, s, ct);
            """));

        // (3) PROSE must not count. Nearly every one of these files now carries a doc comment EXPLAINING
        //     the old folder path — the comment names "/communications/{id}/attachments/{name}" verbatim.
        //     A comment-blind scan would condemn the very documentation this change added, the list would
        //     be abandoned, and the rule would be deleted rather than obeyed.
        Assert.Empty(ScanText("Services/Fake/H.cs", """
            // This used to be "/emails/attachments/{parentDocumentId:N}/{name}", which minted folders.
            /// <remarks>The old path was $"ai-prefill/{requestId}/{fileName}" — see ADR-007.</remarks>
            /* Historically: var spePath = $"/communications/{id:N}/{name}"; */
            var attachmentPath = $"{parentDocumentId:N}_{attachment.FileName}";
            var f = await speFileStore.UploadSmallAsync(driveId, attachmentPath, attachment.Content, ct);
            """));

        // (4) A file that never calls an upload sink is out of scope — a URL or route path there is
        //     entirely legitimate and must not be dragged into this rule.
        Assert.Empty(ScanText("Api/Fake/I.cs", """
            var relativePath = $"/apps/{appId:D}/r/sprk_todo/{todoId:D}";
            return TypedResults.Ok(relativePath);
            """));

        // (5) THE REAL PRODUCTION TREE, which is the control that actually matters: the rule must be green
        //     against the tree as it now stands. A synthetic control can pass while the rule still flags
        //     the shipped code.
        Assert.Empty(ScanTree());
    }

    // =================================================================================================
    // MACHINERY — crude by design (arch-fitness scanning, not compilation), which is adequate BECAUSE
    // both controls above pin the detector's behaviour in each direction.
    // =================================================================================================

    private static IEnumerable<string> ScannedFiles()
        => Directory
            .EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static List<string> ScanTree()
        => ScannedFiles()
            .SelectMany(f => ScanText(Relative(f), File.ReadAllText(f)))
            .ToList();

    private static List<string> InspectedPathExpressions()
    {
        var seen = new List<string>();

        foreach (var file in ScannedFiles())
        {
            var text = Decomment(File.ReadAllText(file));
            if (!PathTakingSinkCall.IsMatch(text) || FilesWhoseCallerSuppliesThePath.Contains(Relative(file)))
            {
                continue;
            }

            foreach (Match m in PathAssignment.Matches(text))
            {
                if (IsPathShapedName(m.Groups["name"].Value))
                {
                    seen.Add($"{Relative(file)}: {m.Groups["lit"].Value}");
                }
            }
        }

        return seen;
    }

    /// <summary>Every folder-separator-bearing upload-path literal in one file's source text.</summary>
    private static List<string> ScanText(string relativeFile, string rawText)
    {
        var violations = new List<string>();
        var text = Decomment(rawText);

        // Only files that actually upload. A path literal in a file that never calls a path-taking sink
        // cannot mint an SPE folder.
        if (!PathTakingSinkCall.IsMatch(text) || FilesWhoseCallerSuppliesThePath.Contains(relativeFile))
        {
            return violations;
        }

        foreach (Match m in PathAssignment.Matches(text))
        {
            if (!IsPathShapedName(m.Groups["name"].Value))
            {
                continue;
            }

            var literal = m.Groups["lit"].Value;
            if (!literal.Contains('/', StringComparison.Ordinal))
            {
                continue;
            }

            violations.Add(
                $"{relativeFile}:{LineOf(text, m.Index)}: {m.Groups["name"].Value} = {literal} "
                + "— contains a '/', so Graph will create that folder on upload");
        }

        return violations;
    }

    /// <summary>
    /// Strips comments so that PROSE naming an old folder path does not count as one. Load-bearing here:
    /// the flattening change deliberately documents each former path in a comment at its own site, so a
    /// comment-blind scan would flag every file it just fixed. String literals are preserved, since the
    /// literals ARE the subject.
    /// </summary>
    private static string Decomment(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            // String / char literals pass through untouched (verbatim and interpolated included).
            if (text[i] == '"' || text[i] == '\'')
            {
                var end = SkipStringLiteral(text, i);
                sb.Append(text, i, end - i);
                i = end;
                continue;
            }

            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') { i++; }
                continue;
            }

            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) { i++; }
                i = Math.Min(i + 2, text.Length);
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Index just past the string/char literal starting at <paramref name="start"/>.</summary>
    private static int SkipStringLiteral(string text, int start)
    {
        var quote = text[start];
        var verbatim = start > 0 && text[start - 1] == '@';
        var i = start + 1;

        while (i < text.Length)
        {
            if (verbatim)
            {
                if (text[i] == quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == quote) { i += 2; continue; }
                    return i + 1;
                }
            }
            else
            {
                if (text[i] == '\\') { i += 2; continue; }
                if (text[i] == quote) { return i + 1; }
                if (text[i] == '\n') { return i; } // unterminated — fail open rather than run away
            }

            i++;
        }

        return i;
    }

    private static int LineOf(string text, int index)
        => text.Take(index).Count(c => c == '\n') + 1;

    private static string Relative(string absolute)
        => absolute
            .Replace(BffRoot + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');
}
