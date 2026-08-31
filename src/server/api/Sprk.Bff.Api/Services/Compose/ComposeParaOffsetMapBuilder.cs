// Task 071 (Track D) — the INTRA-PARAGRAPH OFFSET-ADDRESSING TABLE, extracted from
// `ComposeDocxProjectionBuilder`.
//
// WHY THIS IS ITS OWN COMPONENT. It answers one question, and it is not "what does this document look
// like?": given a paraId and an EDITOR-VISIBLE offset within that paragraph, which OOXML run does that
// offset fall in, and at what run-local position? That is the D2 fine anchor's resolver — the thing
// that lets an edit be placed without a text search (invariant I-7).
//
// Its correctness bar is agreement with a SECOND walk it does not own: `ComposeShadowPatchEngine`
// mirrors this exact run flatten when it applies a patch, and the two must enumerate runs identically
// or an offset resolves to the wrong run and the edit lands in the wrong place. That mirrored-pair
// obligation is this component's reason to change, and it is independent of the HTML projection that
// happens to call it.
//
// `RunEditorLength` moved with it rather than staying with the HTML pipeline: it defines the OFFSET
// SPACE the table is expressed in (how many editor-visible characters a run contributes), so splitting
// it from the table would put a measurement and its unit in two components that must agree.
//
// Extraction is behaviour-preserving — bodies moved verbatim, equivalence proven empirically over the
// whole corpus by the task-071 projection oracle.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

internal static class ComposeParaOffsetMapBuilder
{
    /// <summary>
    /// Builds one paragraph's intra-paragraph offset-addressing entry: the ordered editor-visible run
    /// flatten and each run's editor-offset boundary. The descent MIRRORS <c>ComposeDocxProjectionBuilder.RenderInline</c> exactly
    /// (into <c>w:hyperlink</c>, <c>w:ins</c>, <c>w:del</c>, <c>w:sdt</c>) so the run sequence and per-run
    /// character length match the editor-visible text the projection emits — the offset space the client
    /// measures over. Runs inside pre-existing tracked changes are included (their text is editor-visible)
    /// and tagged with their <see cref="RunTrackChange"/> context. Pure — reads run text lengths only, emits
    /// no document content. Static so it is trivially unit-testable in isolation.
    /// </summary>
    internal static ParaOffsetMap BuildParaOffsetMap(Paragraph paragraph, string paraId)
    {
        var runs = new List<RunBoundary>();
        var runIndex = 0;
        var cumOffset = 0;
        CollectRunBoundaries(paragraph, RunTrackChange.None, runs, ref runIndex, ref cumOffset);
        return new ParaOffsetMap { ParaId = paraId, Runs = runs };
    }

