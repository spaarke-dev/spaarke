using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// The BASE side of Compose's save (ADR-049 R8 third amendment, task 040).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> R6 made every save render the whole body from
/// <see cref="ComposeContentModel"/> — which carries <c>w:jc</c>, <c>w:b</c> and <c>w:i</c> and essentially
/// nothing else. Everything else in <c>w:pPr</c>/<c>w:rPr</c> is discarded at PROJECTION time, before the
/// renderer runs, so a save that edited one paragraph of a 40-page contract rebuilt all 40 pages from a
/// five-node view. Measured on the 18-document corpus (task 023): <b>18.08%</b> of untouched blocks survived,
/// <b>6.67%</b> of the near tier.</para>
///
/// <para><b>What it does.</b> It supplies the base side the render path never had: the retained baseline's own
/// blocks, captured before the swap and re-projected server-side, so a block the user never touched is put
/// back <b>verbatim</b> instead of re-authored from a lossy model. There is deliberately <b>no per-construct
/// preservation logic</b> — properties survive because an untouched block is never re-derived. A feature list
/// of preserved constructs is never finished; cloning preserves constructs nobody enumerated.</para>
///
/// <para><b>Not a second body author</b> (ADR-049 I-5). This type never appends to <c>w:body</c>. It produces a
/// PLAN — a per-posted-block decision of clone-vs-render plus the base counterpart to inherit from — which
/// <see cref="ComposeDocumentRenderer"/> executes. One component writes body children, and it is the renderer.</para>
///
/// <para><b>Not DI-registered</b> (ADR-010). An internal collaborator of the renderer, all-static, no state.</para>
/// </remarks>
internal static class ComposeBlockMerge
{
    /// <summary>
    /// Cap on the LCS dynamic-programming table (cells = |posted| × |base|). Beyond it the alignment falls
    /// back to positional pairing and records <see cref="ComposeMergeStats.AlignmentDegraded"/> — a
    /// documented, counted degradation rather than an unbounded allocation on a pathological document.
    /// 4M cells ≈ 16 MB of <see cref="int"/>, reached at roughly 2,000 × 2,000 blocks; the largest corpus
    /// document is 109.
    /// </summary>
    private const int MaxAlignmentCells = 4_000_000;

    private static readonly JsonSerializerOptions BlockJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Captures the base side: the baseline's direct <c>w:body</c> children (cloned eagerly, because the
    /// caller is about to detach them) paired with a FRESH server-side re-projection of the same bytes.
    /// </summary>
    /// <remarks>
    /// <para><b>Fails open.</b> Returns <c>null</c> when the baseline cannot be re-projected, and the caller
    /// renders exactly as R6 does today. A save is <b>never refused</b> because the base side was unavailable
    /// (ADR-049 invariant 1 — every save terminates in a defined outcome).</para>
    /// </remarks>
    /// <param name="body">The carrier body, still populated. MUST be called before the children are detached.</param>
    /// <param name="carrierBytes">The same carrier bytes <paramref name="body"/> was opened from.</param>
    public static ComposeMergeBaseline? Capture(Body body, byte[] carrierBytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(carrierBytes);

        // DIRECT `w:body` children only. NEVER `body.Descendants<Paragraph>()` — that interleaves
        // `w:txbxContent` paragraphs into the body sequence and mis-pairs every block after the first text
        // box. `mc:AlternateContent`, `w:txbxContent`, `mc:Choice` and `mc:Fallback` are OPAQUE here: carried
        // whole by CloneNode(true), never entered.
        //
        // SectionProperties is excluded because the caller detaches and re-attaches the trailing sectPr
        // itself; including it would double it.
        var blocks = body.ChildElements
            .Where(e => e is not SectionProperties)
            .Select(e => e.CloneNode(true))
            .ToList();

        if (blocks.Count == 0)
        {
            return null;
        }

        IReadOnlyList<ComposeBlock>? model;
        try
        {
            // "Unchanged" is decided against a FRESH SERVER-SIDE RE-PROJECTION — never raw text, never the
            // client's copy of anything. The projection is the only coordinate system (invariant 3): base and
            // posted are then two values of the same type produced by the same builder, and their comparison
            // is total rather than a hand-maintained field list that silently stops covering a field later.
            var reprojected = new ComposeDocxProjectionBuilder().BuildContentModel(carrierBytes);
            model = reprojected.Status != ComposeProjectionStatus.Failed && reprojected.Model is not null
                ? reprojected.Model.Blocks
                : null;
        }
        catch (Exception)
        {
            // Fail open, mirroring ComposeBaselineParaIdStamper's stance.
            model = null;
        }

        return model is null ? null : new ComposeMergeBaseline(blocks, model);
    }

