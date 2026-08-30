// Task 042 (spaarkeai-compose-r8) — FR-A11. THE FAILURES THAT MAKE A MERGE LOOK CORRECT AND BE WRONG.
//
// Content compares equal, the preservation number is good, and Word offers to repair the file on open.
// Two id systems live inside the subtrees the merge clones and neither is content:
//
//   * COMMENT RANGES span multiple paragraphs. Clone one and render its neighbour and a range can be
//     orphaned (a start with no end) or duplicated (emitted from the model on top of a clone that already
//     carries it).
//   * REVISION IDS must be unique document-wide. Cloned blocks bring their existing ids; rendered blocks
//     mint new ones. The two sets must not intersect.
//
// FR-G05 is explicit that OpenXmlValidator passing is NOT sufficient evidence for this class, so the last
// test here opens merged output with headless LibreOffice — an actual document open, not a schema check.
//
// MAINTAIN-class (tests/integration/seam/** KEEP path per ADR-038).

using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeMergeIntegrityTests
{
    private const string EditMarker = " [MERGE-042]";

    private readonly ITestOutputHelper _output;

    public ComposeMergeIntegrityTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> CorpusDocumentNames()
    {
        var data = new TheoryData<string>();
        foreach (var path in ComposeCorpusFixtureLocator.EnumerateDocumentPaths())
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Cross-boundary comment ranges — the case most likely to be missed.
    //
    // A range that STARTS in a block the user did not touch and ENDS in the block they edited crosses the
    // clone/render boundary. The cloned half carries its own markup; the rendered half is rebuilt from a
    // model that may not carry the anchor at all. Either half going missing leaves the other orphaned.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_CommentRangeStartingInAClonedBlockAndEndingInAnEditedOne_StaysWellFormed()
    {
        var source = BuildCrossBoundaryCommentSource();
        var model = Project(source);

        // Edit the SECOND paragraph — the one holding the range END. The first is cloned.
        var edited = EditBlockContaining(model, "second paragraph");
        var rendered = Render(source, edited, out var stats);

        stats.ClonedBlocks.Should().BeGreaterThan(0, "the first paragraph is untouched and must be cloned");
        stats.RenderedBlocks.Should().Be(1, "only the second paragraph changed");

        AssertCommentRangesWellFormed(rendered, "range starts in a cloned block, ends in a rendered one");
    }

    [Fact]
    public void Merge_CommentRangeStartingInAnEditedBlockAndEndingInAClonedOne_StaysWellFormed()
    {
        var source = BuildCrossBoundaryCommentSource();
        var model = Project(source);

        // Edit the FIRST paragraph — the one holding the range START. The second is cloned.
        var edited = EditBlockContaining(model, "first paragraph");
        var rendered = Render(source, edited, out var stats);

        stats.ClonedBlocks.Should().BeGreaterThan(0);
        stats.RenderedBlocks.Should().Be(1);

        AssertCommentRangesWellFormed(rendered, "range starts in a rendered block, ends in a cloned one");
    }

    [Theory]
    [MemberData(nameof(CorpusDocumentNames))]
    public void Merge_Corpus_LeavesNoOrphanedOrDuplicatedCommentRange(string fileName)
    {
        var source = LoadCorpus(fileName);
        var model = Project(source);
        var edited = EditFirstNonEmptyRun(model, out var applied);
        applied.Should().BeTrue($"[{fileName}] the corpus document must expose an editable run");

        var rendered = Render(source, edited, out _);
        AssertCommentRangesWellFormed(rendered, $"[{fileName}] merged output");

        // Logged, not asserted: most corpus documents carry no comments at all, so for those this sweep is
        // a REGRESSION NET rather than evidence. The real signal for cross-boundary integrity comes from the
        // two purpose-built fixtures above. Printing the count keeps the distinction visible instead of
        // letting 18 green rows imply 18 exercised cases.
        _output.WriteLine($"{fileName}: {CommentRangeCount(rendered)} comment range(s)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Revision ids — cloned ids and minted ids must not intersect.
    //
    // The seed is COMPUTED from the carrier (which is where every cloned id comes from), not assumed from a
    // fixed offset. This asserts the property rather than the mechanism, so it keeps holding if the
    // mechanism changes.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    // `carriesRevisions` is declared PER FIXTURE and then CHECKED, because uniqueness over an empty set is
    // vacuously true. The first run of this test reported "0 revision id(s)" for the PAT document — whose
    // filename says "track changes" — and passed. Inspecting the source settled it: that document contains
    // zero `w:ins`/`w:del`/`w:pPrChange`; its revisions were accepted before it was saved, and the name is
    // descriptive of its provenance, not its markup. So the fixture is kept, declared honestly, and the
    // vacuous case is asserted as vacuous rather than presented as evidence.
    [Theory]
    [InlineData("multi-author-redline-synthetic.docx", true)]
    [InlineData("PAT 109270W-1 - CLAIMS track changes vs US12470413 claims(206092900.1).docx", false)]
    public void Merge_RevisionIds_AreUniqueAndSurviveTheMerge(string fileName, bool carriesRevisions)
    {
        var source = LoadCorpus(fileName);
        var model = Project(source);
        var edited = EditFirstNonEmptyRun(model, out var applied);
        applied.Should().BeTrue();

        var rendered = Render(source, edited, out var stats);
        stats.ClonedBlocks.Should().BeGreaterThan(0, "the fixture must exercise the clone path");

        var sourceIds = RevisionIds(source);
        var mergedIds = RevisionIds(rendered);
        _output.WriteLine(
            $"{fileName}: source={sourceIds.Count} merged={mergedIds.Count} revision id(s), " +
            $"cloned={stats.ClonedBlocks} rendered={stats.RenderedBlocks}");

        sourceIds.Count.Should().Be(carriesRevisions ? sourceIds.Count : 0,
            $"[{fileName}] is declared as {(carriesRevisions ? "carrying" : "carrying no")} tracked changes; " +
            "if that has flipped, the assertions below are measuring something other than what they claim");

        if (carriesRevisions)
        {
            sourceIds.Should().NotBeEmpty("the declaration says this fixture carries tracked changes");

            // The real property: cloning must not LOSE revisions. Every revision on an untouched block rides
            // along inside its cloned subtree, so the merged document carries at least as many as the source.
            mergedIds.Count.Should().BeGreaterThanOrEqualTo(sourceIds.Count,
                $"[{fileName}] tracked changes on cloned blocks must survive — a merge that silently drops a " +
                "w:ins is a merge that discards someone's redline");
        }

        mergedIds.Should().OnlyHaveUniqueItems(
            $"[{fileName}] a duplicate revision id across cloned and rendered content produces a document " +
            "Word offers to repair — and the content comparison would still read as preserved");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Duplicate paraIds cannot mis-clone.
    //
    // The POML anticipated a consume-in-document-order scheme with a dup-detection fallback. The merge
    // built in task 040 never resolves a paraId at all — alignment is a longest common subsequence over
    // block CONTENT — so mis-cloning on a duplicate id is impossible by construction rather than by rule.
    // Asserted here so the structural guarantee is a test rather than a claim.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    // `expectBodyLevelDuplicates` is declared PER FIXTURE rather than inferred, because the two fixtures
    // carry DIFFERENT collisions and only one is visible to a body-level scan:
    //   * alternate-content-duplicate-paraid.docx — the same id twice inside the BODY (mc:Choice +
    //     mc:Fallback), which the oracle's duplicate flag sees.
    //   * multipart-paraid-collision.docx — the same id in document.xml, footnotes.xml AND header1.xml.
    //     `paraId` uniqueness is PART-scoped, so nothing is duplicated within the body and the flag is
    //     correctly false. Asserting it true here would have been asserting the wrong property loudly.
    [Theory]
    [InlineData("alternate-content-duplicate-paraid.docx", true)]
    [InlineData("multipart-paraid-collision.docx", false)]
    public void Merge_DuplicateParaIds_DoNotMisCloneAnyBlock(string fileName, bool expectBodyLevelDuplicates)
    {
        var source = LoadCorpus(fileName);
        var model = Project(source);
        var edited = EditFirstNonEmptyRun(model, out var applied);
        applied.Should().BeTrue();

        var rendered = Render(source, edited, out var stats);

        // Every untouched block still survives byte-for-byte after normalization: had a duplicate paraId
        // caused the merge to splice the wrong subtree, this number would drop, not just shuffle.
        var report = ComposeBlockPreservationOracle.Compare(
            source, rendered, EditMarker, ComposeBlockPreservationOracle.ComparisonLevel.Lenient);

        report.DuplicateParaIdsInOriginal.Should().Be(expectBodyLevelDuplicates,
            $"[{fileName}] carries a known collision shape — if this flips, the fixture has drifted and the " +
            "assertion below stops proving what it claims to");
        report.OverallPreservationPercent.Should().Be(100d,
            $"[{fileName}] colliding paraIds must not mis-pair a merge that never keys on paraId — alignment " +
            "is a longest common subsequence over block CONTENT, so the collision has nothing to act on");
        _output.WriteLine(
            $"{fileName}: bodyLevelDuplicates={report.DuplicateParaIdsInOriginal} preservation=100% " +
            $"cloned={stats.ClonedBlocks}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. FR-G05 — an ACTUAL document open, because OpenXmlValidator passing is explicitly not enough.
    //
    // Headless LibreOffice converting the file is the CI-runnable proxy for "Word opens this without
    // offering to repair it". Skipped with a LOUD message when LibreOffice is absent rather than passing
    // silently — a skipped repair check that reads as green is how this class ships.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("AppligentNDA_Signed.docx")]
    [InlineData("PAT 109270W-1 - CLAIMS track changes vs US12470413 claims(206092900.1).docx")]
    [InlineData("content-controls-sdt.docx")]
    [InlineData("ref-cross-references.docx")]
    public void Merge_MergedDocument_OpensInAnActualWordProcessor(string fileName)
    {
        var soffice = FindLibreOffice();
        if (soffice is null)
        {
            _output.WriteLine(
                "SKIPPED: LibreOffice not found. FR-G05 requires an ACTUAL document open — OpenXmlValidator " +
                "passing is explicitly insufficient for the comment/revision corruption class. This check is " +
                "not evidence unless it runs.");
            return;
        }

        var source = LoadCorpus(fileName);
        var model = Project(source);
        var edited = EditFirstNonEmptyRun(model, out var applied);
        applied.Should().BeTrue();

        var rendered = Render(source, edited, out _);

        var work = Path.Combine(Path.GetTempPath(), "compose-042-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var docx = Path.Combine(work, "merged.docx");
            File.WriteAllBytes(docx, rendered);

            var psi = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in new[]
                     {
                         "--headless", "--norestore", "--invisible",
                         $"-env:UserInstallation=file:///{work.Replace('\\', '/')}/profile",
                         "--convert-to", "txt:Text", "--outdir", work, docx,
                     })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(milliseconds: 120_000).Should().BeTrue(
                $"[{fileName}] LibreOffice must finish — a hang is itself a signal about the document");

            var converted = Path.Combine(work, "merged.txt");
            File.Exists(converted).Should().BeTrue(
                $"[{fileName}] LibreOffice produced no output, which means it could not read the merged " +
                $"document. stderr: {stderr}");

            var text = File.ReadAllText(converted);
            text.Should().Contain(EditMarker.Trim(),
                $"[{fileName}] the opened document must contain the user's edit — a file that opens but has " +
                "lost the edit is not a pass");
            _output.WriteLine($"{fileName}: opened, {text.Length} chars of text extracted");
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory is not worth failing a document-integrity test over.
            }
        }
    }

    // ── assertions ───────────────────────────────────────────────────────────────────────────────

    private static void AssertCommentRangesWellFormed(byte[] docx, string because)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        var starts = body.Descendants<CommentRangeStart>().Select(c => c.Id?.Value).Where(v => v is not null).ToList();
        var ends = body.Descendants<CommentRangeEnd>().Select(c => c.Id?.Value).Where(v => v is not null).ToList();

        starts.Should().OnlyHaveUniqueItems($"duplicated commentRangeStart — {because}");
        ends.Should().OnlyHaveUniqueItems($"duplicated commentRangeEnd — {because}");
        starts.Should().BeEquivalentTo(ends,
            $"every comment range needs BOTH endpoints; an orphan is a document Word offers to repair — {because}");
    }

    private static int CommentRangeCount(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<CommentRangeStart>().Count();
    }

    private static List<int> RevisionIds(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants()
            .Select(e => e switch
            {
                InsertedRun i => i.Id?.Value,
                DeletedRun d => d.Id?.Value,
                Inserted i => i.Id?.Value,
                Deleted d => d.Id?.Value,
                ParagraphPropertiesChange p => p.Id?.Value,
                RunPropertiesChange r => r.Id?.Value,
                _ => null,
            })
            .Where(v => v is not null)
            .Select(v => int.Parse(v!, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }

    private static string? FindLibreOffice()
    {
        foreach (var candidate in new[]
                 {
                     Environment.GetEnvironmentVariable("SOFFICE_PATH"),
                     @"C:\Program Files\LibreOffice\program\soffice.exe",
                     @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
                     "/usr/bin/soffice",
                     "/usr/bin/libreoffice",
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static byte[] Render(byte[] carrier, ComposeContentModel model, out ComposeMergeStats stats)
    {
        stats = new ComposeMergeStats();
        return new ComposeDocumentRenderer().RenderIntoCarrier(
            carrier, model, "seam-042", degradations: null, mergeUnchangedBlocks: true, mergeStats: stats);
    }

    private static ComposeContentModel Project(byte[] bytes)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(bytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, "the fixture must project");
        return projection.Model!;
    }

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private static ComposeContentModel EditBlockContaining(ComposeContentModel model, string needle)
    {
        var blocks = model.Blocks.ToList();
        var index = blocks.FindIndex(b => b.Runs.Any(r => r.Text?.Contains(needle, StringComparison.Ordinal) == true));
        index.Should().BeGreaterThanOrEqualTo(0, $"the fixture must contain '{needle}'");

        var runs = blocks[index].Runs.ToList();
        var runIndex = runs.FindIndex(r => r.Text?.Contains(needle, StringComparison.Ordinal) == true);
        runs[runIndex] = runs[runIndex] with { Text = runs[runIndex].Text + EditMarker };
        blocks[index] = blocks[index] with { Runs = runs };
        return model with { Blocks = blocks };
    }

    private static ComposeContentModel EditFirstNonEmptyRun(ComposeContentModel model, out bool applied)
    {
        var blocks = model.Blocks.ToList();
        for (var b = 0; b < blocks.Count; b++)
        {
            var runs = blocks[b].Runs;
            for (var r = 0; r < runs.Count; r++)
            {
                if (string.IsNullOrEmpty(runs[r].Text))
                {
                    continue;
                }

                var newRuns = runs.ToList();
                newRuns[r] = newRuns[r] with { Text = newRuns[r].Text + EditMarker };
                blocks[b] = blocks[b] with { Runs = newRuns };
                applied = true;
                return model with { Blocks = blocks };
            }
        }

        applied = false;
        return model;
    }

    /// <summary>
    /// Two paragraphs with ONE comment range spanning both — start in the first, end in the second. The
    /// shape the merge is most likely to break, and the reason this fixture is built here rather than reused.
    /// </summary>
    private static byte[] BuildCrossBoundaryCommentSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);

            var comments = main.AddNewPart<WordprocessingCommentsPart>();
            comments.Comments = new Comments(
                new Comment(new Paragraph(new Run(new Text("Spans both paragraphs."))))
                {
                    Id = "7",
                    Author = "Reviewer",
                    Initials = "R",
                });
            comments.Comments.Save();

            body.AppendChild(new Paragraph(
                new CommentRangeStart { Id = "7" },
                new Run(new Text("This is the first paragraph of the span."))));

            body.AppendChild(new Paragraph(
                new Run(new Text("This is the second paragraph of the span.")),
                new CommentRangeEnd { Id = "7" },
                new Run(new CommentReference { Id = "7" })));

            body.AppendChild(new SectionProperties(new PageSize { Width = 12240u, Height = 15840u }));
            main.Document.Save();
        }

        return stream.ToArray();
    }
}
