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
    /// <para>LCS pairs only blocks that are already EQUIVALENT, so a mis-pairing is harmless by construction:
    /// two equivalent blocks clone to the same output. What it deliberately cannot do is recognise a MOVED
    /// block (matches never cross), so a reordered body degrades to R6's behaviour — never a failure, but no
    /// preservation. That limitation is recorded in the ADR.</para>
    ///
    /// <para><b>Unmatched blocks are then paired positionally within their gap</b> so that an edited block
    /// still receives its base counterpart for property inheritance (FR-A04). Without this an edited block
    /// would have no base to inherit from and would collapse to Normal — the exact user-visible symptom the
    /// amendment's residue section names.</para>
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
        while (x < n && y < m)
        {
            if (string.Equals(posted[x], baseKeys[y], StringComparison.Ordinal))
            {
                pairs.Add((x, y));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
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
            renderedPr.AppendChild(child.CloneNode(true));
        }
    }

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
                    runPr.AppendChild(child.CloneNode(true));
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