    /// <summary>
    /// Aligns the posted model against the re-projected baseline and returns one step per posted block.
    /// </summary>
    /// <remarks>
    /// <para><b>Alignment is a longest-common-subsequence over block equivalence</b>, not index-for-index
    /// pairing. Naive positional pairing collapses to zero preservation the moment a block is INSERTED or
    /// DELETED — every subsequent index shifts by one and nothing matches — and "the user added a paragraph"
    /// is the single most common edit there is. The task-030 prototype only ever measured single-paragraph
    /// edits, so it never met that case. LCS handles edit, insert and delete exactly.</para>
    ///
    /// <para>LCS pairs only blocks whose PROJECTED MODELS are equivalent. That is not the same as their OOXML
    /// being equivalent — the model carries <c>w:jc</c>, <c>w:b</c>, <c>w:i</c> and little else, and a text box
    /// is accept-flattened into prose — so two blocks can share a key while differing in everything the clone
    /// exists to preserve. <b>Which duplicate a match binds to therefore matters</b> (corrected in task 047b;
    /// this remark previously called a mis-pairing "harmless by construction", and
    /// <c>interior-text-boxes.docx</c> disproves it: the plan cloned base block 1 into output position 2 and
    /// stranded base block 2, so the document ended with the FIRST text box twice and the second one gone).
    /// The traceback tie-break in <c>Align</c> is what keeps the binding right.</para>
    ///
    /// <para>What LCS deliberately cannot do is recognise a MOVED block (matches never cross), so a reordered
    /// body degrades to R6's behaviour — never a failure, but no preservation. That limitation is recorded in
    /// the ADR.</para>
    ///
    /// <para><b>Unmatched blocks are then paired positionally within their gap</b> so that an edited block
    /// still receives its base counterpart for property inheritance (FR-A04). Without this an edited block
    /// would have no base to inherit from and would collapse to Normal — the exact user-visible symptom the
    /// amendment's residue section names. The counterpart is load-bearing for HONESTY as well as fidelity:
    /// <see cref="CarryUnmodeledConstructs"/> and its loss report both run off the base, so a block that fails
    /// to get one is a block whose losses go unreported (task 047b).</para>
    ///
    /// <para><b>An unpaired block is not a defect — an unpaired block WITH A STRANDED BASE is.</b> When the
    /// posted gap genuinely holds more blocks than the base gap, the surplus is content the user ADDED: it has
    /// no base because it never had one, it loses nothing, and it must stay quiet (a warning on every new
    /// paragraph is how a warning surface becomes ignorable). The failure this method must not produce is the
    /// other shape — a posted block left unpaired while a base block goes unused — because there the base
    /// existed and the alignment simply failed to find it.</para>
    ///
    /// <para><c>paraId</c> is NOT a key anywhere here (invariant 4). Duplicates are spec-legal across
    /// <c>mc:AlternateContent</c> and Word regenerates ids on save; keying on it mis-binds on precisely the
    /// documents this project exists to survive.</para>
    /// </remarks>
    public static IReadOnlyList<ComposeMergeStep> Plan(
        IReadOnlyList<ComposeBlock> posted,
        ComposeMergeBaseline baseline,
        ComposeMergeStats? stats = null)
    {
        ArgumentNullException.ThrowIfNull(posted);
        ArgumentNullException.ThrowIfNull(baseline);

        // A base MODEL block and a base ELEMENT are only a valid pair while the two sequences are
        // index-aligned. The projection emits one block per direct body child it can model; when the counts
        // disagree the correspondence is not established and cloning would splice the WRONG subtree, so the
        // merge stands down entirely rather than guess.
        var baseCount = Math.Min(baseline.Model.Count, baseline.Blocks.Count);
        if (baseline.Model.Count != baseline.Blocks.Count)
        {
            stats?.RecordBaselineUnaligned();
            return AllRendered(posted);
        }

        var postedKeys = new string[posted.Count];
        for (var i = 0; i < posted.Count; i++)
        {
            postedKeys[i] = CanonicalKey(posted[i], i, "P");
        }

        var baseKeys = new string[baseCount];
        for (var i = 0; i < baseCount; i++)
        {
            baseKeys[i] = CanonicalKey(baseline.Model[i], i, "B");
        }

        var matches = Align(postedKeys, baseKeys, stats);

        // Walk the matched pairs in order; positionally pair the unmatched blocks in each gap so an edited
        // block keeps a base counterpart to inherit from.
        var steps = new ComposeMergeStep[posted.Count];
        var postedCursor = 0;
        var baseCursor = 0;

        void FillGap(int postedEnd, int baseEnd)
        {
            var gapBase = baseCursor;
            while (postedCursor < postedEnd)
            {
                var counterpart = gapBase < baseEnd ? gapBase++ : -1;
                steps[postedCursor] = new ComposeMergeStep(ComposeMergeAction.Render, postedCursor, counterpart);
                stats?.RecordRendered(counterpart >= 0);
                postedCursor++;
            }
        }

        foreach (var (postedIndex, baseIndex) in matches)
        {
            FillGap(postedIndex, baseIndex);
            steps[postedIndex] = new ComposeMergeStep(ComposeMergeAction.Clone, postedIndex, baseIndex);
            stats?.RecordCloned();
            postedCursor = postedIndex + 1;
            baseCursor = baseIndex + 1;
        }

        FillGap(posted.Count, baseCount);
        return steps;
    }

    private static IReadOnlyList<ComposeMergeStep> AllRendered(IReadOnlyList<ComposeBlock> posted)
    {
        var steps = new ComposeMergeStep[posted.Count];
        for (var i = 0; i < posted.Count; i++)
        {
            steps[i] = new ComposeMergeStep(ComposeMergeAction.Render, i, -1);
        }

        return steps;
    }

