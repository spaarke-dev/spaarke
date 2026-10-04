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
///
/// <para>══ EXTENDED 2026-08-29 — RULE 2, and why one rule was not enough. ══ The original rule caught
/// FOLDER PREFIXES: a <c>'/'</c> the SERVER wrote into a path literal. It could not catch the thing that
/// actually caused the reported folders, because that <c>'/'</c> came from a USER — an unsanitized file name
/// reaching the sink, where the literal is innocent (<c>$"{id:N}_{fileName}"</c>) and the runtime VALUE is
/// not. Rule 1 was green against the Word add-in defect for as long as it existed.
/// <see cref="EveryPathArgumentOfEveryUploadSinkIsANamedSanitizedLocal"/> closes that: it inspects the path
/// ARGUMENT at every sink call rather than the literals near it, and requires two things —</para>
/// <list type="number">
///   <item><description>the argument is a LOCAL whose name ends in <c>path</c>, so the value is named as
///     what it is (the whole upload path) and no site can pass <c>fileName</c> straight through and slip
///     past a name-keyed scan — that blind spot really existed, at
///     <c>Services/Compose/ComposeService.cs</c> and <c>Services/Email/EmailAttachmentProcessor.cs</c>;
///     and</description></item>
///   <item><description>that local's initializer runs the value through
///     <c>SpeUploadPath.SanitizeFileName</c> — the ONE sanitizer (root CLAUDE.md §11).</description></item>
/// </list>
/// <para>The two halves are load-bearing together: (1) makes (2) COMPLETE. Without (1) the sanitization
/// check would only see sites that already happened to use a path-named local, which is the same
/// census-with-holes failure this project has hit repeatedly.</para>
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

    /// <summary>
    /// The Graph plumbing layer, excluded from RULE 2 only (Rule 1 has no reason to care, since the layer
    /// builds no literals).
    ///
    /// <para><b>Why.</b> Every sink call in here is the facade FORWARDING a <c>path</c> parameter it was
    /// handed — <c>SpeFileStore.UploadSmallAsync(driveId, path, …) =&gt; _driveItemOps.UploadSmallAsync(
    /// driveId, path, …)</c>. It originates no path, so there is nothing here to sanitize; requiring a
    /// sanitized local would force the facade to rewrite its callers' values, which would break the ONE
    /// contract that legitimately passes a multi-segment path (<c>Api/OBOEndpoints.cs</c>) and would put the
    /// decision in the layer ADR-007 keeps decision-free.</para>
    ///
    /// <para>Identical exclusion, identical reasoning, and identical constant name as
    /// <see cref="SpeWriteSinkContainerProvenanceGuardTests"/>'s — whose Rule D turns "this layer
    /// originates nothing" from an assumption into a checked claim. That rule is what keeps THIS exclusion
    /// honest too, so do not widen either one without re-reading it.</para>
    /// </summary>
    private const string PlumbingLayer = "Infrastructure/Graph";

    private static bool IsPlumbingLayer(string relativeFile)
        => relativeFile.StartsWith(PlumbingLayer + "/", StringComparison.Ordinal);

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
    // RULE 2 — the path ARGUMENT at every sink call is a named, sanitized local
    // -------------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE. You are here because a sink call's path argument is not a sanitized local.
    // The fix is two lines at the call site, and it is the same two lines every other site uses:
    //
    //     var uploadPath = SpeUploadPath.SanitizeFileName(theName);          // or, when an id carries
    //     var spePath    = $"{id:N}_{SpeUploadPath.SanitizeFileName(name)}"; // the uniqueness
    //     await speFileStore.UploadSmallAsync(driveId, uploadPath, stream, ct);
    //
    // Do NOT satisfy this by renaming a variable to end in "path" — half (2) still fails, which is the
    // point. Do NOT write a second sanitizer; there is exactly one (root CLAUDE.md §11), and adding
    // another is an automatic code-review failure. If a genuinely new site legitimately supplies a
    // MULTI-SEGMENT path (the caller addressing a location inside a container it already holds), it does
    // not belong in this rule at all — add the FILE to FilesWhoseCallerSuppliesThePath with a written
    // reason, exactly as Api/OBOEndpoints.cs is, and validate its segments the way that file does with
    // SpeUploadPath.IsSafeSegment. That is a deliberate contract, not an exemption from sanitizing.
    // =================================================================================================

    /// <summary>The path parameter's ZERO-BASED position in each sink's argument list, from the facade
    /// signatures in <c>Infrastructure/Graph/ISpeFileOperations.cs</c> and <c>UploadSessionManager.cs</c>:
    /// <c>UploadSmallAsync(driveId, path, content, ct)</c>;
    /// <c>UploadSmallAsUserAsync(ctx, containerId, path, content, ct)</c>;
    /// <c>CreateUploadSessionAsUserAsync(ctx, driveId, path, conflictBehavior, ct)</c>. Pinned by
    /// <see cref="SinkSignaturePositionsStillMatchTheFacade"/> so a signature change cannot silently make
    /// this rule inspect the wrong argument.</summary>
    private static readonly IReadOnlyDictionary<string, int> PathArgumentIndex =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["UploadSmallAsync"] = 1,
            ["UploadSmallAsUserAsync"] = 2,
            ["CreateUploadSessionAsUserAsync"] = 2,
        };

    /// <summary>The one sanctioned sanitizer. A site is compliant only if its path local runs through it.</summary>
    private const string Sanitizer = "SanitizeFileName";

    [Fact(DisplayName = "Flat-upload-path rule 2: every SPE upload sink's path argument is a named local sanitized through SpeUploadPath.SanitizeFileName")]
    public void EveryPathArgumentOfEveryUploadSinkIsANamedSanitizedLocal()
    {
        var violations = ScanArgumentsInTree();

        Assert.True(
            violations.Count == 0,
            "SPE upload path argument(s) are not named, sanitized locals.\n\n"
            + "WHY THIS RULE EXISTS AND RULE 1 DOES NOT COVER IT. The folders an operator found in SPE "
            + "Admin were NOT caused by a folder prefix in our code — they were caused by an unsanitized "
            + "FILE NAME. The Word add-in's \"Document Name\" box is free text; a user typed a date, "
            + "\"New Word Document from Word Web Add In 8/24/2026\", it became the upload path verbatim, and "
            + "Graph created a folder \"…Add In 8\" containing a folder \"24\" containing an extension-less "
            + "file \"2026\". A file name IS a path. Rule 1 inspects LITERALS and was green throughout.\n\n"
            + "REMEDY — see the MAINTENANCE PROCEDURE above this test.\n\n"
            + "Offending sink calls:\n  " + string.Join("\n  ", violations));

        // Non-vacuity, and stronger than Rule 1's: assert the scanner actually reached every sink CALL in
        // the tree, not merely "some" path expressions. A scanner that silently stops matching call sites
        // makes this rule pass while covering nothing.
        var inspected = InspectedSinkCalls();
        Assert.True(
            inspected.Count >= 10,
            $"The scanner inspected only {inspected.Count} SPE upload sink call(s) outside the two excluded "
            + "groups (the OBO caller-supplies-the-path file and the Graph plumbing layer). There were 14 "
            + "on 2026-08-29. A number well below that means SinkCallWithArgs, the sink names, or an "
            + "exclusion has drifted from the tree — check before trusting a pass. Inspected:\n  "
            + string.Join("\n  ", inspected));
    }

    [Fact(DisplayName = "Flat-upload-path rule 2 support: the pinned path-argument positions still match the facade signatures")]
    public void SinkSignaturePositionsStillMatchTheFacade()
    {
        // PathArgumentIndex is a hand-maintained map of argument POSITIONS, which is precisely the kind of
        // census this repo has learned to distrust: if a facade method gains a parameter before `path`,
        // every assertion above silently starts inspecting the wrong argument and the rule goes quietly
        // vacuous rather than red. So the positions are re-derived from the declarations themselves.
        var declarations = new[]
        {
            Path.Combine(BffRoot, "Infrastructure", "Graph", "ISpeFileOperations.cs"),
            Path.Combine(BffRoot, "Infrastructure", "Graph", "UploadSessionManager.cs"),
        };

        var mismatches = new List<string>();

        foreach (var (sink, expectedIndex) in PathArgumentIndex)
        {
            var found = false;

            foreach (var file in declarations.Where(File.Exists))
            {
                var text = Decomment(File.ReadAllText(file));

                foreach (Match m in new Regex($@"(?<![A-Za-z0-9_]){Regex.Escape(sink)}\s*\(", RegexOptions.None).Matches(text))
                {
                    var args = SplitTopLevelArguments(ArgumentListAt(text, m.Index + m.Length - 1));

                    // A DECLARATION's parameters are "Type name" pairs; a CALL's are bare expressions.
                    // Only declarations are of interest here.
                    if (args.Count <= expectedIndex || !args[expectedIndex].Trim().EndsWith(" path", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    found = true;
                    break;
                }

                if (found) break;
            }

            if (!found)
            {
                mismatches.Add($"{sink}: no declaration found whose parameter #{expectedIndex} is named 'path'");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            "PathArgumentIndex no longer matches the facade. Rule 2 is inspecting the wrong argument "
            + "position, which makes it pass while checking something else entirely.\n\n"
            + "REMEDY — update PathArgumentIndex to the new position. Do NOT delete the entry.\n\n  "
            + string.Join("\n  ", mismatches));
    }

    // =================================================================================================
    // CONTROLS — mandatory for tests/Spaarke.ArchTests/** per tests/CLAUDE.md
    // =================================================================================================

    [Fact(DisplayName = "Flat-upload-path rule 2 negative control: the detector fires on an unsanitized name, on a pass-through, and on a member-access argument")]
    public void ArgumentDetector_NegativeControl_FiresOnAnUnsanitizedPathArgument()
    {
        // A detector nobody has seen fail is a detector nobody knows works. Each case below is a shape that
        // really existed in this tree before 2026-08-29.

        // (1) THE ACTUAL DEFECT — client-supplied name passed straight through, named as a path so that a
        //     name-only rule would have called it compliant. This is Api/Ai/ChatWordExportEndpoints.cs.
        Assert.NotEmpty(ScanArgumentsInText("Api/Fake/A.cs", """
            var uploadPath = request.Filename;
            var r = await speFileStore.UploadSmallAsUserAsync(ctx, driveId, uploadPath, s, ct);
            """));

        // (2) The id-carrying composition WITHOUT the sanitizer — "{id}_a/b.docx" still mints "{id}_a", so
        //     folding an id in front is not a substitute for sanitizing.
        Assert.NotEmpty(ScanArgumentsInText("Services/Fake/B.cs", """
            var spePath = $"{communicationId:N}_{fileName}";
            var h = await speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);
            """));

        // (3) The blind spot half (1) of the rule exists for: no path-named local at all, the name handed
        //     straight to the sink. Services/Compose/ComposeService.cs and
        //     Services/Email/EmailAttachmentProcessor.cs both did this, and a name-keyed scan saw neither.
        Assert.NotEmpty(ScanArgumentsInText("Services/Fake/C.cs", """
            var created = await _spe.UploadSmallAsUserAsync(httpContext, driveId, fileName, createStream, ct);
            """));

        // (4) A member access, which is a pass-through wearing a different hat.
        Assert.NotEmpty(ScanArgumentsInText("Services/Fake/D.cs", """
            var h = await speFileStore.UploadSmallAsync(driveId, request.Filename, stream, ct);
            """));

        // (5) A local sanitized by SOMETHING ELSE. There is exactly one sanctioned sanitizer; a second one
        //     is what this codebase already accumulated seven times over.
        Assert.NotEmpty(ScanArgumentsInText("Services/Fake/E.cs", """
            var uploadPath = MyOwnScrubber.Clean(fileName);
            var h = await speFileStore.UploadSmallAsync(driveId, uploadPath, stream, ct);
            """));

        // (6) The upload-SESSION sink counts too — it reserves the destination item at a path.
        Assert.NotEmpty(ScanArgumentsInText("Api/Fake/F.cs", """
            var path = name;
            var s = await speFileStore.CreateUploadSessionAsUserAsync(ctx, driveId, path, behavior, ct);
            """));
    }

    [Fact(DisplayName = "Flat-upload-path rule 2 positive control: the detector does not fire on the sanctioned shapes, on multi-line calls, on prose, or on the real tree")]
    public void ArgumentDetector_PositiveControl_DoesNotFireOnTheSanctionedShapes()
    {
        // A guard that flags the code it protects gets deleted rather than obeyed.

        // (1) The plain sanctioned shape.
        Assert.Empty(ScanArgumentsInText("Api/Fake/G.cs", """
            var uploadPath = SpeUploadPath.SanitizeFileName(request.Filename);
            var r = await speFileStore.UploadSmallAsUserAsync(ctx, driveId, uploadPath, s, ct);
            """));

        // (2) The id-carrying sanctioned shape — uniqueness in the name, sanitizer inside the hole.
        Assert.Empty(ScanArgumentsInText("Services/Fake/H.cs", """
            var spePath = $"{communicationId:N}_{SpeUploadPath.SanitizeFileName(fileName)}";
            var h = await speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);
            """));

        // (3) MULTI-LINE calls, which most of the real sites are. A single-line-only scanner would report
        //     these as having no path argument at all and the rule would read as green while seeing nothing.
        Assert.Empty(ScanArgumentsInText("Services/Fake/I.cs", """
            var stagingPath = SpeUploadPath.SanitizeFileName(fileName);
            var uploadResult = await _speFileStore.UploadSmallAsUserAsync(
                httpContext,
                stagingContainerId,
                stagingPath,
                buffer,
                cancellationToken);
            """));

        // (4) PROSE must not count — every one of these files now carries a comment SHOWING the old
        //     unsanitized shape, and a comment-blind scan would condemn the documentation this added.
        Assert.Empty(ScanArgumentsInText("Services/Fake/J.cs", """
            // This used to be: await speFileStore.UploadSmallAsync(driveId, fileName, stream, ct);
            /* and before that: var spePath = $"{id:N}_{fileName}"; */
            var spePath = $"{id:N}_{SpeUploadPath.SanitizeFileName(fileName)}";
            var h = await speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);
            """));

        // (5) The DECLARATION of a sink is not a call to one. Asserted OUTSIDE the plumbing layer so it
        //     genuinely exercises LooksLikeDeclaration rather than being swallowed by the file exclusion —
        //     which is what an earlier draft of this control did, making it prove nothing.
        Assert.Empty(ScanArgumentsInText("Services/Fake/K.cs", """
            public Task<FileHandleDto?> UploadSmallAsync(string driveId, string path, Stream content, CancellationToken ct = default)
                => throw new NotImplementedException();
            """));

        // (6) The Graph plumbing layer FORWARDS the path it was handed and originates nothing, so it is
        //     excluded. This is the shape SpeFileStore really uses; without the exclusion the rule fires on
        //     the facade itself (verified — it did, at SpeFileStore.cs:75/259/284 before the exclusion was
        //     added) and would push a sanitizer INTO the layer, breaking OBO's legitimate sub-path contract.
        Assert.Empty(ScanArgumentsInText("Infrastructure/Graph/Fake/L.cs", """
            public Task<FileHandleDto?> UploadSmallAsync(string driveId, string path, Stream content, CancellationToken ct = default)
                => _driveItemOps.UploadSmallAsync(driveId, path, content, ct);
            """));

        //     …and the exclusion must be a real one: the same forwarding shape OUTSIDE the plumbing layer
        //     is still a violation, so the exclusion is not silently swallowing the whole tree.
        Assert.NotEmpty(ScanArgumentsInText("Services/Fake/M.cs", """
            var h = await _driveItemOps.UploadSmallAsync(driveId, path, content, ct);
            """));

        // (7) THE REAL PRODUCTION TREE — the control that actually matters. A synthetic control can pass
        //     while the rule still flags shipped code.
        Assert.Empty(ScanArgumentsInTree());
    }

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

    // -------------------------------------------------------------------------------------------------
    // RULE 2 MACHINERY. Crude by design (arch-fitness scanning, not compilation) — adequate BECAUSE the
    // two controls above pin the detector's behaviour in each direction, including against the real tree.
    // -------------------------------------------------------------------------------------------------

    /// <summary>A call to a path-taking sink, capturing the sink name and the offset of its open paren.</summary>
    private static readonly Regex SinkCallWithArgs = new(
        @"(?<![A-Za-z0-9_])(?<sink>UploadSmallAsync|UploadSmallAsUserAsync|CreateUploadSessionAsUserAsync)\s*\(",
        RegexOptions.Compiled);

    /// <summary>A local declaration, capturing its name and its whole initializer up to the statement end.
    /// Deliberately permissive on the initializer — it only ever gets substring-searched for the sanitizer,
    /// never parsed.</summary>
    private static readonly Regex LocalDeclaration = new(
        @"(?:var|string\??)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<init>[^;]*);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static List<string> ScanArgumentsInTree()
        => ScannedFiles()
            .SelectMany(f => ScanArgumentsInText(Relative(f), File.ReadAllText(f)))
            .ToList();

    private static List<string> InspectedSinkCalls()
    {
        var seen = new List<string>();

        foreach (var file in ScannedFiles())
        {
            var relative = Relative(file);
            if (FilesWhoseCallerSuppliesThePath.Contains(relative) || IsPlumbingLayer(relative))
            {
                continue;
            }

            var text = Decomment(File.ReadAllText(file));

            foreach (var call in SinkCallsIn(text))
            {
                if (!call.IsDeclaration)
                {
                    seen.Add($"{relative}:{LineOf(text, call.Index)}: {call.Sink}");
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Every sink call in one file whose path argument is not a local sanitized through the ONE sanitizer.
    /// </summary>
    private static List<string> ScanArgumentsInText(string relativeFile, string rawText)
    {
        var violations = new List<string>();

        // Two exclusions, each with its reason written at the constant:
        //   · the OBO surface legitimately accepts a caller-supplied MULTI-SEGMENT path and validates it
        //     segment-by-segment instead of sanitizing (SpeUploadPath.IsSafeSegment);
        //   · the Graph plumbing layer only FORWARDS a path parameter, so it originates nothing to sanitize.
        if (FilesWhoseCallerSuppliesThePath.Contains(relativeFile) || IsPlumbingLayer(relativeFile))
        {
            return violations;
        }

        var text = Decomment(rawText);

        foreach (var call in SinkCallsIn(text))
        {
            // The facade DECLARES these methods; declaring one is not calling one.
            if (call.IsDeclaration)
            {
                continue;
            }

            var line = LineOf(text, call.Index);
            var index = PathArgumentIndex[call.Sink];

            if (call.Arguments.Count <= index)
            {
                violations.Add(
                    $"{relativeFile}:{line}: {call.Sink} has {call.Arguments.Count} argument(s), so its "
                    + $"path argument (#{index}) could not be read — the scanner or the signature has drifted");
                continue;
            }

            var argument = call.Arguments[index].Trim();

            // (1) The argument must be a bare local. A member access or a literal is a pass-through, and a
            //     pass-through is what nobody can grep for later.
            if (!Regex.IsMatch(argument, @"^[A-Za-z_]\w*$"))
            {
                violations.Add(
                    $"{relativeFile}:{line}: {call.Sink} receives `{argument}` as its path — not a local. "
                    + "Assign it to a *Path local sanitized through SpeUploadPath.SanitizeFileName first");
                continue;
            }

            if (!IsPathShapedName(argument))
            {
                violations.Add(
                    $"{relativeFile}:{line}: {call.Sink} receives `{argument}`, whose name does not end in "
                    + "'path'. The value IS the whole upload path, not a file name — name it so, and "
                    + "sanitize it through SpeUploadPath.SanitizeFileName");
                continue;
            }

            // (2) That local's initializer must run the value through the ONE sanitizer.
            var initializer = InitializerOf(text, argument);

            if (initializer is null)
            {
                violations.Add(
                    $"{relativeFile}:{line}: {call.Sink} receives `{argument}`, but no local declaration of "
                    + "that name was found in this file — the scanner cannot attest it is sanitized. "
                    + "Declare it at the call site");
                continue;
            }

            if (!initializer.Contains(Sanitizer, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{relativeFile}:{line}: {call.Sink} receives `{argument}`, initialized as "
                    + $"`{Collapse(initializer)}` — never sanitized. A file name IS the whole upload path");
            }
        }

        return violations;
    }

    private sealed record SinkCall(string Sink, int Index, bool IsDeclaration, IReadOnlyList<string> Arguments);

    private static IEnumerable<SinkCall> SinkCallsIn(string text)
    {
        foreach (Match m in SinkCallWithArgs.Matches(text))
        {
            var openParen = m.Index + m.Length - 1;
            var arguments = SplitTopLevelArguments(ArgumentListAt(text, openParen));

            yield return new SinkCall(
                m.Groups["sink"].Value,
                m.Index,
                IsDeclaration: LooksLikeDeclaration(arguments),
                arguments);
        }
    }

    /// <summary>
    /// A DECLARATION's arguments are "Type name" pairs; a CALL's are bare expressions. Detecting on
    /// "every argument has a space-separated type" is crude, but it is exactly the distinction that
    /// matters and it is pinned by positive control (5).
    /// </summary>
    private static bool LooksLikeDeclaration(IReadOnlyList<string> arguments)
        => arguments.Count > 0
           && arguments.All(a => Regex.IsMatch(
               a.Trim(),
               @"^(?:(?:this|ref|out|in|params)\s+)?[A-Za-z_][\w\.<>,\[\]\?]*\s+[A-Za-z_]\w*(?:\s*=\s*[^,]+)?$"));

    /// <summary>The raw text between the parens of the call whose '(' is at <paramref name="openParen"/>.</summary>
    private static string ArgumentListAt(string text, int openParen)
    {
        var depth = 0;
        var i = openParen;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '"' || c == '\'')
            {
                i = SkipStringLiteral(text, i);
                continue;
            }

            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return text[(openParen + 1)..i];
                }
            }

            i++;
        }

        return string.Empty; // unbalanced — treated as "no arguments", which fails loudly rather than quietly
    }

    /// <summary>Splits an argument list on commas that are not nested inside parens/brackets/braces/strings.</summary>
    private static List<string> SplitTopLevelArguments(string argumentList)
    {
        var arguments = new List<string>();
        var depth = 0;
        var start = 0;
        var i = 0;

        while (i < argumentList.Length)
        {
            var c = argumentList[i];

            if (c == '"' || c == '\'')
            {
                i = SkipStringLiteral(argumentList, i);
                continue;
            }

            if (c is '(' or '[' or '{') { depth++; }
            else if (c is ')' or ']' or '}') { depth--; }
            else if (c == ',' && depth == 0)
            {
                arguments.Add(argumentList[start..i]);
                start = i + 1;
            }

            i++;
        }

        var tail = argumentList[start..];
        if (!string.IsNullOrWhiteSpace(tail) || arguments.Count > 0)
        {
            arguments.Add(tail);
        }

        return arguments;
    }

    /// <summary>
    /// The initializer of the LAST declaration of <paramref name="name"/> in the file, or null when the name
    /// is never declared locally here. Last rather than first because a method may declare the same path
    /// local in more than one branch and the later one is the one a reader lands on; either way, a site
    /// where ANY declaration is unsanitized is a site worth reporting, so the check below is applied to the
    /// concatenation of all of them.
    /// </summary>
    private static string? InitializerOf(string text, string name)
    {
        var initializers = LocalDeclaration
            .Matches(text)
            .Where(m => string.Equals(m.Groups["name"].Value, name, StringComparison.Ordinal))
            .Select(m => m.Groups["init"].Value)
            .ToList();

        if (initializers.Count == 0)
        {
            return null;
        }

        // Every declaration of the name must be sanitized, so report the first that is not.
        return initializers.FirstOrDefault(i => !i.Contains(Sanitizer, StringComparison.Ordinal))
               ?? initializers[0];
    }

    private static string Collapse(string value)
    {
        var single = Regex.Replace(value, @"\s+", " ").Trim();
        return single.Length <= 90 ? single : single[..90] + "…";
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