    private static void CollectRunBoundaries(
        OpenXmlElement container, RunTrackChange trackChange, List<RunBoundary> runs, ref int runIndex, ref int cumOffset)
    {
        // FR-02 (task 012): field-scan state is scoped to THIS container invocation only — a
        // w:fldChar begin/instrText/separate/result/end sequence is assumed to be direct siblings (see
        // ComposeOoxmlPrimitives.FieldScanState remarks; not exercised by the corpus, documented simplification).
        var field = new ComposeOoxmlPrimitives.FieldScanState();
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run r:
                    if (ComposeOoxmlPrimitives.TryAdvanceFieldScan(r, field, out var fieldClosed))
                    {
                        if (fieldClosed)
                        {
                            // The outermost w:fldChar end just closed — emit ONE atom spanning the whole
                            // field, length = its cached RESULT text (never its instrText field code).
                            var atomLen = ComposeOoxmlPrimitives.ExtractRunsDisplayText(field.ResultRuns).Length;
                            runs.Add(new RunBoundary(runIndex, cumOffset, atomLen, trackChange, ComposeAtomKind.Field));
                            runIndex++;
                            cumOffset += atomLen;
                            field.Reset();
                        }
                        break; // consumed as part of the field span either way
                    }
                    if (ComposeOoxmlPrimitives.IsComplexObjectRun(r))
                    {
                        // A drawing/embedded-object run occupies one non-editable atom position — never
                        // opened, so it never silently vanishes from the offset space (I-4).
                        runs.Add(new RunBoundary(runIndex, cumOffset, 1, trackChange, ComposeAtomKind.ComplexObject));
                        runIndex++;
                        cumOffset += 1;
                        break;
                    }
                    var normalLen = RunEditorLength(r);
                    runs.Add(new RunBoundary(runIndex, cumOffset, normalLen, trackChange));
                    runIndex++;
                    cumOffset += normalLen;
                    break;
                case SimpleField sf:
                    // w:fldSimple — its cached display value becomes the atom's content; the field's own
                    // run structure is never reinterpreted as separately editable runs.
                    var sfLen = ComposeOoxmlPrimitives.ExtractAtomDisplayText(sf).Length;
                    runs.Add(new RunBoundary(runIndex, cumOffset, sfLen, trackChange, ComposeAtomKind.Field));
                    runIndex++;
                    cumOffset += sfLen;
                    break;
                case Hyperlink h:
                    CollectRunBoundaries(h, trackChange, runs, ref runIndex, ref cumOffset);
                    break;
                case InsertedRun ins:
                    CollectRunBoundaries(ins, RunTrackChange.Inserted, runs, ref runIndex, ref cumOffset);
                    break;
                case DeletedRun del:
                    CollectRunBoundaries(del, RunTrackChange.Deleted, runs, ref runIndex, ref cumOffset);
                    break;
                case SdtRun sdtRun:
                    if (ComposeOoxmlPrimitives.IsSpecialSdtControl(sdtRun.SdtProperties))
                    {
                        // An inline content control with a genuinely non-text declared type is an atom.
                        var sdtLen = ComposeOoxmlPrimitives.ExtractAtomDisplayText(sdtRun).Length;
                        runs.Add(new RunBoundary(runIndex, cumOffset, sdtLen, trackChange, ComposeAtomKind.Sdt));
                        runIndex++;
                        cumOffset += sdtLen;
                    }
                    else
                    {
                        var sdtContent = sdtRun.GetFirstChild<SdtContentRun>();
                        if (sdtContent is not null) CollectRunBoundaries(sdtContent, trackChange, runs, ref runIndex, ref cumOffset);
                    }
                    break;
                default:
                    // ParagraphProperties, bookmarks, proofErr, etc. — no editor-visible run.
                    break;
            }
        }
    }

    /// <summary>
    /// The number of editor-visible characters a run contributes to the paragraph offset space: its
    /// <c>w:t</c>/<c>w:delText</c> text length, plus one per <c>w:br</c>/<c>w:cr</c>/<c>w:tab</c>/
    /// <c>w:noBreakHyphen</c>/<c>w:sym</c> glyph — mirroring exactly what <c>ComposeDocxProjectionBuilder.RenderRun</c> emits
    /// (each maps to one editor position). A <c>w:sym</c> contributes exactly 1 regardless of whether it
    /// resolves to a mapped Unicode glyph or an unmapped placeholder (FR-06/FR-10) — both are ONE
    /// editor-visible character, so the offset table never diverges from the HTML render either way.
    /// </summary>
    private static int RunEditorLength(Run run)
    {
        var length = 0;
        foreach (var child in run.Elements())
        {
            switch (child)
            {
                case Text t:
                    length += t.Text?.Length ?? 0;
                    break;
                case DeletedText dt:
                    length += dt.Text?.Length ?? 0;
                    break;
                case Break:
                case TabChar:
                case NoBreakHyphen:
                case CarriageReturn:
                case SymbolChar:
                case PositionalTab:
                    length += 1;
                    break;
                case Ruby ruby:
                    // Task 022 WS-2 construct audit: the base text RenderRun now emits — kept length-aligned
                    // with the offset-addressing table per this file's parallel-walk invariant.
                    length += ComposeOoxmlPrimitives.ExtractRunsDisplayText(ComposeOoxmlPrimitives.RubyBaseRuns(ruby)).Length;
                    break;
                default:
                    break;
            }
        }

        return length;
    }
}