    /// <summary>
    /// Longest common subsequence over the two key sequences, returned as ordered (posted, base) index pairs.
    /// Falls back to positional pairing of equal keys when the DP table would exceed
    /// <see cref="MaxAlignmentCells"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The traceback's tie-break is load-bearing</b> (task 047b). When a document contains two blocks
    /// that project IDENTICALLY — consecutive empty paragraphs, repeated signature lines, two text boxes whose
    /// interior prose is the same — the LCS is AMBIGUOUS: several maximum-length alignments exist and they are
    /// not equally good. The original tie-break advanced the POSTED cursor, which pairs a later posted block
    /// with an earlier base one and leaves the gap in front of it holding a posted block and NO base. That is
    /// not a cosmetic preference; see the tie branch below for what it costs.</para>
    /// </remarks>
    private static List<(int Posted, int Base)> Align(string[] posted, string[] baseKeys, ComposeMergeStats? stats)
    {
        var pairs = new List<(int, int)>();
        if (posted.Length == 0 || baseKeys.Length == 0)
        {
            return pairs;
        }

        if ((long)posted.Length * baseKeys.Length > MaxAlignmentCells)
        {
            // Counted degradation, never a silent cap (root §11 / "no silent caps"): pair positionally and
            // clone only where the two sequences already agree at the same index.
            stats?.RecordAlignmentDegraded();
            var limit = Math.Min(posted.Length, baseKeys.Length);
            for (var i = 0; i < limit; i++)
            {
                if (string.Equals(posted[i], baseKeys[i], StringComparison.Ordinal))
                {
                    pairs.Add((i, i));
                }
            }

            return pairs;
        }

        var n = posted.Length;
        var m = baseKeys.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = string.Equals(posted[i], baseKeys[j], StringComparison.Ordinal)
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var x = 0;
        var y = 0;

        // The two halves of the CURRENT gap: posted and base blocks skipped since the last match. `Plan`
        // pairs a gap's leftovers positionally, so what matters to an edited block is not WHICH base block
        // its gap holds but WHETHER its gap holds one at all.
        var gapPosted = 0;
        var gapBase = 0;

        while (x < n && y < m)
        {
            if (string.Equals(posted[x], baseKeys[y], StringComparison.Ordinal))
            {
                pairs.Add((x, y));
                x++;
                y++;
                gapPosted = 0;
                gapBase = 0;
                continue;
            }

            var skippingPosted = dp[x + 1, y];
            var skippingBase = dp[x, y + 1];

            // A TIE means the LCS is genuinely indifferent — both moves yield a maximum-length alignment —
            // so the choice is ours to make on other grounds, and the two are NOT equally good:
            //
            //   • skipping the BASE block leaves it in the gap, where FillGap hands it to the next unmatched
            //     posted block as its counterpart;
            //   • skipping the POSTED block puts a block into a gap that may have no base at all, and a
            //     posted block with no base counterpart inherits no properties, receives no carry, and —
            //     because WarnForConstructsLostOnThisBlock diffs the render AGAINST its base — reports no
            //     construct loss either. An edited paragraph then drops a text box, a footnote reference or a
            //     content control in complete SILENCE, which is the one outcome the residual-loss list
            //     (docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md) promises cannot happen.
            //
            // So on a tie, feed the gap a base block unless it already has spare ones. This is not a guess
            // about intent: it makes the traceback agree with the pairing rule FillGap already applies, where
            // an unmatched posted block sitting next to an unmatched base block IS the edit of it (FR-A04).
            // Measured on the 24-document corpus (294 single-block-edit scenarios) before the change: FIVE
            // posted blocks left unpaired, every one of them with a base block stranded — four of them in
            // AppligentNDA_Signed.docx, a real signed agreement, on consecutive empty paragraphs.
            //
            // The tie is the ONLY branch that changed. Where the DP has a strict preference it is still
            // obeyed exactly, so an alignment with no duplicate keys is unaffected.
            if (skippingPosted > skippingBase || (skippingPosted == skippingBase && gapBase > gapPosted))
            {
                x++;
                gapPosted++;
            }
            else
            {
                y++;
                gapBase++;
            }
        }

        return pairs;
    }

    /// <summary>
    /// The comparison key for one block: canonical JSON of the whole block with every <c>ParaId</c> stripped
    /// at any depth.
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately not a text comparison.</b> Two paragraphs with identical text can differ in
    /// formatting, list level, comment anchors or revision state; a text shortcut would clone a block the user
    /// DID change, silently discarding their edit — a worse failure than the one this mechanism exists to fix.
    /// The key is total over the model by construction rather than a hand-maintained field list.</para>
    ///
    /// <para><b>Why <c>ParaId</c> is stripped.</b> It is an identity hint, not content (invariant 4) — and the
    /// two sides do not agree on it. The HTML projection the client edits emits <c>data-paraid</c> from the
    /// projection's identity map, which MINTS an id for a paragraph the file left unstamped, while
    /// <see cref="ComposeDocxProjectionBuilder"/>'s content model reports that same paragraph's
    /// <c>ParaId</c> as <c>null</c> (it reads the file attribute). Comparing paraIds would therefore mark
    /// every unstamped paragraph as changed the moment the model arrives over the wire — the merge would
    /// measure 100% at the renderer and near 0% in production. No user action can change a paraId alone, so
    /// stripping it cannot mask a real edit.</para>
    ///
    /// <para><b>Fails closed.</b> A block that cannot be serialized gets a unique per-index key, so it can
    /// never match anything and is re-rendered. The cost is losing that block's formatting (today's
    /// behaviour); the cost of failing open would be silently discarding a real edit.</para>
    /// </remarks>
    private static string CanonicalKey(ComposeBlock block, int index, string side)
    {
        try
        {
            var node = JsonSerializer.SerializeToNode(block, BlockJson);
            if (node is null)
            {
                return $" {side}{index}";
            }

            StripParaIds(node);
            return node.ToJsonString(BlockJson);
        }
        catch (Exception)
        {
            return $" {side}{index}";
        }
    }

    private static void StripParaIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove(nameof(ComposeBlock.ParaId));
                foreach (var child in obj.ToList())
                {
                    StripParaIds(child.Value);
                }

                break;

            case JsonArray array:
                foreach (var element in array)
                {
                    StripParaIds(element);
                }

