// Task 072 (Track D) — INLINE element construction, extracted from `ComposeDocumentRenderer`.
//
// WHY THIS IS ITS OWN COMPONENT. It answers one question: how does a model run become OOXML? Character
// formatting, Word fields (simple and the fldChar begin/instrText/separate/end sequence), tracked-change
// wrappers, hyperlinks, and the carried opaque spans/objects that must be re-emitted byte-for-byte. It
// changes when Track A widens what a run can carry — never when the body's block structure changes.
//
// ADR-049 I-5 — ONE BODY AUTHOR, and this is the extraction where that invariant is easiest to break by
// accident, so it is worth being precise about the line. Body children are the direct `w:p` / `w:tbl`
// children of `w:body`. Nothing here appends one: these members BUILD elements and return them, or
// append runs INTO a `w:p` the renderer already owns. The task's own wording draws the boundary the same
// way — collaborators "may build elements", only the renderer "writes body children".
//
// Deliberately NOT extracted alongside this: `ResolveHyperlinkRelationships`, which looks like it
// belongs here by subject matter but calls `Remove()` / `InsertBefore()` on the live body tree. It
// restructures authored content rather than building an element, so it stays with the author. Table
// construction stays for the same class of reason — `BuildTableCell` calls `RenderBlocks` back, so
// extracting it would make a collaborator drive the author.

using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

internal static class ComposeRunAuthor
{
    internal const int MaxFieldInstructionChars = 4096;

    /// <summary>
    /// Task 049 (FR-A10 residual): re-authors a carried Word field onto <paramref name="paragraph"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The FORM the document used is reproduced</b>, not normalised: <c>w:fldSimple</c> comes back
    /// as <c>w:fldSimple</c> and the <c>w:fldChar</c> run sequence as a run sequence. Word treats them as
    /// equivalent, but a save is not licensed to rewrite what the file contains just because the two render
    /// alike — the same reasoning that makes task 048 carry a symbol's code point instead of its glyph.</para>
    /// <para><b>The cached result is re-emitted with the field.</b> Word displays that until something asks
    /// the field to update, so the save is visually a no-op while the field becomes a field again. A field
    /// with no cached result is legal (Word shows the instruction's default) and simply has no result run.</para>
    /// <para><b>Nesting.</b> `w:ins`/`w:del` may not contain `w:fldSimple`, so the simple form puts the
    /// revision wrapper INSIDE, around its result run — which is what Word itself writes. The complex form
    /// is plain runs, so its wrapper goes outside them. Either way the field element is what the paragraph
    /// (or the hyperlink, which admits EG_PContent) receives.</para>
    /// </remarks>
    internal static bool AppendField(Paragraph paragraph, ComposeInlineRun run, ComposeField field, ComposeDocumentRenderer.ListRenderState state)
    {
        // Task 058: a NESTED field carries its own OOXML rather than an instruction, because it HAS no
        // single instruction — see ComposeField.SpanXml. Re-emitted verbatim, so the outer field, every
        // inner field, and both of their result runs come back as the document authored them. A span that
        // does not survive the gate falls through: SpanXml and Instruction are mutually exclusive, so the
        // guard below finds nothing and the caller flattens to the cached result — today's outcome, never a
        // reconstruction (ADR-049 invariant 1).
        if (!string.IsNullOrEmpty(field.SpanXml)
            && TryBuildCarriedFieldSpan(field.SpanXml, state) is { } spanParts)
        {
            AppendFieldParts(paragraph, spanParts, run, state);
            return true;
        }

        // CLIENT INPUT REACHING OOXML AUTHORING — the recurring review-finding class this file already
        // gates for revision attribution (021-F1 / 022-F1 / 024-F1), applied to the instruction. A posted
        // model is not necessarily one we projected: it can carry XML-illegal control characters (which
        // would make the saved package unopenable — an UNDEFINED outcome, and invariant 1 forbids that) or
        // an unbounded string. Sanitized and clamped here rather than trusted, and a request that does not
        // survive that returns false so the caller can flatten instead.
        var instruction = ComposeDocumentRenderer.SanitizeText(field.Instruction);
        if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > MaxFieldInstructionChars)
        {
            return false;
        }

