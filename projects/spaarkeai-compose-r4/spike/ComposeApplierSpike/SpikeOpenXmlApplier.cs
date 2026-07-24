using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Sprk.Bff.Api.Services.Compose.Operations;

namespace ComposeApplierSpike;

/// <summary>
/// CANDIDATE B (task 005) — a build-on-Open-XML-SDK applier that consumes the REAL task-003
/// <see cref="ComposeOperation"/> schema and lands native w:ins / w:del at a
/// (paraId, runIndex, run-local-offset) anchor with ZERO write-path text-search.
///
/// This is a THROWAWAY spike proving the hardest R4 surface (offset -> run split + native
/// w:ins/w:del at an interior location) before Phase 3 commits. It encodes the
/// notes/bridge-prior-art.md finding #6 recipe: resolve w:p by w14:paraId (O(1) dictionary,
/// never text-search) -> index runs within the paragraph -> Run.Clone()-split the target run at
/// the run-local offset preserving RunProperties -> wrap inserts in InsertedRun(w:ins) / deletes
/// in DeletedRun(w:del) with Author/Date/Id -> mutate the OpenXml DOM, never string-edit
/// document.xml. Para-mark deletion is emitted via w:pPr/w:rPr/w:del.
///
/// byte[]-in / byte[]-out (ADR-007); no Microsoft.Graph type; no AI internals (ADR-013).
/// </summary>
public static class SpikeOpenXmlApplier
{
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    private static readonly string Author = "Spaarke Compose (spike)";
    private static readonly DateTime When = new(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);

    public static byte[] Apply(byte[] sourceDocx, ComposeOperationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (sourceDocx is null || sourceDocx.Length == 0)
        {
            throw new ArgumentException("sourceDocx required", nameof(sourceDocx));
        }

        using var buffer = new MemoryStream();
        buffer.Write(sourceDocx, 0, sourceDocx.Length);
        buffer.Position = 0;

        using (var doc = WordprocessingDocument.Open(buffer, isEditable: true))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            var byParaId = BuildParaIdIndex(body);      // O(1) resolve; NEVER text-search
            var idCounter = SeedRevisionId(body);

            foreach (var op in log.Operations)
            {
                var para = ResolveParagraph(byParaId, op.ParaId);
                switch (op)
                {
                    case InsertTextOperation ins:
                        ApplyInsertText(para, ins, ref idCounter);
                        break;
                    case DeleteRangeOperation del:
                        ApplyDeleteRange(para, del, ref idCounter);
                        break;
                    case DeleteParagraphOperation:
                        ApplyDeleteParagraph(para, ref idCounter);   // para-mark probe
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Spike does not implement {op.GetType().Name}; production engine (task 030) covers the full set.");
                }
            }

            doc.MainDocumentPart.Document.Save();
        }