                break;
        }
    }

    /// <summary>
    /// FR-A04 — property inheritance for a RENDERED block that has a base counterpart.
    /// </summary>
    /// <remarks>
    /// <para>An edited block cannot be cloned: it is genuinely different, so it must come from the model. But
    /// the model carries almost no formatting, so rendering it alone collapses the paragraph to Normal — the
    /// user types one word and the paragraph loses its font, size, indentation, spacing and tabs. Inheritance
    /// narrows that to the properties the model actively contradicts.</para>
    ///
    /// <para><b>Additive only — the model always wins.</b> A base property is copied ONLY when the rendered
    /// paragraph has no element of that type. Anything the model expresses (alignment, numbering, heading
    /// style, run marks) is already on the rendered paragraph and is never overwritten.</para>
    ///
    /// <para><b>Three exclusions</b>, each because the model fully determines it and inheriting would fight
    /// the user's edit: <c>w:pStyle</c> and <c>w:numPr</c> (determined by the block's Kind/Level/NumId — a user
    /// who turned a heading into a paragraph must not get the heading style back), and <c>w:sectPr</c> (the
    /// renderer detaches and re-attaches the trailing section itself; inheriting it here would duplicate it).
    /// An interior <c>w:sectPr</c> on an UNTOUCHED paragraph survives through the clone path instead.</para>
    /// </remarks>
    public static void InheritProperties(OpenXmlElement rendered, OpenXmlElement baseElement)
    {
        if (rendered is not Paragraph renderedParagraph || baseElement is not Paragraph baseParagraph)
        {
            // Tables and opaque elements are inherited-into only by being cloned; there is no partial
            // inheritance for them, and inventing one would be per-construct logic.
            return;
        }

        InheritParagraphProperties(renderedParagraph, baseParagraph);
        InheritRunProperties(renderedParagraph, baseParagraph);
    }

    private static void InheritParagraphProperties(Paragraph rendered, Paragraph baseParagraph)
    {
        var basePr = baseParagraph.ParagraphProperties;
        if (basePr is null || !basePr.HasChildren)
        {
            return;
        }

        var renderedPr = rendered.ParagraphProperties;
        if (renderedPr is null)
        {
            renderedPr = new ParagraphProperties();
            rendered.InsertAt(renderedPr, 0);
        }

        var present = renderedPr.ChildElements.Select(e => e.GetType()).ToHashSet();

        foreach (var child in basePr.ChildElements)
        {
            if (child is ParagraphStyleId or NumberingProperties or SectionProperties)
            {
                continue;
            }

            if (!present.Add(child.GetType()))
            {
                continue;
            }

            // ParagraphMarkRunProperties (w:pPr/w:rPr) carries the paragraph-mark's own formatting and is
            // inherited like any other unmodeled property.
            InsertInSchemaOrder(renderedPr, child.CloneNode(true), ParagraphPropertyOrder);
        }
    }

    /// <summary>
    /// Inserts <paramref name="child"/> at its schema-mandated position among <paramref name="container"/>'s
    /// existing children.
    /// </summary>
    /// <remarks>
    /// <b>Appending is not correct here.</b> <c>CT_PPr</c> and <c>CT_RPr</c> are <c>xsd:sequence</c>, so child
    /// ORDER is part of the schema, not a formatting detail. An earlier draft of this inheritance appended,
    /// which produced <c>w:jc</c> before <c>w:spacing</c>/<c>w:ind</c> on any paragraph where the model had set
    /// alignment — schema-invalid output, and invisible to a fixture that never combines the two. The task-041
    /// edited-block measurement surfaced it as `spacing|jc`, `ind|spacing`, `jc|ind` on two corpus documents.
    ///
    /// <para>An element whose name is not in the order table is appended, which is the safe direction: an
    /// unknown extension element belongs after the known sequence rather than in the middle of it.</para>
    /// </remarks>
    private static void InsertInSchemaOrder(
        OpenXmlCompositeElement container, OpenXmlElement child, IReadOnlyList<string> order)
    {
        var rank = OrderRank(child, order);

        OpenXmlElement? predecessor = null;
        foreach (var existing in container.ChildElements)
        {
            if (OrderRank(existing, order) <= rank)
            {
                predecessor = existing;
                continue;
            }

            break;
        }

        if (predecessor is null)
        {
            container.InsertAt(child, 0);
        }
        else
        {
            container.InsertAfter(child, predecessor);
        }
    }

    private static int OrderRank(OpenXmlElement element, IReadOnlyList<string> order)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], element.LocalName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>ECMA-376 <c>CT_PPr</c> child sequence (§17.3.1.26).</summary>
    private static readonly string[] ParagraphPropertyOrder =
    {
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr",
        "suppressLineNumbers", "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap",
        "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc",
        "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId", "cnfStyle", "rPr",
        "sectPr", "pPrChange",
    };

    /// <summary>ECMA-376 <c>CT_RPr</c> child sequence (§17.3.2.28).</summary>
    private static readonly string[] RunPropertyOrder =
    {
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline",
        "shadow", "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing",
        "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd", "fitText",
        "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath", "rPrChange",
    };

    /// <summary>
    /// Applies the base paragraph's DOMINANT run properties — the <c>w:rPr</c> of the run holding the most
    /// characters — underneath each rendered run's own properties.
    /// </summary>
    /// <remarks>
    /// Dominant rather than first-run because a paragraph frequently opens with a short differently-formatted
    /// fragment (a run of bold lead-in, a footnote reference, a field result); taking the first run's
    /// properties would spread that fragment's formatting across the whole paragraph. Character-level
    /// re-association of properties to the runs they came from is out of scope here and belongs with the
    /// opaque-atom carry work.
    /// </remarks>
    private static void InheritRunProperties(Paragraph rendered, Paragraph baseParagraph)
    {
        var dominant = DominantRunProperties(baseParagraph);
        if (dominant is null)
        {
            return;
        }

        foreach (var run in rendered.Descendants<Run>().ToList())
        {
            var runPr = run.RunProperties;
            if (runPr is null)
            {
                runPr = new RunProperties();
                run.InsertAt(runPr, 0);
            }

            var present = runPr.ChildElements.Select(e => e.GetType()).ToHashSet();
            foreach (var child in dominant.ChildElements)
            {
                if (present.Add(child.GetType()))
                {
                    InsertInSchemaOrder(runPr, child.CloneNode(true), RunPropertyOrder);
                }
            }
        }
    }

    private static RunProperties? DominantRunProperties(Paragraph baseParagraph)
    {
        RunProperties? best = null;
        var bestLength = -1;

        // Direct runs only: a run inside an opaque region (text box, AlternateContent) is not this
        // paragraph's prose and must not donate its formatting to the whole block.
        foreach (var run in baseParagraph.Elements<Run>())
        {
            var length = run.Elements<Text>().Sum(t => t.Text?.Length ?? 0);
            if (length > bestLength)
            {
                bestLength = length;
                best = run.RunProperties;
            }
        }

        return best is { HasChildren: true } ? best : null;
    }


    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-A05 — CARRY, task 041.
    //
    // A block the user EDITED cannot be cloned: it is genuinely different, so it must come from the model.
    // Everything in the base block that the model cannot represent therefore disappears — and the task-041
    // corpus measurement says exactly what that costs: bookmarks on 2 documents, a block-level `w:sdt` on 1.
    //
    // The carry takes those constructs from the BASE BLOCK, not from a payload round-tripped through the
    // client. That is a deliberate departure from the task POML, which anticipated extending the client's
    // atom nodes to ferry verbatim XML. Base-carry is better here on four counts:
    //
    //   * The client never touches OOXML, so ADR-049 I-2 holds trivially rather than by discipline.
    //   * No wire growth and no opportunity for a client to mangle a payload it cannot interpret.
    //   * It is the SAME mechanism as FR-A04 property inheritance, extended from `w:pPr`/`w:rPr` to sibling
    //     constructs — one carry path, not two (root §11: extend before you add).
    //   * It works for constructs the editor renders INVISIBLY. A bookmark has no editor representation at
    //     all, so there is nothing for a client-side atom node to attach to in the first place.
    //
    // What base-carry cannot do is track a construct the user MOVED or DELETED. For bookmarks and content
    // controls that is the correct behaviour: neither is deletable through the editor, so re-instating them
    // is right, not presumptuous.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Restores constructs the content model cannot represent onto the elements just rendered for one block,
    /// taking them from that block's base counterpart. Returns the number of constructs carried.
    /// </summary>
    /// <param name="body">The body being authored. Elements may be REPLACED in place (the `w:sdt` case).</param>
    /// <param name="firstRenderedIndex">Index of the first child this block's render appended.</param>
    /// <param name="baseElement">The base counterpart for the block.</param>
    /// <param name="warn">Invoked with a degradation code when a construct cannot be carried.</param>
    public static int CarryUnmodeledConstructs(
        Body body, int firstRenderedIndex, OpenXmlElement baseElement, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(baseElement);

        if (firstRenderedIndex < 0)
        {
            return 0;
        }

        if (firstRenderedIndex >= body.ChildElements.Count)
        {
            // The block rendered NOTHING. `RenderBlocks` appends exactly one child for every block shape
            // except one: a `Table` block whose model carries no rows is skipped entirely. There is nothing
            // to carry ONTO — but there is still a report to make, and this method used to return here before
            // making it, so a block that vanished whole vanished in SILENCE. That is the same never-silent
            // hole task 047b closed on the alignment side, reached by a different route: found by auditing
            // the rest of this path rather than by a bug report, per the task's step 4.
            //
            // Nothing is invented by reporting it — the base's constructs are counted against an output of
            // nothing, so a base that held none still reports none.
            WarnForConstructsLostOnThisBlock(body, firstRenderedIndex, baseElement, warn);
            return 0;
        }

        var carried = 0;

        // ── 1. Bookmarks ────────────────────────────────────────────────────────────────────────
        //
        // The highest-value carry, and the least visible failure without it. A `w:bookmarkStart` is the
        // TARGET of every `REF` field in the document, so dropping one breaks cross-references ELSEWHERE:
        // the user edits paragraph 12 and a reference in paragraph 40 silently stops resolving. Nothing in
        // the edited paragraph looks wrong, which is why this went unnoticed until it was measured.
        var baseParagraph = FindCarrierParagraph(baseElement);
        var renderedParagraph = body.ChildElements[firstRenderedIndex] as Paragraph
            ?? FindCarrierParagraph(body.ChildElements[firstRenderedIndex]);

        if (baseParagraph is not null && renderedParagraph is not null)
        {
            carried += CarryBookmarks(baseParagraph, renderedParagraph);
        }

        // ── 1b. Embedded objects (task 056) ─────────────────────────────────────────────────────
        //
        // The object's own subtree ALSO round-trips through the content model
        // (`ComposeInlineRun.EmbeddedObject`), which is what preserves its exact position inside the
        // paragraph. This is the other half: the restore for a save whose posted model does NOT carry it.
        //
        // That is not a corner case — it is what a KEYSTROKE EDIT from the browser looks like today. The
        // editor shows an embedded object as an opaque atom, and the mapper contributes nothing for one, so
        // the object never reaches the posted model at all. Without this, the model carry would be a
        // producer with no consumer (task 049 shipped exactly that shape for fields and needed task 057 to
        // finish it).
        //
        // Base-carry is the RIGHT half to add rather than a client payload, for the four reasons in the
        // FR-A05 header above — and one more that is specific to this construct: an object references its
        // image by RELATIONSHIP id, and the base block's ids are the CARRIER's own, so a base-carried
        // object resolves by construction. A client round trip would put OOXML in the browser (ADR-049 I-2)
        // and hand back ids the server would then have to distrust.
        if (baseParagraph is not null && renderedParagraph is not null)
        {
            carried += CarryEmbeddedObjects(baseParagraph, renderedParagraph);
        }

        // ── 2. A block-level `w:sdt` shell ──────────────────────────────────────────────────────
        //
        // The projection emits an SDT's inner paragraph as an ordinary block, so editing it renders a bare
        // `w:p` and the content control — its alias, tag, id, binding and placeholder — is gone. Re-wrapping
        // the rendered paragraph in the BASE's own shell keeps the control intact while the prose inside it
        // is the user's new text.
        if (baseElement is SdtBlock baseSdt && body.ChildElements[firstRenderedIndex] is Paragraph rendered)
        {
            if (TryWrapInSdtShell(baseSdt, rendered, out var wrapped))
            {
                body.ReplaceChild(wrapped!, rendered);
                carried++;
            }
            else
            {
                // The shell could not be reconstructed — degrade to the bare paragraph rather than emit a
                // malformed control. Never a refusal (ADR-049 invariant 1).
                //
                // Task 045: the warning is NOT raised here any more. `sdt` joined ReportableConstructs, and
                // WarnForConstructsLostOnThisBlock runs AFTER this carry and counts the final state — so a
                // shell that was not reconstructed is already reported by the count (1 → 0), and a shell
                // that WAS reconstructed correctly reports nothing (1 → 1). Warning here too would double-
                // report the same loss, and a taxonomy that says a thing twice is one users stop reading.
            }
        }

        // ── 3. Report what could NOT be carried ─────────────────────────────────────────────────
        //
        // Task 044. Cloned blocks never reach this method, so nothing here can warn about a construct that
        // survived intact — which is the whole point: the accept-flatten taxonomy predates the merge and
        // warned about text boxes, fields and content controls on blocks that are now cloned verbatim.
        //
        // What remains is the honest half. A block the user EDITED is rebuilt from the model, and any
        // construct the model cannot represent and the carry above cannot restore is genuinely gone. Before
        // this, it went silently: the corpus produced ZERO render-path warnings while an edited paragraph
        // could still lose a field or a drawing. Suppressing false warnings is only safe if the true ones
        // start firing in the same change.
        WarnForConstructsLostOnThisBlock(body, firstRenderedIndex, baseElement, warn);

        return carried;
    }

    /// <summary>
    /// Families of construct that live inside a paragraph, cannot be expressed in
    /// <see cref="ComposeContentModel"/>, and are not restored by the carry above. Each maps to the
    /// degradation code R7's copy layer already understands, so no new warning surface is introduced
    /// (root §11).
    /// </summary>
    private static readonly (string LocalName, string Code)[] ReportableConstructs =
    {
        ("fldSimple", "field-flattened-to-text"),
        ("fldChar", "field-flattened-to-text"),
        ("drawing", "complex-object-dropped"),
        ("object", "complex-object-dropped"),
        ("pict", "complex-object-dropped"),
        ("footnoteReference", "unrepresented-footnote-reference"),
        ("endnoteReference", "unrepresented-endnote-reference"),
        // ("br", …) REMOVED in task 046 — a soft line break is no longer lost. It round-trips as the
        // ComposeInlineRun.IsLineBreak marker run, so there is nothing to report and reporting it would
        // be a false warning. The residual-loss parity test fails if this list and the renderer disagree.
        // ("sym", …) and ("tab", …) REMOVED in task 048 — neither is lost any more. A tab round-trips as the
        // ComposeInlineRun.IsTab marker run and a symbol as ComposeInlineRun.Symbol (font + code point
        // verbatim, not the resolved glyph), so reporting either would be a false warning. Same forcing
        // function as task 046's `br`: the residual-loss parity test fails if this list and the renderer
        // disagree in EITHER direction, so a regression here cannot pass silently.
        // Task 045: the INLINE `w:sdt` — a content control sitting inside a paragraph (a party name, an
        // effective date, a defined-term placeholder: the common shape in a legal template). The
        // block-level SdtBlock case was handled below since 041, but an inline control was dropped in
        // SILENCE, because it was not on this list and the shell carry only ever looks at SdtBlock. The
        // residual-loss parity check found it: `edited: 0/1 kept · codes: (none)`.
        //
        // Reuses the EXISTING `hard-tier-sdt-flattened` code rather than minting a second one (root §11):
        // the client copy already shipped for it — "A content control (form field, dropdown, or date
        // picker) was saved as plain text" — describes the inline case exactly, and one code for one
        // user-visible outcome is what keeps the taxonomy legible.
        ("sdt", "hard-tier-sdt-flattened"),
    };

    private static void WarnForConstructsLostOnThisBlock(
        Body body, int firstRenderedIndex, OpenXmlElement baseElement, Action<string>? warn)
    {
        if (warn is null)
        {
            return;
        }

        // NULL when the block rendered nothing at all (see the caller). "Nothing was written" is a perfectly
        // countable output — it is zero of everything — and treating it as a reason to SKIP the count is what
        // made a dropped block silent.
        var rendered = firstRenderedIndex < body.ChildElements.Count
            ? body.ChildElements[firstRenderedIndex]
            : null;

        foreach (var family in ReportableConstructs.GroupBy(c => c.Code))
        {
            var names = family.Select(f => f.LocalName).ToArray();
            var before = CountConstructs(baseElement, names);
            if (before == 0)
            {
                continue;
            }

            var after = rendered is null ? 0 : CountConstructs(rendered, names);
            for (var i = after; i < before; i++)
            {
                // One warning per LOST instance, not per family, so the count the banner shows is the
                // number of things actually gone rather than the number of kinds of thing.
                warn(family.Key);
            }
        }
    }

    /// <summary>
    /// Counts construct occurrences in a block, NOT descending into opaque regions.
    /// </summary>
    /// <remarks>
    /// A text box's interior is carried whole or not at all, so counting the fields inside one would report
    /// a loss on a construct that either survived entirely or was never separable in the first place.
    /// </remarks>
    private static int CountConstructs(OpenXmlElement block, string[] localNames)
    {
        var count = 0;
        foreach (var element in new[] { block }.Concat(block.Descendants()))
        {
            if (element is AlternateContent or TextBoxContent)
            {
                continue;
            }

            if (element.Ancestors().Any(a => a is AlternateContent or TextBoxContent))
            {
                continue;
            }

            if (localNames.Contains(element.LocalName, StringComparer.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The paragraph that carries a block's prose — the element itself when it is a <c>w:p</c>, else the
    /// first paragraph inside a content-control shell. Never descends into an opaque region.
    /// </summary>
    private static Paragraph? FindCarrierParagraph(OpenXmlElement element)
    {
        if (element is Paragraph paragraph)
        {
            return paragraph;
        }

        if (element is SdtBlock sdt)
        {
            return sdt.GetFirstChild<SdtContentBlock>()?.GetFirstChild<Paragraph>();
        }

        return null;
    }

    /// <summary>
    /// Copies the base paragraph's bookmark markers onto the rendered paragraph.
    /// </summary>
    /// <remarks>
    /// <para><b>The span is widened to the paragraph, deliberately.</b> A bookmark's original extent is
    /// defined by its position among runs whose text the user has just changed, so the exact character range
    /// no longer exists to restore. Starts are placed at the beginning of the content and ends at the end,
    /// which is EXACT for a bookmark spanning the whole paragraph (the shape every cross-reference target
    /// takes) and a widening for a partial one. Widening keeps the reference resolving; dropping it does
    /// not. Stating the trade here because a silently widened span is the kind of thing that should be
    /// found in a comment rather than in a document.</para>
    ///
    /// <para>A marker whose partner lives in a different paragraph is carried on its own — that is a
    /// multi-paragraph bookmark, and the other half rides along on its own (cloned) block.</para>
    /// </remarks>
    private static int CarryBookmarks(Paragraph baseParagraph, Paragraph rendered)
    {
        var starts = baseParagraph.Elements<BookmarkStart>().ToList();
        var ends = baseParagraph.Elements<BookmarkEnd>().ToList();
        if (starts.Count == 0 && ends.Count == 0)
        {
            return 0;
        }

        var existingIds = rendered.Descendants<BookmarkStart>().Select(b => b.Id?.Value)
            .Concat(rendered.Descendants<BookmarkEnd>().Select(b => b.Id?.Value))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        var carried = 0;

        // Insert starts in reverse so their relative order survives: each goes immediately after w:pPr.
        var anchor = rendered.ParagraphProperties as OpenXmlElement;
        for (var i = starts.Count - 1; i >= 0; i--)
        {
            if (existingIds.Contains(starts[i].Id?.Value ?? string.Empty))
            {
                continue;
            }

            var clone = starts[i].CloneNode(true);
            if (anchor is null)
            {
                rendered.InsertAt(clone, 0);
            }
            else
            {
                rendered.InsertAfter(clone, anchor);
            }

            carried++;
        }

        foreach (var end in ends)
        {
            if (existingIds.Contains(end.Id?.Value ?? string.Empty))
            {
                continue;
            }

            rendered.AppendChild(end.CloneNode(true));
            carried++;
        }

        return carried;
    }

    /// <summary>
    /// Task 056: restores embedded objects (<c>w:drawing</c> / <c>w:object</c> / <c>w:pict</c>) the base
    /// paragraph had and the rendered one does not, each inside its own run, at the base's own content
    /// ordinal. Returns the number restored.
    /// </summary>
    /// <remarks>
    /// <para><b>Restore-if-missing, so it composes with the model carry instead of competing with it.</b>
    /// Objects already present in the rendered paragraph — because the posted model carried them, which is
    /// what every server-side model round trip does — are matched by their own OuterXml and left alone. When
    /// the model carried everything, this method restores nothing and changes nothing. That also means the
    /// two paths can never double an object, which is the failure a naive "append the base's objects" would
    /// have shipped.</para>
    ///
    /// <para><b>Position is the base's content ordinal, clamped.</b> The exact character position no longer
    /// exists — the runs around it are the user's new text — so the object is placed at the same index among
    /// the paragraph's content children that it held in the base. For the shape this overwhelmingly takes in
    /// a legal document (an image, chart or embed alone in its own paragraph) that is EXACT. For an object
    /// mid-sentence in a heavily rewritten paragraph it is an approximation, and an approximate POSITION is
    /// a strictly smaller loss than the deletion it replaces. Stated here because a silently relocated image
    /// is the kind of thing that should be found in a comment rather than in a document.</para>
    ///
    /// <para><b>Objects inside an opaque region are not touched.</b> A text box's interior is carried whole
    /// or not at all, and a text-CARRYING box is accept-flattened to prose on the projection side — carrying
    /// the box as well would put the same words in the document twice. Only DIRECT run children of the
    /// paragraph are considered, which is exactly the set the projection would have dropped.</para>
    /// </remarks>
    private static int CarryEmbeddedObjects(Paragraph baseParagraph, Paragraph rendered)
    {
        var baseObjects = new List<(OpenXmlElement Element, int Ordinal)>();
        var ordinal = 0;
        foreach (var child in baseParagraph.ChildElements)
        {
            if (child is ParagraphProperties)
            {
                continue;
            }

            if (child is Run baseRun)
            {
                foreach (var element in baseRun.ChildElements)
                {
                    if (IsCarryableEmbeddedObject(element))
                    {
                        baseObjects.Add((element, ordinal));
                    }
                }
            }

            ordinal++;
        }

        if (baseObjects.Count == 0)
        {
            return 0;
        }

        // Identity for "is this object already here". NOT the subtree's bytes: an object the posted model
        // carried is re-parsed standalone by the renderer, and the SDK re-emits namespace declarations
        // differently once an element is no longer nested in its original scope — so byte equality reports
        // a MISS on the very object that is sitting right there, and the paragraph ends up with two of them.
        // (Found by ComposeObjectCarrySeamTests before this line existed, which is why it says so.)
        // Local name plus the relationship ids the subtree references is stable across that re-serialization
        // and is what actually distinguishes one embedded object from another. Counted rather than
        // set-matched, so a paragraph holding the same image twice restores the right NUMBER of them.
        var present = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var element in rendered.Descendants())
        {
            if (element is Drawing or EmbeddedObject or Picture)
            {
                var key = EmbeddedObjectIdentity(element);
                present[key] = present.TryGetValue(key, out var seen) ? seen + 1 : 1;
            }
        }

        var carried = 0;
        foreach (var (element, baseOrdinal) in baseObjects)
        {
            var key = EmbeddedObjectIdentity(element);
            if (present.TryGetValue(key, out var remaining) && remaining > 0)
            {
                present[key] = remaining - 1;
                continue;
            }

            var run = new Run(element.CloneNode(true));
            var contentStart = rendered.ParagraphProperties is null ? 0 : 1;
            var insertAt = Math.Min(contentStart + baseOrdinal, rendered.ChildElements.Count);
            rendered.InsertAt(run, insertAt);
            carried++;
        }

        return carried;
    }

    /// <summary>
    /// Whether an element is an embedded object this carry may restore — a <c>w:drawing</c>,
    /// <c>w:object</c> or <c>w:pict</c> that does NOT contain a text box.
    /// </summary>
    /// <remarks>
    /// The text-box exclusion is the whole reason this is a method rather than a type check. A box that
    /// carries text is accept-flattened into the paragraph as prose by the projection
    /// (<c>text-box-flattened</c>), so restoring the box on top of that would put the same words in the
    /// document TWICE — a "fix" that corrupts the sentence it was meant to protect. Structural (does the
    /// subtree contain a <c>w:txbxContent</c> / <c>v:textbox</c>?) rather than "does it currently have text
    /// in it", so an empty box behaves the same way as a full one and the two halves of the carry — this and
    /// the projection's <c>TryCarryEmbeddedObjects</c> — agree by construction rather than by coincidence.
    /// Shared by <see cref="ComposeDocxProjectionBuilder"/>, which applies the same rule on the model side.
    /// </remarks>
    internal static bool IsCarryableEmbeddedObject(OpenXmlElement element)
    {
        if (element is not (Drawing or EmbeddedObject or Picture))
        {
            return false;
        }

        foreach (var descendant in element.Descendants())
        {
            if (string.Equals(descendant.LocalName, "txbxContent", StringComparison.Ordinal)
                || string.Equals(descendant.LocalName, "textbox", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A re-serialization-stable identity for an embedded object: its local name plus every relationship id
    /// its subtree references, in document order. See <see cref="CarryEmbeddedObjects"/> for why OuterXml is
    /// not usable here.
    /// </summary>
    private static string EmbeddedObjectIdentity(OpenXmlElement element)
    {
        var ids = new List<string>();
        foreach (var node in new[] { element }.Concat(element.Descendants()))
        {
            foreach (var attribute in node.GetAttributes())
            {
                if (string.Equals(attribute.NamespaceUri, OoxmlRelationshipNamespace, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(attribute.Value))
                {
                    ids.Add(attribute.Value);
                }
            }
        }

        return element.LocalName + "|" + string.Join(",", ids);
    }

    /// <summary>The OOXML relationships namespace — every attribute in it names a package relationship.</summary>
    private const string OoxmlRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Rebuilds the base content control around the freshly rendered paragraph: the base <c>w:sdt</c> cloned
    /// whole, with the prose inside its <c>w:sdtContent</c> replaced by the rendered paragraph.
    /// </summary>
    /// <remarks>
    /// The shell — <c>w:sdtPr</c> with its alias, tag, id, placeholder and binding — is carried VERBATIM and
    /// never re-derived, so a control property nobody enumerated survives for the same reason cloning
    /// preserves formatting nobody enumerated. Returns false when the base has no <c>w:sdtContent</c> or the
    /// clone does not come back as an <c>SdtBlock</c>; the caller then warns rather than emitting a control
    /// that Word would reject.
    /// </remarks>
    private static bool TryWrapInSdtShell(SdtBlock baseSdt, Paragraph rendered, out OpenXmlElement? wrapped)
    {
        wrapped = null;

        if (baseSdt.CloneNode(true) is not SdtBlock shell)
        {
            return false;
        }

        var content = shell.GetFirstChild<SdtContentBlock>();
        if (content is null)
        {
            return false;
        }

        // The rendered paragraph replaces the shell's prose. Non-paragraph children (a nested table, say)
        // are left in place: this carry is scoped to the block the projection emitted as a paragraph, and
        // silently discarding a sibling would be a content loss dressed up as a fix.
        foreach (var paragraph in content.Elements<Paragraph>().ToList())
        {
            paragraph.Remove();
        }

        content.AppendChild(rendered.CloneNode(true));
        wrapped = shell;
        return true;
    }

    /// <summary>
    /// Mirrors <c>RenderBlocks</c>' ordered-list bookkeeping for a block that was CLONED rather than rendered.
    /// </summary>
    /// <remarks>
    /// Without this a rendered list item appearing after cloned list items computes its run continuity against
    /// a cursor that never saw them, and restarts numbering at 1 — task 030's limitation 3. A cloned item
    /// references the carrier's own <c>numId</c>, so a following model item carrying no NumId should continue
    /// THAT instance; recording it here is what makes that happen.
    /// </remarks>
    public static void ObserveClonedBlock(OpenXmlElement cloned, IDictionary<int, int> orderedRunByLevel)
    {
        ArgumentNullException.ThrowIfNull(cloned);
        ArgumentNullException.ThrowIfNull(orderedRunByLevel);

        var level = 0;
        if (cloned is Paragraph paragraph)
        {
            var numPr = paragraph.ParagraphProperties?.NumberingProperties;
            var numId = numPr?.NumberingId?.Val?.Value;
            if (numId is int resolvedNumId)
            {
                level = Math.Clamp(numPr?.NumberingLevelReference?.Val?.Value ?? 0, 0, 8);
                orderedRunByLevel[level] = resolvedNumId;
            }
        }

        // A non-list block closes NESTED runs but leaves the level-0 run continuable — the same contract
        // RenderBlocks applies, so continuity behaves identically whether a block was cloned or rendered.
        foreach (var key in orderedRunByLevel.Keys.Where(k => k > level).ToList())
        {
            orderedRunByLevel.Remove(key);
        }
    }
}

/// <summary>The base side of one save: the baseline's own body children plus their fresh re-projection.</summary>
/// <remarks>The two lists are index-aligned by construction; <see cref="ComposeBlockMerge.Plan"/> stands the
/// merge down entirely if they are not.</remarks>
internal sealed record ComposeMergeBaseline(
    IReadOnlyList<OpenXmlElement> Blocks,
    IReadOnlyList<ComposeBlock> Model);

internal enum ComposeMergeAction
{
    /// <summary>Clone the baseline's own subtree verbatim. Zero property logic on this path — that is the point.</summary>
    Clone,

    /// <summary>Render from the model, inheriting from <see cref="ComposeMergeStep.BaseIndex"/> when it is set.</summary>
    Render,
}

/// <summary>One posted block's disposition. <see cref="BaseIndex"/> is -1 when the block has no counterpart.</summary>
internal readonly record struct ComposeMergeStep(ComposeMergeAction Action, int PostedIndex, int BaseIndex);

/// <summary>
/// How the merge decided, per save. Consumed by the seam measurement; not wired to production telemetry.
/// </summary>
public sealed class ComposeMergeStats
{
    public int ClonedBlocks { get; private set; }
    public int RenderedBlocks { get; private set; }
    public int RenderedWithoutCounterpart { get; private set; }
    public bool BaselineUnavailable { get; private set; }
    public bool BaselineUnaligned { get; private set; }
    public bool AlignmentDegraded { get; private set; }

    public int TotalBlocks => ClonedBlocks + RenderedBlocks;

    public void RecordCloned() => ClonedBlocks++;

    public void RecordRendered(bool hadCounterpart)
    {
        RenderedBlocks++;
        if (!hadCounterpart)
        {
            RenderedWithoutCounterpart++;
        }
    }

    public void RecordBaselineUnavailable() => BaselineUnavailable = true;

    public void RecordBaselineUnaligned() => BaselineUnaligned = true;

    public void RecordAlignmentDegraded() => AlignmentDegraded = true;
}