        var deleted = run.Revision?.Kind == ComposeRevisionKind.Deleted;

        // The result run carries the field's own character formatting (marker-run contract: properties
        // still apply). Built through BuildRun so bold/italic/underline and the delText rule stay in ONE
        // place rather than being restated here.
        var resultRun = field.CachedResult.Length > 0
            ? BuildRun(new ComposeInlineRun
            {
                Text = field.CachedResult,
                Bold = run.Bold,
                Italic = run.Italic,
                Underline = run.Underline,
            }, state, deleted)
            : null;

        OpenXmlElement fieldElement;

        if (field.Complex)
        {
            // begin / instrText / separate / result / end. A run sequence is valid anywhere a run is, so
            // the revision wrapper (when present) simply contains all of them.
            var begin = new Run(new FieldChar { FieldCharType = FieldCharValues.Begin });
            ApplyFieldFlags(begin.GetFirstChild<FieldChar>()!, field);

            OpenXmlElement instructionRun = deleted
                ? new Run(new DeletedFieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve })
                : new Run(new FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve });

            var parts = new List<OpenXmlElement> { begin, instructionRun };
            if (resultRun is not null)
            {
                parts.Add(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
                parts.Add(resultRun);
            }
            parts.Add(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

            AppendFieldParts(paragraph, parts, run, state);
            return true;
        }
        else
        {
            var simple = new SimpleField { Instruction = instruction };
            ApplyFieldFlags(simple, field);

            if (resultRun is not null)
            {
                // Word's own nesting for a revised simple field: w:fldSimple outside, w:ins/w:del inside.
                if (run.Revision is { } simpleRevision)
                {
                    var wrapper = NewRevisionWrapper(simpleRevision, state);
                    wrapper.AppendChild(resultRun);
                    simple.AppendChild(wrapper);
                }
                else
                {
                    simple.AppendChild(resultRun);
                }
            }

            fieldElement = simple;
        }

        if (!string.IsNullOrWhiteSpace(run.Href))
        {
            paragraph.AppendChild(new Hyperlink(fieldElement) { Id = ComposeDocumentRenderer.HyperlinkPendingIdPrefix + run.Href!.Trim() });
            return true;
        }

        paragraph.AppendChild(fieldElement);
        return true;
    }

    /// <summary>
    /// Appends a field authored as a bare RUN SEQUENCE onto <paramref name="paragraph"/>, in the revision /
    /// hyperlink nesting Word itself writes: <c>w:hyperlink</c> OUTSIDE, <c>w:ins</c>/<c>w:del</c> INSIDE
    /// (CT_RunTrackChange does not admit <c>w:hyperlink</c>, and the reverse nesting risks Word's repair
    /// prompt). No element may hold a bare run sequence, so with neither context the parts go straight onto
    /// the paragraph.
    /// </summary>
    /// <remarks>
    /// Extracted at task 058 from the complex-form branch of <see cref="AppendField"/>, unchanged, so the
    /// re-authored sequence and the VERBATIM-carried nested span land in the document the same way. Two
    /// copies of this nesting would be two chances to get the schema wrong, in a file whose one job is to
    /// author packages Word does not report as damaged.
    /// </remarks>
    internal static void AppendFieldParts(
        Paragraph paragraph, IReadOnlyList<OpenXmlElement> parts, ComposeInlineRun run, ComposeDocumentRenderer.ListRenderState state)
    {
        if (run.Revision is { } revision)
        {
            var wrapper = NewRevisionWrapper(revision, state);
            foreach (var part in parts) wrapper.AppendChild(part);
            paragraph.AppendChild(string.IsNullOrWhiteSpace(run.Href)
                ? wrapper
                : new Hyperlink(wrapper) { Id = ComposeDocumentRenderer.HyperlinkPendingIdPrefix + run.Href!.Trim() });
            return;
        }

        if (!string.IsNullOrWhiteSpace(run.Href))
        {
            // A hyperlink admits EG_PContent, so it can hold the whole sequence.
            var link = new Hyperlink { Id = ComposeDocumentRenderer.HyperlinkPendingIdPrefix + run.Href!.Trim() };
            foreach (var part in parts) link.AppendChild(part);
            paragraph.AppendChild(link);
            return;
        }

        foreach (var part in parts) paragraph.AppendChild(part);
    }

    /// <summary>
    /// Task 058 (FR-A10 residual): parses a carried NESTED field span and returns its elements ONLY if it is
    /// safe to author into this carrier. Returns <c>null</c> when it is not, and the caller then falls
    /// through to the flatten.
    /// </summary>
    /// <remarks>
    /// <para><b>Three gates, and the third one is specific to this carry.</b></para>
    /// <list type="number">
    /// <item><description>The shared opaque-carry gate (<see cref="ComposeDocumentRenderer.TryParseOpaqueCarry{T}"/>) — typed SDK
    /// parse plus schema validation plus the size cap, the same one <c>w:pPrChange</c> has used since task
    /// 025 and the embedded-object carry since 056. Client XML never reaches the package
    /// unparsed.</description></item>
    /// <item><description><b>Relationship resolution</b> (<see cref="CarriedObjectRelationshipsResolve"/>) —
    /// a field's RESULT can contain anything a run can, an <c>INCLUDEPICTURE</c> result included, so a span
    /// can name a relationship this package does not have. Authoring that produces a file Word reports as
    /// DAMAGED: strictly worse than the honest flatten it would replace.</description></item>
    /// <item><description><b>It must BE a nested field</b> (<see cref="IsCarryableFieldSpan"/>). Every other
    /// carry in this file is gated by the SDK's own root-element check — a <c>w:drawing</c> payload can only
    /// parse as a <c>w:drawing</c>. This one's holder is a <c>w:p</c>, which admits any paragraph content at
    /// all, so without a structural check the property would be a general-purpose way to author arbitrary
    /// markup into a saved document. The check is what keeps <c>SpanXml</c> a field carry rather than an
    /// injection point, and it is asserted by a test that posts prose through it.</description></item>
    /// </list>
    /// </remarks>
    internal static List<OpenXmlElement>? TryBuildCarriedFieldSpan(string spanXml, ComposeDocumentRenderer.ListRenderState state)
    {
        var holder = ComposeDocumentRenderer.TryParseOpaqueCarry<Paragraph>(spanXml);
        if (holder is null)
        {
            return null;
        }

        var children = holder.ChildElements.ToList();
        if (!IsCarryableFieldSpan(children))
        {
            return null;
        }

        return CarriedObjectRelationshipsResolve(holder, state.CarrierRelationshipIds)
            ? children.Select(c => c.CloneNode(true)).ToList()
            : null;
    }

    /// <summary>
    /// Whether <paramref name="children"/> are exactly one NESTED Word field and nothing else — either a
    /// single <c>w:fldSimple</c> containing a field, or a <c>w:fldChar</c> run sequence that opens on its
    /// first run, closes on its last, holds nothing outside itself, and nests at least once.
    /// </summary>
    /// <remarks>
    /// The nesting requirement is not decoration. A span that does NOT nest is a field the instruction carry
    /// already handles, and admitting it here would give one construct two authoring paths that could drift
    /// apart. Requiring depth &gt; 1 keeps <c>SpanXml</c> scoped to the one class it exists for.
    /// </remarks>
    internal static bool IsCarryableFieldSpan(IReadOnlyList<OpenXmlElement> children)
    {
        if (children.Count == 0)
        {
            return false;
        }

        if (children.Count == 1 && children[0] is SimpleField simple)
        {
            return simple.Descendants<SimpleField>().Any() || simple.Descendants<FieldChar>().Any();
        }

        var depth = 0;
        var maxDepth = 0;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not Run run)
            {
                return false;
            }

            var type = run.GetFirstChild<FieldChar>()?.FieldCharType?.Value;
            if (type == FieldCharValues.Begin)
            {
                depth++;
                if (depth > maxDepth) maxDepth = depth;
            }
            else if (type == FieldCharValues.End)
            {
                if (depth == 0) return false;
                depth--;
                // Closing the outermost field anywhere but on the LAST element would leave content trailing
                // outside the field — content the projection never captured as part of it.
                if (depth == 0 && i != children.Count - 1) return false;
            }
            else if (depth == 0)
            {
                return false; // anything at all outside the field: prose, a separate with no begin, markup
            }
        }

        return depth == 0 && maxDepth > 1;
    }

    /// <summary>
    /// Copies <c>w:fldLock</c> / <c>w:dirty</c> onto the emitted field. <c>fldLock</c> is the one attribute
    /// this carry MUST not drop: the author set it so the field never updates, and re-authoring it without
    /// the lock would silently convert a deliberately frozen field into a live one — the single way carrying
    /// a field could be worse than flattening it.
    /// </summary>
    internal static void ApplyFieldFlags(SimpleField element, ComposeField field)
    {
        if (field.Locked) element.FieldLock = true;
        if (field.Dirty) element.Dirty = true;
    }

    /// <inheritdoc cref="ApplyFieldFlags(SimpleField, ComposeField)"/>
    internal static void ApplyFieldFlags(FieldChar element, ComposeField field)
    {
        if (field.Locked) element.FieldLock = true;
        if (field.Dirty) element.Dirty = true;
    }

    /// <summary>Task 012: revision-author resolution — a fact that carries an author keeps it
    /// (imported revisions round-trip their true authors); an EMPTY author falls back to the save-time
    /// authenticated author (<see cref="ComposeDocumentRenderer.ListRenderState.DefaultRevisionAuthor"/> — the client mapper
    /// omits the author on user-edit revisions), then to the sanitizer's "Unknown" floor.</summary>
    internal static string ResolveRevisionAuthorValue(string? factAuthor, ComposeDocumentRenderer.ListRenderState state)
    {
        // Sanitize FIRST, then decide: a control-chars-only author (hostile client input) must take the
        // fallback exactly like an absent one — checking IsNullOrWhiteSpace on the RAW value would let
        // it bypass the fallback and land on the "Unknown" floor instead of the saving user.
        var sanitized = ComposeDocumentRenderer.SanitizeText(factAuthor ?? string.Empty).Trim();
        return ComposeDocumentRenderer.SanitizeRevisionAuthor(sanitized.Length > 0 ? sanitized : state.DefaultRevisionAuthor);
    }

    internal static OpenXmlElement NewRevisionWrapper(ComposeRevision revision, ComposeDocumentRenderer.ListRenderState state)
    {
        var id = state.NextRevisionId().ToString(CultureInfo.InvariantCulture);
        var author = ResolveRevisionAuthorValue(revision.Author, state);
        var date = ComposeDocumentRenderer.TryValidRevisionDate(revision.Date);
        return revision.Kind == ComposeRevisionKind.Inserted
            ? new InsertedRun { Id = id, Author = author, Date = date }
            : new DeletedRun { Id = id, Author = author, Date = date };
    }

    internal static OpenXmlElement BuildRun(ComposeInlineRun run, ComposeDocumentRenderer.ListRenderState state, bool deleted = false)
    {
        // Task 023: a page-break run IS the break — every other field is ignored by contract
        // (ComposeInlineRun.IsPageBreak). Same markup AppendSection's page-broken section uses.
        // (Inside a w:ins/w:del wrapper the bare break run is schema-legal — no delText involved.)
        if (run.IsPageBreak)
        {
            return new Run(new Break { Type = BreakValues.Page });
        }

        // Task 046: the SOFT break marker — a bare <w:br/>, the same marker-run contract as the page break
        // above. A soft break carries no type attribute; emitting one WITH a type would silently promote a
        // line break into a page break.
        if (run.IsLineBreak)
        {
            return new Run(new Break());
        }

        var element = new Run();
        // Task 025: a tracked run-formatting change (w:rPrChange) forces an rPr even on an unmarked run —
        // the change record lives inside it (LAST in CT_RPr order). A record whose opaque carry fails the
        // parse gate drops — counted on the render degradation sink (task 026).
        var formatChange = run.FormatChange is { } change
            ? (Change: change, Previous: ComposeDocumentRenderer.TryParseOpaqueCarry<PreviousRunProperties>(change.PreviousPropertiesXml))
            : ((ComposeFormatChange Change, PreviousRunProperties? Previous)?)null;
        if (formatChange is { Previous: null })
        {
            state.Warn("tracked-format-change-dropped");
        }
        if (run.Bold || run.Italic || run.Underline || formatChange?.Previous is not null)
        {
            var rPr = new RunProperties();
            if (run.Bold) rPr.AppendChild(new Bold());
            if (run.Italic) rPr.AppendChild(new Italic());
            if (run.Underline) rPr.AppendChild(new Underline { Val = UnderlineValues.Single });
            if (formatChange is { Previous: { } previousRPr } fc)
            {
                // Same drop-on-parse-failure posture as pPrChange: schema requires the previous-rPr
                // child, so an invalid opaque carry drops the whole record (formatting stands as-is).
                rPr.AppendChild(new RunPropertiesChange(previousRPr)
                {
                    Id = state.NextRevisionId().ToString(CultureInfo.InvariantCulture),
                    Author = ResolveRevisionAuthorValue(fc.Change.Author, state),
                    Date = ComposeDocumentRenderer.TryValidRevisionDate(fc.Change.Date),
                });
            }
            element.AppendChild(rPr);
        }

        // Pending-deleted content authors as w:delText (Word rejects w:t inside w:del).
        //
        // Task 041 investigated emitting `xml:space="preserve"` only when the text NEEDS it (leading or
        // trailing whitespace, or empty). The `p/r/t` difference class on five corpus documents is exactly
        // this attribute: the text was character-identical and only the attribute had been added.
        //
        // REVERTED — the conditional rule was measurably WORSE. Word emits `xml:space="preserve"` far more
        // liberally than "the text has edge whitespace", so matching that narrow rule made the renderer
        // disagree with the source on 15 of 18 documents instead of 5. Emitting it unconditionally is safe
        // (it only ever suppresses whitespace trimming) and agrees with the corpus more often.
        //
        // The residual `p/r/t` differences are therefore attribute-PRESENCE, not text loss — verified by
        // inspecting the fixtures. Recorded on the loss list so the class is not re-investigated as if it
        // were content.
        // Task 048: the tab and symbol markers swap the run's TEXT CHILD rather than returning early like the
        // two break markers above. The difference is deliberate: run properties are meaningless on a break but
        // load-bearing here — an underlined tab is the fill-in leader on a signature block, and a symbol run
        // carries bold/italic like any other. Returning early would have silently dropped both.
        //
        // Both are schema-legal inside a w:del wrapper as-is (only w:t must become w:delText), so `deleted`
        // needs no special case.
        // Task 056: an EMBEDDED-OBJECT marker run swaps the run's content child for the carried subtree —
        // the same "properties still apply, content is replaced" contract as IsTab/Symbol above, one level
        // bigger. A carry that does not survive BOTH gates yields a run with no content child at all, which
        // is exactly today's drop; it is deliberately NOT warned here, because ComposeBlockMerge's
        // base-vs-rendered count already reports it and task 045 established that a taxonomy which says a
        // thing twice is one users stop reading.
        if (run.EmbeddedObject is { } embedded)
        {
            var carried = TryBuildCarriedObject(embedded, state);
            if (carried is not null)
            {
                element.AppendChild(carried);
            }
            return WrapInPendingHyperlink(element, run);
        }

        OpenXmlElement textElement =
            run.IsTab ? new TabChar()
            : run.Symbol is { } symbol ? new SymbolChar { Font = symbol.Font, Char = symbol.CharCode }
            : deleted ? new DeletedText(ComposeDocumentRenderer.SanitizeText(run.Text)) { Space = SpaceProcessingModeValues.Preserve }
            : new Text(ComposeDocumentRenderer.SanitizeText(run.Text)) { Space = SpaceProcessingModeValues.Preserve };
        element.AppendChild(textElement);

        return WrapInPendingHyperlink(element, run);
    }

    /// <summary>
    /// G5: a run carrying an href renders as a clean <c>w:hyperlink</c> wrapping the run. The real external
    /// relationship id can only be minted once the MainDocumentPart is in scope, so the href is stashed on a
    /// sentinel <see cref="Hyperlink.Id"/> here; <see cref="ResolveHyperlinkRelationships"/> (called by both
    /// byte-authors after the body is built) swaps it for the true rId. Zero text-search — the wrap is by the
    /// model's own run.
    /// </summary>
    internal static OpenXmlElement WrapInPendingHyperlink(Run element, ComposeInlineRun run) =>
        string.IsNullOrWhiteSpace(run.Href)
            ? element
            : new Hyperlink(element) { Id = ComposeDocumentRenderer.HyperlinkPendingIdPrefix + run.Href!.Trim() };

    /// <summary>
    /// Task 056 (FR-A10 residual): parses a carried embedded object and returns it ONLY if it is safe to
    /// author into this carrier. Returns <c>null</c> when it is not, and the caller then emits a run with no
    /// content — today's drop, which <c>ComposeBlockMerge</c> reports as <c>complex-object-dropped</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Two gates, and the second one is the point of the task.</b></para>
    /// <list type="number">
    /// <item><description>The shared opaque-carry gate (<see cref="ComposeDocumentRenderer.TryParseOpaqueCarry{T}"/>) — the same one
    /// <c>w:pPrChange</c>/<c>w:rPrChange</c> have used since task 025. Client XML never reaches the package
    /// unparsed.</description></item>
    /// <item><description><b>Relationship resolution.</b> A <c>w:drawing</c> names its image by relationship
    /// id (<c>r:embed="rId7"</c>), resolved against the MAIN DOCUMENT PART — the part whose body this save
    /// replaces. A subtree that parses and validates PERFECTLY can still name a relationship this package
    /// does not have, and authoring that produces a file Word reports as DAMAGED: strictly worse than the
    /// honest drop it would replace, and exactly the silent-damage regression R8 exists to end. So every
    /// attribute in the relationships namespace must resolve against the carrier before the subtree is
    /// authored.</description></item>
    /// </list>
    /// <para><b>Measured, not reasoned.</b> The carrier's relationships DO survive the body swap: the SDK
    /// rewrites the main part's XML and never its <c>.rels</c>, so an id the source used still resolves in
    /// the saved package. That was verified by opening a saved package and resolving the reference, not by
    /// reading this file's own remarks — which called such parts "orphaned", a word that does not
    /// distinguish "present with its relationship" from "relationship pruned". Evidence:
    /// <c>projects/spaarkeai-compose-r8/notes/056-object-carry-decisions.md</c> §1;
    /// <c>ComposeObjectCarrySeamTests</c> asserts it continuously.</para>
    /// <para><b>Why the gate is keyed on the NAMESPACE, not on attribute names.</b> <c>r:id</c>,
    /// <c>r:embed</c>, <c>r:link</c>, <c>r:pict</c>, <c>r:dm</c>/<c>r:lo</c>/<c>r:qs</c>/<c>r:cs</c> are all
    /// relationship references, and the list grows with each DrawingML part type. An allow-list of names
    /// would silently stop guarding the first construct nobody thought of.</para>
    /// </remarks>
    internal static OpenXmlElement? TryBuildCarriedObject(ComposeEmbeddedObject embedded, ComposeDocumentRenderer.ListRenderState state)
    {
        var parsed = RootLocalName(embedded.Xml) switch
        {
            "drawing" => (OpenXmlElement?)ComposeDocumentRenderer.TryParseOpaqueCarry<Drawing>(embedded.Xml),
            "object" => ComposeDocumentRenderer.TryParseOpaqueCarry<EmbeddedObject>(embedded.Xml),
            "pict" => ComposeDocumentRenderer.TryParseOpaqueCarry<Picture>(embedded.Xml),
            _ => null,
        };

        if (parsed is null)
        {
            return null;
        }

        return CarriedObjectRelationshipsResolve(parsed, state.CarrierRelationshipIds) ? parsed : null;
    }

    /// <summary>
    /// Whether every relationship reference inside <paramref name="element"/> resolves against
    /// <paramref name="carrierRelationshipIds"/>. An object that references nothing (a shape with no image
    /// part) passes trivially — it has nothing to dangle.
    /// </summary>
    internal static bool CarriedObjectRelationshipsResolve(
        OpenXmlElement element, IReadOnlySet<string> carrierRelationshipIds)
    {
        foreach (var node in new[] { element }.Concat(element.Descendants()))
        {
            foreach (var attribute in node.GetAttributes())
            {
                if (!string.Equals(attribute.NamespaceUri, OoxmlRelationshipNamespace, StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.IsNullOrEmpty(attribute.Value))
                {
                    continue;
                }
                if (!carrierRelationshipIds.Contains(attribute.Value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The OOXML relationships namespace — every attribute in it names a package relationship.</summary>
    internal const string OoxmlRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Every relationship id <paramref name="mainPart"/> can resolve — internal part relationships, external
    /// ones, and hyperlinks. All three kinds are addressed by the same <c>r:*</c> attributes from the body,
    /// so all three belong in the set a carried object is checked against.
    /// </summary>
    internal static IReadOnlySet<string> CollectRelationshipIds(MainDocumentPart mainPart)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in mainPart.Parts)
        {
            if (pair.RelationshipId is { Length: > 0 } id) ids.Add(id);
        }
        foreach (var relationship in mainPart.ExternalRelationships)
        {
            if (relationship.Id is { Length: > 0 } id) ids.Add(id);
        }
        foreach (var relationship in mainPart.HyperlinkRelationships)
        {
            if (relationship.Id is { Length: > 0 } id) ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// The local name of an XML fragment's ROOT element (<c>"&lt;w:drawing …"</c> → <c>drawing</c>), read
    /// without parsing. Used only to choose which typed SDK class the opaque carry is parsed through; the
    /// typed ctor is what actually VALIDATES the name and namespace, so a wrong guess here fails the gate
    /// rather than admitting anything.
    /// </summary>
    internal static string RootLocalName(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return string.Empty;
        }

        var start = xml.IndexOf('<');
        if (start < 0 || start + 1 >= xml.Length)
        {
            return string.Empty;
        }

        var i = start + 1;
        var nameStart = i;
        while (i < xml.Length && xml[i] is not (' ' or '\t' or '\r' or '\n' or '>' or '/'))
        {
            if (xml[i] == ':')
            {
                nameStart = i + 1;
            }
            i++;
        }

        return i > nameStart ? xml[nameStart..i] : string.Empty;
    }
}