        return buffer.ToArray();
    }

    // -- Anchor resolution (paraId; zero text-search) -------------------------------------------

    private static Dictionary<string, Paragraph> BuildParaIdIndex(Body body)
    {
        var map = new Dictionary<string, Paragraph>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in body.Descendants<Paragraph>())
        {
            var pid = p.GetAttributes().FirstOrDefault(a => a.LocalName == "paraId" && a.NamespaceUri == W14).Value;
            if (!string.IsNullOrEmpty(pid) && !map.ContainsKey(pid))
            {
                map[pid] = p;
            }
        }

        return map;
    }

    private static Paragraph ResolveParagraph(Dictionary<string, Paragraph> index, string paraId) =>
        index.TryGetValue(paraId, out var p)
            ? p
            : throw new InvalidOperationException($"paraId {paraId} not found (no text-search fallback in the write path).");

    // -- insertText -----------------------------------------------------------------------------

    private static void ApplyInsertText(Paragraph para, InsertTextOperation op, ref int idCounter)
    {
        var runs = para.Elements<Run>().ToList();
        if (op.At.RunIndex < 0 || op.At.RunIndex >= runs.Count)
        {
            throw new InvalidOperationException($"runIndex {op.At.RunIndex} out of range in paraId {op.ParaId}.");
        }

        var target = runs[op.At.RunIndex];
        var (left, right) = SplitRunAtOffset(target, op.At.Offset);

        var ins = new InsertedRun { Id = NextId(ref idCounter).ToString(), Author = Author, Date = When };
        ins.AppendChild(BuildRun(target, op.Text));

        // Insert the tracked run at the split boundary. left is the run that now ends at the offset.
        (left ?? target).InsertAfterSelf(ins);
    }

    // -- deleteRange (intra-paragraph run-local) ------------------------------------------------

    private static void ApplyDeleteRange(Paragraph para, DeleteRangeOperation op, ref int idCounter)
    {
        var start = op.Range.Start;
        var end = op.Range.End;
        if (start.RunIndex != end.RunIndex)
        {
            // Spike scope: the CIPO acceptance range is intra-run. Multi-run spans decompose into
            // a per-run w:del sweep in the production engine (task 030); flagged, not implemented here.
            throw new NotSupportedException("Spike deleteRange is intra-run; multi-run sweep is task 030.");
        }

        var runs = para.Elements<Run>().ToList();
        var target = runs[start.RunIndex];

        // Isolate [start.Offset, end.Offset) into its own run: split at end first, then at start.
        SplitRunAtOffset(target, end.Offset);
        var (leftOfStart, mid) = SplitRunAtOffset(target, start.Offset);
        var deleteRun = mid ?? target; // the middle run now carries exactly the range text
        _ = leftOfStart;

        WrapRunAsDeleted(deleteRun, ref idCounter);
    }

    // -- deleteParagraph (content w:del + para-mark w:del probe) ---------------------------------

    private static void ApplyDeleteParagraph(Paragraph para, ref int idCounter)
    {
        foreach (var run in para.Elements<Run>().ToList())
        {
            if (run.GetFirstChild<Text>() is null)
            {
                continue;
            }

            WrapRunAsDeleted(run, ref idCounter);
        }

        // Para-mark deletion (bridge-prior-art #6 hardest edge): w:del inside w:pPr/w:rPr.
        var pPr = para.GetFirstChild<ParagraphProperties>();
        if (pPr is null)
        {
            pPr = new ParagraphProperties();
            para.PrependChild(pPr);
        }

        var markProps = pPr.GetFirstChild<ParagraphMarkRunProperties>();
        if (markProps is null)
        {
            markProps = new ParagraphMarkRunProperties();
            pPr.AppendChild(markProps);
        }

        markProps.PrependChild(new Deleted { Id = NextId(ref idCounter).ToString(), Author = Author, Date = When });
    }

    // -- run surgery ----------------------------------------------------------------------------

    /// <summary>
    /// Splits <paramref name="run"/>'s single w:t at run-local <paramref name="offset"/>, preserving
    /// RunProperties on BOTH halves (Run.Clone()). Returns (leftRun, rightRun): the left run ends at
    /// the offset, the right run begins at it. offset==0 -> (null, run); offset==len -> (run, null).
    /// Mutates the DOM in place; never string-edits document.xml.
    /// </summary>
    private static (Run? Left, Run? Right) SplitRunAtOffset(Run run, int offset)
    {
        var textEl = run.GetFirstChild<Text>();
        var text = textEl?.Text ?? string.Empty;
        if (offset <= 0)
        {
            return (null, run);
        }

        if (offset >= text.Length)
        {
            return (run, null);
        }

        var rightRun = (Run)run.CloneNode(true);
        rightRun.GetFirstChild<Text>()!.Text = text[offset..];
        rightRun.GetFirstChild<Text>()!.Space = SpaceProcessingModeValues.Preserve;

        textEl!.Text = text[..offset];
        textEl.Space = SpaceProcessingModeValues.Preserve;

        run.InsertAfterSelf(rightRun);
        return (run, rightRun);
    }

    private static void WrapRunAsDeleted(Run run, ref int idCounter)
    {
        var text = run.GetFirstChild<Text>();
        if (text is null)
        {
            return;
        }

        var inner = new Run();
        if (run.GetFirstChild<RunProperties>() is { } rpr)
        {
            inner.AppendChild((RunProperties)rpr.CloneNode(true));
        }

        // EDGE-4: inside w:del text is DeletedText (w:delText), never w:t.
        inner.AppendChild(new DeletedText(text.Text) { Space = SpaceProcessingModeValues.Preserve });

        var del = new DeletedRun { Id = NextId(ref idCounter).ToString(), Author = Author, Date = When };
        del.AppendChild(inner);
        run.Parent!.ReplaceChild(del, run);
    }

    private static Run BuildRun(Run template, string text)
    {
        var r = new Run();
        if (template.GetFirstChild<RunProperties>() is { } rpr)
        {
            r.AppendChild((RunProperties)rpr.CloneNode(true));
        }

        r.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return r;
    }

    // -- revision ids ---------------------------------------------------------------------------

    private static int NextId(ref int counter) => ++counter;

    private static int SeedRevisionId(Body body)
    {
        var max = 0;
        foreach (var v in body.Descendants<InsertedRun>().Select(x => x.Id?.Value)
                     .Concat(body.Descendants<DeletedRun>().Select(x => x.Id?.Value)))
        {
            if (int.TryParse(v, out var n) && n > max)
            {
                max = n;
            }
        }

        return max;
    }
}
